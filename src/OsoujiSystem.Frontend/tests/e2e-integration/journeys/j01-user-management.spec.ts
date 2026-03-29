import { expect, test } from '@playwright/test'
import {
  uniqueDisplayName,
  uniqueEmployeeNumber,
  uniqueSuffix,
} from '../helpers/api-helpers'
import {
  createUser,
  editUser,
  goToUsers,
} from '../helpers/page-actions'

/**
 * J01: ユーザー管理ジャーニー
 *
 * - ユーザー登録 → 一覧反映確認
 * - プロフィール更新 → 更新反映確認
 */
test.describe.serial('J01 - ユーザー管理', () => {
  const suffix = uniqueSuffix()
  const employeeNumber = uniqueEmployeeNumber(0, suffix)
  const displayName = uniqueDisplayName(0, suffix)
  const updatedDisplayName = `${displayName} Updated`

  test('ユーザーを新規登録し一覧に表示される', async ({ page }) => {
    await goToUsers(page)

    await createUser(page, {
      employeeNumber,
      displayName,
      emailAddress: `e2e-${suffix}@example.com`,
      departmentCode: 'E2E',
    })

    // Verify the user appears in the list
    await expect(page.getByText(displayName, { exact: true })).toBeVisible()
    await expect(page.getByText(employeeNumber, { exact: true })).toBeVisible()
  })

  test('ユーザーのプロフィールを更新できる', async ({ page }) => {
    await goToUsers(page)

    // Wait for user to appear in the list (projection may need time)
    await expect(page.getByText(displayName, { exact: true })).toBeVisible()

    await editUser(page, displayName, {
      displayName: updatedDisplayName,
      departmentCode: 'UPD',
    })

    // Verify updated name appears in the list
    await expect(page.getByText(updatedDisplayName, { exact: true })).toBeVisible()
  })
})
