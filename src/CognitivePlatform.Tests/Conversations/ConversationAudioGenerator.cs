namespace CognitivePlatform.Tests.Conversations;

public static class ConversationAudioGenerator
{
    public static MemoryStream GenerateSyntheticWavStream(double durationSeconds = 10.0, int sampleRate = 16000)
    {
        var numSamples = (int)(sampleRate * durationSeconds);
        var bytesPerSample = 2; // 16-bit mono
        var dataChunkSize = numSamples * bytesPerSample;
        var fileSize = 36 + dataChunkSize;

        var memoryStream = new MemoryStream();
        using var writer = new BinaryWriter(memoryStream, System.Text.Encoding.UTF8, leaveOpen: true);

        // RIFF Header
        writer.Write(new[] { 'R', 'I', 'F', 'F' });
        writer.Write(fileSize);
        writer.Write(new[] { 'W', 'A', 'V', 'E' });

        // fmt Chunk
        writer.Write(new[] { 'f', 'm', 't', ' ' });
        writer.Write(16); // Subchunk1Size for PCM
        writer.Write((short)1); // AudioFormat (1 = PCM)
        writer.Write((short)1); // NumChannels (1 = Mono)
        writer.Write(sampleRate); // SampleRate
        writer.Write(sampleRate * bytesPerSample); // ByteRate
        writer.Write((short)bytesPerSample); // BlockAlign
        writer.Write((short)16); // BitsPerSample

        // data Chunk
        writer.Write(new[] { 'd', 'a', 't', 'a' });
        writer.Write(dataChunkSize);

        // Generate synthetic audio samples (alternating frequencies simulating voice tones)
        for (int i = 0; i < numSamples; i++)
        {
            var t = (double)i / sampleRate;
            // Alternate tone frequency every 2.5 seconds to simulate speaker turn
            var frequency = (t % 5.0 < 2.5) ? 440.0 : 880.0;
            var sampleValue = (short)(Math.Sin(2.0 * Math.PI * frequency * t) * 10000.0);
            writer.Write(sampleValue);
        }

        writer.Flush();
        memoryStream.Position = 0;
        return memoryStream;
    }
}
