# crtcheck.py - the Python side of tests/crtcheck (as gocheck/ and javacheck/ are the Go and Java sides):
#   python3 crtcheck.py libm N | printf | scanf | misc | units | speed
# reads what the C program of the same name wrote on stdin and reports where the Python runtime (package cpp,
# py/cpp; these are part C's crt.py, crtmath.py and crtmisc.py) differs from glibc. The case lines and their checks
# are gocheck's and javacheck's, one for one; libm also prints the special-value table (the inputs where Python's
# math module raises or computes the special case itself, with glibc's result and the runtime's).
# Written by Claude (Anthropic) for the SWMM6 ports; see AI-USE.md.
import builtins
import math
import os
import struct
import sys
import tempfile
import time

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', '..', 'py'))
import cpp                     # noqa: E402
from cpp import *              # noqa: E402,F401,F403

_d = struct.Struct('<d')
_Q = struct.Struct('<Q')


def bits(x):
    return _Q.unpack(_d.pack(x))[0]


def frombits(b):
    return _d.unpack(_Q.pack(b))[0]


def unhex(s):
    return b'' if s == '-' else bytes.fromhex(s)


def lit(b):
    return CPtr(bytearray(b) + b'\0', 0)


def lines(what, check):
    n = bad = 0
    for raw in sys.stdin.buffer:
        line = raw.decode('latin-1').rstrip('\n')
        f = line.split('\t')
        n += 1
        want, got = check(f)
        if want != got:
            bad += 1
            if bad <= 15:
                try:
                    label = unhex(f[0]).decode('latin-1')
                except ValueError:
                    label = f[0]
                print('MISMATCH %r\n  glibc:  %s\n  Python: %s' % (label, want, got))
    print('%s cases: %d, mismatches: %d' % (what, n, bad))


# ------------------------------------------------------------------------------------------------ printf
# hex(format) types args size ret hex(output)

def printf_case(f):
    fmt = lit(unhex(f[0]))
    vals = f[2].split(',')
    args = []
    for i, t in enumerate(f[1]):
        v = vals[i]
        if t == 'd':
            args.append(frombits(int(v, 16)))
        elif t == 'i':
            x = int(v, 16)
            args.append(x - (1 << 32) if x >= 1 << 31 else x)
        elif t == 'l':
            x = int(v, 16)
            args.append(I64(x - (1 << 64) if x >= 1 << 63 else x))
        elif t == 's':
            args.append(lit(unhex(v)))
        elif t == 'S':
            args.append(CPtr_ZERO)
    size = int(f[3])
    buf = R_Buf_i(1024)
    buf.B[:] = b'\x55' * 1024
    buf.B[0] = 0
    r = R_snprintf_CPtr_ul_CPtr_oA(buf, size, fmt, *args)
    out = '-'
    if size > 0:
        k = buf.B.index(0)
        if k > 0:
            out = bytes(buf.B[:k]).hex()
    return f[4] + '\t' + f[5], '%d\t%s' % (r, out)


# ------------------------------------------------------------------------------------------------ scanf
# hex(format) hex(input) types ret values; the targets are the kinds the engine passes

def scanf_case(f):
    fmt = lit(unhex(f[0]))
    inp = lit(unhex(f[1]))
    types = f[2]
    m = len(types)
    ints = [0x55555555] * m
    longs = [0x5555555555555555] * m
    floats = [struct.unpack('<f', b'\x55' * 4)[0]] * m
    doubles = [frombits(0x5555555555555555)] * m
    bufs = [None] * m
    args = []

    def setter(arr, k, conv=None):
        def s(v):
            arr[k] = conv(v) if conv else v
        return s
    for k, t in enumerate(types):
        if t in 'in':
            args.append(R_RefI(setter(ints, k)))
        elif t == 'u':          # a long setter, kept as unsigned int bits read as int
            args.append(R_RefL(setter(ints, k, lambda v: ((v + 0x80000000) & 0xFFFFFFFF) - 0x80000000)))
        elif t == 'l':
            args.append(R_RefL(setter(longs, k)))
        elif t == 'f':
            args.append(R_RefF(setter(floats, k)))
        elif t == 'd':
            args.append(R_RefD(setter(doubles, k)))
        elif t == 'k':          # a char
            ints[k] = 0x55
            args.append(R_RefI(setter(ints, k, lambda v: ((v + 0x80) & 0xFF) - 0x80)))
        elif t in 'sc':
            bufs[k] = R_Buf_i(64)
            bufs[k].B[:] = b'\x55' * 64
            args.append(bufs[k])
    r = R_sscanf(inp, fmt, *args)
    parts = []
    for k, t in enumerate(types):
        if t in 'inuk':
            parts.append(str(ints[k]))
        elif t == 'l':
            parts.append(str(longs[k]))
        elif t == 'f':
            parts.append('%08x' % struct.unpack('<I', struct.pack('<f', floats[k]))[0])
        elif t == 'd':
            parts.append('%016x' % bits(doubles[k]))
        else:
            parts.append(bytes(bufs[k].B[:12]).hex())
        parts.append(' ')
    want = f[3] + '\t' + (f[4] if len(f) > 4 else '')
    return want, '%d\t%s' % (r, ''.join(parts))


