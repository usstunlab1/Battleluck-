import React, { useState } from 'react';
import { 
  Eye, 
  EyeOff, 
  Skull, 
  Pickaxe, 
  Compass, 
  MapPin, 
  Layers, 
  ChevronRight, 
  Sparkles, 
  ShieldAlert,
  Building,
  CheckCircle2,
  Circle,
  RotateCcw,
  Search
} from 'lucide-react';
import { RegionId } from '../../types';
import { REGIONS, V_BLOOD_BOSSES, RESOURCE_NODES, CASTLE_PLOTS, WAYGATES } from '../../data/vrisingData';

interface LeftSidebarProps {
  isOpen: boolean;
  setIsOpen: (open: boolean) => void;
  selectedRegion: RegionId | 'all';
  setSelectedRegion: (r: RegionId | 'all') => void;

  // Layer Toggles
  layerBosses: boolean;
  setLayerBosses: (v: boolean) => void;
  layerResources: boolean;
  setLayerResources: (v: boolean) => void;
  layerWaygates: boolean;
  setLayerWaygates: (v: boolean) => void;
  layerPlots: boolean;
  setLayerPlots: (v: boolean) => void;
  layerContainers: boolean;
  setLayerContainers: (v: boolean) => void;
  layerRadii: boolean;
  setLayerRadii: (v: boolean) => void;
  layerPatrols: boolean;
  setLayerPatrols: (v: boolean) => void;

  // Found Checklist Props
  foundItemIds: Set<string>;
  toggleFound: (id: string) => void;
  onShowAll: () => void;
  onHideAll: () => void;
  onResetFound: () => void;

  // Handler to select node on map
  onSelectItem: (item: { type: string; data: any }) => void;
}

