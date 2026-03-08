ALTER TABLE projection_user_directory
    ADD COLUMN IF NOT EXISTS display_name TEXT NOT NULL DEFAULT '';
