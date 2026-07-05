namespace VoiceTypePL.Core.Audio;

/// <summary>
/// Pojedyncza ramka audio płynąca z przechwytywania do VAD: 16 kHz mono, próbki jako float [-1, 1].
/// Zawiera policzony poziom sygnału (RMS) — używany później do wskaźnika w UI (§5.1).
/// </summary>
/// <param name="Samples">Próbki PCM w zakresie [-1, 1]; zwykle <see cref="AudioFormat.FrameSamples"/> sztuk.</param>
/// <param name="Rms">Pierwiastek średniej kwadratów próbek (0 = cisza, ~1 = maksimum).</param>
public readonly record struct AudioFrame(float[] Samples, float Rms)
{
    /// <summary>Liczy RMS ramki i tworzy <see cref="AudioFrame"/>.</summary>
    public static AudioFrame FromSamples(float[] samples)
    {
        double sumSquares = 0;
        foreach (var s in samples)
        {
            sumSquares += (double)s * s;
        }

        var rms = samples.Length == 0 ? 0f : (float)Math.Sqrt(sumSquares / samples.Length);
        return new AudioFrame(samples, rms);
    }
}
