import type { Page } from '@playwright/test'
import { expect } from '@playwright/test'

/* ---------- Navigation ---------- */

export async function goToFacilities(page: Page): Promise<void> {
  await page.goto('/facilities')
  await expect(page.getByRole('heading', { name: '施設管理' })).toBeVisible()
  await page.waitForURL(/\/facilities(\?.*)?$/)
  await page.waitForLoadState('networkidle')
}

export async function goToUsers(page: Page): Promise<void> {
  await page.goto('/users')
  await expect(page.getByRole('heading', { name: 'ユーザー管理' })).toBeVisible()
}

export async function goToCleaningAreas(page: Page): Promise<void> {
  await page.goto('/cleaning-areas')
  await expect(page.getByRole('heading', { name: '掃除エリア管理' })).toBeVisible()
}

export async function goToWeeklyDutyPlans(page: Page): Promise<void> {
  await page.goto('/weekly-duty-plans')
  await expect(page.getByRole('heading', { name: '清掃計画' })).toBeVisible()
}

export async function goToDashboard(page: Page): Promise<void> {
  await page.goto('/dashboard')
  await expect(page.getByRole('heading', { name: '今週の担当' })).toBeVisible()
}

/* ---------- Facility ---------- */

export async function createFacility(
  page: Page,
  data: { facilityCode: string; name: string; timeZoneId?: string; description?: string },
): Promise<void> {
  const form = page.locator('form').filter({ has: page.getByLabel('施設コード') }).first()

  for (let attempt = 0; attempt < 3; attempt++) {
    await page.getByRole('button', { name: '施設を追加' }).click()
    const visible = await form.isVisible().catch(() => false)
    if (visible) {
      break
    }
    await page.waitForTimeout(300)
  }

  await expect(form).toBeVisible()

  await form.getByLabel('施設コード').fill(data.facilityCode)
  await form.getByLabel('施設名').fill(data.name)

  if (data.timeZoneId) {
    await form.getByLabel('タイムゾーン').fill(data.timeZoneId)
  }
  if (data.description) {
    await form.getByLabel('説明').fill(data.description)
  }

  await form.getByRole('button', { name: '保存' }).click()
  await expect(page.getByText('施設を追加しました。')).toBeVisible()
}

/* ---------- User ---------- */

export async function createUser(
  page: Page,
  data: { employeeNumber: string; displayName: string; emailAddress?: string; departmentCode?: string },
): Promise<void> {
  await page.getByRole('button', { name: 'ユーザーを追加' }).click()
  const form = page.locator('form').filter({ has: page.getByLabel('社員番号') }).first()
  await expect(form).toBeVisible()

  await form.getByLabel('社員番号').fill(data.employeeNumber)
  await form.getByLabel('表示名').fill(data.displayName)

  if (data.emailAddress) {
    await form.getByLabel('メールアドレス').fill(data.emailAddress)
  }
  if (data.departmentCode) {
    await form.getByLabel('部署コード').fill(data.departmentCode)
  }

  await form.getByRole('button', { name: '保存' }).click()
  await expect(page.getByText('ユーザーを追加しました。')).toBeVisible()
}

export async function editUser(
  page: Page,
  currentDisplayName: string,
  data: { displayName?: string; emailAddress?: string; departmentCode?: string },
): Promise<void> {
  const row = page.getByRole('row').filter({ hasText: currentDisplayName })
  await row.getByRole('button', { name: '編集' }).click()
  const form = page.locator('form').filter({ has: page.getByRole('button', { name: '更新' }) }).first()
  await expect(form).toBeVisible()

  if (data.displayName) {
    await form.getByLabel('表示名').fill(data.displayName)
  }
  if (data.emailAddress) {
    await form.getByLabel('メールアドレス').fill(data.emailAddress)
  }
  if (data.departmentCode) {
    await form.getByLabel('部署コード').fill(data.departmentCode)
  }

  await form.getByRole('button', { name: '更新' }).click()
  await expect(page.getByText('ユーザー情報を更新しました。')).toBeVisible()
}

/* ---------- Cleaning Area ---------- */

