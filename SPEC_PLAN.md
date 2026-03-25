# Media Downloader — Full Rebuild Spec Plan

## Overview

Media Downloader is an HTPC media library automation system. It searches for movies/TV via TMDB + Torrentio, downloads via Real-Debrid, organizes files into a Plex-compatible library, controls MPC-BE playback, tracks watch progress, and auto-archives watched content.

**Tech Stack:** .NET 8 (ASP.NET Core Web API) backend, React 18 + TypeScript + Vite + Tailwind frontend, C# WPF desktop app, SQLite database (via Entity Framework Core).

**JSON Convention:** All API request and response bodies use **camelCase** keys (e.g., `searchId`, `streamIndex`, `sizeBytes`, `isCachedRd`, `isAnime`). This is the default `System.Text.Json` behavior. The frontend API client uses response JSON directly without key conversion. Database column names (snake_case) and `.env` keys (SCREAMING_SNAKE) are internal and mapped by EF Core / the configuration module respectively.

**Error Response Convention:** All API error responses use a standard envelope:
```json
{ "error": "not_found", "detail": "No job found with ID abc-123" }
```
- `error` — short, machine-readable error type (e.g., `"not_found"`, `"validation_error"`, `"rd_api_error"`, `"mpc_unreachable"`)
- `detail` — human-readable explanation, displayed to the user via toast
- HTTP status codes: `400` (bad request), `404` (not found), `422` (validation failure), `500` (server error)
- Implementation: shared error response helper or middleware; exceptions like `RealDebridException` are caught and mapped to the appropriate status + envelope
- Frontend: API client reads `detail` first, falls back to `error`, falls back to HTTP status text

---

## 1. Database Schema

### `titles`
The central identity table for all media. One row per movie or TV show.

| Column | Type | Description |
|---|---|---|
| id | TEXT PK | Internal UUID |
| tmdb_id | INTEGER | TMDB identifier (nullable — resolved later for filesystem-discovered media) |
| imdb_id | TEXT | IMDb identifier |
| title | TEXT | Display title |
| year | INTEGER | Release year |
| type | TEXT | "movie" or "tv" |
| is_anime | BOOLEAN | True for anime content (TMDB keyword 210024 or genre 16 + JP origin) |
| overview | TEXT | Description |
| poster_path | TEXT | TMDB poster path |
| folder_name | TEXT | "Title [tmdb_id]" |
| added_at | TEXT | ISO 8601 |
| updated_at | TEXT | ISO 8601 |

### `media_items`
One row per file on disk. Created early in the job pipeline with `file_path = null`, populated once the file is organized.

| Column | Type | Description |
|---|---|---|
| id | TEXT PK | UUID |
| title_id | TEXT FK | → titles |
| job_id | TEXT FK | → jobs (nullable — null for filesystem-discovered media) |
| season | INTEGER | Season number (nullable, for TV) |
| episode | INTEGER | Episode number (nullable, for TV) |
| episode_title | TEXT | Episode title (nullable, for TV) |
| file_path | TEXT | Absolute path to file (nullable — null until file is organized) |
| is_archived | BOOLEAN | True when moved to ARCHIVE_DIR |
| added_at | TEXT | ISO 8601 |
| updated_at | TEXT | ISO 8601 |

### `jobs`
One row per download task. Linked to a title, not to individual files (since one job like a season pack can produce many media items).

| Column | Type | Description |
|---|---|---|
| id | TEXT PK | UUID |
| title_id | TEXT FK | → titles |
| query | TEXT | Original search query |
| season | INTEGER | Requested season (nullable — null for movies or full-show requests) |
| episode | INTEGER | Requested episode (nullable — null for season packs) |
| status | TEXT | PENDING, SEARCHING, FOUND, ADDING_TO_RD, WAITING_FOR_RD, DOWNLOADING, ORGANIZING, COMPLETE, FAILED, CANCELLED |
| progress | REAL | 0.0–1.0 |
| size_bytes | INTEGER | Total file size |
| downloaded_bytes | INTEGER | Bytes downloaded so far |
| quality | TEXT | Human-readable quality |
| torrent_name | TEXT | Torrent name |
| rd_torrent_id | TEXT | Real-Debrid torrent ID |
| error | TEXT | Error message if failed |
| log | TEXT | Newline-delimited progress log (append via raw SQL: `UPDATE jobs SET log = log || @line WHERE id = @id`) |
| stream_data | TEXT | JSON (see schema below) |
| created_at | TEXT | ISO 8601 |
| updated_at | TEXT | ISO 8601 |

**`stream_data` JSON schema:**
```json
{
  "media": {
    "tmdbId": 1396,
    "title": "Breaking Bad",
    "year": 2008,
    "type": "tv",
    "isAnime": false,
    "imdbId": "tt0903747",
    "posterPath": "/path.jpg",
    "season": 1,
    "episode": 1,
    "overview": "..."
  },
  "stream": {
    "index": 0,
    "name": "Torrent Name Here",
    "infoHash": "abc123",
    "sizeBytes": 1500000000,
    "isCachedRd": true,
    "seeders": 45
  }
}
```

**Job status transitions:**
- Happy path: PENDING → SEARCHING → FOUND → ADDING_TO_RD → WAITING_FOR_RD → DOWNLOADING → ORGANIZING → COMPLETE
- Any active state (PENDING through ORGANIZING) → CANCELLED (via user cancel)
- Any active state → FAILED (on error)
- FAILED → PENDING (via retry)
- CANCELLED → PENDING (via retry)
- COMPLETE is terminal — no transitions out
- Retry on PENDING or any active state returns 400

### `watch_progress`
Per-file playback tracking, keyed on media item.

| Column | Type | Description |
|---|---|---|
| media_item_id | TEXT FK | → media_items |
| position_ms | INTEGER | Playback position |
| duration_ms | INTEGER | Total duration |
| watched | BOOLEAN | True when position ≥ 85% |
| updated_at | TEXT | ISO 8601 |
| PK | media_item_id | Single-column primary key |

---

## 2. API Endpoints

### Jobs (`/api`)

| Method | Path | Description |
|---|---|---|
| POST | /search | Search TMDB + Torrentio, return streams |
| POST | /download | Queue download from pre-selected stream |
| GET | /jobs | List all jobs (last 200) |
| GET | /jobs/{id} | Get single job |
| DELETE | /jobs/{id} | Cancel/delete job |
| POST | /jobs/{id}/retry | Re-queue failed/cancelled job |

### Library (`/api`)

| Method | Path | Description |
|---|---|---|
| GET | /library | Query library from database, return items + count |
| POST | /library/refresh | Normalize folders + fetch posters |
| GET | /library/poster | Serve poster (cached or fetched from TMDB on demand) |
| GET | /library/episodes | List episodes grouped by season with progress |
| GET | /progress | Get playback progress for a file |
| POST | /progress | Save playback position |

### MPC Player (`/api/mpc`)

| Method | Path | Description |
|---|---|---|
| GET | /status | Current player state + media context |
| WS | /stream | WebSocket stream of real-time status (fallback: polling) |
| POST | /command | Send wm_command to MPC-BE |
| POST | /open | Open file by mediaItemId (with optional playlist) |
| POST | /next | Skip to next episode |
| POST | /prev | Go to previous episode |

### Settings (`/api`)

| Method | Path | Description |
|---|---|---|
| GET | /settings | Read current settings |
| POST | /settings | Update .env settings (hot-reload) |
| GET | /settings/test-rd | Test Real-Debrid API key |

### System (`/api`)

| Method | Path | Description |
|---|---|---|
| GET | /status | Server health + config summary |
| GET | /logs | Tail server log file |
| GET | /version | Current version + update availability |

### 2.1 Request/Response Schemas

