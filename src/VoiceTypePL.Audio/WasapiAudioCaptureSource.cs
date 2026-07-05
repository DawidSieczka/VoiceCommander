using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using VoiceTypePL.Core.Audio;

namespace VoiceTypePL.Audio;

/// <summary>
/// Przechwytywanie z domyślnego mikrofonu (WASAPI) i konwersja do kanonicznych 16 kHz mono
/// (§5.1): downmix kanałów + resampling (managed WDL) → ramki po 512 próbek z policzonym RMS.
/// Reaguje na zmianę domyślnego urządzenia w locie (restart przechwytywania).
/// </summary>
public sealed class WasapiAudioCaptureSource : IAudioCaptureSource, IMMNotificationClient
{
    private readonly ILogger<WasapiAudioCaptureSource>? _logger;
    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly object _lock = new();
    private readonly float[] _frame = new float[AudioFormat.FrameSamples];
    private readonly float[] _readBuffer = new float[AudioFormat.FrameSamples * 4];

    private WasapiCapture? _capture;
    private BufferedWaveProvider? _buffer;
    private ISampleProvider? _resampled;
    private int _frameFill;
    private bool _running;
    private bool _notificationsRegistered;

    public WasapiAudioCaptureSource(ILogger<WasapiAudioCaptureSource>? logger = null)
    {
        _logger = logger;
    }

    public event EventHandler<AudioFrame>? FrameReady;
    public event EventHandler? CaptureStopped;

    public bool IsCapturing
    {
        get { lock (_lock) { return _running; } }
    }

    public void Start()
    {
        lock (_lock)
        {
            if (_running)
            {
                return;
            }

            StartCaptureLocked();
            _running = true;

            if (!_notificationsRegistered)
            {
                _enumerator.RegisterEndpointNotificationCallback(this);
                _notificationsRegistered = true;
            }
        }
    }

    public void Stop()
    {
        bool raiseStopped = false;
        lock (_lock)
        {
            if (!_running)
            {
                return;
            }

            _running = false;
            StopCaptureLocked();
            raiseStopped = true;
        }

        if (raiseStopped)
        {
            CaptureStopped?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose()
    {
        Stop();
        lock (_lock)
        {
            if (_notificationsRegistered)
            {
                try { _enumerator.UnregisterEndpointNotificationCallback(this); }
                catch (Exception ex) { _logger?.LogDebug(ex, "Nie udało się wyrejestrować notyfikacji audio."); }
                _notificationsRegistered = false;
            }
        }

        _enumerator.Dispose();
    }

    private void StartCaptureLocked()
    {
        var device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
        _logger?.LogInformation("Mikrofon: {Device}", device.FriendlyName);

        var capture = new WasapiCapture(device);
        var deviceFormat = capture.WaveFormat;

        _buffer = new BufferedWaveProvider(deviceFormat)
        {
            ReadFully = false,                 // Read zwraca tylko dostępne próbki (nie dopycha ciszą)
            DiscardOnBufferOverflow = true,
            BufferDuration = TimeSpan.FromSeconds(2),
        };

        ISampleProvider samples = _buffer.ToSampleProvider();
        if (samples.WaveFormat.Channels > 1)
        {
            samples = new MonoDownmixSampleProvider(samples);
        }

        _resampled = samples.WaveFormat.SampleRate == AudioFormat.SampleRate
            ? samples
            : new WdlResamplingSampleProvider(samples, AudioFormat.SampleRate);

        capture.DataAvailable += OnDataAvailable;
        capture.RecordingStopped += OnRecordingStopped;
        capture.StartRecording();
        _capture = capture;

        _frameFill = 0;
    }

    private void StopCaptureLocked()
    {
        if (_capture is null)
        {
            return;
        }

        _capture.DataAvailable -= OnDataAvailable;
        _capture.RecordingStopped -= OnRecordingStopped;
        try { _capture.StopRecording(); }
        catch (Exception ex) { _logger?.LogDebug(ex, "Błąd przy zatrzymywaniu przechwytywania."); }
        _capture.Dispose();
        _capture = null;
        _buffer = null;
        _resampled = null;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        BufferedWaveProvider? buffer;
        ISampleProvider? resampled;
        lock (_lock)
        {
            buffer = _buffer;
            resampled = _resampled;
            if (!_running || buffer is null || resampled is null)
            {
                return;
            }
        }

        buffer.AddSamples(e.Buffer, 0, e.BytesRecorded);

        int read;
        while ((read = resampled.Read(_readBuffer, 0, _readBuffer.Length)) > 0)
        {
            for (var i = 0; i < read; i++)
            {
                _frame[_frameFill++] = _readBuffer[i];
                if (_frameFill == AudioFormat.FrameSamples)
                {
                    var frameCopy = (float[])_frame.Clone();
                    _frameFill = 0;
                    FrameReady?.Invoke(this, AudioFrame.FromSamples(frameCopy));
                }
            }
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is not null)
        {
            _logger?.LogError(e.Exception, "Przechwytywanie audio zatrzymane z błędem.");
        }
    }

    // --- IMMNotificationClient: reakcja na zmianę domyślnego mikrofonu (§5.1) ---

    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
    {
        if (flow != DataFlow.Capture || role != Role.Communications)
        {
            return;
        }

        // Restart poza wątkiem notyfikacji, żeby nie blokować COM.
        Task.Run(() =>
        {
            lock (_lock)
            {
                if (!_running)
                {
                    return;
                }

                _logger?.LogInformation("Zmiana domyślnego mikrofonu — restart przechwytywania.");
                try
                {
                    StopCaptureLocked();
                    StartCaptureLocked();
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Nie udało się przełączyć na nowy mikrofon.");
                }
            }
        });
    }

    public void OnDeviceStateChanged(string deviceId, DeviceState newState) { }
    public void OnDeviceAdded(string pwstrDeviceId) { }
    public void OnDeviceRemoved(string deviceId) { }
    public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }
}
