import { useState, useEffect } from 'react';
import { getEpisodes, openInMpc, getPosterUrl, LibraryItem, SeasonInfo, EpisodeInfo } from '../../api/client';
import { formatSize, formatMs } from '../../utils/format';

interface Props {
  item: LibraryItem;
  onClose: () => void;
  onPlay: () => void;
}

export default function MediaModal({ item, onClose, onPlay }: Props) {
  const [seasons, setSeasons] = useState<SeasonInfo[]>([]);
  const [loading, setLoading] = useState(item.type === 'tv');
  const [error, setError] = useState<string | null>(null);
  const [expandedSeasons, setExpandedSeasons] = useState<Set<number>>(new Set());
  const [posterError, setPosterError] = useState(false);

  const isTV = item.type === 'tv';
  const letter = item.title.charAt(0).toUpperCase();

  // Fetch episodes for TV shows
  useEffect(() => {
    if (!isTV) return;
    let mounted = true;
    const fetchEpisodes = async () => {
      try {
        setLoading(true);
        setError(null);
        const data = await getEpisodes(item.id);
        if (mounted) {
          setSeasons(data.seasons);
          // Expand first season by default
          if (data.seasons.length > 0) {
            setExpandedSeasons(new Set([data.seasons[0].season]));
          }
        }
      } catch (err) {
        if (mounted) setError(err instanceof Error ? err.message : 'Failed to load episodes');
      } finally {
        if (mounted) setLoading(false);
      }
    };
    fetchEpisodes();
    return () => { mounted = false; };
  }, [item.id, isTV]);

  const toggleSeason = (seasonNum: number) => {
    setExpandedSeasons((prev) => {
      const next = new Set(prev);
      if (next.has(seasonNum)) next.delete(seasonNum);
      else next.add(seasonNum);
      return next;
    });
  };

  // Find the continue-watching episode
  const continueResult = findContinueEpisode(seasons);
  const continueEpisode = continueResult?.episode ?? null;
  const continueSeasonNum = continueResult?.seasonNum ?? undefined;

  const totalEpisodes = seasons.reduce((sum, s) => sum + s.episodes.length, 0);

  const handlePlayMovie = async () => {
    try {
      // For movies, find the first episode's media item ID (movies are stored as single episodes)
      const data = await getEpisodes(item.id);
      const ep = data.seasons[0]?.episodes[0];
      if (ep) {
        await openInMpc(ep.mediaItemId);
        onPlay();
        onClose();
      }
    } catch {
      // silently fail -- toast would require addToast prop
    }
  };

  const handlePlayEpisode = async (episode: EpisodeInfo, season: SeasonInfo) => {
    try {
      const playlist = season.episodes
        .filter((e) => e.mediaItemId)
        .map((e) => e.mediaItemId);
      await openInMpc(episode.mediaItemId, playlist);
      onPlay();
      onClose();
    } catch {
      // silently fail
    }
  };

  const handleContinue = async () => {
    if (!continueEpisode) return;
    const season = seasons.find((s) =>
      s.episodes.some((e) => e.mediaItemId === continueEpisode.mediaItemId)
    );
    if (season) {
      await handlePlayEpisode(continueEpisode, season);
    }
  };

  return (
    <div
      className="fixed inset-0 bg-black/70 flex items-start justify-center pt-10 z-50 overflow-y-auto"
      onClick={onClose}
    >
      <div
        className="bg-surface border border-border rounded-[14px] w-full max-w-[720px] my-10 animate-fadeUp"
        onClick={(e) => e.stopPropagation()}
      >
        {/* Close button */}
        <div className="flex justify-end px-4 pt-3.5">
          <button
            onClick={onClose}
            className="bg-surface-2 border border-border text-text-dim text-lg px-2.5 py-0.5 rounded-md leading-none hover:bg-surface-3 hover:text-text transition-colors"
          >
            &times;
          </button>
        </div>

        {/* Body */}
        <div className="px-5 pb-5">
          {/* Header section */}
          <div className="flex gap-4.5 mb-5">
            {/* Poster */}
            <div className="w-[150px] h-[225px] rounded-[10px] overflow-hidden flex-shrink-0 bg-surface-2">
              {!posterError ? (
                <img
                  src={getPosterUrl(item.id)}
                  alt={item.title}
                  className="w-full h-full object-cover"
                  onError={() => setPosterError(true)}
                />
              ) : (
                <div className="w-full h-full flex items-center justify-center bg-gradient-to-br from-surface-2 to-surface text-[60px] font-bold text-text-dim/20">
                  {letter}
                </div>
              )}
            </div>

            {/* Details */}
            <div className="flex-1 min-w-0">
              <h2 className="text-2xl font-semibold mb-1.5">{item.title}</h2>

              {/* Badges */}
              <div className="flex flex-wrap gap-1.5 mb-2.5">
                <span
                  className={`px-2.5 py-0.5 rounded text-[11px] font-semibold border ${
                    isTV
                      ? 'bg-blue/10 text-blue border-blue/25'
                      : 'bg-accent/10 text-accent border-accent/25'
                  }`}
                >
                  {isTV ? 'TV' : 'Movie'}
                </span>
                {item.year && (
                  <Badge>{item.year}</Badge>
                )}
                <Badge>{formatSize(item.totalSize)}</Badge>
                {isTV && totalEpisodes > 0 && (
                  <Badge>{totalEpisodes} episode{totalEpisodes !== 1 ? 's' : ''}</Badge>
                )}
                {isTV && seasons.length > 0 && (
                  <Badge>{seasons.length} season{seasons.length !== 1 ? 's' : ''}</Badge>
                )}
                {!isTV && (
                  <Badge>{item.fileCount} file{item.fileCount !== 1 ? 's' : ''}</Badge>
                )}
              </div>

              {/* Overview */}
              {item.overview && (
                <p className="text-[13px] text-text-dim leading-relaxed mb-3">
                  {item.overview}
                </p>
              )}

              {/* Folder path */}
              {item.folderName && (
                <span className="inline-block font-mono text-xs text-text-dim bg-surface-2 border border-border px-2.5 py-1 rounded">
                  {item.folderName}
                </span>
              )}

              {/* Movie: filename + play */}
              {!isTV && (
                <>
                  <button
                    onClick={handlePlayMovie}
                    className="mt-3.5 flex items-center gap-2 w-full justify-center px-5 py-2.5 bg-accent text-white rounded-lg text-sm font-medium hover:bg-accent-hover transition-colors"
                  >
                    <span>&#9654;</span>
                    Play Movie
                  </button>
                </>
              )}
            </div>
          </div>

          {/* TV: Continue Watching Banner */}
          {isTV && !loading && continueEpisode && (
            <ContinueBanner episode={continueEpisode} seasonNum={continueSeasonNum} onContinue={handleContinue} />
          )}

          {/* TV: Loading */}
          {isTV && loading && (
            <div className="text-center py-8 text-text-dim text-[13px]">
              <svg className="w-5 h-5 animate-spin mx-auto mb-2 text-accent" viewBox="0 0 24 24" fill="none">
                <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="3" />
                <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z" />
              </svg>
              Loading episodes...
            </div>
          )}

          {/* TV: Error */}
          {isTV && error && (
            <div className="text-center py-8 text-red text-sm">{error}</div>
          )}

          {/* TV: Seasons */}
          {isTV && !loading && !error && seasons.length > 0 && (
            <div className="mt-4">
              {seasons.map((season) => {
                const isExpanded = expandedSeasons.has(season.season);
                const watchedCount = season.episodes.filter(
                  (e) => e.progress?.watched
                ).length;

                return (
                  <div key={season.season} className="mb-1.5">
                    {/* Season header */}
                    <button
                      onClick={() => toggleSeason(season.season)}
                      className="w-full flex justify-between items-center px-3.5 py-2.5 bg-surface-2 border border-border rounded-lg cursor-pointer text-sm font-medium hover:border-accent transition-colors"
                    >
                      <span>
                        Season {season.season}
                        <span className="text-xs text-text-dim font-normal ml-2">
                          {season.episodes.length} episode{season.episodes.length !== 1 ? 's' : ''}
                        </span>
                      </span>
                      <div className="flex items-center gap-3">
                        <span className="text-[11px] text-green">
                          {watchedCount}/{season.episodes.length} watched
                        </span>
                        <span
                          className={`text-[11px] text-text-dim transition-transform duration-200 ${
                            isExpanded ? 'rotate-180' : ''
                          }`}
                        >
                          &#9660;
                        </span>
                      </div>
                    </button>

                    {/* Episode list */}
                    {isExpanded && (
                      <div className="bg-surface-2 border border-border rounded-lg mt-1.5 overflow-hidden">
                        {season.episodes.map((ep) => (
                          <EpisodeRow
                            key={ep.mediaItemId}
                            episode={ep}
                            season={season}
                            onPlay={handlePlayEpisode}
                          />
                        ))}
                      </div>
                    )}
                  </div>
                );
              })}
            </div>
          )}
        </div>
      </div>
    </div>
  );
}

