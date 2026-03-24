# Media Downloader — Development Plan

> Tests omitted per request. Each step builds on the previous.

---

## Phase 1: Foundation

### Step 1 — Project scaffolding
- Create `backend/` directory with Python package structure
- Create `frontend/` directory with Vite + React + TypeScript + Tailwind
- `backend/requirements.txt` (fastapi, uvicorn, aiosqlite, httpx, python-dotenv)
- `frontend/package.json` (react, react-dom, vite, tailwindcss, typescript)
- Entry points: `backend/main.py`, `frontend/src/main.tsx`

### Step 2 — Configuration module (`backend/config.py`)
- Load `.env` via python-dotenv
- Typed config class with all settings from Section 7
- Hot-reload support (re-read `.env` on POST /settings)
- Validation for required keys (TMDB_API_KEY, REAL_DEBRID_API_KEY, dirs)

### Step 3 — Database schema + CRUD (`backend/db.py`)
- SQLite via aiosqlite
- Auto-create tables on startup: `jobs`, `media_items`, `watch_progress`
- CRUD functions: `create_job`, `get_job`, `update_job`, `append_log`, `delete_job`, `get_all_jobs`, `get_pending_jobs`
- `JobStatus` enum with all states
- `stream_data` stored as JSON text
- Timestamps as ISO 8601

### Step 4 — Version + self-update (`backend/version.py`, `backend/services/updater.py`)
- `VERSION` constant (semver) read from `version.txt` at project root
- `check_for_updates()` — compare local version against latest GitHub release/tag
- `apply_update()` — `git pull`, `pip install -r requirements.txt`, rebuild frontend (`npm run build`), restart server via subprocess
- API routes:
  - `GET /api/version` — current version + update availability
  - `POST /api/update` — trigger pull + rebuild + restart
- Frontend: version display in header, "Update available" badge, one-click update button

### Step 5 — Base HTTP client (`backend/clients/base.py`)
- Async httpx client with retry + exponential backoff
- Configurable max retries, timeout
- Shared by all external API clients

---

## Phase 2: External API Clients

### Step 6 — TMDB client (`backend/clients/tmdb.py`)
- `parse_query()` — extract season/episode from "S01E03", "Season 2", etc.; strip trailing year
- `search()` — multi-search → movie/TV result with MediaInfo dataclass
- IMDb URL resolution (IMDb ID → TMDB ID)
- Auto-detect anime (genre 16 + animation keywords)
- `get_episode_count()`, `get_episode_title()`
- `fuzzy_resolve()` — typed search → multi-search fallback → shortened title fallback
- `MediaInfo` dataclass: tmdb_id, title, year, type, imdb_id, poster_path, season, episode, overview

### Step 7 — Torrentio client (`backend/clients/torrentio.py`)
- `build_url()` — construct Stremio addon URL with optional RD key, series format for TV/anime
- `get_streams()` — fetch + parse stream list
- `parse_size()` — GB/MB/TB case-insensitive
- `parse_seeders()` — extract count from stream description
- Return list of stream dicts: name, info_hash, size_bytes, is_cached_rd, seeders

### Step 8 — Real-Debrid client (`backend/clients/realdebrid.py`)
- `is_cached()` — check instant availability
- `add_magnet()` → torrent ID
- `select_all_files()`
- `wait_until_downloaded()` — poll with 30-min timeout, progress callback
- `unrestrict_link()` → (URL, filesize)
- `unrestrict_all()` → list of (URL, size)
- `download_magnet()` — full pipeline: add → select → wait → unrestrict
- Custom `RealDebridError` exception

### Step 9 — MPC-BE client (`backend/clients/mpc.py`)
- `MPCStatus` dataclass: file, filepath, filename, state, position, duration, volume, muted
- `parse_variables()` — handle JSON, legacy OnVariable(), HTML `<p>` formats
- `get_status()` → MPCStatus with reachable flag
- Commands: play(891), pause(892), play_pause(887), stop(888), seek(889), mute(909), volume_up(907), volume_down(908)
- `ping()`, `open_file()`
- `ms_to_str()` helper

