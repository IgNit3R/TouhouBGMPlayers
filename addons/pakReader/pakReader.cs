// TFPK / th175 cga container reader for tasofro fighting games (th135/th145/th155/th175).
//
// Self-contained, zero NuGet dependencies (BCL only: System.Numerics.BigInteger for
// RSA, System.IO.Compression.ZLibStream for the TFPK name table, cp932 via
// CodePagesEncodingProvider which ships with the shared runtime).
//
// Sources ported from:
//   - TFPK v0/v1: arc_unpacker src/dec/twilight_frontier/tfpk_archive_decoder.cc
//   - th175 cga/cgb: brliron 135tk th175arc (th175arc.h / read.c / common.c)
//
// Format summary
// ==============
// TFPK (magic "TFPK" + 1 version byte, version 0 = th135, 1 = th145/th155):
//   Everything after the 5-byte header is organised as RSA blocks: each 0x40-byte
//   chunk on disk is m = RSA_public_decrypt(chunk) = unpad(chunk^e mod n) with a
//   512-bit per-game modulus and e = 65537; the recovered PKCS#1 v1.5 message is
//   32 bytes. (Some th145 English-patch builds store the blocks unencrypted; the
//   raw 0x40-byte chunk's first 0x20 bytes are then used directly.)
//   Layout, in block order:
//     block 0                       : u32 dir_count
//     dir_count blocks              : u32 dir_hash_seed, u32 file_count (per dir)
//     block                         : u32 table_zsize, u32 table_size, u32 block_count
//     block_count blocks            : concatenated, first table_zsize bytes are a
//                                     zlib stream inflating to the name table
//                                     (NUL-terminated cp932 file basenames, one per
//                                     file, following the dir table order)
//     block                         : u32 file_count
//     3 blocks per file (b1,b2,b3)  : v0: size=b1[0..4], offset=b1[4..8],
//                                          name_hash=b2[0..4], key=b3[0..16]
//                                     v1: size  = b1[0..4] ^ b3[0..4]
//                                         offset= b1[4..8] ^ b3[4..8]
//                                         name_hash = b2[0..4] ^ b3[0..4]
//                                         key[j*4..] = neg32(b3 u32le[j]) for j in 0..3
//   All file offsets are relative to the data base = table_end + 3*file_count*64:
//   the archive stores a SECOND, unused copy of the 3-block-per-file table between
//   the name/file tables and the data region (both v0 and v1).
//   File content decryption:
//     v0: plain[i] = data[i] ^ key[i % 16]
//     v1: aux[4] = key[0..4]; per byte i: t = data[i];
//         plain[i] = t ^ key[i % 16] ^ aux[i & 3]; aux[i & 3] = t   (ciphertext feed-back)
//   Name resolution: the archive stores FNV name hashes, not names. Names come
//   either from the embedded name table (v0/th135 seeds each dir's chain with the
//   real dir hash; v1 dir hashes are garbage) or from an external file name list
//   (the game's fileslist.txt): full path is lowercased (ASCII only), '/'→'\',
//   hashed with
//     v0: FNV-1  (h *= 0x1000193; h ^= c)  seeded, no final negation
//     v1: FNV-1a (h ^= c; h *= 0x1000193)  then neg32  (h = 0 - h)
//   Unresolved entries are named "unk-%08x" (v0: "unk-%05d-%08x" from the 2nd file on).
//
// th175 cga/cgb (no magic; footer-driven):
//   Last 0x20 bytes of the file are the footer, decrypted in place:
//     u32 unk1..unk5, u32 file_desc_size (0x18), u32 nb_files, u32 footer_size (0x20)
//   Immediately before the footer: nb_files x 0x18 descriptors, decrypted:
//     u64 key (FNV-1a hash of the forward-slash path), u64 abs offset, u64 size
//   Position-dependent stream cipher: per 4-byte word at pos,
//     k = (uint)(size ^ offset_in_file) + pos/4  (offset_in_file = entry's abs offset,
//     or the footer/table's own abs offset); then 4 rounds of
//       round(k): a = k * 0x5E4789C9 (int64); b = (a>>46)+(a>>63);
//                 r = (uint)((k - b*0xADC8) * 0xBC8F + b*0xFFFFF2B9);
//                 if ((int)r <= 0) r += 0x7FFFFFFF;
//     xor word with (r1<<24)|(r2<<16)|(r3<<8)|r4 (big-endian assembly of the 4 LSBs).
//   The entry whose key == PAYLOADER_EXE_HASH (0x1f47c0c8) is stored in plain.
//   Entry names come from fileslist.js (JSON array of paths; '\'→'/' before hashing).

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Numerics;
using System.Text;

