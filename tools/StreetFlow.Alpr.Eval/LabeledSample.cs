namespace StreetFlow.Alpr.Eval;

/// <summary>One labeled dataset entry: an image file and the true plate text.</summary>
public sealed record LabeledSample(string ImagePath, string Plate)
{
    /// <summary>
    /// Loads a dataset directory: images referenced from a labels.csv with lines
    /// of "filename,plate" (an optional header line is skipped).
    /// </summary>
    public static List<LabeledSample> LoadDataset(string directory)
    {
        var labelsPath = Path.Combine(directory, "labels.csv");
        if (!File.Exists(labelsPath))
            throw new FileNotFoundException($"Dataset labels file not found: {labelsPath}");

        var samples = new List<LabeledSample>();
        foreach (var line in File.ReadAllLines(labelsPath))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var separator = line.IndexOf(',');
            if (separator <= 0 || separator == line.Length - 1)
                continue;

            var fileName = line[..separator].Trim().Trim('"');
            var plate = line[(separator + 1)..].Trim().Trim('"');
            if (fileName.Equals("filename", StringComparison.OrdinalIgnoreCase))
                continue; // header

            var imagePath = Path.Combine(directory, fileName);
            if (!File.Exists(imagePath))
            {
                Console.Error.WriteLine($"  labels.csv references missing image: {fileName}");
                continue;
            }

            samples.Add(new LabeledSample(imagePath, plate));
        }

        if (samples.Count == 0)
            throw new InvalidDataException($"No usable samples found in {labelsPath}.");

        return samples;
    }
}
