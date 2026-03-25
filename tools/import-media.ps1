#Requires -Version 5.1
<#
.SYNOPSIS
    Scans existing media folders, resolves metadata via TMDB, reorganizes files into
    the correct folder/filename structure, and indexes them into the database.

.DESCRIPTION
    This script reads your .env file to find MOVIES_DIR, TV_DIR, TMDB_API_KEY, and
    APP_DATA_DIR. It walks both directories, looks up each title folder on TMDB,
    renames folders to "Title [tmdb_id]" format, renames movie files to "Title (Year).ext",
    and renames episode files to "S01E01 - Episode Title.ext" when possible.

    After reorganizing, it calls the backend API to refresh the library database.

.PARAMETER DryRun
    Preview changes without moving or renaming anything.

.PARAMETER SkipReorganize
    Skip the TMDB lookup and file reorganization step. Only trigger a database refresh
    via the API (useful if your files are already organized correctly).

.PARAMETER ApiUrl
    Backend API URL. Defaults to http://localhost:8000.

.EXAMPLE
    .\import-media.ps1 -DryRun
    .\import-media.ps1
    .\import-media.ps1 -SkipReorganize
#>

param(
    [switch]$DryRun,
    [switch]$SkipReorganize,
    [string]$ApiUrl = "http://localhost:8000"
)

$ErrorActionPreference = "Stop"

$VideoExtensions = @('.mkv', '.mp4', '.avi', '.m4v', '.wmv', '.flv', '.mov')

# ── Load .env ──────────────────────────────────────────────────────────────────

function Load-EnvFile {
    $envPath = Join-Path $PSScriptRoot "..\\.env"
    if (-not (Test-Path $envPath)) {
        Write-Host "ERROR: .env file not found at $envPath" -ForegroundColor Red
        Write-Host "Copy .env.example to .env and fill in your settings." -ForegroundColor Yellow
        exit 1
    }

    $envVars = @{}
    Get-Content $envPath | ForEach-Object {
        $line = $_.Trim()
        if ($line -and -not $line.StartsWith('#')) {
            $parts = $line -split '=', 2
            if ($parts.Count -eq 2) {
                $envVars[$parts[0].Trim()] = $parts[1].Trim()
            }
        }
    }
    return $envVars
}

# ── TMDB API helpers ───────────────────────────────────────────────────────────

function Search-Tmdb {
    param(
        [string]$Query,
        [string]$Type,  # "movie" or "tv"
        [string]$ApiKey
    )

    $encoded = [System.Web.HttpUtility]::UrlEncode($Query)
    $searchType = if ($Type -eq "movie") { "movie" } else { "tv" }
    $url = "https://api.themoviedb.org/3/search/${searchType}?api_key=${ApiKey}&query=${encoded}"

    try {
        $response = Invoke-RestMethod -Uri $url -Method Get -ErrorAction Stop
        if ($response.results.Count -gt 0) {
            $first = $response.results[0]
            $title = if ($Type -eq "movie") { $first.title } else { $first.name }
            $dateStr = if ($Type -eq "movie") { $first.release_date } else { $first.first_air_date }
            $year = if ($dateStr -and $dateStr.Length -ge 4) { [int]$dateStr.Substring(0, 4) } else { $null }
            $posterPath = $first.poster_path

            return @{
                TmdbId     = $first.id
                Title      = $title
                Year       = $year
                PosterPath = $posterPath
            }
        }
    }
    catch {
        Write-Host "  TMDB search failed for '$Query': $_" -ForegroundColor Yellow
    }

    # Fallback: multi search
    $url = "https://api.themoviedb.org/3/search/multi?api_key=${ApiKey}&query=${encoded}"
    try {
        $response = Invoke-RestMethod -Uri $url -Method Get -ErrorAction Stop
        foreach ($result in $response.results) {
            if ($result.media_type -eq $Type) {
                $title = if ($Type -eq "movie") { $result.title } else { $result.name }
                $dateStr = if ($Type -eq "movie") { $result.release_date } else { $result.first_air_date }
                $year = if ($dateStr -and $dateStr.Length -ge 4) { [int]$dateStr.Substring(0, 4) } else { $null }
                return @{
                    TmdbId     = $result.id
                    Title      = $title
                    Year       = $year
                    PosterPath = $result.poster_path
                }
            }
        }
    }
    catch {
        Write-Host "  TMDB multi-search failed for '$Query': $_" -ForegroundColor Yellow
    }

    return $null
}

