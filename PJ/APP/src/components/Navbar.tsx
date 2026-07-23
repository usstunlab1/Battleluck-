import React, { useState } from 'react';
import { 
  Shield, 
  Search, 
  Sun, 
  Moon, 
  Share2, 
  Download, 
  Save, 
  MessageSquare, 
  BookOpen, 
  Trash2, 
  Check, 
  Sparkles,
  Layers,
  Heart
} from 'lucide-react';
import { CastleHeartTier } from '../types';

interface NavbarProps {
  searchQuery: string;
  setSearchQuery: (q: string) => void;
  isNightMode: boolean;
  setIsNightMode: React.Dispatch<React.SetStateAction<boolean>>;
  heartTier: CastleHeartTier;
  radiiCount: number;
  containerCount: number;
  onOpenShareModal: () => void;
  onOpenFeedbackModal: () => void;
  onOpenGuideModal: () => void;
  onOpenSaveModal: () => void;
  onClearPlan: () => void;
  showGrid: boolean;
  setShowGrid: React.Dispatch<React.SetStateAction<boolean>>;
  embedMode: boolean;
  setEmbedMode: (v: boolean) => void;
  onOpenBepInExModal?: () => void;
}

export const Navbar: React.FC<NavbarProps> = ({
  searchQuery,
  setSearchQuery,
  isNightMode,
  setIsNightMode,
  heartTier,
  radiiCount,
  containerCount,
  onOpenShareModal,
  onOpenFeedbackModal,
  onOpenGuideModal,
  onOpenSaveModal,
  onClearPlan,
  showGrid,
  setShowGrid,
  embedMode,
  setEmbedMode,
  onOpenBepInExModal,
}) => {
  const [copied, setCopied] = useState(false);

  const handleQuickShare = () => {
    onOpenShareModal();
  };

  return (
    <header className="h-14 bg-zinc-950 border-b border-zinc-800 px-6 flex items-center justify-between text-zinc-100 z-30 shrink-0 sticky top-0 font-sans select-none">
      {/* Brand & Logo */}
      <div className="flex items-center gap-3">
        <div className="w-8 h-8 rounded-lg bg-zinc-900 border border-zinc-800 flex items-center justify-center text-red-500 shadow-sm">
          <Heart className="w-4 h-4 fill-red-500/20" />
        </div>
        <div>
          <div className="flex items-center gap-2">
            <h1 className="font-extrabold text-sm tracking-tight text-zinc-100 font-sans uppercase">
              V RISING
            </h1>
            <span className="text-[10px] font-semibold tracking-wider uppercase px-2 py-0.5 rounded bg-zinc-900 text-zinc-400 border border-zinc-800 font-mono">
              MAP & BASE PLANNER
            </span>
          </div>
        </div>
      </div>

      {/* Center: Search & Quick Metrics */}
      <div className="hidden md:flex items-center gap-3 flex-1 max-w-md mx-6">
        <div className="relative w-full">
          <Search className="w-3.5 h-3.5 absolute left-3 top-1/2 -translate-y-1/2 text-zinc-500" />
          <input
            type="text"
            value={searchQuery}
            onChange={(e) => setSearchQuery(e.target.value)}
            placeholder="Search V Blood Bosses, Ores (Copper, Iron, Silver), Waygates..."
            className="w-full bg-zinc-900 border border-zinc-800 rounded-lg pl-9 pr-3 py-1.5 text-xs text-zinc-200 placeholder-zinc-500 focus:outline-none focus:border-red-900 transition-colors"
          />
          {searchQuery && (
            <button
              onClick={() => setSearchQuery('')}
              className="absolute right-2 top-1/2 -translate-y-1/2 text-xs text-zinc-500 hover:text-zinc-200"
            >
              ×
            </button>
          )}
        </div>

        {/* Live Counters */}
        <div className="hidden lg:flex items-center gap-2 text-xs font-mono bg-zinc-900 border border-zinc-800 px-3 py-1.5 rounded-lg shrink-0 text-zinc-400">
          <span className="text-red-400 font-bold" title="Castle Heart Level">T{heartTier}</span>
          <span className="text-zinc-700">|</span>
          <span className="text-amber-400">{radiiCount} Radii</span>
          <span className="text-zinc-700">|</span>
          <span className="text-emerald-400">{containerCount} Build</span>
        </div>
      </div>

      {/* Right Controls */}
      <div className="flex items-center gap-2 text-xs">
        {/* BepInEx Server Data Hub */}
        <button
          onClick={onOpenBepInExModal}
          title="Open BepInEx Server JSON Injector & Extractor (VRisingServer\BepInEx)"
          className="px-3 py-1.5 rounded-lg text-xs font-bold border border-red-800 bg-red-950/80 hover:bg-red-900 hover:border-red-600 text-red-200 transition-colors flex items-center gap-1.5 font-serif tracking-wider shadow-sm"
        >
          <Sparkles className="w-3.5 h-3.5 text-amber-400 animate-pulse" />
          <span className="hidden sm:inline">BepInEx JSON Hub</span>
        </button>

        {/* Live MapGenie Embed Toggle */}
        <button
          onClick={() => setEmbedMode(!embedMode)}
          title={embedMode ? "Switch to Custom Interactive Planner Engine" : "Switch to Live MapGenie Embed"}
          className={`px-3 py-1.5 rounded-lg text-xs font-bold border font-serif tracking-wider transition-colors flex items-center gap-1.5 ${
            embedMode
              ? 'bg-red-950 text-red-300 border-red-700 shadow-sm'
              : 'bg-zinc-900 text-zinc-300 border-zinc-800 hover:bg-zinc-800 hover:text-white'
          }`}
        >
          <Sparkles className="w-3.5 h-3.5 text-red-400" />
          <span className="hidden sm:inline">{embedMode ? 'MAPGENIE REFERENCE ACTIVE' : 'MAPGENIE REFERENCE'}</span>
        </button>

        {/* Day / Night Toggle */}
        <button
          onClick={() => setIsNightMode(prev => !prev)}
          title={isNightMode ? "Switch to Daylight Mode" : "Switch to Night Mode"}
          className={`flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-medium border transition-colors ${
            isNightMode 
              ? 'bg-zinc-900 text-indigo-300 border-zinc-800 hover:bg-zinc-800' 
              : 'bg-zinc-900 text-amber-300 border-zinc-800 hover:bg-zinc-800'
          }`}
        >
          {isNightMode ? <Moon className="w-3.5 h-3.5 text-indigo-400" /> : <Sun className="w-3.5 h-3.5 text-amber-400" />}
          <span className="hidden sm:inline">{isNightMode ? 'Night Mode' : 'Day Mode'}</span>
        </button>

        {/* Grid Toggle */}
        <button
          onClick={() => setShowGrid(prev => !prev)}
          title="Toggle Grid Overlay"
          className={`px-3 py-1.5 rounded-lg text-xs font-medium border transition-colors ${
            showGrid 
              ? 'bg-zinc-800 text-zinc-100 border-zinc-700' 
              : 'bg-zinc-900 text-zinc-400 border-zinc-800 hover:bg-zinc-800'
          }`}
        >
          <Layers className="w-3.5 h-3.5" />
        </button>

        {/* Clear Plan */}
        <button
          onClick={onClearPlan}
          title="Reset plan"
          className="px-3 py-1.5 rounded-lg text-xs font-medium bg-zinc-900 border border-zinc-800 hover:bg-zinc-800 text-zinc-300 transition-colors flex items-center gap-1.5"
        >
          <Trash2 className="w-3.5 h-3.5 text-zinc-500" />
          <span className="hidden sm:inline">Reset</span>
        </button>

        {/* Saved Plans */}
        <button
          onClick={onOpenSaveModal}
          className="px-3 py-1.5 rounded-lg text-xs font-medium bg-zinc-900 border border-zinc-800 hover:bg-zinc-800 text-zinc-300 transition-colors flex items-center gap-1.5"
        >
          <Save className="w-3.5 h-3.5 text-amber-400" />
          <span className="hidden sm:inline">Saved Plans</span>
        </button>

        {/* Share & Export */}
        <button
          onClick={handleQuickShare}
          className="px-3.5 py-1.5 rounded-lg text-xs font-medium bg-red-800 hover:bg-red-700 text-white border border-red-600/30 shadow-md shadow-red-950/40 transition-colors flex items-center gap-1.5"
        >
          <Share2 className="w-3.5 h-3.5" />
          <span>Share</span>
        </button>

        {/* Guide */}
        <button
          onClick={onOpenGuideModal}
          title="Manual & Strategy Guide"
          className="px-3 py-1.5 rounded-lg text-xs font-medium bg-zinc-900 border border-zinc-800 hover:bg-zinc-800 text-zinc-300 transition-colors flex items-center gap-1.5"
        >
          <BookOpen className="w-3.5 h-3.5 text-blue-400" />
          <span className="hidden md:inline">Guide</span>
        </button>
      </div>
    </header>
  );
};
