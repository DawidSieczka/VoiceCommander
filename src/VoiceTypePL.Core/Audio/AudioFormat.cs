namespace VoiceTypePL.Core.Audio;

/// <summary>
/// Kanoniczny format audio w całym pipelinie: 16 kHz, mono, PCM.
/// Ramka to 512 próbek (~32 ms) — dokładnie okno analizy wymagane przez Silero VAD v5
/// przy 16 kHz, dzięki czemu nie trzeba re-buforować między przechwytywaniem a VAD.
/// </summary>
public static class AudioFormat
{
    /// <summary>Częstotliwość próbkowania [Hz] wymagana przez Whispera i Silero VAD.</summary>
    public const int SampleRate = 16_000;

    /// <summary>Liczba kanałów (mono).</summary>
    public const int Channels = 1;

    /// <summary>Liczba próbek w jednej ramce (okno Silero v5 @16 kHz).</summary>
    public const int FrameSamples = 512;

    /// <summary>Czas trwania jednej ramki.</summary>
    public static TimeSpan FrameDuration { get; } =
        TimeSpan.FromSeconds((double)FrameSamples / SampleRate);

    /// <summary>Zamienia liczbę próbek na czas trwania przy 16 kHz.</summary>
    public static TimeSpan SamplesToDuration(int sampleCount) =>
        TimeSpan.FromSeconds((double)sampleCount / SampleRate);

    /// <summary>Zamienia czas na liczbę próbek przy 16 kHz (zaokrąglenie w górę).</summary>
    public static int DurationToSamples(TimeSpan duration) =>
        (int)Math.Ceiling(duration.TotalSeconds * SampleRate);
}
