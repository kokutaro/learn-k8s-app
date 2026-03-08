ALTER TABLE event_store_events
    DROP CONSTRAINT IF EXISTS event_store_events_stream_type_check;

ALTER TABLE event_store_events
    ADD CONSTRAINT event_store_events_stream_type_check
        CHECK (stream_type IN ('cleaning_area', 'weekly_duty_plan', 'managed_user'));

ALTER TABLE event_store_snapshots
    DROP CONSTRAINT IF EXISTS event_store_snapshots_stream_type_check;

ALTER TABLE event_store_snapshots
    ADD CONSTRAINT event_store_snapshots_stream_type_check
        CHECK (stream_type IN ('cleaning_area', 'weekly_duty_plan', 'managed_user'));

CREATE TABLE IF NOT EXISTS projection_user_directory (
    user_id UUID PRIMARY KEY,
    employee_number CHAR(6) NOT NULL,
    lifecycle_status TEXT NOT NULL,
    department_code TEXT NULL,
    source_event_id UUID NOT NULL,
    aggregate_version BIGINT NOT NULL CHECK (aggregate_version > 0),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT ck_projection_user_directory_employee_number_format
        CHECK (employee_number ~ '^[0-9]{6}$')
);

CREATE INDEX IF NOT EXISTS ix_projection_user_directory_status
    ON projection_user_directory (lifecycle_status, updated_at DESC);
