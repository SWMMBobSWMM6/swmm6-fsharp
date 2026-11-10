# corecheck.py - part A of the Python runtime (package cpp: py/cpp/core_num.py, core_ptr.py, core_str.py,
# core_vec.py) running the operations corecheck.cs runs on the C# runtime (cs/runtime). run.sh requires identical
# output. Doubles print as their bit patterns, booleans as 0/1, strings in brackets. C#'s double.NaN is x86's
# default NaN (DNAN, sign bit set); Python's float('nan') is the positive quiet NaN, so DNAN stands for it here.
# The package is loaded from py/cpp through a shim that imports part A's modules only (the other parts are not
# needed, except R.strtod for std::stod, stubbed below for plain decimal input).
# Written by Claude (Anthropic) for the SWMM6 ports; see AI-USE.md.
import os
import sys
import types

HERE = os.path.dirname(os.path.abspath(__file__))
PKG = os.path.normpath(os.path.join(HERE, '..', '..', 'py', 'cpp'))

cpp = types.ModuleType('cpp')
cpp.__path__ = [PKG]
cpp.__package__ = 'cpp'
sys.modules['cpp'] = cpp
import cpp.core_num, cpp.core_ptr, cpp.core_str, cpp.core_vec   # noqa: E401,E402
for _m in (cpp.core_num, cpp.core_ptr, cpp.core_str, cpp.core_vec):
    for _n in _m.__all__:
        setattr(cpp, _n, getattr(_m, _n))
from cpp import *   # noqa: E402,F401,F403
from cpp.core_num import __all__ as _a1   # noqa: E402
import re as _re   # noqa: E402

_NUM = _re.compile(rb'[ \t\n\v\f\r]*[+-]?(\d+\.?\d*|\.\d+)([eE][+-]?\d+)?')


def _stub_strtod(p, end):
    """Part C's R.strtod for plain decimal text (the test's inputs): the longest decimal prefix, correctly rounded."""
    B = p.B
    e = B.index(0, p.O)
    m = _NUM.match(bytes(B[p.O:e]))
    if not m:
        end.v = p
        return 0.0
    end.v = CPtr(B, p.O + m.end())
    return float(m.group(0))


if not hasattr(cpp, 'R_strtod_CPtr_CPtrr'):
    cpp.R_strtod_CPtr_CPtrr = _stub_strtod
if not hasattr(cpp, 'R_get_ERange'):
    cpp.R_get_ERange = lambda: False     # part C's R.ERange (the stub's plain decimal input never sets it)

out = []
NAN = DNAN          # C#'s double.NaN
NPOS = 18446744073709551615


def P(tag, v):
    out.append('%s=%s\n' % (tag, v))


def d(x):
    return format(f64_to_bits(x), 'x')


def b(x):
    return '1' if x else '0'


def s(x):
    return '[' + x.ToString() + ']'


def vd(v):
    return '%d/%d:' % (v.size(), v.capacity()) + ''.join(' ' + d(v.a[i]) for i in range(v.n))


def vi(v):
    return '%d/%d:' % (v.size(), v.capacity()) + ''.join(' %d' % v.a[i] for i in range(v.n))


def vs(v):
    return '%d/%d:' % (v.size(), v.capacity()) + ''.join(' ' + s(v.a[i]) for i in range(v.n))


def hexb(a, n):
    return ''.join('%02x' % a[i] for i in range(n))


def lit(x):
    t = bytearray(x.encode('latin-1'))
    t.append(0)
    return CPtr(t, 0)


def S(x):
    return NewStr_CPtr(lit(x))


def cs_bytes(a):
    return ''.join(a)


class Holder:
    __slots__ = ('x', 'k')

    def __init__(self):
        self.x = 0.0
        self.k = 0


def atX():
    return lambda o: AttrRef(o, 'x')


def atX2():
    return lambda o: AttrRef(o, 'x')


class Res(IDisposable):
    __slots__ = ('name',)

    def __init__(self, n):
        self.name = n

    def Dispose(self):
        P('dispose', self.name)


def main():
    vec()
    it()
    strs()
    cptr()
    ptr()
    misc()
    nums()
    more()
    fuzz()
    sys.stdout.write(''.join(out))


def vec():
    v = NewVecD()
    caps = ''
    for i in range(20):
        v.push_back(i + 0.5)
        caps += '%d ' % v.capacity()
    P('grow', caps)
    v.resize_ul(5); P('resize5', vd(v))
    v.resize_ul(9); P('resize9', vd(v))
    v.resize_ul_T(12, 7.5); P('resize12', vd(v))
    w = NewVecD_TA([10.0, 20.0, 30.0])
    v.insert_IterT_T(IterD_op_Add(v.begin(), 2), 99.0); P('insT', vd(v))
    v.insert_IterT_TA(IterD_op_Add(v.begin(), 1), [1.0, 2.0, 3.0]); P('insTA', vd(v))
    v.insert_IterT_IterT_IterT(v.end(), w.begin(), w.end()); P('insII', vd(v))
    v.insert_IterT_ul_T(v.begin(), 3, -1.0); P('insN', vd(v))
    raw = [5.0, 6.0, 7.0, 8.0]
    v.insert_IterT_PtrT_PtrT(IterD_op_Add(v.begin(), 4), NewPtrD_TA_l(raw, 1), NewPtrD_TA_l(raw, 3)); P('insP', vd(v))
    r = v.erase_IterT(IterD_op_Add(v.begin(), 3)); P('erase1', vd(v) + ' @%d' % r.i)
    r = v.erase_IterT_IterT(IterD_op_Add(v.begin(), 1), IterD_op_Add(v.begin(), 4)); P('erase3', vd(v) + ' @%d' % r.i)
    v.reserve(100); P('reserve', vd(v))
    v.shrink_to_fit(); P('shrink', vd(v))
    v.assign_ul_T(3, 2.0); P('assignN', vd(v))
    v.assign_IterT_IterT(IterD_op_Add(w.begin(), 1), w.end()); P('assignII', vd(v))
    v.pop_back(); P('pop', vd(v))
    v.swap(w); P('swapV', vd(v)); P('swapW', vd(w))
    c = v.Clone(); P('clone', vd(c))
    e = NewVecD(); e.reserve(10); e.Assign_VecT(v); P('assign', vd(e))
    m = NewVecD(); m.MoveFrom_VecT(c); P('moveTo', vd(m)); P('moveFrom', vd(c))
    v.clear(); P('clear', vd(v) + ' ' + b(v.n == 0))
    P('eqNaN', b(VecD_Eq(NewVecD_TA([NAN, 0.0]), NewVecD_TA([NAN, -0.0]))))
    P('eqLen', b(VecD_Eq(NewVecD_ul(2), NewVecD_ul(3))))
    P('ctorN', vd(NewVecD_ul_T(4, 1.25)))
    try:
        w.at(7)
    except OutOfRange as x:
        P('at', x.what().ToString())
    try:
        w.at(NPOS)
    except OutOfRange as x:
        P('at', x.what().ToString())
    vi_ = NewVecI_TA([1, 2, 3])
    P('vi', vi(vi_)); vi_.push_back(4); P('vi4', vi(vi_))
    vb = NewVecB_TA([200, 1])
    P('eqB', b(VecB_Eq(vb, NewVecB_TA([200, 1]))) + b(VecB_Eq(vb, NewVecB_TA([201, 1]))))
    vz = NewVecZ_ul_T(3, True); vz.resize_ul(5); P('vz', ''.join(b(vz.a[i]) for i in range(vz.n)))

    sv = NewVec_ul_FuncT(3, NewStr)
    sv.a[1].append_CPtr(lit('one'))
    sv.push_back(NewStr_str('two'))
    sc = sv.Clone()
    sc.a[1].append_CPtr(lit('!'))
    P('strVec', vs(sv)); P('strClone', vs(sc))
    P('strEq', b(Vec_Eq(sv, sv.Clone())) + b(Vec_Eq(sv, sc)))
    sv.resize_ul(6); P('strResize', vs(sv))
    sv.insert_IterT_IterT_IterT(Iter_op_Add(sv.begin(), 1), sc.begin(), Iter_op_Add(sc.begin(), 2))
    sc.a[1].append_CPtr(lit('?'))
    P('strIns', vs(sv))
    sv.erase_IterT_IterT(sv.begin(), Iter_op_Add(sv.begin(), 2)); P('strErase', vs(sv))
    va = NewVec_ul_FuncT(2, lambda: Arr_Make__D(3))
    va.a[0] = [1.0, 2.0, 3.0]
    vc = va.Clone()
    vc.a[0][1] = 42.0
    P('arrVec', d(va.a[0][1]) + ' ' + d(vc.a[0][1]))
    none = NewVec(MK_N)
    none.Assign_VecT(NewVec_FuncT(lambda: Arr_Make__D(3)))
    none.resize_ul(2)
    P('mkAdopt', len(none.a[1]))
    none2 = NewVec(MK_N)
    none2.MoveFrom_VecT(NewVec_FuncT(lambda: Arr_Make__D(4)))
    none2.resize_ul(1)
    P('mkMove', len(none2.a[0]))