All responses use camelCase keys. Error responses use the standard `{ "error": "...", "detail": "..." }` envelope.

**POST /api/search**
Request: `{ "query": "Breaking Bad S01E01" }`
Response:
```json
{
  "searchId": "uuid",
  "media": {
    "tmdbId": 1396, "title": "Breaking Bad", "year": 2008,
    "type": "tv", "isAnime": false, "imdbId": "tt0903747",
    "posterUrl": "https://image.tmdb.org/t/p/w500/...",
    "season": 1, "episode": 1, "overview": "..."
  },
  "streams": [
    {
      "index": 0, "name": "Torrent Name Here",
      "infoHash": "abc123", "sizeBytes": 1500000000,
      "isCachedRd": true, "seeders": 45
    }
  ],
  "warning": null
}
```
Search results are cached via `IMemoryCache` with a 15-minute sliding expiration. Expired searches return 404 on download attempts.

**POST /api/download**
Request: `{ "searchId": "uuid", "streamIndex": 0 }`
Response (201): `{ "jobId": "uuid", "titleId": "uuid", "status": "pending" }`
Creates a `titles` row (or reuses existing by tmdbId), creates `media_items` row(s) with `file_path = null`, and creates the job linked to the title.

**GET /api/jobs**
Response: `{ "jobs": [ ...JobObject ] }`
JobObject includes all job columns (camelCase) plus nested `title` object with title metadata.

**GET /api/jobs/{id}**
Response: full JobObject (matching DB columns, camelCase) with nested `title` object.

**GET /api/library**
Query params: `?type=movie|tv` (optional filter), `?search=text` (case-insensitive title match), `?force=true` (bypass any cache)
Response: `{ "items": [...], "count": 42 }`
Items are title objects with aggregated file info (file count, total size, has archived files, etc.).

**GET /api/library/episodes**
Query params: `?titleId={id}` (required), `?includeArchived=true` (optional, include archived episodes)
Response: `{ "seasons": [{ "season": 1, "episodes": [{ "mediaItemId": "uuid", "episode": 1, "episodeTitle": "Pilot", "fileName": "S01E01 - Pilot.mkv", "filePath": "/path/...", "isArchived": false, "progress": { "positionMs": 125000, "durationMs": 3600000, "watched": false } }] }] }`

**GET /api/library/poster?titleId={id}**
Response: image/jpeg binary. Serves cached poster from `{APP_DATA_DIR}/posters/{titleId}.jpg`. If not cached, fetches from TMDB using the title's metadata, caches it, then serves it. Returns 404 only if TMDB has no poster for this title.

**GET /api/progress?mediaItemId={id}**
Response: `{ "positionMs": 125000, "durationMs": 3600000, "watched": false }`

**POST /api/progress**
Request: `{ "mediaItemId": "uuid", "positionMs": 125000, "durationMs": 3600000 }`
Response: `{ "ok": true }`

**POST /api/settings**
Request: `{ "TMDB_API_KEY": "abc", "PORT": "9000" }`
Response: `{ "ok": true, "written": ["TMDB_API_KEY", "PORT"] }`

**POST /api/mpc/command**
Request: `{ "command": "SEEK", "positionMs": 30000 }`
Response: `{ "ok": true }`

**POST /api/mpc/open**
Request: `{ "mediaItemId": "uuid", "playlist": ["mediaItemId1", "mediaItemId2", "..."] }`
Response: `{ "ok": true }`

**GET /api/version**
Response: `{ "version": "1.2.0", "updateAvailable": true, "latestVersion": "1.3.0", "releaseUrl": "https://github.com/psychout98/media-downloader/releases/latest" }`

**WebSocket /api/mpc/stream**
Server pushes every 2 seconds:
```json
{
  "reachable": true, "fileName": "S01E03 - ...And the Bag's in the River.mkv",
  "state": 2, "isPlaying": true, "isPaused": false,
  "positionMs": 125000, "durationMs": 3600000,
  "volume": 85, "muted": false,
  "titleId": "uuid", "title": "Breaking Bad", "isAnime": false,
  "season": 1, "episode": 3, "episodeTitle": "...And the Bag's in the River",
  "episodeCount": 7,
  "year": 2008, "type": "tv",
  "prevEpisode": { "mediaItemId": "uuid", "episode": 2, "title": "Cat's in the Bag..." },
  "nextEpisode": { "mediaItemId": "uuid", "episode": 4, "title": "Cancer Man" }
}
```

**WebSocket lifecycle:**
1. On mount, attempt WebSocket connection to `/api/mpc/stream`
2. On open: clear any polling interval, receive status pushes every 2s
3. On close or error (any kind): immediately start polling `GET /api/mpc/status` every 3s, and set a 30s timer to retry the WebSocket
4. On successful reconnect: clear polling interval, cancel retry timer, resume receiving pushes
5. On unmount: close WebSocket, clear all intervals and timers
Only one data source is active at a time — never both WebSocket and polling simultaneously.

---

## 3. Acceptance Criteria (from tests)

> **Note:** Acceptance criteria names use snake_case for readability and were derived from an earlier Python prototype. When implementing in C#, translate to .NET conventions: PascalCase for public methods and properties (e.g., `parse_query` → `ParseQuery`), camelCase with underscore prefix for private fields (e.g., `_running`, `_prevFile`). The behavioral requirements remain the same.

### AC-1: TMDB Client

| # | Criteria |
|---|---|
| 1.1 | MediaInfo.poster_url builds correct TMDB image CDN URL; returns None when poster_path is None |
| 1.2 | MediaInfo.display_name includes "Title (Year)" for movies; "Title S##E##" for TV; "Title Season #" for season-only |
| 1.3 | parse_query extracts season/episode from "S01E03", "Season 2", "S03", "Episode 5" formats |
| 1.4 | parse_query strips trailing year "2010" and "(2010)" from query but preserves IMDb URLs |
| 1.5 | search() returns correct movie via multi-search; returns TV with season/episode; raises ValueError on no results |
| 1.6 | search() resolves IMDb URL to correct tmdb_id |
| 1.7 | get_episode_count returns count from API; returns 0 on failure |
| 1.8 | get_episode_title returns episode name from API; returns "" on failure |
| 1.9 | fuzzy_resolve returns (title, year, poster) via typed search; falls back to multi-search; shortens title on fallback; raises ValueError when all fail |

### AC-2: Torrentio Client

| # | Criteria |
|---|---|
| 2.1 | parse_size handles GB, MB, TB (case-insensitive); returns None on no match |
| 2.2 | parse_seeders extracts count; returns 0 on no match |
| 2.3 | build_url includes RD key for cached; excludes for uncached; uses series format for TV; defaults episode to 1 |
| 2.4 | get_streams returns list with correct fields; returns empty on no imdb_id, API failure, or empty streams |

### AC-3: Real-Debrid Client

| # | Criteria |
|---|---|
| 3.1 | is_cached returns True/False correctly; returns False on API error |
| 3.2 | add_magnet returns torrent ID; raises RealDebridError on HTTP error or missing ID |
| 3.3 | select_all_files succeeds on 204; raises on failure |
| 3.4 | wait_until_downloaded returns links immediately if "downloaded"; raises on "error" status, timeout, or no links; invokes progress callback |
| 3.5 | unrestrict_link returns (URL, filesize); raises on HTTP error or missing URL |
| 3.6 | unrestrict_all returns list of (URL, size) tuples |
| 3.7 | download_magnet completes full pipeline: add → select → wait → unrestrict |

### AC-4: Job Processor