export const LeftSidebar: React.FC<LeftSidebarProps> = ({
  isOpen,
  setIsOpen,
  selectedRegion,
  setSelectedRegion,
  layerBosses,
  setLayerBosses,
  layerResources,
  setLayerResources,
  layerWaygates,
  setLayerWaygates,
  layerPlots,
  setLayerPlots,
  layerContainers,
  setLayerContainers,
  layerRadii,
  setLayerRadii,
  layerPatrols,
  setLayerPatrols,
  foundItemIds,
  toggleFound,
  onShowAll,
  onHideAll,
  onResetFound,
  onSelectItem,
}) => {
  const [activeTab, setActiveTab] = useState<'layers' | 'bosses' | 'resources' | 'plots'>('layers');
  const [filterText, setFilterText] = useState('');

  // Total checklist count
  const totalItems = V_BLOOD_BOSSES.length + RESOURCE_NODES.length + CASTLE_PLOTS.length + WAYGATES.length;
  const foundCount = Array.from(foundItemIds).length;
  const progressPercent = totalItems > 0 ? Math.round((foundCount / totalItems) * 100) : 0;

  return (
    <aside className={`relative z-20 bg-zinc-950 border-r border-zinc-800 transition-all duration-300 flex flex-col shrink-0 font-sans text-zinc-300 ${
      isOpen ? 'w-80' : 'w-12'
    }`}>
      {/* Toggle Button */}
      <button
        onClick={() => setIsOpen(!isOpen)}
        className="absolute -right-3 top-20 bg-zinc-900 border border-zinc-700 text-zinc-300 hover:text-white p-1 rounded-full shadow-lg z-30 transition-transform"
        title={isOpen ? "Collapse Left Sidebar" : "Expand Left Sidebar"}
      >
        <ChevronRight className={`w-3.5 h-3.5 transition-transform ${isOpen ? 'rotate-180' : ''}`} />
      </button>

      {/* Collapsed Bar View */}
      {!isOpen ? (
        <div className="flex flex-col items-center gap-4 py-6 text-zinc-500">
          <button onClick={() => { setIsOpen(true); setActiveTab('layers'); }} className="hover:text-red-400 p-1.5" title="Layers">
            <Layers className="w-5 h-5" />
          </button>
          <button onClick={() => { setIsOpen(true); setActiveTab('bosses'); }} className="hover:text-red-400 p-1.5" title="V Blood Bosses">
            <Skull className="w-5 h-5" />
          </button>
          <button onClick={() => { setIsOpen(true); setActiveTab('resources'); }} className="hover:text-amber-400 p-1.5" title="Resource Nodes">
            <Pickaxe className="w-5 h-5" />
          </button>
          <button onClick={() => { setIsOpen(true); setActiveTab('plots'); }} className="hover:text-blue-400 p-1.5" title="Castle Plots">
            <Building className="w-5 h-5" />
          </button>
        </div>
      ) : (
        /* Expanded Full Sidebar */
        <div className="flex flex-col h-full overflow-hidden bg-black text-zinc-200 font-sans">
          {/* MapGenie Brand Header */}
          <div className="p-4 border-b border-red-950/60 bg-gradient-to-b from-zinc-950 to-black text-center relative">
            <h1 className="text-2xl font-black tracking-widest text-red-600 uppercase font-serif drop-shadow-[0_2px_10px_rgba(220,38,38,0.5)]">
              V RISING MAP
            </h1>
            <p className="text-[10px] tracking-widest uppercase font-mono text-red-500/80 font-bold -mt-0.5">
              V RISING INTERACTIVE MAP
            </p>

            {/* Sub-badge & Social Links */}
            <div className="flex items-center justify-center gap-1.5 mt-2.5">
              <span className="text-[9px] font-mono font-bold bg-red-950 text-red-400 border border-red-800/80 px-2 py-0.5 rounded uppercase">
                MAP GENIE
              </span>
              <div className="flex items-center gap-1 text-zinc-400 text-[10px]">
                <button className="w-5 h-5 rounded bg-zinc-900 border border-zinc-800 flex items-center justify-center hover:text-white hover:border-red-600">f</button>
                <button className="w-5 h-5 rounded bg-zinc-900 border border-zinc-800 flex items-center justify-center hover:text-white hover:border-red-600">tw</button>
                <button className="w-5 h-5 rounded bg-zinc-900 border border-zinc-800 flex items-center justify-center hover:text-white hover:border-red-600">r/</button>
                <button className="w-5 h-5 rounded bg-zinc-900 border border-zinc-800 flex items-center justify-center hover:text-white hover:border-red-600">&lt;/&gt;</button>
              </div>
            </div>
          </div>

          {/* Quick Action Tabs (MapGenie style) */}
          <div className="grid grid-cols-3 border-b border-zinc-800 text-[10px] font-bold tracking-wider font-serif bg-zinc-950">
            <button
              onClick={() => setActiveTab('bosses')}
              className={`py-2 px-1 text-center border-b-2 transition-colors uppercase ${
                activeTab === 'bosses' ? 'border-red-600 text-red-400 bg-red-950/20' : 'border-transparent text-zinc-400 hover:text-white'
              }`}
            >
              BOSS CHECKLIST
            </button>
            <button
              onClick={() => setActiveTab('resources')}
              className={`py-2 px-1 text-center border-b-2 transition-colors uppercase ${
                activeTab === 'resources' ? 'border-red-600 text-red-400 bg-red-950/20' : 'border-transparent text-zinc-400 hover:text-white'
              }`}
            >
              QUESTS
            </button>
            <button
              onClick={() => setActiveTab('layers')}
              className={`py-2 px-1 text-center border-b-2 transition-colors uppercase ${
                activeTab === 'layers' ? 'border-red-600 text-red-400 bg-red-950/20' : 'border-transparent text-zinc-400 hover:text-white'
              }`}
            >
              ABILITIES
            </button>
          </div>

          {/* Regions Accordion Header */}
          <div className="px-3 py-2 border-b border-zinc-800 bg-zinc-900/40 flex items-center justify-between text-xs font-bold font-serif uppercase tracking-wider text-zinc-300">
            <span>REGIONS</span>
            <button 
              onClick={() => setSelectedRegion(selectedRegion === 'all' ? 'dunley' : 'all')}
              className="text-zinc-500 hover:text-red-400 text-sm font-mono"
            >
              {selectedRegion === 'all' ? '+' : '−'}
            </button>
          </div>

          {/* Show All / Hide All Bar */}
          <div className="px-3 py-2 border-b border-zinc-800 bg-zinc-950 flex items-center justify-between text-[11px] font-bold font-serif uppercase tracking-widest">
            <button onClick={onShowAll} className="text-zinc-300 hover:text-red-400 transition-colors">
              SHOW ALL
            </button>
            <button onClick={onHideAll} className="text-zinc-300 hover:text-red-400 transition-colors">
              HIDE ALL
            </button>
          </div>

          {/* Search Box with SEARCH button */}
          <div className="p-3 border-b border-zinc-800 bg-zinc-950 space-y-2">
            <div className="flex items-center border border-zinc-700 bg-black rounded overflow-hidden focus-within:border-red-600">
              <input
                type="text"
                value={filterText}
                onChange={(e) => setFilterText(e.target.value)}
                placeholder="Search..."
                className="w-full bg-transparent px-3 py-1.5 text-xs text-zinc-200 placeholder-zinc-500 focus:outline-none"
              />
              <button 
                onClick={() => {}} 
                className="bg-zinc-900 hover:bg-red-950 hover:text-red-300 px-3 py-1.5 border-l border-zinc-700 text-[10px] font-bold font-serif tracking-wider uppercase text-zinc-300 transition-colors shrink-0"
              >
                SEARCH
              </button>
            </div>

            {/* Checklist Progress Bar */}
            <div className="bg-zinc-900/90 p-2 rounded border border-zinc-800">
              <div className="flex items-center justify-between text-[10px] font-mono font-bold mb-1">
                <span className="text-zinc-400 uppercase tracking-wider">Progress</span>
                <span className="text-emerald-400">{foundCount} / {totalItems} ({progressPercent}%)</span>
              </div>
              <div className="w-full bg-black h-1.5 rounded-full overflow-hidden border border-zinc-800">
                <div 
                  className="bg-gradient-to-r from-red-600 to-emerald-500 h-full transition-all duration-300" 
                  style={{ width: `${progressPercent}%` }}
                />
              </div>
            </div>
          </div>

          {/* Tab 1: Map Layers Control */}
          {activeTab === 'layers' && (
            <div className="p-4 space-y-3 overflow-y-auto flex-1">
              <h3 className="text-[10px] uppercase font-bold text-zinc-500 tracking-wider">
                Map Layer Visibility
              </h3>

              <div className="space-y-2">
                {/* Enemy Patrol Routes Layer */}
                <div 
                  onClick={() => setLayerPatrols(!layerPatrols)}
                  className="flex items-center justify-between p-2.5 rounded-lg bg-zinc-900/80 border border-zinc-800 hover:border-zinc-700 cursor-pointer transition-colors"
                >
                  <div className="flex items-center gap-2.5">
                    <ShieldAlert className="w-4 h-4 text-amber-500" />
                    <div>
                      <p className="text-xs font-semibold text-zinc-200">Enemy Patrol Routes</p>
                      <p className="text-[10px] text-zinc-500">Squad paths & direction</p>
                    </div>
                  </div>
                  {layerPatrols ? <Eye className="w-4 h-4 text-amber-400" /> : <EyeOff className="w-4 h-4 text-zinc-600" />}
                </div>

                {/* Bosses Layer */}
                <div 
                  onClick={() => setLayerBosses(!layerBosses)}
                  className="flex items-center justify-between p-2.5 rounded-lg bg-zinc-900/80 border border-zinc-800 hover:border-zinc-700 cursor-pointer transition-colors"
                >
                  <div className="flex items-center gap-2.5">
                    <Skull className="w-4 h-4 text-red-500" />
                    <div>
                      <p className="text-xs font-semibold text-zinc-200">V Blood Boss Lairs</p>
                      <p className="text-[10px] text-zinc-500">35+ bosses & unlocks</p>
                    </div>
                  </div>
                  {layerBosses ? <Eye className="w-4 h-4 text-red-400" /> : <EyeOff className="w-4 h-4 text-zinc-600" />}
                </div>

                {/* Resources Layer */}
                <div 
                  onClick={() => setLayerResources(!layerResources)}
                  className="flex items-center justify-between p-2.5 rounded-lg bg-zinc-900/80 border border-zinc-800 hover:border-zinc-700 cursor-pointer transition-colors"
                >
                  <div className="flex items-center gap-2.5">
                    <Pickaxe className="w-4 h-4 text-amber-500" />
                    <div>
                      <p className="text-xs font-semibold text-zinc-200">Resource Mines & Ores</p>
                      <p className="text-[10px] text-zinc-500">Copper, Iron, Silver, Quartz</p>
                    </div>
                  </div>
                  {layerResources ? <Eye className="w-4 h-4 text-amber-400" /> : <EyeOff className="w-4 h-4 text-zinc-600" />}
                </div>

                {/* Waygates Layer */}
                <div 
                  onClick={() => setLayerWaygates(!layerWaygates)}
                  className="flex items-center justify-between p-2.5 rounded-lg bg-zinc-900/80 border border-zinc-800 hover:border-zinc-700 cursor-pointer transition-colors"
                >
                  <div className="flex items-center gap-2.5">
                    <Compass className="w-4 h-4 text-blue-400" />
                    <div>
                      <p className="text-xs font-semibold text-zinc-200">Waygates & Cave Passages</p>
                      <p className="text-[10px] text-zinc-500">Fast travel network</p>
                    </div>
                  </div>
                  {layerWaygates ? <Eye className="w-4 h-4 text-blue-400" /> : <EyeOff className="w-4 h-4 text-zinc-600" />}
                </div>

                {/* Castle Plots Layer */}
                <div 
                  onClick={() => setLayerPlots(!layerPlots)}
                  className="flex items-center justify-between p-2.5 rounded-lg bg-zinc-900/80 border border-zinc-800 hover:border-zinc-700 cursor-pointer transition-colors"
                >
                  <div className="flex items-center gap-2.5">
                    <Building className="w-4 h-4 text-indigo-400" />
                    <div>
                      <p className="text-xs font-semibold text-zinc-200">Claimable Territory Plots</p>
                      <p className="text-[10px] text-zinc-500">Choke points & tile sizes</p>
                    </div>
                  </div>
                  {layerPlots ? <Eye className="w-4 h-4 text-indigo-400" /> : <EyeOff className="w-4 h-4 text-zinc-600" />}
                </div>

                {/* Placed Containers Layer */}
                <div 
                  onClick={() => setLayerContainers(!layerContainers)}
                  className="flex items-center justify-between p-2.5 rounded-lg bg-zinc-900/80 border border-zinc-800 hover:border-zinc-700 cursor-pointer transition-colors"
                >
                  <div className="flex items-center gap-2.5">
                    <MapPin className="w-4 h-4 text-emerald-400" />
                    <div>
                      <p className="text-xs font-semibold text-zinc-200">Placed Base Structures</p>
                      <p className="text-[10px] text-zinc-500">Chests, Stations, Furniture</p>
                    </div>
                  </div>
                  {layerContainers ? <Eye className="w-4 h-4 text-emerald-400" /> : <EyeOff className="w-4 h-4 text-zinc-600" />}
                </div>

                {/* Radius Circles Layer */}
                <div 
                  onClick={() => setLayerRadii(!layerRadii)}
                  className="flex items-center justify-between p-2.5 rounded-lg bg-zinc-900/80 border border-zinc-800 hover:border-zinc-700 cursor-pointer transition-colors"
                >
                  <div className="flex items-center gap-2.5">
                    <Sparkles className="w-4 h-4 text-fuchsia-400" />
                    <div>
                      <p className="text-xs font-semibold text-zinc-200">Custom Radius Circles</p>
                      <p className="text-[10px] text-zinc-500">Territory & alert boundaries</p>
                    </div>
                  </div>
                  {layerRadii ? <Eye className="w-4 h-4 text-fuchsia-400" /> : <EyeOff className="w-4 h-4 text-zinc-600" />}
                </div>
              </div>
            </div>
          )}

          {/* Tab 2: V Blood Boss Explorer */}
          {activeTab === 'bosses' && (
            <div className="p-3 space-y-2 overflow-y-auto flex-1">
              <p className="text-[10px] font-bold uppercase text-zinc-500 tracking-wider mb-2">
                V Blood Bosses ({V_BLOOD_BOSSES.length})
              </p>
              {V_BLOOD_BOSSES
                .filter(b => selectedRegion === 'all' || b.region === selectedRegion)
                .filter(b => !filterText || b.name.toLowerCase().includes(filterText.toLowerCase()) || b.rewards.some(r => r.toLowerCase().includes(filterText.toLowerCase())))
                .map(boss => {
                  const isFound = foundItemIds.has(boss.id);
                  return (
                    <div
                      key={boss.id}
                      className={`p-2.5 rounded-lg bg-zinc-900 border transition-colors flex items-start justify-between group ${
                        isFound ? 'border-emerald-900/60 bg-emerald-950/20' : 'border-zinc-800 hover:border-red-900'
                      }`}
                    >
                      <div className="flex-1 cursor-pointer" onClick={() => onSelectItem({ type: 'boss', data: boss })}>
                        <div className="flex items-center gap-2">
                          <span className="text-[10px] font-bold font-mono px-1.5 py-0.5 rounded bg-red-950 text-red-400 border border-red-900/50">
                            Lv {boss.level}
                          </span>
                          <h4 className={`text-xs font-bold transition-colors ${isFound ? 'line-through text-zinc-400' : 'text-zinc-200 group-hover:text-red-300'}`}>
                            {boss.name}
                          </h4>
                        </div>
                        <p className="text-[11px] text-zinc-400 mt-1">
                          Blood: <span className="text-zinc-300">{boss.bloodType}</span>
                        </p>
                        <div className="flex flex-wrap gap-1 mt-1.5">
                          {boss.rewards.slice(0, 2).map((r, i) => (
                            <span key={i} className="text-[9px] bg-zinc-800 text-zinc-300 px-1.5 py-0.5 rounded">
                              {r}
                            </span>
                          ))}
                        </div>
                      </div>

                      <button
                        onClick={(e) => {
                          e.stopPropagation();
                          toggleFound(boss.id);
                        }}
                        className="p-1 text-zinc-500 hover:text-emerald-400 transition-colors ml-1"
                        title={isFound ? "Mark as Unfound" : "Mark as Found"}
                      >
                        {isFound ? <CheckCircle2 className="w-4 h-4 text-emerald-400" /> : <Circle className="w-4 h-4 text-zinc-600" />}
                      </button>
                    </div>
                  );
                })}
            </div>
          )}

          {/* Tab 3: Resource Nodes Explorer */}
          {activeTab === 'resources' && (
            <div className="p-3 space-y-2 overflow-y-auto flex-1">
              <p className="text-[10px] font-bold uppercase text-zinc-500 tracking-wider mb-2">
                Key Ore & Resource Mines ({RESOURCE_NODES.length})
              </p>
              {RESOURCE_NODES
                .filter(r => selectedRegion === 'all' || r.region === selectedRegion)
                .filter(r => !filterText || r.name.toLowerCase().includes(filterText.toLowerCase()) || r.type.toLowerCase().includes(filterText.toLowerCase()))
                .map(res => {
                  const isFound = foundItemIds.has(res.id);
                  return (
                    <div
                      key={res.id}
                      className={`p-2.5 rounded-lg bg-zinc-900 border transition-colors flex items-center justify-between group ${
                        isFound ? 'border-emerald-900/60 bg-emerald-950/20' : 'border-zinc-800 hover:border-amber-900'
                      }`}
                    >
                      <div className="flex-1 cursor-pointer" onClick={() => onSelectItem({ type: 'resource', data: res })}>
                        <div className="flex items-center gap-2">
                          <span className="w-2 h-2 rounded-full bg-amber-400 shrink-0" />
                          <h4 className={`text-xs font-bold ${isFound ? 'line-through text-zinc-400' : 'text-zinc-200 group-hover:text-amber-300'}`}>
                            {res.name}
                          </h4>
                        </div>
                        <p className="text-[10px] text-zinc-500 capitalize mt-0.5">
                          {res.type.replace('_', ' ')} • {res.density.replace('_', ' ')}
                        </p>
                      </div>

                      <button
                        onClick={(e) => {
                          e.stopPropagation();
                          toggleFound(res.id);
                        }}
                        className="p-1 text-zinc-500 hover:text-emerald-400 transition-colors ml-1"
                        title={isFound ? "Mark as Unfound" : "Mark as Found"}
                      >
                        {isFound ? <CheckCircle2 className="w-4 h-4 text-emerald-400" /> : <Circle className="w-4 h-4 text-zinc-600" />}
                      </button>
                    </div>
                  );
                })}
            </div>
          )}

          {/* Tab 4: Castle Territory Plots */}
          {activeTab === 'plots' && (
            <div className="p-3 space-y-2 overflow-y-auto flex-1">
              <p className="text-[10px] font-bold uppercase text-zinc-500 tracking-wider mb-2">
                Prime Base Plots ({CASTLE_PLOTS.length})
              </p>
              {CASTLE_PLOTS
                .filter(p => selectedRegion === 'all' || p.region === selectedRegion)
                .filter(p => !filterText || p.name.toLowerCase().includes(filterText.toLowerCase()))
                .map(plot => {
                  const isFound = foundItemIds.has(plot.id);
                  return (
                    <div
                      key={plot.id}
                      className={`p-2.5 rounded-lg bg-zinc-900 border transition-colors group ${
                        isFound ? 'border-emerald-900/60 bg-emerald-950/20' : 'border-zinc-800 hover:border-indigo-900'
                      }`}
                    >
                      <div className="flex items-center justify-between">
                        <div className="flex-1 cursor-pointer" onClick={() => onSelectItem({ type: 'plot', data: plot })}>
                          <div className="flex items-center justify-between">
                            <h4 className={`text-xs font-bold ${isFound ? 'line-through text-zinc-400' : 'text-zinc-200 group-hover:text-indigo-300'}`}>
                              {plot.name}
                            </h4>
                            <span className="text-[9px] font-mono px-1.5 py-0.5 rounded bg-zinc-800 text-blue-300 mr-1">
                              {plot.tileSize} Tiles
                            </span>
                          </div>
                          {plot.chokePoint && (
                            <span className="inline-block text-[9px] bg-red-950/80 text-red-400 border border-red-900/50 px-1.5 py-0.5 rounded font-mono mt-1">
                              ⚠️ Single Choke Entry
                            </span>
                          )}
                          <p className="text-[11px] text-zinc-400 mt-1 line-clamp-2">
                            {plot.description}
                          </p>
                        </div>

                        <button
                          onClick={(e) => {
                            e.stopPropagation();
                            toggleFound(plot.id);
                          }}
                          className="p-1 text-zinc-500 hover:text-emerald-400 transition-colors ml-1"
                          title={isFound ? "Mark as Unfound" : "Mark as Found"}
                        >
                          {isFound ? <CheckCircle2 className="w-4 h-4 text-emerald-400" /> : <Circle className="w-4 h-4 text-zinc-600" />}
                        </button>
                      </div>
                    </div>
                  );
                })}
            </div>
          )}
        </div>
      )}
    </aside>
  );
};
