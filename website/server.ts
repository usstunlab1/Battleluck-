import express, { Request, Response, NextFunction } from "express";
import path from "path";
import fs from "fs";
import { createServer as createViteServer } from "vite";

const app = express();
const PORT = 3000;

// Environment variable or default V Rising Dedicated Server BepInEx path
const RAW_BEPINEX_ROOT =
  process.env.BEPINEX_ROOT ||
  "C:\\Users\\ahmad\\OneDrive\\Desktop\\DedicatedServerLauncher\\VRisingServer\\BepInEx";

// Determine effective BEPINEX_ROOT path (fallback to local mock directory if physical Windows path does not exist)
let BEPINEX_ROOT = path.resolve(RAW_BEPINEX_ROOT);
if (!fs.existsSync(BEPINEX_ROOT)) {
  BEPINEX_ROOT = path.resolve(process.cwd(), "mock_bepinex", "BepInEx");
}

// Canonical BattleLuck Map Directory
const BATTLELUCK_DIR = path.join(BEPINEX_ROOT, "config", "BattleLuck");
const MAP_STORAGE_DIR = path.join(BATTLELUCK_DIR, "map");

const MARKERS_FILE = path.join(MAP_STORAGE_DIR, "markers.json");
const RESOURCES_FILE = path.join(MAP_STORAGE_DIR, "resources.json");
const ZONES_FILE = path.join(MAP_STORAGE_DIR, "zones.json");
const SCHEMATICS_FILE = path.join(MAP_STORAGE_DIR, "schematics.json");
const SETTINGS_FILE = path.join(MAP_STORAGE_DIR, "map-settings.json");

// Middleware
app.use(express.json({ limit: "15mb" }));

// Security & Path Traversal Guard
function resolveSafePath(userRelativePath: string): string {
  const resolved = path.resolve(BEPINEX_ROOT, userRelativePath);
  if (!resolved.startsWith(BEPINEX_ROOT)) {
    throw new Error("Security Violation: Access outside BepInEx root is forbidden.");
  }
  return resolved;
}

// Ensure Canonical Storage Directory and Files Exist
function initializeStorage() {
  try {
    fs.mkdirSync(MAP_STORAGE_DIR, { recursive: true });

    if (!fs.existsSync(MARKERS_FILE)) writeAtomicJson(MARKERS_FILE, []);
    if (!fs.existsSync(RESOURCES_FILE)) writeAtomicJson(RESOURCES_FILE, []);
    if (!fs.existsSync(ZONES_FILE)) writeAtomicJson(ZONES_FILE, []);
    if (!fs.existsSync(SCHEMATICS_FILE)) writeAtomicJson(SCHEMATICS_FILE, []);
    if (!fs.existsSync(SETTINGS_FILE)) {
      writeAtomicJson(SETTINGS_FILE, {
        serverName: "V Rising Dedicated Server",
        version: "1.0",
        lastUpdated: new Date().toISOString(),
      });
    }
  } catch (err) {
    console.error("Failed to initialize storage directories:", err);
  }
}

// Safe Atomic JSON Writer
function writeAtomicJson(filePath: string, data: any) {
  const dir = path.dirname(filePath);
  fs.mkdirSync(dir, { recursive: true });

  const tempFile = `${filePath}.tmp_${Date.now()}`;
  const serialized = JSON.stringify(data, null, 2);

  // Validate JSON stringification
  JSON.parse(serialized);

  fs.writeFileSync(tempFile, serialized, "utf8");
  fs.renameSync(tempFile, filePath);
}

// Read JSON safely
function readJsonSafe(filePath: string, fallback: any = []): any {
  try {
    if (!fs.existsSync(filePath)) return fallback;
    const raw = fs.readFileSync(filePath, "utf8");
    return JSON.parse(raw);
  } catch (err) {
    console.error(`Error reading ${filePath}:`, err);
    return fallback;
  }
}

// Security middleware for Admin Endpoints
function adminSecurityGuard(req: Request, res: Response, next: NextFunction) {
  const isLocal =
    req.hostname === "localhost" ||
    req.ip === "127.0.0.1" ||
    req.ip === "::1" ||
    req.ip === "::ffff:127.0.0.1";
  
  const token = req.headers["x-admin-token"];
  const configuredToken = process.env.ADMIN_TOKEN;

  if (isLocal || !configuredToken || token === configuredToken) {
    return next();
  }

  res.status(403).json({ error: "Unauthorized: Admin authorization required." });
}

