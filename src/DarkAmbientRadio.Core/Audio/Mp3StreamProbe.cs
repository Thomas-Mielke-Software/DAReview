using DarkAmbientRadio.Core.Files;

namespace DarkAmbientRadio.Core.Audio;

/// <summary>What the MPEG frame headers of one MP3 say about its bitrate.</summary>
/// <param name="Bitrate">kbit/s; only meaningful when <paramref name="IsConstant"/> is true.</param>
/// <param name="IsConstant">True only when every single frame carries the same bitrate.</param>
/// <param name="FrameCount">Frames that took part in the decision.</param>
public readonly record struct Mp3StreamInfo(int Bitrate, bool IsConstant, int FrameCount);

/// <summary>One track's bitrate, as found by <see cref="Mp3StreamProbe.FindTracksBelow"/>.</summary>
/// <param name="FileName">The file name, without the folder.</param>
/// <param name="Kbps">Bitrate in kbit/s.</param>
public readonly record struct TrackBitrate(string FileName, int Kbps);

/// <summary>What probing every MP3 of an album folder found.</summary>
public readonly record struct AlbumStreamInfo(int TrackCount, int Bitrate, bool AllConstant)
{
    /// <summary>True when every track is CBR at exactly <paramref name="kbps"/>.</summary>
    public bool IsConstantAt(int kbps) => TrackCount > 0 && AllConstant && Bitrate == kbps;
}

/// <summary>
/// Reads MP3 bitrates straight from the MPEG frame headers.
/// <para>
/// The point is telling CBR apart from VBR, which an <em>average</em> bitrate cannot do: a VBR
/// file can average exactly 192 kbit/s and still be nothing like a 192 CBR file. So every frame
/// header is walked and the bitrate field compared — CBR means all of them agree, and a single
/// deviating frame ends the scan with a "no".
/// </para>
/// </summary>
public static class Mp3StreamProbe
{
    // Layer III bitrate tables in kbit/s; index 0 (free) and 15 (bad) are rejected.
    private static readonly int[] Mpeg1Layer3 =
        [0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 0];

    private static readonly int[] Mpeg2Layer3 =
        [0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160, 0];

    // Indexed by the header's version bits: 0 = MPEG 2.5, 1 = reserved, 2 = MPEG 2, 3 = MPEG 1.
    private static readonly int[][] SampleRates =
        [[11025, 12000, 8000], [], [22050, 24000, 16000], [44100, 48000, 32000]];

    /// <summary>How far into the file the first frame may start (ID3v2 is skipped before this).</summary>
    private const int SyncWindow = 128 * 1024;

    /// <summary>Below this a file is not worth judging — the answer would be noise.</summary>
    private const int MinimumFrames = 3;

    /// <summary>How many frames have to follow each other before a sync position is believed.</summary>
    private const int ChainedFrames = 3;

