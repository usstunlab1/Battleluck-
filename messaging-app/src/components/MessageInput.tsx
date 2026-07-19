import React, { useState } from 'react';

interface MessageInputProps {
  onSendMessage: (body: string) => void;
}

export function MessageInput({ onSendMessage }: MessageInputProps) {
  const [message, setMessage] = useState('');

  const handleSend = () => {
    if (message.trim()) {
      onSendMessage(message.trim());
      setMessage('');
    }
  };

  const handleKeyPress = (e: React.KeyboardEvent) => {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault();
      handleSend();
    }
  };

  return (
    <div style={{ padding: '1rem', borderTop: '1px solid #e5e5e5', display: 'flex', gap: '0.5rem' }}>
      <textarea
        placeholder="Type a message..."
        value={message}
        onChange={(e) => setMessage(e.target.value)}
        onKeyPress={handleKeyPress}
        style={{ flex: 1, padding: '0.5rem', minHeight: '60px', resize: 'vertical' }}
      />
      <button onClick={handleSend} style={{ padding: '0.5rem 1rem' }}>
        Send
      </button>
    </div>
  );
}