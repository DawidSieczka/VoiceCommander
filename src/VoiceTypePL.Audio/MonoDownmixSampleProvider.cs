using NAudio.Wave;

namespace VoiceTypePL.Audio;

/// <summary>
/// Miksuje wielokanałowy sygnał do mono, uśredniając kanały. NAudio ma tylko dedykowane
/// stereo→mono; tu obsługujemy dowolną liczbę kanałów (mikrofony array itp.).
/// </summary>
internal sealed class MonoDownmixSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly int _channels;
    private float[] _buffer = Array.Empty<float>();

    public MonoDownmixSampleProvider(ISampleProvider source)
    {
        _source = source;
        _channels = source.WaveFormat.Channels;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 1);
    }

    public WaveFormat WaveFormat { get; }

    public int Read(float[] buffer, int offset, int count)
    {
        var needed = count * _channels;
        if (_buffer.Length < needed)
        {
            _buffer = new float[needed];
        }

        var read = _source.Read(_buffer, 0, needed);
        var frames = read / _channels;

        for (var i = 0; i < frames; i++)
        {
            float sum = 0;
            for (var ch = 0; ch < _channels; ch++)
            {
                sum += _buffer[i * _channels + ch];
            }

            buffer[offset + i] = sum / _channels;
        }

        return frames;
    }
}