def it():
    w = NewVecI_TA([1, 2, 3])
    i0 = w.begin()
    w.push_back(4)
    w.a[0] = 100
    P('staleIter', '%d %d' % (i0.get_Deref(), IterI_op_Add(i0, 0).get_Deref()))
    inc = w.begin(); inc = IterI_op_Increment(inc)
    P('iterSub', '%d %d' % (IterI_op_Sub_IterT_IterT(w.end(), w.begin()), IterI_op_Sub_IterT_l(w.end(), 2).get_Deref()))
    P('iterCmp', b(IterI_op_Lt(w.begin(), w.end())) + b(IterI_op_Eq(inc, IterI_op_Add(w.begin(), 1))) + b(IterI_op_Ne(w.begin(), w.end())))
    sb = ''
    ri = w.rbegin()
    while RevIterI_op_Ne(ri, w.rend()):
        sb += '%d ' % ri.get_Deref()
        ri = RevIterI_op_Increment(ri)
    P('rev', sb)
    bi = NewIterI_TA_l([7, 8, 9], 1)
    P('bareIter', '%d %d %d' % (bi.get_Deref(), IterI_op_Add(bi, 1).get_Deref(), bi.get_Item(-1)))
    fromIt = PtrI_from_IterT(IterI_op_Add(w.begin(), 2))
    P('ptrFromIter', '%d %d' % (fromIt.get_Deref(), fromIt.o))
    sp = NewCSpanD_TA_l_l([1.0, 2.0, 3.0, 4.0, 5.0], 1, 3)
    P('span', '%d %s %s %s %d %s %s %d %s' % (sp.size(), d(sp.front()), d(sp.back()), d(sp.get_Item(1)), sp.subspan(1).size(),
                                              d(sp.subspan(1).front()), d(sp.last(2).front()), sp.first(2).size(), b(CSpan_ZERO.empty())))


def strs():
    s_ = S('Hello, World')
    P('size', s_.size())
    P('find', '%d %d %d %d %d' % (s_.find_CPtr_ul(lit('o'), 0), s_.find_i_ul(ord('o'), 5), s_.find_i_ul(ord('z'), 0),
                                  s_.find_Str_ul(S('World'), 0), s_.find_CPtr_ul(lit('World'), 8)))
    P('findEmpty', '%d %d' % (s_.find_CPtr_ul(lit(''), 12), s_.find_CPtr_ul(lit(''), 13)))
    P('rfind', '%d %d %d %d %d' % (s_.rfind_i_ul(ord('o'), NPOS), s_.rfind_CPtr_ul(lit('o'), 5), s_.rfind_CPtr_ul(lit('l'), NPOS),
                                   s_.rfind_CPtr_ul(lit('Hello, World!'), NPOS), NewStr().rfind_i_ul(ord('a'), NPOS)))
    P('firstOf', '%d %d %d %d' % (s_.find_first_of_CPtr_ul(lit(', '), 0), s_.find_first_not_of_CPtr_ul(lit('Hel'), 0),
                                  s_.find_first_not_of_i_ul(ord('H'), 0), s_.find_first_of_CPtr_ul(lit('xyz'), 0)))
    P('lastOf', '%d %d %d %d %d' % (s_.find_last_of_CPtr_ul(lit('lo'), NPOS), s_.find_last_not_of_CPtr_ul(lit('dl'), NPOS),
                                    s_.find_last_of_i_ul(ord('o'), 6), s_.find_last_not_of_i_ul(ord('d'), NPOS),
                                    s_.find_last_of_CPtr_ul(lit('H'), 3)))
    P('substr', s(s_.substr(7, NPOS)) + s(s_.substr(0, 5)) + s(s_.substr(12, 3)))
    try:
        s_.substr(13, 1)
    except OutOfRange as x:
        P('substrErr', x.what().ToString())
    try:
        s_.at(12)
    except OutOfRange as x:
        P('atErr', x.what().ToString())
    t = s_.Clone()
    t.erase(5, 2); P('erase', s(t))
    t.insert_ul_CPtr(5, lit(' there')); P('insert', s(t))
    t.replace_ul_ul_CPtr(0, 5, lit('Bye')); P('replace', s(t))
    t.append_i(ord('!')); t.append_ul_i(3, ord('?')); t.push_back(ord('#')); P('append', s(t))
    t.pop_back(); t.resize(24, ord('.')); P('resize', s(t) + ' %d' % t.size())
    t.resize(4, 0); P('resize4', s(t))
    t.erase(1, NPOS); P('eraseAll', s(t))
    t.insert_ul_ul_i(1, 2, ord('x')); t.insert_ul_Str(0, S('>>')); P('insertN', s(t))
    P('compare', '%d %d %d %d %d %d' % (S('abc').compare_CPtr(lit('abd')), S('ab').compare_CPtr(lit('abc')), S('abc').compare_CPtr(lit('ab')),
                                        NewStr_ucA_i_i(bytearray([0xFF]), 0, 1).compare_CPtr(lit('a')), S('abc').compare_Str(S('abc')),
                                        s_.compare_ul_ul_Str(7, 5, S('World'))))
    P('startsEnds', b(s_.starts_with_CPtr(lit('Hell'))) + b(s_.starts_with_Str(S('ello'))) + b(s_.starts_with_i(ord('H'))) +
      b(s_.ends_with_CPtr(lit('World'))) + b(s_.ends_with_i(ord('x'))) + b(s_.contains_Str(S('o, W'))) + b(s_.contains_i(ord('#'))))
    P('cat', s(Str_Cat_Str_CPtr(s_, lit('!'))) + s(Str_Cat_CPtr_Str(lit('<'), s_)) + s(Str_Cat_Str_Str(s_, s_)) + s(Str_Cat_Str_i(s_, ord('?'))) +
      s(Str_Cat_i_Str(ord('^'), s_)))
    P('toString', s(Str_to_string_i(-42)) + s(Str_to_string_ul(NPOS)) + s(Str_to_string_u(4294967295)) + s(Str_to_string_l(-9223372036854775808)) +
      s(Str_to_string_d(3.14159)) + s(Str_to_string_d(-0.0)) + s(Str_to_string_d(1e20)) + s(Str_to_string_d(2.5e-7)) + s(Str_to_string_f(f32(0.1))))
    idx = NewPtrL_TA_l([0], 0)
    P('stod', d(Str_stod(S('  12.5e1xyz'), idx)) + ' %d ' % idx.get_Deref() + d(Str_stod(S('-0.25'), PtrL_ZERO)))
    try:
        Str_stod(S('abc'), PtrL_ZERO)
    except InvalidArgument as x:
        P('stodErr', x.what().ToString())
    P('stoi', '%d %d %d' % (Str_stoi(S(' -17abc'), idx, 10), idx.get_Deref(), Str_stol(S('99999999999'))))
    try:
        Str_stoi(S('99999999999'))
    except OutOfRange as x:
        P('stoiErr', x.what().ToString())
    lat = NewStr_str('café€!')
    P('latin1', hexb(lat.b, lat.n))
    g = NewStr()
    for i in range(40):
        g.push_back(ord('a') + i % 26)
    P('grow', '%d ' % len(g.b) + s(g))
    cs = g.c_str(); P('cstr', b(cs.B is g.b) + ' %d' % g.b[g.n])
    h = NewStr(); h.MoveFrom(g); P('move', s(h) + s(g) + ' %d' % len(g.b))
    h.Assign_CPtr(lit('assigned')); P('assignC', s(h) + ' %d' % len(h.b))
    h.Assign_Str(S('xy')); P('assignS', s(h) + ' %d' % len(h.b))
    h.Assign_i(ord('Q')); P('assignI', s(h))
    P('eq', b(h.EqLit(lit('Q'))) + b(h.EqLit(lit('QQ'))) + b(h.Equals_Str(S('Q'))) + b(h.Equals_Str(None)))
    P('hash', '%d %d' % (S('Hello, World').GetHashCode(), NewStr_ucA_i_i(bytearray([0xE9, ord('a')]), 0, 2).GetHashCode()))
    v = S('  view  '); v.remove_prefix(2); v.remove_suffix(2); P('view', s(v))
    a = NewStr(); a.assign_CPtr_CPtr(CPtr_op_Add_CPtr_i(lit('abcdef'), 1), CPtr_op_Add_CPtr_i(lit('abcdef'), 4)); P('assignRange', s(a))
    raw = [ord('q'), ord('r'), ord('s'), ord('t')]
    a.assign_Ptrc_Ptrc(NewPtrB_TA_l(raw, 1), NewPtrB_TA_l(raw, 3)); P('assignPtr', s(a))
    a.assign_ul_i(3, ord('z')); P('assignN', s(a))
    a.append_Str_ul_ul(S('0123456'), 2, 3); a.append_CPtr_ul(lit('ABCDEF'), 2); P('appendSub', s(a))
    P('ctors', s(NewStr_ul_i(5, ord('-'))) + s(NewStr_CPtr_ul(lit('abcdef'), 3)) + s(NewStr_Str_ul_ul(S('abcdef'), 2, 10)) + s(Str_from_CPtr(lit('conv'))))
    bi = s_.begin(); P('strIter', '%s %d' % (chr(bi.get_Deref()), IterB_op_Sub_IterT_IterT(s_.end(), bi)))


