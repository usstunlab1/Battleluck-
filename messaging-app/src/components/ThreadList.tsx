import React, { useState } from 'react';

interface Thread {
  id: string;
  title: string;
  created_by: string;
  created_at: string;
}

interface ThreadListProps {
  threads: Thread[];
  onSelectThread: (thread: Thread) => void;
  onCreateThread: (title: string) => void;
}

export function ThreadList({ threads, onSelectThread, onCreateThread }: ThreadListProps) {
  const [newThreadTitle, setNewThreadTitle] = useState('');

  const handleCreate = () => {
    if (newThreadTitle.trim()) {
      onCreateThread(newThreadTitle.trim());
      setNewThreadTitle('');
    }
  };

  return (
    <div>
      <div style={{ marginBottom: '1rem' }}>
        <input
          type="text"
          placeholder="New thread title"
          value={newThreadTitle}
          onChange={(e) => setNewThreadTitle(e.target.value)}
          style={{ width: '100%', padding: '0.5rem', marginBottom: '0.5rem' }}
        />
        <button onClick={handleCreate} style={{ width: '100%', padding: '0.5rem' }}>
          Create Thread
        </button>
      </div>
      <ul>
        {threads.map((thread) => (
          <li key={thread.id} onClick={() => onSelectThread(thread)}>
            {thread.title || 'Untitled Thread'}
          </li>
        ))}
      </ul>
    </div>
  );
}