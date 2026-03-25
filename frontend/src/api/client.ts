// Types

export interface StatusResponse {
  status: string;
  moviesDir: string;
  tvDir: string;
  archiveDir: string;
}

export interface MediaResult {
  tmdbId: number;
  title: string;
  year: number | null;
  type: string;
  isAnime: boolean;
  imdbId: string | null;
  posterUrl: string | null;
  season: number | null;
  episode: number | null;
  overview: string | null;
}

export interface StreamResult {
  index: number;
  name: string;
  infoHash: string | null;
  sizeBytes: number;
  isCachedRd: boolean;
  seeders: number;
}

export interface SearchResponse {
  searchId: string;
  media: MediaResult;
  streams: StreamResult[];
  warning: string | null;
}

export interface TitleInfo {
  id: string;
  tmdbId: number | null;
  imdbId: string | null;
  title: string;
  year: number | null;
  type: string;
  isAnime: boolean;
  posterPath: string | null;
}

export interface JobResponse {
  id: string;
  titleId: string;
  query: string | null;
  season: number | null;
  episode: number | null;
  status: string;
  progress: number;
  sizeBytes: number;
  downloadedBytes: number;
  quality: string | null;
  torrentName: string | null;
  error: string | null;
  log: string | null;
  createdAt: string;
  updatedAt: string;
  title: TitleInfo | null;
}

export interface LibraryItem {
  id: string;
  tmdbId: number | null;
  imdbId: string | null;
  title: string;
  year: number | null;
  type: string;
  isAnime: boolean;
  overview: string | null;
  posterPath: string | null;
  folderName: string | null;
  fileCount: number;
  totalSize: number;
  hasArchived: boolean;
}

export interface EpisodeInfo {
  mediaItemId: string;
  episode: number | null;
  episodeTitle: string | null;
  fileName: string | null;
  filePath: string | null;
  isArchived: boolean;
  progress: { positionMs: number; durationMs: number; watched: boolean } | null;
}

export interface SeasonInfo {
  season: number;
  episodes: EpisodeInfo[];
}

export interface ProgressInfo {
  positionMs: number;
  durationMs: number;
  watched: boolean;
}

export interface MpcStatusResponse {
  reachable: boolean;
  fileName: string | null;
  state: number;
  isPlaying: boolean;
  isPaused: boolean;
  positionMs: number;
  durationMs: number;
  volume: number;
  muted: boolean;
  titleId: string | null;
  title: string | null;
  isAnime: boolean;
  season: number | null;
  episode: number | null;
  episodeTitle: string | null;
  episodeCount: number | null;
  year: number | null;
  type: string | null;
  prevEpisode: { mediaItemId: string; episode: number; title: string | null } | null;
  nextEpisode: { mediaItemId: string; episode: number; title: string | null } | null;
}

export interface VersionResponse {
  version: string;
  updateAvailable: boolean;
  latestVersion: string | null;
  releaseUrl: string | null;
}

// Error handling

async function handleResponse<T>(response: Response): Promise<T> {
  if (!response.ok) {
    const body = await response.json().catch(() => null);
    const message = body?.detail ?? body?.error ?? response.statusText;
    throw new Error(message);
  }
  return response.json();
}

// API functions

export async function checkStatus(): Promise<StatusResponse> {
  return handleResponse(await fetch('/api/status'));
}

export async function searchMedia(query: string): Promise<SearchResponse> {
  return handleResponse(await fetch('/api/search', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ query }),
  }));
}

export async function downloadStream(searchId: string, streamIndex: number) {
  return handleResponse<{ jobId: string; titleId: string; status: string }>(
    await fetch('/api/download', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ searchId, streamIndex }),
    })
  );
}

export async function getJobs(): Promise<{ jobs: JobResponse[] }> {
  return handleResponse(await fetch('/api/jobs'));
}

export async function deleteJob(id: string) {
  return handleResponse<{ ok: boolean }>(await fetch(`/api/jobs/${id}`, { method: 'DELETE' }));
}

export async function retryJob(id: string) {
  return handleResponse<{ ok: boolean }>(await fetch(`/api/jobs/${id}/retry`, { method: 'POST' }));
}

export async function getLibrary(params?: { type?: string; search?: string; force?: boolean }): Promise<{ items: LibraryItem[]; count: number }> {
  const searchParams = new URLSearchParams();
  if (params?.type) searchParams.set('type', params.type);
  if (params?.search) searchParams.set('search', params.search);
  if (params?.force) searchParams.set('force', 'true');
  const qs = searchParams.toString();
  return handleResponse(await fetch(`/api/library${qs ? `?${qs}` : ''}`));
}

export async function refreshLibrary() {
  return handleResponse<{ renamed: number; postersFetched: number; errors: string[]; totalItems: number }>(
    await fetch('/api/library/refresh', { method: 'POST' })
  );
}

export function getPosterUrl(titleId: string): string {
  return `/api/library/poster?titleId=${encodeURIComponent(titleId)}`;
}

export async function getEpisodes(titleId: string, includeArchived = false): Promise<{ seasons: SeasonInfo[] }> {
  const params = new URLSearchParams({ titleId });
  if (includeArchived) params.set('includeArchived', 'true');
  return handleResponse(await fetch(`/api/library/episodes?${params}`));
}

export async function getProgress(mediaItemId: string): Promise<ProgressInfo> {
  return handleResponse(await fetch(`/api/progress?mediaItemId=${mediaItemId}`));
}

export async function saveProgress(mediaItemId: string, positionMs: number, durationMs: number) {
  return handleResponse<{ ok: boolean }>(await fetch('/api/progress', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ mediaItemId, positionMs, durationMs }),
  }));
}

export async function getMpcStatus(): Promise<MpcStatusResponse> {
  return handleResponse(await fetch('/api/mpc/status'));
}

export async function sendMpcCommand(command: string, positionMs?: number) {
  return handleResponse<{ ok: boolean }>(await fetch('/api/mpc/command', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ command, positionMs }),
  }));
}

export async function openInMpc(mediaItemId: string, playlist?: string[]) {
  return handleResponse<{ ok: boolean }>(await fetch('/api/mpc/open', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ mediaItemId, playlist }),
  }));
}

export function buildWsUrl(): string {
  const proto = window.location.protocol === 'https:' ? 'wss:' : 'ws:';
  return `${proto}//${window.location.host}/api/mpc/stream`;
}