def cptr():
    buf = bytearray(8)
    p = CPtr(buf, 0)
    p.set(0, 200)
    r1 = p.assign(1, 200)
    p.set(2, 127)
    r2 = p.preInc(2)
    p.set(3, -128)
    r3 = p.postDec(3)
    p.set(4, 300)
    r4 = p.postInc(5)
    r5 = p.preDec(6)
    P('cptrElems', ' '.join(str(x) for x in (p.get(0), r1, p.get(1), r2, p.get(2), r3, p.get(3), p.get(4), r4, p.get(5), r5, p.get(6), p.get(4), p.ref(4).v)))
    q = CPtr_op_Add_CPtr_i(p, 3)
    q1 = CPtr_op_Increment(q); q2 = CPtr_op_Decrement(q)
    P('cptrArith', '%d ' % CPtr_Diff(q, p) + b(CPtr_op_Lt(p, q)) + b(CPtr_op_Eq(q, CPtr_op_Add_CPtr_l(p, 3))) + b(CPtr_op_Ne(p, CPtr(bytearray(8), 0))) +
      b(CPtr_op_Eq(CPtr_Null, CPtr(None, 5))) + b(CPtr_Null.get_IsNull()) + b(p.get_IsNull()) +
      ' %d %d %d %d' % (q1.O, q2.O, CPtr_op_Sub(q, 2).O, CPtr_op_Add_CPtr_ul(q, 4).O))
    P('cptrStr', CPtr_op_Add_CPtr_i(lit('text'), 1).ToString() + ' ' + CPtr_Null.ToString())
    pb = [ord('a'), ord('b'), ord('c'), 0]
    fromC = CPtr_from_Ptrc(NewPtrB_TA_l(pb, 1))
    fromUc = CPtr_from_Ptruc(NewPtrB_TA_l([ord('a'), ord('b'), ord('c'), 0], 1))
    P('fromPtr', fromC.ToString() + ' %d' % fromUc.get(1))


def ptr():
    arr = [1.0, 2.0, 3.0, 4.0, 5.0]
    p = NewPtrD_TA_l(arr, 1)
    P('ptrIdx', d(p.get_Item_i(1)) + ' ' + d(p.get_Item_l(2)) + ' ' + d(p.get_Deref()) + ' ' + d(PtrD_op_Add_PtrT_l(p, 3).get_Deref()) + ' ' + d(PtrD_op_Sub_PtrT_l(p, 1).get_Deref()))
    p.set_Item_i(0, 20.0); pi = PtrD_op_Increment(p); pi.set_Deref(30.0)
    P('ptrSet', d(arr[1]) + ' ' + d(arr[2]))
    q = PtrD_op_Add_PtrT_l(p, 2)
    P('ptrCmp', '%d ' % PtrD_op_Sub_PtrT_PtrT(q, p) + b(PtrD_op_Eq(q, NewPtrD_TA_l(arr, 3))) + b(PtrD_op_Eq(q, NewPtrD_TA_l([0.0] * 5, 3))) + b(PtrD_op_Lt(p, q)) +
      b(PtrD_op_Eq(PtrD_ZERO, NewPtrD_TA_l(None, 7))) + b(PtrD_ZERO.get_IsNull()) + b(PtrD_op_Ne(p, q)))
    h = Holder(); h.x = 1.5
    at = atX()
    f1 = NewPtrD_o_RefAtT(h, at); f2 = NewPtrD_o_RefAtT(h, at); f3 = NewPtrD_o_RefAtT(h, atX2()); f4 = NewPtrD_o_RefAtT(Holder(), at)
    f1.set_Deref(2.5)
    P('fieldPtr', d(h.x) + ' ' + d(f2.get_Deref()) + ' ' + d(PtrD_op_Add_PtrT_l(f1, 3).get_Item_i(9)) + ' ' + b(f1.get_IsNull()))
    P('fieldEq', b(PtrD_op_Eq(f1, f2)) + b(PtrD_op_Eq(f1, f3)) + b(PtrD_op_Eq(f1, f4)) + b(PtrD_op_Eq(f1, PtrD_op_Add_PtrT_l(f2, 1))) + b(PtrD_op_Eq(f1, p)))
    ps = NewPtr_TA_l([S('a'), S('b')], 0)
    P('ptrGen', s(Ptr_op_Add_PtrT_l(ps, 1).get_Deref()) + ' ' + b(Ptr_ZERO.get_IsNull()) + b(Ptr_op_Ne(ps, Ptr_op_Add_PtrT_l(ps, 1))))


