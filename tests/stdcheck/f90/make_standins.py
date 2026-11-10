#!/usr/bin/env python3
# make_standins.py OUTDIR - test-local stand-ins for parts A and C of the Fortran runtime, for running the part B
# checks (sortcheck, hashcheck, containercheck) before the real parts exist. Each is its stub
# (f90/runtime/stub/cpp_rt_{a,c}.f90) with the few components and bodies part B and these tests call: the a / i of
# cpp_iter_b / f / z and the a / o of cpp_ptr_d (as part A's generator declares them), Pair's constructor, Str's
# clone, the Iter / Ptr operator + used for results, Vec and array constructors; C's TupN.CompareTo (unused here).
# run.sh uses the real modules instead when they exist and compile.
# Written by Claude (Anthropic) for the SWMM6 ports; see AI-USE.md.
import os
import re
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..', '..'))
STUB = os.path.join(ROOT, 'f90', 'runtime', 'stub')
out = sys.argv[1]
os.makedirs(out, exist_ok=True)


def patch(src, types, bodies, extra):
    s = src
    for name, comps in types.items():
        m = re.search(r'(  type(, [^\n]*)? :: %s\n)(.*?)(  end type %s\n)' % (name, name), s, re.S)
        if not m:
            sys.exit('no type ' + name)
        s = s[:m.start(3)] + comps + s[m.end(3):]
    for name, body in bodies.items():
        m = re.search(r'(  recursive (function|subroutine) %s\(.*?\n)(  end (function|subroutine)\n)' % name, s, re.S)
        if not m:
            sys.exit('no procedure ' + name)
        s = s[:m.start(3)] + body + s[m.start(3):]
    s = s.replace('\ncontains\n', '\ncontains\n' + extra, 1)
    return s


a = open(os.path.join(STUB, 'cpp_rt_a.f90')).read()
types = {}
for k, t in (('b', 'cpp_arr_b'), ('f', 'cpp_arr_f'), ('z', 'cpp_arr_z')):
    types['cpp_iter_' + k] = ('    class(%s), pointer :: a => null()\n    integer(8) :: i = 0_8\n' % t)
types['cpp_ptr_d'] = ('    integer(4) :: cpp_tag = 0\n    class(cpp_arr_d), pointer :: a => null()\n'
                      '    integer(8) :: o = 0_8\n')
types['cpp_str'] = ('    class(cpp_arr_b), pointer :: b => null()\n    integer(4) :: n = 0\n'
                    '  contains\n    procedure :: clone => sa_str_clone\n')
bodies = {
    'cpp_pair_new_a_b': '    allocate(zres)\n    zres%first%p => a1\n    zres%second%p => a2\n',
    'cpp_str_clone': '    allocate(zres)\n    zres%n = self%n\n    allocate(zres%b)\n    allocate(zres%b%a(0:max(self%n, 0)))\n'
                     '    zres%b%a = 0_1\n    if (self%n > 0) zres%b%a(0:self%n - 1) = self%b%a(0:self%n - 1)\n',
    'cpp_iter_b_op_add': '    zres = a1\n    zres%i = a1%i + a2\n',
    'cpp_iter_d_op_add': '    zres = a1\n    zres%i = a1%i + a2\n',
    'cpp_iter_i_op_add': '    zres = a1\n    zres%i = a1%i + a2\n',
    'cpp_iter_o_op_add': '    zres = a1\n    zres%i = a1%i + a2\n',
    'cpp_ptr_d_op_add_ptrt_l': '    zres = a1\n    zres%o = a1%o + a2\n',
    'cpp_vec_d_new_0': '    allocate(zres)\n    zres%a => cpp_arr_d_new(0_8)\n',
    'cpp_vec_i_new_0': '    allocate(zres)\n    zres%a => cpp_arr_i_new(0_8)\n',
    'cpp_vec_o_new_0': '    allocate(zres)\n    zres%a => cpp_arr_o_new(0_8)\n',
    'cpp_arr_b_new': '    allocate(zres)\n    allocate(zres%a(0:a1 - 1_8))\n    zres%a = 0_1\n',
    'cpp_arr_d_new': '    allocate(zres)\n    allocate(zres%a(0:a1 - 1_8))\n    zres%a = 0.0_8\n',
    'cpp_arr_i_new': '    allocate(zres)\n    allocate(zres%a(0:a1 - 1_8))\n    zres%a = 0\n',
    'cpp_arr_l_new': '    allocate(zres)\n    allocate(zres%a(0:a1 - 1_8))\n    zres%a = 0_8\n',
    'cpp_arr_o_new': '    allocate(zres)\n    allocate(zres%a(0:a1 - 1_8))\n',
    'cpp_arr_f_new': '    allocate(zres)\n    allocate(zres%a(0:a1 - 1_8))\n    zres%a = 0.0_4\n',
    'cpp_arr_z_new': '    allocate(zres)\n    allocate(zres%a(0:a1 - 1_8))\n    zres%a = .false.\n',
    'cpp_iter_d_op_sub_itert_itert': '    zres = a1%i - a2%i\n',
}
extra = ('  recursive function sa_str_clone(self) result(zres)\n'
         '    class(cpp_str), intent(inout), target :: self\n'
         '    class(cpp_object), pointer :: zres\n'
         '    class(cpp_str), pointer :: s\n'
         '    s => cpp_str_clone(self)\n'
         '    zres => s\n'
         '  end function\n')
a = patch(a, types, bodies, extra)
a = a.replace('! cpp_rt_a - STUB', '! cpp_rt_a - TEST STAND-IN (tests/stdcheck/f90/make_standins.py) made from the STUB', 1)
open(os.path.join(out, 'cpp_rt_a.f90'), 'w').write(a)

c = open(os.path.join(STUB, 'cpp_rt_c.f90')).read()
c = c.replace('\ncontains\n', '\ncontains\n'
              '  recursive function cpp_tupn_compareto(a, b) result(zres)\n'
              '    class(cpp_tupn), intent(in) :: a, b\n'
              '    integer(4) :: zres\n'
              '    zres = 0\n'
              '  end function\n', 1)
c = c.replace('! cpp_rt_c - STUB', '! cpp_rt_c - TEST STAND-IN (tests/stdcheck/f90/make_standins.py) made from the STUB', 1)
open(os.path.join(out, 'cpp_rt_c.f90'), 'w').write(c)
print('stand-ins written to', out)