# ------------------------------------------------------------------------------------------------ misc
# kind hex(input) result

def misc_case(f):
    raw = unhex(f[1])
    s = lit(raw)
    e = CPtr_op_Add_CPtr_i(s, len(raw))
    k = f[0]
    if k == 'fci':
        v = Cell(0x55555555)
        r = Std_from_chars_CPtr_CPtr_ir(s, e, v)
        got = '%d %d %d' % (r.ec, CPtr_Diff(r.ptr, s), v.v)
    elif k == 'fcu':
        v = Cell(0x55555555)
        r = Std_from_chars_CPtr_CPtr_ur(s, e, v)
        got = '%d %d %d' % (r.ec, CPtr_Diff(r.ptr, s), v.v)
    elif k == 'fcl':
        v = Cell(0x5555555555555555)
        r = cpp.Std_from_chars_CPtr_CPtr_lr(s, e, v)
        got = '%d %d %d' % (r.ec, CPtr_Diff(r.ptr, s), v.v)
    elif k == 'fcd':
        v = Cell(0.25)
        r = Std_from_chars_CPtr_CPtr_dr(s, e, v)
        got = '%d %d %016x' % (r.ec, CPtr_Diff(r.ptr, s), bits(v.v))
    elif k == 'strtol':
        end = Cell(s)
        v = cpp.R_strtol(s, end, 10)
        got = '%d %d' % (v, CPtr_Diff(end.v, s))
    elif k == 'atoi':
        got = str(R_atoi(s))
    elif k == 'atol':
        got = str(R_atol(s))
    elif k == 'strtod':
        end = Cell(s)
        v = R_strtod_CPtr_CPtrr(s, end)
        got = '%016x %d' % (bits(v), CPtr_Diff(end.v, s))
    elif k == 'ctime':
        t = int(raw.decode())
        p = R_ctime_Ptrl(NewPtrL_TA_l([t], 0))
        got = bytes(p.B[p.O:p.O + R_strlen(p)]).hex()
    elif k == 'nprocs':
        got = str(Sys_hardware_concurrency())
    elif k == 'ctype':
        c = int(raw.decode())
        got = '%d%d%d%d%d%d%d%d%d %d %d' % (R_isdigit(c), R_isalpha(c), R_isalnum(c), R_isspace(c), cpp.R_isupper(c),
                                            cpp.R_islower(c), cpp.R_ispunct(c), cpp.R_isprint(c), cpp.R_isxdigit(c),
                                            R_toupper(c), R_tolower(c))
    else:
        raise ValueError(k)
    if got != f[2]:
        got += '  (%s %r)' % (k, raw)
    return f[2], got


# ------------------------------------------------------------------------------------------------ libm

FN = ['atan2', 'cbrt', 'cosh', 'sinh', 'tanh', 'log2', 'expm1', 'log1p', 'hypot', 'fmod', 'remainder', 'round',
      'nextafter', 'exp', 'log', 'pow', 'sin', 'cos', 'tan', 'asin', 'acos', 'atan', 'log10', 'floor', 'ceil', 'trunc',
      'sqrt']
F = [Math_Atan2_d, lambda x, y: Math_Cbrt_d(x), lambda x, y: Math_Cosh_d(x), lambda x, y: Math_Sinh_d(x),
     lambda x, y: Math_Tanh_d(x), lambda x, y: Math_Log2_d(x), lambda x, y: LibM_expm1(x),
     lambda x, y: LibM_log1p(x), LibM_hypot, LibM_fmod, LibM_remainder, lambda x, y: LibM_round(x), LibM_nextafter,
     lambda x, y: Math_Exp_d(x), lambda x, y: Math_Log_d(x), Math_Pow_d, lambda x, y: Math_Sin_d(x),
     lambda x, y: Math_Cos_d(x), lambda x, y: Math_Tan_d(x), lambda x, y: Math_Asin_d(x), lambda x, y: Math_Acos_d(x),
     lambda x, y: Math_Atan_d(x), lambda x, y: Math_Log10_d(x), lambda x, y: Math_Floor_d(x),
     lambda x, y: Math_Ceiling_d(x), lambda x, y: Math_Truncate_d(x), lambda x, y: Math_Sqrt_d(x)]
# what CPython's own math module does on the same inputs (None: it raises), for the special-value table
PYM = [math.atan2, lambda x, y: math.cbrt(x), lambda x, y: math.cosh(x), lambda x, y: math.sinh(x),
       lambda x, y: math.tanh(x), lambda x, y: math.log2(x), lambda x, y: math.expm1(x), lambda x, y: math.log1p(x),
       math.hypot, math.fmod, math.remainder, lambda x, y: float(round(x)), math.nextafter,
       lambda x, y: math.exp(x), lambda x, y: math.log(x), math.pow, lambda x, y: math.sin(x),
       lambda x, y: math.cos(x), lambda x, y: math.tan(x), lambda x, y: math.asin(x), lambda x, y: math.acos(x),
       lambda x, y: math.atan(x), lambda x, y: math.log10(x), lambda x, y: float(math.floor(x)),
       lambda x, y: float(math.ceil(x)), lambda x, y: float(math.trunc(x)), lambda x, y: math.sqrt(x)]
TWO_ARG = {'atan2', 'hypot', 'fmod', 'remainder', 'nextafter', 'pow'}
NSPEC = 24


