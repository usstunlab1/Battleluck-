import React, { useState, useEffect } from 'react';
import { SupabaseClient } from '@supabase/supabase-js';

interface ModConfig {
  id: number;
  file_name: string;
  section: string | null;
  key: string;
  value: string;
  value_type: string;
  description: string | null;
  updated_at: string;
}

export function ModConfigEditor({ supabase }: { supabase: SupabaseClient }) {
  const [configs, setConfigs] = useState<ModConfig[]>([]);
  const [editing, setEditing] = useState<Record<number, string>>({});
  const [saving, setSaving] = useState<Record<number, boolean>>({});
  const [activeFile, setActiveFile] = useState<string>('All');

  useEffect(() => {
    supabase.from('mod_configs').select('*').order('file_name').order('section').order('key')
      .then(({ data }) => { if (data) setConfigs(data); });

    const sub = supabase
      .channel('mod:battleluck:configs', { config: { private: true } })
      .on('broadcast', { event: 'INSERT' }, ({ payload }) => {
        setConfigs(prev => [...prev, payload as ModConfig]);
      })
      .on('broadcast', { event: 'UPDATE' }, ({ payload }) => {
        setConfigs(prev => prev.map(c => c.id === (payload as ModConfig).id ? payload as ModConfig : c));
      })
      .subscribe();

    return () => { supabase.removeChannel(sub); };
  }, []);

  const files = ['All', ...Array.from(new Set(configs.map(c => c.file_name)))];
  const filtered = activeFile === 'All' ? configs : configs.filter(c => c.file_name === activeFile);
  const grouped = filtered.reduce<Record<string, ModConfig[]>>((acc, c) => {
    const key = `${c.file_name} › ${c.section ?? 'root'}`;
    (acc[key] ??= []).push(c);
    return acc;
  }, {});

  const save = async (cfg: ModConfig) => {
    const newVal = editing[cfg.id] ?? cfg.value;
    setSaving(s => ({ ...s, [cfg.id]: true }));
    await supabase.from('mod_configs').update({ value: newVal, updated_at: new Date().toISOString() }).eq('id', cfg.id);
    setConfigs(prev => prev.map(c => c.id === cfg.id ? { ...c, value: newVal } : c));
    setEditing(e => { const n = { ...e }; delete n[cfg.id]; return n; });
    setSaving(s => ({ ...s, [cfg.id]: false }));
  };

  return (
    <div style={styles.panel}>
      <div style={styles.toolbar}>
        <span style={styles.title}>Config Editor</span>
        {files.map(f => (
          <button key={f} onClick={() => setActiveFile(f)}
            style={{ ...styles.chip, background: activeFile === f ? '#334155' : '#1e293b', color: '#66E3FF' }}>
            {f}
          </button>
        ))}
      </div>
      <div style={styles.body}>
        {Object.entries(grouped).map(([group, items]) => (
          <div key={group} style={styles.group}>
            <div style={styles.groupHeader}>{group}</div>
            {items.map(cfg => (
              <div key={cfg.id} style={styles.row}>
                <div style={styles.keyCol}>
                  <span style={styles.key}>{cfg.key}</span>
                  {cfg.description && <span style={styles.desc}>{cfg.description}</span>}
                </div>
                <div style={styles.valCol}>
                  {cfg.value_type === 'boolean' ? (
                    <button onClick={() => {
                      const toggled = cfg.value === 'true' ? 'false' : 'true';
                      setEditing(e => ({ ...e, [cfg.id]: toggled }));
                      supabase.from('mod_configs').update({ value: toggled, updated_at: new Date().toISOString() }).eq('id', cfg.id)
                        .then(() => setConfigs(prev => prev.map(c => c.id === cfg.id ? { ...c, value: toggled } : c)));
                    }} style={{ ...styles.toggle, background: cfg.value === 'true' ? '#166534' : '#7f1d1d', color: cfg.value === 'true' ? '#47FF8A' : '#FF5C7A' }}>
                      {cfg.value === 'true' ? 'ON' : 'OFF'}
                    </button>
                  ) : (
                    <input
                      value={editing[cfg.id] ?? cfg.value}
                      onChange={e => setEditing(ed => ({ ...ed, [cfg.id]: e.target.value }))}
                      onKeyDown={e => e.key === 'Enter' && save(cfg)}
                      style={styles.input}
                    />
                  )}
                  {editing[cfg.id] !== undefined && cfg.value_type !== 'boolean' && (
                    <button onClick={() => save(cfg)} disabled={saving[cfg.id]} style={styles.saveBtn}>
                      {saving[cfg.id] ? '...' : 'Save'}
                    </button>
                  )}
                </div>
              </div>
            ))}
          </div>
        ))}
      </div>
    </div>
  );
}

const styles: Record<string, React.CSSProperties> = {
  panel:       { display: 'flex', flexDirection: 'column', height: '100%', background: '#0f172a', color: '#e2e8f0' },
  toolbar:     { display: 'flex', alignItems: 'center', gap: '0.5rem', padding: '0.75rem 1rem', borderBottom: '1px solid #1e293b', flexWrap: 'wrap' },
  title:       { fontWeight: 700, color: '#66E3FF', marginRight: '0.5rem' },
  chip:        { padding: '0.25rem 0.6rem', border: '1px solid #334155', borderRadius: '4px', cursor: 'pointer', fontSize: '0.75rem' },
  body:        { flex: 1, overflowY: 'auto', padding: '1rem' },
  group:       { marginBottom: '1.5rem' },
  groupHeader: { fontSize: '0.7rem', color: '#7dd3fc', textTransform: 'uppercase', letterSpacing: '0.05em', marginBottom: '0.5rem', borderBottom: '1px solid #1e293b', paddingBottom: '0.25rem' },
  row:         { display: 'flex', alignItems: 'flex-start', gap: '1rem', padding: '0.4rem 0', borderBottom: '1px solid #1e293b22' },
  keyCol:      { flex: 1, display: 'flex', flexDirection: 'column', gap: '2px' },
  key:         { fontSize: '0.85rem', color: '#e2e8f0', fontFamily: 'monospace' },
  desc:        { fontSize: '0.7rem', color: '#64748b' },
  valCol:      { display: 'flex', alignItems: 'center', gap: '0.5rem', minWidth: '220px' },
  input:       { background: '#1e293b', border: '1px solid #334155', borderRadius: '4px', color: '#e2e8f0', padding: '0.3rem 0.5rem', fontSize: '0.8rem', fontFamily: 'monospace', width: '160px' },
  saveBtn:     { padding: '0.25rem 0.6rem', background: '#1d4ed8', color: '#fff', border: 'none', borderRadius: '4px', cursor: 'pointer', fontSize: '0.75rem' },
  toggle:      { padding: '0.25rem 0.75rem', border: 'none', borderRadius: '4px', cursor: 'pointer', fontWeight: 700, fontSize: '0.8rem' },
};