---

## Phase 3: Core Services

### Step 10 — Media organizer (`backend/services/organizer.py`)
- `sanitize()` — remove `<>"/\|?*`, replace colon with ` - `, collapse spaces
- `pick_video_file()` — find largest .mkv/.mp4/.avi in dir tree
- `build_destination()`:
  - Movies: `{MOVIES_DIR}/{Title} [{TMDB_ID}]/{Title} ({Year}).ext`
  - TV/Anime: `{TV_DIR}/{Title} [{TMDB_ID}]/S##E## - {Episode Title}.ext`
  - Season packs keep original filename
- `organize()` — move file, create dirs, overwrite duplicates

### Step 11 — Library manager (`backend/services/library.py`)
- `scan()` — walk MEDIA_DIR + TV_DIR + ANIME_DIR, find video files, extract metadata
- Cache with TTL, force bypass
- `extract_title_year()` — parse year from folder names
- `clean_title()` — remove quality tags, dots→spaces, brackets, collapse spaces
- `safe_folder_name()` — strip Windows-illegal chars
- `refresh()` — resolve titles via TMDB fuzzy_resolve, rename folders, fetch posters

### Step 12 — Job processor (`backend/services/job_processor.py`)
- Background asyncio task polling DB for PENDING jobs every 5s
- Semaphore for MAX_CONCURRENT_DOWNLOADS
- Pipeline per job:
  1. Deserialize stream_data (media + stream info)
  2. Add magnet to Real-Debrid / check cache
  3. Poll RD until downloaded (status updates → DB)
  4. Stream download to staging dir (chunked, progress to DB)
  5. Organize via MediaOrganizer
  6. Fetch poster
  7. Mark COMPLETE or FAILED with error
- `cancel_job()` — cancel asyncio task
- `cleanup_staging()` — remove temp files
- Helper utils: `filename_from_url`, `is_video_url`, `episode_from_filename`, `safe_poster_key`, `save_poster`

### Step 13 — Watch tracker (`backend/services/watch_tracker.py`)
- Background polling MPC-BE every 5s
- Track max position per file via `_max_pct` dict
- After 2 consecutive "stopped" polls → `_on_stopped()`
- File change → callback for previous file
- `_on_stopped()`: if watched >= 85%, archive file + subtitles to ARCHIVE_DIR
- `archive_file()` — move to archive, move subtitles, clean empty source folders
- Helpers: `parse_tmdb_id_from_path`, `compute_rel_path`, `remove_if_empty`, `move_folder_remnants`

### Step 14 — Progress store (`backend/services/progress.py`)
- DB-backed per-file playback progress
- `get_progress(tmdb_id, rel_path)` → {position_ms, duration_ms, watched, updated_at}
- `save_progress(tmdb_id, rel_path, position_ms, duration_ms)`
- Auto-set `watched=True` when position >= 85% of duration

---

## Phase 4: API Routes

### Step 15 — FastAPI app + system routes (`backend/main.py`, `backend/routes/system.py`)
- FastAPI app with CORS, static file serving
- Lifespan: init DB, start JobProcessor, start WatchTracker
- `GET /api/status` — health check + config summary
- `GET /api/logs` — tail server log file, default 200 lines

### Step 16 — Settings routes (`backend/routes/settings.py`)
- `GET /api/settings` — return all config keys
- `POST /api/settings` — update .env file, hot-reload config
- `GET /api/settings/test-rd` — validate Real-Debrid API key
- Strip surrounding quotes from values
- Reject unknown setting keys

### Step 17 — Search + Download + Jobs routes (`backend/routes/jobs.py`)
- `POST /api/search` — TMDB search + Torrentio streams, cache result with TTL
- `POST /api/download` — validate search_id + stream_index, create job
- `GET /api/jobs` — list all (last 200)
- `GET /api/jobs/{id}` — single job
- `DELETE /api/jobs/{id}` — cancel active / delete completed
- `POST /api/jobs/{id}/retry` — reset failed/cancelled to pending

