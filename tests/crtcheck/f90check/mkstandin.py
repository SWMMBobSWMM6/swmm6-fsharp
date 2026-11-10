#!/usr/bin/env python3
"""Make a stand-in for part A of the Fortran runtime (module cpp_rt_a) from its stub, for tests/crtcheck/f90check
while the real part A (f90/runtime/cpp_rt_a.f90) does not exist:

    mkstandin.py OUT.f90 [STUB.f90]

The stub (f90/runtime/stub/cpp_rt_a.f90) has every type as the engine sees it and empty procedure bodies. The
stand-in gives cpp_ptr_<c> the fields of C#'s Ptr<T> (a, the array; o, the offset; ob and at for a Ptr to a field,
where the manifest has a RefAt type), which is what part C's submodule ptr assumes, and fills in the bodies part C
and the checker call: cpp_ptr_<c>_item_i / _new_ta_l, cpp_ptr_d_op_add_ptrt_l, cpp_ptr_l_new_o_refatt and
cpp_cppexception_what (an empty string). Everything else stays a stub.
Written by Claude (Anthropic) for the SWMM6 ports; see AI-USE.md."""
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, '..', '..', '..'))

ELEM = {'b': 'integer(1)', 'd': 'real(8)', 'f': 'real(4)', 'i': 'integer(4)', 'l': 'integer(8)', 's': 'integer(2)',
        'z': 'logical', 'o': 'type(cpp_slot)'}
REFAT = {'d', 'i', 'l', 'o'}


def ptr_type(c):
    lines = ['  type :: cpp_ptr_%s' % c,
             '    class(cpp_arr_%s), pointer :: a => null()' % c,
             '    integer(8) :: o = 0_8']
    if c in REFAT:
        lines += ['    class(*), pointer :: ob => null()',
                  '    class(cpp_refat_%s), pointer :: at => null()' % c]
    lines.append('  end type cpp_ptr_%s' % c)
    return lines


def bodies():
    b = {}
    for c in ELEM:
        if c in REFAT:
            b['cpp_ptr_%s_item_i' % c] = ['    if (associated(self%at)) then',
                                          '      zres => self%at%invoke(self%ob)',
                                          '    else',
                                          '      zres => self%a%a(self%o + a1)',
                                          '    end if']
        else:
            b['cpp_ptr_%s_item_i' % c] = ['    zres => self%a%a(self%o + a1)']
        b['cpp_ptr_%s_new_ta_l' % c] = ['    zres%a => a1', '    zres%o = a2']
    b['cpp_ptr_d_op_add_ptrt_l'] = ['    zres = a1', '    zres%o = a1%o + a2']
    b['cpp_ptr_l_new_o_refatt'] = ['    zres%ob => a1', '    zres%at => a2']
    b['cpp_cppexception_what'] = ['    zres%b => null()', '    zres%o = 0']
    return b


def main():
    out = sys.argv[1]
    stub = sys.argv[2] if len(sys.argv) > 2 else os.path.join(ROOT, 'f90', 'runtime', 'stub', 'cpp_rt_a.f90')
    src = open(stub).read().split('\n')
    res = []
    fill = bodies()
    done = set()
    i = 0
    while i < len(src):
        l = src[i]
        m = re.match(r'\s*type\s*::\s*cpp_ptr_([a-z])\s*$', l)
        if m and m.group(1) in ELEM:
            j = i
            while not re.match(r'\s*end type', src[j]):
                j += 1
            res += ptr_type(m.group(1))
            i = j + 1
            continue
        m = re.match(r'\s*(?:recursive\s+)?function\s+(\w+)\s*\(', l)
        if m and m.group(1) in fill:
            name = m.group(1)
            j = i
            while not re.match(r'\s*end function', src[j]):
                j += 1
            res += src[i:j] + fill[name] + [src[j]]
            done.add(name)
            i = j + 1
            continue
        res.append(l)
        i += 1
    missing = set(fill) - done
    if missing:
        sys.exit('mkstandin.py: not in the stub: ' + ', '.join(sorted(missing)))
    res[0] = '! cpp_rt_a - STAND-IN of part A for tests/crtcheck/f90check (made by mkstandin.py from the stub: Ptr fields and the' \
             ' few bodies part C uses). ' + res[0].lstrip('! ')
    open(out, 'w').write('\n'.join(res))


if __name__ == '__main__':
    main()
