ALTER TABLE positions
    ADD COLUMN IF NOT EXISTS exit_reason integer;
