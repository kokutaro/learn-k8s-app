import '@testing-library/jest-dom/vitest'
import { afterAll, afterEach, beforeAll, vi } from 'vitest'
import { server } from './server'

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => {
  server.resetHandlers()
  vi.restoreAllMocks()
  window.localStorage.clear()
})
afterAll(() => server.close())

Object.defineProperty(window, 'scrollTo', {
  value: vi.fn(),
  writable: true,
})
