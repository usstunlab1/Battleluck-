import React, { useState, useEffect } from 'react';
import { SupabaseClient } from '@supabase/supabase-js';

interface ModEvent {
  id: number;
  event_type: string;
  mode_id: string | null;
  action: string | null;
  risk_level: string;
  status: string;
  payload: Record<string, unknown> | null;
  created_at: string;
}

interface AIResult {
  issues?: { severity: string; description: string; fix: string }[];
  summary?: string;
  highlights?: string[];
  risks?: string[];
}

const RISK_COLORS: Record<string, string> = {
  safe:        '#47FF8A',
  controlled:  '#FFD166',
  destructive: '#FF5C7A',
  critical:    '#C77DFF',
};

const STATUS_COLORS: Record<string, string> = {
  pending:  '#FFD166',
  approved: '#47FF8A',
  executed: '#5CC8FF',
  rejected: '#FF5C7A',
};

export function ModAIPanel({ supabase }: { supabase: SupabaseClient }) {
  const [events, setEvents] = useState<ModEvent[]>([]);
  const [aiResult, setAiResult] = useState<AIResult | null>(null);
  const [loading, setLoading] = useState(false);
  const [activeTab, setActiveTab] = useState<'events' | 'ai'>('events');

  useEffect(() => {
    supabase.from('mod_events').select('*').order('created_at', { ascending: false }).limit(100)
      .then(({ data }) => { if (data) setEvents(data); });

    const sub = supabase
      .channel('mod:battleluck:events', { config: { private: true } })
      .on('broadcast', { event: 'INSERT' }, ({ payload }) => {
        setEvents(prev => [payload as ModEvent, ...prev.slice(0, 99)]);
      })
      .on('broadcast', { event: 'UPDATE' }, ({ payload }) => {
        setEvents(prev => prev.map(e => e.id === (payload as ModEvent).id ? payload as ModEvent : e));
      })
      .subscribe();

    return () => { supabase.removeChannel(sub); };
  }, []);

  const updateStatus = async (id: number, status: string) => {
    await supabase.from('mod_events').update({ status }).eq('id', id);
    setEvents(prev => prev.map(e => e.id === id ? { ...e, status } : e));
  };

  const runAI = async (mode: 'analyze' | 'summarize_events') => {
    setLoading(true);
    setAiResult(null);
    const { data } = await supabase.functions.invoke('analyze-mod-logs', { body: { mode } });
    setAiResult(data);
    setLoading(false);
    setActiveTab('ai');
  };

  return (
    <div style={styles.panel}>
      <div style={styles.toolbar}>
        <span style={styles.title}>Events & AI</span>
        <button onClick={() => setActiveTab('events')} style={{ ...styles.chip, color: activeTab === 'events' ? '#66E3FF' : '#94a3b8', background: activeTab === 'events' ? '#334155' : '#1e293b' }}>Events</button>
        <button onClick={() => setActiveTab('ai')} style={{ ...styles.chip, color: activeTab === 'ai' ? '#66E3FF' : '#94a3b8', background: activeTab === 'ai' ? '#334155' : '#1e293b' }}>AI Analysis</button>
        <div style={{ marginLeft: 'auto', display: 'flex', gap: '0.5rem' }}>
          <button onClick={() => runAI('analyze')} disabled={loading} style={{ ...styles.chip, color: '#FF5C7A', borderColor: '#FF5C7A' }}>
            {loading ? '...' : '🔍 Analyze Errors'}
          </button>
          <button onClick={() => runAI('summarize_events')} disabled={loading} style={{ ...styles.chip, color: '#47FF8A', borderColor: '#47FF8A' }}>
            {loading ? '...' : '📊 Summarize Events'}
          </button>
        </div>
      </div>

      <div style={styles.body}>
        {activeTab === 'events' && (
          <div style={{ display: 'flex', flexDirection: 'column', gap: '0.5rem' }}>
            {events.length === 0 && <p style={{ color: '#64748b', padding: '1rem' }}>No events yet.</p>}
            {events.map(ev => (
              <div key={ev.id} style={styles.eventCard}>
                <div style={styles.eventHeader}>
                  <span style={{ color: '#7dd3fc', fontSize: '0.75rem' }}>{ev.event_type}</span>
                  {ev.mode_id && <span style={{ color: '#FFB347', fontSize: '0.75rem' }}>mode: {ev.mode_id}</span>}
                  {ev.action && <span style={{ color: '#e2e8f0', fontSize: '0.8rem', fontFamily: 'monospace' }}>{ev.action}</span>}
                  <span style={{ color: RISK_COLORS[ev.risk_level] ?? '#94a3b8', fontSize: '0.7rem', marginLeft: 'auto' }}>{ev.risk_level}</span>
                </div>
                <div style={styles.eventFooter}>
                  <span style={{ color: '#64748b', fontSize: '0.7rem' }}>{new Date(ev.created_at).toLocaleString()}</span>
                  <span style={{ color: STATUS_COLORS[ev.status] ?? '#94a3b8', fontSize: '0.75rem' }}>{ev.status}</span>
                  {ev.status === 'pending' && (
                    <div style={{ display: 'flex', gap: '0.4rem', marginLeft: 'auto' }}>
                      <button onClick={() => updateStatus(ev.id, 'approved')} style={{ ...styles.actionBtn, background: '#166534', color: '#47FF8A' }}>Approve</button>
                      <button onClick={() => updateStatus(ev.id, 'rejected')} style={{ ...styles.actionBtn, background: '#7f1d1d', color: '#FF5C7A' }}>Reject</button>
                    </div>
                  )}
                </div>
                {ev.payload && (
                  <pre style={styles.payload}>{JSON.stringify(ev.payload, null, 2)}</pre>
                )}
              </div>
            ))}
          </div>
        )}

        {activeTab === 'ai' && (
          <div style={{ padding: '1rem' }}>
            {loading && <p style={{ color: '#66E3FF' }}>Analyzing with AI...</p>}
            {!loading && !aiResult && <p style={{ color: '#64748b' }}>Click "Analyze Errors" or "Summarize Events" to run AI analysis.</p>}
            {aiResult && (
              <div style={{ display: 'flex', flexDirection: 'column', gap: '1rem' }}>
                {aiResult.summary && (
                  <div style={styles.aiCard}>
                    <div style={styles.aiCardTitle}>Summary</div>
                    <p style={{ color: '#e2e8f0', margin: 0 }}>{aiResult.summary}</p>
                  </div>
                )}
                {aiResult.issues?.map((issue, i) => (
                  <div key={i} style={{ ...styles.aiCard, borderLeft: `3px solid ${issue.severity === 'error' ? '#FF5C7A' : '#FFD166'}` }}>
                    <div style={{ display: 'flex', gap: '0.5rem', alignItems: 'center', marginBottom: '0.5rem' }}>
                      <span style={{ color: issue.severity === 'error' ? '#FF5C7A' : '#FFD166', fontSize: '0.75rem', textTransform: 'uppercase' }}>{issue.severity}</span>
                    </div>
                    <p style={{ color: '#e2e8f0', margin: '0 0 0.5rem 0', fontSize: '0.85rem' }}>{issue.description}</p>
                    <div style={styles.fixBox}>
                      <span style={{ color: '#47FF8A', fontSize: '0.7rem', fontWeight: 700 }}>FIX: </span>
                      <span style={{ color: '#a7f3d0', fontSize: '0.8rem' }}>{issue.fix}</span>
                    </div>
                  </div>
                ))}
                {aiResult.highlights?.map((h, i) => (
                  <div key={i} style={{ ...styles.aiCard, borderLeft: '3px solid #5CC8FF' }}>
                    <span style={{ color: '#7dd3fc', fontSize: '0.8rem' }}>• {h}</span>
                  </div>
                ))}
                {aiResult.risks?.map((r, i) => (
                  <div key={i} style={{ ...styles.aiCard, borderLeft: '3px solid #FFD166' }}>
                    <span style={{ color: '#FFD166', fontSize: '0.8rem' }}>⚠ {r}</span>
                  </div>
                ))}
              </div>
            )}
          </div>
        )}
      </div>
    </div>
  );
}

