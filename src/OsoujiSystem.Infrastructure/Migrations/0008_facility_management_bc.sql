ALTER TABLE event_store_events
    DROP CONSTRAINT IF EXISTS event_store_events_stream_type_check;

ALTER TABLE event_store_events
    ADD CONSTRAINT event_store_events_stream_type_check
        CHECK (stream_type IN ('cleaning_area', 'weekly_duty_plan', 'managed_user', 'facility'));

ALTER TABLE event_store_snapshots
    DROP CONSTRAINT IF EXISTS event_store_snapshots_stream_type_check;

ALTER TABLE event_store_snapshots
    ADD CONSTRAINT event_store_snapshots_stream_type_check
        CHECK (stream_type IN ('cleaning_area', 'weekly_duty_plan', 'managed_user', 'facility'));

CREATE TABLE IF NOT EXISTS projection_facilities (
    facility_id UUID PRIMARY KEY,
    facility_code TEXT NOT NULL,
    name TEXT NOT NULL,
    description TEXT NULL,
    time_zone_id TEXT NOT NULL,
    lifecycle_status TEXT NOT NULL,
    source_event_id UUID NULL,
    aggregate_version BIGINT NOT NULL CHECK (aggregate_version > 0),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT uq_projection_facilities_code UNIQUE (facility_code)
);

CREATE INDEX IF NOT EXISTS ix_projection_facilities_status
    ON projection_facilities (lifecycle_status, updated_at DESC);

DO $$
DECLARE
    legacy_facility_id UUID := '00000000-0000-0000-0000-000000000001';
    legacy_event_id UUID := '00000000-0000-0000-0000-000000000101';
    legacy_occurred_at TIMESTAMPTZ := TIMESTAMPTZ '2026-03-08 00:00:00+00';
BEGIN
    INSERT INTO event_store_events (
        event_id,
        stream_id,
        stream_type,
        stream_version,
        event_type,
        event_schema_version,
        payload,
        metadata,
        occurred_at
    )
    VALUES (
        legacy_event_id,
        legacy_facility_id,
        'facility',
        1,
        'FacilityRegistered',
        1,
        jsonb_build_object(
            'facilityId', legacy_facility_id::text,
            'facilityCode', 'LEGACY-DEFAULT',
            'name', 'Legacy Facility',
            'description', 'Backfilled facility for cleaning areas created before Facility BC existed.',
            'timeZoneId', 'Asia/Tokyo',
            'lifecycleStatus', 'Active'
        ),
        '{}'::jsonb,
        legacy_occurred_at
    )
    ON CONFLICT (event_id) DO NOTHING;

    INSERT INTO event_store_snapshots (
        stream_id,
        stream_type,
        last_included_version,
        snapshot_payload,
        updated_at
    )
    VALUES (
        legacy_facility_id,
        'facility',
        1,
        jsonb_build_object(
            'facilityCode', 'LEGACY-DEFAULT',
            'name', 'Legacy Facility',
            'description', 'Backfilled facility for cleaning areas created before Facility BC existed.',
            'timeZoneId', 'Asia/Tokyo',
            'lifecycleStatus', 'Active'
        ),
        now()
    )
    ON CONFLICT (stream_id) DO NOTHING;

    INSERT INTO projection_facilities (
        facility_id,
        facility_code,
        name,
        description,
        time_zone_id,
        lifecycle_status,
        source_event_id,
        aggregate_version,
        updated_at
    )
    VALUES (
        legacy_facility_id,
        'LEGACY-DEFAULT',
        'Legacy Facility',
        'Backfilled facility for cleaning areas created before Facility BC existed.',
        'Asia/Tokyo',
        'Active',
        legacy_event_id,
        1,
        now()
    )
    ON CONFLICT (facility_id) DO NOTHING;
END $$;

UPDATE event_store_snapshots
SET snapshot_payload = jsonb_set(
        snapshot_payload,
        '{facilityId}',
        to_jsonb('00000000-0000-0000-0000-000000000001'::text),
        true)
WHERE stream_type = 'cleaning_area'
  AND NOT (snapshot_payload ? 'facilityId');

ALTER TABLE projection_cleaning_areas
    ADD COLUMN IF NOT EXISTS facility_id UUID;

UPDATE projection_cleaning_areas
SET facility_id = '00000000-0000-0000-0000-000000000001'
WHERE facility_id IS NULL;

ALTER TABLE projection_cleaning_areas
    ALTER COLUMN facility_id SET NOT NULL;

CREATE INDEX IF NOT EXISTS ix_projection_cleaning_areas_facility
    ON projection_cleaning_areas (facility_id, updated_at DESC);
