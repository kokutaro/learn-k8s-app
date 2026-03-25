import { describe, expect, it } from 'vitest'
import { preserveScrollNavigateOptions } from './navigation'

describe('preserveScrollNavigateOptions', () => {
  it('returns navigate options that keep current scroll position', () => {
    const options = preserveScrollNavigateOptions({
      search: (previous: { status?: string }) => ({ ...previous, status: 'draft' }),
    })

    expect(options.resetScroll).toBe(false)
    expect(typeof options.search).toBe('function')
  })
})