def misc():
    P('min', ' '.join(d(x) for x in (M_min_d_d(0.0, -0.0), M_min_d_d(-0.0, 0.0), M_max_d_d(-0.0, 0.0), M_min_d_d(NAN, 1.0), M_min_d_d(1.0, NAN), M_max_d_d(NAN, 1.0), M_max_d_d(1.0, NAN))))
    P('clamp', d(M_clamp_d_d_d(NAN, 0.0, 1.0)) + ' ' + d(M_clamp_d_d_d(-0.0, 0.0, 1.0)) + ' ' + d(M_clamp_d_d_d(5.0, 0.0, 1.0)) + ' %d' % M_clamp_i_i_i(-5, 0, 10))
    P('unsigned', '%d %d %d %d %d %d %d' % (M_max_ul_ul(NPOS, 1), M_min_ul_ul(NPOS, 1), M_max_u_u(4294967295, 1), M_min_uc_uc(200, 100), M_max_uc_uc(200, 100), M_max_i_i(-3, 2), M_min_l_l(-3, 2)))
    P('listMinMax', d(M_max_dA([1.0, NAN, 3.0])) + ' ' + d(M_min_dA([NAN, 1.0, -2.0])) + ' %d %d' % (M_max_iA([4, 9, 2]), M_min_iA([4, 9, 2])))
    p1 = NewPair_A_B(1, S('b')); p2 = NewPair_A_B(1, S('a'))
    P('pairCmp', '%d %d %d ' % (p1.CompareTo(p2), p2.CompareTo(p1), p1.CompareTo(p1.Clone())) + b(Pair_op_Lt(p2, p1)) + ' ' + b(Pair_Eq(p1, p1.Clone())) + b(Pair_Eq(p1, p2)))
    n1 = NewPair_A_B(NAN, 1); n2 = NewPair_A_B(0.0, 1)
    P('pairNaN', '%d %d %d %d ' % (n1.CompareTo(n2), n2.CompareTo(n1), n1.CompareTo(NewPair_A_B(NAN, 2)), NewPair_A_B(-0.0, 1).CompareTo(n2)) + b(Pair_Eq(n1, NewPair_A_B(NAN, 1))))
    pc = p1.Clone(); pc.second.append_CPtr(lit('!')); P('pairClone', s(p1.second) + s(pc.second))
    pm = NewPair(NewStr, NewStr); pm.first.append_CPtr(lit('m')); P('pairMake', s(pm.first) + s(pm.second))
    p2.Assign(p1); p1.second.append_CPtr(lit('+')); P('pairAssign', s(p2.second))

    o = NewOptD()
    try:
        o.value()
    except BadOptionalAccess as x:
        P('optErr', x.what().ToString())
    P('opt', d(o.value_or(9.0)) + ' ' + b(o.has_value()))
    o.emplace(3.5); oc = o.Clone(); o.reset()
    P('opt2', d(oc.value()) + ' ' + b(o.has) + ' ' + d(o.val))
    os_ = NewOpt_T(S('x')); osc = os_.Clone(); osc.val.append_CPtr(lit('y'))
    osa = NewOpt(); osa.Assign(os_); os_.val.append_CPtr(lit('z'))
    P('optStr', s(os_.val) + s(osc.val) + s(osa.val))

    da = Arr_Make__D(3); da[1] = NAN
    db = Arr_Clone__D(da)
    P('arrD', b(Arr_Eq__D(da, db)) + ' ' + b(db is not da) + ' %d' % len(Arr_Make2__I(2, 3)[1]))
    sa = Arr_MakeWith(2, lambda: S('s'))
    sb = Arr_Clone(sa); sb[0].append_CPtr(lit('!'))
    sd = Arr_Make(2, NewStr); Arr_Assign(sd, sa); sa[1].append_CPtr(lit('?'))
    P('arrStr', s(sa[0]) + s(sb[0]) + s(sd[1]) + s(sa[1]))
    grid = [[1.0, 2.0], [3.0, 4.0]]
    gc = Arr_Clone(grid); gc[0][0] = 9.0
    P('arrShallow', d(grid[0][0]))
    vg = NewVec(MK_N); vg.push_back(grid); vgc = vg.Clone(); vgc.a[0][1][1] = 7.0
    P('vecDeep', d(grid[1][1]))

    src = Cell(S('taken'))
    got = Mv_Take(src, NewStr)
    P('take', s(got) + s(src.v))
    src2 = Cell(S('ref'))
    P('takeRef', s(Mv_TakeRef(src2)) + ' ' + b(src2.v is None))
    x_ = Cell(1.0)
    box = [6.5]
    P('back', d(Mv_Back__DD(2.5, x_, box)) + ' ' + d(x_.v))
    holder = Cell(Res('first'))
    Mv_Reset(holder, Res('second'))
    Mv_Reset(holder, holder.v)
    Mv_Reset(holder, None)
    Mv_Delete(Res('deleted'))
    Dtor_Run([Res('arr0'), None, Res('arr1')])
    pp = Cell(NewPtrD_TA_l([1.0, 2.0, 3.0], 0))
    before = Mv_PostStep_PtrTr_i__D(pp, 1)
    P('postStep', d(before.get_Deref()) + ' ' + d(pp.v.get_Deref()))
    df = NewDefer(lambda: P('defer', 'ran'))
    df.Dispose(); df.Dispose()
    dc = NewDefer(lambda: P('defer', 'cancelled?')); dc.Cancel(); dc.Dispose()

    es = [NewCppException(), NewLogicError_Str(S('logic')), NewRuntimeError_str('runtime'), NewInvalidArgument_str('inv'), NewOutOfRange_Str(S('oor')),
          NewLengthError_str('len'), NewDomainError_str('dom'), NewRangeError_str('range'), NewOverflowError_str('over'), NewBadAlloc(),
          NewBadOptionalAccess(), NewBadFunctionCall(), NewBadVariantAccess()]
    P('exc', ''.join(cstr_text(ex.what()) + '|' + b(isinstance(ex, LogicError)) + b(isinstance(ex, RuntimeError)) + ' ' for ex in es))
    P('excMsg', NewRuntimeError_str('msg here').Message)
    tsb = ''
    for tv in (1e15, 9.99999999999999e14, 123456789012345.0, 1234567890123456.0, 0.0001, 0.00001, 0.000123, 1.5, 100.0, 1e100, 1.7976931348623157e308, 5e-324,
               2.2250738585072014e-308, -2.5e-5, 0.1 + 0.2, 1.0 / 3, 2.0 / 3, 123.456, 1e21, 1e-7, -0.0, 0.0,
               1e16, 1e17, 1.5e17, 12345678901234567.0, 123456789012345678.0, 1e18, 9.99e17, 1.2e18, 99999999999999990.0, 1e-4, 9.9999e-5, 1234567.0e-9):
        tsb += cs_tostring(tv) + ' ' + 'x' + cs_tostring(tv) + cs_tostring(1) + cs_tostring(True) + '|'
    P('netText', tsb)
    ub = Cell(0); qb = Cell(0); lb = Cell(0)
    # parts C and D decode memory read into an int/long box signed: the U and Q copy-backs make it unsigned
    P('backUQ', '%d %d %s %d %s %d' % (Mv_Back__IU(7, ub, [-1]), ub.v, cs_tostring(Mv_Back__DQ(2.5, qb, [-9223372036854775808])), qb.v,
                                       'True' if Mv_Back__ZL(True, lb, [-5]) else 'False', lb.v))


