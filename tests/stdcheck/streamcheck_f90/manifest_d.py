#!/usr/bin/env python3
"""manifest_d.py - part D's coverage of the manifest: every row of f90/runtime_parts.txt marked D must be in
f90/runtime/cpp_rt_d.f90, a TYPE with the stub's parent type, a procedure with the stub's dummy arguments and
result declared exactly as f90/runtime/stub/cpp_rt_d.f90 (made from f90/cs2f90_runtime.txt) declares them, and
public. Prints the misses; exit status 1 if there are any.
Written by Claude (Anthropic) for the SWMM6 ports; see AI-USE.md."""
import os, re, sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..', '..'))
F90 = os.path.join(ROOT, 'f90')


def procs(text):
    """name -> (kind, args, sorted declaration lines) of every top-level procedure of a module."""
    out = {}
    lines = text.split('\n')
    i = 0
    while i < len(lines):
        m = re.match(r'\s*recursive\s+(?:pure\s+)?(function|subroutine)\s+(\w+)\s*\(([^)]*)\)', lines[i])
        if m:
            kind, name, args = m.group(1), m.group(2).lower(), [a.strip().lower() for a in m.group(3).split(',') if a.strip()]
            want = set(args) | ({'zres'} if kind == 'function' else set())
            decl = []
            j = i + 1
            while j < len(lines) and not re.match(r'\s*end\s+(function|subroutine)', lines[j]):
                d = re.match(r'\s*([^!:]+?)\s*::\s*(.+)$', lines[j])
                if d:
                    names = [n.strip().split('=')[0].strip().lower() for n in d.group(2).split(',')]
                    for n in names:
                        if n in want:
                            decl.append(re.sub(r'\s+', ' ', d.group(1).strip().lower()) + ' :: ' + n)
                j += 1
            out[name] = (kind, args, sorted(decl))
            i = j
        i += 1
    return out


def types(text):
    return {m.group(2).lower(): (m.group(1) or '').lower()
            for m in re.finditer(r'^\s*type\s*(?:,\s*extends\((\w+)\))?(?:,\s*abstract)?\s*::\s*(\w+)', text, re.M)}


def main():
    rows = [l.split('\t') for l in open(os.path.join(F90, 'runtime_parts.txt')) if not l.startswith('#') and l.strip()]
    mine_rows = [r for r in rows if r[1] == 'D']
    stub = open(os.path.join(F90, 'runtime', 'stub', 'cpp_rt_d.f90')).read()
    real = open(os.path.join(F90, 'runtime', 'cpp_rt_d.f90')).read()
    sp, rp, st, rt = procs(stub), procs(real), types(stub), types(real)
    pub = set()
    for m in re.finditer(r'^\s*public\s*::\s*((?:[^\n]*&\s*\n)*[^\n]*)', real, re.M):
        pub |= {x.strip().lower() for x in m.group(1).replace('&', ' ').replace('\n', ' ').split(',') if x.strip()}
    miss = []
    for name, part, kind, cs in [(r[0].lower(), r[1], r[2], r[3].strip()) for r in mine_rows]:
        if kind == 'TYPE':
            if name not in rt:
                miss.append(name + ': type missing')
            elif rt[name] != st.get(name):
                miss.append(name + ': extends %s, the stub %s' % (rt[name], st.get(name)))
        else:
            if name not in rp:
                miss.append(name + ': procedure missing')
            elif rp[name] != sp.get(name):
                miss.append(name + ': declared %r, the stub %r' % (rp[name], sp.get(name)))
        if name not in pub:
            miss.append(name + ': not public')
    print('part D rows: %d (%d types, %d procedures); misses: %d' % (
        len(mine_rows), sum(r[2] == 'TYPE' for r in mine_rows), sum(r[2] != 'TYPE' for r in mine_rows), len(miss)))
    for m in miss:
        print('  ' + m)
    return 1 if miss else 0


if __name__ == '__main__':
    sys.exit(main())
