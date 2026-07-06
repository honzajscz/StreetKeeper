using StreetFlow.Alpr.Eval;
using StreetFlow.Collector.Alpr;
using StreetFlow.Collector.Core.Alpr;
using StreetFlow.Collector.Core.Plates;

// StreetFlow ALPR eval harness (spec §3.4): run 2–3 candidate model pairs over a
// labeled sample of Czech plates, measure accuracy, pick the winner — before any
// full integration into the app.
//
// Usage:
//   streetflow-alpr-eval --candidate cand1.json [--candidate cand2.json ...] \
//                        --dataset <dir with images + labels.csv> [--output results.csv]
//
// labels.csv format: one "filename,plate" pair per line (header optional).

var candidatePaths = new List<string>();
string? datasetPath = null;
string? outputPath = null;

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--candidate" when i + 1 < args.Length:
            candidatePaths.Add(args[++i]);
            break;
        case "--dataset" when i + 1 < args.Length:
            datasetPath = args[++i];
            break;
        case "--output" when i + 1 < args.Length:
            outputPath = args[++i];
            break;
        case "--help" or "-h":
            PrintUsage();
            return 0;
        default:
            Console.Error.WriteLine($"Unknown or incomplete argument: {args[i]}");
            PrintUsage();
            return 2;
    }
}

if (candidatePaths.Count == 0 || datasetPath is null)
{
    PrintUsage();
    return 2;
}

List<LabeledSample> samples;
try
{
    samples = LabeledSample.LoadDataset(datasetPath);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Failed to load dataset: {ex.Message}");
    return 1;
}

Console.WriteLine($"Dataset: {samples.Count} labeled plates from {datasetPath}");
Console.WriteLine();

var reports = new List<CandidateReport>();
var detailRows = new List<string> { "candidate,file,expected,expected_normalized,read,read_normalized,exact_match,char_accuracy,detected" };

foreach (var candidatePath in candidatePaths)
{
    AlprModelConfig config;
    try
    {
        config = AlprModelConfig.Load(candidatePath);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Skipping candidate '{candidatePath}': {ex.Message}");
        continue;
    }

    Console.WriteLine($"=== Candidate: {config.Name} ===");

    using var detector = new YoloPlateDetector(config.Detector);
    using var ocr = new CtcPlateOcr(config.Ocr);
    var pipeline = new AlprPipeline(detector, ocr, new AlprPipelineOptions
    {
        // Eval measures raw model quality: no OCR-confidence or plate-length gates,
        // so bad reads show up in the metrics instead of silently disappearing.
        // The app applies its own stricter gates in the field.
        MinDetectionConfidence = config.Detector.ConfidenceThreshold,
        MinOcrConfidence = 0f,
        MinPlateLength = 1,
        MaxPlateLength = 16,
    });

    var report = new CandidateReport(config.Name);

    foreach (var sample in samples)
    {
        var expectedNormalized = PlateNormalizer.Normalize(sample.Plate);
        string readText = "";
        string readNormalized = "";
        var detected = false;

        try
        {
            var candidates = pipeline.ProcessFrame(SkiaImageCodec.DecodeFile(sample.ImagePath));
            detected = candidates.Count > 0;
            if (detected)
            {
                var best = candidates.MaxBy(c => c.OcrConfidence)!;
                readText = best.RawText;
                readNormalized = best.NormalizedPlate;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  {Path.GetFileName(sample.ImagePath)}: {ex.Message}");
        }

        var exact = detected && readNormalized == expectedNormalized;
        var charAccuracy = TextMetrics.CharacterAccuracy(expectedNormalized, readNormalized);
        report.Add(detected, exact, charAccuracy);

        detailRows.Add(string.Join(',',
            Csv(config.Name), Csv(Path.GetFileName(sample.ImagePath)), Csv(sample.Plate), Csv(expectedNormalized),
            Csv(readText), Csv(readNormalized), exact ? "1" : "0",
            charAccuracy.ToString("F3", System.Globalization.CultureInfo.InvariantCulture), detected ? "1" : "0"));
    }

    report.PrintSummary();
    Console.WriteLine();
    reports.Add(report);
}

if (reports.Count == 0)
{
    Console.Error.WriteLine("No candidate could be evaluated.");
    return 1;
}

if (outputPath is not null)
{
    File.WriteAllLines(outputPath, detailRows);
    Console.WriteLine($"Per-image results written to {outputPath}");
}

var winner = reports.MaxBy(r => r.ExactMatchRate)!;
Console.WriteLine($"Best candidate by exact-match rate: {winner.Name} ({winner.ExactMatchRate:P1})");
return 0;

static string Csv(string value) =>
    value.Contains(',') || value.Contains('"')
        ? $"\"{value.Replace("\"", "\"\"")}\""
        : value;

static void PrintUsage()
{
    Console.WriteLine("""
        StreetFlow ALPR eval harness — compares candidate ONNX model pairs on labeled Czech plates.

        Usage:
          dotnet run --project tools/StreetFlow.Alpr.Eval -- \
              --candidate candidates/yolo-paddle.json \
              [--candidate candidates/other.json ...] \
              --dataset path/to/dataset \
              [--output results.csv]

        The dataset directory contains the sample images plus a labels.csv with
        one "filename,plate" pair per line. A candidate JSON describes the model
        pair — see tools/StreetFlow.Alpr.Eval/candidates/example-candidate.json.
        """);
}
