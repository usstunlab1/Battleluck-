import React, { useState, useRef, useEffect, useCallback } from 'react';
import { 
  ZoomIn, 
  ZoomOut, 
  RotateCcw, 
  PlusCircle, 
  Footprints,
  Pickaxe,
  Sparkles,
  ShieldAlert,
  Check,
  X,
  Navigation,
  Compass,
  Zap,
  Flame,
  Skull,
  Hand,
  Pencil
} from 'lucide-react';
import { 
  VBloodBoss, 
  ResourceNode, 
  WaygateNode, 
  CastlePlot, 
  PlacedRadius, 
  PlacedContainer, 
  CustomMarker, 
  RegionId,
  PatrolRoute,
  PatrolPoint,
  PlacedResourceNode,
  ResourceType,
  ActiveTool
} from '../types';
import { REGIONS, V_BLOOD_BOSSES, RESOURCE_NODES, WAYGATES, CASTLE_PLOTS, RESOURCE_TYPES_CONFIG } from '../data/vrisingData';

interface MapCanvasProps {
  activeTool: ActiveTool;
  searchQuery: string;
  isNightMode: boolean;
  showGrid: boolean;
  
  // Layers visibility
  layerBosses: boolean;
  layerResources: boolean;
  layerWaygates: boolean;
  layerPlots: boolean;
  layerContainers: boolean;
  layerRadii: boolean;
  layerPatrols: boolean;
  selectedRegion: RegionId | 'all';

  // Placed State
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

  // Radius creation parameters
  pendingRadiusMeters: number;
  pendingRadiusColor: string;
  pendingRadiusOpacity: number;
  pendingRadiusLabel: string;
  pendingRadiusBorderStyle: 'solid' | 'dashed' | 'pulse';

  // Container placement parameter
  pendingContainer: { id: string; name: string; category: any; icon: string } | null;

  // Patrol Route creation state
  pendingPatrolPoints: PatrolPoint[];
  setPendingPatrolPoints: React.Dispatch<React.SetStateAction<PatrolPoint[]>>;
  pendingEnemyType: string;
  pendingPatrolDirection: 'Clockwise' | 'Counter-Clockwise' | 'Bidirectional' | 'Loop';
  pendingPatrolFrequency: 'Continuous' | 'Every 2 Mins' | 'Every 5 Mins' | 'Night Only';
  pendingPatrolColor: string;
  onFinishPatrolRoute: () => void;

  // Resource placement state
  pendingResourceType: ResourceType;
  pendingResourceDensity: 'low' | 'medium' | 'high' | 'rich_mine';

  // Selected item handler
  onSelectItem: (item: { type: string; data: any } | null) => void;
  selectedItem: { type: string; data: any } | null;

  // Cursor tracking
  setCursorPos: (pos: { x: number; y: number }) => void;

  // Checklist props
  foundItemIds?: Set<string>;
  toggleFound?: (id: string) => void;
}

