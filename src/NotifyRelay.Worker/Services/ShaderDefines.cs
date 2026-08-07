namespace NotifyRelay.Worker.Services;

public static class ShaderDefines
{
    public const int NumBinsPerHistogram = 8;
    public const int NumHistograms = 3;
    public const int NumBins = NumBinsPerHistogram * NumHistograms;
    public const int ValuesPerBin = 256 / NumBinsPerHistogram;
    public const int HistogramOffset0 = 0;
    public const int HistogramOffset1 = NumBinsPerHistogram;
    public const int HistogramOffset2 = 2 * NumBinsPerHistogram;
}
