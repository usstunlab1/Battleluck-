import React, { useState, useEffect, useRef } from 'react';
import { SupabaseClient } from '@supabase/supabase-js';

interface ModLog {
  id: number;
  level: string;
  source: string;
  message: string;
  created_at: string;
}

const LEVEL_COLORS: Record<string, string> = {
  Error:   '#FF5C7A',
  Warning: '#FFD166',
  Info:    '#5CC8FF',
  Message: '#47FF8A',
};

export function ModLogViewer({ supabase }: { supabase: SupabaseClient }) {
  const [logs, setLogs] = useState<ModLog[]>([]);
  const [filter, setFilter] = useState<string>('All');
  const [paused, setPaused] = useState(false);
  const bottomRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    supabase.from('mod_logs').select('*').order('created_at', { ascending: false }).limit(200)
      .then(({ data }) => { if (data) setLogs(data.reverse()); });

    const sub = supabase
      .channel('mod:battleluck:logs', { config: { private: true } })
      .on('broadcast', { event: 'INSERT' }, ({ payload }) => {
        if (!paused) setLogs(prev => [...prev.slice(-499), payload as ModLog]);
      })
      .subscribe();

    return () => { supabase.removeChannel(sub); };
  }, []);

  useEffect(() => {
    if (!paused) bottomRef.current?.scrollIntoView({ behavior: 'smooth' });
  }, [logs, paused]);

  const filtered = filter === 'All' ? logs : logs.filter(l => l.level === filter);

  return (
    <div style={styles.panel}>
      <div style={styles.toolbar}>
        <span style={styles.title}>Live Logs</span>
        <div style={{ display: 'flex', gap: '0.5rem' }}>
          {['All', 'Error', 'Warning', 'Info', 'Message'].map(l => (
            <button key={l} onClick={() => setFilter(l)}
              style={{ ...styles.chip, background: filter === l ? '#334155' : '#1e293b', color: LEVEL_COLORS[l] ?? '#94a3b8' }}>
              {l}
            </button>
          ))}
        </div>
        <button onClick={() => setPaused(p => !p)} style={{ ...styles.chip, color: paused ? '#FFD166' : '#47FF8A' }}>
          {paused ? '▶ Resume' : '⏸ Pause'}
        </button>
        <button onClick={() => setLogs([])} style={{ ...styles.chip, color: '#FF5C7A' }}>Clear</button>
      </div>
      <div style={styles.logBox}>
        {filtered.map(log => (
          <div key={log.id} style={styles.logLine}>
            <span style={{ color: '#64748b', fontSize: '0.7rem', minWidth: '70px' }}>
              {new Date(log.created_at).toLocaleTimeString()}
            </span>
            <span style={{ color: LEVEL_COLORS[log.level] ?? '#94a3b8', minWidth: '60px', fontSize: '0.75rem' }}>
              {log.level}
            </span>
            <span style={{ color: '#7dd3fc', minWidth: '120px', fontSize: '0.75rem' }}>{log.source}</span>
            <span style={{ color: '#e2e8f0', fontSize: '0.8rem', flex: 1 }}>{log.message}</span>
          </div>
        ))}
        <div ref={bottomRef} />
      </div>
    </div>
  );
}

const styles: Record<string, React.CSSProperties> = {
  panel:   { display: 'flex', flexDirection: 'column', height: '100%', background: '#0f172a', color: '#e2e8f0' },
  toolbar: { display: 'flex', alignItems: 'center', gap: '0.5rem', padding: '0.75rem 1rem', borderBottom: '1px solid #1e293b', flexWrap: 'wrap' },
  title:   { fontWeight: 700, color: '#66E3FF', marginRight: '0.5rem' },
  chip:    { padding: '0.25rem 0.6rem', border: '1px solid #334155', borderRadius: '4px', cursor: 'pointer', fontSize: '0.75rem', background: '#1e293b' },
  logBox:  { flex: 1, overflowY: 'auto', padding: '0.5rem 1rem', fontFamily: 'monospace', display: 'flex', flexDirection: 'column', gap: '2px' },
  logLine: { display: 'flex', gap: '1rem', padding: '2px 0', borderBottom: '1px solid #1e293b22' },
};
