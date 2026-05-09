ALTER TABLE public.positions
    ADD COLUMN IF NOT EXISTS is_closing bool NOT NULL DEFAULT false;
