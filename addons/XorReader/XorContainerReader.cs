// XorContainerReader.cs — 移植自 thtk v12 thtk/thdat105.c + thtk/thcrypt105.c
// （E:/GitWorkspace/thworks/tools/thtk/thtk/thdat105.c 的 th105_open / th105_read / th105_data_crypt）
//
// 容器布局（th105b.dat / th123b.dat，thtk 版本号 105 / 123，共用同一套算法）：
//
//   [0..2)  uint16 LE  条目数 entry_count
//   [2..6)  uint32 LE  表头大小 header_size
//   [6..6+header_size)  加密条目表
//   [6+header_size..)   条目数据（连续存放）
//
// 条目表每项：uint32 offset（数据区绝对偏移，从文件头算起）
//              uint32 size
//              uint8  name_length
//              byte[name_length] name（路径名，'/' 分隔）
//
// 两层解密（顺序对解密而言从外到内，加密时相反；两层都各自独立、可交换）：
//   第 1 层 th_crypt105_list：MT19937 流式 XOR —— 以 seed = 6 + header_size 初始化 MT，
//            逐字节 data[i] ^= rng.NextUint32() & 0xff。
//   第 2 层 th_crypt75_list：滚动密钥 XOR —— key=0xc5 起，逐字节 data[i] ^= key;
//            key += 0x83; step1(0x83) += 0x53（均按 byte 环回）。
//   （thtk 中 version==105105 的档案跳过第 2 层；th105/th123 均应用第 2 层。）
//
// 条目数据解密 th_crypt105_file：单字节常量 XOR，
//   key = ((offset >> 1) | 0x23) & 0xff，对整段条目数据用同一个 key。
//   （0x23 = THCRYPT_PATCHCON_KEY，th105/th123 专用；0x08 是 Megamari。）
//
// 解密是自逆的（XOR），加密/解密同一函数。

using System.Buffers.Binary;
using System.Text;

namespace addons.XorReader;

public sealed record XorContainerEntry(uint Offset, uint Size, string Name);

public static class XorContainerReader
{
    public const byte PatchconKey = 0x23;

    /// <summary>解析容器并解出全部条目。整容器可用（内存）时最直接。</summary>
    public static IDictionary<string, byte[]> ReadAll(byte[] container)
    {
        using var ms = new MemoryStream(container, writable: false);
        return ReadAll(ms);
    }

    /// <summary>解析容器并解出全部条目（从流读，不落盘）。</summary>
    public static IDictionary<string, byte[]> ReadAll(Stream stream)
    {
        var entries = ReadEntries(stream);

        var result = new Dictionary<string, byte[]>(entries.Count, StringComparer.Ordinal);
        foreach (var e in entries)
        {
            stream.Seek(e.Offset, SeekOrigin.Begin);
            var data = new byte[e.Size];
            int read = 0;
            while (read < data.Length)
            {
                int n = stream.Read(data, read, data.Length - read);
                if (n <= 0)
                    throw new InvalidDataException($"条目 {e.Name} 数据不完整：offset={e.Offset} size={e.Size}");
                read += n;
            }
            DecryptFileData(data, e.Offset);
            result.Add(e.Name, data);
        }
        return result;
    }

    /// <summary>只解析条目表（offset/size/name），不解密数据。</summary>
    public static IReadOnlyList<XorContainerEntry> ReadEntries(Stream stream)
    {
        using var br = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

        ushort entryCount = br.ReadUInt16();
        uint headerSize = br.ReadUInt32();

        byte[] header = br.ReadBytes((int)headerSize);
        if (header.Length != headerSize)
            throw new InvalidDataException("条目表被截断");

        // 第 1 层：MT19937 流式 XOR，种子 = 6 + header_size
        DecryptHeaderMt(header, 6u + headerSize);
        // 第 2 层：滚动密钥 XOR（0xc5, 0x83, 0x53）
        DecryptHeaderRolling(header, 0xc5, 0x83, 0x53);

        var entries = new List<XorContainerEntry>(entryCount);
        int pos = 0;
        for (int i = 0; i < entryCount; i++)
        {
            uint offset = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(pos));
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(pos + 4));
            byte nameLength = header[pos + 8];
            string name = Encoding.Latin1.GetString(header, pos + 9, nameLength);
            pos += 9 + nameLength;
            entries.Add(new XorContainerEntry(offset, size, name.Replace('\\', '/')));
        }
        return entries;
    }

    /// <summary>解密单个条目数据：key = ((offset&gt;&gt;1) | orKey) &amp; 0xff，常量 XOR 整段。</summary>
    public static void DecryptFileData(byte[] data, uint offset, byte orKey = PatchconKey)
    {
        byte key = (byte)(((offset >> 1) | orKey) & 0xff);
        for (int i = 0; i < data.Length; i++)
            data[i] ^= key;
    }

    /// <summary>th_crypt105_list：MT19937 XOR 流。</summary>
    private static void DecryptHeaderMt(byte[] data, uint seed)
    {
        var rng = new Mt19937(seed);
        for (int i = 0; i < data.Length; i++)
            data[i] ^= (byte)(rng.NextUint32() & 0xff);
    }

    /// <summary>th_crypt75_list：滚动密钥 XOR。key 逐字节 +step1，step1 逐字节 +step2。</summary>
    private static void DecryptHeaderRolling(byte[] data, byte key, byte step1, byte step2)
    {
        unchecked
        {
            for (int i = 0; i < data.Length; i++)
            {
                data[i] ^= key;
                key += step1;
                step1 += step2;
            }
        }
    }
}
