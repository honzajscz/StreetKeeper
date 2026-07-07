# StreetFlow — Usage guide (all scenarios)

Who does what, end to end. Roles per the PRD: **organizer**, **volunteer**,
**public/media** — plus the **ML engineer** running the model eval and the
**developer** doing an offline demo.

---

## Scenario 0 — One-time infrastructure setup (organizer, ~15 min)

1. Create a free project at [supabase.com](https://supabase.com).
2. In the Supabase **SQL Editor**, run [`supabase/schema.sql`](../supabase/schema.sql).
3. From **Project Settings → API**, note the **Project URL** and the **anon key**.
4. Deploy the two web surfaces to any static host (GitHub Pages, Netlify, …):

   ```bash
   dotnet publish src/StreetFlow.OrganizerConsole -c Release -o out/console
   dotnet publish src/StreetFlow.Dashboard        -c Release -o out/dashboard
   # deploy out/console/wwwroot and out/dashboard/wwwroot
   ```

   Before publishing, put the URL + anon key into each project's
   `wwwroot/appsettings.json`. When hosting under a sub-path, adjust
   `<base href>` in `index.html` (see [development.md](development.md)).

   For a quick local run instead: `dotnet run --project src/StreetFlow.OrganizerConsole`.

The anon key is *meant* to be public in this architecture: the database accepts
only reads and inserts (append-only RLS), and holds no personal data.

---

## Scenario 1 — Organizer creates a campaign

*Surface: Organizer Console (Czech UI).*

1. Open the console. A fresh **campaign ID** (GUID) is already generated —
   „Vygenerovat nové" makes another one.
2. Name the campaign (e.g. *„Vinohradská — pilot"*).
3. Mode **Oblast**: click the map to add the vertices of the watched-area polygon
   (≥ 3). „Zpět vrchol" undoes the last vertex.
4. Mode **Body**: click to place the boundary points — the entries/exits where
   volunteers will stand (≥ 2; the MVP is exactly one entry + one exit). Name each
   point in the table (e.g. *„sever — vjezd"*); these names appear in the
   volunteer app and on the dashboard map.
5. „Uložit kampaň do Supabase". The success panel shows the campaign ID (copy
   button) and the dashboard deep-link suffix `?campaign=<id>`.
6. Hand the campaign ID to the volunteers; publish the dashboard link once data
   starts flowing.

Campaigns are immutable through the API — a mistake means creating a new campaign
(new ID) and re-briefing the volunteers. That is deliberate pilot-grade safety.

---

## Scenario 2 — ML engineer selects the ALPR models (the eval step)

*Surface: `tools/StreetFlow.Alpr.Eval`. This is the spec's mandated first step
before trusting the pipeline in the field.*

1. Collect a labeled sample of Czech plates: street-level photos + `labels.csv`
   (`filename,plate` per line) in one directory.
2. For each candidate pair (plate-detector ONNX + OCR ONNX), write a config next to
   the model files — see
   [`candidates/example-candidate.json`](../tools/StreetFlow.Alpr.Eval/candidates/example-candidate.json).
   Typical first candidates: a YOLOv5/v8-nano license-plate detector export +
   PaddleOCR English rec (`en_PP-OCRv4_rec`) or a plate-specific CRNN, as ONNX.
3. Run:

   ```bash
   dotnet run --project tools/StreetFlow.Alpr.Eval -- \
       --candidate cand-a.json --candidate cand-b.json \
       --dataset ~/datasets/cz-plates --output eval-results.csv
   ```

4. Read the report: **exact-match rate on normalized plates** is the number that
   predicts hash-pairing quality end to end; char accuracy tells you how close the
   misses are; the CSV is your error-analysis input (light, angle, distance).
5. Ship the winner to the phones (Scenario 3, step 2). Archive the CSV with the
   decision.

---

## Scenario 3 — Volunteer collects at a boundary point

*Surface: collector app (Android). Assumes the app was installed by/with the
organizer — it is not on any store during the pilot.*

**One-time phone setup**

1. Install the APK (`dotnet build src/StreetFlow.Collector.App -f net10.0-android -t:Run`
   from a dev machine, or a distributed APK).
2. Install the chosen ALPR models into the app's data directory
   `files/models/`: `alpr.json` (same format as an eval candidate config) + the two
   `.onnx` files. For a debug build via adb:

   ```bash
   adb push alpr.json plate-detector.onnx plate-ocr.onnx /data/local/tmp/
   adb shell run-as org.streetflow.collector sh -c \
     'mkdir -p files/models && cp /data/local/tmp/alpr.json /data/local/tmp/*.onnx files/models/'
   ```

3. Connect to the shared database the same way: push a `files/supabase.json`
   containing `{"url": "https://<project>.supabase.co", "anon_key": "<anon key>"}`.
   *Without this file the app runs fully offline (mock campaign, stub sync) — fine
   for training, useless for a real campaign.*
   The Campaign tab always states which mode it is in.

**At the observation post**

1. **Campaign tab** — enter the campaign ID from the organizer, „Load campaign".
   The campaign name and its points appear.
2. „Detect my point" — GPS assigns you to the nearest boundary point within 75 m.
   Stand where the organizer told you; if you get *"No campaign point within
   range"*, you are at the wrong spot (or the point was placed wrong).
3. „Go to capture" → **Capture tab**: mount the phone (tripod/window), aim at the
   street so plates are readable, „Start collection". The app samples frames
   (~1.4 s), reads plates on-device, and shows a running count + the last
   normalized plate. The same car within 10 s is stored once.
4. Collect for the agreed window (the MVP plan: several rush hours within one
   week), then „Stop collection".

**After the session — verification (always on, spec §3.9)**

1. **Captures tab**: each row shows the plate-crop photo next to the decoded plate.
2. Wrong reading you can fix → **Correct** (type the plate as seen on the photo;
   the app re-normalizes and re-hashes it and marks the record corrected).
3. Not a real/usable capture → **Invalidate** (excluded from statistics).
4. **Sync to Supabase** — sends *only* the anonymous records (hash, color, point,
   time). Requires connectivity; retry later if it fails, records stay queued as
   „not sent".
5. Optionally **Delete all local data** — one button wipes every photo and record
   from the phone. Do this at the latest when the campaign ends; nothing needed for
   the statistics remains on the phone after a sync.

**Privacy rules the app enforces for you:** the plate photo and text never leave
the device; corrections/invalidations done *after* a record was synced are only
recorded locally in this version (the shared DB is append-only) — so verify
*before* you sync.

---

## Scenario 4 — Public / media read the result

*Surface: Public Dashboard (Czech UI).*

1. Open the shared link `…/?campaign=<id>` (or paste the campaign ID and „Načíst
   data"). Everything is computed in your browser from anonymous records.
2. The share card gives the headline: **„NN % aut naší oblastí jen projíždí"** with
   passage counts and the collection period. „Kopírovat shrnutí pro sdílení"
   puts a one-paragraph, source-attributed summary on the clipboard.
3. Stat tiles show the evidence base: total records, unique vehicle fingerprints,
   transit passages, local observations, and pairs rejected by the color/type
   safeguard (a data-quality indicator).
4. The **X slider** (1–15 min) recomputes the statistics live — it exists so the
   organizer can calibrate the transit window from real data (the median passage
   duration shown next to it is the anchor). For publication, fix one value and
   state it.
5. The map shows the watched area and the boundary points.

Reading guide: *transit* = same vehicle fingerprint seen at two different boundary
points within X minutes, confirmed by car color/type; *local* = seen but never
paired — the methodology deliberately counts unclear cases as local, so the
headline number is a floor, not a ceiling.

---

## Scenario 5 — Offline demo / training (no Supabase, no models)

Everything degrades gracefully, so you can demo the flow on a bare phone:

- Without `supabase.json`: the Campaign tab loads the **bundled demo campaign**
  (prefilled ID `8f7c3f2a-1b2c-4d5e-9f0a-112233445566`, two points on Vinohradská,
  Prague). GPS point detection only succeeds within 75 m of those coordinates —
  for a demo elsewhere, edit `Resources/Raw/campaign.mock.json` (or just show the
  "no point in range" behavior, which is also worth training).
- Without models in `files/models/`: campaign loading, GPS assignment, the captures
  list, correction, wipe and stub-sync all work; the app tells you why recognition
  is inactive and where to install models.
- The web surfaces without `appsettings.json` values render a configuration banner
  instead of crashing — usable for UI walkthroughs.

---

## Scenario 6 — The MVP micro-pilot, end to end (PRD §6)

The one-week proof run, combining everything above:

1. Organizer: Scenario 0 once, Scenario 1 for the pilot street (1 area, 2 points).
2. ML engineer: Scenario 2; install the winning models on 2 phones (Scenario 3
   setup). Do a 30-minute field test in verification mode first — the Captures tab
   doubles as the accuracy measurement (count corrected/invalidated vs. total).
3. Two volunteers: Scenario 3 during several rush hours across one week, syncing
   after each session and verifying before syncing.
4. Organizer: watch the dashboard fill in, calibrate X from the median passage
   duration, then freeze X.
5. Publish the dashboard link + share-card summary (Scenario 4). The headline
   number and the methodology paragraph are the deliverable for media.

---

## Data lifecycle & privacy summary (all scenarios)

| Data | Where it lives | Leaves the phone? | Deleted by |
| --- | --- | --- | --- |
| Camera frames | RAM only, per sample | never | immediately after processing |
| Plate crop photo + raw/normalized plate text | app sandbox (capture store) | **never** | operator „Delete all local data", any time |
| Anonymous record (salted hash, color, point, time) | phone → Supabase on sync | yes — that's its purpose | append-only by design; contains no PII to delete |
| Campaign config (polygon, points, names) | Supabase | n/a (public by design) | immutable via API |

The salt is campaign-derived, so hashes cannot be joined across campaigns; the same
physical car in two campaigns yields unrelated hashes.
