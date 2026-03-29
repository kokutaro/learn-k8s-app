import { expect, test } from '@playwright/test'
import {
    currentIsoWeekId,
    uniqueAreaName,
    uniqueDisplayName,
    uniqueEmployeeNumber,
    uniqueFacilityCode,
    uniqueSuffix,
} from '../helpers/api-helpers'
import {
    assignAnyMemberToArea,
    createCleaningArea,
    createFacility,
    createUser,
    generatePlan,
    goToCleaningAreas,
    goToDashboard,
    goToFacilities,
    goToUsers,
    goToWeeklyDutyPlans,
    openPlanDetail,
    publishPlan,
    selectAreaForPlan,
} from '../helpers/page-actions'

/**
 * J09: 計画ライフサイクル + ダッシュボード確認ジャーニー
 *
 * セットアップ → 計画生成 → 発行 → ダッシュボードに担当が表示される
 */
test.describe.serial('J09 - ダッシュボードで今週の担当確認', () => {
  const suffix = uniqueSuffix()
  const facilityCode = uniqueFacilityCode(suffix)
  const facilityName = `E2E Dash ${suffix}`
  const areaName = uniqueAreaName(suffix)
  const weekId = currentIsoWeekId()
  let createdAreaId: string | null = null

  const user1 = {
    employeeNumber: uniqueEmployeeNumber(5, suffix),
    displayName: uniqueDisplayName(5, suffix),
  }
  const user2 = {
    employeeNumber: uniqueEmployeeNumber(6, suffix),
    displayName: uniqueDisplayName(6, suffix),
  }

  test('Setup: 施設・ユーザー・エリアを作成し計画を発行する', async ({ page }) => {
    // Create facility
    await goToFacilities(page)
    await createFacility(page, {
      facilityCode,
      name: facilityName,
      timeZoneId: 'Asia/Tokyo',
    })

    // Create users
    await goToUsers(page)
    await createUser(page, {
      employeeNumber: user1.employeeNumber,
      displayName: user1.displayName,
      emailAddress: `e2e-d1-${suffix}@example.com`,
      departmentCode: 'E2E',
    })
    await createUser(page, {
      employeeNumber: user2.employeeNumber,
      displayName: user2.displayName,
      emailAddress: `e2e-d2-${suffix}@example.com`,
      departmentCode: 'E2E',
    })

    // Create cleaning area with 2 spots
    await goToCleaningAreas(page)
    createdAreaId = await createCleaningArea(page, {
      facilityName,
      areaName,
      effectiveFromWeek: weekId,
      spots: [
        { name: `${areaName} Spot X`, sortOrder: 10 },
      ],
    })

    // Assign members
    if (!createdAreaId) {
      throw new Error('Setup requires areaId created in cleaning-area step')
    }
    await page.goto(`/cleaning-areas?areaId=${createdAreaId}`)
    await expect(page.getByRole('heading', { name: areaName })).toBeVisible()
    await assignAnyMemberToArea(page)

    // Generate and publish plan
    await goToWeeklyDutyPlans(page)
    await selectAreaForPlan(page, areaName)
    await expect(page.getByRole('button', { name: '今週の計画を作成' })).toBeEnabled()
    await generatePlan(page)
    await expect(page.getByRole('button', { name: '詳細' }).first()).toBeVisible()
    await openPlanDetail(page)
    await publishPlan(page)
  })

  test('ダッシュボードで今週の担当が表示される', async ({ page }) => {
    await goToDashboard(page)

    // Open settings and select the area
    await page.getByRole('button', { name: '設定' }).click()
    await page.getByLabel('エリア 1').selectOption({ label: areaName })

    // Verify the area heading appears
    await expect(page.getByRole('heading', { name: areaName })).toBeVisible()

    // Verify the plan status is "公開済み"
    await expect(page.getByText('公開済み')).toBeVisible()

    // Verify that spot names are displayed
    await expect(page.getByText(`${areaName} Spot X`)).toBeVisible()
    // Verify dashboard is showing assignment content (not empty state)
    await expect(page.getByText('今週の計画がありません')).toHaveCount(0)
  })
})
