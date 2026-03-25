import { type Page, type Route } from '@playwright/test';

// ── Mock data matching SPEC_PLAN.md §2.1 schemas ──────────────────────────

export const STATUS_OK = {
  status: 'ok',
  moviesDir: '/media/movies',
  tvDir: '/media/tv',
  archiveDir: '/media/archive',
};

export const STATUS_ERROR = { status: 500 };

export const SEARCH_RESULT = {
  searchId: 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee',
  media: {
    tmdbId: 1396,
    title: 'Breaking Bad',
    year: 2008,
    type: 'tv',
    isAnime: false,
    imdbId: 'tt0903747',
    posterUrl: null,
    season: 1,
    episode: 1,
    overview: 'A chemistry teacher diagnosed with terminal lung cancer.',
  },
  streams: [
    {
      index: 0,
      name: 'Breaking.Bad.S01E01.1080p.BluRay.x264-DEMAND',
      infoHash: 'abc123def456',
      sizeBytes: 1_500_000_000,
      isCachedRd: true,
      seeders: 45,
    },
    {
      index: 1,
      name: 'Breaking.Bad.S01E01.2160p.HDR10+.DV.Atmos-GROUP',
      infoHash: 'def789ghi012',
      sizeBytes: 8_000_000_000,
      isCachedRd: false,
      seeders: 12,
    },
    {
      index: 2,
      name: 'Breaking.Bad.S01E01.720p.5.1.WEB-DL',
      infoHash: 'ghi345jkl678',
      sizeBytes: 900_000_000,
      isCachedRd: true,
      seeders: 30,
    },
    {
      index: 3,
      name: 'Breaking.Bad.S01E01.480p.2.0-LOW',
      infoHash: 'jkl901mno234',
      sizeBytes: 400_000_000,
      isCachedRd: false,
      seeders: 5,
    },
  ],
  warning: null,
};

export const DOWNLOAD_RESULT = {
  jobId: '11111111-2222-3333-4444-555555555555',
  titleId: '66666666-7777-8888-9999-aaaaaaaaaaaa',
  status: 'pending',
};

export const JOBS_EMPTY = { jobs: [] };

export const JOBS_LIST = {
  jobs: [
    {
      id: 'job-active-1',
      titleId: 'title-1',
      query: 'Breaking Bad S01E01',
      season: 1,
      episode: 1,
      status: 'downloading',
      progress: 0.45,
      sizeBytes: 1_500_000_000,
      downloadedBytes: 675_000_000,
      quality: '1080p BluRay',
      torrentName: 'Breaking.Bad.S01E01.1080p.BluRay.x264-DEMAND',
      error: null,
      log: null,
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
      title: {
        id: 'title-1',
        tmdbId: 1396,
        imdbId: 'tt0903747',
        title: 'Breaking Bad',
        year: 2008,
        type: 'tv',
        isAnime: false,
        posterPath: null,
      },
    },
    {
      id: 'job-complete-1',
      titleId: 'title-2',
      query: 'Inception',
      season: null,
      episode: null,
      status: 'complete',
      progress: 1.0,
      sizeBytes: 2_000_000_000,
      downloadedBytes: 2_000_000_000,
      quality: '1080p',
      torrentName: 'Inception.2010.1080p.BluRay',
      error: null,
      log: null,
      createdAt: new Date(Date.now() - 3600000).toISOString(),
      updatedAt: new Date(Date.now() - 3000000).toISOString(),
      title: {
        id: 'title-2',
        tmdbId: 27205,
        imdbId: 'tt1375666',
        title: 'Inception',
        year: 2010,
        type: 'movie',
        isAnime: false,
        posterPath: null,
      },
    },
    {
      id: 'job-failed-1',
      titleId: 'title-3',
      query: 'The Matrix',
      season: null,
      episode: null,
      status: 'failed',
      progress: 0.1,
      sizeBytes: 3_000_000_000,
      downloadedBytes: 300_000_000,
      quality: '4K',
      torrentName: 'The.Matrix.1999.2160p-GROUP',
      error: 'Real-Debrid API timeout',
      log: null,
      createdAt: new Date(Date.now() - 7200000).toISOString(),
      updatedAt: new Date(Date.now() - 7000000).toISOString(),
      title: {
        id: 'title-3',
        tmdbId: 603,
        imdbId: 'tt0133093',
        title: 'The Matrix',
        year: 1999,
        type: 'movie',
        isAnime: false,
        posterPath: null,
      },
    },
    {
      id: 'job-pending-1',
      titleId: 'title-4',
      query: 'Naruto',
      season: 1,
      episode: null,
      status: 'pending',
      progress: 0,
      sizeBytes: 5_000_000_000,
      downloadedBytes: 0,
      quality: null,
      torrentName: null,
      error: null,
      log: null,
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
      title: {
        id: 'title-4',
        tmdbId: 20,
        imdbId: 'tt0409591',
        title: 'Naruto',
        year: 2002,
        type: 'tv',
        isAnime: true,
        posterPath: null,
      },
    },
  ],
};

