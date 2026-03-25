import { useState, useEffect, useRef, useCallback } from 'react';
import {
  searchMedia,
  downloadStream,
  getJobs,
  deleteJob,
  retryJob,
  SearchResponse,
  StreamResult,
  JobResponse,
} from '../../api/client';
import { formatSize, timeAgo } from '../../utils/format';

interface Props {
  addToast: (message: string, type: 'error' | 'info') => void;
}

// --- Filter types ---

type ResolutionFilter = '480p' | '720p' | '1080p' | '2160p';
type HdrFilter = 'HDR' | 'HDR10+' | 'Dolby Vision';
type AudioFilter = '2.0' | '5.1' | '7.1' | 'Atmos';
type CacheFilter = 'RD Cached';

interface StreamFilters {
  resolution: Set<ResolutionFilter>;
  hdr: Set<HdrFilter>;
  audio: Set<AudioFilter>;
  cache: Set<CacheFilter>;
}

const RESOLUTION_OPTIONS: ResolutionFilter[] = ['480p', '720p', '1080p', '2160p'];
const HDR_OPTIONS: HdrFilter[] = ['HDR', 'HDR10+', 'Dolby Vision'];
const AUDIO_OPTIONS: AudioFilter[] = ['2.0', '5.1', '7.1', 'Atmos'];
const CACHE_OPTIONS: CacheFilter[] = ['RD Cached'];

type JobFilterTab = 'all' | 'active' | 'done' | 'failed';

// --- Regex helpers for parsing stream attributes ---

const RESOLUTION_PATTERNS: Record<ResolutionFilter, RegExp> = {
  '480p': /480p/i,
  '720p': /720p/i,
  '1080p': /1080p/i,
  '2160p': /2160p|4k|uhd/i,
};

const HDR_PATTERNS: Record<HdrFilter, RegExp> = {
  'HDR': /\bHDR(?!10)\b|HDR10(?!\+)/i,
  'HDR10+': /HDR10\+/i,
  'Dolby Vision': /\b(?:DV|DoVi|Dolby[\.\s-]?Vision)\b/i,
};

const AUDIO_PATTERNS: Record<AudioFilter, RegExp> = {
  '2.0': /\b2[\.\s]0\b|stereo/i,
  '5.1': /\b5[\.\s]1\b/i,
  '7.1': /\b7[\.\s]1\b/i,
  'Atmos': /atmos/i,
};

function parseResolutions(name: string): ResolutionFilter[] {
  return RESOLUTION_OPTIONS.filter((r) => RESOLUTION_PATTERNS[r].test(name));
}

function parseHdr(name: string): HdrFilter[] {
  return HDR_OPTIONS.filter((h) => HDR_PATTERNS[h].test(name));
}

function parseAudio(name: string): AudioFilter[] {
  return AUDIO_OPTIONS.filter((a) => AUDIO_PATTERNS[a].test(name));
}

function streamMatchesFilters(stream: StreamResult, filters: StreamFilters): boolean {
  const name = stream.name;

  // Resolution: OR within group
  if (filters.resolution.size > 0) {
    const parsed = parseResolutions(name);
    if (!parsed.some((r) => filters.resolution.has(r))) return false;
  }

  // HDR: OR within group
  if (filters.hdr.size > 0) {
    const parsed = parseHdr(name);
    if (!parsed.some((h) => filters.hdr.has(h))) return false;
  }

  // Audio: OR within group
  if (filters.audio.size > 0) {
    const parsed = parseAudio(name);
    if (!parsed.some((a) => filters.audio.has(a))) return false;
  }

  // Cache: OR within group
  if (filters.cache.size > 0) {
    if (filters.cache.has('RD Cached') && !stream.isCachedRd) return false;
  }

  return true;
}

function emptyFilters(): StreamFilters {
  return {
    resolution: new Set(),
    hdr: new Set(),
    audio: new Set(),
    cache: new Set(),
  };
}

function activeFilterCount(filters: StreamFilters): number {
  return filters.resolution.size + filters.hdr.size + filters.audio.size + filters.cache.size;
}

// --- Status helpers ---

