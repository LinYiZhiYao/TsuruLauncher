#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""验证目标 exe 里嵌入了 tsuru 图标（256x256 的 PNG 原样出现在资源段）。"""
import struct, sys

ICO = r'C:\Users\Linyizhi\.gemini\TsuruLauncher\Assets\tsuru-icon.ico'

d = open(ICO, 'rb').read()
cnt = struct.unpack('<H', d[4:6])[0]
png = None
off = 6
for i in range(cnt):
    w, h, a, b, c, e, size, offset = struct.unpack('<BBBBHHII', d[off:off + 16])
    off += 16
    if (w or 256) == 256 and png is None:
        png = d[offset:offset + size]

for path in sys.argv[1:]:
    try:
        exe = open(path, 'rb').read()
    except Exception as err:
        print(f'  {path}: 读不到 ({err})')
        continue
    ok = png in exe
    name = path.replace('\\', '/').split('/')[-1]
    print(f"  {'✅' if ok else '❌'} {name:<40} {len(exe):>10,} bytes  图标{'已嵌入' if ok else '未嵌入'}")
