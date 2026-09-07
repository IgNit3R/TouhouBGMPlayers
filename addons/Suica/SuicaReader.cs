using System.Text;

namespace addons.Suica;

/// <summary>
/// Parser for the th075 (Touhou Suimusou / IaMP) "Suica" outer .dat container.
/// Ported from th075_suica_parser.py (verified against go-brightmoon pkg/pbgarc/suica.go).
///
/// Format (little-endian):
///   u16 entry_count
///   entry_count x 108 (0x6C) byte records — the whole table is obfuscated with a
///   rolling XOR keystream: k = t = 0x64; per byte: plain ^= k; k += t; t += 0x4D (mod 256)
///   each record: name[100] (NUL padded, CP932), u32 size @ +0x64, u32 abs offset @ +0x68
///   Entry data region: raw (uncompressed), starts right after the table, entries contiguous.
/// </summary>
public static class SuicaReader
{
    private const int RecordSize = 0x6C;   // 108
    private const int NameFieldSize = 0x64; // 100

    private static readonly Encoding Cp932 = InitCp932();

    private static Encoding InitCp932()
    {
        // CP932 (Shift-JIS + NEC/IBM extensions) is not enabled by default on
        // all runtimes; register the code-pages provider when it is missing.
        try
        {
            return Encoding.GetEncoding(932);
        }
        catch (Exception) // NotSupportedException / ArgumentException depending on runtime
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(932);
        }
    }

    /// <summary>A single entry in the Suica archive table.</summary>
    public sealed record SuicaEntry(int Index, string Name, uint Size, uint Offset);

    /// <summary>WAV loop points parsed from a "cue " chunk (first cue point's dwPosition).</summary>
    public sealed record WaveLoopInfo(double LoopStartSeconds, double LoopEndSeconds, uint LoopStartSample);

    /// <summary>
    /// Generates the rolling XOR keystream used to obfuscate the entry table.
    /// k and t wrap at 32 bits in the reference implementation, but only the low
    /// byte of k is XORed, so byte arithmetic is equivalent.
    /// </summary>
    internal static byte[] Keystream(int length)
    {
        byte k = 0x64, t = 0x64;
        var ks = new byte[length];
        for (int i = 0; i < length; i++)
        {
            ks[i] = k;
            k += t;
            t += 0x4D;
        }
        return ks;
    }

    /// <summary>Parses the entry table from a container stream. Stream position must be 0; not reset.</summary>
    public static List<SuicaEntry> ParseEntries(Stream stream)
    {
        using var br = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        long fileSize = stream.Length;
        int count = br.ReadUInt16();
        long tableSize = (long)count * RecordSize;
        if (2 + tableSize > fileSize)
            throw new InvalidDataException($"Invalid entry count {count}: table size {tableSize} exceeds file size {fileSize}.");

        byte[] table = br.ReadBytes((int)tableSize);
        byte[] ks = Keystream(table.Length);
        for (int i = 0; i < table.Length; i++)
            table[i] ^= ks[i];

        var entries = new List<SuicaEntry>(count);
        for (int i = 0; i < count; i++)
        {
            int p = i * RecordSize;
            int nameLen = 0;
            while (nameLen < NameFieldSize && table[p + nameLen] != 0)
                nameLen++;
            if (nameLen == 0)
                throw new InvalidDataException($"Invalid (empty) entry name at record {i}.");

            string name = Cp932.GetString(table, p, nameLen);
            uint size = BitConverter.ToUInt32(table, p + 0x64);
            uint offset = BitConverter.ToUInt32(table, p + 0x68);
            if (offset > fileSize || size > fileSize - offset)
                throw new InvalidDataException($"Entry {i} ({name}) offset/size out of range: offset={offset}, size={size}.");
            entries.Add(new SuicaEntry(i, name, size, offset));
        }
        return entries;
    }

    /// <summary>Parses the entry table from a full container image.</summary>
    public static List<SuicaEntry> ParseEntries(byte[] container)
    {
        using var ms = new MemoryStream(container, writable: false);
        return ParseEntries(ms);
    }

    /// <summary>
    /// Extracts all entries into memory. The stream may be positioned anywhere;
    /// each entry is read at its absolute offset. Entry data is stored raw
    /// (the Suica format does not compress).
    /// </summary>
    public static IDictionary<string, byte[]> ExtractAll(Stream stream, IReadOnlyList<SuicaEntry> entries)
    {
        var result = new Dictionary<string, byte[]>(entries.Count, StringComparer.OrdinalIgnoreCase);
        var buffer = new byte[81920];
        foreach (var e in entries)
        {
            var data = new byte[e.Size];
            stream.Seek(e.Offset, SeekOrigin.Begin);
            long total = 0;
            while (total < e.Size)
            {
                int n = stream.Read(data, (int)total, (int)Math.Min(buffer.Length, e.Size - total));
                if (n <= 0)
                    throw new EndOfStreamException($"Unexpected EOF while reading entry '{e.Name}'.");
                total += n;
            }
            result[e.Name] = data;
        }
        return result;
    }

    /// <summary>Extracts all entries from a full container image.</summary>
    public static IDictionary<string, byte[]> ExtractAll(byte[] container)
    {
        var entries = ParseEntries(container);
        using var ms = new MemoryStream(container, writable: false);
        return ExtractAll(ms, entries);
    }

    /// <summary>
    /// Reads a single entry's bytes from the stream at its absolute offset (streaming-friendly).
    /// </summary>
    public static byte[] ExtractEntry(Stream stream, SuicaEntry entry)
    {
        var data = new byte[entry.Size];
        stream.Seek(entry.Offset, SeekOrigin.Begin);
        long total = 0;
        while (total < entry.Size)
        {
            int n = stream.Read(data, (int)total, (int)(entry.Size - total));
            if (n <= 0)
                throw new EndOfStreamException($"Unexpected EOF while reading entry '{entry.Name}'.");
            total += n;
        }
        return data;
    }

    /// <summary>
    /// Parses a RIFF/WAVE byte image for its "cue " chunk. Returns the loop points:
    /// loop start = first cue point's dwPosition (samples at 44100 Hz);
    /// loop end = end of the audio data (file tail).
    /// Returns null when the file has no "cue " chunk.
    /// </summary>
    public static WaveLoopInfo? ParseWaveCueLoop(byte[] wav)
    {
        if (wav.Length < 12 || wav.AsSpan(0, 4).SequenceEqual("RIFF"u8) == false)
            throw new InvalidDataException("Not a RIFF file.");
        if (wav.AsSpan(8, 4).SequenceEqual("WAVE"u8) == false)
            throw new InvalidDataException("Not a WAVE file.");

        uint loopStartSample = 0;
        bool hasCue = false;
        long dataEnd = -1;      // absolute end of the "data" chunk payload
        int blockAlign = 0;

        int pos = 12;
        while (pos + 8 <= wav.Length)
        {
            string chunkId = Encoding.ASCII.GetString(wav, pos, 4);
            uint chunkSize = BitConverter.ToUInt32(wav, pos + 4);
            int body = pos + 8;
            if (body + chunkSize > wav.Length)
                chunkSize = (uint)Math.Max(0, wav.Length - body); // tolerate truncated final chunk

            if (chunkId == "fmt " && chunkSize >= 16)
            {
                blockAlign = BitConverter.ToUInt16(wav, body + 12);
            }
            else if (chunkId == "cue " && chunkSize >= 4 + 24 && !hasCue)
            {
                uint numCuePoints = BitConverter.ToUInt32(wav, body);
                if (numCuePoints >= 1)
                {
                    // CUEPOINT: dwIdentifier, dwPosition, fccChunk, dwChunkStart, dwBlockStart, dwSampleOffset
                    loopStartSample = BitConverter.ToUInt32(wav, body + 4 + 4);
                    hasCue = true;
                }
            }
            else if (chunkId == "data")
            {
                dataEnd = body + chunkSize;
            }

            pos = body + (int)chunkSize + ((int)chunkSize & 1); // chunks are word-aligned
        }

        if (!hasCue)
            return null;

        double loopEndSeconds;
        if (dataEnd > 0 && blockAlign > 0)
            loopEndSeconds = (double)(dataEnd / blockAlign) / 44100.0;
        else
            loopEndSeconds = (double)wav.Length / 44100.0; // fallback: raw file tail

        return new WaveLoopInfo(loopStartSample / 44100.0, loopEndSeconds, loopStartSample);
    }
}
