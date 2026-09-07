// Mt19937.cs — 移植自 thtk v12 thtk/rng_mt.c（E:/GitWorkspace/thworks/tools/thtk/thtk/rng_mt.c）
// 标准 MT19937（Matsumoto & Nishimura 1998），与 thtk 实现逐行对应：
//   rng_mt_init   ↔ Init     （init_genrand 型种子初始化）
//   rng_mt_nextint ↔ NextUint32（twist + tempering）
// 注意点：
//   1. mt[0] = seed（不做 1812433253 的 init_genrand 变换，thtk 用的是原始 init_genrand 算法）。
//   2. mti 初始 = 624，首次取数时才 twist（惰性生成），与 thtk 一致。
//   3. tempering：y ^= y>>11; y ^= (y<<7)&0x9d2c5680; y ^= (y<<15)&0xefc60000; y ^= y>>18。
// 全部运算按 uint32 环回（unchecked）。

namespace addons.XorReader;

public sealed class Mt19937
{
    private const int N = 624;
    private const int M = 397;
    private const uint UpperMask = 0x80000000u;
    private const uint LowerMask = 0x7fffffffu;

    private readonly uint[] _mt = new uint[N];
    private int _mti;

    public Mt19937(uint seed)
    {
        Init(seed);
    }

    private void Init(uint seed)
    {
        _mt[0] = seed;
        for (int i = 1; i < N; i++)
        {
            unchecked
            {
                _mt[i] = 0x6c078965u * (_mt[i - 1] ^ (_mt[i - 1] >> 30)) + (uint)i;
            }
        }
        _mti = N;
    }

    public uint NextUint32()
    {
        unchecked
        {
            if (_mti >= N)
            {
                uint t;
                int i;
                for (i = 0; i < N - M; i++)
                {
                    t = (_mt[i] & UpperMask) | (_mt[i + 1] & LowerMask);
                    _mt[i] = _mt[i + M] ^ (t >> 1) ^ ((t & 1) != 0 ? 0x9908b0dfu : 0u);
                }
                for (; i < N - 1; i++)
                {
                    t = (_mt[i] & UpperMask) | (_mt[i + 1] & LowerMask);
                    _mt[i] = _mt[i + (M - N)] ^ (t >> 1) ^ ((t & 1) != 0 ? 0x9908b0dfu : 0u);
                }
                t = (_mt[N - 1] & UpperMask) | (_mt[0] & LowerMask);
                _mt[N - 1] = _mt[M - 1] ^ (t >> 1) ^ ((t & 1) != 0 ? 0x9908b0dfu : 0u);

                _mti = 0;
            }

            uint ret = _mt[_mti++];

            ret ^= ret >> 11;
            ret ^= (ret << 7) & 0x9d2c5680u;
            ret ^= (ret << 15) & 0xefc60000u;
            ret ^= ret >> 18;

            return ret;
        }
    }
}
