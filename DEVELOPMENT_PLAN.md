# Media Downloader — Development Plan

> Tests omitted per request. Each step builds on the previous.

---

## Phase 1: Foundation

### Step 1 — Project scaffolding
- Create .NET solution `MediaDownloader.sln` with projects:
  - `MediaDownloader.Api` — ASP.NET Core Web API (backend)
  - `MediaDownloader.Wpf` — WPF desktop app
  - `MediaDownloader.Shared` — shared models/DTOs (class library)
- Create `frontend/` directory with Vite + React + TypeScript + Tailwind
- `frontend/package.json` (react, react-dom, vite, tailwindcss, typescript)
- `frontend/tailwind.config.ts` — extend default theme with app color palette: bg (#0f0f0f), surface (#1a1a1a, #242424, #2e2e2e), border (#333), text (#e0e0e0, dim #888), accent (#6366f1, hover #818cf8), green, red, yellow, blue, orange, pink (see SPEC_PLAN.md §6 for full values)
- `frontend/vite.config.ts` — proxy `/api` to `http://localhost:8000`, proxy `/api/mpc/stream` WebSocket to `ws://localhost:8000`
- NuGet packages for Api: `Microsoft.EntityFrameworkCore.Sqlite`, `Polly`, `Serilog`, `dotenv.net`
- Entry points: `MediaDownloader.Api/Program.cs`, `frontend/src/main.tsx`

### Step 2 — Configuration module (`MediaDownloader.Api/Configuration/`)
- Load `.env` file on startup via `dotenv.net` (`DotEnv.Load()`) before building the configuration
- `AppSettings` class bound from `appsettings.json` + `.env` override via environment variables
- Typed settings: `TmdbSettings`, `RealDebridSettings`, `MediaSettings`, `MpcSettings`, `ServerSettings`
- Hot-reload support via `IOptionsMonitor<T>` (re-read on POST /settings)
- Validation via data annotations for required keys (TMDB_API_KEY, REAL_DEBRID_API_KEY, dirs)

### Step 3 — Database schema + EF Core (`MediaDownloader.Api/Data/`)
- `AppDbContext : DbContext` with SQLite provider
- Entity classes: `Title`, `MediaItem`, `Job`, `WatchProgress` (see SPEC_PLAN.md §1 for full schema)
  - `Title` — central identity (UUID PK, tmdb_id, imdb_id, title, year, type, is_anime, overview, poster_path, folder_name)
  - `MediaItem` — per-file (UUID PK, title_id FK, job_id FK nullable, season, episode, episode_title, file_path nullable, is_archived)
  - `Job` — download task (UUID PK, title_id FK, query, season, episode, status, progress, stream_data JSON, etc.)
  - `WatchProgress` — per media item (media_item_id FK PK, position_ms, duration_ms, watched)
- Auto-create/migrate database on startup
- Repository pattern: `ITitleRepository`, `IMediaItemRepository`, `IJobRepository`, `IProgressRepository`
- `JobStatus` enum in `MediaDownloader.Shared` with all states (see SPEC_PLAN.md §1 for valid transitions)
- `StreamData` stored as JSON column (EF Core value converter)
- Log append via raw SQL: `UPDATE jobs SET log = log || @line WHERE id = @id`
- Timestamps as ISO 8601 (DateTimeOffset)

### Step 4 — Version detection (`MediaDownloader.Api/Services/UpdateService.cs`)
- `VERSION` constant (semver) read from assembly version
- `CheckForUpdatesAsync()` — compare local version against latest GitHub release via `https://api.github.com/repos/psychout98/media-downloader/releases/latest`. Update checking is disabled (returns `updateAvailable: false`) if the GitHub API is unreachable.
- API endpoint:
  - `GET /api/version` — returns `{ "version", "updateAvailable", "latestVersion", "releaseUrl" }`
- No self-apply logic — update application is handled by the WPF app (downloads new installer, stops backend, launches installer)
- Frontend: version display in header, "Update available" badge with info (actual update triggered from WPF app)

### Step 5 — Base HTTP client (`MediaDownloader.Api/Clients/BaseHttpClient.cs`)
- `HttpClient` with Polly retry + exponential backoff policies
- Configurable max retries, timeout
- Registered via `IHttpClientFactory` in DI
- Shared by all external API clients

---

## Phase 2: External API Clients

### Step 6 — TMDB client (`MediaDownloader.Api/Clients/TmdbClient.cs`)
- `ParseQuery()` — extract season/episode from "S01E03", "Season 2", etc.; strip trailing year
- `SearchAsync()` — multi-search → movie/TV result with `MediaInfo` record
- IMDb URL resolution (IMDb ID → TMDB ID)
- **Anime detection:** set `IsAnime = true` when TMDB title has keyword ID `210024` ("anime"), or fallback when `genres` contains ID `16` (Animation) AND `original_language == "ja"`. `Type` remains strictly `"movie"` or `"tv"`.
- `GetEpisodeCountAsync()`, `GetEpisodeTitleAsync()`
- `FuzzyResolveAsync()` — typed search → multi-search fallback → shortened title fallback
- `MediaInfo` record: TmdbId, Title, Year, Type, IsAnime, ImdbId, PosterPath, Season, Episode, Overview

### Step 7 — Torrentio client (`MediaDownloader.Api/Clients/TorrentioClient.cs`)
- **Base URL:** `https://torrentio.strem.fun`
- `BuildUrl()` — construct Stremio addon URL:
  - Movies: `/{options}/stream/movie/{imdbId}.json`
  - TV: `/{options}/stream/series/{imdbId}:{season}:{episode}.json`
  - Options: `realdebrid={RD_API_KEY}|sort=qualitysize|limit=20` (or without RD key prefix when unset)
  - Episode defaults to 1 when null
- `GetStreamsAsync()` — fetch + parse stream list; request with Chrome User-Agent header
- `ParseSize()` — regex `💾\s*([\d.]+)\s*(GB|MB|TB)` from `title` field, case-insensitive
- `ParseSeeders()` — regex `👤\s*(\d+)` from `title` field
- `IsCachedRd` — true when response stream has a `url` field present
- Return `List<StreamInfo>`: Name, InfoHash, SizeBytes, IsCachedRd, Seeders

### Step 8 — Real-Debrid client (`MediaDownloader.Api/Clients/RealDebridClient.cs`)
- **Base URL:** `https://api.real-debrid.com/rest/1.0`
- **Auth:** `Authorization: Bearer {REAL_DEBRID_API_KEY}`
- `IsCachedAsync()` — `GET /torrents/instantAvailability/{hash}` → truthy `rd` array = cached
- `AddMagnetAsync()` — `POST /torrents/addMagnet` body `{ "magnet": "..." }` → torrent ID
- `SelectAllFilesAsync()` — `POST /torrents/selectFiles/{id}` body `{ "files": "all" }`
- `WaitUntilDownloadedAsync()` — poll `GET /torrents/info/{id}` every 30s, 30-min timeout; done statuses: `downloaded`; error statuses: `error`, `virus`, `dead`, `magnet_error`; progress callback via `IProgress<int>`
- `UnrestrictLinkAsync()` — `POST /unrestrict/link` body `{ "link": "..." }` → `(string Url, long FileSize)`
- `UnrestrictAllAsync()` → `List<(string Url, long Size)>`
- `DownloadMagnetAsync()` — full pipeline: add → select → wait → unrestrict
- Custom `RealDebridException` (mapped to HTTP 502 by error middleware)

### Step 9 — MPC-BE client (`MediaDownloader.Api/Clients/MpcClient.cs`)
- **Base URL:** `http://127.0.0.1:13579` (configurable via `MPC_BE_URL`)
- **Status:** `GET /variables.html` — parse response in order: JSON → legacy `OnVariable()` JS → HTML `<p>` format
- **Commands:** `GET /command.html?wm_command={id}` with optional `&position={ms}` for seek, `&path={encoded}` for open
- `MpcStatus` record: File, FilePath, FileName, State (0/1/2), IsPlaying, IsPaused, Position, Duration, Volume, Muted, Reachable
- Command IDs: PlayPause(887), Stop(888), Seek(889), Play(891), Pause(892), VolumeUp(907), VolumeDown(908), Mute(909), OpenFile(-1)
- `ParseVariables()` — URL-decode `filepatharg` field when present
- `PingAsync()`, `OpenFileAsync(path)`
- `MsToString()` helper

---

## Phase 3: Core Services

### Step 10 — Media organizer (`MediaDownloader.Api/Services/MediaOrganizer.cs`)
- `Sanitize()` — remove `<>"/\|?*`, replace colon with ` - `, collapse spaces
- `PickVideoFile()` — find largest .mkv/.mp4/.avi in dir tree (only used for single-file torrents)
- `BuildDestination()`:
  - Movies: `{MOVIES_DIR}/{Title} [{TMDB_ID}]/{Title} ({Year}).ext`
  - TV: `{TV_DIR}/{Title} [{TMDB_ID}]/S##E## - {Episode Title}.ext`
  - Season packs: each episode organized individually by extracted episode number; falls back to original filename if extraction fails
- `OrganizeAsync()` — move file, create dirs, overwrite duplicates

### Step 11 — Library manager (`MediaDownloader.Api/Services/LibraryManager.cs`)
- **`titles` + `media_items` tables are the source of truth** for all library data. Library API reads from database, not filesystem scans.
- `IndexAsync()` — walk `MOVIES_DIR`, `TV_DIR`, and `ARCHIVE_DIR` to discover media not yet in DB; create `titles` and `media_items` entries. Called on first startup and manual refresh. Does not replace existing DB records. Filesystem-discovered items have `job_id = null`.
- `ExtractTitleYear()` — parse year from folder names
- `CleanTitle()` — remove quality tags, dots→spaces, brackets, collapse spaces
- `SafeFolderName()` — strip Windows-illegal chars
- `RefreshAsync()` — re-index filesystem, update paths for moved files, resolve titles via TMDB FuzzyResolve, rename folders, fetch missing posters

### Step 12 — File download service (`MediaDownloader.Api/Services/FileDownloadService.cs`)
- `DownloadFileAsync(string url, string destPath, IProgress<(long downloaded, long total)> progress, CancellationToken ct)`
- Chunked streaming download using `HttpClient.GetAsync` with `HttpCompletionOption.ResponseHeadersRead`
- Read response stream in 64KB chunks, write to `FileStream`
- Report progress after each chunk via `IProgress<T>`
- Support cancellation via `CancellationToken`
- Total size from `Content-Length` header (known from RD unrestrict response)
- No resume support — failed downloads retry from RD unrestrict step
- Registered via DI, uses `IHttpClientFactory`

### Step 13 — Job processor (`MediaDownloader.Api/Services/JobProcessorService.cs`)
- `BackgroundService` polling DB for PENDING jobs every 5s
- `SemaphoreSlim` for MAX_CONCURRENT_DOWNLOADS
- Pipeline per job:
  1. Deserialize StreamData (media + stream info — see SPEC_PLAN.md §1 for JSON schema)
  2. Create `media_items` row(s) with `file_path = null` (linked to job's `title_id` and `job_id`)
  3. Add magnet to Real-Debrid / check cache
  4. Poll RD until downloaded (status updates → DB)
  5. Download file(s) via `FileDownloadService` to `{APP_DATA_DIR}/staging/{jobId}/`
  6. **Single-file torrent:** organize via MediaOrganizer (pick largest video), update `media_items.file_path`
  7. **Season pack (multiple video URLs):** download all video files sequentially; for each, extract episode number via `EpisodeFromFilename()`, look up episode title from TMDB, organize individually, update corresponding `media_items.file_path`. If episode extraction fails, keep original filename in show folder.
  8. Fetch poster into `{APP_DATA_DIR}/posters/{titleId}.jpg`
  9. Mark COMPLETE or FAILED with error
- Job progress reflects total bytes across all files for season packs
- Log append via raw SQL: `UPDATE jobs SET log = log || @line WHERE id = @id`
- `CancelJob()` — cancel via `CancellationTokenSource`
- `CleanupStaging()` — remove temp files from `{APP_DATA_DIR}/staging/`
- Helper utils: `FilenameFromUrl`, `IsVideoUrl`, `EpisodeFromFilename`, `SafePosterKey`, `SavePosterAsync`

### Step 14 — Watch tracker (`MediaDownloader.Api/Services/WatchTrackerService.cs`)
- `BackgroundService` polling MPC-BE every 5s
- Track max position per file via `ConcurrentDictionary<string, double>`
- After 2 consecutive "stopped" polls → `OnStoppedAsync()`
- File change → callback for previous file
- `OnStoppedAsync()`: if watched >= 85%, archive file + subtitles to ARCHIVE_DIR, update `media_items.is_archived` and `media_items.file_path` in DB
- `ArchiveFileAsync()` — move to archive, move subtitles, update media item record, clean empty source folders
- Helpers: `ParseTitleIdFromPath`, `LookupMediaItem`, `RemoveIfEmpty`, `MoveFolderRemnants`

### Step 15 — Progress store (`MediaDownloader.Api/Services/ProgressService.cs`)
- EF Core-backed per-file playback progress, keyed on `media_item_id`
- `GetProgressAsync(mediaItemId)` → `WatchProgressDto`
- `SaveProgressAsync(mediaItemId, positionMs, durationMs)`
- Auto-set `Watched=true` when position >= 85% of duration

---

## Phase 4: API Controllers

All controllers use the standard error envelope: `{ "error": "...", "detail": "..." }`. All responses use camelCase keys (System.Text.Json default).

### Step 16 — ASP.NET Core app + error middleware + system controller (`Program.cs`, `Middleware/ErrorHandlingMiddleware.cs`, `Controllers/SystemController.cs`)
- ASP.NET Core app with WebSocket support, static file serving (SPA fallback)
- DI registration: DbContext, services, clients, background services
- `ErrorHandlingMiddleware` — catches unhandled exceptions, maps to standard error envelope (see SPEC_PLAN.md §4.0 for exception→status mapping). Register before controllers.
- `GET /api/status` — health check + config summary (moviesDir, tvDir, archiveDir)
- `GET /api/logs` — tail server log file from `{APP_DATA_DIR}/logs/`, default 200 lines
- `GET /api/version` — current version + update availability (delegates to UpdateService)

### Step 17 — Settings controller (`Controllers/SettingsController.cs`)
- `GET /api/settings` — return all config keys
- `POST /api/settings` — update .env file, hot-reload config
- `GET /api/settings/test-rd` — validate Real-Debrid API key
- Strip surrounding quotes from values
- Reject unknown setting keys

### Step 18 — Jobs controller (`Controllers/JobsController.cs`)
- `POST /api/search` — TMDB search + Torrentio streams, cache result with `IMemoryCache` (15-minute sliding expiration). Expired searches return 404 on download attempts.
- `POST /api/download` — validate searchId + streamIndex, create/reuse `titles` row, create `media_items` row(s) with `file_path = null`, create job linked to title
- `GET /api/jobs` — list all (last 200), include nested title object
- `GET /api/jobs/{id}` — single job with nested title
- `DELETE /api/jobs/{id}` — cancel active / delete completed
- `POST /api/jobs/{id}/retry` — reset failed/cancelled to pending (see SPEC_PLAN.md §1 for valid transitions)

### Step 19 — Library controller (`Controllers/LibraryController.cs`)
- `GET /api/library` — query from database; supports `?type=movie|tv`, `?search=text` (case-insensitive), `?force=true`; return items with count
- `POST /api/library/refresh` — re-index filesystem, normalize folders, fetch posters
- `GET /api/library/poster?titleId={id}` — serve cached poster; if not cached, fetch from TMDB using title metadata, cache as `{APP_DATA_DIR}/posters/{titleId}.jpg`, then serve. 404 if no poster on TMDB.
- `GET /api/library/episodes?titleId={id}` — list episodes grouped by season with progress; optional `?includeArchived=true`
- `GET /api/progress?mediaItemId={id}` — get playback progress
- `POST /api/progress` — save playback position (body: `{ mediaItemId, positionMs, durationMs }`)

### Step 20 — MPC player controller (`Controllers/MpcController.cs`)
- `GET /api/mpc/status` — player state + enriched media context: titleId, title, isAnime, season, episode, episodeTitle, episodeCount, year, type, **prevEpisode** (mediaItemId + episode + title), **nextEpisode** (mediaItemId + episode + title). Resolve prev/next by querying `media_items` for adjacent episodes in the same title+season. No Windows paths leaked.
- `WS /api/mpc/stream` — WebSocket pushing status JSON every 2 seconds with all the same fields as GET /status (including prevEpisode/nextEpisode); server polls MPC-BE internally, enriches with DB data, and pushes each result; sends `reachable: false` with zeroed/null fields when MPC-BE unreachable
- `POST /api/mpc/command` — send wm_command, support positionMs for seek
- `POST /api/mpc/open` — open file by `mediaItemId`, support playlist of mediaItemIds; uses playback file resolution (DB lookup → filesystem search fallback → 404)
- `POST /api/mpc/next` — next episode (resolve from current file's media_item → next episode in same season/title)
- `POST /api/mpc/prev` — previous episode (resolve from current file's media_item → previous episode in same season/title)

---

## Phase 5: Frontend

### Step 21 — API client + types (`frontend/src/api/client.ts`)
- Type-safe fetch wrappers for all endpoints
- All HTTP requests use relative paths (`/api/...`) — no base URL configuration needed
- WebSocket URL constructed from `window.location`: `` `${window.location.protocol === 'https:' ? 'wss:' : 'ws:'}//${window.location.host}/api/mpc/stream` ``
- Uses camelCase JSON directly (no key conversion needed — backend uses System.Text.Json default)
- Error handling: reads `detail` first, falls back to `error`, falls back to HTTP status text
- Functions: checkStatus, searchMedia, downloadStream, getJobs, deleteJob, retryJob, getLibrary, refreshLibrary, getPosterUrl, getEpisodes, getProgress, saveProgress, getMpcStatus, sendMpcCommand, openInMpc

### Step 22 — Utility functions (`frontend/src/utils/format.ts`)
- `formatSize()` — 0→"0 B", KB, MB, GB, TB
- `formatMs()` — negative→"0:00", seconds, minutes, hours with padding
- `timeAgo()` — "just now", "Xm ago", "Xh ago", "Xd ago", localized date
- `escapeHtml()` — escape &, <, >, ", '
- `hashColor()` — deterministic HSL from string

### Step 23 — App shell (`frontend/src/App.tsx`)
- Header with "Media Downloader" title
- Tab navigation: Queue, Library, Now Playing
- Connection status indicator (poll /api/status every 30s)
- Toast notification system (error/info, auto-dismiss 5s, stacking)
- Queue tab shown by default

### Step 24 — Queue tab (`frontend/src/components/Queue/QueueTab.tsx`)
- Search input + submit button (disabled when empty)
- Search results: media poster, IMDb badge, type, overview, stream list
- Stream list: torrent name, "RD Cached" badge, seeders, size, download button
- **Stream filter bar** with toggleable chips:
  - Resolution: 480p, 720p, 1080p, 2160p
  - HDR/DV: HDR, HDR10+, Dolby Vision
  - Audio: 2.0, 5.1, 7.1, Atmos
  - Cache: RD Cached
  - Logic: OR within a group (e.g. 1080p OR 2160p), AND across groups (e.g. 2160p AND Dolby Vision AND RD Cached)
  - Parse attributes from stream name via regex
  - Active filter count badge, "Clear all" button
  - "Showing X of Y streams" counter
- Pagination (5/10/25 per page)
- Job list with polling: status badges, progress bars, error messages
- Job actions: delete, cancel, retry, re-search
- Job filters: all, active, done, failed

### Step 25 — Library tab (`frontend/src/components/Library/LibraryTab.tsx`)
- Media grid from API data (sourced from database)
- Filter buttons: all/movies/tv
- Search by title (case-insensitive)
- Refresh button with loading state
- Cards: poster, title, type badge, year, size
- Click → MediaModal

### Step 26 — Media modal (`frontend/src/components/Library/MediaModal.tsx`)
> **Canonical UI reference:** `ux-demos/library-modal.html` (supersedes the simpler modal in `ux-demos/library.html`)
- **Modal layout:** overlay with fade-up animation, 720px max-width, scrollable body
- **Header section:** poster (150×225px, initial-letter gradient fallback), title (h2), badge row (type badge color-coded blue for TV / accent for Movie, year, total size, episode count, season count for TV), overview paragraph, folder path (monospace with border)
- **Movie view:** all of the above plus filename display (monospace, dim), "Play Movie" button (accent bg, play icon); no Episodes section; no getEpisodes call
- **TV view — Continue Watching banner:** shown when a partially-watched episode exists; accent gradient border, "CONTINUE WATCHING" label, episode code + title (e.g. "S01E03 — ...And the Bag's in the River"), progress + time remaining (e.g. "62% · 33:41 remaining"), green "Continue" button. Advances to next unwatched episode if current > 85%.
- **TV view — Seasons:** fetch episodes via getEpisodes(titleId), group by season, expand/collapse with rotating arrow. First season expanded by default. Season header shows episode count (e.g. "7 episodes") and **watch progress count** in green (e.g. "2/7 watched").
- **TV view — Episode rows:** episode number (monospace, e.g. "S01E03"), episode title, **filename** (monospace, dim, e.g. "S01E03 - And the Bag's in the River.mkv"), progress bar (accent fill for in-progress, green fill for watched), percentage + time remaining or "Watched" label
- **Episode action buttons by state:** "Play" (accent) for unwatched, "Continue" (green) for in-progress, "Restart" (dim, outline) for fully watched
- **Currently-playing episode:** highlighted row with surface3 background, accent-colored number + title
- Play triggers openInMpc with playlist of all episodes in the same season → onPlay + onClose callbacks
- Close on backdrop click / X button; does NOT close on inner content click
- Loading state: spinner + "Loading episodes..." text

### Step 27 — Now Playing tab (`frontend/src/components/NowPlaying/NowPlayingTab.tsx`)
> **Canonical UI reference:** `ux-demos/now-playing.html`
- **Player card** (centered, max-width ~600px, rounded, surface bg):
  - **Now Playing header** (centered): "NOW PLAYING" label (uppercase, dim) — changes to "PAUSED" (accent color) when paused. Show title (accent), episode code + title (e.g. "S01E03 — ...And the Bag's in the River"), filename (monospace, dim)
  - **Seek bar**: full-width bar with accent fill, current time + total time below (monospace, e.g. "18:42" / "54:21")
  - **Playback controls** (centered row): -30s skip button, Previous Episode (⏮), Play/Pause primary button (larger, accent bg), Next Episode (⏭), +30s skip button. Skip buttons show "-30" / "+30" text. Skip ±30s with bounds capping.
  - **Volume section** (border-top separator): speaker icon (click to mute/unmute), volume bar with accent fill, percentage label (e.g. "72%")
  - **Episode navigation** (border-top separator): two side-by-side buttons showing "◀ Previous" / "Next ▶" with **actual episode names** (e.g. "S01E02 — Cat's in the Bag..." / "S01E04 — Cancer Man"). Data from `prevEpisode`/`nextEpisode` in WebSocket push.
- **Media Context section** (below player card, surface2 bg, rounded): poster thumbnail (48×72px, initial-letter gradient fallback), title, metadata line (e.g. "Season 1 · Episode 3 of 7 · 2008 · TV")
- **Error/disconnected state**: player card with faded camera icon, "MPC-BE not reachable" heading, help text about enabling MPC-BE web interface on port 13579 and auto-reconnect
- **WebSocket lifecycle** (see SPEC_PLAN.md §2.1 for full detail):
  1. On mount, attempt WebSocket connection to `/api/mpc/stream`
  2. On open: clear any polling interval, receive status pushes every 2s
  3. On close or error (any kind): immediately start polling `GET /api/mpc/status` every 3s, set 30s timer to retry WebSocket
  4. On successful reconnect: clear polling interval, cancel retry timer, resume pushes
  5. On unmount: close WebSocket, clear all intervals and timers
  Only one data source active at a time — never both WebSocket and polling simultaneously

---

## Phase 6: Integration & Polish

### Step 28 — Wire everything together
- Verify full flow: search → select stream → download → organize → library
- Verify watch tracking: play in MPC → progress saved → auto-archive at 85%
- Verify library refresh: folders renamed, posters fetched
- Verify settings: update .env → config hot-reloads

### Step 29 — Error handling & edge cases
- Graceful degradation when MPC-BE not running
- Handle RD API failures, TMDB rate limits
- Handle disk full, permission errors on file operations
- Cancel/retry job flows

---

## Phase 7: WPF Desktop App

### Step 30 — WPF app scaffolding (`MediaDownloader.Wpf/`)
- .NET 8 WPF project with MVVM pattern
- `App.xaml` — entry point, single-instance check via named Mutex
- `MainWindow.xaml` — custom title bar, tabbed layout (Dashboard, Settings)
- Resource dictionaries for styles and themes
- **Communicates with backend exclusively via HTTP API** — does not reference API project directly
- `ApiClient.cs` — typed `HttpClient` wrapper calling backend at `http://localhost:{PORT}`, uses DTOs from `MediaDownloader.Shared`

### Step 31 — Server manager (`MediaDownloader.Wpf/Services/ServerManager.cs`)
- Start/stop backend as child `Process`
- Monitor process health (restart on crash)
- Redirect stdout/stderr for log capture
- Expose `IsRunning` observable property

### Step 32 — Main window features
> **Canonical UI reference:** `ux-demos/wpf-app.html`
- **Dashboard tab:**
  - **Server status card**: colored dot (green=running, red=stopped, orange=updating), status text + subtitle (e.g. "http://localhost:8000 · Uptime 2h 34m"), Start/Stop button
  - **Info grid** (2×2, shown when running): Active Jobs count, Library item count, MPC-BE connection status, **Disk Free** display (free space on MOVIES_DIR drive, e.g. "412 GB")
  - **Active downloads list** (poll backend `/api/jobs?status=active` via ApiClient): each item shows name, percentage (accent), progress bar, metadata with downloaded/total bytes + **download speed** (e.g. "2.4 MB/s") + **ETA** (e.g. "2 min left"). Pending items show "Queued" + total size.
  - **Quick action buttons** (2×2 grid): Open Web UI (globe, accent bg), Media Library (folder, blue bg), Launch MPC-BE (play, green bg), Refresh Library (refresh, orange bg) — each with label + subtitle
  - **Update available card** (accent gradient border): up-arrow icon, "Update Available" title, version transition (e.g. "v1.0.0 → v1.1.0 · 3 bug fixes, 2 features"), "Update Now" button
  - **Stopped state**: status card with Start button, empty state message, update card still visible
  - **Updating state**: orange status dot, "Updating..." text, progress card with spinning icon, progress bar, step indicator log (monospace), disabled footer
  - **Footer**: version number, "View logs" link
- **Update flow:** detect via `GET /api/version`, download new `MediaDownloader-Setup-{version}.exe` to `{APP_DATA_DIR}/updates/`, stop backend, launch installer (`/SILENT` flag), exit. Inno Setup upgrades in-place and relaunches. Auto-update toggle enables unattended updates.
- **Settings tab:**
  - **Directories**: Movies, TV Shows, Archive, App Data paths (monospace, with folder browser dialogs)
  - **API Keys**: TMDB and Real-Debrid keys (masked with last 4 visible, e.g. "••••••••a7f3"); RD key shows green "VALID" badge when verified
  - **Behavior toggles**: Start on boot, Auto-start server, **Auto-update** (off by default — when enabled, applies updates automatically), Max concurrent downloads, Watch threshold
  - **MPC-BE**: URL and Executable path
  - Save/Cancel buttons, "Reset defaults" link in footer

### Step 33 — System tray integration
- `NotifyIcon` with app icon
- Right-click context menu: Open, Open Web UI, Launch MPC-BE, Refresh Library, Settings, Exit
- Minimize to tray on window close (configurable) — hint shown in UI: "Closing the window minimizes to system tray. Right-click tray icon for quick menu."
- Double-click tray icon to restore window
- Balloon notification for download completions

### Step 34 — Auto-start + single instance
- Optional Windows startup via `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` registry key
- Named Mutex to prevent multiple instances
- If second instance launched, bring existing window to foreground

---

## Phase 8: Installer

### Step 35 — Build pipeline
- `dotnet publish MediaDownloader.Api -c Release --self-contained -r win-x64` → `publish/backend/`
- `dotnet publish MediaDownloader.Wpf -c Release --self-contained -r win-x64` → `publish/app/`
- `cd frontend && npm run build` → copy `dist/` to `publish/backend/wwwroot/`
- All outputs in `publish/` ready for installer

### Step 36 — Inno Setup installer (`installer/setup.iss`)
- **Welcome page** with app name + version
- **License agreement** page (if applicable)
- **Destination folder** selection (default: `C:\Program Files\Media Downloader`)
- **Component selection:** Backend + Frontend (required), WPF App (required), MPC-BE portable (optional)
- **Configuration page:** prompt for TMDB API Key, Real-Debrid API Key, Movies directory, TV directory, Archive directory
- **Tasks:** Create desktop shortcut, create Start Menu shortcut, add to Windows startup
- **Post-install:**
  - Write `.env` with user-provided API keys and directories
  - Create default directories if they don't exist (Movies, TV, Archive)
  - Create `APP_DATA_DIR` with subdirectories (posters, staging, logs)
  - Add Windows Firewall exception for backend port (8000)
  - Register uninstaller in Add/Remove Programs
- **Uninstaller:**
  - Remove installed files, shortcuts, startup entry, firewall rule
  - Preserve user data (`.env`, `APP_DATA_DIR` with DB and posters, media folders) — prompt user
- **Supports in-place upgrade** — running installer over existing installation replaces changed files, preserves user data. WPF app uses this for automated updates via `/SILENT` flag.
- Output: `MediaDownloader-Setup-{version}.exe`
