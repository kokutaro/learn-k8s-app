import type { Page } from '@playwright/test'
import { currentIsoWeekId as currentIsoWeekIdFromApp } from '../../../src/lib/date'

const BASE_URL = 'http://localhost:5173'
const API_BASE = `${BASE_URL}/api/v1`

/**
 * Generate unique test data to avoid collisions across test runs.
 * Uses a timestamp suffix so tests can run multiple times without DB reset.
 */
export function uniqueSuffix(): string {
  return Date.now().toString(36).slice(-6)
}

export function uniqueFacilityCode(suffix: string): string {
  return `E2E-${suffix}`.slice(0, 20)
}

export function uniqueEmployeeNumber(index: number, suffix: string): string {
  const base = Number.parseInt(suffix, 36) % 900000
  return String(100000 + base + index).slice(0, 6)
}

export function uniqueAreaName(suffix: string): string {
  return `E2E Area ${suffix}`
}

export function uniqueDisplayName(index: number, suffix: string): string {
  return `E2E User ${index} ${suffix}`
}

export function currentIsoWeekId(date = new Date()): string {
  return currentIsoWeekIdFromApp(date)
}

/**
 * Wait for a condition to appear in the API response.
 * Polls the given URL until the predicate returns true, or throws on timeout.
 */
export async function waitForApiCondition<T>(
  url: string,
  predicate: (body: T) => boolean,
  timeoutMs = 15_000,
  intervalMs = 500,
): Promise<T> {
  const deadline = Date.now() + timeoutMs
  while (Date.now() < deadline) {
    const response = await fetch(url)
    if (response.ok) {
      const body = (await response.json()) as T
      if (predicate(body)) return body
    }
    await new Promise((resolve) => setTimeout(resolve, intervalMs))
  }
  throw new Error(`waitForApiCondition timed out after ${timeoutMs}ms: ${url}`)
}

/**
 * Wait until a newly created item appears in a list endpoint.
 */
export async function waitForListItem(
  listUrl: string,
  predicate: (item: Record<string, unknown>) => boolean,
  timeoutMs = 15_000,
): Promise<void> {
  await waitForApiCondition<{ data: Array<Record<string, unknown>> }>(
    listUrl,
    (body) => body.data.some(predicate),
    timeoutMs,
  )
}

/**
 * Dismiss any visible banner/toast by waiting for it to be hidden.
 */
export async function waitForBannerDismiss(page: Page): Promise<void> {
  const banner = page.locator('[role="status"], [role="alert"]')
  if ((await banner.count()) > 0) {
    await banner.first().waitFor({ state: 'hidden', timeout: 10_000 }).catch(() => {
      // banner may have already been dismissed — ignore
    })
  }
}

/**
 * Fetch the current week info for an area via the API.
 */
export async function fetchCurrentWeek(areaId: string): Promise<{ weekId: string; weekLabel: string }> {
  const response = await fetch(`${API_BASE}/cleaning-areas/${areaId}/current-week`)
  if (!response.ok) throw new Error(`Failed to fetch current week for area ${areaId}: ${response.status}`)
  const body = (await response.json()) as { data: { weekId: string; weekLabel: string } }
  return body.data
}