function Get-EpisodeTitle {
    param(
        [int]$TmdbId,
        [int]$Season,
        [int]$Episode,
        [string]$ApiKey
    )

    try {
        $url = "https://api.themoviedb.org/3/tv/${TmdbId}/season/${Season}/episode/${Episode}?api_key=${ApiKey}"
        $response = Invoke-RestMethod -Uri $url -Method Get -ErrorAction Stop
        return $response.name
    }
    catch {
        return $null
    }
}

# ── Filename helpers ───────────────────────────────────────────────────────────

function Sanitize-Name {
    param([string]$Name)
    $Name = $Name -replace '[<>"/\\|?*]', ''
    $Name = $Name -replace ':', ' - '
    $Name = $Name -replace '\s+', ' '
    $Name = $Name.Trim('. ')
    return $Name
}

function Clean-FolderTitle {
    param([string]$FolderName)

    $name = $FolderName

    # Remove bracket content like [12345]
    $name = $name -replace '\[\d+\]', ''
    # Remove quality tags
    $name = $name -replace '(720p|1080p|2160p|4K|BluRay|BRRip|WEBRip|WEB-DL|HDRip|DVDRip|x264|x265|HEVC|AAC|DTS|REMUX|PROPER|REPACK)', ''
    # Replace dots with spaces
    $name = $name -replace '\.', ' '
    # Remove parenthetical content
    $name = $name -replace '\([^)]*\)', ''
    # Remove bracket content
    $name = $name -replace '\[[^\]]*\]', ''

    # Extract year
    $year = $null
    if ($name -match '[\.\s\-\(]*((?:19|20)\d{2})[\.\)\s]*$') {
        $year = [int]$Matches[1]
        $name = $name.Substring(0, $name.IndexOf($Matches[0]))
    }

    # Collapse spaces
    $name = ($name -replace '\s+', ' ').Trim('. -')

    return @{ Title = $name; Year = $year }
}

function Parse-SeasonEpisode {
    param([string]$FileName)

    if ($FileName -match 'S(\d{1,2})E(\d{1,3})') {
        $season = [int]$Matches[1]
        $episode = [int]$Matches[2]

        # Extract episode title after "S01E03 - "
        $afterEp = $FileName.Substring($FileName.IndexOf($Matches[0]) + $Matches[0].Length).TrimStart(' ', '-')
        $epTitle = if ($afterEp) { $afterEp } else { $null }

        return @{ Season = $season; Episode = $episode; EpisodeTitle = $epTitle }
    }
    return $null
}

# ── Rate limiting ──────────────────────────────────────────────────────────────

$script:lastTmdbCall = [DateTime]::MinValue
function Wait-RateLimit {
    $elapsed = ([DateTime]::Now - $script:lastTmdbCall).TotalMilliseconds
    if ($elapsed -lt 250) {
        Start-Sleep -Milliseconds ([int](250 - $elapsed))
    }
    $script:lastTmdbCall = [DateTime]::Now
}

# ── Main logic ─────────────────────────────────────────────────────────────────

Add-Type -AssemblyName System.Web

$env = Load-EnvFile
$moviesDir = $env['MOVIES_DIR']
$tvDir = $env['TV_DIR']
$tmdbApiKey = $env['TMDB_API_KEY']
$appDataDir = $env['APP_DATA_DIR']

if (-not $tmdbApiKey -or $tmdbApiKey -eq 'your_tmdb_api_key') {
    Write-Host "ERROR: TMDB_API_KEY not set in .env" -ForegroundColor Red
    exit 1
}

if ($DryRun) {
    Write-Host "[DRY RUN] No files will be moved or renamed.`n" -ForegroundColor Cyan
}

$totalProcessed = 0
$totalRenamed = 0
$totalSkipped = 0
$errors = @()