// --- Sub-components ---

function Badge({ children }: { children: React.ReactNode }) {
  return (
    <span className="px-2.5 py-0.5 rounded text-[11px] font-semibold bg-surface-2 text-text-dim border border-border">
      {children}
    </span>
  );
}

function ContinueBanner({
  episode,
  seasonNum,
  onContinue,
}: {
  episode: EpisodeInfo;
  seasonNum?: number;
  onContinue: () => void;
}) {
  const progress = episode.progress;
  const pct = progress ? Math.round((progress.positionMs / progress.durationMs) * 100) : 0;
  const remaining = progress ? progress.durationMs - progress.positionMs : 0;
  const epCode = formatEpCode(episode, seasonNum);

  return (
    <div className="bg-gradient-to-br from-accent/8 to-accent/3 border border-accent/20 rounded-[10px] px-4 py-3.5 mb-4 flex items-center gap-3.5 border-l-4 border-l-accent">
      <div className="flex-1 min-w-0">
        <div className="text-[11px] text-accent uppercase tracking-wider font-semibold mb-0.5">
          Continue Watching
        </div>
        <div className="text-sm font-medium truncate">
          {epCode} &mdash; {episode.episodeTitle ?? 'Unknown'}
        </div>
        <div className="text-xs text-text-dim mt-0.5">
          {pct}% &middot; {formatMs(remaining)} remaining
        </div>
      </div>
      <button
        onClick={onContinue}
        className="px-4.5 py-2 bg-green text-white rounded-lg text-[13px] font-medium hover:bg-green/80 transition-colors whitespace-nowrap flex-shrink-0"
      >
        &#9654; Continue
      </button>
    </div>
  );
}

