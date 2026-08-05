using NAudio.Wave;
using System.Drawing;

namespace SupremeStadiumSoundSelector;

/// <summary>Renders an audio waveform to a Graphics context for visualization in the trimmer.
/// Samples the audio file at a reasonable resolution and draws peaks/troughs.</summary>
internal static class WaveformRenderer
{
    /// <summary>Reads audio samples and returns normalized peak values for drawing.
    /// Returns one value per pixel-width of the target canvas.</summary>
    public static float[] GetWaveformData(string audioPath, int pixelWidth, int pixelHeight)
    {
        var peaks = new float[pixelWidth];
        if (pixelWidth <= 0) return peaks;

        using var reader = new AudioFileReader(audioPath);
        int sampleRate = reader.WaveFormat.SampleRate;
        int samplesPerPixel = Math.Max(1, (int)(reader.TotalTime.TotalSeconds * sampleRate / pixelWidth));

        int channelCount = reader.WaveFormat.Channels;
        var buffer = new float[samplesPerPixel * channelCount];

        int pixelIndex = 0;
        while (pixelIndex < pixelWidth)
        {
            int read = reader.Read(buffer, 0, buffer.Length);
            if (read == 0) break;

            float peak = 0;
            for (int i = 0; i < read; i++)
                peak = Math.Max(peak, Math.Abs(buffer[i]));

            peaks[pixelIndex] = peak;
            pixelIndex++;
        }

        return peaks;
    }

    /// <summary>Draws a waveform onto a Graphics context given normalized peak data.</summary>
    public static void DrawWaveform(Graphics g, float[] peaks, Rectangle bounds, Color waveColor, Color bgColor)
    {
        g.FillRectangle(new SolidBrush(bgColor), bounds);

        if (peaks.Length == 0) return;

        float centerY = bounds.Top + bounds.Height / 2f;
        float pixelWidth = bounds.Width / (float)peaks.Length;
        float scaleY = (bounds.Height / 2f) * 0.95f; // 95% of available space to avoid clipping

        using var pen = new Pen(waveColor, 1f);

        for (int i = 0; i < peaks.Length; i++)
        {
            float peak = peaks[i];
            float x = bounds.Left + i * pixelWidth;
            float height = Math.Max(0.5f, peak * scaleY); // at least 1 pixel

            // Draw top half
            g.DrawLine(pen, x, centerY - height, x, centerY);
            // Draw bottom half (mirrored)
            g.DrawLine(pen, x, centerY, x, centerY + height);
        }
    }
}
