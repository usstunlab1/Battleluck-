import { PlacedRadius, PlacedContainer, CustomMarker, CastleBuildPlan, SavedPlan } from '../types';

export const LOCAL_STORAGE_KEY = 'v_rising_map_plans_v1';

export function calculateDistance(x1: number, y1: number, x2: number, y2: number): number {
  return Math.sqrt((x2 - x1) ** 2 + (y2 - y1) ** 2);
}

// Convert 0-1000 coordinate space to map canvas dimensions
export function mapCoordToPixel(coord: { x: number; y: number }, size: { width: number; height: number }) {
  return {
    px: (coord.x / 1000) * size.width,
    py: (coord.y / 1000) * size.height,
  };
}

export function pixelToMapCoord(pixel: { px: number; py: number }, size: { width: number; height: number }) {
  return {
    x: Math.round(Math.max(0, Math.min(1000, (pixel.px / size.width) * 1000))),
    y: Math.round(Math.max(0, Math.min(1000, (pixel.py / size.height) * 1000))),
  };
}

// Shareable state compressor/decompressor
export function encodeMapState(plan: CastleBuildPlan): string {
  try {
    const compact = {
      ht: plan.heartTier,
      pt: plan.selectedPlotId || '',
      rm: plan.rooms,
      ct: plan.containers.map(c => ({ i: c.id, n: c.name, cat: c.category, ic: c.icon, x: c.x, y: c.y, nt: c.notes || '' })),
      rd: plan.radii.map(r => ({ i: r.id, n: r.name, x: r.x, y: r.y, rm: r.radiusMeters, c: r.color, op: r.opacity, bs: r.borderStyle, l: r.label || '' })),
      mk: plan.customMarkers.map(m => ({ i: m.id, n: m.name, x: m.x, y: m.y, ic: m.icon, c: m.color, nt: m.notes || '', cat: m.category })),
      pr: (plan.patrolRoutes || []).map(p => ({ i: p.id, n: p.name, et: p.enemyType, rg: p.region, pts: p.points, d: p.direction, f: p.frequency, c: p.color, nt: p.notes || '' })),
      rn: (plan.placedResourceNodes || []).map(rn => ({ i: rn.id, n: rn.name, t: rn.type, x: rn.x, y: rn.y, d: rn.density, nt: rn.notes || '' })),
    };
    return btoa(encodeURIComponent(JSON.stringify(compact)));
  } catch (e) {
    console.error('Failed to encode map state', e);
    return '';
  }
}

export function decodeMapState(encoded: string): CastleBuildPlan | null {
  try {
    const jsonStr = decodeURIComponent(atob(encoded));
    const parsed = JSON.parse(jsonStr);
    return {
      heartTier: parsed.ht || 1,
      selectedPlotId: parsed.pt || undefined,
      rooms: parsed.rm || [],
      containers: (parsed.ct || []).map((c: any) => ({
        id: c.i || `cont_${Math.random().toString(36).substring(2, 7)}`,
        name: c.n,
        category: c.cat,
        icon: c.ic,
        x: c.x,
        y: c.y,
        notes: c.nt,
      })),
      radii: (parsed.rd || []).map((r: any) => ({
        id: r.i || `rad_${Math.random().toString(36).substring(2, 7)}`,
        name: r.n,
        x: r.x,
        y: r.y,
        radiusMeters: r.rm,
        color: r.c,
        opacity: r.op ?? 0.3,
        borderStyle: r.bs || 'solid',
        label: r.l,
      })),
      customMarkers: (parsed.mk || []).map((m: any) => ({
        id: m.i || `mark_${Math.random().toString(36).substring(2, 7)}`,
        name: m.n,
        x: m.x,
        y: m.y,
        icon: m.ic,
        color: m.c,
        notes: m.nt,
        category: m.cat,
      })),
      patrolRoutes: (parsed.pr || []).map((p: any) => ({
        id: p.i || `patrol_${Math.random().toString(36).substring(2, 7)}`,
        name: p.n,
        enemyType: p.et,
        region: p.rg || 'dunley',
        points: p.pts || [],
        direction: p.d || 'Clockwise',
        frequency: p.f || 'Continuous',
        color: p.c || '#F59E0B',
        notes: p.nt,
      })),
      placedResourceNodes: (parsed.rn || []).map((rn: any) => ({
        id: rn.i || `res_${Math.random().toString(36).substring(2, 7)}`,
        name: rn.n,
        type: rn.t,
        x: rn.x,
        y: rn.y,
        density: rn.d || 'high',
        notes: rn.nt,
        isCustom: true,
      })),
    };
  } catch (e) {
    console.error('Failed to decode map state', e);
    return null;
  }
}

// Local Storage helpers
export function getSavedPlansFromStorage(): SavedPlan[] {
  try {
    const raw = localStorage.getItem(LOCAL_STORAGE_KEY);
    return raw ? JSON.parse(raw) : [];
  } catch (e) {
    console.error('Error reading saved plans', e);
    return [];
  }
}

export function savePlanToStorage(plan: SavedPlan): SavedPlan[] {
  const existing = getSavedPlansFromStorage();
  const idx = existing.findIndex(p => p.id === plan.id);
  if (idx >= 0) {
    existing[idx] = plan;
  } else {
    existing.unshift(plan);
  }
  try {
    localStorage.setItem(LOCAL_STORAGE_KEY, JSON.stringify(existing));
  } catch (e) {
    console.error('Error saving plan to storage', e);
  }
  return existing;
}

export function deletePlanFromStorage(id: string): SavedPlan[] {
  const existing = getSavedPlansFromStorage().filter(p => p.id !== id);
  try {
    localStorage.setItem(LOCAL_STORAGE_KEY, JSON.stringify(existing));
  } catch (e) {
    console.error('Error deleting plan', e);
  }
  return existing;
}
