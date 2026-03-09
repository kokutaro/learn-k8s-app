import { describe, expect, it } from 'vitest'
import {
  DASHBOARD_SETTINGS_KEY,
  defaultDashboardSettings,
  loadDashboardSettings,
  saveDashboardSettings,
} from './dashboard-settings'

describe('dashboard settings', () => {
  it('returns default settings when local storage is empty', () => {
    expect(loadDashboardSettings()).toEqual(defaultDashboardSettings)
  })

  it('persists and restores dashboard settings', () => {
    const settings = {
      layout: 'double' as const,
      areaIds: [
        '11111111-1111-1111-1111-111111111111',
        '22222222-2222-2222-2222-222222222222',
      ],
    }

    saveDashboardSettings(settings)

    expect(window.localStorage.getItem(DASHBOARD_SETTINGS_KEY)).not.toBeNull()
    expect(loadDashboardSettings()).toEqual(settings)
  })
})