const styles: Record<string, React.CSSProperties> = {
  panel:       { display: 'flex', flexDirection: 'column', height: '100%', background: '#0f172a', color: '#e2e8f0' },
  toolbar:     { display: 'flex', alignItems: 'center', gap: '0.5rem', padding: '0.75rem 1rem', borderBottom: '1px solid #1e293b', flexWrap: 'wrap' },
  title:       { fontWeight: 700, color: '#66E3FF', marginRight: '0.5rem' },
  chip:        { padding: '0.25rem 0.6rem', border: '1px solid #334155', borderRadius: '4px', cursor: 'pointer', fontSize: '0.75rem', background: '#1e293b' },
  body:        { flex: 1, overflowY: 'auto', padding: '0.75rem' },
  eventCard:   { background: '#1e293b', borderRadius: '6px', padding: '0.75rem', border: '1px solid #334155' },
  eventHeader: { display: 'flex', gap: '0.75rem', alignItems: 'center', marginBottom: '0.4rem', flexWrap: 'wrap' },
  eventFooter: { display: 'flex', gap: '0.75rem', alignItems: 'center', flexWrap: 'wrap' },
  actionBtn:   { padding: '0.2rem 0.6rem', border: 'none', borderRadius: '4px', cursor: 'pointer', fontSize: '0.75rem', fontWeight: 700 },
  payload:     { background: '#0f172a', borderRadius: '4px', padding: '0.5rem', fontSize: '0.7rem', color: '#94a3b8', marginTop: '0.5rem', overflow: 'auto', maxHeight: '100px' },
  aiCard:      { background: '#1e293b', borderRadius: '6px', padding: '0.75rem', border: '1px solid #334155' },
  aiCardTitle: { color: '#66E3FF', fontWeight: 700, marginBottom: '0.5rem', fontSize: '0.85rem' },
  fixBox:      { background: '#0f172a', borderRadius: '4px', padding: '0.4rem 0.6rem', display: 'flex', gap: '0.4rem' },
};
