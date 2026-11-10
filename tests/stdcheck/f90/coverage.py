#!/usr/bin/env python3
# coverage.py - manifest coverage of part B of the Fortran runtime: every B row of f90/runtime_parts.txt is public
# in f90/runtime/cpp_rt_b.f90 (types, the Less<T>.Fn variables, procedures), each procedure (in the module or in the
# submodule's interface block) has the stub's kind, dummy names, and dummy and result declarations, and the stub's
# type components are kept. Prints the problems and a count; exits 1 if there are any.
# Written by Claude (Anthropic) for the SWMM6 ports; see AI-USE.md.
import os
import sys
import re
ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', '..', 'f90')
rows = [l.rstrip('\n').split('\t') for l in open(ROOT + '/runtime_parts.txt') if not l.startswith('#')]
brows = [r for r in rows if r[1] == 'B']
def procs(text):
    out = {}
    lines = text.split('\n')
    i = 0
    while i < len(lines):
        m = re.match(r'^\s*recursive (module )?(function|subroutine) (\w+)\(([^)]*)\)( result\((\w+)\))?', lines[i])
        if m:
            name = m.group(3); args = [a.strip() for a in m.group(4).split(',') if a.strip()]
            res = m.group(6)
            decl = {}
            j = i + 1
            while j < len(lines) and not re.match(r'^\s*end (function|subroutine)', lines[j]) and not re.match(r'^\s*(recursive|contains)\b', lines[j]):
                d = re.match(r'^\s*(.*?)\s*::\s*(.*)$', lines[j])
                if d:
                    for n in [x.strip().split('=')[0].strip() for x in re.split(r',(?![^(]*\))', d.group(2))]:
                        decl[n.lower()] = d.group(1).replace(' ', '').lower()
                j += 1
            out[name] = (m.group(2), args, res, {a.lower(): decl.get(a.lower()) for a in args + ([res] if res else [])})
            i = j
        i += 1
    return out
stub = procs(open(ROOT + '/runtime/stub/cpp_rt_b.f90').read())
mine_text = open(ROOT + '/runtime/cpp_rt_b.f90').read()
mine = procs(mine_text)
pub = set()
for m in re.finditer(r'^\s*public ::(.*(?:&\n.*)*)', mine_text, re.M):
    pub |= {x.strip().lstrip('&').strip() for x in m.group(1).replace('&\n', ',').split(',') if x.strip()}
types = set(re.findall(r'^\s*type(?:, [^:]*)? :: (\w+)\s*$', mine_text, re.M))
vars_ = set(re.findall(r'^\s*class\(\w+\), pointer :: (\w+) =>', mine_text, re.M))
bad = 0
for name, part, kind, cs in brows:
    if name not in pub:
        print('NOT PUBLIC', name); bad += 1
    if kind == 'TYPE':
        if name not in types: print('NO TYPE', name); bad += 1
    elif kind == 'VARIABLE':
        if name not in vars_: print('NO VARIABLE', name); bad += 1
    else:
        if name not in mine: print('NO PROC', name); bad += 1; continue
        if name not in stub: print('NOT IN STUB', name); bad += 1; continue
        a, b = stub[name], mine[name]
        if a[0] != b[0] or a[1] != b[1] or (a[2] is None) != (b[2] is None) or a[3] != b[3]:
            print('SIGNATURE', name, a, b); bad += 1
print(len(brows), 'B rows;', sum(1 for r in brows if r[2] == 'TYPE'), 'types,', sum(1 for r in brows if r[2] == 'VARIABLE'), 'variables,',
      sum(1 for r in brows if r[2] in ('FUNCTION', 'SUBROUTINE')), 'procedures;', bad, 'problems')
# stub type components kept
for t, body in re.findall(r'^  type(?:, [^:]*)? :: (\w+)\n(.*?)^  end type', open(ROOT + '/runtime/stub/cpp_rt_b.f90').read(), re.M | re.S):
    for c in re.findall(r'^\s*(\w[^:]*::\s*\w+)', body, re.M):
        if c.replace(' ', '') not in mine_text.replace(' ', ''):
            print('STUB COMPONENT MISSING', t, c)
            bad += 1
sys.exit(1 if bad else 0)