# ------------------------------------------------------------------------------------------------- numeric helpers

edgeBits = [
    0x0000000000000000, 0x8000000000000000, 0x3FF0000000000000, 0xBFF0000000000000, 0x3FE0000000000000,
    0xBFE0000000000000, 0x3FF8000000000000, 0xBFF8000000000000, 0x4004000000000000, 0xC004000000000000,
    0x7FF0000000000000, 0xFFF0000000000000, 0x7FF8000000000000, 0xFFF8000000000000, 0x7FF8000000000123,
    0x7FF4000000000000, 0xFFF0000000000001, 0x0000000000000001, 0x8000000000000001, 0x000B8157268FDAF0,
    0x7FEFFFFFFFFFFFFF, 0xFFEFFFFFFFFFFFFF, 0x3FB999999999999A, 0x3FD5555555555555,
    0x47EFFFFFE0000000, 0x47EFFFFFF0000000, 0x47EFFFFFEFFFFFFF, 0xC7EFFFFFF0000000, 0x36A0000000000000,
    0x3690000000000000, 0x3690000000000001, 0x380FFFFFE0000000, 0x380FFFFFF0000000, 0x4170000010000000,
    0x4170000030000000, 0x3FF0000010000000, 0x3FF0000030000000, 0x3FF0000010000001,
]
edgeVals = [
    2147483647.0, 2147483647.5, 2147483647.9999998, 2147483648.0, -2147483648.0, -2147483648.5, -2147483648.9999998,
    -2147483649.0, -2147483649.5, 4294967295.0, 4294967295.5, 4294967296.0, 4294967297.0, 9223372036854775808.0,
    9223372036854774784.0, -9223372036854775808.0, -9223372036854777856.0, 18446744073709551616.0,
    18446744073709549568.0, 13835058055282163712.0, 1e19, -1e19, 32767.9, 32768.0, -32768.9, -32769.0, 65535.5,
    65536.0, 65537.7, 127.9, 128.0, -128.9, -129.0, 255.5, 256.0, 257.9, -1.5, -255.5, -256.5, -65535.5, 1e308,
    -1e308, 0.1, 16777217.0, 16777219.0, 123456789.123, -987654321.987, 3e9, -3e9, 5e18, -5e18, 1.5e19,
]


def edges():
    return [f64_from_bits(x) for x in edgeBits] + edgeVals


edgeLongs = [
    0, 1, -1, 127, 128, -128, -129, 255, 256, 32767, 32768, -32768, -32769, 65535, 65536, 2147483647, 2147483648,
    -2147483648, -2147483649, 4294967295, 4294967296, 16777217, 16777219, 9007199254740993, -9007199254740993,
    1152921504606846977, 9223372036854775807, -9223372036854775808, -9223372036854775807, 0x0040000040000001,
    0x0040000040000000, -0x0040000040000001, 0x7FFFFF8000000000, 0x7FFFFFC000000000, 0x7FFFFFBFFFFFFFFF,
    12345678901234567,
]
edgeULongs = [
    0, 1, 9223372036854775808, 9223372036854775809, 18446744073709551615, 0x8000008000000001,
    0x8000008000000000, 0x8000018000000000, 0xFFFFFF7FFFFFFFFF, 0xFFFFFF8000000000, 13835058055282163713,
]


def nums():
    for x in edges():
        sb = d(f32(x)) + ' ' + b(signbit(x)) + ' '
        sb += ' '.join(str(f(x)) for f in (f2i32, f2i64, f2u32, f2u64, f2i16, f2u16, f2i8, f2u8)) + ' |'
        sb += ''.join(' ' + str(f(x)) for f in (Conv_D2I, Conv_D2L, Conv_D2U, Conv_D2UL, Conv_D2S, Conv_D2US, Conv_D2SB, Conv_D2B, R_D2I, R_D2L))
        sb += ' | ' + format(f32_to_bits(f32(x)), 'x') + ' ' + cs_tostring(x)
        P('dbl ' + d(x), sb)
    for l in edgeLongs:
        P('long %d' % l, d(f32(l)) + ' ' + ' '.join(str(f(l)) for f in (i32, u32, i16, u16, i8, u8, u64)) + ' ' + d(float(l)))
    for u in edgeULongs:
        P('ulong %d' % u, d(f32(u)) + ' ' + ' '.join(str(f(u)) for f in (i32, u32, i64, i16, i8)) + ' ' + d(float(u)))
    for fb in (0, 0x80000000, 0x3F800000, 0x7F800000, 0xFF800000, 0x7FC00000, 0xFFC00000, 0x7FA00000, 0x7FC00001, 0x00000001, 0x7F7FFFFF, 0x3DCCCCCD):
        P('fbits %x' % fb, d(f32_from_bits(fb)))

    def div(a, bb):
        return a / bb if bb else fdiv0(a, bb)
    sbz = ''
    for x in edges()[:20]:
        sbz += d(div(x, 0.0)) + ' ' + d(div(x, -0.0)) + ' ' + d(div(x, 0)) + ' '
    for l in (5, -5, 0, -9223372036854775808):
        sbz += d(div(l, 0.0)) + ' ' + d(div(l, -0.0)) + ' '
    P('fdiv0', sbz)
    P('nanConst', d(fdiv0(0.0, 0.0)) + ' ' + d(QNAN) + ' ' + d(DNAN) + ' ' + d(DNAN))

    ds = [7, -7, 0, 1, -1, 6, -6, 12345678901234, -12345678901234, 9223372036854775807, -9223372036854775808, -9223372036854775807, 2147483648, -2147483649]
    dv = [2, -2, 3, -3, 1, -1, 7, -97, 9223372036854775807, -9223372036854775808, 2147483648, 4294967296]
    sbd = ''
    for a in ds:
        for bb in dv:
            if a == -9223372036854775808 and bb == -1:
                sbd += 'ovf '
                continue
            sbd += '%d,%d ' % (idiv(a, bb), irem(a, bb))
    P('idiv', sbd)
    try:
        P('idiv0', str(idiv(5, 0)))
    except ZeroDivisionError:
        P('idiv0', 'throws')
    try:
        P('irem0', str(irem(5, 0)))
    except ZeroDivisionError:
        P('irem0', 'throws')

    fm = [5.5, -5.5, 2.0, 1e300, 0.0, -0.0, INF, -INF, NAN, f64_from_bits(0xFFF8000000000000), 5e-324, 1e-310, 7.0, 0.3]
    P('fmod', ''.join(d(fmod(a, bb)) + ' ' for a in fm for bb in fm))


# ------------------------------------------------------------------------------------------------- more cases

class HolderB:
    __slots__ = ('flag',)

    def __init__(self):
        self.flag = False


def vbool(v):
    return '%d/%d:' % (v.size(), v.capacity()) + ''.join(' ' + b(v.a[i]) for i in range(v.n))


def vlong(v):
    return '%d/%d:' % (v.size(), v.capacity()) + ''.join(' %d' % v.a[i] for i in range(v.n))


def vp(v):
    return '%d/%d:' % (v.size(), v.capacity()) + ''.join(' %d%s' % (v.a[i].first, s(v.a[i].second)) for i in range(v.n))


