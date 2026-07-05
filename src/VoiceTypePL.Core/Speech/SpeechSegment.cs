using VoiceTypePL.Core.Audio;

namespace VoiceTypePL.Core.Speech;

/// <summary>
/// Zamknięty segment mowy wykryty przez VAD (jedno „zdanie") — gotowy do transkrypcji (Etap 2).
/// PCM to 16 kHz mono, float [-1, 1], razem z paddingiem ciszy dodanym przez segmenter.
/// </summary>
public sealed class SpeechSegment
{
    public SpeechSegment(float[] pcm, DateTimeOffset capturedAt)
    {
        Pcm = pcm;
        CapturedAt = capturedAt;
    }

    /// <summary>Próbki PCM segmentu (16 kHz mono, [-1, 1]).</summary>
    public float[] Pcm { get; }

    /// <summary>Moment domknięcia segmentu.</summary>
    public DateTimeOffset CapturedAt { get; }

    /// <summary>Czas trwania segmentu wyliczony z liczby próbek.</summary>
    public TimeSpan Duration => AudioFormat.SamplesToDuration(Pcm.Length);
}
