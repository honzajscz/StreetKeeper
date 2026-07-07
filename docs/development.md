# StreetFlow — Development guide

How to build, test, run and extend every part of this repository. For what the
system *is*, read the [README](../README.md) and the specs in
[`superpowers/specs/`](superpowers/specs/).

## Prerequisites

| Target | Needs |
| --- | --- |
| Core, Supabase client, ALPR lib, eval tool, tests | **.NET 10 SDK** only — any OS |
| Organizer Console, Public Dashboard (Blazor WASM) | .NET 10 SDK only (no workload needed; `wasm-tools` is optional for AOT/size optimization) |
| Collector app (`net10.0-android`) | .NET 10 SDK + `dotnet workload install maui-android` + Android SDK (easiest via Visual Studio or VS Code with the .NET MAUI extension) + a device/emulator with a camera |

## Project map and dependencies

```
StreetFlow.Collector.Core          ← pure logic, zero package dependencies
    ▲            ▲            ▲
    │            │            │
StreetFlow.  StreetFlow.  StreetFlow.Collector.Alpr   (OnnxRuntime, SkiaSharp)
Supabase     Dashboard        ▲
    ▲            │            │
    ├────────────┘        StreetFlow.Alpr.Eval (console)
    │
StreetFlow.OrganizerConsole
    │
StreetFlow.Collector.App (MAUI)  ← references Core + Alpr + Supabase
```

Rules that keep this layering healthy:

- **Core stays dependency-free.** No NuGet packages, no I/O assumptions beyond
  `System.IO` for the file store. Anything platform- or vendor-specific goes behind
  an interface defined in Core (`IPlateDetector`, `IPlateOcr`, `IJpegEncoder`,
  `IRecordSink`, `ICampaignConfigSource`, `ICaptureStore`).
- **Wire shapes live in one place.** The PRD JSON shapes are implemented exactly
  once: `CampaignConfigJson` (campaign) and `AnonymousRecord` (record). The Supabase
  client, the mock source, the console and the dashboard all reuse them. If a shape
  changes, change it there and let the compiler and the tests find the rest.
- **The privacy boundary is structural.** `AnonymousRecord` must never grow a field
  that could carry the plate photo or plate text.
  `SupabaseClientTests.SubmitRecord_posts_only_the_anonymous_wire_fields` and
  `CaptureSyncServiceTests.Sync_submits_only_the_anonymous_projection` pin this —
  if you extend the record, extend those tests deliberately.

## Everyday commands

```bash
# full test suite (Core + Supabase wire tests)
dotnet test tests/StreetFlow.Collector.Core.Tests

# a single test class / test
dotnet test tests/StreetFlow.Collector.Core.Tests --filter TransitAnalyzerTests
dotnet test tests/StreetFlow.Collector.Core.Tests --filter "FullyQualifiedName~Correction_renormalizes"

# web surfaces with live dev server (URL is printed on start)
dotnet run --project src/StreetFlow.OrganizerConsole
dotnet run --project src/StreetFlow.Dashboard

# static publish of a web surface (deployable wwwroot/)
dotnet publish src/StreetFlow.Dashboard -c Release -o out/dashboard

# ALPR eval harness
dotnet run --project tools/StreetFlow.Alpr.Eval -- --help

# Android app (requires MAUI workload + Android SDK)
dotnet build src/StreetFlow.Collector.App -f net10.0-android
dotnet build src/StreetFlow.Collector.App -f net10.0-android -t:Run   # deploy to connected device
```

`dotnet build StreetFlow.slnx` builds everything *including* the MAUI app, so on a
machine without the Android toolchain build the projects individually (as above) —
that is also what was done in the cloud environment this repo was developed in.

## Working on each area

### Core logic (TDD)

Every unit in Core was written test-first and should stay that way. The test files
mirror the source layout (`PlateNormalizerTests`, `FileCaptureStoreTests`, …).
Typical extension points:

- **New OCR confusion pair** → add to `PlateNormalizer.ConfusableMap` *plus* a row in
  `Folds_ocr_confusable_characters_to_one_canonical_form`. Remember both readings
  must land on the same canonical char, and think about whether the char occurs on
  real Czech plates.
- **Hash/salt changes** → `PlateHasher`. The salt derivation is deliberately one
  method (`DeriveCampaignSalt`); every surface derives it from the campaign id only.
  Changing it invalidates all previously collected hashes — version it if you must.
- **Capture store semantics** → `FileCaptureStore` + `CaptureRecord`. Keep the state
  machine honest: `Pending → Synced → PendingUpdate` and `Valid → Invalidated` are
  asserted across `FileCaptureStoreTests` and `CaptureSyncServiceTests`.
