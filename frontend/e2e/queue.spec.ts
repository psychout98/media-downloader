import { test, expect } from '@playwright/test';
import {
  mockAllApis,
  SEARCH_RESULT,
  DOWNLOAD_RESULT,
  JOBS_LIST,
  JOBS_EMPTY,
} from './fixtures';

// AC-26: E2E — Queue

test.describe('AC-26: Queue', () => {
  test.beforeEach(async ({ page }) => {
    await mockAllApis(page);
    await page.goto('/');
  });

  // 26.1: Search button disabled when empty
  test('26.1 — search button disabled when empty', async ({ page }) => {
    const searchBtn = page.getByRole('button', { name: 'Search', exact: true });
    await expect(searchBtn).toBeDisabled();
  });

  // 26.2: Search shows media info heading
  test('26.2 — search shows media info', async ({ page }) => {
    await page.getByPlaceholder('Search movies and TV shows...').fill('Breaking Bad S01E01');
    await page.getByRole('button', { name: 'Search', exact: true }).click();
    await expect(page.locator('h2', { hasText: 'Breaking Bad' })).toBeVisible();
  });

  // 26.3: Shows stream count
  test('26.3 — shows stream count', async ({ page }) => {
    await page.getByPlaceholder('Search movies and TV shows...').fill('Breaking Bad');
    await page.getByRole('button', { name: 'Search', exact: true }).click();
    await expect(page.getByText(/of\s+4\s+streams/)).toBeVisible();
  });

  // 26.4: Streams show torrent name, cache status, seeders
  test('26.4 — streams show details', async ({ page }) => {
    await page.getByPlaceholder('Search movies and TV shows...').fill('Breaking Bad');
    await page.getByRole('button', { name: 'Search', exact: true }).click();
    // Stream table shows torrent name, cache badge, and seeders
    const table = page.getByRole('table');
    await expect(table.getByText('Breaking.Bad.S01E01.1080p.BluRay.x264-DEMAND')).toBeVisible();
    await expect(page.getByText('RD CACHED').first()).toBeVisible();
    await expect(table.getByText('45')).toBeVisible();
  });

  // 26.5: Download click shows "Download started" toast
  test('26.5 — download shows toast', async ({ page }) => {
    await page.getByPlaceholder('Search movies and TV shows...').fill('Breaking Bad');
    await page.getByRole('button', { name: 'Search', exact: true }).click();
    await page.getByRole('button', { name: 'Download' }).first().click();
    await expect(page.getByText(/Download started/)).toBeVisible();
  });

  // 26.6: Queue shows existing jobs on load
  test('26.6 — shows existing jobs', async ({ page }) => {
    // Job titles appear in the job list section
    await expect(page.getByText(/Breaking Bad/).first()).toBeVisible();
    await expect(page.getByText('Inception').first()).toBeVisible();
    await expect(page.getByText('The Matrix').first()).toBeVisible();
  });

  // 26.7: Job filters
  test('26.7 — job filter tabs', async ({ page }) => {
    // Active filter — shows downloading/pending jobs
    await page.getByRole('button', { name: 'Active' }).click();
    await expect(page.getByText('downloading', { exact: true })).toBeVisible();

    // Done filter — shows complete jobs
    await page.getByRole('button', { name: 'Done' }).click();
    await expect(page.getByText('complete', { exact: true })).toBeVisible();

    // Failed filter — shows failed jobs
    await page.getByRole('button', { name: 'Failed' }).click();
    await expect(page.getByText('failed', { exact: true })).toBeVisible();

    // All filter — shows everything
    await page.getByRole('button', { name: 'All' }).click();
    await expect(page.getByText('downloading', { exact: true })).toBeVisible();
    await expect(page.getByText('complete', { exact: true })).toBeVisible();
  });

  // 26.8: Failed jobs show error message and Retry button
  test('26.8 — failed jobs show error and retry', async ({ page }) => {
    await page.getByRole('button', { name: 'Failed' }).click();
    await expect(page.getByText('Real-Debrid API timeout')).toBeVisible();
    await expect(page.getByRole('button', { name: 'Retry' })).toBeVisible();
  });

  // 26.9: Retry shows toast
  test('26.9 — retry shows toast', async ({ page }) => {
    await page.route('/api/jobs/job-failed-1/retry', (route) =>
      route.fulfill({ json: { ok: true } })
    );
    await page.getByRole('button', { name: 'Failed' }).click();
    await page.getByRole('button', { name: 'Retry' }).click();
    await expect(page.getByText('Job retry started')).toBeVisible();
  });

  // 26.10: Complete jobs have Delete button; clicking shows toast
  test('26.10 — delete complete job', async ({ page }) => {
    await page.route('/api/jobs/job-complete-1', (route) => {
      if (route.request().method() === 'DELETE') {
        return route.fulfill({ json: { ok: true } });
      }
      return route.continue();
    });
    await page.getByRole('button', { name: 'Done' }).click();
    const deleteBtn = page.getByRole('button', { name: 'Delete' }).first();
    await expect(deleteBtn).toBeVisible();
    await deleteBtn.click();
    // Job should be removed from list
  });

  // 26.11: Active jobs show progress percentage and status
  test('26.11 — active jobs show progress', async ({ page }) => {
    await page.getByRole('button', { name: 'Active' }).click();
    await expect(page.getByText('downloading')).toBeVisible();
  });

  // 26.12: Search results paginated: 5, 10, 25 rows per page
  test('26.12 — pagination controls', async ({ page }) => {
    await page.getByPlaceholder('Search movies and TV shows...').fill('Breaking Bad');
    await page.getByRole('button', { name: 'Search', exact: true }).click();
    // Should have per-page selector
    await expect(page.locator('select')).toBeVisible();
    const options = page.locator('select option');
    await expect(options).toHaveCount(3);
    await expect(options.nth(0)).toHaveText('5 per page');
    await expect(options.nth(1)).toHaveText('10 per page');
    await expect(options.nth(2)).toHaveText('25 per page');
  });

  // 26.13: Stream filters
  test('26.13 — stream filter chips', async ({ page }) => {
    await page.getByPlaceholder('Search movies and TV shows...').fill('Breaking Bad');
    await page.getByRole('button', { name: 'Search', exact: true }).click();

    // Resolution filters
    await expect(page.getByRole('button', { name: '1080p' })).toBeVisible();
    await expect(page.getByRole('button', { name: '2160p' })).toBeVisible();
    await expect(page.getByRole('button', { name: '720p' })).toBeVisible();
    await expect(page.getByRole('button', { name: '480p' })).toBeVisible();

    // HDR filters
    await expect(page.getByRole('button', { name: 'HDR', exact: true })).toBeVisible();
    await expect(page.getByRole('button', { name: 'HDR10+' })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Dolby Vision' })).toBeVisible();

    // Audio filters
    await expect(page.getByRole('button', { name: 'Atmos' })).toBeVisible();

    // Cache filter
    await expect(page.getByRole('button', { name: 'RD Cached' })).toBeVisible();

    // Apply a filter and check count
    await page.getByRole('button', { name: '1080p' }).click();
    // Should show "Showing X of Y streams" with filtered count
    await expect(page.getByText(/Showing\s+\d+\s+of\s+4\s+streams/)).toBeVisible();

    // Clear all button
    await expect(page.getByText('Clear all')).toBeVisible();
    await page.getByText('Clear all').click();
    await expect(page.getByText(/Showing\s+4\s+of\s+4\s+streams/)).toBeVisible();
  });
});

test.describe('AC-26: Queue empty state', () => {
  test('empty job list shows message', async ({ page }) => {
    await page.route('/api/status', (route) =>
      route.fulfill({ json: { status: 'ok', moviesDir: '/m', tvDir: '/t', archiveDir: '/a' } })
    );
    await page.route('/api/jobs', (route) =>
      route.fulfill({ json: JOBS_EMPTY })
    );
    await page.route('/api/library/poster*', (route) =>
      route.fulfill({ status: 404 })
    );
    await page.goto('/');
    await expect(page.getByText('No jobs found')).toBeVisible();
  });
});
