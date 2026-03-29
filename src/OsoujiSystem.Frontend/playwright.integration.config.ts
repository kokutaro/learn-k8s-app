import { defineConfig } from '@playwright/test'

/**
 * Integration E2E tests that run against a live Aspire environment.
 *
 * Prerequisites:
 *   - Run `aspire run` from the repository root before executing tests.
 *   - The frontend is expected at http://localhost:5173 (configured in AppHost).
 */
export default defineConfig({
  testDir: './tests/e2e-integration',
  globalSetup: './tests/e2e-integration/global-setup.ts',
  fullyParallel: false,
  workers: 1,
  retries: 0,
  timeout: 60_000,
  expect: {
    timeout: 15_000,
  },
  use: {
    baseURL: 'http://localhost:5173',
    headless: true,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
})