| # | Criteria |
|---|---|
| 4.1 | filename_from_url extracts filename, strips query params, decodes URL encoding; returns None for no extension, trailing slash, empty URL |
| 4.2 | is_video_url recognizes .mkv, .mp4; rejects .txt; works with query params |
| 4.3 | episode_from_filename extracts from S01E03, s02e10, E05, Ep03, anime "- 12" patterns, 3-digit episodes; returns None when no pattern |
| 4.4 | safe_poster_key strips Windows-illegal chars; leaves normal strings unchanged |
| 4.5 | save_poster returns early when poster_path is None; skips existing poster |
| 4.6 | cancel_job returns True and calls task.cancel() for active jobs; returns False for unknown |
| 4.7 | cleanup_staging removes matching files; handles nonexistent staging dir |

### AC-5: Database Operations

| # | Criteria |
|---|---|
| 5.1 | create_job returns dict with all fields |
| 5.2 | get_job returns None for missing ID; returns created job with all fields |
| 5.3 | update_job changes status, progress, and other fields |
| 5.4 | append_log adds lines with newlines |
| 5.5 | delete_job removes row; returns False for missing ID |
| 5.6 | get_all_jobs returns list ordered DESC by created_at; respects limit |
| 5.7 | get_pending_jobs returns only "pending" status ordered ASC by created_at |
| 5.8 | create_job stores stream_data JSON |
| 5.9 | JobStatus enum has all expected values |

### AC-6: Library Manager

| # | Criteria |
|---|---|
| 6.1 | scan returns empty list for empty dirs; caches results; bypasses cache with force=True; detects video files; cache expires after TTL |
| 6.2 | extract_title_year parses "(2024)", ".2024.", "- 2024" formats; returns None when no year; removes quality tags; handles multi-word titles and special chars |
| 6.3 | clean_title removes quality tags, replaces dots with spaces, removes brackets, collapses spaces, strips leading/trailing dots and dashes |
| 6.4 | safe_folder_name removes invalid chars (<>|?), replaces colon with dash, preserves alphanumeric |

### AC-7: Media Organizer

| # | Criteria |
|---|---|
| 7.1 | sanitize removes <>"/\|?*, replaces colon with dash, collapses spaces, strips dots/spaces, handles empty strings |
| 7.2 | pick_video_file picks largest video; returns None for empty dir; ignores non-video files; finds videos in subdirectories |
| 7.3 | Movie destination: `{MOVIES_DIR}/{Title} [{TMDB_ID}]/{Title} ({Year}).ext`; handles no year |
| 7.4 | TV/Anime destination: `{TV_DIR}/{Title} [{TMDB_ID}]/S##E## - {Episode Title}.ext`; handles no episode title; season pack keeps original name; default season=1 |
| 7.5 | organize moves file to destination; picks largest video from directory; raises FileNotFoundError when no videos; creates parent dirs; preserves extension; overwrites duplicates; sanitizes colons in titles |

### AC-8: Watch Tracker

| # | Criteria |
|---|---|
| 8.1 | parse_tmdb_id_from_path extracts [1396] from Windows and POSIX paths; returns None for no bracket ID; extracts first bracket; handles empty string |
| 8.2 | compute_rel_path extracts relative path from MOVIES_DIR/TV_DIR and ARCHIVE_DIR; falls back to filename |
| 8.3 | remove_if_empty removes folder with no videos; keeps folder with videos; no error on nonexistent; checks subdirectories |
| 8.4 | move_folder_remnants moves non-video files; blocks if videos remain; handles nonexistent source |
| 8.5 | Lifecycle: init sets _running=False and _prev_file=None; stop sets _running=False |
| 8.6 | Tick: records progress while playing; triggers _on_stopped after 2 stopped polls; single poll only increments counter; file change triggers callback for old file; unreachable MPC counts as stopped; 2 unreachable triggers _on_stopped |
| 8.7 | Archive: moves file to ARCHIVE_DIR; moves subtitle files too; no error on nonexistent; skips file outside media_dir; cleans up empty source folder |
| 8.8 | On stopped: archives if ≥ threshold; does NOT archive if below threshold; clears state; handles missing _max_pct entry |

### AC-9: MPC Client

| # | Criteria |
|---|---|
| 9.1 | ms_to_str: 0→"0:00", 45000→"0:45", 125000→"2:05", 3661000→"1:01:01", negative→zero |
| 9.2 | MPCStatus: file/filepath fallback, filename from Windows/POSIX paths, explicit filename preferred, state defaults to 0, is_playing/is_paused, position/duration defaults to 0, volume defaults to 100, muted defaults to False, to_dict has all keys |
| 9.3 | parse_variables: JSON format, legacy OnVariable() format, HTML `<p>` format, URL-decoded filepatharg, empty dict on failure |
| 9.4 | get_status: reachable=True on success, reachable=False on connection error |
| 9.5 | Commands: play_pause(887), play(891), pause(892), stop(888), mute(909), volume_up(907), volume_down(908), seek(889+position) |
| 9.6 | ping: True on success, False on exception |
| 9.7 | open_file: True on success, False on exception |

### AC-10: Search API

| # | Criteria |
|---|---|
| 10.1 | Empty/whitespace query returns 422 |
| 10.2 | Valid query returns search_id, media, streams |
| 10.3 | search_id is a valid UUID |
| 10.4 | Each stream has index, name, info_hash, size_bytes, is_cached_rd |
| 10.5 | Search result cached in state.searches with expires |

### AC-11: Download API

| # | Criteria |
|---|---|
| 11.1 | Invalid search_id returns 404 |
| 11.2 | Valid request returns 201 with job_id and status="pending" |
| 11.3 | stream_index out of range returns 422 |
| 11.4 | Download creates job in database with status="pending" |

### AC-12: Jobs API

| # | Criteria |
|---|---|
| 12.1 | GET /jobs returns 200 with jobs array |
| 12.2 | Empty database returns empty list |
| 12.3 | GET /jobs/{id} returns 404 for unknown ID |
| 12.4 | GET /jobs/{id} returns full job details |
| 12.5 | DELETE /jobs/{id} returns 404 for missing ID |
| 12.6 | DELETE pending job sets status to "cancelled" |
| 12.7 | DELETE completed job removes it from DB |
| 12.8 | POST /jobs/{id}/retry returns 404 for missing ID |
| 12.9 | Retry failed/cancelled job resets to "pending" |
| 12.10 | Retry active/pending job returns 400 |

### AC-13: Library API

| # | Criteria |
|---|---|
| 13.1 | GET /library returns 200 with items array and count |
| 13.2 | Empty library returns count zero |
| 13.3 | ?force=true bypasses cache |
| 13.4 | Library items have standard fields |
| 13.5 | POST /library/refresh returns renamed, posters_fetched, errors, total_items |
| 13.6 | GET /library/poster returns 404 for missing file |
| 13.7 | GET /library/poster returns 400 for directory or non-image |
| 13.8 | Valid poster returns 200 with image content-type |
| 13.9 | GET /library/episodes returns seasons array |
| 13.10 | Episodes have season, episode, title, filename, path, progress fields |
| 13.11 | Nonexistent folder returns 404 |
| 13.12 | ?includeArchived parameter accepted |
| 13.13 | GET /progress returns empty dict for missing; POST saves progress |

### AC-14: Settings API

| # | Criteria |
|---|---|
| 14.1 | GET /settings returns all config keys as strings/numbers |
| 14.2 | POST returns ok=true and written list |
| 14.3 | Unknown setting key returns 400 |
| 14.4 | Surrounding quotes stripped from values |
| 14.5 | Multiple keys updated in one request |
| 14.6 | GET /settings/test-rd returns ok and key_suffix |
| 14.7 | Invalid RD key returns ok=false with graceful error handling |

### AC-15: System API

| # | Criteria |
|---|---|
| 15.1 | GET /status returns 200 with status="ok" |
| 15.2 | Status includes moviesDir, tvDir, archiveDir config fields |
| 15.3 | GET /logs returns lines array |
| 15.4 | Default log limit is 200; supports ?lines param |
| 15.5 | GET /version returns version, updateAvailable, latestVersion, releaseUrl |

