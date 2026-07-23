import React from 'react';
import { X, BookOpen, Shield, Flame, Compass, Heart, AlertTriangle, Crosshair, Sparkles } from 'lucide-react';

interface GuideModalProps {
  isOpen: boolean;
  onClose: () => void;
}

export const GuideModal: React.FC<GuideModalProps> = ({ isOpen, onClose }) => {
  if (!isOpen) return null;

  return (
    <div className="fixed inset-0 z-50 bg-black/80 backdrop-blur-md flex items-center justify-center p-4">
      <div className="bg-slate-950 border border-slate-800 rounded-2xl w-full max-w-2xl max-h-[85vh] overflow-hidden shadow-2xl text-slate-200 flex flex-col animate-in fade-in zoom-in-95 duration-200">
        {/* Header */}
        <div className="h-16 border-b border-slate-800 bg-slate-900/60 px-6 flex items-center justify-between shrink-0">
          <div className="flex items-center gap-3">
            <div className="w-8 h-8 rounded-lg bg-red-950/80 border border-red-700/60 flex items-center justify-center shadow-inner">
              <BookOpen className="w-4 h-4 text-red-400" />
            </div>
            <div>
              <h3 className="text-sm font-bold uppercase tracking-wider text-slate-100 font-mono">
                V Rising Territory & Base Architecture Manual
              </h3>
              <p className="text-[11px] text-slate-400">Essential strategy for Castle Hearts, Patrol Avoidance & Mining</p>
            </div>
          </div>
          <button 
            onClick={onClose}
            className="text-slate-500 hover:text-slate-200 p-1.5 rounded-lg hover:bg-slate-800 transition-colors"
          >
            <X className="w-5 h-5" />
          </button>
        </div>

        {/* Scrollable Content */}
        <div className="p-6 space-y-6 overflow-y-auto text-xs leading-relaxed text-slate-300">
          {/* Section 1: Patrol Routes & Encounters */}
          <section className="space-y-2">
            <h4 className="text-xs font-mono font-bold text-amber-400 uppercase tracking-wider flex items-center gap-2">
              <AlertTriangle className="w-4 h-4 text-amber-500" />
              1. Enemy Patrol Route Avoidance
            </h4>
            <div className="p-3 bg-slate-900/80 rounded-xl border border-slate-800 space-y-1.5">
              <p>
                In V Rising, elite commanders (like <strong className="text-amber-300">Vincent the Frostbringer</strong> or <strong className="text-amber-300">Meredith the Bright Archer</strong>) patrol main road networks accompanied by heavily armored militia, paladins, or tech mechs.
              </p>
              <ul className="list-disc list-inside space-y-1 text-slate-400">
                <li><strong className="text-slate-200">Direction & Timing:</strong> Patrols follow specific loops (Clockwise, Counter-Clockwise, or Bidirectional) every 2–5 minutes.</li>
                <li><strong className="text-slate-200">Avoidance Strategy:</strong> Use the map patrol route overlay to build your castle away from high-traffic paths or place defensive honeycomb walls facing patrol choke points.</li>
              </ul>
            </div>
          </section>

          {/* Section 2: Resource Node Placement */}
          <section className="space-y-2">
            <h4 className="text-xs font-mono font-bold text-emerald-400 uppercase tracking-wider flex items-center gap-2">
              <Crosshair className="w-4 h-4 text-emerald-500" />
              2. Strategic Resource Node Scouting
            </h4>
            <div className="p-3 bg-slate-900/80 rounded-xl border border-slate-800 space-y-1.5">
              <p>
                Building your castle within quick travel range of key mining quarries dramatically accelerates progression:
              </p>
              <div className="grid grid-cols-2 gap-2 text-[11px] pt-1">
                <div className="p-2 bg-slate-950 rounded border border-slate-800">
                  <span className="text-amber-400 font-bold block">Copper & Sulfur (Farbane)</span>
                  <span>Southern Farbane Woods features dense copper outcrops and sulfur quarries.</span>
                </div>
                <div className="p-2 bg-slate-950 rounded border border-slate-800">
                  <span className="text-slate-300 font-bold block">Haunted Iron Mine (Dunley)</span>
                  <span>Central Dunley Farmlands mine yielding massive Iron & Hellcat blood.</span>
                </div>
                <div className="p-2 bg-slate-950 rounded border border-slate-800">
                  <span className="text-cyan-400 font-bold block">Sacred Silver Mine (Silverlight)</span>
                  <span>High-tier silver veins. Carry Silver Resistance Potions before raiding.</span>
                </div>
                <div className="p-2 bg-slate-950 rounded border border-slate-800">
                  <span className="text-rose-400 font-bold block">Stygian Rifts (Mortium)</span>
                  <span>Endgame shadow rift zones for farming Stygian Shards & Dracula power.</span>
                </div>
              </div>
            </div>
          </section>

          {/* Section 3: Castle Heart Tiers & Room Matching */}
          <section className="space-y-2">
            <h4 className="text-xs font-mono font-bold text-red-400 uppercase tracking-wider flex items-center gap-2">
              <Heart className="w-4 h-4 text-red-500" />
              3. Castle Heart Upgrades & Matching Flooring
            </h4>
            <div className="p-3 bg-slate-900/80 rounded-xl border border-slate-800 space-y-1.5">
              <p>
                Match room floor tiles to crafting workstations to unlock <strong className="text-emerald-400">-25% crafting cost</strong> and <strong className="text-emerald-400">+50% refining speed</strong> bonuses:
              </p>
              <ul className="list-disc list-inside space-y-1 text-slate-400">
                <li><strong>Forge Room:</strong> Anvils, Furnaces, Grindstones with Forge Floor.</li>
                <li><strong>Alchemy Lab:</strong> Blood Press, Alchemy Table with Alchemy Floor.</li>
                <li><strong>Athenaeum Library:</strong> Research Desks & Bookcases with Library Floor.</li>
              </ul>
            </div>
          </section>
        </div>

        {/* Footer */}
        <div className="p-4 bg-slate-900/60 border-t border-slate-800 flex justify-end">
          <button
            onClick={onClose}
            className="px-5 py-2 bg-slate-900 hover:bg-slate-800 border border-slate-700 text-xs text-slate-200 rounded-lg transition-colors font-mono font-semibold"
          >
            Got It, Return to Map
          </button>
        </div>
      </div>
    </div>
  );
};
