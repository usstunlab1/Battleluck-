import React from 'react';

interface Message {
  id: string;
  thread_id: string;
  sender_id: string;
  body: string;
  created_at: string;
}

interface MessageListProps {
  messages: Message[];
}

export function MessageList({ messages }: MessageListProps) {
  return (
    <div style={{ flex: 1, padding: '1rem', overflowY: 'auto' }}>
      {messages.length === 0 ? (
        <p style={{ color: '#666' }}>No messages yet. Start the conversation!</p>
      ) : (
        <ul style={{ display: 'flex', flexDirection: 'column', gap: '0.5rem' }}>
          {messages.map((message) => (
            <li
              key={message.id}
              style={{
                padding: '0.75rem',
                background: '#f9f9f9',
                borderRadius: '4px',
                maxWidth: '70%',
              }}
            >
              <div style={{ fontSize: '0.8rem', color: '#666', marginBottom: '0.25rem' }}>
                {message.sender_id.substring(0, 8)}...
              </div>
              <div>{message.body}</div>
              <div style={{ fontSize: '0.75rem', color: '#999', marginTop: '0.25rem' }}>
                {new Date(message.created_at).toLocaleTimeString()}
              </div>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}