using VoiceTypePL.Core.Audio;

namespace VoiceTypePL.Audio;

/// <summary>
/// Źródło audio odtwarzające plik WAV (16 kHz mono) jako strumień ramek — bez mikrofonu.
/// Służy do weryfikacji headless i trybu demonstracyjnego aplikacji. Ramki emitowane są
/// na wątku w tle; opcjonalnie w tempie rzeczywistym (Sleep między ramkami).
/// </summary>
public sealed class WavFileAudioCaptureSource : IAudioCaptureSource
{
    private readonly float[] _samples;
    private readonly bool _realTime;

    private Thread? _thread;
    private volatile bool _running;

    public WavFileAudioCaptureSource(string path, bool realTime = false)
    {
        var wav = WavPcm.Read(path);
        if (wav.SampleRate != AudioFormat.SampleRate)
        {
            throw new NotSupportedException(
                $"Plik musi mieć {AudioFormat.SampleRate} Hz (ma {wav.SampleRate}). Resampling robi WASAPI, nie to źródło.");
        }

        _samples = wav.Samples;
        _realTime = realTime;
    }

    public event EventHandler<AudioFrame>? FrameReady;
    public event EventHandler? CaptureStopped;

    public bool IsCapturing => _running;

    public void Start()
    {
        if (_running)
        {
            return;
        }

        _running = true;
        _thread = new Thread(Run) { IsBackground = true, Name = "WavFileAudioCapture" };
        _thread.Start();
    }

    public void Stop()
    {
        if (!_running)
        {
            return;
        }

        _running = false;
        _thread?.Join(TimeSpan.FromSeconds(1));
    }

    public void Dispose() => Stop();

    private void Run()
    {
        var frameDelay = (int)AudioFormat.FrameDuration.TotalMilliseconds;

        for (var offset = 0; offset < _samples.Length && _running; offset += AudioFormat.FrameSamples)
        {
            var frame = new float[AudioFormat.FrameSamples];
            var take = Math.Min(AudioFormat.FrameSamples, _samples.Length - offset);
            Array.Copy(_samples, offset, frame, 0, take);   // ostatnia ramka dopełniona zerami

            FrameReady?.Invoke(this, AudioFrame.FromSamples(frame));

            if (_realTime && _running)
            {
                Thread.Sleep(frameDelay);
            }
        }

        _running = false;
        CaptureStopped?.Invoke(this, EventArgs.Empty);
    }
}
