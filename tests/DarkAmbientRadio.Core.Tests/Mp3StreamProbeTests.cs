using System.Text;
using DarkAmbientRadio.Core.Audio;
using Xunit;

namespace DarkAmbientRadio.Core.Tests;

/// <summary>
/// Frames are synthesised rather than encoded: the probe only ever looks at frame headers, so a
/// header plus the right number of filler bytes is indistinguishable from real audio to it — and
/// it lets the tests state an exact bitrate pattern, which a real encoder cannot promise.
/// </summary>
public class Mp3StreamProbeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dar-probe-" + Guid.NewGuid().ToString("N")[..8]);

    public Mp3StreamProbeTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    private static readonly int[] Mpeg1Layer3Bitrates =
        [0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320];

    /// <summary>MPEG 1 Layer III, 44.1 kHz, stereo — one frame per given bitrate.</summary>
    private static byte[] Frames(params int[] bitrates)
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

    private string Write(string name, byte[] content)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllBytes(path, content);
        return path;
    }

    private static byte[] Concat(params byte[][] parts)
    {
        var stream = new MemoryStream();
        foreach (var part in parts)
            stream.Write(part);
        return stream.ToArray();
    }

    [Fact]
    public void Constant_bitrate_is_recognised()
    {
        var path = Write("cbr192.mp3", Frames(192, 192, 192, 192, 192, 192, 192, 192));

        var info = Mp3StreamProbe.Probe(path);

        Assert.NotNull(info);
        Assert.True(info!.Value.IsConstant);
        Assert.Equal(192, info.Value.Bitrate);
        Assert.Equal(8, info.Value.FrameCount);
    }

    // The whole reason this probe exists instead of a simple average: these frames average
    // exactly 192 kbit/s and even produce the same frame size as 192 CBR, but the file is VBR.
    [Fact]
    public void Vbr_that_averages_exactly_the_target_is_not_mistaken_for_cbr()
    {
        var path = Write("vbr192.mp3", Frames(128, 256, 128, 256, 128, 256, 128, 256));

        var info = Mp3StreamProbe.Probe(path);

        Assert.NotNull(info);
        Assert.False(info!.Value.IsConstant);
    }

    [Fact]
    public void A_single_deviating_frame_is_enough_to_disqualify_a_file()
    {
        var path = Write("nearly.mp3", Frames(192, 192, 192, 192, 192, 192, 160, 192));

        Assert.False(Mp3StreamProbe.Probe(path)!.Value.IsConstant);
    }

    [Fact]
    public void An_id3v2_tag_in_front_of_the_audio_is_skipped()
    {
        // "ID3", version, flags, syncsafe size (200 bytes) — then 200 bytes of tag payload.
        var header = new byte[] { 0x49, 0x44, 0x33, 0x03, 0x00, 0x00, 0x00, 0x00, 0x01, 0x48 };
        var path = Write("tagged.mp3", Concat(header, new byte[200], Frames(192, 192, 192, 192)));

        var info = Mp3StreamProbe.Probe(path);

        Assert.True(info!.Value.IsConstant);
        Assert.Equal(192, info.Value.Bitrate);
    }

    [Fact]
    public void A_xing_header_frame_does_not_count_against_the_bitrate()
    {
        // Encoders write the Xing/Info frame at their own bitrate; counting it would make every
        // tagged CBR file look variable.
        var xingFrame = Frames(32);
        Encoding.ASCII.GetBytes("Xing").CopyTo(xingFrame, 4 + 32);   // behind the side information

        var path = Write("xing.mp3", Concat(xingFrame, Frames(192, 192, 192, 192, 192)));

        var info = Mp3StreamProbe.Probe(path);

        Assert.True(info!.Value.IsConstant);
        Assert.Equal(192, info.Value.Bitrate);
        Assert.Equal(5, info.Value.FrameCount);   // the Xing frame is not among them
    }

    [Fact]
    public void Trailing_tags_end_the_scan_without_spoiling_the_result()
    {
        // What mp3gain leaves behind: an APEv2 tag, plus an ID3v1 block.
        var ape = Encoding.ASCII.GetBytes("APETAGEX");
        var id3v1 = Encoding.ASCII.GetBytes("TAG");
        var path = Write("tailed.mp3", Concat(Frames(192, 192, 192, 192), ape, new byte[100], id3v1, new byte[125]));

        var info = Mp3StreamProbe.Probe(path);

        Assert.True(info!.Value.IsConstant);
        Assert.Equal(192, info.Value.Bitrate);
    }

    [Fact]
    public void Files_that_are_not_mpeg_audio_yield_no_answer()
    {
        Assert.Null(Mp3StreamProbe.Probe(Write("text.mp3", Encoding.ASCII.GetBytes(new string('x', 5000)))));
        Assert.Null(Mp3StreamProbe.Probe(Path.Combine(_root, "missing.mp3")));
    }

    [Fact]
    public void A_file_with_almost_no_frames_yields_no_answer()
        => Assert.Null(Mp3StreamProbe.Probe(Write("stub.mp3", Frames(192, 192))));

    [Fact]
    public void An_album_counts_as_constant_only_when_every_track_agrees()
    {
        var album = Directory.CreateDirectory(Path.Combine(_root, "album")).FullName;
        File.WriteAllBytes(Path.Combine(album, "01.mp3"), Frames(192, 192, 192, 192));
        File.WriteAllBytes(Path.Combine(album, "02.mp3"), Frames(192, 192, 192, 192));

        var info = Mp3StreamProbe.ProbeAlbum(album);

        Assert.True(info.AllConstant);
        Assert.True(info.IsConstantAt(192));
        Assert.False(info.IsConstantAt(320));
        Assert.Equal(2, info.TrackCount);

        // One VBR track is enough to send the whole album through the encoder.
        File.WriteAllBytes(Path.Combine(album, "03.mp3"), Frames(128, 256, 128, 256));
        Assert.False(Mp3StreamProbe.ProbeAlbum(album).IsConstantAt(192));
    }

    [Fact]
    public void An_album_of_mixed_constant_bitrates_is_not_constant()
    {
        var album = Directory.CreateDirectory(Path.Combine(_root, "mixed")).FullName;
        File.WriteAllBytes(Path.Combine(album, "01.mp3"), Frames(192, 192, 192, 192));
        File.WriteAllBytes(Path.Combine(album, "02.mp3"), Frames(320, 320, 320, 320));

        Assert.False(Mp3StreamProbe.ProbeAlbum(album).IsConstantAt(192));
    }

    [Fact]
    public void An_unreadable_track_keeps_the_album_from_counting_as_constant()
    {
        var album = Directory.CreateDirectory(Path.Combine(_root, "broken")).FullName;
        File.WriteAllBytes(Path.Combine(album, "01.mp3"), Frames(192, 192, 192, 192));
        File.WriteAllText(Path.Combine(album, "02.mp3"), "not audio");

        Assert.False(Mp3StreamProbe.ProbeAlbum(album).IsConstantAt(192));
    }
}