def ulps(a, b):
    ia, ib = struct.unpack('<q', _d.pack(a))[0], struct.unpack('<q', _d.pack(b))[0]
    if ia < 0:
        ia = -(1 << 63) - ia
    if ib < 0:
        ib = -(1 << 63) - ib
    return abs(ia - ib)


def show(x):
    if x != x:
        return ('-nan' if bits(x) >> 63 else 'nan') + ('' if bits(x) & 0x7FFFFFFFFFFFFFFF == 0x7FF8000000000000
                                                      else '(0x%x)' % (bits(x) & 0xFFFFFFFFFFFFF))
    if x == 0.0:
        return '-0' if bits(x) >> 63 else '0'
    return repr(x)


def libm(n):
    inp = sys.stdin.buffer
    per = n + NSPEC * NSPEC
    print('%-10s %8s %10s %9s %8s %10s  %s' % ('function', 'cases', 'mismatch', 'NaN-sign', 'max ulp', 'special',
                                               'first mismatch (x, y: glibc vs Python)'))
    total = 0
    table = []
    for fi, name in enumerate(FN):
        data = inp.read(per * 24)
        if len(data) < per * 24:
            print('short input')
            sys.exit(2)
        fn = F[fi]
        bad = nan_sign = 0
        maxu = 0
        nan_num = False
        first = ''
        sbad = 0
        k = 0
        for x, y, want in struct.iter_unpack('<ddQ', data):
            got = bits(fn(x, y))
            special = k >= n
            k += 1
            if special:
                try:
                    p = PYM[fi](x, y)
                    pb = bits(p)
                except (ValueError, builtins.OverflowError, ZeroDivisionError) as e:
                    p = None
                    pb = type(e).__name__
                if pb != want:
                    args = show(x) + (', ' + show(y) if name in TWO_ARG else '')
                    key = (name, args)
                    if key not in {t[:2] for t in table[-40:]}:
                        table.append((name, args, show(frombits(want)),
                                      pb if p is None else show(p), show(frombits(got)), got == want))
            if got == want:
                continue
            bad += 1
            if special:
                sbad += 1
            w, g = frombits(want), frombits(got)
            desc = '%s, %s: %s vs %s' % (show(x), show(y), show(w), show(g))
            if w != w and g != g:
                nan_sign += 1
            elif w == w and g == g:
                maxu = max(maxu, ulps(w, g))
            else:
                nan_num = True
            if not first:
                first = desc
        total += bad
        print('%-10s %8d %10d %9d %8s %10d  %s' % (name, per, bad, nan_sign, 'NaN/num' if nan_num else maxu, sbad, first))
    print('total mismatches: %d' % total)
    print()
    print('special-value table: the inputs (of the %dx%d grid of special values per function) where CPython\'s math'
          % (NSPEC, NSPEC))
    print('module raises or does not give glibc\'s bits, with glibc\'s result and the runtime\'s')
    print('%-10s %-34s %-24s %-22s %-24s %s' % ('function', 'arguments', 'glibc', 'math module', 'runtime', 'same'))
    seen = set()
    for name, args, w, p, g, ok in table:
        if (name, args) in seen:
            continue
        seen.add((name, args))
        print('%-10s %-34s %-24s %-22s %-24s %s' % (name, args, w, p, g, 'yes' if ok else 'NO'))
    print('special-value table rows: %d, runtime differs from glibc in: %d' % (len(seen),
                                                                              sum(1 for t in table if not t[5])))


# ------------------------------------------------------------------------------------------------ units

un = ubad = 0


def check(name, ok, *detail):
    global un, ubad
    un += 1
    if not ok:
        ubad += 1
        print('FAIL', name, detail)


