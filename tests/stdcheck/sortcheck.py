#!/usr/bin/env python3
# sortcheck.py - the cases of sortcheck.cpp through py/cpp/stdalgo.py; output must equal sortcheck.cpp's byte for
# byte. No argument (or O): the generic StdAlgo_Sort / StdAlgo_StableSort / StdAlgo_PartialSort over a list of
# structs. With D, I or L: the same permutations from the specialisations (StdAlgo_Sort__I over the ids, ordered by
# their keys, and so on), which must give the same output too.
# Written by Claude (Anthropic) for the SWMM6 ports; see AI-USE.md.
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', 'py'))
import cpp  # noqa: E402

M64 = 0xFFFFFFFFFFFFFFFF
_s = 1729


def rnd():
    global _s
    s = _s
    s ^= (s << 13) & M64
    s ^= s >> 7
    s ^= (s << 17) & M64
    _s = s
    return s


class E:
    __slots__ = ('key', 'id')

    def __init__(self, key, id_):
        self.key = key
        self.id = id_


def run_o(key, n, which):
    a = [E(key[i], i) for i in range(n)]
    lt = lambda x, y: x.key < y.key
    if which == 0:
        cpp.StdAlgo_Sort(a, 0, n, lt)
    elif which == 1:
        cpp.StdAlgo_StableSort(a, 0, n, lt)
    else:
        cpp.StdAlgo_PartialSort(a, 0, n, n, lt)
    return [e.id for e in a]


def run_prim(code, conv):
    sort = getattr(cpp, 'StdAlgo_Sort__' + code)
    stable = getattr(cpp, 'StdAlgo_StableSort__' + code)
    partial = getattr(cpp, 'StdAlgo_PartialSort__' + code)

    def run(key, n, which):
        a = [conv(i) for i in range(n)]
        lt = lambda x, y: key[int(x)] < key[int(y)]
        if which == 0:
            sort(a, 0, n, lt)
        elif which == 1:
            stable(a, 0, n, lt)
        else:
            partial(a, 0, n, n, lt)
        return [int(x) for x in a]
    return run


def main():
    mode = sys.argv[1] if len(sys.argv) > 1 else 'O'
    if mode == 'O':
        run = run_o
    elif mode == 'I':
        run = run_prim('I', int)
    elif mode == 'L':
        run = run_prim('L', int)
    elif mode == 'D':
        run = run_prim('D', float)
    else:
        sys.exit('mode: O, D, I or L')
    ns = list(range(0, 301)) + [511, 512, 513, 1000, 4096, 10007]
    out = []
    for n in ns:
        for pat in range(7):
            key = [0] * n
            for i in range(n):
                if pat == 0:
                    k = rnd() % 5                       # many ties
                elif pat == 1:
                    k = rnd() % 1000000                 # few ties
                elif pat == 2:
                    k = i // 3                          # sorted with runs
                elif pat == 3:
                    k = (n - i) // 2                    # reversed with runs
                elif pat == 4:
                    k = 7                               # all equal
                elif pat == 5:
                    k = i if i < n // 2 else n - i      # organ pipe
                else:
                    k = (i * 7919) % 31                 # sawtooth
                key[i] = k
            for tag, which in (('sort', 0), ('stable', 1), ('heap', 2)):
                ids = run(key, n, which)
                out.append('%s %d %d:%s\n' % (tag, n, pat, ''.join(' %d' % x for x in ids)))
    sys.stdout.write(''.join(out))


if __name__ == '__main__':
    main()
