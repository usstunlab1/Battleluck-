import React, { useState, useEffect } from 'react';
import { 
  X, 
  Upload, 
  Download, 
  FileCode, 
  Check, 
  Copy, 
  Database, 
  FolderCheck, 
  Sparkles,
  Server,
  RefreshCw,
  AlertCircle,
  CheckCircle2,
  HelpCircle
} from 'lucide-react';
import { 
  PlacedResourceNode, 
  CustomMarker, 
  PatrolRoute, 
  PlacedContainer, 
  PlacedRadius 
} from '../types';

interface BepInExDataModalProps {
  isOpen: boolean;
  onClose: () => void;
  placedResourceNodes: PlacedResourceNode[];
  setPlacedResourceNodes: React.Dispatch<React.SetStateAction<PlacedResourceNode[]>>;
  customMarkers: CustomMarker[];
  setCustomMarkers: React.Dispatch<React.SetStateAction<CustomMarker[]>>;
  patrolRoutes: PatrolRoute[];
  setPatrolRoutes: React.Dispatch<React.SetStateAction<PatrolRoute[]>>;
  placedContainers: PlacedContainer[];
  setPlacedContainers: React.Dispatch<React.SetStateAction<PlacedContainer[]>>;
  placedRadii: PlacedRadius[];
  setPlacedRadii: React.Dispatch<React.SetStateAction<PlacedRadius[]>>;
  onReloadMapData?: () => void;
}

interface ImportPreviewSummary {
  rawItems: any[];
  added: number;
  changed: number;
  skipped: number;
  invalid: number;
  detectedTypes: Record<string, number>;
}