    /// <summary>
    /// Probes one file, or returns <c>null</c> when it is unreadable or not a Layer III stream.
    /// A null answer means "don't know" and callers should treat it as "not what we want".
    /// </summary>
    public static Mp3StreamInfo? Probe(string filePath)
    {
        // Reading a cloud placeholder is an app-triggered download, and a few of those get the app
        // blocked by Windows. "Don't know" costs a re-encode at worst; the block costs everything.
        if (CloudFiles.IsPlaceholder(filePath))
            return null;

        try
        {
            // Denies nothing: the file may be open elsewhere (player, Nextcloud prefetch).
            using var stream = new FileStream(
                filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 65536, FileOptions.SequentialScan);

            if (!TrySync(stream, SkipId3v2(stream), out var position))
                return null;

            stream.Position = position;

            var header = new byte[4];
            var bitrate = 0;
            var counted = 0;
            var frameIndex = 0;

            while (stream.Read(header, 0, 4) == 4)
            {
                if (!TryParseHeader(header, out var frame))
                    break;   // trailing ID3v1 / the APE tag mp3gain appends / padding: done

                // The Xing/Info/VBRI frame is a header, not audio, and encoders routinely write
                // it at another bitrate — counting it would call every tagged file VBR.
                if (frameIndex == 0 && TryReadVbrTag(stream, frame, out _))
                {
                    frameIndex++;
                    stream.Seek(frame.Length - 4, SeekOrigin.Current);
                    continue;
                }

                if (counted == 0)
                    bitrate = frame.Bitrate;
                else if (frame.Bitrate != bitrate)
                    return new Mp3StreamInfo(0, IsConstant: false, counted);

                counted++;
                frameIndex++;
                stream.Seek(frame.Length - 4, SeekOrigin.Current);
            }

            return counted < MinimumFrames ? null : new Mp3StreamInfo(bitrate, IsConstant: true, counted);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Probes every MP3 directly inside <paramref name="folder"/>. Anything unreadable or VBR
    /// makes the whole album "not constant" — this drives a decision to skip re-encoding, so
    /// it must only say yes when it is sure about every track.
    /// </summary>
    public static AlbumStreamInfo ProbeAlbum(string folder)
    {
        if (!Directory.Exists(folder))
            return default;

        var files = Directory.GetFiles(folder, "*.mp3");
        if (files.Length == 0)
            return default;

        var bitrate = 0;
        foreach (var file in files)
        {
            var info = Probe(file);
            if (info is not { IsConstant: true })
                return new AlbumStreamInfo(files.Length, 0, AllConstant: false);

            if (bitrate == 0)
                bitrate = info.Value.Bitrate;
            else if (info.Value.Bitrate != bitrate)
                return new AlbumStreamInfo(files.Length, 0, AllConstant: false);
        }

        return new AlbumStreamInfo(files.Length, bitrate, AllConstant: true);
    }

    /// <summary>
    /// The average bitrate in kbit/s, or <c>null</c> when the file cannot be judged — a quality
    /// warning, not a decision, so this deliberately does <em>not</em> walk the file the way
    /// <see cref="Probe"/> does. It reads the first real frame and, when the encoder left a
    /// Xing/VBRI frame count, derives the average from the playing time that implies; otherwise
    /// the first frame's bitrate is the answer, which is exact for CBR material.
    /// </summary>
    public static int? ProbeAverageBitrate(string filePath)
    {
        // Reading a cloud placeholder is an app-triggered download; see Probe.
        if (CloudFiles.IsPlaceholder(filePath))
            return null;

        try
        {
            using var stream = new FileStream(
                filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 65536, FileOptions.SequentialScan);

            if (!TrySync(stream, SkipId3v2(stream), out var audioStart))
                return null;

            stream.Position = audioStart;
            var header = new byte[4];
            if (stream.Read(header, 0, 4) != 4 || !TryParseHeader(header, out var frame))
                return null;

            if (!TryReadVbrTag(stream, frame, out var frameCount) || frameCount <= 0)
                return frame.Bitrate;

            var seconds = (double)frameCount * frame.SamplesPerFrame / frame.SampleRate;
            if (seconds <= 0)
                return frame.Bitrate;

            return (int)Math.Round((stream.Length - audioStart) * 8 / seconds / 1000);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// The MP3s directly inside <paramref name="folder"/> whose average bitrate is below
    /// <paramref name="kbps"/>, in album order. A track that cannot be judged is left out:
    /// "don't know" is not "too low".
    /// </summary>
    public static IReadOnlyList<TrackBitrate> FindTracksBelow(string folder, int kbps)
    {
        if (!Directory.Exists(folder))
            return [];

        return Directory.GetFiles(folder, "*.mp3")
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .Select(file => new TrackBitrate(Path.GetFileName(file), ProbeAverageBitrate(file) ?? 0))
            .Where(track => track.Kbps > 0 && track.Kbps < kbps)
            .ToList();
    }

    private readonly record struct FrameHeader(int Bitrate, int Length, int SampleRate, bool IsMpeg1, bool IsMono)
    {
        /// <summary>Layer III carries 1152 samples per frame on MPEG 1 and 576 on MPEG 2/2.5.</summary>
        public int SamplesPerFrame => IsMpeg1 ? 1152 : 576;
    }

    private static bool TryParseHeader(ReadOnlySpan<byte> h, out FrameHeader frame)
    {
        frame = default;

        if (h.Length < 4 || h[0] != 0xFF || (h[1] & 0xE0) != 0xE0)
            return false;

        var version = (h[1] >> 3) & 0x03;
        if (version == 1)                       // reserved
            return false;

        if (((h[1] >> 1) & 0x03) != 1)          // layer bits: 01 = Layer III, i.e. MP3
            return false;

        var bitrateIndex = (h[2] >> 4) & 0x0F;
        if (bitrateIndex is 0 or 15)            // free format / invalid
            return false;

        var sampleIndex = (h[2] >> 2) & 0x03;
        if (sampleIndex == 3)
            return false;

        var isMpeg1 = version == 3;
        var bitrate = isMpeg1 ? Mpeg1Layer3[bitrateIndex] : Mpeg2Layer3[bitrateIndex];
        var sampleRate = SampleRates[version][sampleIndex];
        var padding = (h[2] >> 1) & 0x01;

        var length = (isMpeg1 ? 144 : 72) * bitrate * 1000 / sampleRate + padding;
        if (length <= 4)
            return false;

        frame = new FrameHeader(bitrate, length, sampleRate, isMpeg1, IsMono: ((h[3] >> 6) & 0x03) == 3);
        return true;
    }

    /// <summary>
    /// Peeks whether this frame carries a Xing/Info/VBRI header, without moving the stream, and
    /// reads the total frame count out of it when it does (0 when the tag omits it). That count
    /// is what makes a VBR file measurable without decoding it.
    /// <para>
    /// The Xing tag sits right behind the side information, whose size depends on version and
    /// mode; VBRI (Fraunhofer) always sits 32 bytes behind the header. Both are big-endian.
    /// </para>
    /// </summary>
    private static bool TryReadVbrTag(FileStream stream, FrameHeader frame, out int frameCount)
    {
        frameCount = 0;

        var sideInfo = frame.IsMpeg1
            ? (frame.IsMono ? 17 : 32)
            : (frame.IsMono ? 9 : 17);

        var resume = stream.Position;
        try
        {
            Span<byte> tag = stackalloc byte[8];

            stream.Seek(sideInfo, SeekOrigin.Current);
            if (stream.Read(tag) == 8 && (Is(tag, "Xing") || Is(tag, "Info")))
            {
                // Flags follow the marker; bit 0 says a frame count comes next.
                if ((BeInt32(tag[4..]) & 0x0001) != 0)
                {
                    Span<byte> count = stackalloc byte[4];
                    if (stream.Read(count) == 4)
                        frameCount = BeInt32(count);
                }
                return true;
            }

            stream.Position = resume + 32;
            if (stream.Read(tag) == 8 && Is(tag, "VBRI"))
            {
                // "VBRI", version, delay, quality, byte count, frame count.
                stream.Position = resume + 32 + 14;
                Span<byte> count = stackalloc byte[4];
                if (stream.Read(count) == 4)
                    frameCount = BeInt32(count);
                return true;
            }

            return false;
        }
        finally
        {
            stream.Position = resume;
        }

        static bool Is(ReadOnlySpan<byte> value, string expected)
            => value[0] == expected[0] && value[1] == expected[1]
            && value[2] == expected[2] && value[3] == expected[3];

        static int BeInt32(ReadOnlySpan<byte> v) => (v[0] << 24) | (v[1] << 16) | (v[2] << 8) | v[3];
    }

    /// <summary>Returns the offset just past an ID3v2 tag, or 0 when there is none.</summary>
    private static long SkipId3v2(Stream stream)
    {
        var head = new byte[10];
        if (stream.Read(head, 0, 10) != 10)
            return 0;

        if (head[0] != (byte)'I' || head[1] != (byte)'D' || head[2] != (byte)'3')
            return 0;

        // Syncsafe integer: seven bits per byte.
        var size = ((head[6] & 0x7F) << 21) | ((head[7] & 0x7F) << 14)
                 | ((head[8] & 0x7F) << 7) | (head[9] & 0x7F);

        var total = 10L + size;
        if ((head[5] & 0x10) != 0)
            total += 10;   // footer present

        return total;
    }

    /// <summary>Finds the first real frame at or after <paramref name="start"/>.</summary>
    private static bool TrySync(FileStream stream, long start, out long position)
    {
        position = 0;
        if (start >= stream.Length)
            return false;

        stream.Position = start;
        var buffer = new byte[SyncWindow];
        var read = stream.Read(buffer, 0, buffer.Length);

        for (var i = 0; i + 4 <= read; i++)
        {
            if (buffer[i] != 0xFF || !StartsFrameChain(buffer, i, read))
                continue;

            position = start + i;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Whether a run of consecutive frame headers begins at <paramref name="offset"/>.
    /// <para>
    /// A single parseable header proves nothing. The padding an encoder or tagger leaves between
    /// the ID3v2 tag and the first audio frame routinely contains four bytes that read as a
    /// perfectly valid header, and taking it at face value is how a 128 kbit/s rip gets reported
    /// as "8 kbit/s, 1:45:23" — the bitrate is nonsense, and any duration derived from it with it.
    /// A real frame is followed by the next one exactly <c>Length</c> bytes on.
    /// </para>
    /// </summary>
    private static bool StartsFrameChain(byte[] buffer, int offset, int length)
    {
        for (var frames = 0; frames < ChainedFrames; frames++)
        {
            if (offset + 4 > length || !TryParseHeader(buffer.AsSpan(offset, 4), out var frame))
                return false;

            offset += frame.Length;
        }

        return true;
    }
}
