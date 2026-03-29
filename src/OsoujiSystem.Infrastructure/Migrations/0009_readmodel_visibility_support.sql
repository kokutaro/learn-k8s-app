CREATE TABLE IF NOT EXISTS readmodel_visibility_checkpoints (
    projector_name TEXT PRIMARY KEY,
    last_visible_global_position BIGINT NOT NULL CHECK (last_visible_global_position >= 0),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS readmodel_cache_invalidation_tasks (
    task_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    projector_name TEXT NOT NULL,
    cache_key TEXT NOT NULL,
    operation_kind TEXT NOT NULL CHECK (operation_kind IN ('remove', 'increment_namespace')),
    reason_global_position BIGINT NOT NULL CHECK (reason_global_position >= 0),
    retry_count INT NOT NULL DEFAULT 0 CHECK (retry_count >= 0),
    next_retry_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    last_error TEXT NULL,
    resolved_at TIMESTAMPTZ NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_readmodel_cache_invalidation_tasks_key_reason
    ON readmodel_cache_invalidation_tasks (projector_name, cache_key, operation_kind, reason_global_position);

CREATE INDEX IF NOT EXISTS ix_readmodel_cache_invalidation_tasks_projector_pending
    ON readmodel_cache_invalidation_tasks (projector_name, next_retry_at)
    WHERE resolved_at IS NULL;

CREATE INDEX IF NOT EXISTS ix_readmodel_cache_invalidation_tasks_projector_reason
    ON readmodel_cache_invalidation_tasks (projector_name, reason_global_position)
    WHERE resolved_at IS NULL;

INSERT INTO readmodel_visibility_checkpoints (projector_name, last_visible_global_position, updated_at)
VALUES ('main_projector', 0, now())
ON CONFLICT (projector_name) DO NOTHING;
