import React, { useState } from 'react';
import { 
  PlusCircle, 
  Trash2, 
  MousePointer, 
  Sparkles, 
  Package, 
  Heart, 
  X, 
  ChevronLeft, 
  Check, 
  Footprints,
  Pickaxe,
  MapPin,
  ShieldAlert,
  Navigation,
  Compass,
  AlertTriangle
} from 'lucide-react';
import { 
  CastleHeartTier, 
  PlacedRadius, 
  PlacedContainer, 
  CustomMarker, 
  ActiveTool, 
  PatrolRoute, 
  PatrolPoint, 
  PlacedResourceNode, 
  ResourceType 
} from '../../types';
import { 
  RADIUS_PRESETS, 
  PLACEABLE_CONTAINERS, 
  ROOM_PRESETS, 
  COMMON_ENEMY_TYPES, 
  RESOURCE_TYPES_CONFIG 
} from '../../data/vrisingData';

interface RightSidebarProps {
  isOpen: boolean;
  setIsOpen: (open: boolean) => void;
  activeTool: ActiveTool;
  setActiveTool: (tool: ActiveTool) => void;

  // Radius Creator Params
  pendingRadiusMeters: number;
  setPendingRadiusMeters: (m: number) => void;
  pendingRadiusColor: string;
  setPendingRadiusColor: (c: string) => void;
  pendingRadiusOpacity: number;
  setPendingRadiusOpacity: (o: number) => void;
  pendingRadiusLabel: string;
  setPendingRadiusLabel: (l: string) => void;
  pendingRadiusBorderStyle: 'solid' | 'dashed' | 'pulse';
  setPendingRadiusBorderStyle: (s: 'solid' | 'dashed' | 'pulse') => void;

  // Castle & Container Params
  heartTier: CastleHeartTier;
  setHeartTier: (t: CastleHeartTier) => void;
  pendingContainer: { id: string; name: string; category: any; icon: string } | null;
  setPendingContainer: (c: { id: string; name: string; category: any; icon: string } | null) => void;

  // Patrol Route Creator Params
  pendingPatrolPoints: PatrolPoint[];
  setPendingPatrolPoints: React.Dispatch<React.SetStateAction<PatrolPoint[]>>;
  pendingEnemyType: string;
  setPendingEnemyType: (et: string) => void;
  pendingPatrolDirection: 'Clockwise' | 'Counter-Clockwise' | 'Bidirectional' | 'Loop';
  setPendingPatrolDirection: (d: 'Clockwise' | 'Counter-Clockwise' | 'Bidirectional' | 'Loop') => void;
  pendingPatrolFrequency: 'Continuous' | 'Every 2 Mins' | 'Every 5 Mins' | 'Night Only';
  setPendingPatrolFrequency: (f: 'Continuous' | 'Every 2 Mins' | 'Every 5 Mins' | 'Night Only') => void;
  pendingPatrolColor: string;
  setPendingPatrolColor: (c: string) => void;
  onFinishPatrolRoute: () => void;

  // Resource Placement Params
  pendingResourceType: ResourceType;
  setPendingResourceType: (rt: ResourceType) => void;
  pendingResourceDensity: 'low' | 'medium' | 'high' | 'rich_mine';
  setPendingResourceDensity: (d: 'low' | 'medium' | 'high' | 'rich_mine') => void;

  // Inspector & Selection
  selectedItem: { type: string; data: any } | null;
  onSelectItem: (item: { type: string; data: any } | null) => void;

  // Placed Lists
  placedRadii: PlacedRadius[];
  setPlacedRadii: React.Dispatch<React.SetStateAction<PlacedRadius[]>>;
  placedContainers: PlacedContainer[];
  setPlacedContainers: React.Dispatch<React.SetStateAction<PlacedContainer[]>>;
  customMarkers: CustomMarker[];
  setCustomMarkers: React.Dispatch<React.SetStateAction<CustomMarker[]>>;
  patrolRoutes: PatrolRoute[];
  setPatrolRoutes: React.Dispatch<React.SetStateAction<PatrolRoute[]>>;
  placedResourceNodes: PlacedResourceNode[];
  setPlacedResourceNodes: React.Dispatch<React.SetStateAction<PlacedResourceNode[]>>;

  // BepInEx Modal trigger
  onOpenBepInExModal?: () => void;
}

