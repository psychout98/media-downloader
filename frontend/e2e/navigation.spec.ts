import { test, expect } from '@playwright/test';
import {
  mockAllApis,
  STATUS_OK,
  MPC_STATUS_UNREACHABLE,
  MPC_STATUS_PLAYING,
} from './fixtures';

// AC-24: E2E — Navigation

test.describe('AC-24: Navigation', () => {
  test.beforeEach(async ({ page }) => {
    await mockAllApis(page);
  });

  // 24.1: App loads with "Media Downloader" header and "Connected" status
  test('24.1 — header and connected status', async ({ page }) => {
    await page.goto('/');
    await expect(page.locator('h1')).toHaveText('Media Downloader');
    await expect(page.getByText('Connected')).toBeVisible();
  });

  // 24.2: Default tab is Queue showing search input
  test('24.2 — default tab is Queue', async ({ page }) => {
    await page.goto('/');
    await expect(page.getByPlaceholder('Search movies and TV shows...')).toBeVisible();
  });

  // 24.3: Library tab shows search input
  test('24.3 — Library tab shows search input', async ({ page }) => {
    await page.goto('/');
    await page.getByRole('button', { name: 'Library' }).click();
    await expect(page.getByPlaceholder('Search library...')).toBeVisible();
  });

  // 24.4: Now Playing tab shows "MPC-BE not reachable" when offline
  test('24.4 — Now Playing shows MPC-BE not reachable', async ({ page }) => {
    await page.route('/api/mpc/status', (route) =>
      route.fulfill({ json: MPC_STATUS_UNREACHABLE })
    );
    await page.goto('/');
    await page.getByRole('button', { name: 'Now Playing' }).click();
    await expect(page.getByText('MPC-BE not reachable')).toBeVisible();
  });

  // 24.5: Queue tab returns to search/download view
  test('24.5 — return to Queue tab', async ({ page }) => {
    await page.goto('/');
    await page.getByRole('button', { name: 'Library' }).click();
    await page.getByRole('button', { name: 'Queue' }).click();
    await expect(page.getByPlaceholder('Search movies and TV shows...')).toBeVisible();
  });

  // 24.6: Shows "Disconnected" when /api/status returns 500
  test('24.6 — disconnected on API failure', async ({ page }) => {
    await page.route('/api/status', (route) =>
      route.fulfill({ status: 500, json: { error: 'server_error', detail: 'Internal error' } })
    );
    await page.goto('/');
    await expect(page.getByText('Disconnected')).toBeVisible();
  });

  // 17.6: Tab navigation switches between tabs
  test('17.6 — tab navigation switches views', async ({ page }) => {
    await page.goto('/');

    // Queue is default
    await expect(page.getByPlaceholder('Search movies and TV shows...')).toBeVisible();

    // Switch to Library
    await page.getByRole('button', { name: 'Library' }).click();
    await expect(page.getByPlaceholder('Search library...')).toBeVisible();

    // Switch to Now Playing
    await page.getByRole('button', { name: 'Now Playing' }).click();
    await expect(page.getByText('MPC-BE not reachable')).toBeVisible();

    // Switch back to Queue
    await page.getByRole('button', { name: 'Queue' }).click();
    await expect(page.getByPlaceholder('Search movies and TV shows...')).toBeVisible();
  });
});
