#!/usr/bin/env python3
"""extract_tables.py - read the constant tables of glibc's math routines out of the compiled objects.

  extract_tables.py BUILD_DIR OUT.json

No code from glibc is run: the objects are only compiled (build_glibm.sh), and the bytes of each table
symbol are copied out of the object file's .rodata with objdump/objcopy. The layouts below follow the
struct definitions in glibc 2.39's sysdeps/ieee754/dbl-64/math_config.h and the table files; every
symbol's size is checked against the layout before anything is used.
Written by Claude (Anthropic); see AI-USE.md.
"""
import json, os, struct, subprocess, sys, tempfile

B, OUT = sys.argv[1], sys.argv[2]


def symbols(obj):
    r = subprocess.run(['objdump', '-t', obj], capture_output=True, text=True, check=True).stdout
    syms = {}
    for line in r.split('\n'):
        p = line.split()
        if len(p) >= 6 and p[-4 if len(p) == 6 else -4].startswith('.rodata'):
            pass
        if len(p) >= 5 and ' O ' in line and '.rodata' in line:
            name, size, sec, val = p[-1], int(p[-2], 16), p[-3], int(p[0], 16)
            syms[name.split('.')[0]] = (sec, val, size)
    return syms


def section(obj, sec):
    with tempfile.TemporaryDirectory() as td:
        o = os.path.join(td, 's.bin')
        subprocess.run(['objcopy', '-O', 'binary', '--only-section=' + sec, obj, o], check=True)
        return open(o, 'rb').read()


# (object, symbol, expected size, {key: (kind, offset, stride, count or dims)})
LAYOUT = [
    ('e_exp_data.o', '__exp_data', 0x8b0, {
        '__exp_data.invln2N': ('f64', 0, 0, None), '__exp_data.negln2hiN': ('f64', 8, 0, None),
        '__exp_data.negln2loN': ('f64', 16, 0, None), '__exp_data.poly[]': ('f64', 24, 8, 4),
        '__exp_data.shift': ('f64', 56, 0, None), '__exp_data.tab[]': ('u64', 176, 8, 256)}),
    ('e_log_data.o', '__log_data', 0x1090, {
        '__log_data.ln2hi': ('f64', 0, 0, None), '__log_data.ln2lo': ('f64', 8, 0, None),
        '__log_data.poly[]': ('f64', 16, 8, 5), '__log_data.poly1[]': ('f64', 56, 8, 11),
        '__log_data.tab[].invc': ('f64', 144, 16, 128), '__log_data.tab[].logc': ('f64', 152, 16, 128)}),
    ('e_pow_log_data.o', '__pow_log_data', 0x1048, {
        '__pow_log_data.ln2hi': ('f64', 0, 0, None), '__pow_log_data.ln2lo': ('f64', 8, 0, None),
        '__pow_log_data.poly[]': ('f64', 16, 8, 7), '__pow_log_data.tab[].invc': ('f64', 72, 32, 128),
        '__pow_log_data.tab[].logc': ('f64', 88, 32, 128), '__pow_log_data.tab[].logctail': ('f64', 96, 32, 128)}),
    ('sincostab.o', '__sincostab', 0xdc0, {'__sincostab.x[]': ('f64', 0, 8, 440)}),
    ('s_tan.o', 'xfg', 0x1740, {'xfg[][].d': ('f64', 0, 8, (186, 4))}),
    ('e_asin.o', 'asncs', None, {'asncs.x[]': ('f64', 0, 8, 'all')}),
    ('e_asin.o', 'inroot', 0x400, {'inroot[]': ('f64', 0, 8, 128)}),
    ('e_asin.o', 'powtwo', 0xe0, {'powtwo[]': ('f64', 0, 8, 28)}),
    ('s_atan.o', 'cij', 0x34b8, {'cij[][].d': ('f64', 0, 8, (241, 7))}),
    ('branred.o', 'toverp', 0x258, {'toverp[]': ('f64', 0, 8, 75)}),
    # set 2 (glibm2.*). __log2_data as e_log2_data.c is compiled (generic, so with tab2, which the FMA log2 does
    # not read): invln2hi, invln2lo, poly[6], poly1[10], tab[64] {invc, logc}, tab2[64] {chi, clo}
    ('e_log2_data.o', '__log2_data', 0x890, {
        '__log2_data.invln2hi': ('f64', 0, 0, None), '__log2_data.invln2lo': ('f64', 8, 0, None),
        '__log2_data.poly[]': ('f64', 16, 8, 6), '__log2_data.poly1[]': ('f64', 64, 8, 10),
        '__log2_data.tab[].invc': ('f64', 144, 16, 64), '__log2_data.tab[].logc': ('f64', 152, 16, 64)}),
    ('e_atan2.o', 'cij', 0x34b8, {'cij[][].d': ('f64', 0, 8, (241, 7))}),     # uatan.tbl, as in s_atan.o
    ('s_cbrt.o', 'factor', 0x28, {'factor[]': ('f64', 0, 8, 5)}),
]

res = {}
for obj, sym, size, keys in LAYOUT:
    path = os.path.join(B, obj)
    if not os.path.exists(path):
        continue
    syms = symbols(path)
    sec, val, sz = syms[sym]
    if size is not None and sz != size:
        sys.exit('%s in %s: size %#x, layout expects %#x' % (sym, obj, sz, size))
    data = section(path, sec)[val:val + sz]
    for key, (kind, off, stride, count) in keys.items():
        if count is None:
            res[key] = dict(kind=kind, dims=[], data=['%016x' % struct.unpack_from('<Q', data, off)[0]])
            continue
        if count == 'all':
            count = sz // 8
        dims = list(count) if isinstance(count, tuple) else [count]
        n = 1
        for d in dims:
            n *= d
        vals = ['%016x' % struct.unpack_from('<Q', data, off + i * stride)[0] for i in range(n)]
        if key in res and res[key]['data'] != vals:   # cij is in s_atan.o and e_atan2.o (both include uatan.tbl)
            sys.exit('%s differs between objects' % key)
        res[key] = dict(kind=kind, dims=dims, data=vals)
json.dump(res, open(OUT, 'w'))
print('tables:', ', '.join('%s %s%s' % (k, v['kind'], v['dims']) for k, v in sorted(res.items())))
