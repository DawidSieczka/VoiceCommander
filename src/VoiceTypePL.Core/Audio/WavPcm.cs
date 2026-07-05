namespace VoiceTypePL.Core.Audio;

/// <summary>Zdekodowany plik WAV: próbki float [-1, 1] plus podstawowy format.</summary>
public sealed record WavData(float[] Samples, int SampleRate, int Channels);

/// <summary>
/// Minimalny czytnik WAV (PCM 16-bit) — na potrzeby źródła audio z pliku i testów VAD.
/// Pomija nieznane chunki (np. LIST/INFO) i miksuje stereo do mono. Świadomie nie resampluje —
/// pliki do pipeline'u powinny być 16 kHz (WASAPI resampluje osobno przez MediaFoundation).
/// </summary>
public static class WavPcm
{
    public static WavData Read(Stream stream)
    {
        using var reader = new BinaryReader(stream);

        if (new string(reader.ReadChars(4)) != "RIFF")
        {
            throw new InvalidDataException("To nie jest plik RIFF/WAV.");
        }

        reader.ReadUInt32();                                  // rozmiar pliku
        if (new string(reader.ReadChars(4)) != "WAVE")
        {
            throw new InvalidDataException("Brak nagłówka WAVE.");
        }

        int channels = 0, sampleRate = 0, bitsPerSample = 0;
        byte[]? data = null;

        while (stream.Position < stream.Length)
        {
            var chunkId = new string(reader.ReadChars(4));
            var chunkSize = reader.ReadUInt32();

            if (chunkId == "fmt ")
            {
                var audioFormat = reader.ReadUInt16();        // 1 = PCM
                channels = reader.ReadUInt16();
                sampleRate = reader.ReadInt32();
                reader.ReadInt32();                           // byteRate
                reader.ReadUInt16();                          // blockAlign
                bitsPerSample = reader.ReadUInt16();

                if (audioFormat != 1 || bitsPerSample != 16)
                {
                    throw new NotSupportedException(
                        $"Obsługiwane jest tylko PCM 16-bit (format={audioFormat}, bity={bitsPerSample}).");
                }

                var consumed = 16;
                if (chunkSize > consumed)
                {
                    reader.ReadBytes((int)chunkSize - consumed); // ewentualne pole rozszerzenia
                }
            }
            else if (chunkId == "data")
            {
                data = reader.ReadBytes((int)chunkSize);
            }
            else
            {
                reader.ReadBytes((int)chunkSize);             // pomiń nieznany chunk
                if (chunkSize % 2 == 1)
                {
                    reader.ReadByte();                        // wyrównanie do słowa
                }
            }
        }

        if (data is null || channels == 0)
        {
            throw new InvalidDataException("Plik WAV nie zawiera chunków 'fmt '/'data'.");
        }

        var totalSamples = data.Length / 2;                   // 16-bit
        var monoCount = totalSamples / channels;
        var samples = new float[monoCount];

        for (var i = 0; i < monoCount; i++)
        {
            int sum = 0;
            for (var ch = 0; ch < channels; ch++)
            {
                var idx = (i * channels + ch) * 2;
                short s = (short)(data[idx] | (data[idx + 1] << 8));
                sum += s;
            }

            samples[i] = sum / (float)channels / 32768f;      // downmix + normalizacja
        }

        return new WavData(samples, sampleRate, channels);
    }

    public static WavData Read(string path)
    {
        using var stream = File.OpenRead(path);
        return Read(stream);
    }
}
