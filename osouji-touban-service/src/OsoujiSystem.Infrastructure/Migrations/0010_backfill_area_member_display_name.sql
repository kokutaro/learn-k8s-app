CREATE INDEX IF NOT EXISTS ix_projection_user_directory_employee_number_nonblank_display_name
    ON projection_user_directory (employee_number)
    WHERE NULLIF(BTRIM(display_name), '') IS NOT NULL;

CREATE TABLE IF NOT EXISTS migration_area_member_display_name_backfill_runs (
    run_id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    target_member_count BIGINT NOT NULL,
    updated_member_count BIGINT NOT NULL,
    unresolved_member_count BIGINT NOT NULL,
    ambiguous_match_count BIGINT NOT NULL,
    missing_rate_before NUMERIC(8, 6) NOT NULL,
    missing_rate_after NUMERIC(8, 6) NOT NULL,
    executed_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

WITH normalized_user_directory AS (
    SELECT
        p.user_id,
        p.employee_number,
        NULLIF(BTRIM(p.display_name), '') AS normalized_display_name
    FROM projection_user_directory p
),
active_members AS (
    SELECT
        m.user_id,
        m.employee_number
    FROM projection_area_members m
    WHERE m.is_active = true
),
employee_directory_resolution AS (
    SELECT
        d.employee_number,
        COUNT(*) FILTER (WHERE d.normalized_display_name IS NOT NULL) AS candidate_count,
        CASE
            WHEN COUNT(*) FILTER (WHERE d.normalized_display_name IS NOT NULL) = 1
                THEN MAX(d.normalized_display_name)
            ELSE NULL
        END AS unique_display_name
    FROM normalized_user_directory d
    GROUP BY d.employee_number
),
member_backfill_candidates AS (
    SELECT
        m.user_id,
        m.employee_number,
        u.normalized_display_name AS user_id_display_name,
        COALESCE(e.candidate_count, 0) AS employee_candidate_count,
        e.unique_display_name AS employee_unique_display_name,
        COALESCE(u.normalized_display_name, e.unique_display_name) AS backfilled_display_name
    FROM active_members m
    LEFT JOIN normalized_user_directory u ON u.user_id = m.user_id
    LEFT JOIN employee_directory_resolution e ON e.employee_number = m.employee_number
),
pre_metrics AS (
    SELECT
        COUNT(*) AS target_member_count,
        COUNT(*) FILTER (WHERE c.user_id_display_name IS NULL) AS missing_before_count,
        COUNT(*) FILTER (
            WHERE c.user_id_display_name IS NULL
              AND c.employee_candidate_count > 1
        ) AS ambiguous_match_count
    FROM member_backfill_candidates c
),
upserted AS (
    INSERT INTO projection_user_directory (
        user_id,
        employee_number,
        display_name,
        lifecycle_status,
        department_code,
        source_event_id,
        aggregate_version,
        email_address,
        updated_at
    )
    SELECT
        c.user_id,
        c.employee_number,
        c.backfilled_display_name,
        'Active',
        '',
        gen_random_uuid(),
        1,
        NULL,
        now()
    FROM member_backfill_candidates c
    WHERE c.user_id_display_name IS NULL
      AND c.backfilled_display_name IS NOT NULL
    ON CONFLICT (user_id)
    DO UPDATE SET
        display_name = EXCLUDED.display_name,
        updated_at = now()
    WHERE NULLIF(BTRIM(projection_user_directory.display_name), '') IS NULL
      AND NULLIF(BTRIM(EXCLUDED.display_name), '') IS NOT NULL
    RETURNING projection_user_directory.user_id
),
final_metrics AS (
    SELECT
        pm.target_member_count,
        updates.updated_member_count,
        GREATEST(pm.missing_before_count - updates.updated_member_count, 0) AS unresolved_member_count,
        pm.ambiguous_match_count,
        CASE
            WHEN pm.target_member_count = 0 THEN 0::numeric
            ELSE pm.missing_before_count::numeric / pm.target_member_count::numeric
        END AS missing_rate_before,
        CASE
            WHEN pm.target_member_count = 0 THEN 0::numeric
            ELSE GREATEST(pm.missing_before_count - updates.updated_member_count, 0)::numeric
                 / pm.target_member_count::numeric
        END AS missing_rate_after
    FROM pre_metrics pm
    CROSS JOIN (
        SELECT COUNT(*) AS updated_member_count
        FROM upserted
    ) AS updates
)
INSERT INTO migration_area_member_display_name_backfill_runs (
    target_member_count,
    updated_member_count,
    unresolved_member_count,
    ambiguous_match_count,
    missing_rate_before,
    missing_rate_after,
    executed_at
)
SELECT
    f.target_member_count,
    f.updated_member_count,
    f.unresolved_member_count,
    f.ambiguous_match_count,
    f.missing_rate_before,
    f.missing_rate_after,
    now()
FROM final_metrics f;

DO $$
DECLARE
    metrics RECORD;
BEGIN
    SELECT
        run_id,
        target_member_count,
        updated_member_count,
        unresolved_member_count,
        ambiguous_match_count,
        missing_rate_before,
        missing_rate_after,
        executed_at
    INTO metrics
    FROM migration_area_member_display_name_backfill_runs
    ORDER BY run_id DESC
    LIMIT 1;

    RAISE NOTICE
        '0010_backfill_area_member_display_name run_id=% target=% updated=% unresolved=% ambiguous=% missing_before=% missing_after=% executed_at=%',
        metrics.run_id,
        metrics.target_member_count,
        metrics.updated_member_count,
        metrics.unresolved_member_count,
        metrics.ambiguous_match_count,
        metrics.missing_rate_before,
        metrics.missing_rate_after,
        metrics.executed_at;
END $$;
