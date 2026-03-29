import type { FullConfig } from '@playwright/test'

const DEFAULT_BASE_URL = 'http://localhost:5173'
const DEFAULT_TIMEOUT_MS = 60_000
const DEFAULT_INTERVAL_MS = 1_000

type ReadyCheckOptions = {
  baseUrl: string
  timeoutMs: number
  intervalMs: number
}

async function waitForFrontendReady(options: ReadyCheckOptions): Promise<void> {
  const startedAt = Date.now()
  const deadline = startedAt + options.timeoutMs

  let lastError: string | undefined

  while (Date.now() < deadline) {
    try {
      const response = await fetch(options.baseUrl, {
        redirect: 'follow',
      })

      if (response.ok) {
        return
      }

      lastError = `HTTP ${response.status} ${response.statusText}`
    } catch (error) {
      lastError = error instanceof Error ? error.message : String(error)
    }

    await new Promise((resolve) => setTimeout(resolve, options.intervalMs))
  }

  const elapsedMs = Date.now() - startedAt
  throw new Error(
    [
      `Frontend readiness check timed out after ${elapsedMs}ms.`,
      `Expected reachable URL: ${options.baseUrl}`,
      'Before running integration E2E, start Aspire from repository root:',
      '  aspire run',
      lastError ? `Last observed error: ${lastError}` : undefined,
    ]
      .filter(Boolean)
      .join('\n'),
  )
}

export default async function globalSetup(config: FullConfig): Promise<void> {
  const baseUrl = (config.projects[0]?.use?.baseURL as string | undefined)
    ?? process.env.E2E_BASE_URL
    ?? DEFAULT_BASE_URL

  const timeoutMs = Number.parseInt(process.env.E2E_READY_TIMEOUT_MS ?? '', 10)
  const intervalMs = Number.parseInt(process.env.E2E_READY_INTERVAL_MS ?? '', 10)

  await waitForFrontendReady({
    baseUrl,
    timeoutMs: Number.isFinite(timeoutMs) && timeoutMs > 0 ? timeoutMs : DEFAULT_TIMEOUT_MS,
    intervalMs: Number.isFinite(intervalMs) && intervalMs > 0 ? intervalMs : DEFAULT_INTERVAL_MS,
  })
}