function statusBadgeClasses(status: string): string {
  switch (status) {
    case 'complete':
      return 'bg-green/15 text-green border border-green/25';
    case 'failed':
    case 'cancelled':
      return 'bg-red/15 text-red border border-red/25';
    case 'downloading':
      return 'bg-blue/15 text-blue border border-blue/25';
    case 'pending':
      return 'bg-accent/15 text-accent border border-accent/25';
    case 'waiting_for_rd':
      return 'bg-yellow/15 text-yellow border border-yellow/25';
    case 'organizing':
      return 'bg-orange/15 text-orange border border-orange/25';
    default:
      return 'bg-surface-2 text-text-dim border border-border';
  }
}

function statusLabel(status: string): string {
  return status.replace(/_/g, ' ');
}

function progressBarColor(status: string): string {
  if (status === 'complete') return 'bg-green';
  if (status === 'downloading') return 'bg-blue';
  if (status === 'organizing') return 'bg-orange';
  return 'bg-accent';
}

function isActiveStatus(status: string): boolean {
  return ['downloading', 'pending', 'waiting_for_rd', 'organizing'].includes(status);
}

function isDoneStatus(status: string): boolean {
  return status === 'complete';
}

function isFailedStatus(status: string): boolean {
  return status === 'failed' || status === 'cancelled';
}

function jobTitle(job: JobResponse): string {
  const base = job.title?.title ?? job.query ?? 'Unknown';
  const parts: string[] = [base];
  if (job.season != null) {
    parts.push(`S${String(job.season).padStart(2, '0')}`);
    if (job.episode != null) {
      parts[parts.length - 1] += `E${String(job.episode).padStart(2, '0')}`;
    }
  }
  return parts.join(' \u2014 ');
}

// --- Component ---