namespace addons;

/// <summary>Audio-relevant container-entry kind, detected from the magic bytes.</summary>
public enum TfpkAudioKind : byte
{
    Unknown = 0,
    /// <summary>Raw Ogg stream ("OggS").</summary>
    Ogg = 1,
    /// <summary>Tasofro TFWA container ("TFWA") - despite the .wav extension it wraps Ogg.</summary>
    TFWA = 2,
}

/// <summary>Merged entry metadata (after multi-archive override resolution).</summary>
public sealed record TfpkEntryInfo(string Name, long Size, TfpkAudioKind Kind);

/// <summary>
/// Reads tasofro .pak (TFPK v0/v1) and th175 .cga/.cgb archives and exposes
/// name → byte[] lookups. When several archives are opened together, entries of
/// later archives override same-named entries of earlier ones (matching the
/// engine's th135+th135b, th155.pak+th155b.pak, data.cga+data.cgb load order).
/// All content is decrypted in memory; nothing is written to disk.
/// </summary>
public sealed class pakReader : IDisposable
{
    private const uint PAYLOADER_EXE_HASH = 0x1F47C0C8;

    private sealed record Loc(long Offset, int Size, byte[]? Key, uint CgaKey);

    private readonly List<Stream> _streams = new();
    private readonly List<int> _kind = new();          // 0 = TFPK v0, 1 = TFPK v1, 2 = cga/cgb
    private readonly List<Dictionary<string, Loc>> _byName = new();  // normalized per archive
    // norm name -> (archive index, loc, display name, size); insertion ordered
    private readonly Dictionary<string, (int Arch, Loc Loc, string Name, long Size)> _merged = new();
    private List<TfpkEntryInfo>? _entriesView;

    private static readonly Encoding Cp932 = InitCp932();

