# -*- coding: utf-8 -*-
"""thxxbgm 逆向分析脚本：PE 结构 / 导入表 / 字符串 / 数据文件编码"""
import struct, sys, re, io

EXE = r"E:\GitWorkspace\thworks\tools\thxxbgm\THxxBGM.exe"
data = open(EXE, "rb").read()
print("exe size:", len(data))

# --- PE header ---
pe_off = struct.unpack_from("<I", data, 0x3C)[0]
assert data[pe_off:pe_off+4] == b"PE\0\0"
machine, num_sec, ts, symtab, nsyms, opt_size, chars = struct.unpack_from("<HHIIIHH", data, pe_off+4)
opt_off = pe_off + 24
magic = struct.unpack_from("<H", data, opt_off)[0]
subsys = struct.unpack_from("<H", data, opt_off + 68)[0]
print(f"machine=0x{machine:04X} magic=0x{magic:04X} subsystem={subsys} sections={num_sec}")
print("  subsystem: 2=GUI 3=console")

sec_off = opt_off + opt_size
sections = []
for i in range(num_sec):
    off = sec_off + i*40
    name = data[off:off+8].rstrip(b"\0").decode("ascii", "replace")
    vsize, va, rsize, roff = struct.unpack_from("<IIII", data, off+8)
    sections.append((name, va, vsize, roff, rsize))
    print(f"  sec {name:<8} va=0x{va:08X} vsize=0x{vsize:06X} raw=0x{roff:08X} rsize=0x{rsize:06X}")

def rva2off(rva):
    for name, va, vsize, roff, rsize in sections:
        if va <= rva < va + max(vsize, rsize):
            return roff + (rva - va)
    return None

# --- data directories: imports & resources ---
dd_off = opt_off + (96 if magic == 0x10B else 112)
imp_rva, imp_size = struct.unpack_from("<II", data, dd_off + 8)
res_rva, res_size = struct.unpack_from("<II", data, dd_off + 16)
print(f"import dir rva=0x{imp_rva:X} size=0x{imp_size:X}")
print(f"resource dir rva=0x{res_rva:X} size=0x{res_size:X}")

# --- import table: DLL names ---
print("\n-- imported DLLs --")
if imp_rva:
    off = rva2off(imp_rva)
    while True:
        ilt, ts, fc, name_rva, iat = struct.unpack_from("<IIIII", data, off)
        if name_rva == 0:
            break
        no = rva2off(name_rva)
        end = data.index(b"\0", no)
        print("  ", data[no:end].decode("ascii", "replace"))
        off += 20

# --- link version ---
lnkmaj, lnkmin = struct.unpack_from("<BB", data, opt_off + 2)
print(f"\nlinker version: {lnkmaj}.{lnkmin}  (2.xx=VC2008, 14.x=VS2015+, 10/11=VC2010/12)")

# --- embedded strings (ASCII + UTF-16) ---
print("\n-- notable ASCII strings --")
for pat in [rb"waveOut[A-Za-z]+", rb"mci[A-Za-z]+", rb"dsound", rb"DirectSound[A-Za-z]*",
            rb"MSXML[A-Za-z0-9.]*", rb"Mfc[0-9A-Za-z]*", b"MFC", rb"vorbis[A-Za-z_]*",
            rb"ov_[a-z_]+", rb"\.fsx", rb"thbgm\.dat", rb"\.dat", rb"bgmdat",
            rb"WAVE", rb"winmm", rb"COMCTL32", rb"LoadLibrary", rb"CoCreateInstance"]:
    hits = set(m.decode() for m in re.findall(pat, data))
    if hits:
        print(f"  {pat.decode():24} -> {sorted(hits)[:8]}")

print("\n-- notable UTF-16LE strings --")
u16 = data.decode("utf-16-le", errors="ignore")
for pat in [r"thbgm\.dat", r"\.fsx", r"\.wav", r"MSXML", r"setting\.xml", r"bgm", r"Loop", r"Fade"]:
    hits = sorted(set(re.findall(pat, u16, re.I)))[:10]
    if hits:
        print(f"  {pat:18} -> {hits}")

# --- titles txt encoding ---
for enc_file in [r"E:\GitWorkspace\thworks\tools\thxxbgm\List\titles_th20.txt"]:
    b = open(enc_file, "rb").read()
    print("\ntitles file:", enc_file, "size:", len(b))
    print("  head bytes:", b[:16].hex())
    if b[:2] in (b"\xff\xfe", b"\xfe\xff"):
        print("  -> UTF-16 BOM; sample:", b[2:80].decode("utf-16-le", "replace"))
    else:
        for enc in ("utf-8", "cp932"):
            try:
                print(f"  -> {enc} sample:", b[:60].decode(enc))
                break
            except Exception:
                pass

# --- playlist pls ---
b = open(r"E:\GitWorkspace\thworks\tools\thxxbgm\playlist\NewPlaylist.pls", "rb").read()
print("\npls size:", len(b), "head:", b[:4].hex())
head = b[:300]
print("  sample:", head.decode("utf-16-le", "replace") if b[:2]==b"\xff\xfe" else head.decode("utf-8","replace").replace("\n"," | "))

# --- fsx count & size stats ---
import os
d = r"E:\GitWorkspace\thworks\tools\thxxbgm\fsx"
sizes = [(f, os.path.getsize(os.path.join(d,f))) for f in os.listdir(d)]
print("\nfsx files:", len(sizes), "total bytes:", sum(s for _,s in sizes))
