import React, { useMemo, useState } from 'react';
import { Check, ChevronRight, Clock3, Compass, Crown, MapPin, Search, Shield, Sparkles, Swords } from 'lucide-react';
import { NpcQuest, QuestStatus } from '../types';
import { NPC_QUESTS, NPC_REGION_LABELS } from '../data/npcQuests';

const statusLabel: Record<QuestStatus, string> = {
  available: 'Available',
  active: 'In progress',
  completed: 'Completed',
};

export const NpcQuestBoard: React.FC = () => {
  const [quests, setQuests] = useState(NPC_QUESTS);
  const [selectedId, setSelectedId] = useState(NPC_QUESTS[0].id);
  const [status, setStatus] = useState<QuestStatus | 'all'>('all');
  const [query, setQuery] = useState('');

  const visible = useMemo(() => quests.filter(q => {
    const matchesStatus = status === 'all' || q.status === status;
    const haystack = `${q.title} ${q.npcName} ${q.description} ${NPC_REGION_LABELS[q.region]}`.toLowerCase();
    return matchesStatus && haystack.includes(query.trim().toLowerCase());
  }), [quests, query, status]);

  const selected = quests.find(q => q.id === selectedId) ?? visible[0] ?? quests[0];
  const progress = (quest: NpcQuest) => {
    const current = quest.objectives.reduce((sum, item) => sum + Math.min(item.current, item.target), 0);
    const target = quest.objectives.reduce((sum, item) => sum + item.target, 0);
    return target ? Math.round((current / target) * 100) : 0;
  };

  const setQuestStatus = (next: QuestStatus) => {
    setQuests(items => items.map(q => q.id === selected.id ? { ...q, status: next } : q));
  };

  return (
    <section className="h-full overflow-hidden bg-[#090909] text-zinc-100">
      <div className="h-full grid grid-cols-[minmax(320px,0.78fr)_minmax(440px,1.22fr)]">
        <div className="min-w-0 border-r border-white/10 flex flex-col bg-gradient-to-b from-zinc-950 to-black">
          <header className="px-6 pt-6 pb-4 border-b border-white/10">
            <div className="flex items-end justify-between gap-4 mb-5">
              <div>
                <p className="text-[10px] tracking-[0.3em] text-red-500 uppercase font-bold mb-1">Vardoran contracts</p>
                <h1 className="font-serif text-3xl font-black tracking-tight">NPC Quest Board</h1>
              </div>
              <div className="text-right">
                <div className="text-2xl font-black text-amber-400">{quests.filter(q => q.status === 'active').length}</div>
                <div className="text-[9px] uppercase tracking-widest text-zinc-500">Active</div>
              </div>
            </div>
            <div className="relative">
              <Search className="absolute left-3 top-2.5 w-4 h-4 text-zinc-500" />
              <input value={query} onChange={e => setQuery(e.target.value)} placeholder="Search quests, NPCs, regions…"
                className="w-full rounded-lg border border-white/10 bg-white/[0.04] py-2 pl-9 pr-3 text-sm outline-none focus:border-red-700 placeholder:text-zinc-600" />
            </div>
            <div className="flex gap-1 mt-3">
              {(['all', 'available', 'active', 'completed'] as const).map(item => (
                <button key={item} onClick={() => setStatus(item)}
                  className={`rounded-md px-2.5 py-1.5 text-[10px] uppercase tracking-wider font-bold transition ${status === item ? 'bg-red-700 text-white' : 'text-zinc-500 hover:bg-white/5 hover:text-zinc-200'}`}>
                  {item === 'all' ? 'All contracts' : statusLabel[item]}
                </button>
              ))}
            </div>
          </header>

          <div className="flex-1 overflow-y-auto p-3 space-y-2">
            {visible.map(quest => (
              <button key={quest.id} onClick={() => setSelectedId(quest.id)}
                className={`w-full text-left rounded-xl border p-4 transition-all ${selected.id === quest.id ? 'border-red-700/80 bg-red-950/30 shadow-[inset_3px_0_0_#dc2626]' : 'border-white/5 bg-white/[0.025] hover:border-white/15'}`}>
                <div className="flex items-start justify-between gap-3">
                  <div className="min-w-0">
                    <div className="flex items-center gap-2 mb-1.5">
                      <span className="text-[9px] uppercase tracking-wider font-black" style={{ color: quest.accent }}>{quest.difficulty}</span>
                      <span className="text-zinc-700">•</span>
                      <span className="text-[10px] text-zinc-500">Level {quest.level}</span>
                    </div>
                    <h2 className="font-serif text-lg font-bold truncate">{quest.title}</h2>
                    <p className="text-xs text-zinc-500 mt-1 truncate">{quest.npcName} · {NPC_REGION_LABELS[quest.region]}</p>
                  </div>
                  <ChevronRight className="w-4 h-4 text-zinc-600 mt-1 shrink-0" />
                </div>
                <div className="flex items-center gap-3 mt-4">
                  <div className="h-1 flex-1 rounded-full bg-zinc-800 overflow-hidden">
                    <div className="h-full rounded-full" style={{ width: `${progress(quest)}%`, backgroundColor: quest.accent }} />
                  </div>
                  <span className="text-[10px] font-mono text-zinc-400">{progress(quest)}%</span>
                  <span className={`text-[9px] uppercase font-black ${quest.status === 'completed' ? 'text-emerald-400' : quest.status === 'active' ? 'text-amber-400' : 'text-zinc-500'}`}>
                    {statusLabel[quest.status]}
                  </span>
                </div>
              </button>
            ))}
            {visible.length === 0 && <div className="py-16 text-center text-sm text-zinc-600">No contracts match this search.</div>}
          </div>
        </div>

        <article className="min-w-0 h-full overflow-y-auto relative">
          <div className="absolute inset-x-0 top-0 h-64 opacity-20 pointer-events-none" style={{ background: `radial-gradient(circle at 70% 0%, ${selected.accent}, transparent 62%)` }} />
          <div className="relative max-w-4xl mx-auto px-8 py-8">
            <div className="flex justify-between gap-8">
              <div>
                <div className="flex items-center gap-2 text-xs text-zinc-400 mb-4">
                  <MapPin className="w-4 h-4" style={{ color: selected.accent }} />
                  {NPC_REGION_LABELS[selected.region]}
                  {selected.expiresIn && <><span className="text-zinc-700">/</span><Clock3 className="w-3.5 h-3.5" /> Expires in {selected.expiresIn}</>}
                </div>
                <h2 className="font-serif text-4xl font-black leading-tight">{selected.title}</h2>
                <p className="mt-3 text-zinc-400 max-w-2xl leading-relaxed">{selected.description}</p>
              </div>
              <div className="shrink-0 w-20 h-20 rounded-2xl border border-white/10 bg-black/40 flex flex-col items-center justify-center shadow-xl">
                <Crown className="w-6 h-6 mb-1" style={{ color: selected.accent }} />
                <span className="text-2xl font-black">{selected.level}</span>
                <span className="text-[8px] tracking-widest uppercase text-zinc-500">Level</span>
              </div>
            </div>

            <div className="mt-8 grid grid-cols-[1fr_220px] gap-5">
              <div className="space-y-5">
                <section className="rounded-2xl border border-white/10 bg-white/[0.035] p-5">
                  <div className="flex items-center gap-3 mb-4">
                    <div className="w-10 h-10 rounded-full border border-white/10 bg-zinc-900 flex items-center justify-center"><Shield className="w-5 h-5" style={{ color: selected.accent }} /></div>
                    <div><h3 className="font-bold">{selected.npcName}</h3><p className="text-xs text-zinc-500">{selected.npcRole}</p></div>
                  </div>
                  <p className="font-serif italic text-zinc-300 leading-relaxed">“{selected.story}”</p>
                </section>

                <section className="rounded-2xl border border-white/10 bg-black/30 p-5">
                  <h3 className="text-xs uppercase tracking-[0.2em] font-black text-zinc-400 flex items-center gap-2 mb-4"><Swords className="w-4 h-4 text-red-500" /> Objectives</h3>
                  <div className="space-y-4">
                    {selected.objectives.map(objective => {
                      const done = objective.current >= objective.target;
                      const pct = Math.min(100, Math.round(objective.current / objective.target * 100));
                      return <div key={objective.id}>
                        <div className="flex justify-between text-sm mb-2"><span className={done ? 'text-zinc-500 line-through' : 'text-zinc-200'}>{objective.label}</span><span className="font-mono text-xs text-zinc-500">{objective.current}/{objective.target}</span></div>
                        <div className="h-1.5 bg-zinc-800 rounded-full overflow-hidden"><div className={`h-full rounded-full ${done ? 'bg-emerald-500' : 'bg-red-600'}`} style={{ width: `${pct}%` }} /></div>
                      </div>;
                    })}
                  </div>
                </section>
              </div>

              <div className="space-y-5">
                <section className="rounded-2xl border border-amber-900/40 bg-amber-950/10 p-4">
                  <h3 className="text-[10px] uppercase tracking-[0.2em] font-black text-amber-500 flex items-center gap-2 mb-4"><Sparkles className="w-4 h-4" /> Rewards</h3>
                  <div className="space-y-3">
                    {selected.rewards.map(reward => <div key={reward.name} className="flex items-center justify-between gap-2 text-xs"><span className="text-zinc-300">{reward.name}</span>{reward.amount && <span className="font-mono font-bold text-amber-400">×{reward.amount}</span>}</div>)}
                  </div>
                </section>
                <section className="rounded-2xl border border-white/10 bg-white/[0.025] p-4 text-xs">
                  <div className="flex justify-between mb-2"><span className="text-zinc-500">Difficulty</span><span className="font-bold" style={{ color: selected.accent }}>{selected.difficulty}</span></div>
                  <div className="flex justify-between"><span className="text-zinc-500">Total progress</span><span className="font-mono">{progress(selected)}%</span></div>
                </section>
              </div>
            </div>

            <div className="mt-6 flex items-center justify-end gap-3 border-t border-white/10 pt-5">
              {selected.status === 'available' && <button onClick={() => setQuestStatus('active')} className="rounded-lg bg-red-700 hover:bg-red-600 px-5 py-2.5 text-sm font-bold shadow-lg shadow-red-950/50 flex items-center gap-2"><Compass className="w-4 h-4" /> Accept quest</button>}
              {selected.status === 'active' && <button onClick={() => setQuestStatus('completed')} className="rounded-lg bg-emerald-700 hover:bg-emerald-600 px-5 py-2.5 text-sm font-bold flex items-center gap-2"><Check className="w-4 h-4" /> Mark complete</button>}
              {selected.status === 'completed' && <div className="text-emerald-400 text-sm font-bold flex items-center gap-2"><Check className="w-5 h-5" /> Contract completed</div>}
            </div>
          </div>
        </article>
      </div>
    </section>
  );
};