### AC-16: MPC Player Control API

| # | Criteria |
|---|---|
| 16.1 | GET /mpc/status returns all player fields including season/episode/episodeTitle/episodeCount/year/type |
| 16.2 | Status includes media context with TMDB fields + prevEpisode/nextEpisode (mediaItemId, episode number, title) resolved from DB |
| 16.3 | POST /mpc/command returns ok=true; supports position_ms for seek |
| 16.4 | POST /mpc/open returns 404 when file not found; supports playlist param |
| 16.5 | POST /mpc/next returns next episode or 404 |
| 16.6 | POST /mpc/prev returns 404 if nothing playing |
| 16.7 | WS /mpc/stream returns WebSocket connection pushing status JSON every 2 seconds |
| 16.8 | No Windows paths (C:\, D:\) leaked in media context |

### AC-17: Frontend — App Shell

| # | Criteria |
|---|---|
| 17.1 | Renders header with "Media Downloader" title |
| 17.2 | Renders Queue, Library, Now Playing tab buttons |
| 17.3 | Queue tab shown by default |
| 17.4 | Shows "Disconnected" initially; "Connected" after successful status check; "Disconnected" on failure |
| 17.5 | Polls status every 30 seconds; clears interval on unmount |
| 17.6 | Tab navigation switches between Queue, Library, Now Playing |
| 17.7 | Library onPlay callback switches to Now Playing tab |
| 17.8 | Toast notifications: renders messages, supports error/info types, auto-dismisses after 5 seconds, stacks multiple |

### AC-18: Frontend — Queue Tab

| # | Criteria |
|---|---|
| 18.1 | Renders search input and button; button disabled when empty, enabled with text |
| 18.2 | Calls searchMedia on submit; shows warning toast for API warnings; error toast on failure |
| 18.3 | Renders job list from polling; shows "No jobs found" when empty |
| 18.4 | Status badges: success for complete, error for failed, info for downloading, accent for pending |
| 18.5 | Progress bar shows percentage for active jobs; not shown for completed |
| 18.6 | Delete job on button click; cancel active job; retry failed job resets to pending |
| 18.7 | Stream list renders torrent names, "RD Cached" badge, seeders count |
| 18.8 | Download button triggers downloadStream; success/error toast |
| 18.9 | Shows media poster when search result has poster_url |
| 18.10 | Shows "No streams found" when empty; "Searching..." during load |
| 18.11 | Job displays: query when title null, progress with bytes, error message, torrent_name |
| 18.12 | Job filters: all, active, done, failed (includes cancelled) |
| 18.13 | Search results show IMDb badge, type, overview, size, stream name |
| 18.14 | Search results paginated with 5/10/25 rows per page |
| 18.15 | Stream filter bar with toggleable chips: resolution (480p, 720p, 1080p, 2160p), HDR/DV (HDR, HDR10+, Dolby Vision), audio (2.0, 5.1, 7.1, Atmos), RD Cached; filters are OR within a group, AND across groups |
| 18.16 | Filters parsed from stream name via regex; unrecognized attributes shown as-is |
| 18.17 | Active filter count badge on filter bar; "Clear all" resets filters |
| 18.18 | Filtered count shown: "Showing X of Y streams" |
| 18.19 | Inactive jobs have re-search shortcut |

### AC-19: Frontend — Library Tab

| # | Criteria |
|---|---|
| 19.1 | Renders grid from API data; shows "Library is empty" when empty |
| 19.2 | Items display title, type badge, year, size |
| 19.3 | Filter buttons for all/movies/tv work correctly |
| 19.4 | Search filters by title (case-insensitive); shows "No results found" when no matches |
| 19.5 | Refresh button calls refreshLibrary then getLibrary; shows success/error toast; disabled while loading |
| 19.6 | Clicking item opens MediaModal; close button works |

### AC-20: Frontend — Media Modal

> **Canonical UI reference:** `ux-demos/library-modal.html` (supersedes the simpler modal in `ux-demos/library.html`)

| # | Criteria |
|---|---|
| 20.1 | Movie: renders title, year badge, storage badge, folder path (monospace), file count badge, filename display (monospace); no Episodes section; no getEpisodes call |
| 20.2 | Movie: "Play Movie" button (accent color, play icon) calls openInMpc; triggers onPlay+onClose on success; error toast on failure |
| 20.3 | Poster image when set; initial-letter placeholder (gradient background, faded letter) when no poster; handles null year |
| 20.4 | TV: fetches episodes with titleId and archive params; shows spinner + "Loading episodes..." then seasons |
| 20.5 | TV: expands first season by default; collapse/expand on click; arrow rotates on expand |
| 20.6 | TV: each episode row shows: episode number (monospace, e.g. "S01E03"), episode title, filename (monospace, dim text, e.g. "S01E03 - And the Bag's in the River.mkv") |
| 20.7 | TV: progress bar per episode — purple/accent fill for in-progress, green fill for watched; percentage + time remaining text (e.g. "62% · 33:41 left") or "Watched" label |
| 20.8 | TV: episode action buttons vary by state — "Play" (accent) for unwatched, "Continue" (green) for in-progress, "Restart" (dim, outline) for watched episodes |
| 20.9 | TV: currently-playing episode row highlighted with surface3 background and accent-colored episode number + title |
| 20.10 | TV: season header shows episode count (e.g. "7 episodes") and watch progress count in green (e.g. "2/7 watched") |
| 20.11 | TV: **Continue Watching banner** at top of modal body (below poster/info, above seasons) — shows label "CONTINUE WATCHING", episode code + title (e.g. "S01E03 — ...And the Bag's in the River"), progress percentage + time remaining, and a green "Continue" button. Only shown when a partially-watched episode exists. |
| 20.12 | TV: play episode with playlist of all episodes in the same season; callbacks + toast on success/failure |
| 20.13 | TV: single-episode playlist fallback when season group not found |
| 20.14 | Modal header info: poster, title (h2), badge row (type badge color-coded movie vs TV, year, total size, episode count, season count for TV), overview paragraph, folder path (monospace with border) |
| 20.15 | Modal closes on backdrop click, × button; does NOT close on inner content click |
| 20.16 | Continue Watching button in banner advances to next unwatched episode if current > 85% watched |

### AC-21: Frontend — Now Playing Tab

> **Canonical UI reference:** `ux-demos/now-playing.html`

| # | Criteria |
|---|---|
| 21.1 | **Player card** layout: centered, max-width ~600px, rounded card with surface background |
| 21.2 | **Now Playing header** (centered): "NOW PLAYING" label (uppercase, dim), show title (accent color), episode code + title (e.g. "S01E03 — ...And the Bag's in the River"), filename (monospace, dim) |
| 21.3 | **Paused state**: header label changes to "PAUSED" (accent color), play button replaces pause button |
| 21.4 | **Seek bar**: full-width progress bar with accent fill, current time and total duration displayed below (monospace) |
| 21.5 | **Playback controls** (centered row): -30s button, Previous Episode button, Play/Pause primary button (larger, accent), Next Episode button, +30s button. Skip buttons show "-30" / "+30" text labels. |
| 21.6 | Skip ±30 seconds with bounds capping (0 to duration) |
| 21.7 | **Volume section** (below controls, separated by border): speaker icon (clickable mute toggle), volume bar with accent fill, percentage label (e.g. "72%") |
| 21.8 | **Episode navigation** (below volume, separated by border): two side-by-side buttons showing "Previous" / "Next" with actual episode names displayed (e.g. "S01E02 — Cat's in the Bag..." / "S01E04 — Cancer Man") |
| 21.9 | **Media Context section** (below player card): shows poster thumbnail (48×72px, initial-letter fallback), title, and metadata line (e.g. "Season 1 · Episode 3 of 7 · 2008 · TV") |
| 21.10 | **Error/disconnected state**: player card shows camera icon (faded), "MPC-BE not reachable" heading, help text about enabling web interface on port 13579, auto-reconnect note |
| 21.11 | Falls back to polling when WebSocket fails; retries WebSocket every 30s; cleans up on unmount |