// Scan allowed structured files ONLY (.json, .yaml, .yml, .txt) under BattleLuck
function scanAllowedFiles(dir: string): string[] {
  let results: string[] = [];
  if (!fs.existsSync(dir)) return results;

  const entries = fs.readdirSync(dir, { withFileTypes: true });
  for (const entry of entries) {
    const fullPath = path.join(dir, entry.name);

    if (entry.isDirectory()) {
      results = results.concat(scanAllowedFiles(fullPath));
    } else if (entry.isFile()) {
      const ext = path.extname(entry.name).toLowerCase();
      // Strictly ignore DLL, EXE, cache, log, binary files
      if ([".json", ".yaml", ".yml", ".txt"].includes(ext)) {
        results.push(fullPath);
      }
    }
  }
  return results;
}

// Initialize filesystem on boot
initializeStorage();

// -------------------------------------------------------------
// API ROUTES
// -------------------------------------------------------------

// 1. GET /api/bepinex/status
app.get("/api/bepinex/status", (req: Request, res: Response) => {
  const rootExists = fs.existsSync(BEPINEX_ROOT);
  const battleLuckExists = fs.existsSync(BATTLELUCK_DIR);
  const mapStorageExists = fs.existsSync(MAP_STORAGE_DIR);
  const allowedFiles = battleLuckExists ? scanAllowedFiles(BATTLELUCK_DIR) : [];

  res.json({
    status: "ok",
    configuredRoot: RAW_BEPINEX_ROOT,
    activeRoot: BEPINEX_ROOT,
    rootExists,
    battleLuckFolderExists: battleLuckExists,
    mapStorageExists,
    allowedFilesCount: allowedFiles.length,
    canonicalStoragePath: MAP_STORAGE_DIR,
  });
});

// 2. GET /api/bepinex/catalog
app.get("/api/bepinex/catalog", (req: Request, res: Response) => {
  try {
    const files = scanAllowedFiles(BATTLELUCK_DIR);
    const catalogFiles = files.map((filePath) => {
      const relativePath = path.relative(BEPINEX_ROOT, filePath);
      const stat = fs.statSync(filePath);
      let preview = null;
      let itemCount = 0;

      if (filePath.endsWith(".json")) {
        try {
          const content = readJsonSafe(filePath, null);
          if (Array.isArray(content)) {
            itemCount = content.length;
          } else if (typeof content === "object" && content !== null) {
            itemCount = Object.keys(content).length;
          }
          preview = content;
        } catch {}
      }

      return {
        relativePath,
        sizeBytes: stat.size,
        modifiedAt: stat.mtime.toISOString(),
        itemCount,
      };
    });

    res.json({
      success: true,
      totalFiles: catalogFiles.length,
      files: catalogFiles,
    });
  } catch (err: any) {
    res.status(500).json({ error: err.message || "Failed to scan BepInEx catalog" });
  }
});

// 3. GET /api/map/data
app.get("/api/map/data", (req: Request, res: Response) => {
  try {
    const markers = readJsonSafe(MARKERS_FILE, []);
    const resources = readJsonSafe(RESOURCES_FILE, []);
    const zones = readJsonSafe(ZONES_FILE, []);
    const schematics = readJsonSafe(SCHEMATICS_FILE, []);
    const settings = readJsonSafe(SETTINGS_FILE, {});

    res.json({
      success: true,
      markers,
      resources,
      zones,
      schematics,
      settings,
    });
  } catch (err: any) {
    res.status(500).json({ error: err.message || "Failed to read map data" });
  }
});

