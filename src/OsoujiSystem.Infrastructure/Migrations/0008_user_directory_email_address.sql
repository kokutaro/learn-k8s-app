ALTER TABLE projection_user_directory
    ADD COLUMN IF NOT EXISTS email_address TEXT NULL;

WITH latest_managed_user_events AS (
    SELECT DISTINCT ON (stream_id)
        stream_id,
        stream_version
    FROM event_store_events
    WHERE stream_type = 'managed_user'
    ORDER BY stream_id, stream_version DESC
),
managed_user_snapshots AS (
    SELECT
        s.stream_id AS user_id,
        s.last_included_version AS aggregate_version,
        s.snapshot_payload ->> 'emailAddress' AS email_address,
        e.stream_version AS latest_event_version
    FROM event_store_snapshots s
    INNER JOIN latest_managed_user_events e
        ON e.stream_id = s.stream_id
    WHERE s.stream_type = 'managed_user'
)
UPDATE projection_user_directory p
SET email_address = m.email_address
FROM managed_user_snapshots m
WHERE p.user_id = m.user_id
  AND m.aggregate_version = m.latest_event_version
  AND p.aggregate_version <= m.aggregate_version;
