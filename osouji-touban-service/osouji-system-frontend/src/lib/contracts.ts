import { z } from 'zod'

export const guidSchema = z.string().regex(
  /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/,
  'Invalid GUID format.',
)

export const apiErrorDetailSchema = z.object({
  field: z.string(),
  message: z.string(),
  code: z.string(),
})

export const apiErrorSchema = z.object({
  error: z.object({
    code: z.string(),
    message: z.string(),
    details: z.array(apiErrorDetailSchema).optional(),
    args: z.record(z.string(), z.unknown()).optional(),
  }),
})

export const cursorPageMetaSchema = z.object({
  limit: z.number(),
  hasNext: z.boolean(),
  nextCursor: z.string().nullable(),
})

export const cursorPageLinksSchema = z.object({
  self: z.string(),
})

export const weekRuleSchema = z.object({
  startDay: z.string(),
  startTime: z.string(),
  timeZoneId: z.string(),
  effectiveFromWeek: z.string(),
  effectiveFromWeekLabel: z.string().optional(),
})

export const facilitySummarySchema = z.object({
  id: guidSchema,
  facilityCode: z.string(),
  name: z.string(),
  timeZoneId: z.string(),
  lifecycleStatus: z.string(),
  version: z.number(),
})

export const facilityDetailSchema = facilitySummarySchema.extend({
  description: z.string().nullable(),
})

export const userSummarySchema = z.object({
  userId: guidSchema,
  employeeNumber: z.string(),
  displayName: z.string(),
  lifecycleStatus: z.string(),
  departmentCode: z.string().nullable(),
  version: z.number(),
})

export const userDetailSchema = userSummarySchema.extend({
  emailAddress: z.string().email().nullable(),
})

export const cleaningSpotSchema = z.object({
  id: guidSchema,
  name: z.string(),
  sortOrder: z.number(),
})

export const areaMemberSchema = z.object({
  id: guidSchema,
  userId: guidSchema,
  employeeNumber: z.string(),
})

export const cleaningAreaSummarySchema = z.object({
  id: guidSchema,
  facilityId: guidSchema,
  name: z.string(),
  currentWeekRule: weekRuleSchema,
  memberCount: z.number(),
  spotCount: z.number(),
  version: z.number(),
})

export const cleaningAreaDetailSchema = z.object({
  id: guidSchema,
  facilityId: guidSchema,
  name: z.string(),
  currentWeekRule: weekRuleSchema,
  pendingWeekRule: weekRuleSchema.nullable(),
  rotationCursor: z.number(),
  spots: z.array(cleaningSpotSchema),
  members: z.array(areaMemberSchema),
  version: z.number(),
})

export const cleaningAreaCurrentWeekSchema = z.object({
  areaId: guidSchema,
  weekId: z.string(),
  weekLabel: z.string().optional(),
  timeZoneId: z.string(),
})

export const dutyUserSummarySchema = z.object({
  userId: guidSchema,
  employeeNumber: z.string(),
  displayName: z.string(),
  departmentCode: z.string().nullable(),
  lifecycleStatus: z.string(),
})

export const assignmentPolicySchema = z.object({
  fairnessWindowWeeks: z.number(),
})

export const dutyAssignmentSchema = z.object({
  spotId: guidSchema,
  userId: guidSchema,
  user: dutyUserSummarySchema.nullable(),
})

export const offDutyEntrySchema = z.object({
  userId: guidSchema,
  user: dutyUserSummarySchema.nullable(),
})

export const weeklyDutyPlanSummarySchema = z.object({
  id: guidSchema,
  areaId: guidSchema,
  weekId: z.string(),
  weekLabel: z.string().optional(),
  revision: z.number(),
  status: z.string(),
  version: z.number(),
})

export const weeklyDutyPlanDetailSchema = weeklyDutyPlanSummarySchema.extend({
  assignmentPolicy: assignmentPolicySchema,
  assignments: z.array(dutyAssignmentSchema),
  offDutyEntries: z.array(offDutyEntrySchema),
})

export const facilityFormSchema = z.object({
  facilityCode: z.string().trim().min(1).max(50),
  name: z.string().trim().min(1).max(100),
  description: z.string().trim().max(300).optional().or(z.literal('')),
  timeZoneId: z.string().trim().min(1),
})

export const facilityEditSchema = facilityFormSchema.omit({ facilityCode: true })

export const userCreateSchema = z.object({
  employeeNumber: z.string().regex(/^\d{6}$/, 'EmployeeNumber must be exactly 6 digits.'),
  displayName: z.string().trim().min(1).max(100),
  emailAddress: z.string().email().optional().or(z.literal('')),
  departmentCode: z.string().trim().max(50).optional().or(z.literal('')),
})

export const userEditSchema = userCreateSchema.omit({ employeeNumber: true })

export const weekRuleFormSchema = z.object({
  startDay: z.enum(['sunday', 'monday', 'tuesday', 'wednesday', 'thursday', 'friday', 'saturday']),
  startTime: z.string().regex(/^\d{2}:\d{2}:\d{2}$/),
  timeZoneId: z.string().trim().min(1),
  effectiveFromWeek: z.string().regex(/^\d{4}-W\d{2}$/),
})

export const spotFormSchema = z.object({
  name: z.string().trim().min(1).max(100),
  sortOrder: z.coerce.number().int().min(0).max(9999),
})

export const cleaningAreaFormSchema = z.object({
  facilityId: guidSchema,
  name: z.string().trim().min(1).max(100),
  initialWeekRule: weekRuleFormSchema,
  initialSpots: z.array(spotFormSchema).min(1),
})

export const planGenerateSchema = z.object({
  fairnessWindowWeeks: z.coerce.number().int().min(1).max(52),
})

export function apiEnvelopeSchema<T extends z.ZodTypeAny>(schema: T) {
  return z.object({
    data: schema,
  })
}

export function cursorPageSchema<T extends z.ZodTypeAny>(schema: T) {
  return z.object({
    data: z.array(schema),
    meta: cursorPageMetaSchema,
    links: cursorPageLinksSchema,
  })
}

export type FacilitySummary = z.infer<typeof facilitySummarySchema>
export type FacilityDetail = z.infer<typeof facilityDetailSchema>
export type UserSummary = z.infer<typeof userSummarySchema>
export type UserDetail = z.infer<typeof userDetailSchema>
export type CleaningAreaSummary = z.infer<typeof cleaningAreaSummarySchema>
export type CleaningAreaDetail = z.infer<typeof cleaningAreaDetailSchema>
export type CleaningAreaCurrentWeek = z.infer<typeof cleaningAreaCurrentWeekSchema>
export type WeeklyDutyPlanSummary = z.infer<typeof weeklyDutyPlanSummarySchema>
export type WeeklyDutyPlanDetail = z.infer<typeof weeklyDutyPlanDetailSchema>
export type WeekRule = z.infer<typeof weekRuleSchema>
