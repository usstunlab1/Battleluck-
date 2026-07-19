import React, { useState, useEffect, useRef } from 'react';
import { createClient } from '@supabase/supabase-js';
import { ThreadList } from './components/ThreadList';
import { MessageList } from './components/MessageList';
import { MessageInput } from './components/MessageInput';
import { ThreadSummary } from './components/ThreadSummary';
import './App.css';

const supabaseUrl = import.meta.env.VITE_SUPABASE_URL || '';
const supabaseAnonKey = import.meta.env.VITE_SUPABASE_ANON_KEY || '';

const supabase = createClient(supabaseUrl, supabaseAnonKey);

interface Thread {
  id: string;
  title: string;
  created_by: string;
  created_at: string;
}

interface Message {
  id: string;
  thread_id: string;
  sender_id: string;
  body: string;
  created_at: string;
}

interface ThreadSummary {
  thread_id: string;
  summary: string;
  key_points: string[];
  model: string;
  created_at: string;
}

function App() {
  const [threads, setThreads] = useState<Thread[]>([]);
  const [selectedThread, setSelectedThread] = useState<Thread | null>(null);
  const [messages, setMessages] = useState<Message[]>([]);
  const [summary, setSummary] = useState<ThreadSummary | null>(null);
  const [loading, setLoading] = useState(false);
  const [user, setUser] = useState<any>(null);

  useEffect(() => {
    supabase.auth.getSession().then(({ data: { session } }) => {
      setUser(session?.user ?? null);
    });

    supabase.auth.onAuthStateChange((_event, session) => {
      setUser(session?.user ?? null);
    });
  }, []);

  // Keep JWT fresh for private channel auth
  useEffect(() => {
    const { data: { subscription } } = supabase.auth.onAuthStateChange(async (_event, session) => {
      if (session) await supabase.realtime.setAuth(session.access_token);
    });
    return () => subscription.unsubscribe();
  }, []);

  useEffect(() => {
    if (!user) return;
    fetchThreads();

    // Subscribe to thread list updates
    const threadsSub = supabase
      .channel('threads')
      .on('broadcast', { event: 'INSERT' }, ({ payload }) => {
        setThreads((prev) => [payload as Thread, ...prev]);
      })
      .subscribe();

    return () => { supabase.removeChannel(threadsSub); };
  }, [user]);

  useEffect(() => {
    if (!selectedThread) return;
    fetchMessages(selectedThread.id);
    fetchSummary(selectedThread.id);

    const topic = `thread:${selectedThread.id}:messages`;
    const messagesSub = supabase
      .channel(topic, { config: { private: true } })
      .on('broadcast', { event: 'INSERT' }, ({ payload }) => {
        setMessages((prev) => [...prev, payload as Message]);
      })
      .on('broadcast', { event: 'DELETE' }, ({ payload }) => {
        setMessages((prev) => prev.filter((m) => m.id !== (payload as Message).id));
      })
      .subscribe();

    return () => { supabase.removeChannel(messagesSub); };
  }, [selectedThread]);

  const fetchThreads = async () => {
    const { data, error } = await supabase.from('threads').select('*').order('created_at', { ascending: false });
    if (data) setThreads(data);
  };

  const fetchMessages = async (threadId: string) => {
    const { data, error } = await supabase
      .from('messages')
      .select('*')
      .eq('thread_id', threadId)
      .order('created_at', { ascending: true });
    if (data) setMessages(data);
  };

  const fetchSummary = async (threadId: string) => {
    const { data, error } = await supabase
      .from('thread_summaries')
      .select('*')
      .eq('thread_id', threadId)
      .single();
    if (data) setSummary(data);
  };

  const createThread = async (title: string) => {
    if (!user) return;
    const { data, error } = await supabase
      .from('threads')
      .insert({ title, created_by: user.id })
      .select()
      .single();
    
    if (data) {
      // Add creator as member
      await supabase.from('thread_members').insert({
        thread_id: data.id,
        user_id: user.id,
        role: 'admin',
      });
      fetchThreads();
    }
  };

  const sendMessage = async (body: string) => {
    if (!selectedThread || !user) return;
    const { data, error } = await supabase
      .from('messages')
      .insert({
        thread_id: selectedThread.id,
        sender_id: user.id,
        body,
      })
      .select()
      .single();
    
    // realtime subscription will append the new message
  };

  const summarizeThread = async () => {
    if (!selectedThread) return;
    setLoading(true);
    
    const { data, error } = await supabase.functions.invoke('summarize-thread', {
      body: { thread_id: selectedThread.id },
    });
    
    if (data) {
      setSummary(data.summary);
    }
    setLoading(false);
  };

  if (!user) {
    return (
      <div className="auth-container">
        <h1>Messaging App</h1>
        <button onClick={() => supabase.auth.signInWithOAuth({ provider: 'github' })}>
          Sign in with GitHub
        </button>
      </div>
    );
  }

  return (
    <div className="app">
      <div className="sidebar">
        <h2>Threads</h2>
        <ThreadList threads={threads} onSelectThread={setSelectedThread} onCreateThread={createThread} />
      </div>
      <div className="main">
        {selectedThread ? (
          <>
            <div className="header">
              <h2>{selectedThread.title}</h2>
              <button onClick={summarizeThread} disabled={loading}>
                {loading ? 'Summarizing...' : 'Summarize Thread'}
              </button>
            </div>
            <MessageList messages={messages} />
            <MessageInput onSendMessage={sendMessage} />
            {summary && <ThreadSummary summary={summary} />}
          </>
        ) : (
          <div className="no-thread">Select a thread or create a new one</div>
        )}
      </div>
    </div>
  );
}

export default App;