export async function createCleaningArea(
  page: Page,
  data: {
    facilityName: string
    areaName: string
    effectiveFromWeek: string
    spots: Array<{ name: string; sortOrder: number }>
  },
): Promise<string> {
  await page.getByRole('button', { name: '掃除エリアを追加' }).click()
  const form = page.locator('form').filter({ has: page.getByLabel('エリア名') }).first()
  await expect(form).toBeVisible()

  await form.getByLabel('施設').selectOption({ label: data.facilityName })
  await form.getByLabel('エリア名').fill(data.areaName)
  await form.getByLabel('適用開始週').fill(data.effectiveFromWeek)

  // Fill the first spot (always present by default)
  if (data.spots.length > 0) {
    await form.getByLabel('掃除箇所 1').fill(data.spots[0].name)
  }

  // Add and fill additional spots
  for (let i = 1; i < data.spots.length; i++) {
    await form.getByRole('button', { name: '箇所を追加' }).click()
    await form.getByLabel(`掃除箇所 ${i + 1}`).fill(data.spots[i].name)
  }

  const createResponsePromise = page.waitForResponse((response) => {
    return response.url().includes('/api/v1/cleaning-areas')
      && response.request().method() === 'POST'
      && response.status() >= 200
      && response.status() < 300
  })

  await form.getByRole('button', { name: '保存' }).click()
  const createResponse = await createResponsePromise
  const body = (await createResponse.json()) as { data?: { areaId?: string } }
  const areaId = body.data?.areaId
  if (!areaId) {
    throw new Error('createCleaningArea: areaId was not returned by API')
  }

  await expect(page.getByText('掃除エリアを追加しました。')).toBeVisible()
  return areaId
}

export async function selectCleaningArea(page: Page, areaName: string): Promise<void> {
  await page.getByRole('button', { name: areaName }).click()
  await expect(page.getByRole('heading', { name: areaName })).toBeVisible()
}

export async function assignMemberToArea(page: Page, userDisplayName: string): Promise<void> {
  const select = page.getByLabel('アサインするユーザー')
  await expect(select).toBeVisible()

  await expect.poll(async () => {
    const optionTexts = await select.locator('option').allTextContents()
    return optionTexts.some((text) => text.includes(userDisplayName))
  }, {
    timeout: 20_000,
    message: `Waiting for assignable user option: ${userDisplayName}`,
  }).toBe(true)

  await select.evaluate((element, displayName) => {
    const node = element as HTMLSelectElement
    const option = Array.from(node.options).find((item) => item.text.includes(displayName as string))
    if (!option) {
      throw new Error(`Option not found for display name: ${String(displayName)}`)
    }
    node.value = option.value
    node.dispatchEvent(new Event('change', { bubbles: true }))
  }, userDisplayName)

  await page.getByRole('button', { name: 'アサイン' }).click()
  await expect(page.getByText('ユーザーをアサインしました。')).toBeVisible()
}

export async function assignAnyMemberToArea(page: Page): Promise<void> {
  const select = page.getByLabel('アサインするユーザー')
  await expect(select).toBeVisible()

  const candidates = await select.evaluate((element) => {
    const node = element as HTMLSelectElement
    return Array.from(node.options)
      .filter((item) => item.value)
      .map((item) => ({ value: item.value, text: item.text }))
  })

  if (candidates.length === 0) {
    throw new Error('No assignable user option is available.')
  }

  const conflictText = 'このユーザーは別の担当エリアに割り当て済みです。割り当てを解除してから再度操作してください。'
  const baselineCount = await page.getByRole('button', { name: '解除' }).count()

  for (const candidate of candidates) {
    await select.selectOption({ value: candidate.value })
    await page.getByRole('button', { name: 'アサイン' }).click()

    for (let attempt = 0; attempt < 20; attempt++) {
      const success = await page.getByText('ユーザーをアサインしました。').first().isVisible().catch(() => false)
      const conflict = await page.getByText(conflictText).first().isVisible().catch(() => false)
      const currentCount = await page.getByRole('button', { name: '解除' }).count()

      if (success || currentCount > baselineCount) {
        return
      }

      if (conflict) {
        break
      }

      await page.waitForTimeout(300)
    }
  }

  throw new Error('Failed to assign any available user in the area.')
}

/* ---------- Weekly Duty Plan ---------- */

export async function selectAreaForPlan(page: Page, areaName: string): Promise<void> {
  await page.getByLabel('掃除エリア').selectOption({ label: areaName })
}

export async function generatePlan(page: Page): Promise<void> {
  await page.getByRole('button', { name: '今週の計画を作成' }).click()

  await expect.poll(async () => {
    const created = await page.getByText('今週の清掃計画を作成しました。').first().isVisible().catch(() => false)
    const alreadyCreated = await page.getByText('この週の清掃計画はすでに作成されています。').first().isVisible().catch(() => false)
    return created || alreadyCreated
  }, {
    timeout: 15_000,
    message: 'Waiting for plan creation success or already-created feedback',
  }).toBe(true)
}

export async function openPlanDetail(page: Page): Promise<void> {
  await page.getByRole('button', { name: '詳細' }).first().click()
}

export async function publishPlan(page: Page): Promise<void> {
  await page.getByRole('button', { name: '発行する' }).click()
  await expect(page.getByText('清掃計画を発行しました。')).toBeVisible()
}