if (-not $SkipReorganize) {

    # ── Process Movies ─────────────────────────────────────────────────────────

    if ($moviesDir -and (Test-Path $moviesDir)) {
        Write-Host "Scanning MOVIES_DIR: $moviesDir" -ForegroundColor Green
        Write-Host ("=" * 60)

        $movieFolders = Get-ChildItem -Path $moviesDir -Directory

        foreach ($folder in $movieFolders) {
            $totalProcessed++

            # Check if already in correct format: "Title [12345]"
            if ($folder.Name -match '\[\d+\]$') {
                Write-Host "  [OK] $($folder.Name) (already organized)" -ForegroundColor DarkGray
                $totalSkipped++
                continue
            }

            # Parse the folder name to get a search query
            $parsed = Clean-FolderTitle -FolderName $folder.Name
            $searchQuery = $parsed.Title
            if (-not $searchQuery) {
                Write-Host "  [SKIP] $($folder.Name) - couldn't parse title" -ForegroundColor Yellow
                $totalSkipped++
                continue
            }

            Write-Host "  Processing: $($folder.Name)" -ForegroundColor White
            Write-Host "    Searching TMDB for: '$searchQuery' (movie)..." -NoNewline

            Wait-RateLimit
            $tmdb = Search-Tmdb -Query $searchQuery -Type "movie" -ApiKey $tmdbApiKey

            if (-not $tmdb) {
                Write-Host " NOT FOUND" -ForegroundColor Red
                $errors += "Movie not found on TMDB: $($folder.Name) (searched: '$searchQuery')"
                continue
            }

            Write-Host " Found: $($tmdb.Title) ($($tmdb.Year)) [tmdb:$($tmdb.TmdbId)]" -ForegroundColor Green

            # Build new folder name
            $safeName = Sanitize-Name -Name $tmdb.Title
            $newFolderName = "$safeName [$($tmdb.TmdbId)]"
            $newFolderPath = Join-Path $moviesDir $newFolderName

            # Find the video file in the folder
            $videoFiles = Get-ChildItem -Path $folder.FullName -Recurse -File |
                Where-Object { $VideoExtensions -contains $_.Extension.ToLower() } |
                Sort-Object Length -Descending

            if ($videoFiles.Count -eq 0) {
                Write-Host "    No video files found in folder" -ForegroundColor Yellow
                $errors += "No video files: $($folder.Name)"
                continue
            }

            $videoFile = $videoFiles[0]
            $ext = $videoFile.Extension

            # Build new filename
            $newFileName = if ($tmdb.Year) {
                "$safeName ($($tmdb.Year))$ext"
            } else {
                "$safeName$ext"
            }

            if ($DryRun) {
                if ($folder.Name -ne $newFolderName) {
                    Write-Host "    RENAME FOLDER: $($folder.Name) -> $newFolderName" -ForegroundColor Cyan
                }
                if ($videoFile.Name -ne $newFileName) {
                    Write-Host "    RENAME FILE:   $($videoFile.Name) -> $newFileName" -ForegroundColor Cyan
                }
            } else {
                try {
                    # Rename folder first
                    if ($folder.Name -ne $newFolderName) {
                        if (Test-Path $newFolderPath) {
                            # Folder already exists, move file into it
                            $destFile = Join-Path $newFolderPath $newFileName
                            Move-Item -Path $videoFile.FullName -Destination $destFile -Force
                            # Clean up old folder if empty
                            $remaining = Get-ChildItem -Path $folder.FullName -Recurse -File
                            if ($remaining.Count -eq 0) {
                                Remove-Item -Path $folder.FullName -Recurse -Force
                            }
                        } else {
                            Rename-Item -Path $folder.FullName -NewName $newFolderName
                            # Now rename the file inside
                            $movedVideoPath = Join-Path $newFolderPath $videoFile.Name
                            if (Test-Path $movedVideoPath) {
                                $destFile = Join-Path $newFolderPath $newFileName
                                if ($movedVideoPath -ne $destFile) {
                                    Rename-Item -Path $movedVideoPath -NewName $newFileName
                                }
                            }
                        }
                    } else {
                        # Folder name is fine, just rename the file
                        $destFile = Join-Path $folder.FullName $newFileName
                        if ($videoFile.FullName -ne $destFile) {
                            Rename-Item -Path $videoFile.FullName -NewName $newFileName
                        }
                    }

                    $totalRenamed++
                    Write-Host "    DONE" -ForegroundColor Green
                }
                catch {
                    Write-Host "    FAILED: $_" -ForegroundColor Red
                    $errors += "Failed to rename $($folder.Name): $_"
                }
            }
        }

        Write-Host ""
    } else {
        Write-Host "MOVIES_DIR not set or doesn't exist, skipping movies." -ForegroundColor Yellow
    }

    # ── Process TV Shows ───────────────────────────────────────────────────────

    if ($tvDir -and (Test-Path $tvDir)) {
        Write-Host "Scanning TV_DIR: $tvDir" -ForegroundColor Green
        Write-Host ("=" * 60)

        $showFolders = Get-ChildItem -Path $tvDir -Directory

        foreach ($folder in $showFolders) {
            $totalProcessed++

            # Check if already in correct format
            if ($folder.Name -match '\[\d+\]$') {
                Write-Host "  [OK] $($folder.Name) (already organized)" -ForegroundColor DarkGray
                $totalSkipped++
                continue
            }

            $parsed = Clean-FolderTitle -FolderName $folder.Name
            $searchQuery = $parsed.Title
            if (-not $searchQuery) {
                Write-Host "  [SKIP] $($folder.Name) - couldn't parse title" -ForegroundColor Yellow
                $totalSkipped++
                continue
            }

            Write-Host "  Processing: $($folder.Name)" -ForegroundColor White
            Write-Host "    Searching TMDB for: '$searchQuery' (tv)..." -NoNewline

            Wait-RateLimit
            $tmdb = Search-Tmdb -Query $searchQuery -Type "tv" -ApiKey $tmdbApiKey

            if (-not $tmdb) {
                Write-Host " NOT FOUND" -ForegroundColor Red
                $errors += "TV show not found on TMDB: $($folder.Name) (searched: '$searchQuery')"
                continue
            }

            Write-Host " Found: $($tmdb.Title) ($($tmdb.Year)) [tmdb:$($tmdb.TmdbId)]" -ForegroundColor Green

            $safeName = Sanitize-Name -Name $tmdb.Title
            $newFolderName = "$safeName [$($tmdb.TmdbId)]"
            $newFolderPath = Join-Path $tvDir $newFolderName

            # Get all video files (including in season subfolders)
            $videoFiles = Get-ChildItem -Path $folder.FullName -Recurse -File |
                Where-Object { $VideoExtensions -contains $_.Extension.ToLower() }

            if ($videoFiles.Count -eq 0) {
                Write-Host "    No video files found" -ForegroundColor Yellow
                $errors += "No video files: $($folder.Name)"
                continue
            }

            Write-Host "    Found $($videoFiles.Count) video file(s)" -ForegroundColor White

            if ($DryRun) {
                if ($folder.Name -ne $newFolderName) {
                    Write-Host "    RENAME FOLDER: $($folder.Name) -> $newFolderName" -ForegroundColor Cyan
                }

                foreach ($vf in $videoFiles) {
                    $baseName = [System.IO.Path]::GetFileNameWithoutExtension($vf.Name)
                    $seInfo = Parse-SeasonEpisode -FileName $baseName
                    if ($seInfo) {
                        Wait-RateLimit
                        $epTitle = Get-EpisodeTitle -TmdbId $tmdb.TmdbId -Season $seInfo.Season -Episode $seInfo.Episode -ApiKey $tmdbApiKey
                        $epSafe = if ($epTitle) { Sanitize-Name -Name $epTitle } else { $null }
                        $newName = if ($epSafe) {
                            "S{0:D2}E{1:D2} - {2}{3}" -f $seInfo.Season, $seInfo.Episode, $epSafe, $vf.Extension
                        } else {
                            "S{0:D2}E{1:D2}{2}" -f $seInfo.Season, $seInfo.Episode, $vf.Extension
                        }
                        if ($vf.Name -ne $newName) {
                            Write-Host "    RENAME FILE: $($vf.Name) -> $newName" -ForegroundColor Cyan
                        }
                    }
                }
            } else {
                try {
                    # First, flatten any season subfolders - move all video files to the show root
                    foreach ($vf in $videoFiles) {
                        if ($vf.Directory.FullName -ne $folder.FullName) {
                            $destPath = Join-Path $folder.FullName $vf.Name
                            if (-not (Test-Path $destPath)) {
                                Move-Item -Path $vf.FullName -Destination $destPath
                            }
                        }
                    }

                    # Remove empty season subdirectories
                    Get-ChildItem -Path $folder.FullName -Directory | ForEach-Object {
                        $remaining = Get-ChildItem -Path $_.FullName -Recurse -File
                        if ($remaining.Count -eq 0) {
                            Remove-Item -Path $_.FullName -Recurse -Force
                        }
                    }

                    # Rename episode files
                    $currentFiles = Get-ChildItem -Path $folder.FullName -File |
                        Where-Object { $VideoExtensions -contains $_.Extension.ToLower() }

                    foreach ($vf in $currentFiles) {
                        $baseName = [System.IO.Path]::GetFileNameWithoutExtension($vf.Name)
                        $seInfo = Parse-SeasonEpisode -FileName $baseName
                        if ($seInfo) {
                            Wait-RateLimit
                            $epTitle = Get-EpisodeTitle -TmdbId $tmdb.TmdbId -Season $seInfo.Season -Episode $seInfo.Episode -ApiKey $tmdbApiKey
                            $epSafe = if ($epTitle) { Sanitize-Name -Name $epTitle } else { $null }
                            $newName = if ($epSafe) {
                                "S{0:D2}E{1:D2} - {2}{3}" -f $seInfo.Season, $seInfo.Episode, $epSafe, $vf.Extension
                            } else {
                                "S{0:D2}E{1:D2}{2}" -f $seInfo.Season, $seInfo.Episode, $vf.Extension
                            }
                            if ($vf.Name -ne $newName) {
                                Rename-Item -Path $vf.FullName -NewName $newName
                            }
                        }
                    }

                    # Rename the show folder
                    if ($folder.Name -ne $newFolderName) {
                        if (Test-Path $newFolderPath) {
                            # Merge into existing folder
                            Get-ChildItem -Path $folder.FullName -File | ForEach-Object {
                                $dest = Join-Path $newFolderPath $_.Name
                                Move-Item -Path $_.FullName -Destination $dest -Force
                            }
                            $remaining = Get-ChildItem -Path $folder.FullName -Recurse -File
                            if ($remaining.Count -eq 0) {
                                Remove-Item -Path $folder.FullName -Recurse -Force
                            }
                        } else {
                            Rename-Item -Path $folder.FullName -NewName $newFolderName
                        }
                    }

                    $totalRenamed++
                    Write-Host "    DONE" -ForegroundColor Green
                }
                catch {
                    Write-Host "    FAILED: $_" -ForegroundColor Red
                    $errors += "Failed to process $($folder.Name): $_"
                }
            }
        }

        Write-Host ""
    } else {
        Write-Host "TV_DIR not set or doesn't exist, skipping TV shows." -ForegroundColor Yellow
    }
}