def more():
    t = S('  a,b;;c  ')
    P('s1', ' '.join(str(x) for x in (
        t.find_first_not_of_i_ul(32, 0), t.find_first_not_of_CPtr_ul(lit(' a'), 0), t.find_first_not_of_CPtr_ul(lit(' '), 20),
        t.find_last_not_of_CPtr_ul(lit(' '), NPOS), t.find_last_not_of_CPtr_ul(lit(' c'), 7), t.find_last_not_of_i_ul(32, 1),
        NewStr().find_last_not_of_CPtr_ul(lit(' '), NPOS), t.find_first_not_of_CPtr_ul(lit(''), 3), t.find_last_not_of_CPtr_ul(lit(''), 4),
        t.find_first_not_of_CPtr_ul(lit(' abc,;'), 0), t.find_first_not_of_i_ul(ord('x'), 9), t.find_last_not_of_i_ul(ord('x'), NPOS))))
    P('s2', ' '.join(str(x) for x in (
        t.rfind_CPtr_ul(lit(';'), 6), t.rfind_CPtr_ul(lit(';;'), NPOS), t.rfind_CPtr_ul(lit(''), 4), t.rfind_CPtr_ul(lit(''), 99),
        t.rfind_i_ul(ord(';'), 5), t.rfind_i_ul(ord('x'), NPOS), t.find_CPtr_ul(lit(';'), 6), t.find_CPtr_ul(lit('c '), 0),
        t.find_CPtr_ul(lit('zz'), 11), t.find_i_ul(ord(';'), 11), t.find_CPtr_ul(lit(''), 10), t.rfind_CPtr_ul(lit('  '), 0),
        t.find_first_of_CPtr_ul(lit(';,'), 4), t.find_last_of_CPtr_ul(lit('ab'), NPOS), t.find_last_of_i_ul(ord(';'), 5),
        t.find_first_of_CPtr_ul(lit(''), 0), t.find_last_of_CPtr_ul(lit('xyz'), 3))))
    u = S('ab'); u.append_Str(u); u.append_Str(u); P('selfApp', s(u) + ' %d' % len(u.b))
    u.insert_ul_Str(1, u); P('selfIns', s(u) + ' %d ' % len(u.b) + s(Str_Cat_Str_Str(u, u)))
    u.replace_ul_ul_CPtr(2, 5, lit('XY')); P('repl', s(u)); u.replace_ul_ul_Str(0, 100, S('whole')); P('repl2', s(u))
    u.replace_ul_ul_CPtr(5, 0, lit('!')); P('repl3', s(u))
    P('substrEdge', s(u.substr(u.size(), NPOS)) + s(u.substr(2, 0)) + s(u.substr(1, 2)))
    P('cmp2', '%d %d %d %d %d' % (S('b').compare_CPtr(lit('abc')), NewStr().compare_Str(NewStr()), S('abc').compare_ul_ul_Str(1, 2, S('bc')),
                                  S('abc').compare_ul_ul_Str(0, 9, S('abcd')), S('abé').compare_CPtr(lit('abz'))))
    e = S('0123456789'); e.erase(3, 2); P('erase2', s(e)); e.erase(5, NPOS); P('erase3', s(e)); e.erase(0, 0); P('erase4', s(e) + ' %d' % e.n)
    e.resize(8, ord('z')); P('resz', s(e)); e.resize(2, ord('q')); P('resz2', s(e)); e.resize(4); P('resz3', hexb(e.b, e.n))
    P('emptyOps', b(NewStr().EqLit(lit(''))) + b(NewStr().starts_with_CPtr(lit(''))) + b(NewStr().starts_with_Str(NewStr())) +
      b(S('x').starts_with_CPtr(lit('xy'))) + b(NewStr().Equals(NewStr())) + b(S('a').Equals(S('b'))))
    hi = NewStr(); hi.push_back(-61); hi.push_back(0xA9); hi.push_back(300)
    P('hiBytes', hexb(hi.b, hi.n) + ' %d %d %d %d %d' % (hi.Char(0), hi.Char(2), hi.front(), hi.back(), hi.GetHashCode()))
    longS = NewStr()
    for i in range(300):
        longS.push_back(33 + i % 90)
    P('longHash', '%d %d' % (longS.GetHashCode(), len(longS.b)))
    cl = S('abc'); cl.clear(); cp = cl.c_str(); P('clearCstr', '%d %d %d' % (cp.get(0), cl.n, len(cl.b)))
    ap = NewStr(); ap.append_CPtr(lit('x')); ap.append_i(65); ap.append_Str_ul_ul(S('12345'), 1, 99); P('appendMix', s(ap))
    aptr = NewStr(); src = [ord('h'), 0, ord('i'), ord('!')]; aptr.append_CPtr_ul(CPtr(src, 0), 4); P('appendNul', hexb(aptr.b, aptr.n) + ' %d' % aptr.n)
    asg = S('long string here'); asg.assign_CPtr_ul(CPtr(src, 2), 2); P('assignCul', s(asg) + ' %d' % len(asg.b))
    sw = S('starts')
    P('startsC', b(sw.starts_with_CPtr(lit('sta'))) + b(sw.starts_with_CPtr(lit('starts!'))) + b(sw.starts_with_Str(S('starts'))) + b(sw.EqLit(lit('starts'))) + b(sw.EqLit(lit('start'))))
    P('catMix', s(Str_Cat_CPtr_Str(lit(''), NewStr())) + s(Str_Cat_Str_CPtr(NewStr(), lit('x'))) + s(Str_Cat_Str_i(S('a'), 0x62)) + str(len(s(Str_Cat_Str_i(S('a'), -1)))))

    vd2 = NewVecD(); vd2.reserve(3); vd2.push_back(1.0); vd2.push_back(2.0); vd2.push_back(3.0); vd2.push_back(4.0); P('vcap', vd(vd2))
    vd2.insert_IterT_IterT_IterT(vd2.begin(), IterD_op_Add(vd2.begin(), 1), vd2.end()); P('vselfins', vd(vd2))
    vd2.erase_IterT_IterT(vd2.begin(), vd2.end()); P('veraseall', vd(vd2))
    vd2.resize_ul(2); vd2.push_back(5.0); vd2.resize_ul(1); vd2.resize_ul_T(4, 9.0); P('vresz', vd(vd2))
    vd2.assign_ul_T(10, 0.25); P('vassignBig', vd(vd2))
    vd2.assign_ul_T(2, 0.5); P('vassignSmall', vd(vd2))
    vd2.MoveFrom_TA([7.0, 8.0]); P('vmoveTA', vd(vd2))
    vd2.insert_IterT_ul_T(IterD_op_Add(vd2.begin(), 1), 2, 1.5); P('vinsN', vd(vd2))
    bk = vd2.back(); fr = vd2.front(); dat = vd2.data(); P('vends', d(bk) + ' ' + d(fr) + ' ' + d(dat.get_Item_i(1)) + ' ' + d(vd2.at(3)))
    vd2.emplace_back(6.5); P('vemplace', vd(vd2))
    vb2 = NewVecZ(); vb2.push_back(True); vb2.resize_ul(3); vb2.pop_back(); vb2.insert_IterT_T(vb2.begin(), True); P('vbool', vbool(vb2) + ' ' + b(VecZ_Eq(vb2, vb2.Clone())))
    vl2 = NewVecL_ul(2); vl2.a[1] = -9223372036854775808; vl2.push_back(9223372036854775807); vl2.erase_IterT(vl2.begin()); P('vlong', vlong(vl2))
    pv = NewVec(lambda: NewPair(MK_I, NewStr)); pv.push_back(NewPair_A_B(1, S('one'))); pv.push_back(NewPair_A_B(2, S('two')))
    pvc = pv.Clone(); pvc.a[0].second.append_CPtr(lit('!')); pvc.a[1].first = 22
    P('vpair', vp(pv) + ' |' + vp(pvc) + ' ' + b(Vec_Eq(pv, pv)) + b(Vec_Eq(pv, pvc)))
    vs2 = NewVec(NewStr); vs2.resize_ul(2); vs2.a[0].append_CPtr(lit('x')); P('vstrResize', vs(vs2) + ' ' + b(vs2.a[0] is not vs2.a[1]))
    vs2.insert_IterT_ul_T(Iter_op_Add(vs2.begin(), 1), 2, S('c')); vs2.a[1].append_CPtr(lit('1')); P('vstrInsN', vs(vs2))
    try:
        vs2.at(5)
    except OutOfRange as x:
        P('vstrAt', cstr_text(x.what()))
    sbr = ''
    ri = vs2.rbegin()
    while RevIter_op_Ne(ri, vs2.rend()):
        sbr += s(ri.get_Deref())
        ri = RevIter_op_Increment(ri)
    P('vstrRev', sbr)
    vs2.erase_IterT(Iter_op_Add(vs2.begin(), 1)); vs2.pop_back(); P('vstrErase', vs(vs2))
    vv = NewVec_ul_T(2, NewVecD_TA([1.0]), NewVecD); vv.a[0].push_back(2.0); P('vvec', vd(vv.a[0]) + ' ' + vd(vv.a[1]))
    vvc = vv.Clone(); vvc.a[1].push_back(3.0); P('vvecClone', vd(vv.a[1]) + ' ' + vd(vvc.a[1]))
    vs3 = NewVec_TA([S('a'), S('b')], NewStr); vs4 = NewVec(NewStr); vs4.Assign_VecT(vs3); vs3.a[0].append_CPtr(lit('!')); P('vstrAssign', vs(vs4) + ' ' + b(Vec_Eq(vs3, vs4)))
    vs4.MoveFrom_TA([S('m')]); P('vstrMoveTA', vs(vs4))
    vs4.swap(vs3); P('vstrSwap', vs(vs4) + ' ' + vs(vs3))
    vd3 = NewVecD_TA([NAN, 0.0]); P('vEqNaN', b(VecD_Eq(vd3, NewVecD_TA([NAN, 0.0]))) + b(VecD_Eq(vd3, NewVecD_TA([NAN, 1.0]))))

    ia = [1, 2, 3, 4]
    pi = NewPtrI_TA_l(ia, 1)
    pi.set_Item_i(1, 30); PtrI_op_Add_PtrT_l(pi, 2).set_Item_i(0, 40)
    P('ptrI', '%d %d %d %d ' % (ia[2], ia[3], PtrI_op_Sub_PtrT_l(pi, 1).get_Deref(), PtrI_op_Sub_PtrT_PtrT(PtrI_op_Add_PtrT_l(pi, 2), pi)) +
      b(PtrI_op_Ne(pi, NewPtrI_TA_l(ia, 1))) + b(PtrI_op_Lt(pi, PtrI_op_Add_PtrT_l(pi, 1))) + b(PtrI_op_Eq(pi, NewPtrI_TA_l([0] * 4, 1))))
    hb = HolderB(); bx = Box_Of__Z(lambda: AttrRef(hb, 'flag')); bx.set_V(True); arrb = [False, False]; bx2 = NewBoxZ_TA_l(arrb, 1); bx2.set_V(True)
    P('box', b(hb.flag) + b(bx.get_V()) + b(arrb[1]) + b(bx2.get_V()))
    od = NewOptD(); od.emplace(2.5); od2 = NewOptD(); od2.Assign(od); od.reset(); od3 = NewOptD(); od3.MoveFrom(od2)
    P('optD', d(od.val) + b(od.has) + d(od2.val) + b(od3.has) + d(od3.value_or(1.0)) + ' ' + d(NewOptD_T(4.5).value()) + ' ' + d(NewOptD().Clone().val))
    xd = Cell(3.5); taken = Mv_Take__D(xd); P('takeD', d(taken) + ' ' + d(xd.v))
    cpm = Cell(CPtr(bytearray([1, 2, 3]), 0)); cpold = Mv_PostStep_CPtrr_i(cpm, 2); P('postStepC', '%d %d %d' % (cpold.O, cpm.v.O, cpm.v.get(0)))
    itv = NewVecI_TA([5, 6, 7]); itp = Cell(itv.begin()); itold = Mv_PostStep_IterTr_i__I(itp, 1); P('postStepI', '%d %d' % (itold.get_Deref(), itp.v.get_Deref()))
    vr = NewVec(MK_N); vr.push_back(Res('v0')); vr.push_back(Res('v1')); Dtor_Run(vr)
    orr = NewOpt_T(Res('opt')); Dtor_Run(orr); Dtor_Run(NewOpt())
    Dtor_Run([[Res('nested')]])

    fa = [0.0] * 5; Arr_Fill__D(fa, 1, 3, 2.5); fb2 = [0.0] * 5; Arr_Assign__D(fb2, [9.0, 8.0])
    P('arrFill', d(fa[0]) + d(fa[1]) + d(fa[2]) + d(fa[3]) + ' ' + d(fb2[0]) + d(fb2[1]) + d(fb2[2]) + ' ' + b(Arr_Eq__I([1, 2], [1, 2])) + b(Arr_Eq__I([1, 2], [1])))
    sarr = [None] * 3; shared = S('sh'); Array_Fill(sarr, shared); shared.append_CPtr(lit('!')); P('arrayFill', s(sarr[0]) + s(sarr[2]) + b(sarr[0] is sarr[1]))
    sfill = [None] * 3; Arr_Fill(sfill, 0, 3, shared); shared.append_CPtr(lit('?')); P('arrFillStr', s(sfill[0]) + b(sfill[0] is sfill[1]))
    P('arrMake2', '%d %s %d ' % (len(Arr_Make2__D(3, 2)), d(Arr_Make2__D(3, 2)[2][1]), Arr_Make(2, NewStr)[1].n) + b(Arr_Eq([S('q')], [S('q')])))

    sb8 = [-128, 127, -1, 0]
    cs8 = CPtr_from_Ptrc(NewPtrB_TA_l(sb8, 0))
    pd = cs8.preInc(0); pd2 = cs8.postDec(2)
    P('cptrS', '%d %d %d %d %d ' % (pd, pd2, cs8.get(0), cs8.get(1), cs8.get(2)) + b(CPtr_Diff(CPtr_op_Add_CPtr_i(cs8, 2), cs8) == 2) +
      b(CPtr_op_Gt(CPtr_op_Add_CPtr_i(cs8, 2), cs8)) + b(CPtr_op_Le(cs8, cs8)) + b(CPtr_op_Ge(CPtr_op_Add_CPtr_i(cs8, 1), CPtr_op_Add_CPtr_i(cs8, 2))))