const COLOR_PALETTE = [
  '#EF4444', // Red
  '#F59E0B', // Amber
  '#10B981', // Emerald
  '#3B82F6', // Blue
  '#8B5CF6', // Purple
  '#EC4899', // Pink
  '#38BDF8', // Sky Cyan
];

export const RightSidebar: React.FC<RightSidebarProps> = ({
  isOpen,
  setIsOpen,
  activeTool,
  setActiveTool,
  pendingRadiusMeters,
  setPendingRadiusMeters,
  pendingRadiusColor,
  setPendingRadiusColor,
  pendingRadiusOpacity,
  setPendingRadiusOpacity,
  pendingRadiusLabel,
  setPendingRadiusLabel,
  pendingRadiusBorderStyle,
  setPendingRadiusBorderStyle,
  heartTier,
  setHeartTier,
  pendingContainer,
  setPendingContainer,
  pendingPatrolPoints,
  setPendingPatrolPoints,
  pendingEnemyType,
  setPendingEnemyType,
  pendingPatrolDirection,
  setPendingPatrolDirection,
  pendingPatrolFrequency,
  setPendingPatrolFrequency,
  pendingPatrolColor,
  setPendingPatrolColor,
  onFinishPatrolRoute,
  pendingResourceType,
  setPendingResourceType,
  pendingResourceDensity,
  setPendingResourceDensity,
  selectedItem,
  onSelectItem,
  placedRadii,
  setPlacedRadii,
  placedContainers,
  setPlacedContainers,
  customMarkers,
  setCustomMarkers,
  patrolRoutes,
  setPatrolRoutes,
  placedResourceNodes,
  setPlacedResourceNodes,
  onOpenBepInExModal,
}) => {
  const [containerCategory, setContainerCategory] = useState<'storage' | 'workstation' | 'furniture' | 'defense'>('storage');

  // Delete handler for selected item
  const handleDeleteSelected = () => {
    if (!selectedItem) return;
    if (selectedItem.type === 'radius') {
      setPlacedRadii(prev => prev.filter(r => r.id !== selectedItem.data.id));
    } else if (selectedItem.type === 'container') {
      setPlacedContainers(prev => prev.filter(c => c.id !== selectedItem.data.id));
    } else if (selectedItem.type === 'marker') {
      setCustomMarkers(prev => prev.filter(m => m.id !== selectedItem.data.id));
    } else if (selectedItem.type === 'patrol') {
      setPatrolRoutes(prev => prev.filter(p => p.id !== selectedItem.data.id));
    } else if (selectedItem.type === 'placed_resource') {
      setPlacedResourceNodes(prev => prev.filter(rn => rn.id !== selectedItem.data.id));
    }
    onSelectItem(null);
  };

  return (
    <aside className={`relative z-20 bg-slate-950/95 border-l border-slate-800/80 transition-all duration-300 flex flex-col shrink-0 ${
      isOpen ? 'w-92' : 'w-12'
    }`}>
      {/* Toggle Button */}
      <button
        onClick={() => setIsOpen(!isOpen)}
        className="absolute -left-3 top-20 bg-slate-900 border border-slate-700 text-slate-300 hover:text-white p-1 rounded-full shadow-lg z-30 transition-transform"
        title={isOpen ? "Collapse Right Sidebar" : "Expand Tools Sidebar"}
      >
        <ChevronLeft className={`w-3.5 h-3.5 transition-transform ${isOpen ? '' : 'rotate-180'}`} />
      </button>

      {!isOpen ? (
        <div className="flex flex-col items-center gap-4 py-6 text-slate-400">
          <button onClick={() => { setIsOpen(true); setActiveTool('select'); }} className="hover:text-red-400 p-1.5" title="Select & Inspect">
            <MousePointer className="w-5 h-5" />
          </button>
          <button onClick={() => { setIsOpen(true); setActiveTool('patrol'); }} className="hover:text-amber-400 p-1.5" title="Enemy Patrol Routes">
            <Footprints className="w-5 h-5" />
          </button>
          <button onClick={() => { setIsOpen(true); setActiveTool('resource_place'); }} className="hover:text-emerald-400 p-1.5" title="Resource Nodes">
            <Pickaxe className="w-5 h-5" />
          </button>
          <button onClick={() => { setIsOpen(true); setActiveTool('radius'); }} className="hover:text-fuchsia-400 p-1.5" title="Radius Tool">
            <Sparkles className="w-5 h-5" />
          </button>
          <button onClick={() => { setIsOpen(true); setActiveTool('container'); }} className="hover:text-blue-400 p-1.5" title="Castle Structures">
            <Package className="w-5 h-5" />
          </button>
        </div>
      ) : (
        <div className="flex flex-col h-full overflow-hidden bg-black text-zinc-200 font-sans">
          {/* Admin Map Editor & BepInEx Hub Banner */}
          <div className="p-3.5 border-b border-red-950 bg-gradient-to-b from-zinc-950 via-red-950/20 to-black text-center space-y-2">
            <div className="flex items-center justify-between">
              <span className="text-[9px] font-mono font-bold bg-emerald-950 text-emerald-400 border border-emerald-800 px-2 py-0.5 rounded-full uppercase flex items-center gap-1">
                <span className="w-1.5 h-1.5 rounded-full bg-emerald-400 animate-pulse" /> ADMIN ACTIVE
              </span>
              <span className="text-[10px] font-mono text-zinc-400">UNLOCKED</span>
            </div>

            <h2 className="text-lg font-black tracking-widest text-red-600 uppercase font-serif drop-shadow-[0_2px_8px_rgba(220,38,38,0.5)]">
              ADMIN MAP EDITOR
            </h2>

            <button 
              onClick={onOpenBepInExModal} 
              className="w-full py-2 px-3 border border-red-700/80 bg-red-950/80 hover:bg-red-900 hover:border-red-500 text-xs font-bold font-serif uppercase tracking-wider text-red-200 transition-colors rounded-lg flex items-center justify-center gap-2 shadow-lg"
            >
              <Sparkles className="w-4 h-4 text-amber-400 animate-bounce" />
              <span>BepInEx Server JSON Hub</span>
            </button>

            {/* Admin Capabilities List */}
            <div className="text-[10px] text-zinc-400 text-left space-y-1 font-mono pt-1">
              <p className="flex items-center gap-1.5"><span className="text-emerald-400">✓</span> Add & edit custom ores, bosses & markers</p>
              <p className="flex items-center gap-1.5"><span className="text-emerald-400">✓</span> Sync with BepInEx Dedicated Server JSON</p>
              <p className="flex items-center gap-1.5"><span className="text-emerald-400">✓</span> Free progress tracking (No login needed)</p>
            </div>
          </div>

          {/* Tool Navigation Header */}
          <div className="grid grid-cols-5 bg-zinc-950 border-b border-zinc-800 text-[10px] font-bold font-serif uppercase tracking-wider">
            <button
              onClick={() => setActiveTool('select')}
              className={`py-2 px-1 text-center transition-colors border-b-2 flex flex-col items-center gap-1 ${
                activeTool === 'select' ? 'border-red-600 text-red-400 bg-red-950/20 font-bold' : 'border-transparent text-zinc-400 hover:text-zinc-200'
              }`}
            >
              <MousePointer className="w-3.5 h-3.5" />
              <span>Select</span>
            </button>
            <button
              onClick={() => setActiveTool('patrol')}
              className={`py-2 px-1 text-center transition-colors border-b-2 flex flex-col items-center gap-1 ${
                activeTool === 'patrol' ? 'border-amber-600 text-amber-400 bg-red-950/20 font-bold' : 'border-transparent text-zinc-400 hover:text-zinc-200'
              }`}
            >
              <Footprints className="w-3.5 h-3.5" />
              <span>Patrols</span>
            </button>
            <button
              onClick={() => setActiveTool('resource_place')}
              className={`py-2 px-1 text-center transition-colors border-b-2 flex flex-col items-center gap-1 ${
                activeTool === 'resource_place' ? 'border-emerald-600 text-emerald-400 bg-red-950/20 font-bold' : 'border-transparent text-zinc-400 hover:text-zinc-200'
              }`}
            >
              <Pickaxe className="w-3.5 h-3.5" />
              <span>Ores</span>
            </button>
            <button
              onClick={() => setActiveTool('radius')}
              className={`py-2 px-1 text-center transition-colors border-b-2 flex flex-col items-center gap-1 ${
                activeTool === 'radius' ? 'border-fuchsia-600 text-fuchsia-400 bg-red-950/20 font-bold' : 'border-transparent text-zinc-400 hover:text-zinc-200'
              }`}
            >
              <Sparkles className="w-3.5 h-3.5" />
              <span>Radius</span>
            </button>
            <button
              onClick={() => setActiveTool('container')}
              className={`py-2 px-1 text-center transition-colors border-b-2 flex flex-col items-center gap-1 ${
                activeTool === 'container' ? 'border-blue-600 text-blue-400 bg-red-950/20 font-bold' : 'border-transparent text-zinc-400 hover:text-zinc-200'
              }`}
            >
              <Package className="w-3.5 h-3.5" />
              <span>Castle</span>
            </button>
          </div>

          {/* Body Content */}
          <div className="p-4 space-y-4 overflow-y-auto flex-1 text-slate-300 text-xs">
            {/* ITEM INSPECTOR */}
            {selectedItem && (
              <div className="p-3.5 rounded-xl bg-red-950/30 border border-red-800/80 space-y-2.5 relative animate-in fade-in zoom-in-95 duration-150">
                <button
                  onClick={() => onSelectItem(null)}
                  className="absolute top-2.5 right-2.5 text-slate-400 hover:text-white"
                >
                  <X className="w-4 h-4" />
                </button>
                <div className="flex items-center gap-2">
                  <span className="text-[10px] font-mono uppercase bg-red-900/80 text-red-300 px-2 py-0.5 rounded font-bold">
                    Selected {selectedItem.type.replace('_', ' ')}
                  </span>
                  <h3 className="text-xs font-bold text-slate-100 truncate pr-6">
                    {selectedItem.data.name}
                  </h3>
                </div>

                {/* Patrol Route Inspector */}
                {selectedItem.type === 'patrol' && (
                  <div className="space-y-1.5 text-slate-300 text-xs">
                    <p><span className="text-slate-500">Enemy Unit:</span> <span className="font-bold text-amber-300">{selectedItem.data.enemyType}</span></p>
                    <p><span className="text-slate-500">Direction:</span> {selectedItem.data.direction}</p>
                    <p><span className="text-slate-500">Frequency:</span> {selectedItem.data.frequency}</p>
                    <p><span className="text-slate-500">Waypoints:</span> {selectedItem.data.points?.length || 0} Points</p>
                    {selectedItem.data.notes && <p className="text-[11px] text-slate-400 italic">{selectedItem.data.notes}</p>}
                    <button
                      onClick={handleDeleteSelected}
                      className="w-full py-1.5 mt-2 rounded bg-red-900/60 hover:bg-red-800 text-red-200 text-xs font-semibold flex items-center justify-center gap-1.5 transition-colors font-mono"
                    >
                      <Trash2 className="w-3.5 h-3.5" /> Remove Patrol Route
                    </button>
                  </div>
                )}

                {/* Placed Resource Node Inspector */}
                {selectedItem.type === 'placed_resource' && (
                  <div className="space-y-1.5 text-slate-300 text-xs">
                    <p><span className="text-slate-500">Ore Type:</span> <span className="font-bold text-emerald-400 capitalize">{selectedItem.data.type}</span></p>
                    <p><span className="text-slate-500">Density:</span> <span className="capitalize">{selectedItem.data.density}</span></p>
                    <button
                      onClick={handleDeleteSelected}
                      className="w-full py-1.5 mt-2 rounded bg-red-900/60 hover:bg-red-800 text-red-200 text-xs font-semibold flex items-center justify-center gap-1.5 transition-colors font-mono"
                    >
                      <Trash2 className="w-3.5 h-3.5" /> Delete Resource Node
                    </button>
                  </div>
                )}

                {/* Radius Inspector */}
                {selectedItem.type === 'radius' && (
                  <div className="space-y-1.5 text-xs">
                    <p>Radius Size: <span className="font-bold text-fuchsia-300">{selectedItem.data.radiusMeters} meters</span></p>
                    <button
                      onClick={handleDeleteSelected}
                      className="w-full py-1.5 mt-2 rounded bg-red-900/60 hover:bg-red-800 text-red-200 text-xs font-semibold flex items-center justify-center gap-1.5 transition-colors font-mono"
                    >
                      <Trash2 className="w-3.5 h-3.5" /> Delete Radius Zone
                    </button>
                  </div>
                )}

                {/* Container Inspector */}
                {selectedItem.type === 'container' && (
                  <div className="space-y-1.5 text-xs">
                    <p>Category: <span className="capitalize text-emerald-400 font-bold">{selectedItem.data.category}</span></p>
                    <button
                      onClick={handleDeleteSelected}
                      className="w-full py-1.5 mt-2 rounded bg-red-900/60 hover:bg-red-800 text-red-200 text-xs font-semibold flex items-center justify-center gap-1.5 transition-colors font-mono"
                    >
                      <Trash2 className="w-3.5 h-3.5" /> Remove Base Structure
                    </button>
                  </div>
                )}
              </div>
            )}

            {/* TOOL: ENEMY PATROL ROUTE CREATOR */}
            {activeTool === 'patrol' && (
              <div className="space-y-4">
                <div className="flex items-center justify-between">
                  <h3 className="text-xs font-bold text-slate-200 uppercase tracking-wider font-mono flex items-center gap-1.5">
                    <Footprints className="w-4 h-4 text-amber-400" />
                    Enemy Patrol Route Tool
                  </h3>
                  <span className="text-[10px] font-mono bg-amber-950 text-amber-300 border border-amber-800 px-2 py-0.5 rounded">
                    {pendingPatrolPoints.length} Points Set
                  </span>
                </div>

                <p className="text-[11px] text-slate-400 leading-relaxed">
                  Click on the map sequentially to set waypoints for an enemy patrol path. Strategic route planning helps avoid deadly elite encounters!
                </p>

                {/* Enemy Type Selection */}
                <div>
                  <label className="text-[10px] font-mono uppercase text-slate-400 block mb-1 font-bold">
                    Select Enemy Squad
                  </label>
                  <select
                    value={pendingEnemyType}
                    onChange={(e) => setPendingEnemyType(e.target.value)}
                    className="w-full bg-slate-900 border border-slate-800 rounded-lg px-2.5 py-1.5 text-xs text-slate-200 focus:border-amber-500 focus:outline-none"
                  >
                    {COMMON_ENEMY_TYPES.map(e => (
                      <option key={e.id} value={e.name}>
                        {e.name} ({e.level})
                      </option>
                    ))}
                    <option value="Custom Elite Patrol">Custom Elite Squad</option>
                  </select>
                </div>

                {/* Direction Selector */}
                <div>
                  <label className="text-[10px] font-mono uppercase text-slate-400 block mb-1 font-bold">
                    Patrol Direction
                  </label>
                  <div className="grid grid-cols-2 gap-1 text-[11px] font-mono">
                    {(['Clockwise', 'Counter-Clockwise', 'Bidirectional', 'Loop'] as const).map(dir => (
                      <button
                        key={dir}
                        onClick={() => setPendingPatrolDirection(dir)}
                        className={`p-1.5 rounded border text-center transition-all ${
                          pendingPatrolDirection === dir
                            ? 'bg-amber-950 border-amber-600 text-amber-300 font-bold'
                            : 'bg-slate-900 border-slate-800 text-slate-400 hover:text-slate-200'
                        }`}
                      >
                        {dir}
                      </button>
                    ))}
                  </div>
                </div>

                {/* Frequency Selector */}
                <div>
                  <label className="text-[10px] font-mono uppercase text-slate-400 block mb-1 font-bold">
                    Patrol Frequency
                  </label>
                  <div className="grid grid-cols-2 gap-1 text-[11px] font-mono">
                    {(['Continuous', 'Every 2 Mins', 'Every 5 Mins', 'Night Only'] as const).map(freq => (
                      <button
                        key={freq}
                        onClick={() => setPendingPatrolFrequency(freq)}
                        className={`p-1.5 rounded border text-center transition-all ${
                          pendingPatrolFrequency === freq
                            ? 'bg-amber-950 border-amber-600 text-amber-300 font-bold'
                            : 'bg-slate-900 border-slate-800 text-slate-400 hover:text-slate-200'
                        }`}
                      >
                        {freq}
                      </button>
                    ))}
                  </div>
                </div>

                {/* Color Selector */}
                <div>
                  <label className="text-[10px] font-mono uppercase text-slate-400 block mb-1.5 font-bold">
                    Route Line Color
                  </label>
                  <div className="flex items-center gap-2">
                    {COLOR_PALETTE.map(c => (
                      <button
                        key={c}
                        onClick={() => setPendingPatrolColor(c)}
                        style={{ backgroundColor: c }}
                        className={`w-6 h-6 rounded-full border-2 transition-transform ${
                          pendingPatrolColor === c ? 'border-white scale-110 shadow-lg' : 'border-transparent opacity-80'
                        }`}
                      />
                    ))}
                  </div>
                </div>

                {/* Active Waypoints List & Complete Route Button */}
                <div className="p-3 bg-slate-900/80 rounded-xl border border-slate-800 space-y-2">
                  <div className="flex items-center justify-between">
                    <span className="text-[10px] font-mono uppercase text-slate-400 font-bold">Waypoints Added</span>
                    <span className="text-amber-400 font-bold font-mono">{pendingPatrolPoints.length}</span>
                  </div>

                  <div className="flex gap-2">
                    <button
                      onClick={onFinishPatrolRoute}
                      disabled={pendingPatrolPoints.length < 2}
                      className="w-full py-2 rounded-lg bg-gradient-to-r from-amber-600 to-amber-700 hover:from-amber-500 hover:to-amber-600 disabled:opacity-40 text-white font-bold text-xs shadow-md flex items-center justify-center gap-1.5 font-mono transition-all"
                    >
                      <Check className="w-4 h-4 text-emerald-300" /> Save Patrol Route
                    </button>
                    <button
                      onClick={() => setPendingPatrolPoints([])}
                      disabled={pendingPatrolPoints.length === 0}
                      className="px-3 py-2 rounded-lg bg-slate-800 hover:bg-red-950 disabled:opacity-40 text-slate-300 hover:text-red-300 text-xs font-mono transition-all"
                    >
                      Clear
                    </button>
                  </div>
                </div>
              </div>
            )}

            {/* TOOL: RESOURCE NODE PLACEMENT */}
            {activeTool === 'resource_place' && (
              <div className="space-y-4">
                <div className="flex items-center justify-between">
                  <h3 className="text-xs font-bold text-slate-200 uppercase tracking-wider font-mono flex items-center gap-1.5">
                    <Pickaxe className="w-4 h-4 text-emerald-400" />
                    Resource Node Placement
                  </h3>
                  <span className="text-[10px] font-mono bg-emerald-950 text-emerald-300 border border-emerald-800 px-2 py-0.5 rounded capitalize">
                    {pendingResourceType}
                  </span>
                </div>

                <p className="text-[11px] text-slate-400 leading-relaxed">
                  Select a resource ore or plant type below, then click anywhere on the map canvas to place customized resource node markers.
                </p>

                {/* Resource Palette Grid */}
                <div>
                  <label className="text-[10px] font-mono uppercase text-slate-400 block mb-1.5 font-bold">
                    Select Resource Type
                  </label>
                  <div className="grid grid-cols-2 gap-1.5">
                    {(Object.keys(RESOURCE_TYPES_CONFIG) as ResourceType[]).map(typeKey => {
                      const cfg = RESOURCE_TYPES_CONFIG[typeKey];
                      const isSelected = pendingResourceType === typeKey;
                      return (
                        <button
                          key={typeKey}
                          onClick={() => setPendingResourceType(typeKey)}
                          className={`p-2 rounded-lg border text-left transition-all flex items-center gap-2 ${
                            isSelected
                              ? 'bg-emerald-950/80 border-emerald-600 text-emerald-200 font-bold shadow-md'
                              : 'bg-slate-900 border-slate-800 hover:border-slate-700 text-slate-300'
                          }`}
                        >
                          <span className="w-2.5 h-2.5 rounded-full shrink-0" style={{ backgroundColor: cfg.color }} />
                          <span className="text-xs truncate">{cfg.name}</span>
                        </button>
                      );
                    })}
                  </div>
                </div>

                {/* Density Selector */}
                <div>
                  <label className="text-[10px] font-mono uppercase text-slate-400 block mb-1 font-bold">
                    Node Yield / Density
                  </label>
                  <div className="grid grid-cols-4 gap-1 text-[10px] font-mono">
                    {(['rich_mine', 'high', 'medium', 'low'] as const).map(d => (
                      <button
                        key={d}
                        onClick={() => setPendingResourceDensity(d)}
                        className={`py-1.5 rounded text-center capitalize border transition-all ${
                          pendingResourceDensity === d
                            ? 'bg-emerald-950 border-emerald-600 text-emerald-300 font-bold'
                            : 'bg-slate-900 border-slate-800 text-slate-400 hover:text-slate-200'
                        }`}
                      >
                        {d.replace('_', ' ')}
                      </button>
                    ))}
                  </div>
                </div>
              </div>
            )}

            {/* TOOL: RADIUS CREATOR */}
            {activeTool === 'radius' && (
              <div className="space-y-4">
                <div className="flex items-center justify-between">
                  <h3 className="text-xs font-bold text-slate-200 uppercase tracking-wider font-mono">
                    Custom Radius Settings
                  </h3>
                  <span className="text-[10px] text-fuchsia-400 font-mono bg-fuchsia-950/80 px-2 py-0.5 rounded border border-fuchsia-800/50">
                    {pendingRadiusMeters}m
                  </span>
                </div>

                {/* Radius Presets */}
                <div>
                  <label className="text-[10px] font-mono uppercase text-slate-400 block mb-1.5 font-bold">
                    Quick Presets
                  </label>
                  <div className="space-y-1.5">
                    {RADIUS_PRESETS.map(preset => (
                      <button
                        key={preset.id}
                        onClick={() => {
                          setPendingRadiusMeters(preset.radiusMeters);
                          setPendingRadiusColor(preset.color);
                          setPendingRadiusLabel(preset.name);
                        }}
                        className="w-full text-left p-2 rounded-lg bg-slate-900 border border-slate-800 hover:border-fuchsia-500/60 transition-all flex items-center justify-between group"
                      >
                        <div>
                          <p className="text-xs font-semibold text-slate-200 group-hover:text-fuchsia-300">
                            {preset.name}
                          </p>
                          <p className="text-[10px] text-slate-400">{preset.description}</p>
                        </div>
                        <span className="text-xs font-mono font-bold text-fuchsia-400">
                          {preset.radiusMeters}m
                        </span>
                      </button>
                    ))}
                  </div>
                </div>

                {/* Radius Size Slider */}
                <div>
                  <div className="flex justify-between text-xs text-slate-300 mb-1">
                    <span>Radius Distance</span>
                    <span className="font-mono text-fuchsia-400 font-bold">{pendingRadiusMeters} meters</span>
                  </div>
                  <input
                    type="range"
                    min="10"
                    max="400"
                    step="5"
                    value={pendingRadiusMeters}
                    onChange={(e) => setPendingRadiusMeters(Number(e.target.value))}
                    className="w-full accent-fuchsia-500 bg-slate-800 rounded-lg cursor-pointer h-2"
                  />
                </div>

                {/* Color Picker */}
                <div>
                  <label className="text-[10px] font-mono uppercase text-slate-400 block mb-1.5 font-bold">
                    Color Accent
                  </label>
                  <div className="flex items-center gap-2">
                    {COLOR_PALETTE.map(c => (
                      <button
                        key={c}
                        onClick={() => setPendingRadiusColor(c)}
                        style={{ backgroundColor: c }}
                        className={`w-6 h-6 rounded-full border-2 transition-transform ${
                          pendingRadiusColor === c ? 'border-white scale-110 shadow-lg' : 'border-transparent opacity-80 hover:opacity-100'
                        }`}
                      />
                    ))}
                  </div>
                </div>

                {/* Fill Opacity Slider */}
                <div>
                  <div className="flex justify-between text-xs text-slate-300 mb-1">
                    <span>Fill Opacity</span>
                    <span className="font-mono text-slate-400">{Math.round(pendingRadiusOpacity * 100)}%</span>
                  </div>
                  <input
                    type="range"
                    min="0.05"
                    max="0.8"
                    step="0.05"
                    value={pendingRadiusOpacity}
                    onChange={(e) => setPendingRadiusOpacity(Number(e.target.value))}
                    className="w-full accent-slate-400 bg-slate-800 rounded-lg cursor-pointer h-2"
                  />
                </div>
              </div>
            )}

            {/* TOOL: CASTLE BUILD */}
            {activeTool === 'container' && (
              <div className="space-y-4">
                <h3 className="text-xs font-bold text-slate-200 uppercase tracking-wider font-mono">
                  Castle Structure Palette
                </h3>

                {/* Castle Heart Tier Selector */}
                <div className="p-3 rounded-xl bg-slate-900 border border-slate-800 space-y-2">
                  <div className="flex items-center justify-between">
                    <span className="text-xs font-bold text-red-400 flex items-center gap-1.5">
                      <Heart className="w-4 h-4 fill-red-500 text-red-500" />
                      Castle Heart Tier
                    </span>
                    <span className="text-xs font-mono font-bold text-slate-200">Tier {heartTier}</span>
                  </div>
                  <div className="grid grid-cols-5 gap-1 text-xs font-mono">
                    {([1, 2, 3, 4, 5] as const).map(tier => (
                      <button
                        key={tier}
                        onClick={() => setHeartTier(tier)}
                        className={`py-1.5 rounded text-center border font-bold transition-all ${
                          heartTier === tier
                            ? 'bg-red-950 border-red-600 text-red-300'
                            : 'bg-slate-950 border-slate-800 text-slate-400 hover:text-slate-200'
                        }`}
                      >
                        T{tier}
                      </button>
                    ))}
                  </div>
                </div>

                {/* Category Selector */}
                <div className="grid grid-cols-4 gap-1 text-[11px] font-medium bg-slate-900 p-1 rounded-lg">
                  {(['storage', 'workstation', 'furniture', 'defense'] as const).map(cat => (
                    <button
                      key={cat}
                      onClick={() => setContainerCategory(cat)}
                      className={`py-1.5 rounded text-center capitalize transition-all ${
                        containerCategory === cat
                          ? 'bg-blue-950 text-blue-300 font-bold border border-blue-800'
                          : 'text-slate-400 hover:text-slate-200'
                      }`}
                    >
                      {cat}
                    </button>
                  ))}
                </div>

                {/* Items List */}
                <div className="space-y-1.5 max-h-60 overflow-y-auto">
                  {PLACEABLE_CONTAINERS.filter(c => c.category === containerCategory).map(item => {
                    const isSelected = pendingContainer?.id === item.id;
                    return (
                      <div
                        key={item.id}
                        onClick={() => setPendingContainer({ id: item.id, name: item.name, category: item.category, icon: item.icon })}
                        className={`p-2.5 rounded-lg border cursor-pointer transition-all flex items-center justify-between ${
                          isSelected
                            ? 'bg-blue-950/80 border-blue-600 text-blue-200 shadow-md'
                            : 'bg-slate-900 border-slate-800 hover:border-slate-700 text-slate-300'
                        }`}
                      >
                        <div>
                          <p className="text-xs font-bold">{item.name}</p>
                          {item.capacitySlots ? (
                            <p className="text-[10px] text-slate-400">{item.capacitySlots} Storage Slots</p>
                          ) : null}
                        </div>
                        {isSelected && <Check className="w-4 h-4 text-blue-400" />}
                      </div>
                    );
                  })}
                </div>
              </div>
            )}

            {/* TOOL: SELECT & INSPECT */}
            {activeTool === 'select' && (
              <div className="space-y-4 text-xs text-slate-300">
                <div className="p-3 bg-slate-900/80 border border-slate-800 rounded-xl space-y-1.5">
                  <h3 className="font-bold text-slate-100 flex items-center gap-1.5">
                    <MousePointer className="w-4 h-4 text-red-400" /> Select & Inspect Mode
                  </h3>
                  <p className="text-slate-400 text-[11px]">
                    Click any element, patrol route, boss, or radius circle on the map to inspect or drag it.
                  </p>
                </div>

                <div className="space-y-2">
                  <p className="text-[10px] font-mono uppercase text-slate-400 font-bold">
                    Placed Patrol Routes ({patrolRoutes.length})
                  </p>
                  {patrolRoutes.map(p => (
                    <div
                      key={p.id}
                      onClick={() => onSelectItem({ type: 'patrol', data: p })}
                      className="p-2 bg-slate-900 border border-slate-800 rounded-lg flex items-center justify-between cursor-pointer hover:border-amber-500"
                    >
                      <span className="font-bold text-slate-200">{p.name}</span>
                      <span className="text-amber-400 font-mono text-[10px]">{p.enemyType}</span>
                    </div>
                  ))}

                  <p className="text-[10px] font-mono uppercase text-slate-400 font-bold pt-2">
                    Custom Placed Ores ({placedResourceNodes.length})
                  </p>
                  {placedResourceNodes.map(rn => (
                    <div
                      key={rn.id}
                      onClick={() => onSelectItem({ type: 'placed_resource', data: rn })}
                      className="p-2 bg-slate-900 border border-slate-800 rounded-lg flex items-center justify-between cursor-pointer hover:border-emerald-500"
                    >
                      <span className="font-bold text-slate-200">{rn.name}</span>
                      <span className="text-emerald-400 font-mono text-[10px] capitalize">{rn.type}</span>
                    </div>
                  ))}
                </div>
              </div>
            )}
          </div>
        </div>
      )}
    </aside>
  );
};
