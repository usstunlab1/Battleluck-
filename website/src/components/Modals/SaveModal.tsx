import React, { useState, useEffect } from 'react';
import { X, Save, Trash2, Calendar, Check, FolderOpen } from 'lucide-react';
import { SavedPlan, CastleBuildPlan } from '../../types';
import { getSavedPlansFromStorage, savePlanToStorage, deletePlanFromStorage } from '../../utils/mapUtils';

interface SaveModalProps {
  isOpen: boolean;
  onClose: () => void;
  currentPlan: CastleBuildPlan;
  onLoadPlan: (plan: CastleBuildPlan) => void;
}

export const SaveModal: React.FC<SaveModalProps> = ({
  isOpen,
  onClose,
  currentPlan,
  onLoadPlan,
}) => {
  const [plans, setPlans] = useState<SavedPlan[]>([]);
  const [newPlanName, setNewPlanName] = useState('');
  const [saveSuccess, setSaveSuccess] = useState(false);

  useEffect(() => {
    if (isOpen) {
      setPlans(getSavedPlansFromStorage());
    }
  }, [isOpen]);

  if (!isOpen) return null;

  const handleSave = () => {
    if (!newPlanName.trim()) return;
    const newSavedPlan: SavedPlan = {
      id: `plan_${Date.now()}`,
      name: newPlanName.trim(),
      updatedAt: new Date().toLocaleDateString('en-US', { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' }),
      buildPlan: currentPlan,
    };

    const updated = savePlanToStorage(newSavedPlan);
    setPlans(updated);
    setNewPlanName('');
    setSaveSuccess(true);
    setTimeout(() => setSaveSuccess(false), 2000);
  };

  const handleDelete = (id: string, e: React.MouseEvent) => {
    e.stopPropagation();
    const updated = deletePlanFromStorage(id);
    setPlans(updated);
  };

  return (
    <div className="fixed inset-0 z-50 bg-black/80 backdrop-blur-md flex items-center justify-center p-4">
      <div className="bg-slate-950 border border-slate-800 rounded-2xl w-full max-w-lg overflow-hidden shadow-2xl text-slate-200 flex flex-col animate-in fade-in zoom-in-95 duration-200">
        {/* Header */}
        <div className="h-16 border-b border-slate-800 bg-slate-900/60 px-6 flex items-center justify-between">
          <div className="flex items-center gap-3">
            <div className="w-8 h-8 rounded-lg bg-amber-950/80 border border-amber-700/60 flex items-center justify-center">
              <Save className="w-4 h-4 text-amber-400" />
            </div>
            <div>
              <h3 className="text-sm font-bold uppercase tracking-wider text-slate-100 font-mono">
                Saved Castle Blueprints & Map Plans
              </h3>
              <p className="text-[11px] text-slate-400">Save and reload custom layout configurations</p>
            </div>
          </div>
          <button 
            onClick={onClose}
            className="text-slate-500 hover:text-slate-200 p-1.5 rounded-lg hover:bg-slate-800 transition-colors"
          >
            <X className="w-5 h-5" />
          </button>
        </div>

        {/* Content */}
        <div className="p-6 space-y-5">
          {/* Create New Plan Input */}
          <div className="p-4 bg-slate-900/80 rounded-xl border border-slate-800 space-y-2">
            <label className="text-[10px] font-mono uppercase tracking-wider text-slate-400 block font-bold">
              Save Current Map Layout
            </label>
            <div className="flex items-center gap-2">
              <input
                type="text"
                value={newPlanName}
                onChange={(e) => setNewPlanName(e.target.value)}
                placeholder="e.g. Dunley Choke Fortress v1"
                className="w-full bg-slate-950 border border-slate-800 rounded-lg px-3 py-2 text-xs font-medium text-slate-200 focus:outline-none focus:border-amber-500"
              />
              <button
                onClick={handleSave}
                disabled={!newPlanName.trim()}
                className="px-4 py-2 bg-gradient-to-r from-amber-600 to-amber-700 hover:from-amber-500 hover:to-amber-600 disabled:opacity-50 text-white text-xs font-semibold rounded-lg transition-all shrink-0 flex items-center gap-1.5 shadow-md shadow-amber-950/40"
              >
                {saveSuccess ? <Check className="w-4 h-4 text-emerald-400" /> : <Save className="w-4 h-4" />}
                <span>{saveSuccess ? 'Saved!' : 'Save Plan'}</span>
              </button>
            </div>
          </div>

          {/* List of Saved Plans */}
          <div className="space-y-2">
            <label className="text-[10px] font-mono uppercase tracking-wider text-slate-400 block font-bold">
              Your Saved Blueprints ({plans.length})
            </label>

            {plans.length === 0 ? (
              <div className="p-6 bg-slate-900/40 rounded-xl border border-slate-800/80 text-center space-y-1 text-slate-500 text-xs">
                <FolderOpen className="w-6 h-6 mx-auto text-slate-600" />
                <p>No saved plans yet. Name and save your current configuration above.</p>
              </div>
            ) : (
              <div className="space-y-2 max-h-60 overflow-y-auto">
                {plans.map(p => (
                  <div
                    key={p.id}
                    onClick={() => {
                      onLoadPlan(p.buildPlan);
                      onClose();
                    }}
                    className="p-3 bg-slate-900 border border-slate-800 hover:border-amber-500/80 rounded-xl cursor-pointer transition-all flex items-center justify-between group"
                  >
                    <div>
                      <h4 className="text-xs font-bold text-slate-200 group-hover:text-amber-300">
                        {p.name}
                      </h4>
                      <div className="flex items-center gap-3 text-[10px] font-mono text-slate-400 mt-1">
                        <span className="flex items-center gap-1">
                          <Calendar className="w-3 h-3 text-slate-500" /> {p.updatedAt}
                        </span>
                        <span>• T{p.buildPlan.heartTier} Heart</span>
                        <span>• {p.buildPlan.radii.length} Radii</span>
                        <span>• {p.buildPlan.containers.length} Build</span>
                      </div>
                    </div>
                    <button
                      onClick={(e) => handleDelete(p.id, e)}
                      title="Delete Saved Plan"
                      className="p-1.5 rounded bg-slate-800 hover:bg-red-950 text-slate-400 hover:text-red-300 transition-colors"
                    >
                      <Trash2 className="w-4 h-4" />
                    </button>
                  </div>
                ))}
              </div>
            )}
          </div>
        </div>

        {/* Footer */}
        <div className="p-4 bg-slate-900/60 border-t border-slate-800 flex justify-end">
          <button
            onClick={onClose}
            className="px-4 py-1.5 bg-slate-900 hover:bg-slate-800 border border-slate-800 text-xs text-slate-300 rounded-lg transition-colors font-mono"
          >
            Close
          </button>
        </div>
      </div>
    </div>
  );
};
