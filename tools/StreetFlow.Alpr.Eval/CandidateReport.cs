namespace StreetFlow.Alpr.Eval;

/// <summary>Accumulates per-candidate metrics and prints the summary block.</summary>
public sealed class CandidateReport(string name)
{
    public string Name { get; } = name;

    private int _total;
    private int _detected;
    private int _exact;
    private double _charAccuracySum;

    public void Add(bool detected, bool exactMatch, double charAccuracy)
    {
        _total++;
        if (detected) _detected++;
        if (exactMatch) _exact++;
        _charAccuracySum += charAccuracy;
    }

    public double ExactMatchRate => _total == 0 ? 0 : (double)_exact / _total;
    public double DetectionRate => _total == 0 ? 0 : (double)_detected / _total;
    public double MeanCharAccuracy => _total == 0 ? 0 : _charAccuracySum / _total;

    public void PrintSummary()
    {
        Console.WriteLine($"  samples:            {_total}");
        Console.WriteLine($"  plate detected:     {_detected} ({DetectionRate:P1})");
        Console.WriteLine($"  exact match (norm): {_exact} ({ExactMatchRate:P1})");
        Console.WriteLine($"  mean char accuracy: {MeanCharAccuracy:P1}");
    }
}
