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
    goToFacilities,
    goToUsers,
    goToWeeklyDutyPlans,
    openPlanDetail,
    publishPlan,
    selectAreaForPlan,
} from '../helpers/page-actions'

/**
 * J02: 初期セットアップジャーニー
 *
 * 施設作成 → ユーザー作成 → エリア作成 → メンバーアサイン → 計画生成 → 発行
 *
 * All steps run serially because each depends on data created in the previous step.
 */
test.describe.serial('J02 - 初期セットアップ', () => {
  const suffix = uniqueSuffix()
  const facilityCode = uniqueFacilityCode(suffix)
  const facilityName = `E2E Facility ${suffix}`
  const areaName = uniqueAreaName(suffix)
  const weekId = currentIsoWeekId()
  let createdAreaId: string | null = null

  const user1 = {
    employeeNumber: uniqueEmployeeNumber(1, suffix),
    displayName: uniqueDisplayName(1, suffix),
  }
  const user2 = {
    employeeNumber: uniqueEmployeeNumber(2, suffix),
    displayName: uniqueDisplayName(2, suffix),
  }

  test('Step 1: 施設を作成する', async ({ page }) => {
    await goToFacilities(page)

    await createFacility(page, {
      facilityCode,
      name: facilityName,
      timeZoneId: 'Asia/Tokyo',
      description: `E2E integration test facility (${suffix})`,
    })

  })

  test('Step 2: ユーザーを2名作成する', async ({ page }) => {
    await goToUsers(page)

    await createUser(page, {
      employeeNumber: user1.employeeNumber,
      displayName: user1.displayName,
      emailAddress: `e2e-u1-${suffix}@example.com`,
      departmentCode: 'E2E',
    })

    await createUser(page, {
      employeeNumber: user2.employeeNumber,
      displayName: user2.displayName,
      emailAddress: `e2e-u2-${suffix}@example.com`,
      departmentCode: 'E2E',
    })
  })

  test('Step 3: 掃除エリアを作成する', async ({ page }) => {
    await goToCleaningAreas(page)

    createdAreaId = await createCleaningArea(page, {
      facilityName,
      areaName,
      effectiveFromWeek: weekId,
      spots: [
        { name: `${areaName} Spot A`, sortOrder: 10 },
      ],
    })

    // The area should appear in the list and be auto-selected
    await expect(page.getByRole('heading', { name: areaName })).toBeVisible()

    // Verify the spots are visible in the detail view
    await expect(page.getByText(`${areaName} Spot A`)).toBeVisible()
  })

  test('Step 4: メンバーをアサインする', async ({ page }) => {
    if (!createdAreaId) {
      throw new Error('Step 4 requires areaId created in Step 3')
    }

    await goToCleaningAreas(page)
    await page.goto(`/cleaning-areas?areaId=${createdAreaId}`)
    await expect(page.getByRole('heading', { name: areaName })).toBeVisible()

    // Assign user 1
    // The select option shows: "DisplayName (EmployeeNumber)"
    await assignAnyMemberToArea(page)

    // Verify at least one member is shown in the member section
    await expect.poll(async () => page.getByRole('button', { name: '解除' }).count(), {
      timeout: 15_000,
    }).toBeGreaterThanOrEqual(1)
  })

  test('Step 5: 今週の清掃計画を生成する', async ({ page }) => {
    await goToWeeklyDutyPlans(page)

    await selectAreaForPlan(page, areaName)

    // Wait for the current week to be loaded
    await expect(page.getByRole('button', { name: '今週の計画を作成' })).toBeEnabled()

    await generatePlan(page)

    // A plan should appear in the list
    await expect(page.getByRole('button', { name: '詳細' }).first()).toBeVisible()
  })

  test('Step 6: 計画を発行する', async ({ page }) => {
    await goToWeeklyDutyPlans(page)

    await selectAreaForPlan(page, areaName)

    // Select the plan to see details
    await expect(page.getByRole('button', { name: '詳細' }).first()).toBeVisible()
    await openPlanDetail(page)

    // Verify plan detail is shown with assignments
    await expect(page.getByRole('heading', { name: areaName, exact: true })).toBeVisible()

    // Publish the plan
    await publishPlan(page)

    // Verify status changed to published
    await expect(page.getByText('公開済み').first()).toBeVisible()
  })
})