### AC-22: Frontend — API Client

| # | Criteria |
|---|---|
| 22.1 | checkStatus fetches /api/status; throws on HTTP error with message |
| 22.2 | searchMedia POSTs to /api/search with query |
| 22.3 | downloadStream sends camelCase keys (searchId, streamIndex); returns jobId, status, message |
| 22.4 | getJobs unwraps jobs array from envelope; returns empty array |
| 22.5 | getLibrary unwraps items array; passes force parameter |
| 22.6 | getPosterUrl encodes path correctly with special characters |
| 22.7 | Error handling includes status code; handles JSON parse errors; propagates network errors; prioritizes detail over error |
| 22.8 | retryJob POSTs to /api/jobs/{id}/retry |
| 22.9 | getPosterUrl returns correct URL string with titleId for use in img src |
| 22.10 | getEpisodes unwraps seasons; includes includeArchived when provided |
| 22.11 | getProgress fetches with path param; saveProgress POSTs progress data |
| 22.12 | getMpcStatus fetches status; sendMpcCommand sends command with optional position |
| 22.13 | openInMpc sends path with optional playlist |

### AC-23: Frontend — Utility Functions

| # | Criteria |
|---|---|
| 23.1 | formatSize: 0→"0 B", bytes, KB, MB, GB, TB all formatted correctly |
| 23.2 | formatMs: negative→"0:00", seconds, minutes:seconds, hours:minutes:seconds with zero padding |
| 23.3 | timeAgo: "just now" within 60s, "Xm ago", "Xh ago", "Xd ago", localized date for old |
| 23.4 | escapeHtml: escapes &, <, >, ", '; handles normal and empty strings |
| 23.5 | hashColor: returns valid HSL, deterministic, different inputs → different colors, handles empty/special/unicode |

### AC-24: E2E — Navigation

| # | Criteria |
|---|---|
| 24.1 | App loads with "Media Downloader" header and "Connected" status |
| 24.2 | Default tab is Queue showing "Search & Download" |
| 24.3 | Library tab shows search input |
| 24.4 | Now Playing tab shows "MPC-BE not reachable" when offline |
| 24.5 | Queue tab returns to search/download view |
| 24.6 | Shows "Disconnected" when /api/status returns 500 |

### AC-25: E2E — Library

| # | Criteria |
|---|---|
| 25.1 | Displays media cards for movies, TV, anime |
| 25.2 | Filter buttons show/hide cards by type |
| 25.3 | Search filters by title (case-insensitive) |
| 25.4 | "No results found" when search has no matches |
| 25.5 | Clicking card opens detail modal with title heading |
| 25.6 | Modal fetches and displays episodes |
| 25.7 | Refresh button triggers refresh and shows result toast |
| 25.8 | Cards show type badge and year |
| 25.9 | Episodes show Play, Continue Watching, or Start from Beginning based on watch status |
| 25.10 | Episodes with watch history show seek bar |
| 25.11 | Continue Watching button advances to next episode if current > 85% |

### AC-26: E2E — Queue

| # | Criteria |
|---|---|
| 26.1 | Search button disabled when empty |
| 26.2 | Search shows media info heading |
| 26.3 | Shows stream count |
| 26.4 | Streams show torrent name, cache status, seeders |
| 26.5 | Download click shows "Download started" toast |
| 26.6 | Queue shows existing jobs on load |
| 26.7 | Job filters: active→downloading, done→complete, failed→failed, all→everything |
| 26.8 | Failed jobs show error message and Retry button |
| 26.9 | Retry shows "Job re-queued" toast |
| 26.10 | Complete jobs have Delete button; clicking shows "Job deleted" toast |
| 26.11 | Active jobs show progress percentage and status |
| 26.12 | Search results paginated: 5, 10, 25 rows per page |
| 26.13 | Stream filters: resolution chips (480p–2160p), HDR/DV chips, audio chips (2.0–Atmos), RD Cached toggle; OR within group, AND across groups; shows "Showing X of Y streams" |
| 26.14 | Inactive jobs have re-search/retry shortcut |

---

## 4. Core Services

### 4.0 Error Handling Middleware
`Middleware/ErrorHandlingMiddleware.cs` — catches unhandled exceptions and maps them to the standard `{ "error": "...", "detail": "..." }` envelope. Register in the middleware pipeline before controllers.

**Exception → HTTP status mapping:**
| Exception | Status | Error Key |
|---|---|---|
| `RealDebridException` | 502 | `rd_api_error` |
| `FileNotFoundException` | 404 | `not_found` |
| `ArgumentException` / validation failures | 422 | `validation_error` |
| `KeyNotFoundException` | 404 | `not_found` |
| All others | 500 | `server_error` |