export const BepInExDataModal: React.FC<BepInExDataModalProps> = ({
  isOpen,
  onClose,
  placedResourceNodes,
  setPlacedResourceNodes,
  customMarkers,
  setCustomMarkers,
  patrolRoutes,
  setPatrolRoutes,
  placedContainers,
  setPlacedContainers,
  placedRadii,
  setPlacedRadii,
  onReloadMapData,
}) => {
  const [jsonInput, setJsonInput] = useState('');
  const [importStatus, setImportStatus] = useState<string | null>(null);
  const [copied, setCopied] = useState(false);
  const [activeTab, setActiveTab] = useState<'import' | 'export' | 'status' | 'instructions'>('import');
  
  // Backend status state
  const [serverStatus, setServerStatus] = useState<any>(null);
  const [isSyncing, setIsSyncing] = useState(false);
  const [preview, setPreview] = useState<ImportPreviewSummary | null>(null);
  const [overwriteDuplicates, setOverwriteDuplicates] = useState(true);

  // Fetch BepInEx Server Status on mount/open
  useEffect(() => {
    if (isOpen) {
      fetchServerStatus();
    }
  }, [isOpen]);

  const fetchServerStatus = async () => {
    try {
      const res = await fetch('/api/bepinex/status');
      if (res.ok) {
        const data = await res.json();
        setServerStatus(data);
      }
    } catch {
      // Backend offline or SPA mode fallback
    }
  };

  if (!isOpen) return null;

  // Build complete exportable JSON object for BepInEx server mods
  const exportData = {
    serverSource: serverStatus?.activeRoot || "C:\\Users\\ahmad\\OneDrive\\Desktop\\DedicatedServerLauncher\\VRisingServer\\BepInEx",
    exportedAt: new Date().toISOString(),
    placedResourceNodes,
    customMarkers,
    patrolRoutes,
    placedContainers,
    placedRadii,
  };

  // Phase 1: Validate and generate preview
  const handleGeneratePreview = () => {
    setImportStatus(null);
    if (!jsonInput.trim()) {
      setImportStatus('⚠️ Please paste JSON content to validate and preview.');
      setPreview(null);
      return;
    }

    try {
      const parsed = JSON.parse(jsonInput);
      const items = Array.isArray(parsed) ? parsed : [parsed];

      let added = 0;
      let changed = 0;
      let skipped = 0;
      let invalid = 0;
      const detectedTypes: Record<string, number> = {};

      items.forEach((item: any) => {
        if (!item || typeof item !== 'object') {
          invalid++;
          return;
        }

        const type = String(item.type || item.Type || item.category || 'resource').toLowerCase();
        detectedTypes[type] = (detectedTypes[type] || 0) + 1;

        const id = String(item.id || item.ID || '');
        const exists =
          placedResourceNodes.some(r => r.id === id) ||
          customMarkers.some(m => m.id === id) ||
          patrolRoutes.some(p => p.id === id);

        if (exists) {
          if (overwriteDuplicates) changed++;
          else skipped++;
        } else {
          added++;
        }
      });

      setPreview({
        rawItems: items,
        added,
        changed,
        skipped,
        invalid,
        detectedTypes,
      });

      setImportStatus('✅ Preview generated! Review the breakdown below and confirm injection.');
    } catch (err: any) {
      setPreview(null);
      setImportStatus(`❌ Invalid JSON Syntax: ${err.message}`);
    }
  };

  // Phase 2: Confirm and execute import (writes to state & backend /api/bepinex/import)
  const handleConfirmImport = async () => {
    if (!preview || preview.rawItems.length === 0) return;

    setIsSyncing(true);
    let countResources = 0;
    let countMarkers = 0;
    let countPatrols = 0;

    try {
      // 1. Try sending to backend Express API
      const res = await fetch('/api/bepinex/import', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          items: preview.rawItems,
          sourceFile: 'bepinex_user_import.json',
          overwrite: overwriteDuplicates,
        }),
      });

      if (res.ok) {
        const data = await res.json();
        setImportStatus(`✅ Backend Sync Complete! Saved to ${serverStatus?.canonicalStoragePath || 'BepInEx/config/BattleLuck/map'}. Added: ${data.summary.added}, Updated: ${data.summary.updated}.`);
        if (onReloadMapData) onReloadMapData();
      }
    } catch {
      // Ignore network errors, fall back to React client state update
    }

    // 2. Client-side state integration
    preview.rawItems.forEach((item: any, idx: number) => {
      const x = Number(item.x ?? item.PosX ?? item.mapPosition?.x ?? 500);
      const y = Number(item.y ?? item.PosY ?? item.mapPosition?.y ?? 500);
      const name = String(item.name ?? item.Name ?? item.PrefabName ?? `Imported Item ${idx + 1}`);
      const type = String(item.type ?? item.Type ?? 'copper_vein');

      if (type === 'patrol' || item.points) {
        countPatrols++;
        setPatrolRoutes(prev => [
          ...prev,
          {
            id: item.id || `patrol_bepinex_${Date.now()}_${idx}`,
            name,
            enemyType: item.enemyType ?? 'Bandit Patrol',
            region: item.region ?? 'dunley',
            points: item.points ?? [{ x, y }, { x: x + 20, y: y + 20 }],
            direction: item.direction ?? 'Loop',
            frequency: item.frequency ?? 'Continuous',
            color: item.color ?? '#EF4444',
            isCustom: true,
            notes: 'Imported from BepInEx JSON',
          }
        ]);
      } else if (['copper_vein', 'iron_mine', 'silver_deposit', 'quartz', 'gem_vein', 'blood_stone', 'sacred_grape', 'resource'].includes(type)) {
        countResources++;
        setPlacedResourceNodes(prev => [
          ...prev,
          {
            id: item.id || `res_bepinex_${Date.now()}_${idx}`,
            name,
            type: (type === 'resource' ? 'iron' : type) as any,
            density: item.density ?? item.Density ?? 'high',
            region: item.region ?? 'dunley',
            x,
            y,
            isCustom: true,
          }
        ]);
      } else {
        countMarkers++;
        setCustomMarkers(prev => [
          ...prev,
          {
            id: item.id || `marker_bepinex_${Date.now()}_${idx}`,
            name,
            category: item.category ?? 'stash',
            icon: item.icon ?? 'MapPin',
            color: item.color ?? '#F59E0B',
            x,
            y,
            notes: item.notes ?? 'Imported from BepInEx server JSON',
          }
        ]);
      }
    });

    setIsSyncing(false);
    setPreview(null);
    setJsonInput('');
    fetchServerStatus();
  };

  const handleReloadServerData = async () => {
    setIsSyncing(true);
    try {
      const res = await fetch('/api/map/reload', { method: 'POST' });
      if (res.ok) {
        const data = await res.json();
        setImportStatus(`✅ Reloaded BepInEx data: ${data.counts.markers} markers, ${data.counts.resources} resources, ${data.counts.zones} zones.`);
        if (onReloadMapData) onReloadMapData();
      }
    } catch {
      setImportStatus('ℹ️ Backend server unreachable. Rendered using local map state.');
    } finally {
      setIsSyncing(false);
      fetchServerStatus();
    }
  };

  const handleCopyExport = () => {
    navigator.clipboard.writeText(JSON.stringify(exportData, null, 2));
    setCopied(true);
    setTimeout(() => setCopied(false), 2000);
  };

  const handleDownloadExport = () => {
    const blob = new Blob([JSON.stringify(exportData, null, 2)], { type: 'application/json' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `bepinex_vrising_map_data_${Date.now()}.json`;
    a.click();
    URL.revokeObjectURL(url);
  };

  const handleLoadSampleBepInExData = () => {
    const sampleData = [
      {
        "id": "res_copper_mine_01",
        "PrefabName": "Ore_Copper_Rich_Mine",
        "Name": "Bandit Copper Quarry",
        "Type": "copper_vein",
        "Density": "rich_mine",
        "PosX": 480,
        "PosY": 630,
        "Region": "farbane"
      },
      {
        "id": "res_iron_mine_02",
        "PrefabName": "Ore_Iron_Haunted_Mine",
        "Name": "Haunted Iron Vein",
        "Type": "iron_mine",
        "Density": "rich_mine",
        "PosX": 510,
        "PosY": 490,
        "Region": "dunley"
      },
      {
        "id": "zone_battleluck_event_01",
        "Name": "BattleLuck World Boss Arena",
        "Type": "zone",
        "category": "event",
        "radius": 120,
        "x": 580,
        "y": 420,
        "color": "#EF4444"
      }
    ];

    setJsonInput(JSON.stringify(sampleData, null, 2));
    setImportStatus('ℹ️ Loaded sample BepInEx Server spawner dataset. Click "Generate Preview" below!');
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/80 backdrop-blur-md font-sans">
      <div className="bg-zinc-950 border border-red-900/80 rounded-2xl w-full max-w-3xl max-h-[90vh] flex flex-col shadow-2xl overflow-hidden text-zinc-200">
        
        {/* Header */}
        <div className="p-4 border-b border-red-950 bg-gradient-to-r from-zinc-950 via-red-950/30 to-zinc-950 flex items-center justify-between">
          <div className="flex items-center gap-3">
            <div className="p-2 rounded-xl bg-red-950 border border-red-800 text-red-400">
              <Server className="w-5 h-5" />
            </div>
            <div>
              <div className="flex items-center gap-2">
                <h2 className="text-lg font-black font-serif tracking-wider uppercase text-red-500">
                  BepInEx Server Data Hub
                </h2>
                <span className={`px-2 py-0.5 rounded text-[9px] font-mono font-bold uppercase ${
                  serverStatus?.rootExists ? 'bg-emerald-950 text-emerald-400 border border-emerald-800' : 'bg-amber-950 text-amber-400 border border-amber-800'
                }`}>
                  {serverStatus?.rootExists ? 'ONLINE (EXPRESS BRIDGE)' : 'LOCAL FALLBACK'}
                </span>
              </div>
              <p className="text-[11px] font-mono text-zinc-400 mt-0.5">
                Path: <span className="text-amber-400">{serverStatus?.activeRoot || "C:\\Users\\ahmad\\...\\VRisingServer\\BepInEx"}</span>
              </p>
            </div>
          </div>

          <button
            onClick={onClose}
            className="p-1.5 rounded-lg text-zinc-400 hover:text-white hover:bg-zinc-900 transition-colors"
          >
            <X className="w-5 h-5" />
          </button>
        </div>

        {/* Navigation Tabs */}
        <div className="grid grid-cols-4 bg-zinc-900/80 border-b border-zinc-800 text-xs font-bold font-serif uppercase tracking-wider">
          <button
            onClick={() => setActiveTab('import')}
            className={`py-3 px-1 text-center border-b-2 flex items-center justify-center gap-1.5 transition-colors ${
              activeTab === 'import' ? 'border-red-600 text-red-400 bg-zinc-950' : 'border-transparent text-zinc-400 hover:text-zinc-200'
            }`}
          >
            <Upload className="w-4 h-4" /> Import JSON
          </button>
          <button
            onClick={() => setActiveTab('export')}
            className={`py-3 px-1 text-center border-b-2 flex items-center justify-center gap-1.5 transition-colors ${
              activeTab === 'export' ? 'border-red-600 text-red-400 bg-zinc-950' : 'border-transparent text-zinc-400 hover:text-zinc-200'
            }`}
          >
            <Download className="w-4 h-4" /> Export JSON
          </button>
          <button
            onClick={() => setActiveTab('status')}
            className={`py-3 px-1 text-center border-b-2 flex items-center justify-center gap-1.5 transition-colors ${
              activeTab === 'status' ? 'border-red-600 text-red-400 bg-zinc-950' : 'border-transparent text-zinc-400 hover:text-zinc-200'
            }`}
          >
            <Database className="w-4 h-4" /> Server Catalog
          </button>
          <button
            onClick={() => setActiveTab('instructions')}
            className={`py-3 px-1 text-center border-b-2 flex items-center justify-center gap-1.5 transition-colors ${
              activeTab === 'instructions' ? 'border-red-600 text-red-400 bg-zinc-950' : 'border-transparent text-zinc-400 hover:text-zinc-200'
            }`}
          >
            <FolderCheck className="w-4 h-4" /> Setup Guide
          </button>
        </div>

        {/* Content Body */}
        <div className="p-5 flex-1 overflow-y-auto space-y-4">
          
          {/* TAB 1: IMPORT WITH PREVIEW */}
          {activeTab === 'import' && (
            <div className="space-y-4">
              <div className="flex items-center justify-between">
                <div>
                  <h3 className="text-sm font-bold text-zinc-100 flex items-center gap-2">
                    <Sparkles className="w-4 h-4 text-red-400" />
                    Validated JSON Importer & Injector
                  </h3>
                  <p className="text-xs text-zinc-400 mt-0.5">
                    Supports BattleLuck events, resource nodes, custom markers, and zones.
                  </p>
                </div>

                <button
                  onClick={handleLoadSampleBepInExData}
                  className="px-3 py-1.5 rounded-lg bg-zinc-800 hover:bg-zinc-700 text-xs font-bold text-amber-300 border border-zinc-700 transition-colors shrink-0"
                >
                  ⚡ Sample Data
                </button>
              </div>

              <textarea
                value={jsonInput}
                onChange={(e) => {
                  setJsonInput(e.target.value);
                  setPreview(null);
                }}
                placeholder='Paste BepInEx JSON configuration... e.g. [{"id": "res_01", "type": "iron_mine", "x": 510, "y": 490}]'
                rows={7}
                className="w-full bg-black border border-zinc-800 rounded-xl p-3 text-xs font-mono text-emerald-400 placeholder-zinc-600 focus:border-red-600 focus:outline-none"
              />

              {importStatus && (
                <div className="p-3 rounded-xl bg-zinc-900 border border-zinc-800 text-xs text-zinc-200 font-mono">
                  {importStatus}
                </div>
              )}

              {/* PREVIEW BREAKDOWN CARD */}
              {preview && (
                <div className="p-4 bg-zinc-900/90 border border-red-900/60 rounded-xl space-y-3 font-mono text-xs">
                  <div className="flex items-center justify-between border-b border-zinc-800 pb-2">
                    <span className="font-bold text-red-400 flex items-center gap-2">
                      <CheckCircle2 className="w-4 h-4 text-emerald-400" /> Validation & Merge Preview
                    </span>
                    <label className="flex items-center gap-2 text-[11px] text-zinc-400 cursor-pointer">
                      <input 
                        type="checkbox" 
                        checked={overwriteDuplicates} 
                        onChange={(e) => setOverwriteDuplicates(e.target.checked)}
                        className="rounded accent-red-600"
                      />
                      Overwrite matching IDs
                    </label>
                  </div>

                  <div className="grid grid-cols-4 gap-2 text-center text-[11px]">
                    <div className="p-2 rounded bg-emerald-950/60 border border-emerald-800/80">
                      <span className="text-emerald-400 font-bold block">{preview.added}</span>
                      <span className="text-zinc-400 text-[10px]">ADDED</span>
                    </div>
                    <div className="p-2 rounded bg-amber-950/60 border border-amber-800/80">
                      <span className="text-amber-400 font-bold block">{preview.changed}</span>
                      <span className="text-zinc-400 text-[10px]">CHANGED</span>
                    </div>
                    <div className="p-2 rounded bg-zinc-950 border border-zinc-800">
                      <span className="text-zinc-400 font-bold block">{preview.skipped}</span>
                      <span className="text-zinc-400 text-[10px]">SKIPPED</span>
                    </div>
                    <div className="p-2 rounded bg-red-950/60 border border-red-800/80">
                      <span className="text-red-400 font-bold block">{preview.invalid}</span>
                      <span className="text-zinc-400 text-[10px]">INVALID</span>
                    </div>
                  </div>

                  <div className="text-[11px] text-zinc-400">
                    <p className="font-bold text-zinc-300 mb-1">Detected Types:</p>
                    <div className="flex flex-wrap gap-1.5">
                      {Object.entries(preview.detectedTypes).map(([type, count]) => (
                        <span key={type} className="px-2 py-0.5 rounded bg-zinc-800 border border-zinc-700 text-amber-300">
                          {type}: {count}
                        </span>
                      ))}
                    </div>
                  </div>
                </div>
              )}

              <div className="flex justify-end gap-3 pt-2">
                {!preview ? (
                  <button
                    onClick={handleGeneratePreview}
                    className="px-5 py-2.5 rounded-xl bg-zinc-800 hover:bg-zinc-700 text-zinc-200 font-bold font-serif text-xs uppercase tracking-wider transition-colors flex items-center gap-2"
                  >
                    <CheckCircle2 className="w-4 h-4 text-emerald-400" /> Validate & Preview
                  </button>
                ) : (
                  <button
                    onClick={handleConfirmImport}
                    disabled={isSyncing}
                    className="px-5 py-2.5 rounded-xl bg-gradient-to-r from-red-700 to-red-900 hover:from-red-600 hover:to-red-800 text-white font-bold font-serif text-xs uppercase tracking-wider shadow-lg transition-colors flex items-center gap-2 disabled:opacity-50"
                  >
                    {isSyncing ? <RefreshCw className="w-4 h-4 animate-spin" /> : <Upload className="w-4 h-4" />}
                    Confirm & Inject Into Map
                  </button>
                )}
              </div>
            </div>
          )}

          {/* TAB 2: EXPORT */}
          {activeTab === 'export' && (
            <div className="space-y-4">
              <div>
                <h3 className="text-sm font-bold text-zinc-100 flex items-center gap-2">
                  <FileCode className="w-4 h-4 text-red-400" />
                  Extract Custom Map Data to BepInEx Format
                </h3>
                <p className="text-xs text-zinc-400 mt-0.5">
                  Export all placed items to JSON for placement in <span className="text-amber-400">BepInEx/config/BattleLuck/map/</span>.
                </p>
              </div>

              <div className="relative">
                <textarea
                  readOnly
                  value={JSON.stringify(exportData, null, 2)}
                  rows={10}
                  className="w-full bg-black border border-zinc-800 rounded-xl p-3 text-xs font-mono text-amber-300 focus:outline-none"
                />

                <button
                  onClick={handleCopyExport}
                  className="absolute right-3 top-3 p-2 bg-zinc-900 border border-zinc-700 rounded-lg text-zinc-300 hover:text-white hover:border-red-500 transition-colors text-xs flex items-center gap-1.5 font-bold font-mono"
                >
                  {copied ? <Check className="w-4 h-4 text-emerald-400" /> : <Copy className="w-4 h-4" />}
                  {copied ? 'Copied!' : 'Copy'}
                </button>
              </div>

              <div className="flex items-center justify-between pt-2">
                <p className="text-[11px] text-zinc-500 font-mono">
                  Includes {placedResourceNodes.length} Ores, {customMarkers.length} Markers, {patrolRoutes.length} Patrols
                </p>

                <button
                  onClick={handleDownloadExport}
                  className="px-5 py-2.5 rounded-xl bg-emerald-950 border border-emerald-800 hover:bg-emerald-900 text-emerald-300 font-bold font-serif text-xs uppercase tracking-wider transition-colors flex items-center gap-2"
                >
                  <Download className="w-4 h-4" /> Download .json file
                </button>
              </div>
            </div>
          )}

          {/* TAB 3: SERVER CATALOG & RELOAD */}
          {activeTab === 'status' && (
            <div className="space-y-4">
              <div className="flex items-center justify-between">
                <div>
                  <h3 className="text-sm font-bold text-zinc-100 flex items-center gap-2">
                    <Database className="w-4 h-4 text-red-400" />
                    BepInEx Server Filesystem Directory
                  </h3>
                  <p className="text-xs text-zinc-400 mt-0.5">
                    Scans <span className="text-amber-400">BepInEx/config/BattleLuck/**/*.json</span> safely.
                  </p>
                </div>

                <button
                  onClick={handleReloadServerData}
                  disabled={isSyncing}
                  className="px-3 py-1.5 rounded-lg bg-zinc-800 hover:bg-zinc-700 text-xs font-bold text-amber-300 border border-zinc-700 transition-colors flex items-center gap-1.5"
                >
                  <RefreshCw className={`w-3.5 h-3.5 ${isSyncing ? 'animate-spin' : ''}`} /> Reload Server Data
                </button>
              </div>

              <div className="bg-black border border-zinc-800 rounded-xl p-4 font-mono text-xs space-y-2">
                <div className="flex justify-between border-b border-zinc-800 pb-2">
                  <span className="text-zinc-400">Server Directory Root:</span>
                  <span className="text-amber-400">{serverStatus?.activeRoot}</span>
                </div>
                <div className="flex justify-between border-b border-zinc-800 pb-2">
                  <span className="text-zinc-400">BattleLuck Directory Exists:</span>
                  <span className={serverStatus?.battleLuckFolderExists ? 'text-emerald-400' : 'text-red-400'}>
                    {serverStatus?.battleLuckFolderExists ? 'YES' : 'NO (Will Auto-Create)'}
                  </span>
                </div>
                <div className="flex justify-between border-b border-zinc-800 pb-2">
                  <span className="text-zinc-400">Scanned BepInEx Config Files:</span>
                  <span className="text-emerald-400">{serverStatus?.allowedFilesCount ?? 0} files</span>
                </div>
                <div className="flex justify-between">
                  <span className="text-zinc-400">Canonical Storage Target:</span>
                  <span className="text-zinc-200">{serverStatus?.canonicalStoragePath}</span>
                </div>
              </div>
            </div>
          )}

          {/* TAB 4: SETUP GUIDE */}
          {activeTab === 'instructions' && (
            <div className="space-y-4 text-xs text-zinc-300 leading-relaxed">
              <div className="p-4 bg-zinc-900/90 border border-zinc-800 rounded-xl space-y-2">
                <h4 className="font-bold text-red-400 text-sm flex items-center gap-2">
                  <Database className="w-4 h-4" /> DedicatedServerLauncher BepInEx Integration
                </h4>
                <p>
                  To sync custom spawned resources, boss lairs, and patrol paths with your local V Rising Dedicated Server:
                </p>
                <ol className="list-decimal list-inside space-y-1.5 text-zinc-400 font-mono text-[11px] pt-1">
                  <li>Locate your server folder: <span className="text-amber-300">C:\Users\ahmad\OneDrive\Desktop\DedicatedServerLauncher\VRisingServer\BepInEx</span></li>
                  <li>Copy exported JSON files into <span className="text-amber-300">BepInEx\config\BattleLuck\map\</span></li>
                  <li>In this interactive map, use the <span className="text-red-400">Import JSON</span> tab anytime to visualize modified server world spawns.</li>
                </ol>
              </div>
            </div>
          )}

        </div>

        {/* Footer */}
        <div className="p-4 border-t border-zinc-800 bg-zinc-950 flex items-center justify-between text-xs text-zinc-500 font-mono">
          <span>Server Integration Active</span>
          <button
            onClick={onClose}
            className="px-4 py-2 bg-zinc-900 hover:bg-zinc-800 text-zinc-300 rounded-xl border border-zinc-800 transition-colors"
          >
            Close Window
          </button>
        </div>

      </div>
    </div>
  );
};
