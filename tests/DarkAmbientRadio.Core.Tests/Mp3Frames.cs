using Xunit;

namespace DarkAmbientRadio.Core.Tests;

/// <summary>
/// Synthesises MP3 frames instead of encoding them: everything under test here reads frame
/// headers (or, in TagLib's case, the first one plus the file length), so a header followed by
/// the right number of filler bytes is indistinguishable from real audio — and it lets a test
/// state an exact bitrate pattern, which no real encoder can promise.
/// </summary>
internal static class Mp3Frames
{
    private static readonly int[] Mpeg1Layer3Bitrates =
        [0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320];

    /// <summary>MPEG 1 Layer III, 44.1 kHz, stereo — one frame per given bitrate.</summary>
    public static byte[] At(params int[] bitrates)
    {
        var stream = new MemoryStream();
        foreach (var kbps in bitrates)
        {
            var index = Array.IndexOf(Mpeg1Layer3Bitrates, kbps);
            Assert.True(index > 0, $"{kbps} is not an MPEG 1 Layer III bitrate");

            var length = 144 * kbps * 1000 / 44100;
            stream.WriteByte(0xFF);                       // sync
            stream.WriteByte(0xFB);                       // MPEG 1, Layer III, no CRC
            stream.WriteByte((byte)(index << 4));         // bitrate index, 44.1 kHz, no padding
            stream.WriteByte(0x00);                       // stereo
            stream.Write(new byte[length - 4]);
        }

        return stream.ToArray();
    }

    /// <summary>The same, repeated — enough audio for a reader that judges by file length.</summary>
    public static byte[] Track(int kbps, int frames)
        => At(Enumerable.Repeat(kbps, frames).ToArray());
}