# ── Summary ────────────────────────────────────────────────────────────────────

Write-Host ("=" * 60) -ForegroundColor White
Write-Host "SUMMARY" -ForegroundColor Green
Write-Host "  Processed: $totalProcessed"
Write-Host "  Renamed:   $totalRenamed"
Write-Host "  Skipped:   $totalSkipped (already organized)"
Write-Host "  Errors:    $($errors.Count)"

if ($errors.Count -gt 0) {
    Write-Host "`nErrors:" -ForegroundColor Red
    foreach ($err in $errors) {
        Write-Host "  - $err" -ForegroundColor Red
    }
}

if ($DryRun) {
    Write-Host "`n[DRY RUN] No changes were made. Run without -DryRun to apply." -ForegroundColor Cyan
    exit 0
}

# ── Trigger database refresh via API ───────────────────────────────────────────

Write-Host "`n$("=" * 60)" -ForegroundColor White
Write-Host "Triggering library refresh via API..." -ForegroundColor Green
Write-Host "  (Make sure the Media Downloader backend is running on $ApiUrl)" -ForegroundColor Yellow

try {
    $response = Invoke-RestMethod -Uri "$ApiUrl/api/library/refresh" -Method Post -TimeoutSec 300 -ErrorAction Stop
    Write-Host "  Library refresh complete!" -ForegroundColor Green
    Write-Host "    Items indexed: $($response.totalItems)"
    Write-Host "    Posters fetched: $($response.postersFetched)"
    if ($response.errors -and $response.errors.Count -gt 0) {
        Write-Host "    Refresh errors: $($response.errors.Count)" -ForegroundColor Yellow
        foreach ($err in $response.errors) {
            Write-Host "      - $err" -ForegroundColor Yellow
        }
    }
}
catch {
    Write-Host "  Could not reach the API at $ApiUrl" -ForegroundColor Yellow
    Write-Host "  Start the backend and run: Invoke-RestMethod -Uri '$ApiUrl/api/library/refresh' -Method Post" -ForegroundColor Yellow
    Write-Host "  Or just open the app and click 'Refresh Library' in the UI." -ForegroundColor Yellow
}

Write-Host "`nDone!" -ForegroundColor Green
