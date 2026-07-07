# StreetKeeper / StreetFlow

StreetFlow proves that cars mostly **drive through** a watched area (transit /
shortcut) instead of having a destination there — producing one shareable headline
number (e.g. *"68 % of cars just pass through our street"*).

Documentation:

- [Usage guide — all roles and scenarios](docs/usage.md) (organizer, ML eval,
  volunteer, public/media, offline demo, the full MVP micro-pilot)
- [Development guide](docs/development.md) (build/test matrix, project layering,
  extension points, verification checklist, troubleshooting)
- Design docs in [`docs/superpowers/specs/`](docs/superpowers/specs/):
  [StreetFlow — PRD](docs/superpowers/specs/2026-06-16-streetflow-design.md) ·
  [Collector app (MAUI + ALPR), iteration 1](docs/superpowers/specs/2026-06-16-streetflow-collector-app-design.md)

All four PRD surfaces are in this repo — everything runs on clients, there is no
custom backend (Supabase free tier is the shared database):

```
[Organizer Console] --> [Supabase] : campaign config (campaign_id, polygon, points)
     (Blazor WASM)          |
                            | campaign config
                            v
[MAUI app @ point A]  --\
[MAUI app @ point B]  ---> [Supabase: anonymous records] <--- [Public Dashboard]
  (OCR on-device,          (hash + color/type +               (pairing + statistics
   hash, zero-retention)    time + point + campaign)           in the browser, share card)
```

## The on-device chain (collector, iteration 1 scope)

```
camera → plate detection → OCR → normalization → hash (campaign salt)
       → local capture store (+ crop photo) → verification UI → sync
```

**Privacy boundary (zero-retention):** the plate crop photo and the decoded plate
text exist only in the app sandbox on the phone, kept solely so the operator can
verify OCR correctness; they can be wiped with one button. Only the anonymous record
`{campaign, point_id, plate_hash, color, vehicle_type, timestamp}` ever leaves the
device. The wire type (`AnonymousRecord`) structurally has no fields that could carry
the photo or the plate text, and a test pins that down.

## Repository layout

