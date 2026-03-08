WITH latest_managed_user_events AS (
    SELECT DISTINCT ON (stream_id)
        stream_id,
        event_id,
        stream_version
    FROM event_store_events
    WHERE stream_type = 'managed_user'
    ORDER BY stream_id, stream_version DESC
),
managed_user_snapshots AS (
    SELECT
        s.stream_id AS user_id,
        s.last_included_version AS aggregate_version,
        s.snapshot_payload ->> 'employeeNumber' AS employee_number,
        COALESCE(s.snapshot_payload ->> 'displayName', '') AS display_name,
        s.snapshot_payload ->> 'departmentCode' AS department_code,
        s.snapshot_payload ->> 'lifecycleStatus' AS lifecycle_status,
        e.event_id AS source_event_id,
        e.stream_version AS latest_event_version
    FROM event_store_snapshots s
    INNER JOIN latest_managed_user_events e
        ON e.stream_id = s.stream_id
    WHERE s.stream_type = 'managed_user'
)
INSERT INTO projection_user_directory (
    user_id,
    employee_number,
    display_name,
    lifecycle_status,
    department_code,
    source_event_id,
    aggregate_version,
    updated_at
)
SELECT
    user_id,
    employee_number,
    display_name,
    lifecycle_status,
    department_code,
    source_event_id,
    aggregate_version,
    now()
FROM managed_user_snapshots
WHERE employee_number IS NOT NULL
  AND lifecycle_status IS NOT NULL
  AND aggregate_version = latest_event_version
ON CONFLICT (user_id)
DO UPDATE SET
    employee_number = EXCLUDED.employee_number,
    display_name = EXCLUDED.display_name,
    lifecycle_status = EXCLUDED.lifecycle_status,
    department_code = EXCLUDED.department_code,
    source_event_id = EXCLUDED.source_event_id,
    aggregate_version = EXCLUDED.aggregate_version,
    updated_at = now()
WHERE projection_user_directory.aggregate_version <= EXCLUDED.aggregate_version;
