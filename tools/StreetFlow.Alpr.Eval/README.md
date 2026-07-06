# StreetFlow ALPR eval harness

The **first implementation step** of the collector surface (design doc §3.4): before the
ALPR pipeline is fully integrated into the app, 2–3 concrete candidate model pairs are
evaluated on a sample of **Czech license plates**, their accuracy is measured, and the
winner is selected.

## What it does

For every candidate (a detector + OCR ONNX pair described by a JSON config), the harness
runs the exact same pipeline the app uses (`AlprPipeline` from
`StreetFlow.Collector.Core`) over a labeled image set and reports:

- **detection rate** — share of images where a plate was found at all,
- **exact-match rate** — share of images where the *normalized* reading equals the
  *normalized* label (this is what determines hash-match quality end to end),
- **mean character accuracy** — 1 − normalized Levenshtein distance, to see how close
  the misses are.

## Preparing candidates

Candidate models are not committed to the repo (see `.gitignore`); drop the `.onnx`
files next to a config file:

```jsonc
{
  "name": "yolo-plate + paddle-rec",
  "detector": {
    "model_path": "../models/plate-detector.onnx", // YOLOv5/v8-style single-class export
    "input_size": 640,
    "confidence_threshold": 0.35,
    "nms_iou_threshold": 0.45
  },
  "ocr": {
    "model_path": "../models/plate-ocr.onnx",      // CRNN / PaddleOCR-rec style CTC model
    "input_width": 320,
    "input_height": 48,
    "charset": "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ" // class order WITHOUT the CTC blank (blank = index 0)
  }
}
```

Suitable public candidates to try first: a YOLOv5/YOLOv8-nano license-plate detector
export, and PaddleOCR's English recognition model (`en_PP-OCRv4_rec`) or a
plate-specific CRNN, all converted to ONNX.

## Preparing the dataset

A directory of street-level photos of Czech plates plus a `labels.csv`:

```csv
filename,plate
img001.jpg,3A2 3456
img002.jpg,5B7 2233
```

Labels are compared **after normalization** (uppercase, separators stripped, `O→0`,
`I→1`, …) — the same canonicalization the app applies before hashing.

## Running

```bash
dotnet run --project tools/StreetFlow.Alpr.Eval -- \
    --candidate tools/StreetFlow.Alpr.Eval/candidates/yolo-paddle.json \
    --candidate tools/StreetFlow.Alpr.Eval/candidates/other-pair.json \
    --dataset ~/datasets/cz-plates \
    --output eval-results.csv
```

The per-image CSV (`--output`) is the input for error analysis: which plates fail, in
which light, at which angles — feeding the field-test verification step (§6).