// 4. POST /api/bepinex/import (Preview & Merge)
app.post("/api/bepinex/import", adminSecurityGuard, (req: Request, res: Response) => {
  try {
    const { items, sourceFile, overwrite } = req.body;

    if (!items || (!Array.isArray(items) && typeof items !== "object")) {
      return res.status(400).json({ error: "Invalid import payload. Must be array or object." });
    }

    const rawList = Array.isArray(items) ? items : [items];
    const source = sourceFile || "bepinex_import.json";
    const timestamp = new Date().toISOString();

    let addedCount = 0;
    let updatedCount = 0;
    let skippedCount = 0;
    let invalidCount = 0;

    const currentMarkers = readJsonSafe(MARKERS_FILE, []);
    const currentResources = readJsonSafe(RESOURCES_FILE, []);
    const currentZones = readJsonSafe(ZONES_FILE, []);
    const currentSchematics = readJsonSafe(SCHEMATICS_FILE, []);

    rawList.forEach((raw: any, index: number) => {
      if (!raw || typeof raw !== "object") {
        invalidCount++;
        return;
      }

      const id = String(raw.id || raw.ID || `import_${Date.now()}_${index}`);
      const type = String(raw.type || raw.Type || raw.category || "resource").toLowerCase();
      const x = Number(raw.x ?? raw.PosX ?? raw.mapPosition?.x ?? 500);
      const y = Number(raw.y ?? raw.PosY ?? raw.mapPosition?.y ?? 500);
      const name = String(raw.name || raw.Name || raw.PrefabName || `Imported ${type}`);

      const normalizedObj = {
        id,
        type,
        name,
        category: raw.category || raw.Type || "general",
        mapPosition: { x, y },
        worldPosition: raw.worldPosition || { x: raw.PosX ?? 0, y: raw.PosY ?? 0, z: raw.PosZ ?? 0 },
        icon: raw.icon || "MapPin",
        color: raw.color || "#EF4444",
        enabled: raw.enabled !== false,
        source: "import",
        sourceFile: source,
        importedAtUtc: timestamp,
        metadata: raw.metadata || raw,
        radius: raw.radius ? Number(raw.radius) : undefined,
      };

      if (["copper_vein", "iron_mine", "silver_deposit", "quartz", "resource", "ore"].includes(type)) {
        const existingIdx = currentResources.findIndex((r: any) => r.id === id);
        if (existingIdx >= 0) {
          if (overwrite) {
            currentResources[existingIdx] = normalizedObj;
            updatedCount++;
          } else {
            skippedCount++;
          }
        } else {
          currentResources.push(normalizedObj);
          addedCount++;
        }
      } else if (["zone", "radius", "area"].includes(type)) {
        const existingIdx = currentZones.findIndex((z: any) => z.id === id);
        if (existingIdx >= 0) {
          if (overwrite) {
            currentZones[existingIdx] = normalizedObj;
            updatedCount++;
          } else {
            skippedCount++;
          }
        } else {
          currentZones.push(normalizedObj);
          addedCount++;
        }
      } else if (["schematic", "base", "structure"].includes(type)) {
        const existingIdx = currentSchematics.findIndex((s: any) => s.id === id);
        if (existingIdx >= 0) {
          if (overwrite) {
            currentSchematics[existingIdx] = normalizedObj;
            updatedCount++;
          } else {
            skippedCount++;
          }
        } else {
          currentSchematics.push(normalizedObj);
          addedCount++;
        }
      } else {
        const existingIdx = currentMarkers.findIndex((m: any) => m.id === id);
        if (existingIdx >= 0) {
          if (overwrite) {
            currentMarkers[existingIdx] = normalizedObj;
            updatedCount++;
          } else {
            skippedCount++;
          }
        } else {
          currentMarkers.push(normalizedObj);
          addedCount++;
        }
      }
    });

    // Save atomic JSON files
    writeAtomicJson(MARKERS_FILE, currentMarkers);
    writeAtomicJson(RESOURCES_FILE, currentResources);
    writeAtomicJson(ZONES_FILE, currentZones);
    writeAtomicJson(SCHEMATICS_FILE, currentSchematics);

    res.json({
      success: true,
      summary: {
        added: addedCount,
        updated: updatedCount,
        skipped: skippedCount,
        invalid: invalidCount,
        totalProcessed: rawList.length,
      },
    });
  } catch (err: any) {
    res.status(500).json({ error: err.message || "Import execution failed" });
  }
});

// 5. POST /api/map/resources (Create resource)
app.post("/api/map/resources", adminSecurityGuard, (req: Request, res: Response) => {
  try {
    const resource = req.body;
    if (!resource.id) resource.id = `res_${Date.now()}`;

    const resources = readJsonSafe(RESOURCES_FILE, []);
    resources.push(resource);
    writeAtomicJson(RESOURCES_FILE, resources);

    res.json({ success: true, resource });
  } catch (err: any) {
    res.status(500).json({ error: err.message || "Failed to create resource" });
  }
});

// 6. PUT /api/map/resources/:id (Update resource)
app.put("/api/map/resources/:id", adminSecurityGuard, (req: Request, res: Response) => {
  try {
    const { id } = req.params;
    const updated = req.body;

    let resources = readJsonSafe(RESOURCES_FILE, []);
    const idx = resources.findIndex((r: any) => r.id === id);
    if (idx === -1) {
      return res.status(404).json({ error: "Resource not found" });
    }

    resources[idx] = { ...resources[idx], ...updated, id };
    writeAtomicJson(RESOURCES_FILE, resources);

    res.json({ success: true, resource: resources[idx] });
  } catch (err: any) {
    res.status(500).json({ error: err.message || "Failed to update resource" });
  }
});

