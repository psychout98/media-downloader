import { test, expect } from '@playwright/test';
import {
  mockAllApis,
  LIBRARY_ITEMS,
  LIBRARY_EMPTY,
  EPISODES_TV,
  REFRESH_RESULT,
} from './fixtures';

// AC-25: E2E — Library

test.describe('AC-25: Library', () => {
  test.beforeEach(async ({ page }) => {
    await mockAllApis(page);
    await page.goto('/');
    await page.getByRole('button', { name: 'Library' }).click();
  });

  // 25.1: Displays media cards for movies, TV, anime
  test('25.1 — displays media cards', async ({ page }) => {
    await expect(page.getByText('Breaking Bad')).toBeVisible();
    await expect(page.getByText('Inception')).toBeVisible();
    await expect(page.getByText('Naruto')).toBeVisible();
  });

  // 25.2: Filter buttons show/hide cards by type
  test('25.2 — filter buttons', async ({ page }) => {
    // Movies filter
    await page.getByRole('button', { name: 'Movies' }).click();
    await expect(page.getByText('Inception')).toBeVisible();
    await expect(page.getByText('Breaking Bad')).not.toBeVisible();

    // TV filter
    await page.getByRole('button', { name: 'TV' }).click();
    await expect(page.getByText('Breaking Bad')).toBeVisible();
    await expect(page.getByText('Naruto')).toBeVisible();
    await expect(page.getByText('Inception')).not.toBeVisible();

    // All filter
    await page.getByRole('button', { name: 'All' }).click();
    await expect(page.getByText('Breaking Bad')).toBeVisible();
    await expect(page.getByText('Inception')).toBeVisible();
    await expect(page.getByText('Naruto')).toBeVisible();
  });

  // 25.3: Search filters by title (case-insensitive)
  test('25.3 — search filters by title', async ({ page }) => {
    await page.getByPlaceholder('Search library...').fill('breaking');
    await expect(page.getByText('Breaking Bad')).toBeVisible();
    await expect(page.getByText('Inception')).not.toBeVisible();
    await expect(page.getByText('Naruto')).not.toBeVisible();
  });

  // 25.4: "No results found" when search has no matches
  test('25.4 — no results message', async ({ page }) => {
    await page.getByPlaceholder('Search library...').fill('zzznonexistent');
    await expect(page.getByText('No results found')).toBeVisible();
  });

  // 25.5: Clicking card opens detail modal with title heading
  test('25.5 — clicking card opens modal', async ({ page }) => {
    await page.getByText('Breaking Bad').first().click();
    await expect(page.locator('h2', { hasText: 'Breaking Bad' })).toBeVisible();
  });

  // 25.6: Modal fetches and displays episodes
  test('25.6 — modal shows episodes', async ({ page }) => {
    await page.getByText('Breaking Bad').first().click();
    await expect(page.getByText('Pilot', { exact: true })).toBeVisible();
    await expect(page.getByText(/Cat.s in the Bag/).first()).toBeVisible();
  });

  // 25.7: Refresh button triggers refresh and shows result toast
  test('25.7 — refresh button', async ({ page }) => {
    await page.getByRole('button', { name: 'Refresh' }).click();
    await expect(page.getByText('Library refreshed')).toBeVisible();
  });

  // 25.8: Cards show type badge and year
  test('25.8 — cards show type badge and year', async ({ page }) => {
    // Check for type badges in the grid
    const tvBadges = page.locator('text=TV');
    expect(await tvBadges.count()).toBeGreaterThanOrEqual(2); // Breaking Bad and Naruto
    await expect(page.getByText('2008').first()).toBeVisible();
    await expect(page.getByText('2010').first()).toBeVisible();
  });

  // 25.9: Episodes show Play, Continue, or Restart based on watch status
  test('25.9 — episode action buttons by state', async ({ page }) => {
    await page.getByText('Breaking Bad').first().click();
    // Wait for episodes to load
    await expect(page.getByText('Pilot', { exact: true })).toBeVisible();

    // Watched episodes → Restart button (Season 1 is expanded, ep 1+2 are watched)
    await expect(page.getByRole('button', { name: 'Restart' }).first()).toBeVisible();

    // In-progress episode → Continue button (ep 3 is in-progress, banner + episode row)
    // The banner has a Continue button, and the episode row also has one
    const continueButtons = page.getByRole('button', { name: /Continue/ });
    expect(await continueButtons.count()).toBeGreaterThanOrEqual(1);

    // Unwatched episode → Play button (ep 4 has no progress)
    const playButtons = page.getByRole('button', { name: /Play/ });
    expect(await playButtons.count()).toBeGreaterThanOrEqual(1);
  });

  // 25.10: Episodes with watch history show seek bar
  test('25.10 — episodes with progress show bar', async ({ page }) => {
    await page.getByText('Breaking Bad').first().click();
    await expect(page.getByText('Pilot', { exact: true })).toBeVisible();
    // Watched episode should show "Watched" label, and in-progress shows percentage
    await expect(page.getByText('Watched', { exact: true }).first()).toBeVisible();
    // In-progress episode should show percentage + time left
    await expect(page.getByText(/\d+%/).first()).toBeVisible();
  });

  // 25.11: Continue Watching button advances to next episode if current > 85%
  test('25.11 — continue watching banner', async ({ page }) => {
    await page.getByText('Breaking Bad').first().click();
    // S01E03 is at 37.5% (1125000/3000000), so it should show as continue watching
    await expect(page.getByText('Continue Watching')).toBeVisible();
    await expect(page.getByText('S01E03', { exact: true }).first()).toBeVisible();
  });
});

test.describe('AC-25: Library empty state', () => {
  test('empty library shows message', async ({ page }) => {
    await page.route('/api/status', (route) =>
      route.fulfill({ json: { status: 'ok', moviesDir: '/m', tvDir: '/t', archiveDir: '/a' } })
    );
    await page.route('/api/library', (route) =>
      route.fulfill({ json: LIBRARY_EMPTY })
    );
    await page.route('/api/jobs', (route) =>
      route.fulfill({ json: { jobs: [] } })
    );
    await page.route('/api/library/poster*', (route) =>
      route.fulfill({ status: 404 })
    );
    await page.goto('/');
    await page.getByRole('button', { name: 'Library' }).click();
    await expect(page.getByText('Library is empty')).toBeVisible();
  });
});