- **Transit rules** → `TransitAnalyzer`. It is a pure function; new pairing rules
  (e.g. per-point-pair windows, flow aggregation for the map) belong here so the
  dashboard and any offline analysis stay identical.

### ALPR (ONNX) layer

`StreetFlow.Collector.Alpr` is model-agnostic on purpose. Supported layouts:

- Detector: YOLOv5-style `[1, N, 5+nc]` and YOLOv8/11-style `[1, 4+nc, N]`
  single-class exports (auto-detected in `YoloPlateDetector.DecodeCandidates`).
- OCR: CTC models with input `[1,3,H,W]`, output `[1,T,C]` or `[1,C,T]`, blank at
  class index 0 (PaddleOCR convention), charset supplied via config.

A new model family that doesn't fit these shapes gets a new `IPlateDetector`/
`IPlateOcr` implementation rather than more branches in the existing ones.
Accuracy is *never* unit-tested (spec §6) — it is measured by the eval harness and
the in-app verification mode.

### Eval harness

`tools/StreetFlow.Alpr.Eval` — see its [README](../tools/StreetFlow.Alpr.Eval/README.md)
for candidate/dataset formats. When comparing candidates, keep the dataset fixed and
committed to your own storage (not this repo); the per-image CSV (`--output`) is the
error-analysis artifact worth archiving with the decision.

### MAUI app

Pages are plain code-behind (no MVVM framework) to keep the surface small. Session
state lives in `Services/AppState`; ALPR model loading in `Services/AlprRuntime`;
optional Supabase connectivity in `Services/SupabaseConnection`. Field-calibration
constants are deliberately single-point-of-change:

| Knob | Where |
| --- | --- |
| GPS point radius | `PointDetector.DefaultRadiusMeters` (Core) |
| Frame sampling interval | `CapturePage.SampleInterval` |
| Duplicate suppression window | `CaptureServiceOptions.DuplicateSuppressionWindow` |
| Detection/OCR confidence gates | `AlprPipelineOptions` (wired in `AlprRuntime`) |

### Web surfaces (Blazor WASM)

Both sites are static-hosting-only (PRD hard constraint). Leaflet comes from the
unpkg CDN with SRI hashes; the tiny interop layer is
`wwwroot/js/streetflow-map.js` — **kept byte-identical in both projects**; if you
change one, copy it to the other (or extract a shared static asset project if it
grows). UI language is Czech (the audiences are Czech organizers/public); code and
comments stay English.

Deploying under a sub-path (e.g. GitHub Pages `https://user.github.io/repo/`)
requires adjusting `<base href="/" />` in `wwwroot/index.html` to
`<base href="/repo/" />` at publish time.

Configuration is read from `wwwroot/appsettings.json` (`Supabase:Url`,
`Supabase:AnonKey`); both pages render a friendly banner when unconfigured, so an
empty config is a valid demo state.

### Supabase

Schema changes go into `supabase/schema.sql` (it is written to be re-runnable:
`create table if not exists`, `drop policy if exists`). Keep the API append-only
unless you also design the poisoning story; the current RLS answers the PRD's open
question conservatively. The .NET client (`StreetFlow.Supabase`) is intentionally a
thin PostgREST wrapper — resist the urge to add an ORM.

## Verification checklist before pushing

1. `dotnet test tests/StreetFlow.Collector.Core.Tests` — must be green.
2. `dotnet build` each of: Core, Alpr, Supabase, OrganizerConsole, Dashboard, Eval —
   zero warnings is the current baseline; keep it.
3. If you touched the MAUI app and have the toolchain:
   `dotnet build src/StreetFlow.Collector.App -f net10.0-android`.
4. If you touched wire shapes, schema, or RLS: re-read the privacy-boundary tests
   and `supabase/schema.sql` together — they must agree.

## Troubleshooting

| Symptom | Likely cause / fix |
| --- | --- |
| `NETSDK1147` / missing workload building the app | `dotnet workload install maui-android` |
| App builds but camera preview is black | Camera permission denied — the Capture tab re-requests on appearing; check Android settings |
| `SupabaseApiException ... 401` | Wrong anon key, or URL points at the wrong project |
| `SupabaseApiException ... 403 row-level security` | `schema.sql` not applied (policies missing), or you attempted update/delete — the API is append-only by design |
| `new row ... violates check constraint "records_plate_hash_check"` | The hash isn't 64 lowercase hex chars — something bypassed `PlateHasher` |
| Dashboard shows 0 % with data present | Window X too small for the area, or points share one `point_id`; check the slider and the campaign's point definitions |
| Leaflet map empty/gray | No network access to `tile.openstreetmap.org` / unpkg CDN from the browser |
