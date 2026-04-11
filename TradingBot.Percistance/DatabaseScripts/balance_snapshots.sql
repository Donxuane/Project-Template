CREATE TABLE public.balance_snapshots (
	id int8 GENERATED ALWAYS AS IDENTITY( INCREMENT BY 1 MINVALUE 1 MAXVALUE 9223372036854775807 START 1 CACHE 1 NO CYCLE) NOT NULL,
	asset text NOT NULL,
	symbol int4 NOT NULL,
	side int4 NOT NULL,
	"free" numeric(38, 18) NOT NULL,
	"locked" numeric(38, 18) NOT NULL,
	created_at timestamptz DEFAULT now() NOT NULL,
	updated_at timestamptz Null,
	CONSTRAINT balance_snapshots_pkey PRIMARY KEY (id)
);

CREATE INDEX IF NOT EXISTS ix_balance_snapshots_asset_symbol_created_at
    ON balance_snapshots (asset, symbol, created_at DESC);

CREATE UNIQUE INDEX IF NOT EXISTS uq_balance_snapshots_asset_symbol
    ON balance_snapshots (asset, symbol);