export const LIBRARY_ITEMS = {
  items: [
    {
      id: 'lib-1',
      tmdbId: 1396,
      imdbId: 'tt0903747',
      title: 'Breaking Bad',
      year: 2008,
      type: 'tv',
      isAnime: false,
      overview: 'A chemistry teacher diagnosed with terminal lung cancer.',
      posterPath: null,
      folderName: 'Breaking Bad [1396]',
      fileCount: 62,
      totalSize: 45_000_000_000,
      hasArchived: false,
    },
    {
      id: 'lib-2',
      tmdbId: 27205,
      imdbId: 'tt1375666',
      title: 'Inception',
      year: 2010,
      type: 'movie',
      isAnime: false,
      overview: 'A thief who steals corporate secrets through the use of dream-sharing technology.',
      posterPath: null,
      folderName: 'Inception [27205]',
      fileCount: 1,
      totalSize: 2_000_000_000,
      hasArchived: false,
    },
    {
      id: 'lib-3',
      tmdbId: 20,
      imdbId: 'tt0409591',
      title: 'Naruto',
      year: 2002,
      type: 'tv',
      isAnime: true,
      overview: 'A young ninja who seeks recognition from his peers.',
      posterPath: null,
      folderName: 'Naruto [20]',
      fileCount: 220,
      totalSize: 80_000_000_000,
      hasArchived: true,
    },
  ],
  count: 3,
};

export const LIBRARY_EMPTY = { items: [], count: 0 };

export const EPISODES_TV = {
  seasons: [
    {
      season: 1,
      episodes: [
        {
          mediaItemId: 'ep-s1e1',
          episode: 1,
          episodeTitle: 'Pilot',
          fileName: 'S01E01 - Pilot.mkv',
          filePath: '/media/tv/Breaking Bad [1396]/S01E01 - Pilot.mkv',
          isArchived: false,
          progress: { positionMs: 2_400_000, durationMs: 3_480_000, watched: true },
        },
        {
          mediaItemId: 'ep-s1e2',
          episode: 2,
          episodeTitle: "Cat's in the Bag...",
          fileName: "S01E02 - Cat's in the Bag.mkv",
          filePath: "/media/tv/Breaking Bad [1396]/S01E02 - Cat's in the Bag.mkv",
          isArchived: false,
          progress: { positionMs: 2_100_000, durationMs: 2_880_000, watched: true },
        },
        {
          mediaItemId: 'ep-s1e3',
          episode: 3,
          episodeTitle: '...And the Bag\'s in the River',
          fileName: "S01E03 - And the Bag's in the River.mkv",
          filePath: "/media/tv/Breaking Bad [1396]/S01E03 - And the Bag's in the River.mkv",
          isArchived: false,
          progress: { positionMs: 1_125_000, durationMs: 3_000_000, watched: false },
        },
        {
          mediaItemId: 'ep-s1e4',
          episode: 4,
          episodeTitle: 'Cancer Man',
          fileName: 'S01E04 - Cancer Man.mkv',
          filePath: '/media/tv/Breaking Bad [1396]/S01E04 - Cancer Man.mkv',
          isArchived: false,
          progress: null,
        },
      ],
    },
    {
      season: 2,
      episodes: [
        {
          mediaItemId: 'ep-s2e1',
          episode: 1,
          episodeTitle: 'Seven Thirty-Seven',
          fileName: 'S02E01 - Seven Thirty-Seven.mkv',
          filePath: '/media/tv/Breaking Bad [1396]/S02E01 - Seven Thirty-Seven.mkv',
          isArchived: false,
          progress: null,
        },
      ],
    },
  ],
};

