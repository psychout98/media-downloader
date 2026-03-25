import { useState, useEffect, useRef, useCallback } from 'react';
import {
  getMpcStatus,
  sendMpcCommand,
  openInMpc,
  getPosterUrl,
  buildWsUrl,
  MpcStatusResponse,
} from '../../api/client';
import { formatMs } from '../../utils/format';

interface NowPlayingTabProps {
  addToast: (message: string, type: 'error' | 'info') => void;
}

const POLL_INTERVAL_MS = 3000;
const WS_RETRY_DELAY_MS = 30000;

export default function NowPlayingTab({ addToast }: NowPlayingTabProps) {
  const [status, setStatus] = useState<MpcStatusResponse | null>(null);

  const wsRef = useRef<WebSocket | null>(null);
  const pollIntervalRef = useRef<ReturnType<typeof setInterval> | null>(null);
  const retryTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const mountedRef = useRef(true);

  // ── Helpers ──────────────────────────────────────────────────────────

  const clearPolling = useCallback(() => {
    if (pollIntervalRef.current !== null) {
      clearInterval(pollIntervalRef.current);
      pollIntervalRef.current = null;
    }
  }, []);

  const clearRetryTimer = useCallback(() => {
    if (retryTimerRef.current !== null) {
      clearTimeout(retryTimerRef.current);
      retryTimerRef.current = null;
    }
  }, []);

  const poll = useCallback(async () => {
    try {
      const data = await getMpcStatus();
      if (mountedRef.current) setStatus(data);
    } catch {
      // polling failure is silent — we'll keep trying
    }
  }, []);

  const startPolling = useCallback(() => {
    if (pollIntervalRef.current !== null) return;
    // Fetch immediately, then every POLL_INTERVAL_MS
    poll();
    pollIntervalRef.current = setInterval(poll, POLL_INTERVAL_MS);
  }, [poll]);

  // ── WebSocket lifecycle ─────────────────────────────────────────────

  const connectWs = useCallback(() => {
    if (!mountedRef.current) return;
    // Close any lingering connection
    if (wsRef.current) {
      wsRef.current.onopen = null;
      wsRef.current.onclose = null;
      wsRef.current.onerror = null;
      wsRef.current.onmessage = null;
      wsRef.current.close();
      wsRef.current = null;
    }

    let ws: WebSocket;
    try {
      ws = new WebSocket(buildWsUrl());
    } catch {
      startPolling();
      retryTimerRef.current = setTimeout(connectWs, WS_RETRY_DELAY_MS);
      return;
    }

    wsRef.current = ws;

    ws.onopen = () => {
      clearPolling();
      clearRetryTimer();
    };

    ws.onmessage = (event) => {
      try {
        const data: MpcStatusResponse = JSON.parse(event.data);
        if (mountedRef.current) setStatus(data);
      } catch {
        // ignore malformed messages
      }
    };

    const handleDisconnect = () => {
      if (!mountedRef.current) return;
      wsRef.current = null;
      startPolling();
      clearRetryTimer();
      retryTimerRef.current = setTimeout(connectWs, WS_RETRY_DELAY_MS);
    };

    ws.onclose = handleDisconnect;
    ws.onerror = () => {
      // onerror is always followed by onclose in browsers, but guard just in case
      ws.close();
    };
  }, [clearPolling, clearRetryTimer, startPolling]);

  // Mount / unmount
  useEffect(() => {
    mountedRef.current = true;
    connectWs();

    return () => {
      mountedRef.current = false;
      clearPolling();
      clearRetryTimer();
      if (wsRef.current) {
        wsRef.current.onopen = null;
        wsRef.current.onclose = null;
        wsRef.current.onerror = null;
        wsRef.current.onmessage = null;
        wsRef.current.close();
        wsRef.current = null;
      }
    };
  }, [connectWs, clearPolling, clearRetryTimer]);

  // ── Command helpers ─────────────────────────────────────────────────

  const exec = useCallback(
    async (fn: () => Promise<unknown>, label: string) => {
      try {
        await fn();
      } catch (err) {
        addToast(`${label} failed: ${err instanceof Error ? err.message : 'Unknown error'}`, 'error');
      }
    },
    [addToast],
  );

  const handlePlayPause = useCallback(
    () => exec(() => sendMpcCommand('PLAYPAUSE'), 'Play/Pause'),
    [exec],
  );

  const handleSkipBack = useCallback(() => {
    if (!status) return;
    exec(() => sendMpcCommand('SEEK', Math.max(0, status.positionMs - 30000)), 'Skip back');
  }, [exec, status]);

  const handleSkipForward = useCallback(() => {
    if (!status) return;
    exec(
      () => sendMpcCommand('SEEK', Math.min(status.durationMs, status.positionMs + 30000)),
      'Skip forward',
    );
  }, [exec, status]);

  const handleMute = useCallback(
    () => exec(() => sendMpcCommand('MUTE'), 'Mute'),
    [exec],
  );

  const handleSeek = useCallback(
    (e: React.MouseEvent<HTMLDivElement>) => {
      if (!status || status.durationMs <= 0) return;
      const rect = e.currentTarget.getBoundingClientRect();
      const ratio = Math.max(0, Math.min(1, (e.clientX - rect.left) / rect.width));
      const ms = Math.round(ratio * status.durationMs);
      exec(() => sendMpcCommand('SEEK', ms), 'Seek');
    },
    [exec, status],
  );

  const handleVolumeUp = useCallback(
    () => exec(() => sendMpcCommand('VOLUME_UP'), 'Volume up'),
    [exec],
  );

  const handleVolumeDown = useCallback(
    () => exec(() => sendMpcCommand('VOLUME_DOWN'), 'Volume down'),
    [exec],
  );

  const handlePrevEpisode = useCallback(() => {
    if (!status?.prevEpisode) return;
    exec(() => openInMpc(status.prevEpisode!.mediaItemId), 'Previous episode');
  }, [exec, status]);

  const handleNextEpisode = useCallback(() => {
    if (!status?.nextEpisode) return;
    exec(() => openInMpc(status.nextEpisode!.mediaItemId), 'Next episode');
  }, [exec, status]);

  // ── Disconnected state ──────────────────────────────────────────────

  if (!status || !status.reachable) {
    return (
      <div className="flex justify-center px-4 py-8">
        <div className="w-full max-w-[600px] rounded-xl bg-surface p-6">
          <div className="flex flex-col items-center text-center gap-3">
            <span className="text-4xl opacity-30">🎥</span>
            <h2 className="text-xl font-semibold text-text">MPC-BE not reachable</h2>
            <p className="text-text-dim text-sm leading-relaxed">
              Make sure MPC-BE is running with the web interface enabled on port 13579.
            </p>
            <p className="text-text-dim text-xs mt-2">Will auto-reconnect...</p>
          </div>
        </div>
      </div>
    );
  }

  // ── Derived values ──────────────────────────────────────────────────

  const progressPercent =
    status.durationMs > 0 ? (status.positionMs / status.durationMs) * 100 : 0;
  const volumePercent = status.volume;

  // ── Reachable — Player UI ───────────────────────────────────────────

  return (
    <div className="flex flex-col items-center px-4 py-8 gap-4">
      {/* Player Card */}
      <div className="w-full max-w-[600px] rounded-xl bg-surface p-6">
        {/* Header */}
        <div className="flex flex-col items-center text-center gap-1">
          <span
            className={`text-sm uppercase tracking-wider ${
              status.isPaused ? 'text-accent' : 'text-text-dim'
            }`}
          >
            {status.isPaused ? 'PAUSED' : 'NOW PLAYING'}
          </span>
          <h2 className="text-xl font-semibold text-accent">
            {status.title ?? 'Unknown Title'}
          </h2>
          {status.type === 'tv' && status.season != null && status.episode != null && (
            <p className="text-text text-sm">
              S{String(status.season).padStart(2, '0')}E
              {String(status.episode).padStart(2, '0')}
              {status.episodeTitle ? ` — ${status.episodeTitle}` : ''}
            </p>
          )}
          {status.fileName && (
            <p className="font-mono text-text-dim text-sm truncate max-w-full mt-1">
              {status.fileName}
            </p>
          )}
        </div>

        {/* Seek bar */}
        <div className="mt-5">
          <div
            className="h-2 w-full bg-surface-3 rounded cursor-pointer"
            onClick={handleSeek}
          >
            <div
              className="h-full bg-accent rounded"
              style={{ width: `${progressPercent}%` }}
            />
          </div>
          <div className="flex justify-between mt-1">
            <span className="font-mono text-sm text-text-dim">
              {formatMs(status.positionMs)}
            </span>
            <span className="font-mono text-sm text-text-dim">
              {formatMs(status.durationMs)}
            </span>
          </div>
        </div>

        {/* Playback controls */}
        <div className="flex items-center justify-center gap-3 py-4">
          <button
            onClick={handleSkipBack}
            className="bg-surface-2 rounded-lg px-3 py-2 text-sm text-text hover:bg-surface-3 transition-colors"
          >
            -30
          </button>
          <button
            onClick={handlePrevEpisode}
            disabled={!status.prevEpisode}
            className="bg-surface-2 rounded-lg px-3 py-2 text-text hover:bg-surface-3 transition-colors disabled:opacity-30 disabled:cursor-not-allowed"
          >
            ⏮
          </button>
          <button
            onClick={handlePlayPause}
            className="bg-accent hover:bg-accent-hover rounded-full w-12 h-12 flex items-center justify-center text-lg text-white transition-colors"
          >
            {status.isPaused ? '▶' : '⏸'}
          </button>
          <button
            onClick={handleNextEpisode}
            disabled={!status.nextEpisode}
            className="bg-surface-2 rounded-lg px-3 py-2 text-text hover:bg-surface-3 transition-colors disabled:opacity-30 disabled:cursor-not-allowed"
          >
            ⏭
          </button>
          <button
            onClick={handleSkipForward}
            className="bg-surface-2 rounded-lg px-3 py-2 text-sm text-text hover:bg-surface-3 transition-colors"
          >
            +30
          </button>
        </div>

        {/* Volume */}
        <div className="border-t border-border pt-4 mt-4">
          <div className="flex items-center gap-3">
            <button
              onClick={handleMute}
              className="text-text hover:text-accent transition-colors text-lg shrink-0"
            >
              {status.muted ? '🔇' : '🔊'}
            </button>
            <button
              onClick={handleVolumeDown}
              className="bg-surface-2 rounded-lg px-2 py-1 text-xs text-text hover:bg-surface-3 transition-colors shrink-0"
            >
              −
            </button>
            <div className="h-2 flex-1 bg-surface-3 rounded">
              <div
                className="h-full bg-accent rounded"
                style={{ width: `${status.muted ? 0 : volumePercent}%` }}
              />
            </div>
            <button
              onClick={handleVolumeUp}
              className="bg-surface-2 rounded-lg px-2 py-1 text-xs text-text hover:bg-surface-3 transition-colors shrink-0"
            >
              +
            </button>
            <span className="font-mono text-sm text-text-dim w-10 text-right shrink-0">
              {status.muted ? 0 : volumePercent}%
            </span>
          </div>
        </div>

        {/* Episode navigation */}
        {(status.prevEpisode || status.nextEpisode) && (
          <div className="border-t border-border pt-4 mt-4 flex gap-3">
            <button
              onClick={handlePrevEpisode}
              disabled={!status.prevEpisode}
              className="flex-1 bg-surface-2 rounded-lg p-3 text-left hover:bg-surface-3 transition-colors disabled:opacity-30 disabled:cursor-not-allowed"
            >
              <span className="text-sm text-text">◀ Previous</span>
              {status.prevEpisode && (
                <p className="text-xs text-text-dim truncate mt-1">
                  E{String(status.prevEpisode.episode).padStart(2, '0')}
                  {status.prevEpisode.title ? ` — ${status.prevEpisode.title}` : ''}
                </p>
              )}
            </button>
            <button
              onClick={handleNextEpisode}
              disabled={!status.nextEpisode}
              className="flex-1 bg-surface-2 rounded-lg p-3 text-right hover:bg-surface-3 transition-colors disabled:opacity-30 disabled:cursor-not-allowed"
            >
              <span className="text-sm text-text">Next ▶</span>
              {status.nextEpisode && (
                <p className="text-xs text-text-dim truncate mt-1">
                  E{String(status.nextEpisode.episode).padStart(2, '0')}
                  {status.nextEpisode.title ? ` — ${status.nextEpisode.title}` : ''}
                </p>
              )}
            </button>
          </div>
        )}
      </div>

      {/* Media Context */}
      {status.titleId && (
        <div className="w-full max-w-[600px] bg-surface-2 rounded-lg p-4">
          <div className="flex items-center gap-4">
            <div className="w-12 h-[72px] rounded overflow-hidden shrink-0 bg-gradient-to-br from-surface-3 to-surface">
              <img
                src={getPosterUrl(status.titleId)}
                alt=""
                className="w-full h-full object-cover"
                onError={(e) => {
                  (e.currentTarget as HTMLImageElement).style.display = 'none';
                }}
              />
            </div>
            <div className="min-w-0">
              <p className="text-text font-semibold truncate">
                {status.title ?? 'Unknown Title'}
              </p>
              <p className="text-text-dim text-sm truncate">
                {[
                  status.type === 'tv' && status.season != null
                    ? `Season ${status.season}`
                    : null,
                  status.type === 'tv' && status.episode != null && status.episodeCount != null
                    ? `Episode ${status.episode} of ${status.episodeCount}`
                    : status.type === 'tv' && status.episode != null
                      ? `Episode ${status.episode}`
                      : null,
                  status.year,
                  status.type === 'tv' ? 'TV' : status.type === 'movie' ? 'Movie' : status.type,
                ]
                  .filter(Boolean)
                  .join(' · ')}
              </p>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
