#!/usr/bin/env python3
# hashcheck.py - the operations of hashcheck.cpp through the Python runtime's hash tables (py/cpp/containers.py:
# UMap, USet, HashTable, the rehash policy, std::hash); output must equal hashcheck.cpp's: bucket counts and
# iteration order after inserts, operator[], erases, reserve and copies, and std::hash of strings.
# Written by Claude (Anthropic) for the SWMM6 ports; see AI-USE.md.
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', 'py'))
import cpp  # noqa: E402

M64 = 0xFFFFFFFFFFFFFFFF
_s = 52024
PRE = ('J', 'C', 'N', 'OF', 'P', 'RG', 'SUB_', 'Link-', 'node.', 'W')


def rnd():
    global _s
    s = _s
    s ^= (s << 13) & M64
    s ^= s >> 7
    s ^= (s << 17) & M64
    _s = s
    return s


def mkstr(x):
    """A Str from Python text, through its public fields b and n."""
    r = cpp.NewStr()
    e = x.encode('latin-1')
    r.b = bytearray(e) + b'\0'
    r.n = len(e)
    return r


def text(s):
    return bytes(s.b[:s.n]).decode('latin-1')


def name(r):
    x = PRE[r % 10] + str((r >> 8) % 5000)
    if (r >> 20) % 3 == 0:
        x += '_' + str((r >> 24) % 97)
    return mkstr(x)


def main():
    out = []
    for rnd_ in range(40):
        m = cpp.NewUMap_FuncKul_FuncKKb_FuncV(None, None, cpp.MK_I)
        mi = cpp.NewUMap_FuncKul_FuncKKb_FuncV(None, None, cpp.MK_I)
        st = cpp.NewUSet_FuncTul_FuncTTb(None, None)
        n = 1 + rnd() % (60 if rnd_ < 20 else 3000)
        if rnd_ % 7 == 3:
            m.reserve(n)
        for i in range(n):
            r = rnd()
            k = name(r)
            if r % 5 == 0:
                v = m.ref_Index(k)
                v.v = v.v + i
            else:
                m.emplace_K_V(k, i)
            mi.emplace_K_V(r % 20000 - 10000, i)
            st.insert(k)
            if r % 11 == 0 and not m.empty():
                m.erase_K(name(rnd()))
            if r % 13 == 0:
                mi.erase_K(rnd() % 20000 - 10000)
            if r % 17 == 0 and not m.empty():
                m.erase_MItE(m.begin())
        c = m.Clone()
        out.append('map %d: %d buckets %d:' % (rnd_, m.bucket_count(), m.size()))
        out.extend(' %s=%d' % (text(kv.first), kv.second) for kv in m.Items())
        out.append('\ncopy:')
        out.extend(' ' + text(kv.first) for kv in c.Items())
        out.append('\nimap %d %d:' % (mi.bucket_count(), mi.size()))
        out.extend(' %d=%d' % (kv.first, kv.second) for kv in mi.Items())
        out.append('\nset %d:' % st.bucket_count())
        out.extend(' ' + text(k) for k in st.Items())
        out.append('\n')
    for w in ('', 'a', 'abcdefg', 'abcdefgh', 'abcdefghi', 'Junction_17', 'OUTFALL-1'):
        out.append('hash %s %d\n' % (w, cpp.StdHash_Of_Str(mkstr(w))))
    sys.stdout.write(''.join(out))


if __name__ == '__main__':
    main()
