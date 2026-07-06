# StreetKeeper / StreetFlow

StreetFlow proves that cars mostly **drive through** a watched area (transit /
shortcut) instead of having a destination there — producing one shareable headline
number (e.g. *"68 % of cars just pass through our street"*).

This repository currently contains **iteration 1 of the collector app surface** — the
first-built and riskiest part of the system: on-device license-plate reading on a
volunteer's phone. The design documents live in
[`docs/superpowers/specs/`](docs/superpowers/specs/):

- [StreetFlow — PRD](docs/superpowers/specs/2026-06-16-streetflow-design.md)
- [Collector app (MAUI + ALPR), iteration 1](docs/superpowers/specs/2026-06-16-streetflow-collector-app-design.md)

## The on-device chain (iteration 1 scope)

```
camera → plate detection → OCR → normalization → hash (campaign salt)
       → local capture store (+ crop photo) → verification UI → (stub sync)
```

**Privacy boundary (zero-retention):** the plate crop photo and the decoded plate
text exist only in the app sandbox on the phone, kept solely so the operator can
verify OCR correctness; they can be wiped with one button. Only the anonymous record
`{campaign, point_id, plate_hash, color, vehicle_type=null, timestamp}` is ever
allowed to leave the device — and in iteration 1 even that goes to a stub, not to a
real backend. The wire type (`AnonymousRecord`) structurally has no fields that could
carry the photo or the plate text.

## Repository layout

| Project | What it is |
| --- | --- |
| `src/StreetFlow.Collector.Core` | Pure, dependency-free logic: campaign config parsing, GPS point detector, plate normalizer, salted SHA-256 plate hasher, color heuristic, ALPR pipeline orchestration, local capture store (invalidate / correct → re-hash / wipe), sync service + stub sink, and the **transit analyzer** (PRD §5 pairing + headline metric). Developed TDD-first. |
| `tests/StreetFlow.Collector.Core.Tests` | xunit suite for every unit above (97 tests). |
| `src/StreetFlow.Collector.Alpr` | ONNX Runtime implementations: YOLO-style plate detector, CTC (CRNN / PaddleOCR-rec) OCR, SkiaSharp image codec. Model-agnostic — concrete models are chosen by the eval step. |
| `tools/StreetFlow.Alpr.Eval` | Offline eval harness (**the spec's first implementation step**): compares 2–3 candidate model pairs on labeled Czech plate photos and reports detection / exact-match / char accuracy. See its [README](tools/StreetFlow.Alpr.Eval/README.md). |
| `src/StreetFlow.Collector.App` | .NET MAUI app (Android-first): campaign loading (bundled mock JSON in iteration 1), GPS → boundary-point detection, camera preview + frame sampling, always-on verification & correction UI, one-button local wipe, stub sync. |

## Building and testing

Everything except the MAUI app builds and tests on any OS with the .NET 10 SDK:

```bash
dotnet test tests/StreetFlow.Collector.Core.Tests
dotnet run --project tools/StreetFlow.Alpr.Eval -- --help
```

The MAUI app targets `net10.0-android` and additionally needs the MAUI workload and
an Android SDK (Visual Studio / VS Code with the .NET MAUI extension sets both up):

```bash
dotnet workload install maui-android
dotnet build src/StreetFlow.Collector.App -f net10.0-android
```

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
time window **X** (default 5 min, `TransitAnalysisOptions.TransitWindow`) is a
**transit passage**; a hash match is only confirmed when the vehicle fingerprint
(color / type) does not contradict it; everything that never pairs counts as
**local**, which biases the headline number downwards — the defensible direction
for a public claim. Headline metric: `TransitShare = transit / (transit + local)`.

It lives in Core deliberately: the Public Dashboard (Blazor WASM) will run this
exact code in the browser, and pilot data can be analyzed with it (including
calibration of X from the `TransitPassage.Duration` distribution) before the
dashboard surface exists.

## Provisional decisions on the spec's open questions (§8)

| Open question | Iteration-1 decision (single place to change) |
| --- | --- |
| Campaign salt form | `streetflow:{campaign_id}` — `PlateHasher.DeriveCampaignSalt` |
| GPS assignment radius | 75 m — `PointDetector.DefaultRadiusMeters` |
| Frame sampling interval | 1.4 s — `CapturePage.SampleInterval` |
| Duplicate suppression | same normalized plate within 10 s at one point — `CaptureServiceOptions` |
| Detector / OCR model choice | decided by `tools/StreetFlow.Alpr.Eval` on real Czech-plate samples |

## Out of scope for iteration 1 (per spec §7)

Real Supabase writes and update/invalidation sync, vehicle-type classification, iOS,
multi-point orchestration, Organizer Console and Public Dashboard surfaces.