# ------------------------------------------------------------------------------------------------- randomized

_rs = [12345]
_M64 = 0xFFFFFFFFFFFFFFFF


def Next():
    z = _rs[0] = (_rs[0] + 0x9E3779B97F4A7C15) & _M64
    z = ((z ^ (z >> 30)) * 0xBF58476D1CE4E5B9) & _M64
    z = ((z ^ (z >> 27)) * 0x94D049BB133111EB) & _M64
    return z ^ (z >> 31)


def Rnd(k):
    return Next() % k


def RStr():
    n = Rnd(9)
    x = NewStr()
    for _ in range(n):
        x.push_back(ord('ab c'[Rnd(4)]))
    return x


def RPos():
    return NPOS if Rnd(8) == 0 else Rnd(12)


def fuzz():
    for it in range(400):
        a = RStr(); x = RStr(); pos = Rnd(12); rpos = RPos(); ch = ord('ab c'[Rnd(4)])
        xc = x.c_str()
        sb = ' '.join(str(v) for v in (
            a.find_Str_ul(x, pos), a.find_CPtr_ul(xc, pos), a.find_i_ul(ch, pos),
            a.rfind_Str_ul(x, rpos), a.rfind_CPtr_ul(xc, rpos), a.rfind_i_ul(ch, rpos),
            a.find_first_of_CPtr_ul(xc, pos), a.find_first_of_Str_ul(x, pos), a.find_first_not_of_CPtr_ul(xc, pos), a.find_first_not_of_Str_ul(x, pos),
            a.find_first_not_of_i_ul(ch, pos),
            a.find_last_of_CPtr_ul(xc, rpos), a.find_last_of_Str_ul(x, rpos), a.find_last_not_of_CPtr_ul(xc, rpos), a.find_last_not_of_Str_ul(x, rpos),
            a.find_last_not_of_i_ul(ch, rpos),
            a.compare_Str(x), a.compare_CPtr(xc)))
        sb += ' ' + b(a.starts_with_Str(x)) + b(a.starts_with_CPtr(xc)) + b(a.ends_with_Str(x)) + b(a.ends_with_CPtr(xc)) + b(a.Equals(x)) + b(a.EqLit(xc)) + b(a.contains_Str(x))
        sb += ' %d %d' % (a.GetHashCode(), a.CompareTo(x))
        p2 = Rnd(a.size() + 1)
        e1 = a.Clone(); e1.erase(p2, Rnd(5)); e2 = a.Clone(); e2.insert_ul_Str(p2, x); e3 = a.Clone(); e3.replace_ul_ul_Str(p2, Rnd(5), x)
        e4 = a.Clone(); e4.append_Str(x); e5 = a.substr(p2, Rnd(6)); e6 = a.Clone(); e6.resize(Rnd(12), ch)
        e7 = NewStr(); e7.Assign(a); e7.append_CPtr(xc); e7.append_CPtr_ul(xc, x.size()); e7.push_back(ch)
        sb += ' ' + s(e1) + s(e2) + s(e3) + s(e4) + s(e5) + s(e6) + s(e7) + '%d %d %d %d' % (len(e2.b), len(e4.b), len(e6.b), len(e7.b))
        P('fz%d' % it, sb)

    v = NewVecI()
    for it in range(400):
        op = Rnd(12); n = v.n
        if op == 0:
            v.push_back(Rnd(100))
        elif op == 1:
            if n > 0:
                v.pop_back()
        elif op == 2:
            v.insert_IterT_T(IterI_op_Add(v.begin(), Rnd(n + 1)), Rnd(100))
        elif op == 3:
            src = NewVecI_TA([Rnd(9), Rnd(9), Rnd(9)]); k = Rnd(4)
            v.insert_IterT_IterT_IterT(IterI_op_Add(v.begin(), Rnd(n + 1)), src.begin(), IterI_op_Add(src.begin(), k))
        elif op == 4:
            pos = IterI_op_Add(v.begin(), Rnd(n + 1)); cnt = Rnd(4)
            v.insert_IterT_ul_T(pos, cnt, Rnd(100))
        elif op == 5:
            if n > 0:
                v.erase_IterT(IterI_op_Add(v.begin(), Rnd(n)))
        elif op == 6:
            p = Rnd(n + 1); q = p + Rnd(n - p + 1)
            v.erase_IterT_IterT(IterI_op_Add(v.begin(), p), IterI_op_Add(v.begin(), q))
        elif op == 7:
            v.resize_ul(Rnd(20))
        elif op == 8:
            c = Rnd(20)
            v.resize_ul_T(c, Rnd(100))
        elif op == 9:
            v.reserve(Rnd(40))
        elif op == 10:
            if Rnd(3) == 0:
                v.shrink_to_fit()
            else:
                c = Rnd(10)
                v.assign_ul_T(c, Rnd(100))
        elif op == 11:
            if Rnd(4) == 0:
                v.clear()
            else:
                pos = IterI_op_Add(v.begin(), Rnd(n + 1))
                v.insert_IterT_TA(pos, [Rnd(9), Rnd(9)])
        P('vf%d' % it, '%d ' % op + vi(v))

    w = NewVec(NewStr); pool = NewVec(NewStr)
    for it in range(200):
        op = Rnd(8); n = w.n
        if op == 0:
            pool.push_back(RStr()); w.push_back(pool.back().Clone())
        elif op == 1:
            if n > 0:
                w.pop_back()
        elif op == 2:
            if pool.n > 0:
                k = Rnd(pool.n + 1)
                w.insert_IterT_IterT_IterT(Iter_op_Add(w.begin(), Rnd(n + 1)), pool.begin(), Iter_op_Add(pool.begin(), k))
        elif op == 3:
            pos = Iter_op_Add(w.begin(), Rnd(n + 1)); cnt = Rnd(3)
            w.insert_IterT_ul_T(pos, cnt, RStr())
        elif op == 4:
            if n > 0:
                p = Rnd(n)
                w.erase_IterT_IterT(Iter_op_Add(w.begin(), p), Iter_op_Add(w.begin(), p + Rnd(n - p + 1)))
        elif op == 5:
            w.resize_ul(Rnd(8))
        elif op == 6:
            c = Rnd(8)
            w.resize_ul_T(c, RStr())
        elif op == 7:
            c = w.Clone()
            if c.n > 0:
                c.a[Rnd(c.n)].append_CPtr(lit('#'))
            w.Assign_VecT(c if Rnd(2) == 0 else w)
        if pool.n > 0:
            pool.a[Rnd(pool.n)].append_CPtr(lit('*'))
        if w.n > 0:
            w.a[Rnd(w.n)].push_back(ord('+'))
        P('wf%d' % it, '%d ' % op + vs(w))

    for it in range(300):
        bits = Next(); x = f64_from_bits(bits)
        y = float(i64(Next())) / float(1 << Rnd(64))
        l = i64(Next()) >> Rnd(64)
        m = i64(Next()) >> Rnd(64)
        sb = ' '.join(str(t) for t in (d(f32(x)), d(f32(y)), f2i32(y), f2i64(y), f2u32(y), f2u64(y), f2i16(y), f2i8(y), Conv_D2UL(y), Conv_D2I(y),
                                       Conv_D2L(x), d(f32(l)), d(f32(u64(l))))) + ' '
        if m != 0 and not (l == -9223372036854775808 and m == -1):
            sb += '%d,%d' % (idiv(l, m), irem(l, m))
        else:
            sb += 'z'
        sb += ' ' + cs_tostring(x) + ' ' + cs_tostring(y) + ' ' + cs_tostring(y * 1e-300) + ' ' + cs_tostring(float(l)) + ' ' + cs_tostring(x * 1e30)
        sb += ' ' + d(fmod(x, y)) + ' ' + d(fmod(y, x)) + ' ' + d(y / 0.0 if 0.0 else fdiv0(y, 0.0)) + ' ' + d(fdiv0(x, -0.0)) + ' %d %d' % (i32(l), u16(l))
        P('nf%d' % it, sb)


main()