export default function QueueTab({ addToast }: Props) {
  // Search state
  const [query, setQuery] = useState('');
  const [searching, setSearching] = useState(false);
  const [searchResult, setSearchResult] = useState<SearchResponse | null>(null);
  const [downloadingIndices, setDownloadingIndices] = useState<Set<number>>(new Set());

  // Stream filters
  const [filters, setFilters] = useState<StreamFilters>(emptyFilters);

  // Pagination
  const [perPage, setPerPage] = useState(10);
  const [currentPage, setCurrentPage] = useState(1);

  // Jobs
  const [jobs, setJobs] = useState<JobResponse[]>([]);
  const [jobFilter, setJobFilter] = useState<JobFilterTab>('all');
  const [deletingJobs, setDeletingJobs] = useState<Set<string>>(new Set());
  const [retryingJobs, setRetryingJobs] = useState<Set<string>>(new Set());
  const jobPollRef = useRef<ReturnType<typeof setInterval> | null>(null);

  // --- Search ---

  const handleSearch = useCallback(async () => {
    const q = query.trim();
    if (!q) return;
    setSearching(true);
    setSearchResult(null);
    setFilters(emptyFilters());
    setCurrentPage(1);
    try {
      const result = await searchMedia(q);
      setSearchResult(result);
      if (result.warning) {
        addToast(result.warning, 'info');
      }
    } catch (err: unknown) {
      addToast(err instanceof Error ? err.message : 'Search failed', 'error');
    } finally {
      setSearching(false);
    }
  }, [query, addToast]);

  const handleSearchKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === 'Enter') handleSearch();
  };

  // --- Download ---

  const handleDownload = useCallback(
    async (stream: StreamResult) => {
      if (!searchResult) return;
      setDownloadingIndices((prev) => new Set(prev).add(stream.index));
      try {
        await downloadStream(searchResult.searchId, stream.index);
        addToast(`Download started \u2014 ${searchResult.media.title}`, 'info');
      } catch (err: unknown) {
        addToast(err instanceof Error ? err.message : 'Download failed', 'error');
      } finally {
        setDownloadingIndices((prev) => {
          const next = new Set(prev);
          next.delete(stream.index);
          return next;
        });
      }
    },
    [searchResult, addToast],
  );

  // --- Stream filtering and pagination ---

  const filteredStreams = searchResult
    ? searchResult.streams.filter((s) => streamMatchesFilters(s, filters))
    : [];

  const totalFiltered = filteredStreams.length;
  const totalPages = Math.max(1, Math.ceil(totalFiltered / perPage));
  const safePage = Math.min(currentPage, totalPages);
  const pagedStreams = filteredStreams.slice((safePage - 1) * perPage, safePage * perPage);

  // Reset page when filters or perPage change
  useEffect(() => {
    setCurrentPage(1);
  }, [filters, perPage]);

  function toggleFilter<T>(
    group: keyof StreamFilters,
    value: T,
  ) {
    setFilters((prev) => {
      const next = { ...prev };
      const set = new Set(prev[group] as Set<T>);
      if (set.has(value)) {
        set.delete(value);
      } else {
        set.add(value);
      }
      (next[group] as Set<T>) = set;
      return next;
    });
  }

  const totalActiveFilters = activeFilterCount(filters);

  // --- Jobs polling ---

  const fetchJobs = useCallback(async () => {
    try {
      const result = await getJobs();
      setJobs(result.jobs);
    } catch {
      // Silently fail on poll
    }
  }, []);

  useEffect(() => {
    fetchJobs();
    jobPollRef.current = setInterval(fetchJobs, 5000);
    return () => {
      if (jobPollRef.current) clearInterval(jobPollRef.current);
    };
  }, [fetchJobs]);

  const handleDeleteJob = useCallback(
    async (id: string) => {
      setDeletingJobs((prev) => new Set(prev).add(id));
      try {
        await deleteJob(id);
        setJobs((prev) => prev.filter((j) => j.id !== id));
      } catch (err: unknown) {
        addToast(err instanceof Error ? err.message : 'Failed to delete job', 'error');
      } finally {
        setDeletingJobs((prev) => {
          const next = new Set(prev);
          next.delete(id);
          return next;
        });
      }
    },
    [addToast],
  );

  const handleRetryJob = useCallback(
    async (id: string) => {
      setRetryingJobs((prev) => new Set(prev).add(id));
      try {
        await retryJob(id);
        addToast('Job retry started', 'info');
        fetchJobs();
      } catch (err: unknown) {
        addToast(err instanceof Error ? err.message : 'Failed to retry job', 'error');
      } finally {
        setRetryingJobs((prev) => {
          const next = new Set(prev);
          next.delete(id);
          return next;
        });
      }
    },
    [addToast, fetchJobs],
  );

  // --- Job filtering ---

  const filteredJobs = jobs.filter((j) => {
    switch (jobFilter) {
      case 'active':
        return isActiveStatus(j.status);
      case 'done':
        return isDoneStatus(j.status);
      case 'failed':
        return isFailedStatus(j.status);
      default:
        return true;
    }
  });

  const jobFilterTabs: { key: JobFilterTab; label: string }[] = [
    { key: 'all', label: 'All' },
    { key: 'active', label: 'Active' },
    { key: 'done', label: 'Done' },
    { key: 'failed', label: 'Failed' },
  ];

  // --- Render ---

  return (
    <div className="space-y-6">
      {/* Search bar */}
      <div className="flex gap-3">
        <input
          type="text"
          placeholder="Search movies and TV shows..."
          value={query}
          onChange={(e) => setQuery(e.target.value)}
          onKeyDown={handleSearchKeyDown}
          className="flex-1 px-4 py-2.5 bg-surface-2 border border-border rounded-lg text-text text-sm
                     outline-none placeholder:text-text-dim focus:border-accent transition-colors"
        />
        <button
          onClick={handleSearch}
          disabled={!query.trim() || searching}
          className="px-5 py-2.5 bg-accent text-white rounded-lg text-sm font-medium
                     hover:bg-accent-hover transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
        >
          {searching ? 'Searching...' : 'Search'}
        </button>
      </div>

      {/* Search result */}
      {searchResult && (
        <div className="bg-surface border border-border rounded-xl p-5">
          {/* Media info */}
          <div className="flex gap-4 mb-4">
            <div className="w-[120px] h-[180px] rounded-lg bg-surface-2 overflow-hidden shrink-0">
              {searchResult.media.posterUrl ? (
                <img
                  src={searchResult.media.posterUrl}
                  alt={searchResult.media.title}
                  className="w-full h-full object-cover"
                />
              ) : (
                <div className="w-full h-full flex items-center justify-center text-5xl font-bold text-text-dim/30">
                  {searchResult.media.title.charAt(0)}
                </div>
              )}
            </div>
            <div className="flex-1 min-w-0">
              <h2 className="text-xl font-semibold mb-1">{searchResult.media.title}</h2>
              <div className="flex flex-wrap gap-2 mb-2">
                <span
                  className={`px-2 py-0.5 rounded text-xs font-semibold uppercase border ${
                    searchResult.media.type === 'tv'
                      ? 'bg-blue/10 text-blue border-blue/25'
                      : 'bg-accent/10 text-accent border-accent/25'
                  }`}
                >
                  {searchResult.media.type}
                </span>
                {searchResult.media.year && (
                  <span className="px-2 py-0.5 rounded text-xs font-semibold bg-surface-2 text-text-dim border border-border">
                    {searchResult.media.year}
                  </span>
                )}
                {searchResult.media.imdbId && (
                  <span className="px-2 py-0.5 rounded text-xs font-semibold bg-yellow/10 text-yellow border border-yellow/25">
                    IMDb {searchResult.media.imdbId}
                  </span>
                )}
              </div>
              {searchResult.media.overview && (
                <p className="text-sm text-text-dim leading-relaxed">{searchResult.media.overview}</p>
              )}
            </div>
          </div>

          {/* Stream filters */}
          {searchResult.streams.length > 0 && (
            <div className="bg-surface-2 border border-border rounded-xl p-4 mb-4">
              {/* Resolution */}
              <div className="flex items-center gap-2 mb-3 flex-wrap">
                <span className="text-xs font-semibold uppercase tracking-wide text-text-dim min-w-[72px] shrink-0">
                  Resolution
                </span>
                {RESOLUTION_OPTIONS.map((r) => (
                  <button
                    key={r}
                    onClick={() => toggleFilter('resolution', r)}
                    className={`px-2.5 py-1 rounded-md text-xs font-medium border transition-colors select-none ${
                      filters.resolution.has(r)
                        ? 'bg-accent border-accent text-white'
                        : 'bg-surface border-border text-text-dim hover:border-accent hover:text-text'
                    }`}
                  >
                    {r}
                  </button>
                ))}
              </div>

              {/* HDR */}
              <div className="flex items-center gap-2 mb-3 flex-wrap">
                <span className="text-xs font-semibold uppercase tracking-wide text-text-dim min-w-[72px] shrink-0">
                  HDR / DV
                </span>
                {HDR_OPTIONS.map((h) => (
                  <button
                    key={h}
                    onClick={() => toggleFilter('hdr', h)}
                    className={`px-2.5 py-1 rounded-md text-xs font-medium border transition-colors select-none ${
                      filters.hdr.has(h)
                        ? 'bg-pink border-pink text-white'
                        : 'bg-surface border-border text-text-dim hover:border-accent hover:text-text'
                    }`}
                  >
                    {h}
                  </button>
                ))}
              </div>

              {/* Audio */}
              <div className="flex items-center gap-2 mb-3 flex-wrap">
                <span className="text-xs font-semibold uppercase tracking-wide text-text-dim min-w-[72px] shrink-0">
                  Audio
                </span>
                {AUDIO_OPTIONS.map((a) => (
                  <button
                    key={a}
                    onClick={() => toggleFilter('audio', a)}
                    className={`px-2.5 py-1 rounded-md text-xs font-medium border transition-colors select-none ${
                      filters.audio.has(a)
                        ? 'bg-orange border-orange text-white'
                        : 'bg-surface border-border text-text-dim hover:border-accent hover:text-text'
                    }`}
                  >
                    {a}
                  </button>
                ))}
              </div>

              {/* Cache */}
              <div className="flex items-center gap-2 flex-wrap">
                <span className="text-xs font-semibold uppercase tracking-wide text-text-dim min-w-[72px] shrink-0">
                  Cache
                </span>
                {CACHE_OPTIONS.map((c) => (
                  <button
                    key={c}
                    onClick={() => toggleFilter('cache', c)}
                    className={`px-2.5 py-1 rounded-md text-xs font-medium border transition-colors select-none ${
                      filters.cache.has(c)
                        ? 'bg-green border-green text-white'
                        : 'bg-surface border-border text-text-dim hover:border-accent hover:text-text'
                    }`}
                  >
                    {c}
                  </button>
                ))}
              </div>

              {/* Filter meta */}
              {(totalActiveFilters > 0 || searchResult.streams.length > 0) && (
                <div className="flex justify-between items-center mt-3 pt-3 border-t border-border">
                  <span className="text-xs text-text-dim">
                    Showing <strong className="text-text">{totalFiltered}</strong> of{' '}
                    <strong className="text-text">{searchResult.streams.length}</strong> streams
                    {totalActiveFilters > 0 && (
                      <span className="inline-flex items-center justify-center bg-accent text-white text-[10px] font-bold w-[18px] h-[18px] rounded-full ml-2">
                        {totalActiveFilters}
                      </span>
                    )}
                  </span>
                  {totalActiveFilters > 0 && (
                    <button
                      onClick={() => setFilters(emptyFilters())}
                      className="text-xs text-accent hover:underline"
                    >
                      Clear all
                    </button>
                  )}
                </div>
              )}
            </div>
          )}

          {/* Stream table */}
          {pagedStreams.length > 0 && (
            <div className="overflow-x-auto">
              <table className="w-full border-collapse">
                <thead>
                  <tr>
                    <th className="text-left px-3 py-2 text-xs text-text-dim uppercase tracking-wide border-b border-border">
                      Stream
                    </th>
                    <th className="text-left px-3 py-2 text-xs text-text-dim uppercase tracking-wide border-b border-border">
                      Attributes
                    </th>
                    <th className="text-left px-3 py-2 text-xs text-text-dim uppercase tracking-wide border-b border-border">
                      Size
                    </th>
                    <th className="text-left px-3 py-2 text-xs text-text-dim uppercase tracking-wide border-b border-border">
                      Seeders
                    </th>
                    <th className="px-3 py-2 border-b border-border" />
                  </tr>
                </thead>
                <tbody>
                  {pagedStreams.map((stream) => {
                    const resolutions = parseResolutions(stream.name);
                    const hdr = parseHdr(stream.name);
                    const audio = parseAudio(stream.name);
                    return (
                      <tr key={stream.index} className="hover:bg-surface-2 transition-colors">
                        <td className="px-3 py-2.5 text-sm border-b border-border max-w-xs">
                          <span className="whitespace-pre-wrap break-words">{stream.name}</span>
                        </td>
                        <td className="px-3 py-2.5 border-b border-border">
                          <div className="flex gap-1 flex-wrap">
                            {resolutions.map((r) => (
                              <span
                                key={r}
                                className="px-1.5 py-px rounded text-[10px] font-semibold bg-blue/10 text-blue border border-blue/25"
                              >
                                {r}
                              </span>
                            ))}
                            {hdr.map((h) => (
                              <span
                                key={h}
                                className="px-1.5 py-px rounded text-[10px] font-semibold bg-pink/10 text-pink border border-pink/25"
                              >
                                {h}
                              </span>
                            ))}
                            {audio.map((a) => (
                              <span
                                key={a}
                                className="px-1.5 py-px rounded text-[10px] font-semibold bg-orange/10 text-orange border border-orange/25"
                              >
                                {a}
                              </span>
                            ))}
                            {stream.isCachedRd && (
                              <span className="px-1.5 py-px rounded text-[10px] font-semibold bg-green/10 text-green border border-green/25">
                                RD CACHED
                              </span>
                            )}
                          </div>
                        </td>
                        <td className="px-3 py-2.5 text-sm border-b border-border whitespace-nowrap">
                          {formatSize(stream.sizeBytes)}
                        </td>
                        <td className="px-3 py-2.5 text-sm border-b border-border text-text-dim">
                          {stream.seeders}
                        </td>
                        <td className="px-3 py-2.5 border-b border-border">
                          <button
                            onClick={() => handleDownload(stream)}
                            disabled={downloadingIndices.has(stream.index)}
                            className="px-3 py-1.5 bg-accent text-white rounded-md text-xs font-medium
                                       hover:bg-accent-hover transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
                          >
                            {downloadingIndices.has(stream.index) ? 'Starting...' : 'Download'}
                          </button>
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          )}

          {/* Empty state for filtered streams */}
          {searchResult.streams.length > 0 && totalFiltered === 0 && (
            <p className="text-sm text-text-dim text-center py-6">
              No streams match the selected filters.
            </p>
          )}

          {/* Pagination */}
          {totalFiltered > 0 && (
            <div className="flex justify-between items-center pt-3 text-sm text-text-dim">
              <div className="flex items-center gap-2">
                <span>
                  Showing {(safePage - 1) * perPage + 1}&ndash;
                  {Math.min(safePage * perPage, totalFiltered)} of {totalFiltered}
                </span>
                <select
                  value={perPage}
                  onChange={(e) => setPerPage(Number(e.target.value))}
                  className="bg-surface-2 border border-border text-text px-2 py-1 rounded text-xs"
                >
                  <option value={5}>5 per page</option>
                  <option value={10}>10 per page</option>
                  <option value={25}>25 per page</option>
                </select>
              </div>
              <div className="flex gap-1.5">
                {Array.from({ length: totalPages }, (_, i) => i + 1).map((page) => (
                  <button
                    key={page}
                    onClick={() => setCurrentPage(page)}
                    className={`px-2.5 py-1 rounded text-xs border transition-colors ${
                      page === safePage
                        ? 'bg-accent border-accent text-white'
                        : 'bg-surface-2 border-border text-text hover:border-accent'
                    }`}
                  >
                    {page}
                  </button>
                ))}
              </div>
            </div>
          )}
        </div>
      )}

      {/* Divider */}
      <div className="h-px bg-border" />

      {/* Jobs section */}
      <div>
        <h3 className="text-base font-semibold mb-3">Jobs</h3>

        {/* Job filter bar */}
        <div className="flex items-center gap-2 mb-4">
          <span className="text-sm text-text-dim mr-1">Filter:</span>
          {jobFilterTabs.map((tab) => (
            <button
              key={tab.key}
              onClick={() => setJobFilter(tab.key)}
              className={`px-3.5 py-1.5 rounded-md text-xs font-medium border transition-colors ${
                jobFilter === tab.key
                  ? 'bg-accent border-accent text-white'
                  : 'bg-surface-2 border-border text-text-dim hover:border-accent hover:text-text'
              }`}
            >
              {tab.label}
            </button>
          ))}
          <span className="ml-auto text-xs text-text-dim">{filteredJobs.length} jobs</span>
        </div>

        {/* Job list */}
        {filteredJobs.length === 0 ? (
          <p className="text-sm text-text-dim text-center py-8">No jobs to display.</p>
        ) : (
          <div className="space-y-2.5">
            {filteredJobs.map((job) => (
              <div key={job.id} className="bg-surface border border-border rounded-xl px-4 py-3.5">
                {/* Header */}
                <div className="flex justify-between items-center mb-1.5">
                  <span className="text-sm font-medium truncate mr-3">{jobTitle(job)}</span>
                  <span
                    className={`px-2 py-0.5 rounded text-[11px] font-semibold uppercase shrink-0 ${statusBadgeClasses(
                      job.status,
                    )}`}
                  >
                    {statusLabel(job.status)}
                  </span>
                </div>

                {/* Torrent name */}
                {job.torrentName && (
                  <div className="text-xs text-text-dim truncate mb-1">{job.torrentName}</div>
                )}

                {/* Error */}
                {job.error && <div className="text-xs text-red mb-1.5">{job.error}</div>}

                {/* Meta */}
                <div className="text-xs text-text-dim mb-2">
                  {job.quality && <span>{job.quality} &middot; </span>}
                  {job.sizeBytes > 0 && <span>{formatSize(job.sizeBytes)} &middot; </span>}
                  <span>{timeAgo(job.createdAt)}</span>
                </div>

                {/* Progress bar for active jobs */}
                {isActiveStatus(job.status) && job.progress > 0 && (
                  <div className="h-1 bg-surface-2 rounded-full overflow-hidden mb-2">
                    <div
                      className={`h-full rounded-full transition-[width] duration-300 ${progressBarColor(
                        job.status,
                      )}`}
                      style={{ width: `${Math.min(100, Math.round(job.progress * 100))}%` }}
                    />
                  </div>
                )}

                {/* Actions */}
                <div className="flex gap-1.5">
                  {isFailedStatus(job.status) && (
                    <button
                      onClick={() => handleRetryJob(job.id)}
                      disabled={retryingJobs.has(job.id)}
                      className="px-2.5 py-1 rounded-md text-[11px] border border-border bg-surface-2
                                 text-text-dim hover:border-accent hover:text-text transition-colors
                                 disabled:opacity-50"
                    >
                      {retryingJobs.has(job.id) ? 'Retrying...' : 'Retry'}
                    </button>
                  )}
                  <button
                    onClick={() => handleDeleteJob(job.id)}
                    disabled={deletingJobs.has(job.id)}
                    className="px-2.5 py-1 rounded-md text-[11px] border border-border bg-surface-2
                               text-red hover:border-red transition-colors disabled:opacity-50"
                  >
                    {deletingJobs.has(job.id) ? 'Deleting...' : 'Delete'}
                  </button>
                </div>
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  );
}