function EpisodeRow({
  episode,
  season,
  onPlay,
}: {
  episode: EpisodeInfo;
  season: SeasonInfo;
  onPlay: (ep: EpisodeInfo, season: SeasonInfo) => void;
}) {
  const progress = episode.progress;
  const isWatched = progress?.watched ?? false;
  const isInProgress = progress && !progress.watched && progress.positionMs > 0;
  const pct = progress
    ? Math.round((progress.positionMs / progress.durationMs) * 100)
    : 0;
  const remaining = progress ? progress.durationMs - progress.positionMs : 0;
  const epCode = formatEpCode(episode, season.season);

  return (
    <div
      className={`flex items-center gap-3 px-3.5 py-3 border-b border-border last:border-b-0 transition-colors hover:bg-surface-3 ${
        isInProgress ? 'bg-surface-3' : ''
      }`}
    >
      {/* Episode number */}
      <span
        className={`text-xs font-semibold min-w-[54px] font-mono ${
          isInProgress ? 'text-accent' : 'text-text-dim'
        }`}
      >
        {epCode}
      </span>

      {/* Info */}
      <div className="flex-1 min-w-0">
        <div
          className={`text-[13px] font-medium truncate ${
            isInProgress ? 'text-accent' : ''
          }`}
        >
          {episode.episodeTitle ?? 'Unknown'}
        </div>
        {episode.fileName && (
          <div className="text-[10px] text-text-dim font-mono mt-0.5 truncate">
            {episode.fileName}
          </div>
        )}
      </div>

      {/* Progress bar */}
      <div className="w-[100px] flex-shrink-0">
        {(isWatched || isInProgress) && (
          <>
            <div className="h-[3px] bg-border rounded-sm overflow-hidden mb-1">
              <div
                className={`h-full rounded-sm ${isWatched ? 'bg-green' : 'bg-accent'}`}
                style={{ width: `${isWatched ? 100 : pct}%` }}
              />
            </div>
            <div className="text-[10px] text-text-dim text-right">
              {isWatched
                ? 'Watched'
                : `${pct}% \u00b7 ${formatMs(remaining)} left`}
            </div>
          </>
        )}
      </div>

      {/* Action buttons */}
      <div className="flex gap-1.5 flex-shrink-0">
        {isWatched ? (
          <button
            onClick={() => onPlay(episode, season)}
            className="px-3 py-1 rounded-md text-[11px] font-medium bg-surface-3 text-text-dim border border-border hover:border-accent hover:text-text transition-colors"
          >
            Restart
          </button>
        ) : isInProgress ? (
          <button
            onClick={() => onPlay(episode, season)}
            className="px-3 py-1 rounded-md text-[11px] font-medium bg-green text-white hover:bg-green/80 transition-colors"
          >
            &#9654; Continue
          </button>
        ) : (
          <button
            onClick={() => onPlay(episode, season)}
            className="px-3 py-1 rounded-md text-[11px] font-medium bg-accent text-white hover:bg-accent-hover transition-colors"
          >
            &#9654; Play
          </button>
        )}
      </div>
    </div>
  );
}

// --- Helpers ---

function formatEpCode(ep: EpisodeInfo, seasonNum?: number): string {
  const epNum = ep.episode ?? 0;
  if (seasonNum != null) {
    return `S${String(seasonNum).padStart(2, '0')}E${String(epNum).padStart(2, '0')}`;
  }
  return `E${String(epNum).padStart(2, '0')}`;
}

/**
 * Find the episode to continue watching.
 * If the current in-progress episode is >85% complete, advance to the next unwatched.
 */
function findContinueEpisode(seasons: SeasonInfo[]): { episode: EpisodeInfo; seasonNum: number } | null {
  // Flatten all episodes in order with season info
  const all: { episode: EpisodeInfo; seasonNum: number }[] = [];
  for (const season of seasons) {
    for (const ep of season.episodes) {
      all.push({ episode: ep, seasonNum: season.season });
    }
  }

  // Find the first in-progress episode
  const inProgressIdx = all.findIndex(
    ({ episode: ep }) => ep.progress && !ep.progress.watched && ep.progress.positionMs > 0
  );

  if (inProgressIdx === -1) return null;

  const inProgress = all[inProgressIdx];
  const pct = inProgress.episode.progress!.positionMs / inProgress.episode.progress!.durationMs;

  // If >85% complete, advance to the next unwatched episode
  if (pct > 0.85) {
    for (let i = inProgressIdx + 1; i < all.length; i++) {
      const entry = all[i];
      if (!entry.episode.progress?.watched) {
        return entry;
      }
    }
  }

  return inProgress;
}
