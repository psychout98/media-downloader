import { useState, useEffect, useCallback } from 'react';
import { checkStatus } from './api/client';
import QueueTab from './components/Queue/QueueTab';
import LibraryTab from './components/Library/LibraryTab';
import NowPlayingTab from './components/NowPlaying/NowPlayingTab';

type Tab = 'queue' | 'library' | 'nowplaying';

interface Toast {
  id: number;
  message: string;
  type: 'error' | 'info';
}

let toastId = 0;

export default function App() {
  const [activeTab, setActiveTab] = useState<Tab>('queue');
  const [connected, setConnected] = useState(false);
  const [toasts, setToasts] = useState<Toast[]>([]);

  const addToast = useCallback((message: string, type: 'error' | 'info' = 'info') => {
    const id = ++toastId;
    setToasts((prev) => [...prev, { id, message, type }]);
    setTimeout(() => setToasts((prev) => prev.filter((t) => t.id !== id)), 5000);
  }, []);

  useEffect(() => {
    let mounted = true;
    const poll = async () => {
      try {
        await checkStatus();
        if (mounted) setConnected(true);
      } catch {
        if (mounted) setConnected(false);
      }
    };
    poll();
    const interval = setInterval(poll, 30000);
    return () => { mounted = false; clearInterval(interval); };
  }, []);

  const handlePlay = useCallback(() => setActiveTab('nowplaying'), []);

  const tabs: { key: Tab; label: string }[] = [
    { key: 'queue', label: 'Queue' },
    { key: 'library', label: 'Library' },
    { key: 'nowplaying', label: 'Now Playing' },
  ];

  return (
    <div className="min-h-screen bg-bg text-text">
      {/* Header */}
      <header className="border-b border-border bg-surface px-6 py-3 flex items-center justify-between">
        <h1 className="text-lg font-semibold">Media Downloader</h1>
        <div className="flex items-center gap-4">
          <nav className="flex gap-1">
            {tabs.map((tab) => (
              <button
                key={tab.key}
                onClick={() => setActiveTab(tab.key)}
                className={`px-4 py-1.5 rounded text-sm font-medium transition-colors ${
                  activeTab === tab.key
                    ? 'bg-accent text-white'
                    : 'text-text-dim hover:text-text hover:bg-surface-2'
                }`}
              >
                {tab.label}
              </button>
            ))}
          </nav>
          <div className="flex items-center gap-2 text-sm">
            <div className={`w-2 h-2 rounded-full ${connected ? 'bg-green' : 'bg-red'}`} />
            <span className="text-text-dim">{connected ? 'Connected' : 'Disconnected'}</span>
          </div>
        </div>
      </header>

      {/* Main content */}
      <main className="max-w-7xl mx-auto p-6">
        {activeTab === 'queue' && <QueueTab addToast={addToast} />}
        {activeTab === 'library' && <LibraryTab addToast={addToast} onPlay={handlePlay} />}
        {activeTab === 'nowplaying' && <NowPlayingTab addToast={addToast} />}
      </main>

      {/* Toasts */}
      <div className="fixed bottom-4 right-4 flex flex-col gap-2 z-50">
        {toasts.map((toast) => (
          <div
            key={toast.id}
            className={`px-4 py-3 rounded-lg shadow-lg text-sm max-w-sm ${
              toast.type === 'error' ? 'bg-red text-white' : 'bg-accent text-white'
            }`}
          >
            {toast.message}
          </div>
        ))}
      </div>
    </div>
  );
}
