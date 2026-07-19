import { serve } from "https://deno.land/std@0.168.0/http/server.ts";
import { createClient } from "https://esm.sh/@supabase/supabase-js@2";

const OPENAI_API_KEY = Deno.env.get("OPENAI_API_KEY")!;
const SUPABASE_URL = Deno.env.get("SUPABASE_URL")!;
const SUPABASE_SERVICE_ROLE_KEY = Deno.env.get("SUPABASE_SERVICE_ROLE_KEY")!;

const supabase = createClient(SUPABASE_URL, SUPABASE_SERVICE_ROLE_KEY);

const corsHeaders = {
  "Access-Control-Allow-Origin": "*",
  "Access-Control-Allow-Headers": "authorization, x-client-info, apikey, content-type",
};

serve(async (req: Request) => {
  if (req.method === "OPTIONS") return new Response("ok", { headers: corsHeaders });

  try {
    const { mode = "analyze", limit = 50, event_id } = await req.json();

    if (mode === "analyze") {
      // Fetch recent error/warning logs
      const { data: logs } = await supabase
        .from("mod_logs")
        .select("level, source, message, created_at")
        .in("level", ["Error", "Warning"])
        .order("created_at", { ascending: false })
        .limit(limit);

      if (!logs?.length) {
        return new Response(JSON.stringify({ result: "No errors or warnings found." }), {
          headers: { ...corsHeaders, "Content-Type": "application/json" },
        });
      }

      const logText = logs.map(l =>
        `[${l.level}][${l.source}] ${l.message}`
      ).join("\n");

      const res = await fetch("https://api.openai.com/v1/chat/completions", {
        method: "POST",
        headers: { "Authorization": `Bearer ${OPENAI_API_KEY}`, "Content-Type": "application/json" },
        body: JSON.stringify({
          model: "gpt-4o-mini",
          messages: [
            {
              role: "system",
              content: "You are a BepInEx/BattleLuck mod debug assistant for a VRising dedicated server. Analyze logs and suggest fixes concisely.",
            },
            {
              role: "user",
              content: `Analyze these BattleLuck mod logs and identify issues with suggested fixes:\n\n${logText}\n\nRespond as JSON: {"issues": [{"severity": "error|warning", "description": "...", "fix": "..."}], "summary": "..."}`,
            },
          ],
          response_format: { type: "json_object" },
        }),
      });

      const data = await res.json();
      const result = JSON.parse(data.choices[0].message.content);
      return new Response(JSON.stringify(result), {
        headers: { ...corsHeaders, "Content-Type": "application/json" },
      });
    }

    if (mode === "summarize_events") {
      // Summarize recent mod events activity
      const { data: events } = await supabase
        .from("mod_events")
        .select("event_type, mode_id, action, risk_level, status, created_at")
        .order("created_at", { ascending: false })
        .limit(limit);

      if (!events?.length) {
        return new Response(JSON.stringify({ summary: "No events recorded yet." }), {
          headers: { ...corsHeaders, "Content-Type": "application/json" },
        });
      }

      const eventText = events.map(e =>
        `[${e.event_type}] mode=${e.mode_id ?? "none"} action=${e.action ?? "none"} risk=${e.risk_level} status=${e.status}`
      ).join("\n");

      const res = await fetch("https://api.openai.com/v1/chat/completions", {
        method: "POST",
        headers: { "Authorization": `Bearer ${OPENAI_API_KEY}`, "Content-Type": "application/json" },
        body: JSON.stringify({
          model: "gpt-4o-mini",
          messages: [
            { role: "system", content: "You are a BattleLuck mod event analyst for a VRising server." },
            {
              role: "user",
              content: `Summarize this BattleLuck event activity:\n\n${eventText}\n\nRespond as JSON: {"summary": "...", "highlights": ["..."], "risks": ["..."]}`,
            },
          ],
          response_format: { type: "json_object" },
        }),
      });

      const data = await res.json();
      const result = JSON.parse(data.choices[0].message.content);
      return new Response(JSON.stringify(result), {
        headers: { ...corsHeaders, "Content-Type": "application/json" },
      });
    }

    return new Response(JSON.stringify({ error: "Unknown mode. Use: analyze | summarize_events" }), {
      status: 400,
      headers: { ...corsHeaders, "Content-Type": "application/json" },
    });

  } catch (err) {
    return new Response(JSON.stringify({ error: err.message }), {
      status: 500,
      headers: { ...corsHeaders, "Content-Type": "application/json" },
    });
  }
});