    private static Encoding InitCp932()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(932);
    }

    // ------------------------------------------------------------------ public API

    /// <summary>
    /// Opens the given archives in override order. <paramref name="fileNameList"/>,
    /// when provided, supplies real paths for hash-named entries: for TFPK v1
    /// archives (th145/th155) it is the game's fileslist.txt (full file paths,
    /// one per string); for TFPK v0 (th135) it is a list of directory paths
    /// (the file basenames come from the archive's embedded name table); for
    /// th175 cga/cgb it is fileslist.js (full paths). Backslashes or slashes
    /// both accepted.
    /// </summary>
    public static pakReader Open(IEnumerable<string> archivePaths, IEnumerable<string>? fileNameList = null)
    {
        var reader = new pakReader();
        try
        {
            foreach (var path in archivePaths)
            {
                var stream = File.OpenRead(path);
                reader._streams.Add(stream);
                var (kindIndex, map) = ParseArchive(stream, fileNameList);
                reader._kind.Add(kindIndex);
                reader._byName.Add(map);
            }
            reader.BuildMerged();
            return reader;
        }
        catch
        {
            reader.Dispose();
            throw;
        }
    }

    /// <summary>All entries after multi-archive override resolution, in first-seen order.</summary>
    public IReadOnlyList<TfpkEntryInfo> Entries => _entriesView ??= BuildEntriesView();

    public int Count => _merged.Count;

    public bool Contains(string name) => _merged.ContainsKey(NormName(name));

    /// <summary>Returns the (possibly overridden) content of an entry; throws if absent.</summary>
    public byte[] GetEntry(string name)
    {
        if (!TryGetEntry(name, out var data))
            throw new KeyNotFoundException($"TFPK entry not found: {name}");
        return data;
    }

    public bool TryGetEntry(string name, out byte[] data)
    {
        if (_merged.TryGetValue(NormName(name), out var hit))
        {
            data = ReadData(hit.Arch, hit.Loc);
            return true;
        }
        data = Array.Empty<byte>();
        return false;
    }

    /// <summary>Detects the entry kind from magic bytes.</summary>
    public static TfpkAudioKind DetectKind(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 4)
        {
            if (data[0] == (byte)'O' && data[1] == (byte)'g' && data[2] == (byte)'g' && data[3] == (byte)'S')
                return TfpkAudioKind.Ogg;
            if (data[0] == (byte)'T' && data[1] == (byte)'F' && data[2] == (byte)'W' && data[3] == (byte)'A')
                return TfpkAudioKind.TFWA;
        }
        return TfpkAudioKind.Unknown;
    }

    public void Dispose()
    {
        foreach (var s in _streams)
            s.Dispose();
        _streams.Clear();
    }

    // ------------------------------------------------------------------ merge / read

    private void BuildMerged()
    {
        for (int a = 0; a < _byName.Count; a++)
        {
            foreach (var (norm, loc) in _byName[a])
                _merged[norm] = (a, loc, norm, loc.Size);
        }
    }

    private List<TfpkEntryInfo> BuildEntriesView()
    {
        var list = new List<TfpkEntryInfo>(_merged.Count);
        foreach (var (norm, hit) in _merged)
        {
            var kind = PeekKind(hit.Arch, hit.Loc);
            list.Add(new TfpkEntryInfo(hit.Name, hit.Size, kind));
        }
        return list;
    }

    private TfpkAudioKind PeekKind(int arch, Loc loc)
    {
        var s = _streams[arch];
        if (loc.Size < 4)
            return TfpkAudioKind.Unknown;
        s.Seek(loc.Offset, SeekOrigin.Begin);
        Span<byte> buf = stackalloc byte[4];
        if (s.Read(buf) != 4)
            return TfpkAudioKind.Unknown;

        // Decrypt just the first 4 bytes (both ciphers are position-local here:
        // v1's feedback chain leaves the first 4 bytes unchanged, v0 is a plain
        // XOR, cga derives everything from size^offset).
        int kind = _kind[arch];
        if (kind == 2)
        {
            if (loc.CgaKey != PAYLOADER_EXE_HASH)
            {
                uint fileKey = unchecked((uint)((loc.Size ^ loc.Offset) & 0xFFFFFFFF));
                uint tmp = fileKey, xor = 0;
                for (int i = 0; i < 4; i++)
                {
                    tmp = DecryptStep175(tmp);
                    xor = (xor << 8) | (tmp & 0xFF);
                }
                uint word = BinaryPrimitives.ReadUInt32LittleEndian(buf) ^ xor;
                BinaryPrimitives.WriteUInt32LittleEndian(buf, word);
            }
        }
        else if (kind == 0 && loc.Key != null)
        {
            for (int i = 0; i < 4; i++)
                buf[i] ^= loc.Key[i];
        }
        // kind == 1: first 4 bytes pass through the cipher unchanged.
        return DetectKind(buf);
    }

    private byte[] ReadData(int arch, Loc loc)
    {
        var s = _streams[arch];
        s.Seek(loc.Offset, SeekOrigin.Begin);
        var data = new byte[loc.Size];
        int read = 0;
        while (read < data.Length)
        {
            int n = s.Read(data, read, data.Length - read);
            if (n <= 0)
                throw new EndOfStreamException($"Unexpected end of archive at {s.Position} (need {data.Length - read} more bytes)");
            read += n;
        }

        if (_kind[arch] == 2)
        {
            // th175 cga/cgb position-dependent stream cipher.
            if (loc.CgaKey != PAYLOADER_EXE_HASH)
                DecryptCga(data, loc.Offset);
        }
        else
        {
            DecryptTfpkContent(data, _kind[arch], loc.Key!);
        }
        return data;
    }

    private static string NormName(string name) => name.ToLowerInvariant().Replace('/', '\\');

    // ------------------------------------------------------------------ archive parsing

    private static (int Kind, Dictionary<string, Loc> Map) ParseArchive(Stream stream, IEnumerable<string>? fileNameList)
    {
        Span<byte> magic = stackalloc byte[5];
        long oldPos = stream.Position;
        if (stream.Read(magic) != 5)
            throw new InvalidDataException("File too small for a TFPK/cga container");
        stream.Seek(oldPos, SeekOrigin.Begin);

        if (magic[0] == (byte)'T' && magic[1] == (byte)'F' && magic[2] == (byte)'P' && magic[3] == (byte)'K')
        {
            if (magic[4] > 1)
                throw new InvalidDataException($"Unsupported TFPK version {magic[4]}");
            return ParseTfpk(stream, magic[4], fileNameList);
        }
        return ParseCga(stream, fileNameList);
    }

    // ------------------------------------------------------------ TFPK (th135/145/155)

    private static readonly byte[][] RsaModuli =
    {
        // TH13.5 Japanese version
        new byte[]
        {
            0xC7, 0x9A, 0x9E, 0x9B, 0xFB, 0xC2, 0x0C, 0xB0,
            0xC3, 0xE7, 0xAE, 0x27, 0x49, 0x67, 0x62, 0x8A,
            0x78, 0xBB, 0xD1, 0x2C, 0xB2, 0x4D, 0xF4, 0x87,
            0xC7, 0x09, 0x35, 0xF7, 0x01, 0xF8, 0x2E, 0xE5,
            0x49, 0x3B, 0x83, 0x6B, 0x84, 0x26, 0xAA, 0x42,
            0x9A, 0xE1, 0xCC, 0xEE, 0x08, 0xA2, 0x15, 0x1C,
            0x42, 0xE7, 0x48, 0xB1, 0x9C, 0xCE, 0x7A, 0xD9,
            0x40, 0x1A, 0x4D, 0xD4, 0x36, 0x37, 0x5C, 0x89,
        },
        // TH13.5 English patch
        new byte[]
        {
            0xFF, 0x65, 0x72, 0x74, 0x61, 0x69, 0x52, 0x20,
            0x2D, 0x2D, 0x20, 0x69, 0x6F, 0x6B, 0x6E, 0x61,
            0x6C, 0x46, 0x20, 0x73, 0x73, 0x65, 0x6C, 0x42,
            0x20, 0x64, 0x6F, 0x47, 0x20, 0x79, 0x61, 0x4D,
            0x08, 0x8B, 0xF4, 0x75, 0x5D, 0x78, 0xB1, 0xC8,
            0x93, 0x7F, 0x40, 0xEA, 0x34, 0xA5, 0x85, 0xC1,
            0x1B, 0x8D, 0x63, 0x17, 0x75, 0x98, 0x2D, 0xA8,
            0x17, 0x45, 0x31, 0x31, 0x51, 0x4F, 0x6E, 0x8D,
        },
        // TH14.5 Japanese version
        new byte[]
        {
            0xC6, 0x43, 0xE0, 0x9D, 0x35, 0x5E, 0x98, 0x1D,
            0xBE, 0x63, 0x6D, 0x3A, 0x5F, 0x84, 0x0F, 0x49,
            0xB8, 0xE8, 0x53, 0xF5, 0x42, 0x06, 0x37, 0x3B,
            0x36, 0x25, 0xCB, 0x65, 0xCE, 0xDD, 0x68, 0x8C,
            0xF7, 0x5D, 0x72, 0x0A, 0xC0, 0x47, 0xBD, 0xFA,
            0x3B, 0x10, 0x4C, 0xD2, 0x2C, 0xFE, 0x72, 0x03,
            0x10, 0x4D, 0xD8, 0x85, 0x15, 0x35, 0x55, 0xA3,
            0x5A, 0xAF, 0xC3, 0x4A, 0x3B, 0xF3, 0xE2, 0x37,
        },
        // TH15.5 Japanese version
        new byte[]
        {
            0xC4, 0x4D, 0x6A, 0x2F, 0x05, 0x78, 0x2C, 0x0F,
            0xD7, 0x5C, 0x82, 0x97, 0x17, 0x60, 0x91, 0xDD,
            0x6F, 0x83, 0x61, 0x81, 0xD1, 0x4E, 0x06, 0x9B,
            0x94, 0x37, 0xD2, 0x98, 0x4D, 0xE4, 0x7B, 0xBF,
            0x42, 0x60, 0xA7, 0x8F, 0x88, 0xD6, 0xFD, 0xFE,
            0xE1, 0xF5, 0x6A, 0x0B, 0x29, 0xCF, 0x0B, 0xED,
            0x66, 0xF0, 0xAC, 0x4E, 0xD7, 0xEF, 0x96, 0x06,
            0x8B, 0xFA, 0x8E, 0x33, 0x48, 0xA3, 0x02, 0x7D,
        },
    };

    private const int RsaExponent = 65537;
    private const int RsaBlockSize = 0x40;
    private const int RsaMessageSize = 0x20;

    /// <summary>Sequential reader over RSA-protected 0x40-byte blocks yielding 0x20-byte payloads.</summary>
    private sealed class RsaBlockReader
    {
        private readonly Stream _s;
        private readonly BigInteger _n;
        private readonly bool _raw;

        private RsaBlockReader(Stream s, BigInteger n, bool raw)
        {
            _s = s;
            _n = n;
            _raw = raw;
        }

        public static RsaBlockReader Create(Stream s)
        {
            long pos = s.Position;
            var chunk = new byte[RsaBlockSize];
            if (s.Read(chunk, 0, RsaBlockSize) != RsaBlockSize)
                throw new InvalidDataException("Stream too short for a TFPK RSA block");
            s.Seek(pos, SeekOrigin.Begin);

            foreach (var modulus in RsaModuli)
            {
                if (TryDecryptBlock(chunk, modulus, out _))
                    return new RsaBlockReader(s, FromBigEndian(modulus), raw: false);
            }

            // No encryption - TH14.5 English patch. First 4 bytes are the dir count,
            // so the rest of the first 16 bytes should be zero.
            bool plausible = true;
            for (int i = 4; i < 16; i++)
                plausible &= chunk[i] == 0;
            if (plausible)
                return new RsaBlockReader(s, default, raw: true);

            throw new InvalidDataException("Unknown TFPK RSA public key (no modulus matched)");
        }

        public long Position => _s.Position;

        public byte[] ReadBlock()
        {
            var chunk = new byte[RsaBlockSize];
            if (_s.Read(chunk, 0, RsaBlockSize) != RsaBlockSize)
                throw new EndOfStreamException("Truncated TFPK RSA block");
            if (_raw)
            {
                var raw = new byte[RsaMessageSize];
                Array.Copy(chunk, raw, RsaMessageSize);
                return raw;
            }
            if (!TryDecryptBlock(chunk, _n, out var msg))
                throw new InvalidDataException($"TFPK RSA block failed PKCS#1 unpadding at stream pos {_s.Position - RsaBlockSize}");
            return msg;
        }

        public uint ReadU32() => BinaryPrimitives.ReadUInt32LittleEndian(ReadBlock());
    }

    private static bool TryDecryptBlock(byte[] chunk64, byte[] modulusBe, out byte[] message32)
        => TryDecryptBlock(chunk64, FromBigEndian(modulusBe), out message32);

    private static bool TryDecryptBlock(byte[] chunk64, BigInteger n, out byte[] message32)
    {
        message32 = Array.Empty<byte>();
        if (n == default)
            return false;

        BigInteger c = FromBigEndian(chunk64);
        BigInteger m = BigInteger.ModPow(c, RsaExponent, n);
        Span<byte> be = stackalloc byte[64];
        if (!TryWriteBigEndian(m, be))
            return false;

        // PKCS#1 v1.5 type-1: 00 01 FF..FF 00 || message. The message length is
        // exactly the block's valid payload (4..16 bytes depending on the field
        // being encoded); arc_unpacker relies on reading only within it.
        if (be[0] != 0 || be[1] != 1)
            return false;
        int i = 2;
        while (i < be.Length && be[i] == 0xFF)
            i++;
        if (i < 10 || i >= be.Length || be[i] != 0)
            return false;
        int msgLen = be.Length - i - 1;
        if (msgLen is < 4 or > RsaMessageSize)
            return false;
        message32 = new byte[RsaMessageSize];
        be[(i + 1)..].ToArray().CopyTo(message32, 0);
        return true;
    }

    private static BigInteger FromBigEndian(ReadOnlySpan<byte> be)
    {
        var le = new byte[be.Length + 1]; // extra byte keeps the value positive
        for (int i = 0; i < be.Length; i++)
            le[be.Length - 1 - i] = be[i];
        return new BigInteger(le);
    }

    private static bool TryWriteBigEndian(BigInteger value, Span<byte> be)
    {
        byte[] le = value.ToByteArray();
        if (le.Length > be.Length && (le.Length != be.Length + 1 || le[^1] != 0))
            return false;
        be.Clear();
        for (int i = 0; i < be.Length && i < le.Length; i++)
            be[be.Length - 1 - i] = le[i];
        return true;
    }

    private static uint Neg32(uint x) => unchecked((uint)(-(int)x));

    private static (int Kind, Dictionary<string, Loc> Map) ParseTfpk(Stream stream, int version, IEnumerable<string>? fileNameList)
    {
        stream.Seek(5, SeekOrigin.Begin); // "TFPK" + version byte
        var reader = RsaBlockReader.Create(stream);

        // name-list based lookup (game fileslist / user provided).
        // v1: full file paths, hashed whole. v0 (th135): the archive seeds each
        // dir's name chain with the hash of the DIRECTORY path including its
        // trailing separator ("data\bgm\"), so the list is treated as dir paths.
        var userMap = new Dictionary<uint, string>();
        var dirMap = new Dictionary<uint, string>();
        if (fileNameList != null)
        {
            foreach (var name in fileNameList)
            {
                if (string.IsNullOrEmpty(name))
                    continue;
                if (version == 0)
                {
                    string dir = name.TrimEnd('\\', '/');
                    dirMap[TfpkNameHash(dir + "\\", version)] = dir;
                }
                else
                {
                    userMap[TfpkNameHash(name, version)] = name;
                }
            }
        }

        // --- directory table
        uint dirCount = reader.ReadU32();
        var dirs = new (uint Seed, uint FileCount)[dirCount];
        for (int i = 0; i < dirCount; i++)
        {
            var block = reader.ReadBlock();
            dirs[i] = (BinaryPrimitives.ReadUInt32LittleEndian(block.AsSpan(0)),
                       BinaryPrimitives.ReadUInt32LittleEndian(block.AsSpan(4)));
        }

        // --- embedded name table (zlib); all three header fields live in ONE block
        var nameHeader = reader.ReadBlock();
        uint tableZsize = BinaryPrimitives.ReadUInt32LittleEndian(nameHeader.AsSpan(0));
        uint tableSize = BinaryPrimitives.ReadUInt32LittleEndian(nameHeader.AsSpan(4));
        uint nameBlockCount = BinaryPrimitives.ReadUInt32LittleEndian(nameHeader.AsSpan(8));

        var packed = new MemoryStream();
        for (int i = 0; i < nameBlockCount; i++)
        {
            try
            {
                packed.Write(reader.ReadBlock());
            }
            catch (Exception ex) when (Environment.GetEnvironmentVariable("TFPK_DEBUG") != null)
            {
                Console.Error.WriteLine($"[TFPK_DEBUG] name block {i}/{nameBlockCount} zsize={tableZsize} size={tableSize} pos={reader.Position} ex={ex.Message}");
                throw;
            }
        }
        var nameBytes = new byte[tableZsize];
        packed.Position = 0;
        if (packed.Read(nameBytes, 0, nameBytes.Length) != nameBytes.Length)
            throw new InvalidDataException("TFPK name table shorter than declared");
        string? unpacked;
        using (var zl = new ZLibStream(new MemoryStream(nameBytes), CompressionMode.Decompress))
        using (var outMs = new MemoryStream())
        {
            zl.CopyTo(outMs);
            unpacked = Cp932.GetString(outMs.ToArray());
        }

        // Build hash -> "dir/name" map from the embedded table.
        var fnMap = new Dictionary<uint, string>();
        int namePos = 0;
        foreach (var (seed, dirFileCount) in dirs)
        {
            string dn;
            if (userMap.TryGetValue(seed, out var fullDir) || dirMap.TryGetValue(seed, out fullDir))
                dn = fullDir.Replace('\\', '/');
            else
                dn = $"unk-{seed:x8}";
            if (dn.Length > 0 && dn[dn.Length - 1] != '/')
                dn += "/";

            for (uint j = 0; j < dirFileCount; j++)
            {
                int end = namePos;
                while (end < unpacked.Length && unpacked[end] != '\0')
                    end++;
                string fn = unpacked[namePos..end];
                namePos = end + 1;
                if (fn.Length == 0)
                    continue;
                uint hash = TfpkNameHash(fn, version, seed);
                fnMap[hash] = dn + fn;
            }
        }
        // User-provided full paths override embedded (garbage-seeded) names.
        foreach (var (hash, name) in userMap)
            fnMap[hash] = name;

        // --- file table
        uint fileCount = reader.ReadU32();
        long tableEnd = reader.Position;
        // Both v0 (th135) and v1 (th145/th155) archives contain a second (unused)
        // copy of the file table - another 3 blocks per file - between the table
        // and the data region. Verified by signature-scanning th135.pak for a
        // known sfl entry: true start = tableEnd + 3*fileCount*64 (delta 1837632
        // for 9571 entries), and by constant-drift analysis of th155b.
        long dataBase = tableEnd + 3L * fileCount * RsaBlockSize;
        var map = new Dictionary<string, Loc>(capacity: (int)fileCount);
        for (uint i = 0; i < fileCount; i++)
        {
            var b1 = reader.ReadBlock();
            var b2 = reader.ReadBlock();
            var b3 = reader.ReadBlock();

            long size;
            long offset;
            uint fnHash;
            byte[] key;

            if (version == 0)
            {
                size = BinaryPrimitives.ReadUInt32LittleEndian(b1.AsSpan(0));
                offset = BinaryPrimitives.ReadUInt32LittleEndian(b1.AsSpan(4));
                fnHash = BinaryPrimitives.ReadUInt32LittleEndian(b2.AsSpan(0));
                key = b3[..16];
            }
            else
            {
                // b3 is consumed sequentially: K0=size, K1=offset, K2=name hash, K3=unknown.
                uint s1 = BinaryPrimitives.ReadUInt32LittleEndian(b1.AsSpan(0)) ^ BinaryPrimitives.ReadUInt32LittleEndian(b3.AsSpan(0));
                uint o1 = BinaryPrimitives.ReadUInt32LittleEndian(b1.AsSpan(4)) ^ BinaryPrimitives.ReadUInt32LittleEndian(b3.AsSpan(4));
                size = s1;
                offset = o1;
                fnHash = BinaryPrimitives.ReadUInt32LittleEndian(b2.AsSpan(0)) ^ BinaryPrimitives.ReadUInt32LittleEndian(b3.AsSpan(8));

                key = new byte[16];
                for (int j = 0; j < 4; j++)
                {
                    uint v = BinaryPrimitives.ReadUInt32LittleEndian(b3.AsSpan(j * 4));
                    BinaryPrimitives.WriteUInt32LittleEndian(key.AsSpan(j * 4), Neg32(v));
                }
            }

            string name = fnMap.TryGetValue(fnHash, out var known)
                ? known
                : (version == 0 && i > 0 ? $"unk-{i:d5}-{fnHash:x8}" : $"unk-{fnHash:x8}");

            map[NormName(name)] = new Loc(dataBase + offset, checked((int)size), key, 0);
        }

        return (version, map);
    }

    private static string NormalizeForHash(string name)
    {
        // arc_unpacker: lower ASCII only, then '/' -> '\'.
        var chars = name.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            char c = chars[i];
            if (c >= 'A' && c <= 'Z')
                chars[i] = (char)(c + ('a' - 'A'));
            else if (c == '/')
                chars[i] = '\\';
        }
        return new string(chars);
    }

    private static uint TfpkNameHash(string name, int version, uint seed = 0x811C9DC5)
    {
        byte[] bytes = Cp932.GetBytes(NormalizeForHash(name));
        unchecked
        {
            uint h = seed;
            if (version == 0)
            {
                // FNV-1
                foreach (byte c in bytes)
                {
                    h *= 0x1000193;
                    h ^= c;
                }
                return h;
            }
            // FNV-1a + final negation
            foreach (byte c in bytes)
            {
                h ^= c;
                h *= 0x1000193;
            }
            return Neg32(h);
        }
    }

    private static void DecryptTfpkContent(byte[] data, int version, byte[] key)
    {
        if (version == 0)
        {
            for (int i = 0; i < data.Length; i++)
                data[i] ^= key[i % key.Length];
        }
        else
        {
            Span<byte> aux = stackalloc byte[4];
            key.AsSpan(0, 4).CopyTo(aux);
            for (int i = 0; i < data.Length; i++)
            {
                byte t = data[i];
                data[i] = (byte)(t ^ key[i % key.Length] ^ aux[i & 3]);
                aux[i & 3] = t;
            }
        }
    }

    // ------------------------------------------------------------ th175 cga / cgb

    private static (int Kind, Dictionary<string, Loc> Map) ParseCga(Stream stream, IEnumerable<string>? fileNameList)
    {
        long fileLen = stream.Length;
        if (fileLen < 0x20)
            throw new InvalidDataException("cga file too small");

        var hashMap = new Dictionary<uint, string>();
        if (fileNameList != null)
        {
            foreach (var raw in fileNameList)
            {
                if (string.IsNullOrEmpty(raw))
                    continue;
                string path = raw.Replace('\\', '/');
                uint h = CalcHash175(path);
                if (!hashMap.ContainsKey(h))
                    hashMap[h] = path;
            }
        }

        // --- footer
        long footerOffset = fileLen - 0x20;
        var footer = new byte[0x20];
        stream.Seek(footerOffset, SeekOrigin.Begin);
        if (stream.Read(footer, 0, 0x20) != 0x20)
            throw new EndOfStreamException();
        DecryptCgaBuffer(footer, footerOffset);

        uint fileDescSize = BinaryPrimitives.ReadUInt32LittleEndian(footer.AsSpan(0x14));
        uint nbFiles = BinaryPrimitives.ReadUInt32LittleEndian(footer.AsSpan(0x18));
        uint footerSize = BinaryPrimitives.ReadUInt32LittleEndian(footer.AsSpan(0x1C));
        if (footerSize != 0x20 || fileDescSize != 0x18)
            throw new InvalidDataException($"Invalid cga footer (desc={fileDescSize}, footer={footerSize})");

        // --- descriptor table
        long tableOffset = footerOffset - (long)fileDescSize * nbFiles;
        if (tableOffset < 0)
            throw new InvalidDataException("Invalid cga descriptor table size");
        var table = new byte[fileDescSize * nbFiles];
        stream.Seek(tableOffset, SeekOrigin.Begin);
        {
            int read = 0;
            while (read < table.Length)
            {
                int n = stream.Read(table, read, table.Length - read);
                if (n <= 0)
                    throw new EndOfStreamException();
                read += n;
            }
        }
        DecryptCgaBuffer(table, tableOffset);

        var map = new Dictionary<string, Loc>(capacity: (int)nbFiles);
        for (uint i = 0; i < nbFiles; i++)
        {
            int o = (int)(i * fileDescSize);
            ulong key = BinaryPrimitives.ReadUInt64LittleEndian(table.AsSpan(o));
            ulong offset = BinaryPrimitives.ReadUInt64LittleEndian(table.AsSpan(o + 8));
            ulong size = BinaryPrimitives.ReadUInt64LittleEndian(table.AsSpan(o + 16));

            uint hash = (uint)(key & 0xFFFFFFFF);
            string name = hashMap.TryGetValue(hash, out var known) ? known : $"unk/{hash:x8}";
            map[NormName(name)] = new Loc((long)offset, checked((int)size), null, hash);
        }

        return (2, map);
    }

    private static uint CalcHash175(string path)
    {
        // FNV-1a over the forward-slash path (no case folding; fileslist.js is lowercase).
        unchecked
        {
            uint h = 0x811C9DC5;
            foreach (byte c in Encoding.UTF8.GetBytes(path))
            {
                h ^= c;
                h *= 0x1000193;
            }
            return h;
        }
    }

    /// <summary>Decrypts a buffer whose absolute position in the archive is <paramref name="offsetInFile"/>.</summary>
    private static void DecryptCgaBuffer(byte[] buffer, long offsetInFile)
        => DecryptCgaBuffer(buffer.AsSpan(), buffer.Length, offsetInFile);

    private static void DecryptCga(byte[] data, long offsetInFile)
        => DecryptCgaBuffer(data.AsSpan(), data.Length, offsetInFile);

    private static void DecryptCgaBuffer(Span<byte> buffer, long size, long offsetInFile)
    {
        uint fileKey = unchecked((uint)((size ^ offsetInFile) & 0xFFFFFFFF));
        int pos = 0;
        for (; pos + 4 <= buffer.Length; pos += 4)
        {
            uint tmp = fileKey;
            uint xor = 0;
            for (int i = 0; i < 4; i++)
            {
                tmp = DecryptStep175(tmp);
                xor = (xor << 8) | (tmp & 0xFF);
            }
            uint word = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(pos, 4));
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.Slice(pos, 4), word ^ xor);
            fileKey++;
        }
        if (pos < buffer.Length)
        {
            uint tmp = fileKey;
            uint xor = 0;
            for (int i = 0; i < 4; i++)
            {
                tmp = DecryptStep175(tmp);
                xor = (xor << 8) | (tmp & 0xFF);
            }
            Span<byte> xb = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(xb, xor);
            for (int i = 0; pos + i < buffer.Length; i++)
                buffer[pos + i] ^= xb[i];
        }
    }

    private static uint DecryptStep175(uint key)
    {
        long a = key * 0x5E4789C9L;
        uint b = (uint)((a >> 0x2E) + (a >> 0x3F));
        uint ret = unchecked((uint)((key - b * 0xADC8u) * 0xBC8Fu + b * 0xFFFFF2B9u));
        if ((int)ret <= 0)
            ret += 0x7FFFFFFF;
        return ret;
    }
}