export const MPC_STATUS_PLAYING = {
  reachable: true,
  fileName: "S01E03 - And the Bag's in the River.mkv",
  state: 2,
  isPlaying: true,
  isPaused: false,
  positionMs: 1_125_000,
  durationMs: 3_258_000,
  volume: 72,
  muted: false,
  titleId: 'lib-1',
  title: 'Breaking Bad',
  isAnime: false,
  season: 1,
  episode: 3,
  episodeTitle: '...And the Bag\'s in the River',
  episodeCount: 7,
  year: 2008,
  type: 'tv',
  prevEpisode: { mediaItemId: 'ep-s1e2', episode: 2, title: "Cat's in the Bag..." },
  nextEpisode: { mediaItemId: 'ep-s1e4', episode: 4, title: 'Cancer Man' },
};

export const MPC_STATUS_PAUSED = {
  ...MPC_STATUS_PLAYING,
  state: 1,
  isPlaying: false,
  isPaused: true,
};

export const MPC_STATUS_UNREACHABLE = {
  reachable: false,
  fileName: null,
  state: 0,
  isPlaying: false,
  isPaused: false,
  positionMs: 0,
  durationMs: 0,
  volume: 0,
  muted: false,
  titleId: null,
  title: null,
  isAnime: false,
  season: null,
  episode: null,
  episodeTitle: null,
  episodeCount: null,
  year: null,
  type: null,
  prevEpisode: null,
  nextEpisode: null,
};

export const REFRESH_RESULT = {
  renamed: 2,
  postersFetched: 5,
  errors: [],
  totalItems: 15,
};

export const VERSION_RESULT = {
  version: '0.2.1',
  updateAvailable: false,
  latestVersion: '0.2.1',
  releaseUrl: null,
};

// ── Route helpers ──────────────────────────────────────────────────────────

/**
 * Set up standard API mocks for E2E tests.
 * Call this before navigating to the page.
 */
export async function mockAllApis(page: Page) {
  await page.route('/api/status', (route: Route) =>
    route.fulfill({ json: STATUS_OK })
  );
  await page.route('/api/jobs', (route: Route) =>
    route.fulfill({ json: JOBS_LIST })
  );
  await page.route('/api/library', (route: Route) =>
    route.fulfill({ json: LIBRARY_ITEMS })
  );
  await page.route('/api/library/poster*', (route: Route) =>
    route.fulfill({ status: 404 })
  );
  await page.route('/api/library/episodes*', (route: Route) =>
    route.fulfill({ json: EPISODES_TV })
  );
  await page.route('/api/library/refresh', (route: Route) =>
    route.fulfill({ json: REFRESH_RESULT })
  );
  await page.route('/api/mpc/status', (route: Route) =>
    route.fulfill({ json: MPC_STATUS_UNREACHABLE })
  );
  await page.route('/api/mpc/command', (route: Route) =>
    route.fulfill({ json: { ok: true } })
  );
  await page.route('/api/mpc/open', (route: Route) =>
    route.fulfill({ json: { ok: true } })
  );
  await page.route('/api/version', (route: Route) =>
    route.fulfill({ json: VERSION_RESULT })
  );
  await page.route('/api/search', (route: Route) =>
    route.fulfill({ json: SEARCH_RESULT })
  );
  await page.route('/api/download', (route: Route) =>
    route.fulfill({ status: 201, json: DOWNLOAD_RESULT })
  );
  await page.route('/api/progress*', (route: Route) => {
    if (route.request().method() === 'POST') {
      return route.fulfill({ json: { ok: true } });
    }
    return route.fulfill({ json: { positionMs: 0, durationMs: 0, watched: false } });
  });
}