// 7. DELETE /api/map/resources/:id (Delete resource)
app.delete("/api/map/resources/:id", adminSecurityGuard, (req: Request, res: Response) => {
  try {
    const { id } = req.params;
    let resources = readJsonSafe(RESOURCES_FILE, []);
    resources = resources.filter((r: any) => r.id !== id);
    writeAtomicJson(RESOURCES_FILE, resources);

    res.json({ success: true, deletedId: id });
  } catch (err: any) {
    res.status(500).json({ error: err.message || "Failed to delete resource" });
  }
});

// 8. POST /api/map/zones
app.post("/api/map/zones", adminSecurityGuard, (req: Request, res: Response) => {
  try {
    const zone = req.body;
    if (!zone.id) zone.id = `zone_${Date.now()}`;

    const zones = readJsonSafe(ZONES_FILE, []);
    zones.push(zone);
    writeAtomicJson(ZONES_FILE, zones);

    res.json({ success: true, zone });
  } catch (err: any) {
    res.status(500).json({ error: err.message || "Failed to create zone" });
  }
});

// 9. PUT /api/map/zones/:id
app.put("/api/map/zones/:id", adminSecurityGuard, (req: Request, res: Response) => {
  try {
    const { id } = req.params;
    const updated = req.body;

    let zones = readJsonSafe(ZONES_FILE, []);
    const idx = zones.findIndex((z: any) => z.id === id);
    if (idx === -1) {
      return res.status(404).json({ error: "Zone not found" });
    }

    zones[idx] = { ...zones[idx], ...updated, id };
    writeAtomicJson(ZONES_FILE, zones);

    res.json({ success: true, zone: zones[idx] });
  } catch (err: any) {
    res.status(500).json({ error: err.message || "Failed to update zone" });
  }
});

// 10. DELETE /api/map/zones/:id
app.delete("/api/map/zones/:id", adminSecurityGuard, (req: Request, res: Response) => {
  try {
    const { id } = req.params;
    let zones = readJsonSafe(ZONES_FILE, []);
    zones = zones.filter((z: any) => z.id !== id);
    writeAtomicJson(ZONES_FILE, zones);

    res.json({ success: true, deletedId: id });
  } catch (err: any) {
    res.status(500).json({ error: err.message || "Failed to delete zone" });
  }
});

// 11. POST /api/map/export
app.post("/api/map/export", (req: Request, res: Response) => {
  try {
    const markers = readJsonSafe(MARKERS_FILE, []);
    const resources = readJsonSafe(RESOURCES_FILE, []);
    const zones = readJsonSafe(ZONES_FILE, []);
    const schematics = readJsonSafe(SCHEMATICS_FILE, []);
    const settings = readJsonSafe(SETTINGS_FILE, {});

    res.json({
      exportedAt: new Date().toISOString(),
      bepinexTargetDir: MAP_STORAGE_DIR,
      data: {
        markers,
        resources,
        zones,
        schematics,
        settings,
      },
    });
  } catch (err: any) {
    res.status(500).json({ error: err.message || "Export failed" });
  }
});

// 12. POST /api/map/reload
app.post("/api/map/reload", adminSecurityGuard, (req: Request, res: Response) => {
  try {
    initializeStorage();
    const markers = readJsonSafe(MARKERS_FILE, []);
    const resources = readJsonSafe(RESOURCES_FILE, []);
    const zones = readJsonSafe(ZONES_FILE, []);
    const schematics = readJsonSafe(SCHEMATICS_FILE, []);

    res.json({
      success: true,
      message: "BepInEx map storage reloaded successfully.",
      counts: {
        markers: markers.length,
        resources: resources.length,
        zones: zones.length,
        schematics: schematics.length,
      },
    });
  } catch (err: any) {
    res.status(500).json({ error: err.message || "Reload failed" });
  }
});

// -------------------------------------------------------------
// VITE DEV SERVER OR STATIC PRODUCTION SERVING
// -------------------------------------------------------------
async function startServer() {
  if (process.env.NODE_ENV !== "production") {
    const vite = await createViteServer({
      server: { middlewareMode: true },
      appType: "spa",
    });
    app.use(vite.middlewares);
  } else {
    const distPath = path.join(process.cwd(), "dist");
    app.use(express.static(distPath));
    app.get("*", (req: Request, res: Response) => {
      res.sendFile(path.join(distPath, "index.html"));
    });
  }

  app.listen(PORT, "0.0.0.0", () => {
    console.log(`V Rising Map Admin Server running on http://localhost:${PORT}`);
    console.log(`BepInEx Directory: ${BEPINEX_ROOT}`);
  });
}

startServer();
