import React, { useState } from 'react';
import { X, Copy, Check, Share2, Download, Link2 } from 'lucide-react';
import { CastleBuildPlan } from '../../types';
import { encodeMapState } from '../../utils/mapUtils';

interface ShareModalProps {
  isOpen: boolean;
  onClose: () => void;
  plan: CastleBuildPlan;
}

export const ShareModal: React.FC<ShareModalProps> = ({ isOpen, onClose, plan }) => {
  const [copied, setCopied] = useState(false);

  if (!isOpen) return null;

  const encodedState = encodeMapState(plan);
  const shareUrl = `${window.location.origin}${window.location.pathname}#plan=${encodedState}`;

  const handleCopy = () => {
    navigator.clipboard.writeText(shareUrl);
    setCopied(true);
    setTimeout(() => setCopied(false), 2500);
  };

  return (
    <div className="fixed inset-0 z-50 bg-black/80 backdrop-blur-sm flex items-center justify-center p-4">
      <div className="bg-zinc-950 border border-zinc-800 rounded-xl w-full max-w-lg overflow-hidden shadow-2xl text-zinc-300 animate-in fade-in zoom-in-95 duration-200">
        {/* Header */}
        <div className="h-14 border-b border-zinc-800 bg-zinc-900/50 px-6 flex items-center justify-between">
          <div className="flex items-center gap-2">
            <div className="w-7 h-7 rounded-full bg-red-900/50 border border-red-500/30 flex items-center justify-center">
              <Share2 className="w-3.5 h-3.5 text-red-400" />
            </div>
            <h3 className="text-xs font-semibold uppercase tracking-widest text-zinc-100 font-sans">
              Share Castle Blueprint & Map
            </h3>
          </div>
          <button 
            onClick={onClose}
            className="text-zinc-500 hover:text-zinc-200 p-1 rounded-lg transition-colors"
          >
            <X className="w-5 h-5" />
          </button>
        </div>

        {/* Content */}
        <div className="p-6 space-y-5">
          <p className="text-xs text-zinc-400 leading-relaxed">
            Generate a unique sharable link containing your custom radius zones, container placements, custom pins, and selected castle heart configuration.
          </p>

          <div className="space-y-2">
            <label className="text-[10px] uppercase font-bold text-zinc-500 tracking-wider block">
              Sharable Blueprint Link
            </label>
            <div className="flex items-center gap-2">
              <input
                type="text"
                readOnly
                value={shareUrl}
                className="w-full bg-zinc-900 border border-zinc-800 rounded-lg px-3 py-2 text-xs font-mono text-zinc-300 focus:outline-none"
              />
              <button
                onClick={handleCopy}
                className="px-4 py-2 bg-red-800 hover:bg-red-700 text-white text-xs font-medium rounded-lg transition-colors shrink-0 flex items-center gap-1.5 border border-red-600/30 shadow-md shadow-red-950/40"
              >
                {copied ? <Check className="w-4 h-4 text-emerald-400" /> : <Copy className="w-4 h-4" />}
                <span>{copied ? 'Copied!' : 'Copy'}</span>
              </button>
            </div>
          </div>

          <div className="p-4 bg-zinc-900/50 rounded-lg border border-zinc-800/80 space-y-2">
            <h4 className="text-xs font-semibold text-zinc-200 uppercase tracking-wider">Plan Summary</h4>
            <div className="grid grid-cols-3 gap-2 text-center text-xs font-mono">
              <div className="p-2 bg-zinc-900 rounded border border-zinc-800">
                <span className="text-red-400 font-bold block">T{plan.heartTier}</span>
                <span className="text-[10px] text-zinc-500 uppercase">Heart Tier</span>
              </div>
              <div className="p-2 bg-zinc-900 rounded border border-zinc-800">
                <span className="text-amber-400 font-bold block">{plan.radii.length}</span>
                <span className="text-[10px] text-zinc-500 uppercase">Radii</span>
              </div>
              <div className="p-2 bg-zinc-900 rounded border border-zinc-800">
                <span className="text-emerald-400 font-bold block">{plan.containers.length}</span>
                <span className="text-[10px] text-zinc-500 uppercase">Structures</span>
              </div>
            </div>
          </div>
        </div>

        {/* Footer */}
        <div className="p-4 bg-zinc-900/40 border-t border-zinc-800 flex justify-end">
          <button
            onClick={onClose}
            className="px-4 py-1.5 bg-zinc-900 hover:bg-zinc-800 border border-zinc-800 text-xs text-zinc-300 rounded transition-colors font-medium"
          >
            Close
          </button>
        </div>
      </div>
    </div>
  );
};
