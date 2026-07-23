export type MapObjectType =
  | "marker"
  | "resource"
  | "zone"
  | "schematic"
  | "boss"
  | "event";

export interface MapObject {
  id: string;
  type: MapObjectType;
  name: string;
  category?: string;
  mapPosition: {
    x: number;
    y: number;
  };
  worldPosition?: {
    x: number;
    y: number;
    z: number;
  };
  icon?: string;
  color?: string;
  enabled: boolean;
  source: "admin" | "import" | "battleluck";
  metadata?: Record<string, unknown>;
  radius?: number;
  polygon?: Array<{ x: number; y: number }>;
  sourceFile?: string;
  importedAtUtc?: string;
}

export type RegionId = 
  | 'farbane' 
  | 'dunley' 
  | 'silverlight' 
  | 'cursed' 
  | 'hallowed' 
  | 'gloomrot_south' 
  | 'gloomrot_north' 
  | 'oakveil';

export type MarkerType = 
  | 'boss' 
  | 'resource' 
  | 'waygate' 
  | 'cave' 
  | 'plot' 
  | 'container' 
  | 'custom'
  | 'station';

export type ResourceType = 
  | 'copper' 
  | 'iron' 
  | 'silver' 
  | 'quartz' 
  | 'sulfur' 
  | 'blood_rose' 
  | 'cotton' 
  | 'ghost_shroom' 
  | 'sacred_grape'
  | 'stygian_shard'
  | 'fish';

export type CastleHeartTier = 1 | 2 | 3 | 4 | 5;

export interface VBloodBoss {
  id: string;
  name: string;
  level: number;
  region: RegionId;
  x: number; // 0-1000 coordinate space
  y: number;
  bloodType: string;
  rewards: string[];
  description: string;
  icon: string;
  recommendedGearScore: number;
}

export interface ResourceNode {
  id: string;
  type: ResourceType;
  name: string;
  region: RegionId;
  x: number;
  y: number;
  density: 'low' | 'medium' | 'high' | 'rich_mine';
}

export interface WaygateNode {
  id: string;
  name: string;
  region: RegionId;
  x: number;
  y: number;
  type: 'waygate' | 'cave';
  targetCaveX?: number;
  targetCaveY?: number;
}

export interface CastlePlot {
  id: string;
  name: string;
  region: RegionId;
  x: number;
  y: number;
  tileSize: number; // e.g. 120, 180, 260
  chokePoint: boolean;
  sunlightRating: 'low' | 'medium' | 'high'; // low is good for vampires
  resourceProximity: string[];
  description: string;
}

export interface PlacedContainer {
  id: string;
  name: string;
  category: 'storage' | 'workstation' | 'defense' | 'furniture';
  icon: string;
  x: number; // Map coordinates
  y: number;
  notes?: string;
  capacitySlots?: number;
  rotation?: number; // 0, 90, 180, 270
}

export interface RadiusPreset {
  id: string;
  name: string;
  radiusMeters: number; // e.g. 30, 80, 150
  color: string;
  description: string;
}

export interface PlacedRadius {
  id: string;
  name: string;
  x: number;
  y: number;
  radiusMeters: number; // scale relative to map
  color: string; // hex or rgba
  opacity: number; // 0.1 to 0.8
  borderStyle: 'solid' | 'dashed' | 'pulse';
  label?: string;
}

export interface CustomMarker {
  id: string;
  name: string;
  x: number;
  y: number;
  icon: string;
  color: string;
  notes?: string;
  category: 'stash' | 'raid_target' | 'ally' | 'hazard' | 'pin';
}

export type ActiveTool = 'select' | 'radius' | 'container' | 'marker' | 'patrol' | 'resource_place';

export interface PatrolPoint {
  x: number;
  y: number;
}

export interface PatrolRoute {
  id: string;
  name: string;
  enemyType: string; // e.g. "Vincent Militia Patrol", "Gloomrot Mech Squad", "Paladin Guard", "Skeleton Horde"
  region: RegionId;
  points: PatrolPoint[]; // Array of 0-1000 coordinate points forming the route
  direction: 'Clockwise' | 'Counter-Clockwise' | 'Bidirectional' | 'Loop';
  frequency: 'Continuous' | 'Every 2 Mins' | 'Every 5 Mins' | 'Night Only';
  color: string;
  isCustom?: boolean;
  notes?: string;
}

export interface PlacedResourceNode {
  id: string;
  name: string;
  type: ResourceType;
  x: number;
  y: number;
  density: 'low' | 'medium' | 'high' | 'rich_mine';
  region?: RegionId;
  notes?: string;
  isCustom?: boolean;
}

export interface RoomLayout {
  id: string;
  name: string;
  requiredFloor: string;
  bonus: string;
  recommendedCount: number;
}

export interface CastleBuildPlan {
  heartTier: CastleHeartTier;
  selectedPlotId?: string;
  rooms: string[]; // room layout ids
  containers: PlacedContainer[];
  radii: PlacedRadius[];
  customMarkers: CustomMarker[];
  patrolRoutes?: PatrolRoute[];
  placedResourceNodes?: PlacedResourceNode[];
}

export interface SavedPlan {
  id: string;
  name: string;
  updatedAt: string;
  buildPlan: CastleBuildPlan;
}

export interface FeedbackSubmission {
  id: string;
  type: 'bug' | 'feature' | 'map_accuracy' | 'general';
  rating: number;
  comment: string;
  createdAt: string;
}

export type QuestDifficulty = 'Novice' | 'Veteran' | 'Elite' | 'Legendary';
export type QuestStatus = 'available' | 'active' | 'completed';

export interface NpcQuestObjective {
  id: string;
  label: string;
  current: number;
  target: number;
}

export interface NpcQuest {
  id: string;
  title: string;
  npcName: string;
  npcRole: string;
  region: RegionId;
  difficulty: QuestDifficulty;
  level: number;
  description: string;
  story: string;
  objectives: NpcQuestObjective[];
  rewards: Array<{ name: string; amount?: number }>;
  expiresIn?: string;
  status: QuestStatus;
  accent: string;
}
