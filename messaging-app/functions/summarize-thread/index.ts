import { serve } from "https://deno.land/std@0.168.0/http/server.ts";
import { createClient } from "https://esm.sh/@supabase/supabase-js@2";

const OPENAI_API_KEY = Deno.env.get("OPENAI_API_KEY")!;
const SUPABASE_URL = Deno.env.get("SUPABASE_URL")!;
const SUPABASE_SERVICE_ROLE_KEY = Deno.env.get("SUPABASE_SERVICE_ROLE_KEY")!;

const supabase = createClient(SUPABASE_URL, SUPABASE_SERVICE_ROLE_KEY);

const OPENAI_API_URL = "https://api.openai.com/v1/chat/completions";

serve(async (req: Request) => {
  // Handle CORS
  if (req.method === "OPTIONS") {
    return new Response("ok", {
      headers: {
        "Access-Control-Allow-Origin": "*",
        "Access-Control-Allow-Headers": "authorization, x-client-info, apikey, content-type",
      },
    });
  }

  try {
    const { thread_id, model = "gpt-4o-mini" } = await req.json();

    if (!thread_id) {
      return new Response(JSON.stringify({ error: "thread_id is required" }), {
        status: 400,
        headers: { "Content-Type": "application/json" },
      });
    }

    // Verify caller is a thread member using their JWT
    const authHeader = req.headers.get("Authorization");
    if (!authHeader) {
      return new Response(JSON.stringify({ error: "Unauthorized" }), {
        status: 401,
        headers: { "Content-Type": "application/json" },
      });
    }
    const callerClient = createClient(SUPABASE_URL, Deno.env.get("SUPABASE_ANON_KEY")!, {
      global: { headers: { Authorization: authHeader } },
    });
    const { data: membership, error: memberError } = await callerClient
      .from("thread_members")
      .select("user_id")
      .eq("thread_id", thread_id)
      .maybeSingle();
    if (memberError || !membership) {
      return new Response(JSON.stringify({ error: "Forbidden: not a thread member" }), {
        status: 403,
        headers: { "Content-Type": "application/json" },
      });
    }

    // Fetch messages for the thread
    const { data: messages, error: messagesError } = await supabase
      .from("messages")
      .select("body, created_at, sender_id")
      .eq("thread_id", thread_id)
      .order("created_at", { ascending: true });

    if (messagesError) {
      return new Response(JSON.stringify({ error: messagesError.message }), {
        status: 400,
        headers: { "Content-Type": "application/json" },
      });
    }

    if (!messages || messages.length === 0) {
      return new Response(JSON.stringify({ error: "No messages found in thread" }), {
        status: 404,
        headers: { "Content-Type": "application/json" },
      });
    }

    // Build conversation text
    const conversationText = messages
      .map((m: any) => `User: ${m.body}`)
      .join("\n");

    // Call OpenAI API
    const openaiResponse = await fetch(OPENAI_API_URL, {
      method: "POST",
      headers: {
        "Authorization": `Bearer ${OPENAI_API_KEY}`,
        "Content-Type": "application/json",
      },
      body: JSON.stringify({
        model,
        messages: [
          {
            role: "system",
            content: "You are a helpful assistant that summarizes conversation threads. Provide a concise summary and extract key points as a JSON array.",
          },
          {
            role: "user",
            content: `Summarize this conversation thread and extract key points:\n\n${conversationText}\n\nRespond with JSON in this format: {"summary": "brief summary", "key_points": ["point 1", "point 2"]}`,
          },
        ],
        response_format: { type: "json_object" },
      }),
    });

    if (!openaiResponse.ok) {
      const error = await openaiResponse.text();
      return new Response(JSON.stringify({ error: `OpenAI API error: ${error}` }), {
        status: 500,
        headers: { "Content-Type": "application/json" },
      });
    }

    const openaiData = await openaiResponse.json();
    const content = openaiData.choices[0].message.content;
    const result = JSON.parse(content);

    // Get the latest message ID
    const latestMessageId = messages[messages.length - 1].id;

    // Upsert the summary
    const { data: summary, error: upsertError } = await supabase
      .from("thread_summaries")
      .upsert({
        thread_id,
        latest_message_id: latestMessageId,
        summary: result.summary,
        key_points: result.key_points,
        model,
      })
      .select()
      .single();

    if (upsertError) {
      return new Response(JSON.stringify({ error: upsertError.message }), {
        status: 500,
        headers: { "Content-Type": "application/json" },
      });
    }

    return new Response(JSON.stringify({ success: true, summary }), {
      status: 200,
      headers: { "Content-Type": "application/json" },
    });
  } catch (error) {
    return new Response(JSON.stringify({ error: error.message }), {
      status: 500,
      headers: { "Content-Type": "application/json" },
    });
  }
});