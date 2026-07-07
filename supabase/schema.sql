-- StreetFlow shared database schema (Supabase free tier).
-- PRD §2/§4: holds campaign configuration (written by the Organizer Console,
-- read by the collector app and the dashboard) and anonymized passage records
-- (written by collector apps, read by the dashboard). No personal data ever
-- lands here: plate photos and plate text never leave the volunteers' phones;
-- records carry only an irreversible salted hash.
--
-- Apply in the Supabase SQL editor (or `supabase db push`). See README.md.

-- ---------------------------------------------------------------------------
-- Campaign configuration (PRD §4 wire shape)
-- ---------------------------------------------------------------------------
create table if not exists public.campaigns (
    campaign_id  uuid primary key,
    name         text not null check (char_length(name) between 1 and 200),
    area_polygon jsonb,
    points       jsonb not null default '[]'::jsonb,
    created_at   timestamptz not null default now(),

    -- points must be an array of {point_id, name, lat, lng} objects
    constraint points_is_array check (jsonb_typeof(points) = 'array')
);

comment on table public.campaigns is
    'StreetFlow campaign configuration authored by the Organizer Console. Contains no personal data.';

-- ---------------------------------------------------------------------------
-- Anonymized passage records (PRD §4 wire shape)
-- ---------------------------------------------------------------------------
create table if not exists public.records (
    id           bigint generated always as identity primary key,
    campaign     uuid not null references public.campaigns (campaign_id),
    point_id     text not null check (char_length(point_id) between 1 and 100),
    plate_hash   text not null check (plate_hash ~ '^[0-9a-f]{64}$'),
    color        text check (color is null or char_length(color) <= 30),
    vehicle_type text check (vehicle_type is null or char_length(vehicle_type) <= 30),
    "timestamp"  timestamptz not null,
    created_at   timestamptz not null default now(),

    -- basic poisoning guard: reject records stamped in the future
    constraint timestamp_not_in_future check ("timestamp" <= now() + interval '10 minutes')
);

comment on table public.records is
    'Anonymous StreetFlow passage records: salted SHA-256 plate hash + coarse color/type + time + point. No PII.';

create index if not exists records_campaign_hash_idx
    on public.records (campaign, plate_hash, "timestamp");

-- ---------------------------------------------------------------------------
-- Row level security (PRD open question §9 — pilot-grade policy)
--
-- The anon key is shared by design (no user accounts in this architecture),
-- so the policy is: everyone may read, everyone may append, nobody may modify
-- or delete through the API. DB poisoning by someone holding the anon key is a
-- consciously accepted pilot risk (PRD §7); the checks above and the
-- append-only policy bound the damage.
-- ---------------------------------------------------------------------------
alter table public.campaigns enable row level security;
alter table public.records  enable row level security;

drop policy if exists campaigns_select on public.campaigns;
create policy campaigns_select on public.campaigns
    for select to anon, authenticated using (true);

drop policy if exists campaigns_insert on public.campaigns;
create policy campaigns_insert on public.campaigns
    for insert to anon, authenticated with check (true);

-- no update/delete policies on purpose: campaigns are immutable via the API

drop policy if exists records_select on public.records;
create policy records_select on public.records
    for select to anon, authenticated using (true);

drop policy if exists records_insert on public.records;
create policy records_insert on public.records
    for insert to anon, authenticated with check (true);

-- no update/delete policies on purpose: records are append-only via the API
