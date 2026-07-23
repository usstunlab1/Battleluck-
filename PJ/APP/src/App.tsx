import React, { useState, useEffect } from 'react';
import { Navbar } from './components/Navbar';
import { LeftSidebar } from './components/Sidebar/LeftSidebar';
import { RightSidebar } from './components/Sidebar/RightSidebar';
import { MapCanvas } from './components/MapCanvas';
import { Footer } from './components/Footer';
import { ShareModal } from './components/Modals/ShareModal';
import { SaveModal } from './components/Modals/SaveModal';
import { FeedbackModal } from './components/Modals/FeedbackModal';
import { GuideModal } from './components/Modals/GuideModal';
import { BepInExDataModal } from './components/BepInExDataModal';

import { 
  RegionId, 
  CastleHeartTier, 
  PlacedRadius, 
  PlacedContainer, 
  CustomMarker, 
  CastleBuildPlan,
  PatrolRoute,
  PatrolPoint,
  PlacedResourceNode,
  ResourceType,
  ActiveTool
} from './types';

import { DEFAULT_PATROL_ROUTES, RESOURCE_TYPES_CONFIG } from './data/vrisingData';
import { decodeMapState } from './utils/mapUtils';

export default function App() {
  // Global & Navbar state
  const [searchQuery, setSearchQuery] = useState('');
  const [isNightMode, setIsNightMode] = useState(true);
  const [showGrid, setShowGrid] = useState(true);
  const [embedMode, setEmbedMode] = useState(false);
  
  // Region & Layer visibility
  const [selectedRegion, setSelectedRegion] = useState<RegionId | 'all'>('all');
  const [layerBosses, setLayerBosses] = useState(false);
  const [layerResources, setLayerResources] = useState(false);
  const [layerWaygates, setLayerWaygates] = useState(false);
  const [layerPlots, setLayerPlots] = useState(false);
  const [layerContainers, setLayerContainers] = useState(true);
  const [layerRadii, setLayerRadii] = useState(true);
  const [layerPatrols, setLayerPatrols] = useState(true);

  // Sidebar Open States
  const [isLeftOpen, setIsLeftOpen] = useState(true);
  const [isRightOpen, setIsRightOpen] = useState(true);

  // Active Tool Selection
  const [activeTool, setActiveTool] = useState<ActiveTool>('select');

  // Pending Radius Settings
  const [pendingRadiusMeters, setPendingRadiusMeters] = useState(60);
  const [pendingRadiusColor, setPendingRadiusColor] = useState('#EF4444');
  const [pendingRadiusOpacity, setPendingRadiusOpacity] = useState(0.25);
  const [pendingRadiusLabel, setPendingRadiusLabel] = useState('Castle Claim Zone');
  const [pendingRadiusBorderStyle, setPendingRadiusBorderStyle] = useState<'solid' | 'dashed' | 'pulse'>('solid');

  // Pending Castle/Container Settings
  const [heartTier, setHeartTier] = useState<CastleHeartTier>(2);
  const [pendingContainer, setPendingContainer] = useState<{ id: string; name: string; category: any; icon: string } | null>(null);

  // Enemy Patrol Route Creator state
  const [patrolRoutes, setPatrolRoutes] = useState<PatrolRoute[]>(DEFAULT_PATROL_ROUTES);
  const [pendingPatrolPoints, setPendingPatrolPoints] = useState<PatrolPoint[]>([]);
  const [pendingEnemyType, setPendingEnemyType] = useState('Dunley Militia Patrol');
  const [pendingPatrolDirection, setPendingPatrolDirection] = useState<'Clockwise' | 'Counter-Clockwise' | 'Bidirectional' | 'Loop'>('Bidirectional');
  const [pendingPatrolFrequency, setPendingPatrolFrequency] = useState<'Continuous' | 'Every 2 Mins' | 'Every 5 Mins' | 'Night Only'>('Every 2 Mins');
  const [pendingPatrolColor, setPendingPatrolColor] = useState('#F59E0B');

  // Custom Resource Node Placement state
  const [placedResourceNodes, setPlacedResourceNodes] = useState<PlacedResourceNode[]>([
    {
      id: 'custom_iron_mine_1',
      name: 'Custom Iron Mine Hub',
      type: 'iron',
      x: 560,
      y: 430,
      density: 'rich_mine',
      isCustom: true,
      notes: 'High yield iron mining location',
    }
  ]);
  const [pendingResourceType, setPendingResourceType] = useState<ResourceType>('copper');
  const [pendingResourceDensity, setPendingResourceDensity] = useState<'low' | 'medium' | 'high' | 'rich_mine'>('high');

  // Map Placed Items State
  const [placedRadii, setPlacedRadii] = useState<PlacedRadius[]>([
    {
      id: 'default_radius_1',
      name: 'Central Dunley Castle Radius',
      x: 520,
      y: 500,
      radiusMeters: 80,
      color: '#EF4444',
      opacity: 0.25,
      borderStyle: 'solid',
    }
  ]);
  const [placedContainers, setPlacedContainers] = useState<PlacedContainer[]>([
    {
      id: 'default_cont_1',
      x: 515,
      y: 495,
      name: 'Vampire Lockbox',
      category: 'storage',
      icon: 'box',
    }
  ]);
  const [customMarkers, setCustomMarkers] = useState<CustomMarker[]>([]);

  // Selected item on map inspector
  const [selectedItem, setSelectedItem] = useState<{ type: string; data: any } | null>(null);

  // Modal Visibility
  const [isShareOpen, setIsShareOpen] = useState(false);
  const [isSaveOpen, setIsSaveOpen] = useState(false);
  const [isFeedbackOpen, setIsFeedbackOpen] = useState(false);
  const [isGuideOpen, setIsGuideOpen] = useState(false);
  const [isBepInExOpen, setIsBepInExOpen] = useState(false);

  // Cursor position tracking
  const [cursorPos, setCursorPos] = useState({ x: 500, y: 500 });

  // Found / Checklist items tracking (MapGenie style)
  const [foundItemIds, setFoundItemIds] = useState<Set<string>>(() => {
    try {
      const saved = localStorage.getItem('vrising_found_items');
      return saved ? new Set(JSON.parse(saved)) : new Set();
    } catch (e) {
      return new Set();
    }
  });

  useEffect(() => {
    try {
      localStorage.setItem('vrising_found_items', JSON.stringify(Array.from(foundItemIds)));
    } catch (e) {
      // ignore
    }
  }, [foundItemIds]);

  const handleFetchBackendMapData = async () => {
    try {
      const res = await fetch('/api/map/data');
      if (!res.ok) return;
      const data = await res.json();
      if (!data.success) return;

      if (Array.isArray(data.markers) && data.markers.length > 0) {
        setCustomMarkers(prev => {
          const existingIds = new Set(prev.map(m => m.id));
          const newMarkers = data.markers
            .filter((m: any) => !existingIds.has(m.id))
            .map((m: any) => ({
              id: m.id,
              name: m.name || 'Marker',
              category: m.category || 'stash',
              icon: m.icon || 'MapPin',
              color: m.color || '#F59E0B',
              x: m.mapPosition?.x ?? m.x ?? 500,
              y: m.mapPosition?.y ?? m.y ?? 500,
              notes: m.metadata?.notes || 'Loaded from BepInEx server',
            }));
          return [...prev, ...newMarkers];
        });
      }

      if (Array.isArray(data.resources) && data.resources.length > 0) {
        setPlacedResourceNodes(prev => {
          const existingIds = new Set(prev.map(r => r.id));
          const newNodes = data.resources
            .filter((r: any) => !existingIds.has(r.id))
            .map((r: any) => ({
              id: r.id,
              name: r.name || 'Resource Node',
              type: (r.type === 'resource' ? 'iron' : r.type) as any,
              density: r.density || 'high',
              region: r.region || 'dunley',
              x: r.mapPosition?.x ?? r.x ?? 500,
              y: r.mapPosition?.y ?? r.y ?? 500,
              isCustom: true,
            }));
          return [...prev, ...newNodes];
        });
      }
    } catch (err) {
      // Offline fallback
    }
  };

  useEffect(() => {
    handleFetchBackendMapData();
  }, []);

  const toggleFound = (id: string) => {
    setFoundItemIds(prev => {
      const next = new Set(prev);
      if (next.has(id)) {
        next.delete(id);
      } else {
        next.add(id);
      }
      return next;
    });
  };

  const handleShowAll = () => {
    setLayerBosses(true);
    setLayerResources(true);
    setLayerWaygates(true);
    setLayerPlots(true);
    setLayerContainers(true);
    setLayerRadii(true);
    setLayerPatrols(true);
  };

  const handleHideAll = () => {
    setLayerBosses(false);
    setLayerResources(false);
    setLayerWaygates(false);
    setLayerPlots(false);
    setLayerContainers(false);
    setLayerRadii(false);
    setLayerPatrols(false);
  };

  const handleResetFound = () => {
    if (window.confirm('Reset all checked "Found" locations?')) {
      setFoundItemIds(new Set());
    }
  };

  // On initial mount, attempt to parse hash state if present
  useEffect(() => {
    const hash = window.location.hash;
    if (hash && hash.includes('#plan=')) {
      const encoded = hash.split('#plan=')[1];
      const decoded = decodeMapState(encoded);
      if (decoded) {
        setHeartTier(decoded.heartTier || 2);
        if (decoded.radii) setPlacedRadii(decoded.radii);
        if (decoded.containers) setPlacedContainers(decoded.containers);
        if (decoded.customMarkers) setCustomMarkers(decoded.customMarkers);
        if (decoded.patrolRoutes) setPatrolRoutes(decoded.patrolRoutes);
        if (decoded.placedResourceNodes) setPlacedResourceNodes(decoded.placedResourceNodes);
      }
    }
  }, []);

  const handleFinishPatrolRoute = () => {
    if (pendingPatrolPoints.length < 2) return;
    const newPatrol: PatrolRoute = {
      id: `patrol_${Date.now()}_${Math.random().toString(36).substring(2, 5)}`,
      name: `${pendingEnemyType} Route`,
      enemyType: pendingEnemyType,
      region: selectedRegion === 'all' ? 'dunley' : selectedRegion,
      points: pendingPatrolPoints,
      direction: pendingPatrolDirection,
      frequency: pendingPatrolFrequency,
      color: pendingPatrolColor,
      isCustom: true,
      notes: 'User defined enemy patrol route',
    };
    setPatrolRoutes(prev => [...prev, newPatrol]);
    setPendingPatrolPoints([]);
    setSelectedItem({ type: 'patrol', data: newPatrol });
  };

  const handleClearPlan = () => {
    if (window.confirm("Are you sure you want to clear all custom placed radius circles, containers, patrol routes, and custom resource nodes?")) {
      setPlacedRadii([]);
      setPlacedContainers([]);
      setCustomMarkers([]);
      setPatrolRoutes([]);
      setPlacedResourceNodes([]);
      setSelectedItem(null);
    }
  };

  const handleLoadPlan = (plan: CastleBuildPlan) => {
    setHeartTier(plan.heartTier || 2);
    setPlacedRadii(plan.radii || []);
    setPlacedContainers(plan.containers || []);
    setCustomMarkers(plan.customMarkers || []);
    setPatrolRoutes(plan.patrolRoutes || DEFAULT_PATROL_ROUTES);
    setPlacedResourceNodes(plan.placedResourceNodes || []);
    setSelectedItem(null);
  };

  const currentPlan: CastleBuildPlan = {
    heartTier,
    rooms: [],
    radii: placedRadii,
    containers: placedContainers,
    customMarkers: customMarkers,
    patrolRoutes: patrolRoutes,
    placedResourceNodes: placedResourceNodes,
  };

  return (
    <div className={`h-screen w-screen flex flex-col overflow-hidden bg-slate-950 text-slate-100 font-sans antialiased ${isNightMode ? 'dark' : ''}`}>
      {/* Top Navbar Header */}
      <Navbar
        searchQuery={searchQuery}
        setSearchQuery={setSearchQuery}
        isNightMode={isNightMode}
        setIsNightMode={setIsNightMode}
        heartTier={heartTier}
        radiiCount={placedRadii.length}
        containerCount={placedContainers.length}
        onOpenShareModal={() => setIsShareOpen(true)}
        onOpenFeedbackModal={() => setIsFeedbackOpen(true)}
        onOpenGuideModal={() => setIsGuideOpen(true)}
        onOpenSaveModal={() => setIsSaveOpen(true)}
        onClearPlan={handleClearPlan}
        showGrid={showGrid}
        setShowGrid={setShowGrid}
        embedMode={embedMode}
        setEmbedMode={setEmbedMode}
        onOpenBepInExModal={() => setIsBepInExOpen(true)}
      />

      {/* Main Workspace Layout */}
      <div className="flex-1 flex overflow-hidden relative">
        {/* Left Sidebar: Layers & Region Explorer */}
        <LeftSidebar
          isOpen={isLeftOpen}
          setIsOpen={setIsLeftOpen}
          selectedRegion={selectedRegion}
          setSelectedRegion={setSelectedRegion}
          layerBosses={layerBosses}
          setLayerBosses={setLayerBosses}
          layerResources={layerResources}
          setLayerResources={setLayerResources}
          layerWaygates={layerWaygates}
          setLayerWaygates={setLayerWaygates}
          layerPlots={layerPlots}
          setLayerPlots={setLayerPlots}
          layerContainers={layerContainers}
          setLayerContainers={setLayerContainers}
          layerRadii={layerRadii}
          setLayerRadii={setLayerRadii}
          layerPatrols={layerPatrols}
          setLayerPatrols={setLayerPatrols}
          foundItemIds={foundItemIds}
          toggleFound={toggleFound}
          onShowAll={handleShowAll}
          onHideAll={handleHideAll}
          onResetFound={handleResetFound}
          onSelectItem={setSelectedItem}
        />

        {/* Center Interactive Map Canvas or Live Embed */}
        <main className="flex-1 h-full relative overflow-hidden bg-slate-950">
          {embedMode ? (
              <div className="w-full h-full relative bg-zinc-950">
              <iframe 
                src="https://mapgenie.io/v-rising/maps/vardoran?embed=light&x=-0.8450344820944906&y=0.6482687402716323&zoom=10" 
                className="w-full h-full border-0"
                title="V Rising Map"
                style={{ position: 'relative', width: '100%', height: '100%' }}
              />
            </div>
          ) : (
            <MapCanvas
              searchQuery={searchQuery}
              selectedRegion={selectedRegion}
              layerBosses={layerBosses}
              layerResources={layerResources}
              layerWaygates={layerWaygates}
              layerPlots={layerPlots}
              layerContainers={layerContainers}
              layerRadii={layerRadii}
              layerPatrols={layerPatrols}
              foundItemIds={foundItemIds}
              toggleFound={toggleFound}
              activeTool={activeTool}
              pendingRadiusMeters={pendingRadiusMeters}
              pendingRadiusColor={pendingRadiusColor}
              pendingRadiusOpacity={pendingRadiusOpacity}
              pendingRadiusLabel={pendingRadiusLabel}
              pendingRadiusBorderStyle={pendingRadiusBorderStyle}
              pendingContainer={pendingContainer}
              pendingPatrolPoints={pendingPatrolPoints}
              setPendingPatrolPoints={setPendingPatrolPoints}
              pendingEnemyType={pendingEnemyType}
              pendingPatrolDirection={pendingPatrolDirection}
              pendingPatrolFrequency={pendingPatrolFrequency}
              pendingPatrolColor={pendingPatrolColor}
              onFinishPatrolRoute={handleFinishPatrolRoute}
              pendingResourceType={pendingResourceType}
              pendingResourceDensity={pendingResourceDensity}
              placedRadii={placedRadii}
              setPlacedRadii={setPlacedRadii}
              placedContainers={placedContainers}
              setPlacedContainers={setPlacedContainers}
              customMarkers={customMarkers}
              setCustomMarkers={setCustomMarkers}
              patrolRoutes={patrolRoutes}
              setPatrolRoutes={setPatrolRoutes}
              placedResourceNodes={placedResourceNodes}
              setPlacedResourceNodes={setPlacedResourceNodes}
              selectedItem={selectedItem}
              onSelectItem={setSelectedItem}
              showGrid={showGrid}
              isNightMode={isNightMode}
              setCursorPos={setCursorPos}
            />
          )}
        </main>

        {/* Right Sidebar: Patrol Creator, Resource Palette & Castle Inspector */}
        <RightSidebar
          isOpen={isRightOpen}
          setIsOpen={setIsRightOpen}
          activeTool={activeTool}
          setActiveTool={setActiveTool}
          pendingRadiusMeters={pendingRadiusMeters}
          setPendingRadiusMeters={setPendingRadiusMeters}
          pendingRadiusColor={pendingRadiusColor}
          setPendingRadiusColor={setPendingRadiusColor}
          pendingRadiusOpacity={pendingRadiusOpacity}
          setPendingRadiusOpacity={setPendingRadiusOpacity}
          pendingRadiusLabel={pendingRadiusLabel}
          setPendingRadiusLabel={setPendingRadiusLabel}
          pendingRadiusBorderStyle={pendingRadiusBorderStyle}
          setPendingRadiusBorderStyle={setPendingRadiusBorderStyle}
          heartTier={heartTier}
          setHeartTier={setHeartTier}
          pendingContainer={pendingContainer}
          setPendingContainer={setPendingContainer}
          pendingPatrolPoints={pendingPatrolPoints}
          setPendingPatrolPoints={setPendingPatrolPoints}
          pendingEnemyType={pendingEnemyType}
          setPendingEnemyType={setPendingEnemyType}
          pendingPatrolDirection={pendingPatrolDirection}
          setPendingPatrolDirection={setPendingPatrolDirection}
          pendingPatrolFrequency={pendingPatrolFrequency}
          setPendingPatrolFrequency={setPendingPatrolFrequency}
          pendingPatrolColor={pendingPatrolColor}
          setPendingPatrolColor={setPendingPatrolColor}
          onFinishPatrolRoute={handleFinishPatrolRoute}
          pendingResourceType={pendingResourceType}
          setPendingResourceType={setPendingResourceType}
          pendingResourceDensity={pendingResourceDensity}
          setPendingResourceDensity={setPendingResourceDensity}
          selectedItem={selectedItem}
          onSelectItem={setSelectedItem}
          placedRadii={placedRadii}
          setPlacedRadii={setPlacedRadii}
          placedContainers={placedContainers}
          setPlacedContainers={setPlacedContainers}
          customMarkers={customMarkers}
          setCustomMarkers={setCustomMarkers}
          patrolRoutes={patrolRoutes}
          setPatrolRoutes={setPatrolRoutes}
          placedResourceNodes={placedResourceNodes}
          setPlacedResourceNodes={setPlacedResourceNodes}
          onOpenBepInExModal={() => setIsBepInExOpen(true)}
        />
      </div>

      {/* Bottom Status Footer */}
      <Footer 
        onOpenFeedbackModal={() => setIsFeedbackOpen(true)} 
        cursorPos={cursorPos}
      />

      {/* Modals */}
      <ShareModal
        isOpen={isShareOpen}
        onClose={() => setIsShareOpen(false)}
        plan={currentPlan}
      />

      <SaveModal
        isOpen={isSaveOpen}
        onClose={() => setIsSaveOpen(false)}
        currentPlan={currentPlan}
        onLoadPlan={handleLoadPlan}
      />

      <FeedbackModal
        isOpen={isFeedbackOpen}
        onClose={() => setIsFeedbackOpen(false)}
      />

      <GuideModal
        isOpen={isGuideOpen}
        onClose={() => setIsGuideOpen(false)}
      />

      <BepInExDataModal
        isOpen={isBepInExOpen}
        onClose={() => setIsBepInExOpen(false)}
        placedResourceNodes={placedResourceNodes}
        setPlacedResourceNodes={setPlacedResourceNodes}
        customMarkers={customMarkers}
        setCustomMarkers={setCustomMarkers}
        patrolRoutes={patrolRoutes}
        setPatrolRoutes={setPatrolRoutes}
        placedContainers={placedContainers}
        setPlacedContainers={setPlacedContainers}
        placedRadii={placedRadii}
        setPlacedRadii={setPlacedRadii}
        onReloadMapData={handleFetchBackendMapData}
      />
    </div>
  );
}