| Project | What it is |
| --- | --- |
| `src/StreetFlow.Collector.Core` | Pure, dependency-free logic: campaign config parsing, GPS point detector, plate normalizer, salted SHA-256 plate hasher, color heuristic, ALPR pipeline orchestration, local capture store (invalidate / correct → re-hash / wipe), sync service, and the **transit analyzer** (PRD §5 pairing + headline metric). Developed TDD-first. |
| `tests/StreetFlow.Collector.Core.Tests` | xunit suite for every unit above plus the Supabase wire protocol (104 tests). |
| `src/StreetFlow.Collector.Alpr` | ONNX Runtime implementations: YOLO-style plate detector, CTC (CRNN / PaddleOCR-rec) OCR, SkiaSharp image codec. Model-agnostic — concrete models are chosen by the eval step. |
| `tools/StreetFlow.Alpr.Eval` | Offline eval harness (**the collector spec's first implementation step**): compares candidate model pairs on labeled Czech plate photos and reports detection / exact-match / char accuracy. See its [README](tools/StreetFlow.Alpr.Eval/README.md). |
| `src/StreetFlow.Collector.App` | .NET MAUI app (Android-first): campaign loading, GPS → boundary-point assignment, camera preview + frame sampling, always-on verification & correction UI, local wipe, sync. |
| `src/StreetFlow.Supabase` | Thin PostgREST client for the shared database: campaign store, record sink/reader. Shared by the app and both web surfaces. |
| `src/StreetFlow.OrganizerConsole` | **Organizer Console** (Blazor WASM, static hosting, Czech UI): generate `campaign_id` client-side, draw the area polygon and place boundary points on a Leaflet/OSM map, save the campaign to Supabase. |
| `src/StreetFlow.Dashboard` | **Public Dashboard** (Blazor WASM, static hosting, Czech UI): loads anonymous records, runs `TransitAnalyzer` in the browser, shows the headline number, a share card, per-window calibration slider and the area map. Deep-linkable via `?campaign=<id>`. |
| `supabase/` | Database schema (campaigns + records, append-only RLS, sanity checks) and setup guide. |

## Building and testing

Everything except the MAUI app builds and tests on any OS with the .NET 10 SDK:

```bash
dotnet test tests/StreetFlow.Collector.Core.Tests

# web surfaces (local dev server)
dotnet run --project src/StreetFlow.OrganizerConsole
dotnet run --project src/StreetFlow.Dashboard

# static publish (deploy wwwroot/ to GitHub Pages or any static host)
dotnet publish src/StreetFlow.Dashboard -c Release -o out/dashboard
```

The MAUI app targets `net10.0-android` and additionally needs the MAUI workload and
an Android SDK (Visual Studio / VS Code with the .NET MAUI extension sets both up):

```bash
dotnet workload install maui-android
dotnet build src/StreetFlow.Collector.App -f net10.0-android
```

## Wiring the surfaces together

1. **Supabase**: create a free project and run [`supabase/schema.sql`](supabase/schema.sql)
   — see [`supabase/README.md`](supabase/README.md).
2. **Web surfaces**: put the project URL + anon key into
   `wwwroot/appsettings.json` of the console and the dashboard.
3. **Collector app**: put the same into `{app data}/supabase.json` on the phone
   (`{"url": "https://…supabase.co", "anon_key": "…"}`). Without the file the app
   runs fully offline against the bundled mock campaign with a stub sync.
4. **Organizer** creates the campaign in the console and hands the `campaign_id` to
   volunteers; the **dashboard** is shared as `…/?campaign=<id>`.

## ALPR models

Model files are **not** committed. Run the eval harness on a labeled sample of Czech
plates to pick the winning candidate pair, then install it on the phone at
`{app data}/models/`: an `alpr.json` (same format as the eval candidate config) plus
the two `.onnx` files it references. Without models the app still runs — campaign
loading, GPS point detection, the capture store and the verification UI all work; the
capture screen tells you why recognition is inactive.

## Transit analysis (PRD §5)

`TransitAnalyzer` implements the proof logic as a pure function over anonymous
observations: the same `plate_hash` at **two different boundary points** within the
time window **X** (default 5 min) is a **transit passage**; a hash match is only
confirmed when the vehicle fingerprint (color / type) does not contradict it;
everything that never pairs counts as **local**, which biases the headline number
downwards — the defensible direction for a public claim. Headline metric:
`TransitShare = transit / (transit + local)`. The dashboard runs this exact code in
the browser and exposes the window X as a live slider (with the median passage
duration as a calibration aid).

## Provisional decisions on the spec's open questions

| Open question | Current decision (single place to change) |
| --- | --- |
| Campaign salt form | `streetflow:{campaign_id}` — `PlateHasher.DeriveCampaignSalt` |
| GPS assignment radius | 75 m — `PointDetector.DefaultRadiusMeters` |
| Frame sampling interval | 1.4 s — `CapturePage.SampleInterval` |
| Duplicate suppression | same normalized plate within 10 s at one point — `CaptureServiceOptions` |
| Transit window X | 5 min default + live dashboard slider — `TransitAnalysisOptions` |
| Detector / OCR model choice | decided by `tools/StreetFlow.Alpr.Eval` on real Czech-plate samples |
| Supabase protection (RLS) | append-only anon policy + format checks — `supabase/schema.sql` |

## Known limitations / next steps

- Records corrected or invalidated **after** they were synced are amended only
  locally (`SyncState.PendingUpdate`); the append-only RLS has no update path yet.
  Needs a client-generated record key + update policy in a follow-up.
- Vehicle-type classification (`vehicle_type` stays `null`), iOS, and the flow-map
  visualization (arrows entry→exit) remain out of scope, per the specs.
- The MAUI app head is authored but requires a machine with the MAUI workload +
  Android SDK to build (not available in the environment this repo was developed in).
