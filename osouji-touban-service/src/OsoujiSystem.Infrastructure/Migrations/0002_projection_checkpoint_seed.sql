INSERT INTO projection_checkpoints (projector_name, last_global_position, updated_at)
VALUES ('main_projector', 0, now())
ON CONFLICT (projector_name) DO NOTHING;