def units():
    # sscanf into pointer parameters: the stash and R_Back, as the engine's parsers use them
    v = Cell(-9)
    flag = Cell(ord('q'))
    r = R_Back_i_T1r_T2r__LB(R_sscanf(R_Lit('123 Y'), R_Lit('%ld %c'), R_Slot, R_Slot), v, flag)
    check('slot %ld %c', r == 2 and v.v == 123 and flag.v == ord('Y'), r, v.v, flag.v)
    v.v = -9
    flag.v = ord('q')
    r = R_Back_i_T1r_T2r__LB(R_sscanf(R_Lit('77'), R_Lit('%ld %c'), R_Slot, R_Slot), v, flag)
    check('slot partial', r == 1 and v.v == 77 and flag.v == ord('q'), r, v.v, flag.v)
    x = Cell(99.99)

    def setx(s):
        x.v = s
    r = R_Back_i_T1r__B(R_sscanf(R_Lit('1.5 X'), R_Lit('%f %c'), R_RefF(setx), R_Slot), flag)
    check('RefF + slot', r == 2 and x.v == 1.5 and flag.v == ord('X'), r, x.v, flag.v)
    h = Cell(0)
    r = cpp.R_Back_i_T1r__S(R_sscanf(R_Lit('-12'), R_Lit('%d'), R_Slot), h)
    check('slot converted', r == 1 and h.v == -12, r, h.v)
    d = Cell(0.0)
    r = cpp.R_Back_i_T1r__D(R_sscanf(R_Lit('2.5'), R_Lit('%lf'), R_Slot), d)
    check('slot double', r == 1 and d.v == 2.5, r, d.v)
    s1 = R_Buf_i(80)
    r = R_sscanf(R_Lit('  SWMM5 x'), R_Lit('%s'), s1)
    check('%s CPtr', r == 1 and R_strcmp(s1, R_Lit('SWMM5')) == 0, r)
    ci = Cell(0)
    cd = Cell(0.0)
    r = R_sscanf(R_Lit('7 0.5'), R_Lit('%d %lf'), ci, cd)
    check('Ref cells as targets', r == 2 and ci.v == 7 and cd.v == 0.5, r, ci.v, cd.v)
    pb = NewPtrB_TA_l([0] * 16, 2)
    r = R_sscanf(R_Lit('abc'), R_Lit('%s'), pb)
    check('%s into a char array pointer', r == 1 and pb.a[2] == ord('a') and pb.a[4] == ord('c') and pb.a[5] == 0, r)
    lb = CPtr([0] * 8, 0)
    r = R_sscanf(R_Lit('xy\xe9'), R_Lit('%s'), lb)
    check('%s into a list buffer', r == 1 and lb.B[:4] == [120, 121, 0xE9, 0], lb.B)

    # strtod details: hex floats, NaN payloads, the end pointer
    end = Cell(CPtr_ZERO)
    s = R_Lit('  0x1.8p1rest')
    check('strtod hex', R_strtod_CPtr_CPtrr(s, end) == 3.0 and CPtr_Diff(end.v, s) == 9)
    check('strtod nan payload', bits(R_strtod_CPtr(R_Lit('nan(0x5)'))) == 0x7ff8000000000005)
    check('strtod -nan', bits(R_strtod_CPtr(R_Lit('-nan'))) == 0xfff8000000000000)
    s = R_Lit('abc')
    check('strtod no number', R_strtod_CPtr_CPtrr(s, end) == 0.0 and CPtr_op_Eq(end.v, s))
    check('strtod huge exponent', R_strtod_CPtr(R_Lit('1e99999999999')) == math.inf
          and R_strtod_CPtr(R_Lit('1e-99999999999')) == 0.0)
    # glibc's ERANGE (lesson 22): overflow, a nonzero number read as zero, a tiny inexact result; not an exact
    # subnormal, nor a number that rounds up to DBL_MIN
    er = []
    for t, want in (('1e999', 1), ('-1e999', 1), ('1e-400', 1), ('2.2250738585072011e-308', 1),
                    ('2.22507385850720138e-308', 0), ('2.2250738585072014e-308', 0), ('4.9e-324', 1), ('1.5', 0),
                    ('0', 0), ('0e-999', 0), ('0x1p-1080', 1), ('0x1p99999', 1), ('inf', 0), ('abc', 0),
                    ('4.940656458412465441765687928682213723651e-324', 1),
                    ('4.940656458412465441248710587739e-324', 1), ('0x3p-1074', 0),
                    (str(3 * 5 ** 1074) + 'e-1074', 0)):              # 3 * 2^-1074 written exactly
        R_strtod_CPtr(R_Lit(t))
        if (1 if cpp.R_get_ERange() else 0) != want:
            er.append(t)
    check('strtod ERANGE', not er, er)
    check('atof', R_atof(R_Lit(' -2.5e1x')) == -25.0)

    # raw memory
    b = [0]
    Mem_memcpy_PtrT_PtrU_ul__LD(NewPtrL_TA_l(b, 0), NewPtrD_TA_l([-2.75], 0), 8)
    check('memcpy double->u64 (unsigned)', b[0] == bits(-2.75), b[0])
    view = Bytes_View_PtrT__L(NewPtrL_TA_l([0x0102030405060708], 0))
    check('bytes view', len(view.B) == 8 and view.B[0] == 8 and view.B[7] == 1)
    f = [0.0]
    Mem_memcpy_PtrT_CPtr_ul__D(NewPtrD_TA_l(f, 0), view, 8)
    check('memcpy CPtr->double', bits(f[0]) == 0x0102030405060708)
    c = Mem_Calloc__D(5, 8)
    check('calloc', len(c.a) == 5 and c.o == 0 and c.a[4] == 0.0)
    arr = [object(), object(), object()]
    check('diff', Mem_Diff_T_PtrT(arr[2], NewPtr_TA_l(arr, 1)) == 1)
    u = [0, 0]
    Mem_memcpy_PtrT_PtrU_ul__IB(NewPtrI_TA_l(u, 1), NewPtrB_TA_l([1, 2, 3, 4, 5], 1), 4)
    check('memcpy bytes->u32 offset', u[0] == 0 and u[1] == 0x05040302, u)
    crc = [0]
    Mem_memcpy_PtrT_PtrU_ul__IB(NewPtrI_TA_l(crc, 0), NewPtrB_TA_l([0x01, 0x02, 0x03, 0xF4], 0), 4)
    check('memcpy uint crc (unsigned)', crc[0] == 0xF4030201, crc)
    w = [0x11111111, 0x22222222]
    Mem_memcpy_PtrT_CPtr_ul__I(NewPtrI_TA_l(w, 0), CPtr(bytearray([1, 2, 3, 4, 5, 6]), 0), 6)
    check('memcpy partial element', w[0] == 0x04030201 and w[1] == 0x22220605, hex(w[0]), hex(w[1]))
    neg = [0]
    Mem_memcpy_PtrT_CPtr_ul__I(NewPtrI_TA_l(neg, 0), CPtr(bytearray(b'\xff\xff\xff\xff'), 0), 4)
    check('memcpy int (signed)', neg[0] == -1, neg)
    zv = Bytes_View_PtrT__Z(NewPtrZ_TA_l([True, False, True], 1))
    check('bool view', len(zv.B) == 2 and zv.B[0] == 0 and zv.B[1] == 1)
    dv = Bytes_View_PtrT__D(NewPtrD_TA_l([1.0, 2.0], 1))
    check('double view offset', len(dv.B) == 8 and dv.B[7] == 0x40 and dv.B[6] == 0)
    sh = [0, 0]
    Mem_memcpy_PtrT_CPtr_ul__S(NewPtrS_TA_l(sh, 0), CPtr(bytearray(b'\xff\x7f\x01\x80'), 0), 4)
    check('memcpy shorts', sh[0] == 0x7FFF and sh[1] == -0x7FFF, sh)
    fl = [0.0]
    Mem_memcpy_PtrT_CPtr_ul__F(NewPtrF_TA_l(fl, 0), Bytes_View_PtrT__F(NewPtrF_TA_l([0.1 + 0 * f32(0.1)], 0)), 4)
    check('memcpy float', fl[0] == f32(0.1), fl)

    # binary files
    with tempfile.TemporaryDirectory(prefix='crtcheck') as dr:
        name = R_Lit(os.path.join(dr, 't.bin'))
        fp = R_fopen(name, R_Lit('w+b'))
        Mem_fwrite_CPtr_ul_ul_CFile(R_Lit('STAMP'), 1, 5, fp)
        R_fwriteI1(42, fp)
        pos = R_ftell(fp)
        R_fwriteD1(3.5, fp)
        R_fwriteF1(0.25, fp)
        Mem_fwrite_PtrT_ul_ul_CFile__I(NewPtrI_TA_l([7, -8, 9], 0), 4, 3, fp)
        R_fseek_CFile_l_i(fp, pos, 0)
        R_fwriteD1(4.5, fp)                          # overwrite in place
        R_rewind(fp)
        stamp = R_BufOf(16, [])
        got = Mem_fread_CPtr_ul_ul_CFile(stamp, 1, 5, fp)
        i, d, fl = Cell(0), Cell(0.0), Cell(0.0)
        ok = (got == 5 and R_strncmp(stamp, R_Lit('STAMP'), 5) == 0 and R_freadI1(i, fp) == 1 and i.v == 42
              and R_freadD1(d, fp) == 1 and d.v == 4.5 and R_freadF1(fl, fp) == 1 and fl.v == 0.25)
        check('binary round trip', ok, got, i.v, d.v, fl.v)
        ints = [0] * 4
        k = Mem_fread_PtrT_ul_ul_CFile__I(NewPtrI_TA_l(ints, 0), 4, 4, fp)
        check('short fread', k == 3 and ints[:3] == [7, -8, 9] and R_feof(fp) == 1, k, ints)
        check('fgetc at end', R_fgetc(fp) == -1)
        check('ftell at end', R_ftell(fp) == 5 + 4 + 8 + 4 + 12)
        check('fseek negative', R_fseek_CFile_l_i(fp, -1, 0) == -1)
        check('fseek end', R_fseek_CFile_l_i(fp, -4, 2) == 0 and R_ftell(fp) == 29 and R_feof(fp) == 0)
        last = Cell(0)
        check('fread after seek', R_freadI1(last, fp) == 1 and last.v == 9)
        check('fclose', R_fclose(fp) == 0)
        fp = R_fopen(name, R_Lit('r'))
        line = R_Buf_i(8)
        check('fgets', not R_fgets(line, 4, fp).get_IsNull() and R_strcmp(line, R_Lit('STA')) == 0)
        part = [0x11111111, 0x22222222]
        R_fseek_CFile_l_i(fp, -6, 2)
        pk = Mem_fread_PtrT_ul_ul_CFile__I(NewPtrI_TA_l(part, 0), 4, 2, fp)
        # the last 6 bytes are the top two of -8 and all of 9: ff ff 09 00 | 00 00
        check('fread partial element', pk == 1 and part[0] == 0x0009FFFF and part[1] == 0x22220000 and R_feof(fp) == 1,
              pk, hex(part[0]), hex(part[1]))
        R_fclose(fp)
        check('fopen missing', R_fopen(R_Lit(os.path.join(dr, 'none')), R_Lit('r')) is None)
        # a directory: "r" opens, as glibc opens it, and every read fails with the error flag set, not end-of-file
        # (lesson 24; C# CFile.Dir); "r+" and "w" fail
        dfp = R_fopen(R_Lit(dr), R_Lit('r'))
        dline = R_Buf_i(8)
        check('fopen directory', dfp is not None and R_fgets(dline, 8, dfp).get_IsNull() and R_feof(dfp) == 0
              and cpp.R_ferror(dfp) == 1 and R_fgetc(dfp) == -1 and R_feof(dfp) == 0 and R_fclose(dfp) == 0
              and R_fopen(R_Lit(dr), R_Lit('r+')) is None and R_fopen(R_Lit(dr), R_Lit('w')) is None)
        check('ferror of a file', cpp.R_ferror(R_fopen(name, R_Lit('r'))) == 0)
        check('fopen bad mode', R_fopen(name, R_Lit('x')) is None)
        # append mode: writes go at the end, no seeking before the old end
        an = R_Lit(os.path.join(dr, 'a.txt'))
        fp = R_fopen(an, R_Lit('w'))
        R_fprintf(fp, R_Lit('one\n'))
        R_fclose(fp)
        fp = R_fopen(an, R_Lit('a'))
        check('append position', R_ftell(fp) == 4)
        check('append no seek back', R_fseek_CFile_l_i(fp, 0, 0) == -1)
        R_fprintf(fp, R_Lit('two\n'))
        R_fclose(fp)
        check('append content', open(os.path.join(dr, 'a.txt'), 'rb').read() == b'one\ntwo\n')
        # text lines: fgets and the end-of-file flag
        tn = os.path.join(dr, 'l.txt')
        open(tn, 'wb').write(b'ab\ncdef\ng')
        fp = R_fopen(R_Lit(tn), R_Lit('r'))
        lb = R_Buf_i(16)
        got = []
        while not R_fgets(lb, 4, fp).get_IsNull():
            got.append((R_Str(lb), R_feof(fp)))
        check('fgets lines', got == [('ab\n', 0), ('cde', 0), ('f\n', 0), ('g', 1)] and R_feof(fp) == 1, got)
        R_fclose(fp)
        # fprintf of every argument kind the translator passes
        fp = R_fopen(R_Lit(os.path.join(dr, 't.txt')), R_Lit('w'))
        R_fprintf(fp, R_Lit('%-6s|%3d|%5.2f|%c|%zu|%s|%d|%hu|%u|%c|%d|%s|%x|%ld|%lld|%.3f\n'), R_Lit('ab'), I8(-5),
                  F32(1.25), U8(ord('Z')), U64((1 << 64) - 1), NewStr_CPtr(R_Lit('str')), 7, U16(65535),
                  U32(0xFFFFFFFF), Ch(ord('q')), True, 'pystr', I16(-1), I64(-(1 << 63)), U64(1 << 63), 2)
        R_FlushAll()
        R_fclose(fp)
        txt = open(os.path.join(dr, 't.txt'), 'rb').read()
        check('fprintf argument kinds', txt == b'ab    | -5| 1.25|Z|18446744073709551615|str|7|65535|4294967295|q|1'
              b'|pystr|ffffffff|-9223372036854775808|-9223372036854775808|2.000\n', txt)
        check('fprintf null file', R_fprintf(None, R_Lit('x')) == -1)
        sb = R_Buf_i(8)
        check('snprintf truncation', R_snprintf_CPtr_ul_CPtr_oA(sb, 4, R_Lit('%d'), 123456) == 6
              and R_strcmp(sb, R_Lit('123')) == 0)
        check('snprintf size 0', R_snprintf_CPtr_ul_CPtr_oA(sb, 0, R_Lit('%s'), R_Lit('abc')) == 3
              and R_strcmp(sb, R_Lit('123')) == 0)
        check('remove', cpp.R_remove(an) == 0 and cpp.R_remove(an) == -1)
        check('File.Exists', File_Exists(os.path.join(dr, 't.txt')) and not File_Exists(dr) and not File_Exists(''))
        check('printf nan/inf', R_Format(R_Lit('%f %+.2e %5.1f %G %a'), (DNAN, math.inf, -math.inf, QNAN, -0.0))
              == b'-nan +inf  -inf NAN -0x0p+0')

    # tuples, atomics, variants, once
    t1, t2 = [1.0, 2.0], [1.0, 2.0]
    a = NewTupN_oA(1, 2.5, NewPtrD_TA_l(t1, 0), True, 3)
    b = NewTupN_oA(1, 2.5, NewPtrD_TA_l(t1, 0), True, 3)
    c = NewTupN_oA(1, 2.5, NewPtrD_TA_l(t2, 0), True, 3)
    d = NewTupN_oA(1, 2.5, NewPtrD_TA_l(t1, 0), True, 4)
    check('tuple equal', a.CompareTo(b) == 0 and a.Equals(b) and a == b and hash(a) == hash(b))
    check('tuple identity', a.CompareTo(c) != 0 and a.CompareTo(c) == -c.CompareTo(a))
    check('tuple enum', a.CompareTo(d) < 0 and d.CompareTo(a) > 0 and a < d and isinstance(a, ICopy))
    m = NewAtomicL()
    check('fetch_add', m.fetch_add(5, 0) == 0 and m.fetch_add(2, 0) == 5 and m.v == 7)
    fa = cpp.NewAtomicD_T(1.5)
    old = Cell(1.0)
    ok1 = fa.compare_exchange(old, 3.0)
    ok2 = fa.compare_exchange(old, 3.0)
    check('compare_exchange', not ok1 and old.v == 1.5 and ok2 and fa.v == 3.0)
    z = NewAtomicZ_T(True)
    z.v = False
    check('atomic bool', not z.v and not z.Clone().v and isinstance(z, ICopy))
    ul = NewAtomicL_T((1 << 64) - 1)
    ul.fetch_add(1, 0)
    check('atomic ulong wraps', ul.v == 0, ul.v)
    sl = NewAtomicL_T(-1)
    sl.fetch_add(1, 0)
    check('atomic long', sl.v == 0)
    v = NewVariant()
    check('variant default', v.v is False and v.idx == 0)
    s = NewStr_CPtr(R_Lit('abc'))
    v.Set_Str(s)
    s.append_i(ord('d'))
    w = v.Clone()
    check('variant string', v.idx == 3 and v.v.ToString() == 'abc' and w.v is not v.v and w.v.ToString() == 'abc')
    v.Set_d(2.5)
    check('variant double', v.idx == 2 and v.v == 2.5 and v.index() == 2)
    once = NewOnceFlag()
    calls = [0]

    def inc():
        calls[0] += 1
    Std_call_once(once, inc)
    Std_call_once(once, inc)
    check('call_once', calls[0] == 1)
    x, y = Cell(1), Cell(2)
    Std_swap__I(x, y)
    check('swap', x.v == 2 and y.v == 1)
    da = [1.0, 2.0]
    Std_swap__D(ArrRef(da, 0), ArrRef(da, 1))
    check('swap array elements', da == [2.0, 1.0])
    t0 = TimePoint_now()
    check('duration', TimePoint_op_Sub(TimePoint_now(), t0).Clone().count() >= 0 and TimePoint_ZERO.ns == 0)
    check('qnan', bits(Std_QNaN) == 0x7FF8000000000000)
    check('nop', Std_Nop(3) == 0)
    pr = NewPair_A_B(4, 0.5)
    check('get0/get1', Std_get0_PairAB__ID(pr) == 4 and Std_get1_PairAB__ID(pr) == 0.5)
    thrown = False
    try:
        Std_Rethrow(IOError('x'))
    except IOError:
        thrown = True
    check('Rethrow', thrown)
    ec = NewErrorCode()
    ec.v = 2
    check('error_code', ec.Bool() and ec.message().ToString() == 'No such file or directory')
    se = SysException('NotImplementedException', 'm')
    check('SysException', str(se) == 'm' and se.kind == 'NotImplementedException' and not isinstance(se, CppException)
          and isinstance(se, Exception))
    check('locks', NewThread().joinable() is False and NewMutex() is not None and NewCondVar() is not None
          and NewLock() is not None)
    check('hardware_concurrency', Sys_hardware_concurrency() == os.sysconf('SC_NPROCESSORS_ONLN'))

    # math corners
    check('CopySign NaN sign', Math_CopySign_d(1.0, DNAN) == -1.0)
    check('Truncate', Math_Truncate_d(-2.7) == -2.0 and bits(Math_Truncate_d(-0.5)) == 0x8000000000000000)
    check('Ceiling -0', bits(Math_Ceiling_d(-0.5)) == 0x8000000000000000 and Math_Ceiling_d(0.2) == 1.0)
    check('Floor', bits(Math_Floor_d(-0.0)) == 0x8000000000000000 and Math_Floor_d(-0.5) == -1.0
          and Math_Floor_d(math.inf) == math.inf and Math_Floor_d(1e300) == 1e300)
    check('round', LibM_round(2.5) == 3.0 and LibM_round(-2.5) == -3.0 and LibM_round(0.49999999999999994) == 0.0
          and bits(LibM_round(-0.4)) == 0x8000000000000000 and cpp.LibM_lround(-2.5) == -3)
    check('pow errors', Math_Pow_d(0.0, -1.0) == math.inf and Math_Pow_d(-0.0, -3.0) == -math.inf
          and bits(Math_Pow_d(-8.0, 1.0 / 3)) == 0xFFF8000000000000 and Math_Pow_d(10.0, 400.0) == math.inf
          and Math_Pow_d(-10.0, 401.0) == -math.inf)
    check('log errors', Math_Log_d(0.0) == -math.inf and Math_Log_d(-1.0) != Math_Log_d(-1.0)
          and Math_Log10_d(-0.0) == -math.inf)
    check('exp overflow', Math_Exp_d(1000.0) == math.inf and Math_Cosh_d(1000.0) == math.inf
          and Math_Sinh_d(-1000.0) == -math.inf)
    check('sqrt', bits(Math_Sqrt_d(-1.0)) == 0xFFF8000000000000 and Math_Sqrt_d(4.0) == 2.0
          and bits(Math_Sqrt_d(QNAN)) == 0x7FF8000000000000)
    check('Log2 powers', Math_Log2_d(1024.0) == 10.0 and Math_Log2_d(5e-324) == -1074.0 and Math_Log2_d(1.0) == 0.0)

    # char arrays and strings
    p = R_LitN('OPENSWMM_HS_V1', 16)
    check('LitN', len(p.B) == 16 and p.B[14] == 0 and p.B[15] == 0)
    q = R_CopyBuf(p)
    q.B[0] = ord('X')
    check('CopyBuf', p.B[0] == ord('O') and q.O == p.O)
    dst = R_BufOf(6, [ord('a'), ord('b')])
    check('BufOf', bytes(dst.B) == b'ab\0\0\0\0')
    check('bufs', len(R_Bufs(5, 24)) == 5 and len(R_Bufs(5, 24)[4].B) == 24)
    tp = NewPtrL_TA_l([0], 0)
    t = R_time_Ptrl(tp)
    check('time', t > 1700000000 and tp.a[0] == t and R_difftime(t + 3, t) == 3.0 and R_time_Ptrl(PtrL_ZERO) >= t)
    check('strcmp unsigned', R_strcmp(CPtr(bytearray([0xE9, 0]), 0), R_Lit('e')) > 0
          and R_strcmp(R_Lit('ab'), R_Lit('abc')) == -ord('c') and R_strcmp(CPtr([97, 0], 0), R_Lit('a')) == 0)
    hay = R_Lit('a,b,,c')
    check('strchr', CPtr_Diff(R_strchr(hay, ord(',')), hay) == 1 and CPtr_Diff(R_strchr(hay, 0), hay) == 6
          and R_strchr(hay, ord('z')).get_IsNull())
    check('strrchr', CPtr_Diff(R_strrchr(hay, ord(',')), hay) == 4 and R_strrchr(hay, ord('z')).get_IsNull())
    check('strstr', CPtr_Diff(R_strstr(hay, R_Lit(',,')), hay) == 3 and R_strstr(hay, R_Lit('x')).get_IsNull()
          and CPtr_op_Eq(R_strstr(hay, R_Lit('')), hay))
    check('strcspn', cpp.R_strcspn(hay, R_Lit(',')) == 1 and cpp.R_strcspn(R_Lit('abc'), R_Lit('x')) == 3)
    toks = []
    tk = R_strtok(hay, R_Lit(','))
    while not tk.get_IsNull():
        toks.append(R_Str(tk))
        tk = R_strtok(CPtr_ZERO, R_Lit(','))
    check('strtok', toks == ['a', 'b', 'c'], toks)
    cat = R_Buf_i(16)
    cpp.R_strcpy(cat, R_Lit('ab'))
    cpp.R_strcat(cat, R_Lit('cd'))
    check('strcpy/strcat', R_Str(cat) == 'abcd' and R_strlen(cat) == 4)
    cpp.R_strcpy(CPtr_op_Add_CPtr_i(cat, 1), cat)
    check('strcpy overlap', R_Str(cat) == 'aabcd')
    check('toupper', R_toupper(ord('a')) == ord('A') and R_toupper(ord('{')) == ord('{'))
    check('atoi wrap', R_atoi(R_Lit('4294967297')) == 1 and R_atoi(R_Lit('99999999999999999999')) == -1)
    check('Lit non-Latin-1', bytes(R_Lit('a∞b').B) == b'a\x1eb\0')
    check('class R', cpp.R.strlen(R_Lit('abc')) == 3 and cpp.R.getenv is not None and cpp.R.stdout_ is R_stdout_)
    print('units cases: %d, failures: %d' % (un, ubad))


