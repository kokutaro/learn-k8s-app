export function currentIsoWeekId(date = new Date()) {
  const normalized = new Date(Date.UTC(date.getFullYear(), date.getMonth(), date.getDate()))
  const day = normalized.getUTCDay() || 7
  normalized.setUTCDate(normalized.getUTCDate() + 4 - day)
  const yearStart = new Date(Date.UTC(normalized.getUTCFullYear(), 0, 1))
  const weekNumber = Math.ceil((((normalized.getTime() - yearStart.getTime()) / 86400000) + 1) / 7)
  return `${normalized.getUTCFullYear()}-W${String(weekNumber).padStart(2, '0')}`
}

export function formatTimestamp(value: number) {
  return new Intl.DateTimeFormat('ja-JP', {
    hour: '2-digit',
    minute: '2-digit',
    month: 'numeric',
    day: 'numeric',
  }).format(new Date(value))
}
