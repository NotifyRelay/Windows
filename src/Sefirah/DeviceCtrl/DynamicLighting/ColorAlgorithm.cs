using System.Numerics;

namespace NotifyRelay.DeviceCtrl.DynamicLighting;

public class ColorAlgorithm
{
    private int _numPixels;
    private readonly float[] _avgBins = new float[ShaderDefines.NumHistograms];
    private readonly List<uint>[] _histograms = new List<uint>[ShaderDefines.NumHistograms];

    public ColorAlgorithm()
    {
        for (int i = 0; i < ShaderDefines.NumHistograms; i++)
        {
            _histograms[i] = new List<uint>();
        }
    }

    public void Initialize(int numPixels)
    {
        _numPixels = numPixels;
    }

    public Vector3 CalculatePredominantColor(uint[] shaderOutput)
    {
        Vector3 avgColor = CalculateAverageColor(shaderOutput);
        Vector3 topXBinsColor = CalculateColorFromTopBins();

        float alpha = 0.4f;
        Vector3 finalColor = (1.0f - alpha) * topXBinsColor + alpha * avgColor;

        return finalColor;
    }

    private void ClearStatistics()
    {
        for (int histogram = 0; histogram < ShaderDefines.NumHistograms; histogram++)
        {
            _avgBins[histogram] = 0.0f;
            _histograms[histogram].Clear();
        }
    }

    private Vector3 CalculateAverageColor(uint[] shaderOutput)
    {
        ClearStatistics();

        float[] numPixelsPerHistogram = new float[ShaderDefines.NumHistograms];

        for (int histogram = 0; histogram < ShaderDefines.NumHistograms; histogram++)
        {
            var currHistogram = _histograms[histogram];
            currHistogram.Capacity = ShaderDefines.NumBinsPerHistogram;
            for (int i = 0; i < ShaderDefines.NumBinsPerHistogram; i++)
            {
                currHistogram.Add(0);
            }

            int offset = histogram * ShaderDefines.NumBinsPerHistogram;
            for (int bin = 0; bin < ShaderDefines.NumBinsPerHistogram; bin++)
            {
                uint numPixelsInBin = shaderOutput[offset + bin];
                currHistogram[bin] = numPixelsInBin;

                _avgBins[histogram] += bin * (numPixelsInBin / (float)_numPixels);
                numPixelsPerHistogram[histogram] += numPixelsInBin;
            }
        }

        byte c1 = (byte)Math.Min(_avgBins[0] * ShaderDefines.ValuesPerBin, 255.0f);
        byte c2 = (byte)Math.Min(_avgBins[1] * ShaderDefines.ValuesPerBin, 255.0f);
        byte c3 = (byte)Math.Min(_avgBins[2] * ShaderDefines.ValuesPerBin, 255.0f);

        Vector3 avgColor = new Vector3(c1, c2, c3) / 255.0f;
        Vector3.Normalize(avgColor);

        return avgColor;
    }

    private Vector3 CalculateColorFromTopBins()
    {
        float[] sumOfTopXBins = new float[ShaderDefines.NumHistograms];

        uint pixelCoverage = 0;

        float percentPixelsRequired = 0.3f;
        float binDisplacementValue = 0.01f;

        for (int bin = ShaderDefines.NumBinsPerHistogram - 1; bin >= 0; bin--)
        {
            float threshold = percentPixelsRequired * _numPixels;
            if (pixelCoverage > threshold)
            {
                break;
            }

            for (int histogram = 0; histogram < ShaderDefines.NumHistograms; histogram++)
            {
                uint numPixelsInBin = _histograms[histogram][bin];

                sumOfTopXBins[histogram] += numPixelsInBin * (bin + binDisplacementValue);
                pixelCoverage += numPixelsInBin;
            }
        }

        Vector3 color = new Vector3(sumOfTopXBins[0], sumOfTopXBins[1], sumOfTopXBins[2]);
        Vector3.Normalize(color);

        return color;
    }
}