### 4.1 JobProcessor
Background worker polling for PENDING jobs every 5s. Semaphore limits to MAX_CONCURRENT_DOWNLOADS (default 2). Pipeline per job:
1. Deserialize pre-selected stream + media from `stream_data`
2. Create `media_items` row(s) with `file_path = null` (linked to the job's `title_id` and `job_id`)
3. Add magnet to Real-Debrid (or check cache)
4. Poll RD until downloaded (30-min timeout)
5. Download file(s) via `FileDownloadService` to `{APP_DATA_DIR}/staging/{jobId}/`
6. **Single-file torrent:** organize via MediaOrganizer (pick largest video), update `media_items.file_path`
7. **Season pack (multiple video URLs):** download all video files sequentially; for each, extract episode number via `EpisodeFromFilename()`, look up episode title from TMDB, organize individually, update corresponding `media_items.file_path`. If episode extraction fails, keep original filename in the show folder.
8. Fetch TMDB poster into `{APP_DATA_DIR}/posters/{titleId}.jpg`
9. Mark COMPLETE or FAILED
- Job progress reflects total bytes across all files for season packs
- Log append uses raw SQL for atomicity: `UPDATE jobs SET log = log || @line WHERE id = @id`

### 4.2 FileDownloadService
- `DownloadFileAsync(url, destPath, progress, cancellationToken)`
- Chunked streaming download using `HttpClient.GetAsync` with `HttpCompletionOption.ResponseHeadersRead`
- Read response stream in 64KB chunks, write to `FileStream`
- Report progress after each chunk via `IProgress<(long downloaded, long total)>`
- Support cancellation via `CancellationToken`
- Total size from `Content-Length` header (known from RD unrestrict response)
- No resume support — failed downloads retry from RD unrestrict step
- Registered via DI, uses `IHttpClientFactory`

### 4.3 LibraryManager
- **IndexAsync():** walk `MOVIES_DIR`, `TV_DIR`, and `ARCHIVE_DIR` to discover media not yet in the database; add new entries. Called on first startup and manual refresh. Does not replace existing DB records.
- **The `media_items` table is the source of truth for all library data.** The library API reads from the database, not filesystem scans.
- **RefreshAsync():** re-index filesystem, update paths for moved files, resolve titles via TMDB for unresolved entries, fetch missing posters

### 4.4 MediaOrganizer
- Movies: `{MOVIES_DIR}/{Title} [{TMDB_ID}]/{Title} ({Year}).ext`
- TV: `{TV_DIR}/{Title} [{TMDB_ID}]/S##E## - {Episode Title}.ext`
- Season packs: each episode organized individually by extracted episode number; falls back to original filename if extraction fails
- `PickVideoFile()` only used for single-file torrents (picks largest video)
- Sanitize Windows-illegal chars, default season=1

### 4.5 WatchTracker
Background polling MPC-BE every 5s:
- Track max position reached per file
- After 2 consecutive "stopped" polls, trigger _on_stopped
- File change triggers callback for previous file
- If watched ≥ 85%: archive to ARCHIVE_DIR (with subtitles), update `is_archived` in DB, clean empty folders
- Clear state after callback

### 4.6 ProgressStore
EF Core-backed per-file playback progress (position_ms, duration_ms, updated_at).

### 4.7 Playback File Resolution
When opening a file for playback (MPC controller):
1. Look up path from database (`media_items.file_path`)
2. If file exists at that path, play it
3. If not found, search across `MOVIES_DIR`, `TV_DIR`, and `ARCHIVE_DIR` for a filename match; update DB record with new path; play it
4. If still not found, return 404 with message indicating file couldn't be located

---

## 5. External API Clients

### 5.1 TMDBClient
- Query parsing: extract season/episode from various formats
- Multi-search, movie search, TV search
- **Anime detection:** set `is_anime = true` when TMDB title has keyword ID `210024` ("anime"), or as fallback when `genres` contains ID `16` (Animation) AND `original_language == "ja"`. The `type` field remains strictly `"movie"` or `"tv"`.
- `MediaInfo` record includes `isAnime: bool` — set at search time, persists through job pipeline and library
- fuzzy_resolve with fallback (typed → multi → shortened title)
- Episode count and title lookup

### 5.2 TorrentioClient
Stremio addon integration using the standard Stremio addon protocol.

**Base URL:** `https://torrentio.strem.fun`

**URL format:**
- Movies: `GET /{options}/stream/movie/{imdbId}.json`
- TV/Anime: `GET /{options}/stream/series/{imdbId}:{season}:{episode}.json`
- `{options}`: with RD key → `realdebrid={RD_API_KEY}|sort=qualitysize|limit=20`; without RD → `sort=qualitysize|limit=20`
- `{season}` and `{episode}` are 1-indexed integers; episode defaults to 1 when null
- When no options: `GET /stream/movie/{imdbId}.json` (no leading options segment)

**Request headers:** `User-Agent: Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36`, `Accept: application/json`

**Response format:**
```json
{
  "streams": [
    {
      "name": "Torrentio\n480p",
      "title": "Torrent.Name.Here\n👤 45 💾 1.5 GB",
      "infoHash": "abc123def456...",
      "url": "https://..."
    }
  ]
}
```

**Parsing:**
- Seeders: regex `👤\s*(\d+)` from `title` field
- Size: regex `💾\s*([\d.]+)\s*(GB|MB|TB)` from `title` field (case-insensitive)
- `isCachedRd`: true when `url` field is present (RD has a direct download link)
- If no `url`, construct magnet: `magnet:?xt=urn:btih:{infoHash}`

**Return:** `List<StreamInfo>` with: Name, InfoHash, SizeBytes, IsCachedRd, Seeders

### 5.3 RealDebridClient
Full debrid download pipeline with polling and progress reporting.

**Base URL:** `https://api.real-debrid.com/rest/1.0`
**Auth header:** `Authorization: Bearer {REAL_DEBRID_API_KEY}`

**Endpoints:**

| Method | Path | Body | Response |
|---|---|---|---|
| GET | `/torrents/instantAvailability/{hash}` | — | `{ "{hash}": { "rd": [...] } }` — truthy `rd` = cached |
| POST | `/torrents/addMagnet` | `{ "magnet": "magnet:?xt=..." }` | `{ "id": "torrent_id" }` |
| POST | `/torrents/selectFiles/{id}` | `{ "files": "all" }` | 200/201/204 on success |
| GET | `/torrents/info/{id}` | — | `{ "id": "...", "status": "...", "progress": 0-100, "links": ["..."] }` |
| POST | `/unrestrict/link` | `{ "link": "rd_link" }` | `{ "download": "https://cdn...", "filesize": 5000000 }` |

**Torrent info statuses:**
- Done: `downloaded`
- In progress: `downloading`, `queued`, `magnet_conversion`
- Error: `error`, `virus`, `dead`, `magnet_error`

**Polling:** 30-second interval (`RD_POLL_INTERVAL`), 30-minute timeout. Progress callback via `IProgress<int>` (0–100).

**Pipeline:** `DownloadMagnetAsync(magnet, progress, ct)` → add magnet → select all files → poll until downloaded → unrestrict all links → return `List<(string Url, long FileSize)>`

**Custom exception:** `RealDebridException` for API errors, mapped to HTTP 502 by error middleware.

### 5.4 MPCClient
MPC-BE web interface integration for playback control and status monitoring.

**Base URL:** `http://127.0.0.1:13579` (configurable via `MPC_BE_URL`)

**Endpoints:**
- `GET /variables.html` — player status (supports multiple response formats)
- `GET /command.html?wm_command={id}` — send command
- `GET /command.html?wm_command=889&position={ms}` — seek to position
- `GET /command.html?wm_command=-1&path={url_encoded_windows_path}` — open file

**Command IDs:**
| ID | Command |
|---|---|
| 887 | Play/Pause toggle |
| 888 | Stop |
| 891 | Play |
| 892 | Pause |
| 907 | Volume up |
| 908 | Volume down |
| 909 | Mute toggle |
| 889 | Seek (requires `position` param in ms) |
| -1 | Open file (requires `path` param) |

**Status response parsing** (try in order, first success wins):
1. **JSON:** `{ "file": "...", "state": 2, "position": 125000, "duration": 3600000, "volumelevel": 85, "muted": 0 }`
2. **Legacy JS:** `OnVariable("file","C:\\path");OnVariable("state","2");...`
3. **HTML:** `<p id="file">path</p><p id="state">2</p>...`

**MpcStatus fields:** File, FilePath, FileName, State (0=stopped, 1=paused, 2=playing), IsPlaying, IsPaused, Position (ms), Duration (ms), Volume (0–100), Muted (bool), Reachable (bool).

URL-decode `filepatharg` field when present.

### 5.5 BaseClient
- HTTP retry with exponential backoff (Polly)
- Shared by all API clients via `IHttpClientFactory`
- Configurable max retries, timeout

---

## 6. Frontend Architecture

### Dev Workflow
During development, the React frontend runs via `npm run dev` (Vite dev server, default port 5173). Vite proxies `/api` and WebSocket requests to the backend. Configure in `vite.config.ts`:
```ts
server: {
  proxy: {
    '/api': 'http://localhost:8000',
    '/api/mpc/stream': { target: 'ws://localhost:8000', ws: true }
  }
}
```
In production, the backend serves the built frontend from `wwwroot/` with SPA fallback. CORS is not needed in production since everything is same-origin.

### Tailwind Theme
Extend the default Tailwind theme with the app's color palette (matching UX demos):
```ts
// tailwind.config.ts — extend colors
colors: {
  bg: '#0f0f0f',
  surface: { DEFAULT: '#1a1a1a', 2: '#242424', 3: '#2e2e2e' },
  border: '#333',
  text: { DEFAULT: '#e0e0e0', dim: '#888' },
  accent: { DEFAULT: '#6366f1', hover: '#818cf8' },
  green: '#22c55e',
  red: '#ef4444',
  yellow: '#eab308',
  blue: '#3b82f6',
  orange: '#f97316',
  pink: '#ec4899'
}
```

### Components
```
App.tsx                     — Tab routing, connection status polling, toast system
├── Queue/QueueTab.tsx      — Search form, stream list, job list with filters
├── Library/LibraryTab.tsx  — Media grid, search, filter buttons, refresh
│   └── Library/MediaModal.tsx — Detail view, episodes, play controls
└── NowPlaying/NowPlayingTab.tsx — Real-time player, seek, volume, next/prev
```

### API Client (`api/client.ts`)
Type-safe fetch wrappers for all endpoints. Uses camelCase JSON directly (no key conversion needed). Error handling reads `detail` first, falls back to `error`, falls back to HTTP status text.

All HTTP requests use relative paths (`/api/...`) — no base URL configuration needed. Works in both dev (Vite proxy) and production (same origin).

WebSocket URL is constructed from `window.location`: `` `${window.location.protocol === 'https:' ? 'wss:' : 'ws:'}//${window.location.host}/api/mpc/stream` ``

### Utilities (`utils/format.ts`)
formatSize, formatMs, timeAgo, escapeHtml, hashColor

---

## 7. Configuration (`.env`)

```
TMDB_API_KEY, REAL_DEBRID_API_KEY
MOVIES_DIR, TV_DIR, ARCHIVE_DIR
APP_DATA_DIR (default C:/ProgramData/MediaDownloader)
WATCH_THRESHOLD (default 0.85)
MPC_BE_URL (default http://127.0.0.1:13579)
MPC_BE_EXE (path to mpc-be64.exe)
HOST (default 0.0.0.0), PORT (default 8000)
MAX_CONCURRENT_DOWNLOADS (default 2)
RD_POLL_INTERVAL (default 30)
GITHUB_REPO (hardcoded: psychout98/media-downloader — used by UpdateService to check GitHub Releases API)
```

**`.env` loading:** Uses `dotenv.net` (`DotEnv.Load()`) on startup before building the configuration. Environment variables from `.env` override `appsettings.json` values.

**APP_DATA_DIR contents:**
- `media-downloader.db` — SQLite database
- `posters/` — cached poster images (`{titleId}.jpg`)
- `staging/` — temporary download files (`staging/{jobId}/{filename}`)
- `logs/` — server log files

User-facing directories (`MOVIES_DIR`, `TV_DIR`, `ARCHIVE_DIR`) only contain organized media files — no app metadata, posters, databases, or temp files.

---

## 8. File Naming Conventions

- **Folders:** `{Title} [{tmdb_id}]` (e.g., `Breaking Bad [1396]`)
- **Movies:** `{Title} ({Year}).ext` (e.g., `Inception (2010).mkv`)
- **Episodes:** `S{NN}E{NN} - {Episode Title}.ext` (e.g., `S01E01 - Pilot.mkv`)
- No season subfolders (flat structure)
- Colons → ` - `, strip <>"/\|?*

---

## 9. Implementation Order

### Phase 1: Foundation
1. Database schema + EF Core DbContext (titles, media_items, jobs, watch_progress) + migrations (AC-5)
2. Configuration/settings module via appsettings.json + .env override via dotenv.net (AC-14, AC-15)
3. BaseHttpClient with retry/backoff (Polly)

### Phase 2: External Clients
4. TMDBClient (AC-1)
5. TorrentioClient (AC-2)
6. RealDebridClient (AC-3)
7. MPCClient (AC-9)

### Phase 3: Core Services
8. MediaOrganizer (AC-7)
9. FileDownloadService
10. LibraryManager (AC-6)
11. JobProcessor as BackgroundService (AC-4)
12. WatchTracker as BackgroundService (AC-8)
13. ProgressStore

### Phase 4: API Controllers
14. ErrorHandlingMiddleware + SystemController (AC-15)
15. SettingsController (AC-14)
16. JobsController — search + download + CRUD (AC-10, AC-11, AC-12)
17. LibraryController (AC-13)
18. MpcController (AC-16)

### Phase 5: Frontend
19. API client + types (AC-22)
20. Utility functions (AC-23)
21. App shell + tabs + toasts (AC-17)
22. QueueTab (AC-18)
23. LibraryTab + MediaModal (AC-19, AC-20)
24. NowPlayingTab (AC-21)

### Phase 6: E2E & Integration
25. E2E tests (AC-24, AC-25, AC-26)
26. WPF desktop app
27. Installer (Inno Setup)
28. CI/CD

---

## 10. Test Strategy

- **Backend unit tests:** xUnit + Moq + EF Core in-memory provider
- **Backend integration tests:** WebApplicationFactory with mocked services
- **Frontend unit tests:** Vitest + @testing-library/react
- **Frontend E2E tests:** Playwright with mocked API routes
- **Coverage target:** 100% (enforced)

---

## 11. WPF Desktop App

### Overview
C# .NET 8 WPF application that wraps the backend server and provides a native Windows system tray experience. Manages the ASP.NET Core server lifecycle, shows active downloads, and provides quick actions. **Communicates with the backend exclusively via the HTTP API** — it does not reference the API project directly. Shared DTO classes live in `MediaDownloader.Shared`.

### Application Icon
- `MediaDownloader.Wpf/icon.ico` — committed to the repo and referenced via `<ApplicationIcon>icon.ico</ApplicationIcon>` in the csproj
- Used for: taskbar, title bar, system tray `NotifyIcon`, and Inno Setup installer

### Features

> **Canonical UI reference:** `ux-demos/wpf-app.html`

- **Server Lifecycle:** Start/stop the backend as a child process; auto-start on app launch
- **System Tray:** Minimize to tray icon; right-click context menu (Open Web UI, Launch MPC-BE, Refresh Library, Settings, Exit)
- **Main Window — Dashboard:**
  - Custom title bar with app icon ("M" badge), app name, minimize/close buttons
  - **Server status card**: colored dot (green=running, red=stopped, orange=updating), status text, uptime display (e.g. "Uptime 2h 34m"), Start/Stop button
  - **Info grid** (2×2 below status when running): Active Jobs count, Library item count, MPC-BE connection status (green "Connected" / dim "Disconnected"), **Disk Free** (e.g. "412 GB" — report free space on the drive containing MOVIES_DIR)
  - **Active downloads list**: each item shows name, percentage, progress bar (accent fill), metadata line with downloaded/total bytes, **download speed** (e.g. "2.4 MB/s"), and **ETA** (e.g. "2 min left"). Pending items show "Queued" + total size.
  - **Quick action buttons** (2×2 grid): Open Web UI (globe icon, accent bg), Media Library (folder icon, blue bg), Launch MPC-BE (play icon, green bg), Refresh Library (refresh icon, orange bg) — each with label + subtitle
  - **Update available card** (accent gradient border): up-arrow icon, "Update Available" title, version transition (e.g. "v1.0.0 → v1.1.0 · 3 bug fixes, 2 features"), "Update Now" button
  - **Footer**: version number, "View logs" link
  - Stopped state: status card with Start button, empty state message ("Server is not running"), update card still visible
- **Main Window — Updating state:**
  - Status dot orange, "Updating..." text, "Server will restart automatically" subtitle
  - Update progress card: spinning icon, version info, progress bar with step indicator (e.g. "Step 1/3 — Downloading MediaDownloader-Setup-1.1.0.exe..."), log output area (monospace) showing download progress → stop server → launch installer steps
  - Footer shows version transition, disabled "Please wait..." link
- **Main Window — Settings tab:**
  - **Directories section**: Movies, TV Shows, Archive, App Data paths (monospace, with folder browser dialogs)
  - **API Keys section**: TMDB API Key and Real-Debrid Key (masked with last 4 chars visible, e.g. "••••••••a7f3"), RD key shows "VALID" badge in green when verified
  - **Behavior section**: Start on boot (toggle), Auto-start server (toggle), **Auto-update** (toggle — off by default), Max concurrent downloads (value display), Watch threshold (percentage display)
  - **MPC-BE section**: URL and Executable path
  - Save/Cancel buttons at bottom, "Reset defaults" link in footer
- **Update Flow:** Detects update via `GET /api/version`. "Update Now" downloads the new `MediaDownloader-Setup-{version}.exe` to `{APP_DATA_DIR}/updates/`, stops the backend, launches the installer (supports `/SILENT` flag), and exits. Inno Setup upgrades in-place and relaunches the app. When "Auto-update" toggle is enabled, updates are applied automatically without user interaction.
- **Auto-Start:** Optional Windows startup via Registry or Startup folder
- **Single Instance:** Prevent multiple instances via named mutex
- **Error Handling & Diagnostics:**
  - **Global exception handlers:** `DispatcherUnhandledException`, `AppDomain.UnhandledException`, and `TaskScheduler.UnobservedTaskException` all wired up in `App.OnStartup`. Unhandled exceptions show a `MessageBox` with the error message and log file path, then shut down gracefully.
  - **Startup log file:** Written to `{LocalAppData}\MediaDownloader\logs\wpf-startup.log`. Captures timestamped entries for each startup phase (single-instance check, `InitializeComponent`, ViewModel creation, backend path resolution, server start, settings load). The log is append-only and helps diagnose launch failures even when the UI never renders.
  - **Async exception safety:** All `async void` event handlers (e.g. `Loaded`, `DispatcherTimer.Tick`) wrap their bodies in `try/catch` and log failures rather than allowing them to propagate as unobserved task exceptions.
  - **Backend path logging:** On startup, the resolved backend executable path is logged along with whether the file was found. If the backend is missing, a warning is logged (the app still starts but shows "Stopped" status).

### Architecture
```
MediaDownloader.Wpf/
├── App.xaml / App.xaml.cs         — Entry point, single-instance check
├── MainWindow.xaml / .xaml.cs     — Main window with tabs
├── ViewModels/
│   ├── MainViewModel.cs           — Main window state + commands
│   └── SettingsViewModel.cs       — Settings binding
├── Services/
│   ├── ServerManager.cs           — Start/stop/monitor backend process
│   ├── ApiClient.cs               — Typed HttpClient wrapper calling backend at http://localhost:{PORT}, uses DTOs from MediaDownloader.Shared
│   ├── UpdateService.cs           — Check for updates via /api/version, download installer, apply
│   └── SettingsManager.cs         — Read/write .env configuration
├── Views/
│   ├── DownloadListView.xaml      — Active downloads display
│   └── SettingsView.xaml          — Settings panel
├── Converters/                    — Value converters for XAML bindings
└── Resources/
    ├── Styles.xaml                — App-wide styles and templates
    └── Icons/                     — App and tray icons
```

### MediaDownloader.Shared
Class library referenced by both `MediaDownloader.Api` and `MediaDownloader.Wpf`:

**`JobStatus` enum:** Pending, Searching, Found, AddingToRd, WaitingForRd, Downloading, Organizing, Complete, Failed, Cancelled

**DTO classes:**

- `TitleDto` — Id, TmdbId, ImdbId, Title, Year, Type, IsAnime, Overview, PosterPath, FolderName
- `MediaItemDto` — Id, TitleId, JobId, Season, Episode, EpisodeTitle, FilePath, IsArchived, AddedAt
- `JobDto` — Id, TitleId, Query, Season, Episode, Status, Progress, SizeBytes, DownloadedBytes, Quality, TorrentName, Error, CreatedAt, UpdatedAt, Title (nested `TitleDto`). WPF calculates download speed and ETA client-side by tracking DownloadedBytes changes over time.
- `VersionDto` — Version, UpdateAvailable, LatestVersion, ReleaseUrl
- `SettingsDto` — all config keys as string properties
- `StatusDto` — Status, MoviesDir, TvDir, ArchiveDir
- `MpcStatusDto` — Reachable, FileName, State, IsPlaying, IsPaused, PositionMs, DurationMs, Volume, Muted, TitleId, Title, IsAnime, Season, Episode, EpisodeTitle, EpisodeCount, Year, Type, PrevEpisode (nullable `EpisodeRefDto`), NextEpisode (nullable `EpisodeRefDto`)
- `EpisodeRefDto` — MediaItemId, Episode, Title

**Shared constants:** config key names (e.g., `ConfigKeys.TmdbApiKey`), default values (e.g., `Defaults.Port = 8000`)

---

## 12. Installer Specification

### Technology
**Inno Setup** — free, scriptable Windows installer framework producing a single `.exe` setup file.

### What Gets Installed
| Component | Source | Install Location |
|---|---|---|
| Backend | Published .NET 8 self-contained build | `{app}\backend\` |
| Frontend | `vite build` output (static files) | `{app}\backend\wwwroot\` |
| WPF App | Published .NET 8 executable | `{app}\` |
| MPC-BE | Bundled portable or download prompt | `{app}\mpc-be\` (optional) |

### Installer Behavior
1. **Welcome page** with app name + version
2. **License agreement** (if applicable)
3. **Destination folder** selection (default: `C:\Program Files\Media Downloader`)
4. **Component selection:** Backend + Frontend (required), WPF App (required), MPC-BE portable (optional)
5. **Configuration page:** prompt for TMDB API Key, Real-Debrid API Key, Movies directory, TV directory, Archive directory
6. **Start menu + desktop shortcuts**
7. **Optional:** Add to Windows startup (auto-start on login)
8. **Firewall rule:** Add exception for backend server port (default 8000)

### Post-Install
- Write initial `.env` file with user-provided API keys and directories
- Create default directories if they don't exist (Movies, TV, Archive)
- Create `APP_DATA_DIR` with subdirectories (posters, staging, logs)
- Register uninstaller in Windows Add/Remove Programs

### Uninstaller
- Remove all installed files
- Remove Start Menu + Desktop shortcuts
- Remove Windows startup entry (if added)
- Remove firewall rule
- **Preserve** user data: `.env`, `APP_DATA_DIR` (SQLite DB, posters), media library folders (prompt user)

### Build Process
```
1. dotnet publish backend -c Release --self-contained -r win-x64
2. dotnet publish wpf -c Release --self-contained -r win-x64
3. cd frontend && npm run build
4. Copy frontend/dist → backend/publish/wwwroot
5. Run Inno Setup compiler: iscc installer.iss
→ Output: MediaDownloader-Setup-{version}.exe
```

### Installer Script Location
`installer/setup.iss` — Inno Setup script defining all the above behavior.

### CI/CD — GitHub Actions

#### Auto Version Workflow (`auto-version.yml`)
- **Trigger:** every push to `main` (excludes tag pushes and commits containing `[skip-version]`)
- **Runner:** `ubuntu-latest`
- **Steps:** reads the latest `v*` tag, bumps the patch number (e.g. `v0.2.3` → `v0.2.4`), updates `<Version>` in both `MediaDownloader.Wpf.csproj` and `MediaDownloader.Api.csproj`, commits with `[skip-version]` marker, creates and pushes the new `v*` tag
- **Loop prevention:** the version-bump commit includes `[skip-version]` in the message so the workflow skips its own commits; `tags-ignore: '**'` prevents triggering on the tag push

#### Release Workflow (`release.yml`)
- **Trigger:** push of version tag (`v*`) — automatically fired by the Auto Version workflow above
- **Runner:** `windows-latest` with .NET 8 SDK + Node.js LTS
- **Asset validation:** all csproj-referenced resources (`ApplicationIcon`, embedded files) must be present in the repo; build fails immediately if missing
- **Build steps:** `dotnet publish` for Api and Wpf (win-x64, self-contained), `npm ci && npm run build` for frontend
- **Package:** run Inno Setup compiler (`iscc installer/setup.iss`)
- **Artifact:** upload `MediaDownloader-Setup-{version}.exe` as GitHub Release asset