### Step 18 — Library routes (`backend/routes/library.py`)
- `GET /api/library` — scan + return items with count
- `POST /api/library/refresh` — normalize folders, fetch posters
- `GET /api/library/poster` — serve cached poster image
- `GET /api/library/poster/tmdb` — fetch from TMDB, cache, serve
- `GET /api/library/episodes` — list episodes grouped by season with progress
- `GET /api/progress` — get playback progress
- `POST /api/progress` — save playback position

### Step 19 — MPC player routes (`backend/routes/mpc.py`)
- `GET /api/mpc/status` — player state + media context (TMDB fields, no Windows paths)
- `GET /api/mpc/stream` — SSE stream of real-time status
- `POST /api/mpc/command` — send wm_command, support position_ms for seek
- `POST /api/mpc/open` — open file by tmdb_id + rel_path, support playlist
- `POST /api/mpc/next` — next episode
- `POST /api/mpc/prev` — previous episode

---

## Phase 5: Frontend

### Step 20 — API client + types (`frontend/src/api/client.ts`)
- Type-safe fetch wrappers for all endpoints
- Snake_case key conversion
- Error handling with status codes, JSON parse fallback
- Functions: checkStatus, searchMedia, downloadStream, getJobs, deleteJob, retryJob, getLibrary, refreshLibrary, getPosterUrl, getTmdbPoster, getEpisodes, getProgress, saveProgress, getMpcStatus, sendMpcCommand, openInMpc

### Step 21 — Utility functions (`frontend/src/utils/format.ts`)
- `formatSize()` — 0→"0 B", KB, MB, GB, TB
- `formatMs()` — negative→"0:00", seconds, minutes, hours with padding
- `timeAgo()` — "just now", "Xm ago", "Xh ago", "Xd ago", localized date
- `escapeHtml()` — escape &, <, >, ", '
- `hashColor()` — deterministic HSL from string

### Step 22 — App shell (`frontend/src/App.tsx`)
- Header with "Media Downloader" title
- Tab navigation: Queue, Library, Now Playing
- Connection status indicator (poll /api/status every 30s)
- Toast notification system (error/info, auto-dismiss 5s, stacking)
- Queue tab shown by default

### Step 23 — Queue tab (`frontend/src/components/Queue/QueueTab.tsx`)
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

### Step 24 — Library tab (`frontend/src/components/Library/LibraryTab.tsx`)
- Media grid from API data
- Filter buttons: all/movies/tv/anime
- Search by title (case-insensitive)
- Refresh button with loading state
- Cards: poster, title, type badge, year, size
- Click → MediaModal

### Step 25 — Media modal (`frontend/src/components/Library/MediaModal.tsx`)
- Movie view: title, year, storage, folder, file count, Play button
- TV view: fetch episodes, group by season, expand/collapse
- Episode list: play button, progress bar, continue watching logic
- Poster image with initial-letter fallback
- Close on backdrop click / X button
- Play triggers openInMpc → onPlay + onClose callbacks

### Step 26 — Now Playing tab (`frontend/src/components/NowPlaying/NowPlayingTab.tsx`)
- MPC-BE status display (filename, position, duration)
- "MPC-BE not reachable" error state
- Play/Pause/Stop controls
- Skip +/-30 seconds with bounds capping
- Volume slider + mute toggle
- SSE stream with polling fallback
- Cleanup on unmount

---

## Phase 6: Integration & Polish

### Step 27 — Wire everything together
- Verify full flow: search → select stream → download → organize → library
- Verify watch tracking: play in MPC → progress saved → auto-archive at 85%
- Verify library refresh: folders renamed, posters fetched
- Verify settings: update .env → config hot-reloads

### Step 28 — Error handling & edge cases
- Graceful degradation when MPC-BE not running
- Handle RD API failures, TMDB rate limits
- Handle disk full, permission errors on file operations
- Cancel/retry job flows

### Step 29 — Production build & deployment
- Backend: uvicorn with production settings
- Frontend: `vite build` → serve from FastAPI static files
- Single-command startup script