# ------------------------------------------------------------------------------------------------ speed

def speed():
    """Calls per second of the hot paths (no reference: a record for comparison between versions)."""
    f = R_Lit('%-16s %14.3f %8.2f %10d\n')
    nm = R_Lit('Conduit C1')
    cases = [
        ('R_Format %-16s %14.3f %8.2f %10d', lambda: R_Format(f, (nm, 1234.5678, 0.25, 42))),
        ('R_snprintf %02d/%02d/%04d', lambda: R_snprintf_CPtr_ul_CPtr_oA(buf, 32, R_Lit('%02d/%02d/%04d %02d:%02d'),
                                                                         1, 2, 2020, 3, 4)),
        ('R_sscanf %d:%d:%d', lambda: R_sscanf(R_Lit('12:30:45'), R_Lit('%d:%d:%d'), R_RefI(int), R_RefI(int),
                                               R_RefI(int))),
        ('R_strtod_CPtr', lambda: R_strtod_CPtr(num)),
        ('R_atoi', lambda: R_atoi(inum)),
        ('R_strcmp', lambda: R_strcmp(nm, nm2)),
        ('R_Lit', lambda: R_Lit('CONDUITS')),
        ('Math_Pow_d', lambda: Math_Pow_d(1.7, 0.6667)),
        ('Math_Floor_d', lambda: Math_Floor_d(-2.5)),
        ('Math_Exp_d', lambda: Math_Exp_d(0.3)),
    ]
    buf = R_Buf_i(64)
    num = R_Lit('  123.456e-1 ')
    inum = R_Lit(' 4711')
    nm2 = R_Lit('Conduit C2')
    for name, fn in cases:
        n = 20000
        t = time.perf_counter()
        for _ in range(n):
            fn()
        dt = time.perf_counter() - t
        print('%-36s %8.2f us/call' % (name, dt / n * 1e6))


def main():
    what = sys.argv[1]
    if what == 'libm':
        libm(int(sys.argv[2]))
    elif what == 'printf':
        lines('printf', printf_case)
    elif what == 'scanf':
        lines('scanf', scanf_case)
    elif what == 'misc':
        lines('misc', misc_case)
    elif what == 'units':
        units()
    elif what == 'speed':
        speed()
    else:
        raise SystemExit('crtcheck.py: libm N | printf | scanf | misc | units | speed')
    R_FlushAll()


if __name__ == '__main__':
    main()
