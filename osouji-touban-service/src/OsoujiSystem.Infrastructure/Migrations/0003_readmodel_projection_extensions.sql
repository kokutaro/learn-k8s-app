-- ReadModel projection extensions for CQRS GET endpoints.

CREATE TABLE IF NOT EXISTS projection_cleaning_area_spots (
    area_id UUID NOT NULL,
    spot_id UUID NOT NULL,
    spot_name TEXT NOT NULL,
    sort_order INT NOT NULL,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT pk_projection_cleaning_area_spots PRIMARY KEY (area_id, spot_id)
);

CREATE INDEX IF NOT EXISTS ix_projection_cleaning_area_spots_area_sort
    ON projection_cleaning_area_spots (area_id, sort_order, spot_name, spot_id);

ALTER TABLE projection_weekly_plans
    ADD COLUMN IF NOT EXISTS aggregate_version BIGINT;

UPDATE projection_weekly_plans
SET aggregate_version = snapshots.last_included_version
FROM event_store_snapshots snapshots
WHERE snapshots.stream_id = projection_weekly_plans.plan_id
  AND snapshots.stream_type = 'weekly_duty_plan'
  AND projection_weekly_plans.aggregate_version IS NULL;

UPDATE projection_weekly_plans
SET aggregate_version = revision
WHERE aggregate_version IS NULL;

ALTER TABLE projection_weekly_plans
    ALTER COLUMN aggregate_version SET NOT NULL;

ALTER TABLE projection_weekly_plans
    ADD CONSTRAINT ck_projection_weekly_plans_aggregate_version_positive
    CHECK (aggregate_version > 0);

ALTER TABLE projection_weekly_plans
    ADD COLUMN IF NOT EXISTS created_at TIMESTAMPTZ;

UPDATE projection_weekly_plans
SET created_at = first_events.first_occurred_at
FROM (
    SELECT stream_id, MIN(occurred_at) AS first_occurred_at
    FROM event_store_events
    WHERE stream_type = 'weekly_duty_plan'
    GROUP BY stream_id
) AS first_events
WHERE first_events.stream_id = projection_weekly_plans.plan_id
  AND projection_weekly_plans.created_at IS NULL;

UPDATE projection_weekly_plans
SET created_at = updated_at
WHERE created_at IS NULL;

ALTER TABLE projection_weekly_plans
    ALTER COLUMN created_at SET NOT NULL;

CREATE INDEX IF NOT EXISTS ix_projection_weekly_plans_created_at
    ON projection_weekly_plans (created_at DESC, plan_id);
