import { z } from 'zod'
import { guidSchema } from './contracts'

export const dashboardSettingsSchema = z.object({
  layout: z.enum(['single', 'double']),
  areaIds: z.array(guidSchema).max(2),
})

export type DashboardSettings = z.infer<typeof dashboardSettingsSchema>

export const DASHBOARD_SETTINGS_KEY = 'osouji.dashboard.settings.v1'

export const defaultDashboardSettings: DashboardSettings = {
  layout: 'single',
  areaIds: [],
}

export function loadDashboardSettings(): DashboardSettings {
  if (typeof window === 'undefined') {
    return defaultDashboardSettings
  }

  const raw = window.localStorage.getItem(DASHBOARD_SETTINGS_KEY)
  if (!raw) {
    return defaultDashboardSettings
  }

  let json: unknown
  try {
    json = JSON.parse(raw)
  } catch {
    return defaultDashboardSettings
  }

  const parsed = dashboardSettingsSchema.safeParse(json)
  return parsed.success ? parsed.data : defaultDashboardSettings
}

export function saveDashboardSettings(settings: DashboardSettings) {
  if (typeof window === 'undefined') {
    return
  }

  window.localStorage.setItem(DASHBOARD_SETTINGS_KEY, JSON.stringify(settings))
}
