import React from 'react';
import { MessageSquare, RefreshCw, MapPin } from 'lucide-react';

interface FooterProps {
  onOpenFeedbackModal: () => void;
  cursorPos?: { x: number; y: number };
}

export const Footer: React.FC<FooterProps> = ({ onOpenFeedbackModal, cursorPos }) => {
  return (
    <footer className="h-10 bg-zinc-950 border-t border-zinc-800 flex items-center justify-between px-6 shrink-0 font-sans text-zinc-300 z-30 select-none">
      <div className="flex items-center gap-4 text-[11px] font-medium">
        <span className="flex items-center gap-1.5 text-zinc-300">
          <span className="w-1.5 h-1.5 bg-emerald-500 rounded-full animate-pulse"></span> 
          Server Sync Active
        </span>
        <span className="text-zinc-700">|</span>
        <span className="text-zinc-400 font-mono flex items-center gap-1">
          <MapPin className="w-3 h-3 text-zinc-500" />
          Map Cursor: X:{cursorPos ? String(cursorPos.x).padStart(4, '0') : '0500'} Y:{cursorPos ? String(cursorPos.y).padStart(4, '0') : '0500'}
        </span>
      </div>
      <button 
        onClick={onOpenFeedbackModal}
        className="text-[10px] text-zinc-400 hover:text-zinc-200 uppercase tracking-widest flex items-center gap-1.5 font-semibold transition-colors"
      >
        <MessageSquare className="w-3 h-3 text-zinc-500" />
        Submit Feedback
      </button>
    </footer>
  );
};
