# StreetFlow — Supabase setup

The shared database (PRD §2, surface 3) is a plain Supabase free-tier project: hosted
Postgres + auto-generated REST API. It is *not* our server — Backend-as-a-Service.

## One-time setup

1. Create a project at [supabase.com](https://supabase.com) (free tier).
2. Open **SQL Editor** and run [`schema.sql`](schema.sql).
3. From **Project Settings → API** grab:
   - the **Project URL** (e.g. `https://abcdefgh.supabase.co`),
   - the **anon (public) key**.
4. Put both into the configuration of each surface:
   - Organizer Console: `src/StreetFlow.OrganizerConsole/wwwroot/appsettings.json`
   - Public Dashboard: `src/StreetFlow.Dashboard/wwwroot/appsettings.json`
   - Collector app: `{app data}/supabase.json` on the phone —
     `{ "url": "https://…supabase.co", "anon_key": "…" }`
     (without this file the app keeps using the bundled mock campaign + stub sync).

## Access model (pilot-grade, PRD §7/§9)

The anon key is shared by design — this architecture has no user accounts. RLS makes
both tables **read-anything / append-only**: nobody can update or delete through the
API, campaigns and records are immutable once written. CHECK constraints reject
malformed hashes (`plate_hash` must be 64 lowercase hex chars) and future-stamped
records. Deliberate poisoning by someone holding the anon key remains an accepted
pilot risk, as stated in the PRD.

Consequence for the collector app: locally invalidated/corrected records that were
already sent cannot be amended remotely yet (`SyncState.PendingUpdate` stays local).
Doing this properly needs a client-generated record key plus an update policy —
a follow-up surface once the pilot shows it matters.
