import React, { useState } from 'react';
import { X, MessageSquare, Star, Send, Check } from 'lucide-react';

interface FeedbackModalProps {
  isOpen: boolean;
  onClose: () => void;
}

export const FeedbackModal: React.FC<FeedbackModalProps> = ({ isOpen, onClose }) => {
  const [rating, setRating] = useState<number>(5);
  const [feedbackType, setFeedbackType] = useState<'feature' | 'bug' | 'map_accuracy' | 'general'>('feature');
  const [comment, setComment] = useState('');
  const [submitted, setSubmitted] = useState(false);

  if (!isOpen) return null;

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    if (!comment.trim()) return;
    setSubmitted(true);
    setTimeout(() => {
      setSubmitted(false);
      setComment('');
      onClose();
    }, 1800);
  };

  return (
    <div className="fixed inset-0 z-50 bg-black/80 backdrop-blur-md flex items-center justify-center p-4">
      <div className="bg-slate-950 border border-slate-800 rounded-2xl w-full max-w-md overflow-hidden shadow-2xl text-slate-200 flex flex-col animate-in fade-in zoom-in-95 duration-200">
        {/* Header */}
        <div className="h-16 border-b border-slate-800 bg-slate-900/60 px-6 flex items-center justify-between">
          <div className="flex items-center gap-3">
            <div className="w-8 h-8 rounded-lg bg-emerald-950/80 border border-emerald-700/60 flex items-center justify-center">
              <MessageSquare className="w-4 h-4 text-emerald-400" />
            </div>
            <div>
              <h3 className="text-sm font-bold uppercase tracking-wider text-slate-100 font-mono">
                Submit Feedback or Report Bug
              </h3>
              <p className="text-[11px] text-slate-400">Help us improve the V Rising Map & Planner</p>
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
        <form onSubmit={handleSubmit} className="p-6 space-y-4">
          {/* Rating */}
          <div>
            <label className="text-[10px] font-mono uppercase text-slate-400 block mb-1.5 font-bold">
              Rate App Experience
            </label>
            <div className="flex items-center gap-2">
              {[1, 2, 3, 4, 5].map((star) => (
                <button
                  type="button"
                  key={star}
                  onClick={() => setRating(star)}
                  className="p-1 hover:scale-110 transition-transform"
                >
                  <Star
                    className={`w-6 h-6 ${
                      star <= rating
                        ? 'fill-amber-400 text-amber-400'
                        : 'text-slate-700 hover:text-slate-500'
                    }`}
                  />
                </button>
              ))}
            </div>
          </div>

          {/* Feedback Type */}
          <div>
            <label className="text-[10px] font-mono uppercase text-slate-400 block mb-1.5 font-bold">
              Feedback Category
            </label>
            <div className="grid grid-cols-2 gap-1.5 text-xs font-mono">
              {[
                { id: 'feature', label: '💡 Feature Request' },
                { id: 'bug', label: '🐛 Bug Report' },
                { id: 'map_accuracy', label: '🗺️ Map Data Fix' },
                { id: 'general', label: '💬 General Note' },
              ].map((cat) => (
                <button
                  type="button"
                  key={cat.id}
                  onClick={() => setFeedbackType(cat.id as any)}
                  className={`p-2 rounded-lg border text-left transition-all ${
                    feedbackType === cat.id
                      ? 'bg-emerald-950/80 border-emerald-600 text-emerald-300 font-bold'
                      : 'bg-slate-900 border-slate-800 text-slate-400 hover:text-slate-200'
                  }`}
                >
                  {cat.label}
                </button>
              ))}
            </div>
          </div>

          {/* Comment */}
          <div>
            <label className="text-[10px] font-mono uppercase text-slate-400 block mb-1 font-bold">
              Your Suggestions / Comments
            </label>
            <textarea
              rows={4}
              value={comment}
              onChange={(e) => setComment(e.target.value)}
              placeholder="e.g. Add patrol route direction arrows, or update Iron Mine location..."
              className="w-full bg-slate-900 border border-slate-800 rounded-xl p-3 text-xs text-slate-200 focus:outline-none focus:border-emerald-500"
            />
          </div>

          <div className="pt-2 flex justify-end">
            <button
              type="submit"
              disabled={!comment.trim() || submitted}
              className="w-full py-2.5 bg-gradient-to-r from-emerald-600 to-teal-700 hover:from-emerald-500 hover:to-teal-600 disabled:opacity-50 text-white font-bold text-xs rounded-xl shadow-lg shadow-emerald-950/50 flex items-center justify-center gap-2 transition-all font-mono"
            >
              {submitted ? (
                <>
                  <Check className="w-4 h-4 text-emerald-300" />
                  <span>Feedback Received! Thank You!</span>
                </>
              ) : (
                <>
                  <Send className="w-4 h-4" />
                  <span>Submit Feedback</span>
                </>
              )}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
};