export const MapCanvas: React.FC<MapCanvasProps> = ({
  activeTool,
  searchQuery,
  isNightMode,
  showGrid,
  layerBosses,
  layerResources,
  layerWaygates,
  layerPlots,
  layerContainers,
  layerRadii,
  layerPatrols,
  selectedRegion,
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
  pendingRadiusMeters,
  pendingRadiusColor,
  pendingRadiusOpacity,
  pendingRadiusLabel,
  pendingRadiusBorderStyle,
  pendingContainer,
  pendingPatrolPoints,
  setPendingPatrolPoints,
  pendingEnemyType,
  pendingPatrolDirection,
  pendingPatrolFrequency,
  pendingPatrolColor,
  onFinishPatrolRoute,
  pendingResourceType,
  pendingResourceDensity,
  onSelectItem,
  selectedItem,
  setCursorPos,
  foundItemIds = new Set(),
  toggleFound,
}) => {
  const containerRef = useRef<HTMLDivElement>(null);

  // Zoom & Pan transformation state
  const [zoom, setZoom] = useState<number>(1);
  const [pan, setPan] = useState<{ x: number; y: number }>({ x: 0, y: 0 });
  const [isPanning, setIsPanning] = useState<boolean>(false);
  const [startPan, setStartPan] = useState<{ x: number; y: number }>({ x: 0, y: 0 });

  // Dragging placed items state
  const [draggingItem, setDraggingItem] = useState<{ type: string; id: string } | null>(null);

  // Hovered item for tooltip
  const [hoveredNode, setHoveredNode] = useState<{ type: string; data: any; px: number; py: number } | null>(null);

  // Handle Zooming via Mouse Wheel
  const handleWheel = (e: React.WheelEvent) => {
    e.preventDefault();
    const zoomFactor = e.deltaY < 0 ? 1.15 : 0.85;
    setZoom(prevZoom => Math.min(Math.max(0.6, prevZoom * zoomFactor), 4.5));
  };

  // Handle Pan start
  const handleMouseDown = (e: React.MouseEvent) => {
    if (e.button === 0 && (activeTool === 'select' || e.target === containerRef.current || (e.target as HTMLElement).tagName === 'svg')) {
      setIsPanning(true);
      setStartPan({ x: e.clientX - pan.x, y: e.clientY - pan.y });
    }
  };

  const handleMouseMove = (e: React.MouseEvent) => {
    if (!containerRef.current) return;
    const rect = containerRef.current.getBoundingClientRect();
    const clickX = (e.clientX - rect.left - pan.x) / zoom;
    const clickY = (e.clientY - rect.top - pan.y) / zoom;
    
    const mapX = Math.round(Math.max(0, Math.min(1000, (clickX / rect.width) * 1000)));
    const mapY = Math.round(Math.max(0, Math.min(1000, (clickY / rect.height) * 1000)));

    setCursorPos({ x: mapX, y: mapY });

    if (isPanning) {
      setPan({ x: e.clientX - startPan.x, y: e.clientY - startPan.y });
      return;
    }

    if (draggingItem) {
      if (draggingItem.type === 'radius') {
        setPlacedRadii(prev => prev.map(r => r.id === draggingItem.id ? { ...r, x: mapX, y: mapY } : r));
      } else if (draggingItem.type === 'container') {
        setPlacedContainers(prev => prev.map(c => c.id === draggingItem.id ? { ...c, x: mapX, y: mapY } : c));
      } else if (draggingItem.type === 'marker') {
        setCustomMarkers(prev => prev.map(m => m.id === draggingItem.id ? { ...m, x: mapX, y: mapY } : m));
      } else if (draggingItem.type === 'resource_place') {
        setPlacedResourceNodes(prev => prev.map(rn => rn.id === draggingItem.id ? { ...rn, x: mapX, y: mapY } : rn));
      }
    }
  };

  const handleMouseUp = () => {
    setIsPanning(false);
    setDraggingItem(null);
  };

  // Handle Map Click for placement & tools
  const handleMapClick = (e: React.MouseEvent) => {
    if (isPanning) return;
    if (!containerRef.current) return;

    const rect = containerRef.current.getBoundingClientRect();
    const clickX = (e.clientX - rect.left - pan.x) / zoom;
    const clickY = (e.clientY - rect.top - pan.y) / zoom;
    
    const mapX = Math.round(Math.max(10, Math.min(990, (clickX / rect.width) * 1000)));
    const mapY = Math.round(Math.max(10, Math.min(990, (clickY / rect.height) * 1000)));

    if (activeTool === 'radius') {
      const newRadius: PlacedRadius = {
        id: `rad_${Date.now()}_${Math.random().toString(36).substring(2, 5)}`,
        name: pendingRadiusLabel || `Radius (${pendingRadiusMeters}m)`,
        x: mapX,
        y: mapY,
        radiusMeters: pendingRadiusMeters,
        color: pendingRadiusColor,
        opacity: pendingRadiusOpacity,
        borderStyle: pendingRadiusBorderStyle,
        label: pendingRadiusLabel || `${pendingRadiusMeters}m Zone`,
      };
      setPlacedRadii(prev => [...prev, newRadius]);
      onSelectItem({ type: 'radius', data: newRadius });
    } else if (activeTool === 'container' && pendingContainer) {
      const newContainer: PlacedContainer = {
        id: `cont_${Date.now()}_${Math.random().toString(36).substring(2, 5)}`,
        name: pendingContainer.name,
        category: pendingContainer.category,
        icon: pendingContainer.icon,
        x: mapX,
        y: mapY,
        notes: 'Placed Base Structure',
      };
      setPlacedContainers(prev => [...prev, newContainer]);
      onSelectItem({ type: 'container', data: newContainer });
    } else if (activeTool === 'marker') {
      const newMarker: CustomMarker = {
        id: `mark_${Date.now()}_${Math.random().toString(36).substring(2, 5)}`,
        name: 'Strategic Pin',
        x: mapX,
        y: mapY,
        icon: 'Pin',
        color: '#EF4444',
        notes: 'Strategic planning note',
        category: 'pin',
      };
      setCustomMarkers(prev => [...prev, newMarker]);
      onSelectItem({ type: 'marker', data: newMarker });
    } else if (activeTool === 'patrol') {
      // Add a point to current drawing patrol route
      setPendingPatrolPoints(prev => [...prev, { x: mapX, y: mapY }]);
    } else if (activeTool === 'resource_place') {
      const resConfig = RESOURCE_TYPES_CONFIG[pendingResourceType] || { name: 'Resource Node', color: '#F59E0B' };
      const newResourceNode: PlacedResourceNode = {
        id: `placed_res_${Date.now()}_${Math.random().toString(36).substring(2, 5)}`,
        name: `${resConfig.name} Node`,
        type: pendingResourceType,
        x: mapX,
        y: mapY,
        density: pendingResourceDensity,
        isCustom: true,
        notes: 'User placed resource node',
      };
      setPlacedResourceNodes(prev => [...prev, newResourceNode]);
      onSelectItem({ type: 'placed_resource', data: newResourceNode });
    } else if (activeTool === 'select') {
      if ((e.target as HTMLElement).tagName === 'svg' || e.target === containerRef.current) {
        onSelectItem(null);
      }
    }
  };

  const handleResetView = () => {
    setZoom(1);
    setPan({ x: 0, y: 0 });
  };

  // Filtered Lists
  const filteredBosses = V_BLOOD_BOSSES.filter(b => {
    if (selectedRegion !== 'all' && b.region !== selectedRegion) return false;
    if (!searchQuery) return true;
    const q = searchQuery.toLowerCase();
    return b.name.toLowerCase().includes(q) || b.rewards.some(r => r.toLowerCase().includes(q)) || b.bloodType.toLowerCase().includes(q);
  });

  const filteredResources = RESOURCE_NODES.filter(r => {
    if (selectedRegion !== 'all' && r.region !== selectedRegion) return false;
    if (!searchQuery) return true;
    const q = searchQuery.toLowerCase();
    return r.name.toLowerCase().includes(q) || r.type.toLowerCase().includes(q);
  });

  const filteredPatrols = patrolRoutes.filter(p => {
    if (selectedRegion !== 'all' && p.region !== selectedRegion) return false;
    if (!searchQuery) return true;
    const q = searchQuery.toLowerCase();
    return p.name.toLowerCase().includes(q) || p.enemyType.toLowerCase().includes(q);
  });

  const filteredPlots = CASTLE_PLOTS.filter(p => {
    if (selectedRegion !== 'all' && p.region !== selectedRegion) return false;
    if (!searchQuery) return true;
    const q = searchQuery.toLowerCase();
    return p.name.toLowerCase().includes(q) || p.resourceProximity.some(r => r.toLowerCase().includes(q));
  });

  return (
    <div className="relative w-full h-full bg-[#070a11] overflow-hidden select-none flex-1 flex flex-col">
      {/* Top Floating Active Mode Banner */}
      {activeTool !== 'select' && (
        <div className="absolute top-4 left-1/2 -translate-x-1/2 z-30 bg-slate-950/95 text-slate-100 border border-slate-700/80 px-5 py-2.5 rounded-full shadow-2xl flex items-center gap-3 backdrop-blur-xl animate-in fade-in slide-in-from-top-4 duration-200">
          <PlusCircle className="w-4 h-4 text-red-400" />
          <span className="text-xs font-semibold font-sans tracking-wide">
            {activeTool === 'radius' && `Click map to place ${pendingRadiusMeters}m Radius Circle`}
            {activeTool === 'container' && `Click map to place ${pendingContainer?.name || 'Base Structure'}`}
            {activeTool === 'marker' && 'Click map to place Strategic Marker'}
            {activeTool === 'patrol' && `Drawing Route (${pendingPatrolPoints.length} points). Click map to add waypoints.`}
            {activeTool === 'resource_place' && `Click map to place ${RESOURCE_TYPES_CONFIG[pendingResourceType]?.name || 'Resource Node'}`}
          </span>

          {activeTool === 'patrol' && (
            <div className="flex items-center gap-1 ml-2">
              <button
                onClick={(e) => {
                  e.stopPropagation();
                  onFinishPatrolRoute();
                }}
                disabled={pendingPatrolPoints.length < 2}
                className="px-2.5 py-1 rounded-full bg-emerald-700 hover:bg-emerald-600 disabled:opacity-40 text-white text-[11px] font-mono font-bold flex items-center gap-1 transition-all"
              >
                <Check className="w-3 h-3" /> Finish Route
              </button>
              <button
                onClick={(e) => {
                  e.stopPropagation();
                  setPendingPatrolPoints([]);
                }}
                className="px-2 py-1 rounded-full bg-slate-800 hover:bg-red-950 text-slate-300 hover:text-red-300 text-[11px] font-mono transition-all"
              >
                Clear
              </button>
            </div>
          )}
        </div>
      )}

      {/* Main Viewport Canvas */}
      <div 
        ref={containerRef}
        onWheel={handleWheel}
        onMouseDown={handleMouseDown}
        onMouseMove={handleMouseMove}
        onMouseUp={handleMouseUp}
        onClick={handleMapClick}
        className={`w-full h-full cursor-${isPanning ? 'grabbing' : activeTool === 'select' ? 'grab' : 'crosshair'} relative`}
      >
        <div 
          className="w-full h-full absolute top-0 left-0 transition-transform duration-75 ease-out origin-top-left"
          style={{
            transform: `translate(${pan.x}px, ${pan.y}px) scale(${zoom})`,
          }}
        >
          {/* SVG Map Graphic Canvas */}
          <svg 
            viewBox="0 0 1000 1000" 
            className="w-full h-full max-w-full max-h-full drop-shadow-2xl"
            style={{ width: '100%', height: '100%' }}
          >
            <defs>
              {/* Region Gradients */}
              <linearGradient id="grad-farbane" x1="0%" y1="0%" x2="100%" y2="100%">
                <stop offset="0%" stopColor="#064E3B" stopOpacity="0.8" />
                <stop offset="100%" stopColor="#022C22" stopOpacity="0.9" />
              </linearGradient>
              <linearGradient id="grad-dunley" x1="0%" y1="0%" x2="100%" y2="100%">
                <stop offset="0%" stopColor="#78350F" stopOpacity="0.75" />
                <stop offset="100%" stopColor="#451A03" stopOpacity="0.95" />
              </linearGradient>
              <linearGradient id="grad-silverlight" x1="0%" y1="0%" x2="100%" y2="100%">
                <stop offset="0%" stopColor="#1E293B" stopOpacity="0.85" />
                <stop offset="100%" stopColor="#0F172A" stopOpacity="0.95" />
              </linearGradient>
              <linearGradient id="grad-cursed" x1="0%" y1="0%" x2="100%" y2="100%">
                <stop offset="0%" stopColor="#581C87" stopOpacity="0.85" />
                <stop offset="100%" stopColor="#3B0764" stopOpacity="0.95" />
              </linearGradient>
              <linearGradient id="grad-gloomrot" x1="0%" y1="0%" x2="100%" y2="100%">
                <stop offset="0%" stopColor="#065F46" stopOpacity="0.85" />
                <stop offset="100%" stopColor="#042F2E" stopOpacity="0.95" />
              </linearGradient>
              <linearGradient id="grad-mortium" x1="0%" y1="0%" x2="100%" y2="100%">
                <stop offset="0%" stopColor="#7F1D1D" stopOpacity="0.85" />
                <stop offset="100%" stopColor="#450A0A" stopOpacity="0.95" />
              </linearGradient>

              {/* Grid pattern */}
              <pattern id="grid-pattern" width="50" height="50" patternUnits="userSpaceOnUse">
                <path d="M 50 0 L 0 0 0 50" fill="none" stroke="#334155" strokeWidth="0.5" strokeOpacity="0.3" />
              </pattern>

              {/* Day/Night solar rays filter */}
              <linearGradient id="solar-rays" x1="0%" y1="0%" x2="100%" y2="100%">
                <stop offset="0%" stopColor="#F59E0B" stopOpacity="0.18" />
                <stop offset="50%" stopColor="#EF4444" stopOpacity="0.06" />
                <stop offset="100%" stopColor="#000000" stopOpacity="0" />
              </linearGradient>

              {/* Arrowhead Marker for Patrol Routes */}
              <marker id="arrowhead" viewBox="0 0 10 10" refX="5" refY="5" markerWidth="6" markerHeight="6" orient="auto-start-reverse">
                <path d="M 0 0 L 10 5 L 0 10 z" fill="#F59E0B" />
              </marker>
            </defs>

            {/* Base Dark Terrain */}
            <rect width="1000" height="1000" fill="#05080E" />

            {/* Region Territorials */}
            {/* Farbane Woods */}
            <path 
              d="M 150 650 Q 300 580 500 620 Q 750 600 850 720 Q 880 880 750 950 Q 500 980 200 950 Q 120 820 150 650 Z" 
              fill="url(#grad-farbane)" 
              stroke="#059669" 
              strokeWidth="2" 
              strokeDasharray="4 2"
            />
            {/* Dunley Farmlands */}
            <path 
              d="M 220 370 Q 450 330 780 360 Q 820 520 780 600 Q 500 620 220 580 Q 180 480 220 370 Z" 
              fill="url(#grad-dunley)" 
              stroke="#D97706" 
              strokeWidth="2"
              strokeDasharray="4 2"
            />
            {/* Silverlight Hills */}
            <path 
              d="M 50 280 Q 200 240 320 280 Q 300 500 220 560 Q 80 520 50 280 Z" 
              fill="url(#grad-silverlight)" 
              stroke="#3B82F6" 
              strokeWidth="2"
              strokeDasharray="4 2"
            />
            {/* Cursed Forest */}
            <path 
              d="M 680 120 Q 880 90 980 150 Q 990 320 850 360 Q 720 350 680 120 Z" 
              fill="url(#grad-cursed)" 
              stroke="#9333EA" 
              strokeWidth="2"
              strokeDasharray="4 2"
            />
            {/* Gloomrot */}
            <path 
              d="M 100 60 Q 380 40 450 100 Q 420 320 300 340 Q 120 320 100 60 Z" 
              fill="url(#grad-gloomrot)" 
              stroke="#10B981" 
              strokeWidth="2"
              strokeDasharray="4 2"
            />
            {/* Ruins of Mortium */}
            <path 
              d="M 780 370 Q 980 360 980 580 Q 880 620 780 580 Z" 
              fill="url(#grad-mortium)" 
              stroke="#DC2626" 
              strokeWidth="2"
              strokeDasharray="4 2"
            />

            {/* Region Names (hidden - only our events shown) */}
            

            {/* Grid Overlay */}
            {showGrid && (
              <rect width="1000" height="1000" fill="url(#grid-pattern)" className="pointer-events-none" />
            )}

            {/* Day/Night Solar hazard */}
            {!isNightMode && (
              <rect width="1000" height="1000" fill="url(#solar-rays)" className="pointer-events-none" />
            )}

            {/* Cave Passage Links */}
            {layerWaygates && WAYGATES.filter(w => w.type === 'cave' && w.targetCaveX).map(c => (
              <g key={`cave_link_${c.id}`}>
                <line 
                  x1={c.x} 
                  y1={c.y} 
                  x2={c.targetCaveX!} 
                  y2={c.targetCaveY!} 
                  stroke="#A855F7" 
                  strokeWidth="2" 
                  strokeDasharray="6 4" 
                  strokeOpacity="0.75"
                />
              </g>
            ))}

            {/* LAYER 0: ENEMY PATROL ROUTES (LINES & DIRECTIONAL ARROWS) */}
            {layerPatrols && filteredPatrols.map(patrol => {
              if (!patrol.points || patrol.points.length < 2) return null;
              const isSelected = selectedItem?.type === 'patrol' && selectedItem.data.id === patrol.id;
              const pointsString = patrol.points.map(p => `${p.x},${p.y}`).join(' ');

              return (
                <g 
                  key={patrol.id}
                  onClick={(e) => {
                    e.stopPropagation();
                    onSelectItem({ type: 'patrol', data: patrol });
                  }}
                  onMouseEnter={() => setHoveredNode({ type: 'Enemy Patrol Route', data: patrol, px: patrol.points[0].x, py: patrol.points[0].y })}
                  onMouseLeave={() => setHoveredNode(null)}
                  className="cursor-pointer group"
                >
                  {/* Background Glow Line */}
                  <polyline
                    points={pointsString}
                    fill="none"
                    stroke={patrol.color}
                    strokeWidth={isSelected ? 6 : 4}
                    strokeOpacity={isSelected ? 0.9 : 0.6}
                    strokeLinecap="round"
                    strokeLinejoin="round"
                    strokeDasharray={patrol.direction === 'Bidirectional' ? '8 4' : 'none'}
                    className="transition-all"
                  />

                  {/* Waypoint Nodes along patrol line */}
                  {patrol.points.map((pt, idx) => (
                    <circle
                      key={`pt_${idx}`}
                      cx={pt.x}
                      cy={pt.y}
                      r={idx === 0 ? 5 : 3.5}
                      fill={idx === 0 ? '#FFFFFF' : patrol.color}
                      stroke="#0F172A"
                      strokeWidth="1.5"
                    />
                  ))}

                  {/* Label at mid point */}
                  {patrol.points.length >= 2 && (
                    <text
                      x={patrol.points[Math.floor(patrol.points.length / 2)].x}
                      y={patrol.points[Math.floor(patrol.points.length / 2)].y - 8}
                      fill="#FFFFFF"
                      fontSize="9"
                      fontWeight="800"
                      textAnchor="middle"
                      className="pointer-events-none drop-shadow-[0_1px_4px_rgba(0,0,0,1)] font-mono"
                    >
                      ⚠️ {patrol.enemyType} ({patrol.frequency})
                    </text>
                  )}
                </g>
              );
            })}

            {/* DRAWING PATROL ROUTE PREVIEW (When user is placing points) */}
            {activeTool === 'patrol' && pendingPatrolPoints.length > 0 && (
              <g>
                <polyline
                  points={pendingPatrolPoints.map(p => `${p.x},${p.y}`).join(' ')}
                  fill="none"
                  stroke={pendingPatrolColor}
                  strokeWidth="3.5"
                  strokeDasharray="5 3"
                  className="animate-pulse"
                />
                {pendingPatrolPoints.map((pt, i) => (
                  <circle
                    key={`draw_pt_${i}`}
                    cx={pt.x}
                    cy={pt.y}
                    r="4.5"
                    fill="#38BDF8"
                    stroke="#FFFFFF"
                    strokeWidth="2"
                  />
                ))}
              </g>
            )}

            {/* LAYER 1: Custom Placed Radii Circles */}
            {layerRadii && placedRadii.map(r => {
              const isSelected = selectedItem?.type === 'radius' && selectedItem.data.id === r.id;
              return (
                <g 
                  key={r.id}
                  onClick={(e) => {
                    e.stopPropagation();
                    onSelectItem({ type: 'radius', data: r });
                  }}
                  onMouseDown={(e) => {
                    if (activeTool === 'select') {
                      e.stopPropagation();
                      setDraggingItem({ type: 'radius', id: r.id });
                    }
                  }}
                  className="cursor-pointer group"
                >
                  <circle
                    cx={r.x}
                    cy={r.y}
                    r={r.radiusMeters}
                    fill={r.color}
                    fillOpacity={r.opacity}
                    stroke={isSelected ? '#FFFFFF' : r.color}
                    strokeWidth={isSelected ? 3 : 2}
                    strokeDasharray={r.borderStyle === 'dashed' ? '6 4' : 'none'}
                    className={r.borderStyle === 'pulse' ? 'animate-pulse' : ''}
                  />
                  <circle cx={r.x} cy={r.y} r="4" fill={r.color} stroke="#FFFFFF" strokeWidth="1.5" />
                  <text
                    x={r.x}
                    y={r.y - r.radiusMeters - 6}
                    fill="#FFFFFF"
                    fontSize="11"
                    fontWeight="700"
                    textAnchor="middle"
                    className="drop-shadow-[0_2px_4px_rgba(0,0,0,0.9)] font-mono pointer-events-none"
                  >
                    {r.label || `${r.radiusMeters}m`}
                  </text>
                </g>
              );
            })}

            {/* LAYER 2: Castle Territory Plots */}
            {layerPlots && filteredPlots.map(plot => {
              const isSelected = selectedItem?.type === 'plot' && selectedItem.data.id === plot.id;
              return (
                <g 
                  key={plot.id}
                  onClick={(e) => {
                    e.stopPropagation();
                    onSelectItem({ type: 'plot', data: plot });
                  }}
                  className="cursor-pointer hover:scale-105 transition-transform"
                >
                  <rect
                    x={plot.x - plot.tileSize / 4}
                    y={plot.y - plot.tileSize / 4}
                    width={plot.tileSize / 2}
                    height={plot.tileSize / 2}
                    rx="8"
                    fill={plot.chokePoint ? '#7F1D1D' : '#1E293B'}
                    fillOpacity="0.5"
                    stroke={isSelected ? '#F59E0B' : plot.chokePoint ? '#EF4444' : '#64748B'}
                    strokeWidth={isSelected ? 3 : 1.5}
                    strokeDasharray="4 2"
                  />
                  <text
                    x={plot.x}
                    y={plot.y + 4}
                    fill="#E2E8F0"
                    fontSize="9"
                    fontWeight="700"
                    textAnchor="middle"
                    className="pointer-events-none drop-shadow font-mono"
                  >
                    {plot.name} ({plot.tileSize}t)
                  </text>
                </g>
              );
            })}

            {/* LAYER 3: Resource Nodes (Static Map Nodes + Custom Placed Nodes) */}
            {layerResources && (
              <>
                {/* Static Map Resources */}
                {filteredResources.map(res => {
                  const isSelected = selectedItem?.type === 'resource' && selectedItem.data.id === res.id;
                  const isFound = foundItemIds.has(res.id);
                  const cfg = RESOURCE_TYPES_CONFIG[res.type] || { color: '#F59E0B' };
                  return (
                    <g 
                      key={res.id}
                      onClick={(e) => {
                        e.stopPropagation();
                        onSelectItem({ type: 'resource', data: res });
                      }}
                      onMouseEnter={() => setHoveredNode({ type: 'Resource Node', data: res, px: res.x, py: res.y })}
                      onMouseLeave={() => setHoveredNode(null)}
                      className={`cursor-pointer hover:scale-125 transition-all ${isFound ? 'opacity-50 hover:opacity-100' : ''}`}
                    >
                      <circle
                        cx={res.x}
                        cy={res.y}
                        r={res.density === 'rich_mine' ? 9 : 6}
                        fill={isFound ? '#10B981' : cfg.color}
                        fillOpacity="0.9"
                        stroke={isSelected ? '#FFFFFF' : '#0F172A'}
                        strokeWidth="1.5"
                      />
                      {res.density === 'rich_mine' && (
                        <circle cx={res.x} cy={res.y} r="12" fill="none" stroke={cfg.color} strokeWidth="1" strokeDasharray="2 2" className="animate-spin" />
                      )}
                      {isFound && (
                        <circle cx={res.x + 5} cy={res.y - 5} r="3.5" fill="#10B981" stroke="#064E3B" strokeWidth="0.8" />
                      )}
                    </g>
                  );
                })}

                {/* Custom User-Placed Resource Nodes */}
                {placedResourceNodes.map(pRes => {
                  const isSelected = selectedItem?.type === 'placed_resource' && selectedItem.data.id === pRes.id;
                  const cfg = RESOURCE_TYPES_CONFIG[pRes.type] || { name: 'Resource Node', color: '#F59E0B' };

                  return (
                    <g
                      key={pRes.id}
                      onClick={(e) => {
                        e.stopPropagation();
                        onSelectItem({ type: 'placed_resource', data: pRes });
                      }}
                      onMouseDown={(e) => {
                        if (activeTool === 'select') {
                          e.stopPropagation();
                          setDraggingItem({ type: 'resource_place', id: pRes.id });
                        }
                      }}
                      onMouseEnter={() => setHoveredNode({ type: 'Placed Resource Node', data: pRes, px: pRes.x, py: pRes.y })}
                      onMouseLeave={() => setHoveredNode(null)}
                      className="cursor-pointer hover:scale-125 transition-transform"
                    >
                      <circle
                        cx={pRes.x}
                        cy={pRes.y}
                        r="8"
                        fill={cfg.color}
                        stroke={isSelected ? '#FFFFFF' : '#022C22'}
                        strokeWidth="2"
                        className="drop-shadow-[0_0_8px_rgba(245,158,11,0.6)]"
                      />
                      <text
                        x={pRes.x}
                        y={pRes.y - 11}
                        fill="#FFFFFF"
                        fontSize="9"
                        fontWeight="800"
                        textAnchor="middle"
                        className="pointer-events-none font-mono drop-shadow-[0_1px_3px_rgba(0,0,0,1)]"
                      >
                        {pRes.name}
                      </text>
                    </g>
                  );
                })}
              </>
            )}

            {/* LAYER 4: Waygates & Caves */}
            {layerWaygates && WAYGATES.map(wg => {
              const isSelected = selectedItem?.type === 'waygate' && selectedItem.data.id === wg.id;
              return (
                <g
                  key={wg.id}
                  onClick={(e) => {
                    e.stopPropagation();
                    onSelectItem({ type: 'waygate', data: wg });
                  }}
                  className="cursor-pointer hover:scale-125 transition-transform"
                >
                  <polygon
                    points={`${wg.x},${wg.y - 10} ${wg.x + 8},${wg.y + 6} ${wg.x - 8},${wg.y + 6}`}
                    fill={wg.type === 'waygate' ? '#3B82F6' : '#A855F7'}
                    stroke={isSelected ? '#FFFFFF' : '#0F172A'}
                    strokeWidth="1.5"
                  />
                </g>
              );
            })}

            {/* LAYER 5: V Blood Bosses */}
            {layerBosses && filteredBosses.map(boss => {
              const isSelected = selectedItem?.type === 'boss' && selectedItem.data.id === boss.id;
              const isFound = foundItemIds.has(boss.id);
              return (
                <g
                  key={boss.id}
                  onClick={(e) => {
                    e.stopPropagation();
                    onSelectItem({ type: 'boss', data: boss });
                  }}
                  onMouseEnter={() => setHoveredNode({ type: 'V Blood Boss', data: boss, px: boss.x, py: boss.y })}
                  onMouseLeave={() => setHoveredNode(null)}
                  className={`cursor-pointer hover:scale-125 transition-all group ${isFound ? 'opacity-60 hover:opacity-100' : ''}`}
                >
                  <circle
                    cx={boss.x}
                    cy={boss.y}
                    r="10"
                    fill={isFound ? '#065F46' : '#7F1D1D'}
                    stroke={isSelected ? '#FFFFFF' : isFound ? '#10B981' : '#EF4444'}
                    strokeWidth="2"
                    className="drop-shadow-[0_0_8px_rgba(239,68,68,0.8)]"
                  />
                  <text
                    x={boss.x}
                    y={boss.y + 3}
                    fill="#FFFFFF"
                    fontSize="9"
                    fontWeight="900"
                    textAnchor="middle"
                    className="pointer-events-none font-mono"
                  >
                    {isFound ? '✓' : boss.level}
                  </text>
                  <text
                    x={boss.x}
                    y={boss.y - 13}
                    fill={isFound ? '#A7F3D0' : '#FCA5A5'}
                    fontSize="9"
                    fontWeight="700"
                    textAnchor="middle"
                    className="pointer-events-none drop-shadow-[0_1px_3px_rgba(0,0,0,1)] font-sans"
                  >
                    {boss.name}
                  </text>
                </g>
              );
            })}

            {/* LAYER 6: Placed Containers & Workstations */}
            {layerContainers && placedContainers.map(cont => {
              const isSelected = selectedItem?.type === 'container' && selectedItem.data.id === cont.id;
              return (
                <g
                  key={cont.id}
                  onClick={(e) => {
                    e.stopPropagation();
                    onSelectItem({ type: 'container', data: cont });
                  }}
                  onMouseDown={(e) => {
                    if (activeTool === 'select') {
                      e.stopPropagation();
                      setDraggingItem({ type: 'container', id: cont.id });
                    }
                  }}
                  className="cursor-pointer hover:scale-125 transition-transform"
                >
                  <rect
                    x={cont.x - 7}
                    y={cont.y - 7}
                    width="14"
                    height="14"
                    rx="3"
                    fill="#059669"
                    stroke={isSelected ? '#FFFFFF' : '#022C22'}
                    strokeWidth="1.5"
                  />
                  <text
                    x={cont.x}
                    y={cont.y + 3}
                    fill="#FFFFFF"
                    fontSize="8"
                    fontWeight="800"
                    textAnchor="middle"
                    className="pointer-events-none font-mono"
                  >
                    {cont.name.charAt(0)}
                  </text>
                </g>
              );
            })}

            {/* LAYER 7: Custom User Pins */}
            {customMarkers.map(marker => {
              const isSelected = selectedItem?.type === 'marker' && selectedItem.data.id === marker.id;
              return (
                <g
                  key={marker.id}
                  onClick={(e) => {
                    e.stopPropagation();
                    onSelectItem({ type: 'marker', data: marker });
                  }}
                  onMouseDown={(e) => {
                    if (activeTool === 'select') {
                      e.stopPropagation();
                      setDraggingItem({ type: 'marker', id: marker.id });
                    }
                  }}
                  className="cursor-pointer hover:scale-125 transition-transform"
                >
                  <circle
                    cx={marker.x}
                    cy={marker.y}
                    r="7"
                    fill={marker.color}
                    stroke={isSelected ? '#FFFFFF' : '#0F172A'}
                    strokeWidth="1.5"
                  />
                  <text
                    x={marker.x}
                    y={marker.y - 10}
                    fill="#FFFFFF"
                    fontSize="9"
                    fontWeight="700"
                    textAnchor="middle"
                    className="pointer-events-none font-mono drop-shadow-[0_1px_3px_rgba(0,0,0,1)]"
                  >
                    {marker.name}
                  </text>
                </g>
              );
            })}
          </svg>
        </div>
      </div>

      {/* Hover Info Tooltip Popup */}
      {hoveredNode && (
        <div 
          className="absolute z-40 bg-slate-950/95 border border-slate-700/80 rounded-xl p-3 shadow-2xl text-xs text-slate-100 backdrop-blur-md pointer-events-auto max-w-xs transition-opacity duration-150 animate-in fade-in zoom-in-95"
          style={{
            left: Math.min(window.innerWidth - 260, (hoveredNode.px / 1000) * (containerRef.current?.clientWidth || 800) + pan.x + 20),
            top: Math.max(20, (hoveredNode.py / 1000) * (containerRef.current?.clientHeight || 800) + pan.y - 40),
          }}
        >
          <div className="flex items-center justify-between gap-2 border-b border-slate-800 pb-1.5 mb-1.5">
            <span className="font-bold text-red-400">{hoveredNode.data.name}</span>
            <span className="text-[10px] font-mono px-1.5 py-0.5 rounded bg-slate-800 text-slate-300">
              {hoveredNode.type}
            </span>
          </div>

          {hoveredNode.type === 'Enemy Patrol Route' && (
            <div className="space-y-1 font-sans">
              <p className="text-slate-300"><span className="text-slate-500">Enemy Unit:</span> {hoveredNode.data.enemyType}</p>
              <p className="text-slate-300"><span className="text-slate-500">Frequency:</span> <span className="text-amber-400 font-bold">{hoveredNode.data.frequency}</span></p>
              <p className="text-slate-300"><span className="text-slate-500">Direction:</span> {hoveredNode.data.direction}</p>
              {hoveredNode.data.notes && <p className="text-[11px] text-slate-400 italic pt-1">{hoveredNode.data.notes}</p>}
            </div>
          )}

          {hoveredNode.type === 'V Blood Boss' && (
            <div className="space-y-1">
              <p className="text-slate-300"><span className="text-slate-500">Level:</span> Lv {hoveredNode.data.level}</p>
              <p className="text-slate-300"><span className="text-slate-500">Blood Type:</span> {hoveredNode.data.bloodType}</p>
              <p className="text-slate-400 text-[11px] italic">{hoveredNode.data.description}</p>
            </div>
          )}

          {(hoveredNode.type === 'Resource Node' || hoveredNode.type === 'Placed Resource Node') && (
            <div>
              <p className="text-slate-300">Type: <span className="capitalize font-semibold text-amber-300">{hoveredNode.data.type.replace('_', ' ')}</span></p>
              <p className="text-slate-400">Density: <span className="capitalize">{hoveredNode.data.density.replace('_', ' ')}</span></p>
            </div>
          )}

          {/* MapGenie Mark as Found Button inside Tooltip */}
          {toggleFound && hoveredNode.data.id && (
            <div className="pt-2 border-t border-slate-800/80 mt-2 flex items-center justify-between">
              <span className="text-[10px] text-slate-400 font-mono">
                {foundItemIds.has(hoveredNode.data.id) ? 'Status: ✓ FOUND' : 'Status: NOT FOUND'}
              </span>
              <button
                onClick={(e) => {
                  e.stopPropagation();
                  toggleFound(hoveredNode.data.id);
                }}
                className={`px-2 py-1 rounded text-[10px] font-bold transition-colors ${
                  foundItemIds.has(hoveredNode.data.id)
                    ? 'bg-emerald-950 text-emerald-400 border border-emerald-800 hover:bg-emerald-900'
                    : 'bg-slate-800 text-slate-300 border border-slate-700 hover:bg-slate-700 hover:text-white'
                }`}
              >
                {foundItemIds.has(hoveredNode.data.id) ? '✓ Completed' : '+ Mark Found'}
              </button>
            </div>
          )}
        </div>
      )}

      {/* MapGenie Floating Vertical Right Toolstack */}
      <div className="absolute right-4 bottom-20 z-30 flex flex-col items-center bg-black/90 border border-zinc-700/80 rounded-lg overflow-hidden shadow-2xl backdrop-blur-md divide-y divide-zinc-800">
        <button
          onClick={() => {}}
          title="Pan / Select Map Tool"
          className="p-3 hover:bg-zinc-800 text-zinc-300 hover:text-white transition-colors"
        >
          <Hand className="w-5 h-5 text-zinc-200" />
        </button>
        <button
          onClick={() => {}}
          title="Add Custom Marker / Note"
          className="p-3 hover:bg-zinc-800 text-zinc-300 hover:text-white transition-colors"
        >
          <Pencil className="w-5 h-5 text-zinc-200" />
        </button>
        <button
          onClick={() => setZoom(prev => Math.min(4.5, prev * 1.25))}
          title="Zoom In (+)"
          className="p-3 hover:bg-zinc-800 text-zinc-300 hover:text-white transition-colors text-lg font-bold font-mono"
        >
          +
        </button>
        <button
          onClick={() => setZoom(prev => Math.max(0.6, prev / 1.25))}
          title="Zoom Out (-)"
          className="p-3 hover:bg-zinc-800 text-zinc-300 hover:text-white transition-colors text-lg font-bold font-mono"
        >
          −
        </button>
      </div>

      {/* Floating Bottom Left Map Controls */}
      <div className="absolute bottom-4 left-4 z-20 flex items-center gap-1.5 bg-slate-950/90 border border-slate-800/80 p-1.5 rounded-xl shadow-2xl backdrop-blur-md">
        <button
          onClick={() => setZoom(prev => Math.min(4.5, prev * 1.25))}
          title="Zoom In"
          className="p-2 rounded-lg hover:bg-slate-800 text-slate-300 hover:text-white transition-colors"
        >
          <ZoomIn className="w-4 h-4" />
        </button>
        <button
          onClick={() => setZoom(prev => Math.max(0.6, prev / 1.25))}
          title="Zoom Out"
          className="p-2 rounded-lg hover:bg-slate-800 text-slate-300 hover:text-white transition-colors"
        >
          <ZoomOut className="w-4 h-4" />
        </button>
        <button
          onClick={handleResetView}
          title="Reset Zoom & View"
          className="p-2 rounded-lg hover:bg-slate-800 text-slate-300 hover:text-white transition-colors"
        >
          <RotateCcw className="w-4 h-4" />
        </button>
        <div className="h-4 w-px bg-slate-800 mx-1" />
        <span className="text-xs font-mono text-slate-400 px-2">
          {Math.round(zoom * 100)}%
        </span>
      </div>

      {/* Map Legend removed - only our events shown */}
      
    </div>
  );
};
