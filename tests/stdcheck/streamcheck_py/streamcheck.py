# streamcheck.py - the cases of streamcheck.cpp / streamcheck.cs through the Python runtime's streams and file system
# (py/cpp: streams.py, fs.py, rfs.py, fsnet.py of part D, over parts A and C), statement for statement as
# streamcheck.cs. Section a must equal the C# and Go outputs (and the C++ output but for the one glibc %#g quirk);
# section b the C# and Go outputs (the C# port's own behaviour); section c the C++ and C# outputs.
# Run from tests/stdcheck (paths are relative to it): python3 -I streamcheck_py/streamcheck.py a|b|c
# Written by Claude (Anthropic) for the SWMM6 ports; see AI-USE.md.
import os
import struct
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', '..', 'py'))
import cpp  # noqa: E402

from cpp import (  # noqa: E402
    CPtr, Fs_absolute, Fs_canonical_FsPath, Fs_canonical_FsPath_ErrorCode, Fs_create_directories_FsPath,
    Fs_create_directories_FsPath_ErrorCode, Fs_create_hard_link, Fs_current_path, Fs_equivalent_FsPath_FsPath,
    Fs_equivalent_FsPath_FsPath_ErrorCode, Fs_exists_FileStatus, Fs_exists_FsPath, Fs_exists_FsPath_ErrorCode,
    Fs_file_size_FsPath, Fs_file_size_FsPath_ErrorCode, Fs_is_directory_FileStatus, Fs_is_directory_FsPath,
    Fs_is_regular_file_FileStatus, Fs_is_regular_file_FsPath, Fs_is_symlink, Fs_remove_FsPath,
    Fs_remove_FsPath_ErrorCode, Fs_rename_FsPath_FsPath, Fs_rename_FsPath_FsPath_ErrorCode, Fs_status_FsPath,
    Fs_symlink_status_FsPath, Fs_symlink_status_FsPath_ErrorCode, Fs_temp_directory_path,
    Fs_weakly_canonical_FsPath, Fs_weakly_canonical_FsPath_ErrorCode, Ios_app, Ios_beg, Ios_binary, Ios_cur,
    Ios_end, Ios_adjustfield, Ios_fixed, Ios_floatfield, Ios_in, Ios_left, Ios_out, Ios_scientific, Ios_trunc,
    NewFStream, NewFsPath, NewFsPath_CPtr, NewFsPath_Str, NewIFStream_FsPath_i, NewIFStream_Str_i,
    NewIStringStream, NewIStringStream_Str, NewOFStream, NewOFStream_CPtr_i, NewOFStream_FsPath_i,
    NewOFStream_Str_i, NewOStringStream, NewOStringStream_Str, NewPtrD_TA_l, NewPtrI_TA_l, NewStr, NewStr_str,
    R_Lit, SetFill, SetPrec, SetW, Streams_cout, Streams_getline_IStream_Str, Streams_getline_IStream_Str_c,
    Streams_setfill, Streams_setw, f32,
)

sb = []


class Cell(object):
    __slots__ = ('v',)

    def __init__(self, v):
        self.v = v


class _ErrorCode(object):
    """std::error_code as cs/runtime/Misc.cs has it (a stand-in only while part C has no ErrorCode)."""
    __slots__ = ('v',)

    def __init__(self):
        self.v = 0

    def Bool(self):
        return self.v != 0

    def value(self):
        return self.v

    def clear(self):
        self.v = 0

    def message(self):
        m = {0: 'Success', 2: 'No such file or directory', 13: 'Permission denied', 17: 'File exists',
             20: 'Not a directory', 21: 'Is a directory', 95: 'Operation not supported'}
        return NewStr_str(m.get(self.v, 'Unknown error %d' % self.v))


NewErrorCode = getattr(cpp, 'NewErrorCode', None) or _ErrorCode


def Bits(u):
    return struct.unpack('<d', struct.pack('<Q', u))[0]


def St(s):
    return ('g' if s.good() else '-') + ('e' if s.eof() else '-') + ('f' if s.fail() else '-') + ('b' if s.bad() else '-')


def G(v):
    o = NewOStringStream()
    o.Put_d(v)
    return o.str_().ToString()


def S(s):
    return NewStr_str(s)


def B(b):
    return '1' if b else '0'


def W(s):
    sb.append(s)


def T(x):
    """C#'s string concatenation of a runtime value: its ToString()."""
    return x.ToString()


def c(ch):
    return ord(ch)


def main():
    sec = sys.argv[1] if len(sys.argv) > 1 else 'a'
    if sec == 'a':
        A()
    elif sec == 'b':
        Bsec()
    else:
        C()
    data = ''.join(sb).encode('latin-1')
    Streams_cout.write_CPtr_l(CPtr(bytearray(data), 0), len(data))


def C():
    f = NewIFStream_Str_i(S('build/streamcheck.bin'), Ios_in | Ios_binary)
    b = [0]
    f.read_PtrT_l__I(NewPtrI_TA_l(b, 0), 4)
    f.seekg(4, Ios_cur)
    f.read_PtrT_l__I(NewPtrI_TA_l(b, 0), 4)
    W('cur at8 %d tellg %d %s\n' % (b[0], f.tellg(), St(f)))


def A():
    vals = [0.0, Bits(0x8000000000000000), 1.0, -1.0, 0.1, 0.5, 1.5, 2.5, 123.456, 1e-5,
            1.23456789e-7, 1e21, 1.7976931348623157e308, Bits(1), 2.2250738585072014e-308, 1.0 / 3.0, 2.0 / 3.0, 100.0,
            1e15, 123456789012345678.0, 0.000123456, 9.999999, 99.995, 0.125, Bits(0x7ff8000000000000),
            Bits(0x7ff0000000000000), Bits(0xfff0000000000000), 1e100, 5e-310, 3.14159265358979, -0.0004567,
            1234567.0, 0.95, 0.05, 1e-300, 6.02214076e23]
    precs = [0, 1, 2, 3, 4, 6, 8, 10, 15, 16, 17, 20, -1]
    for v in vals:
        for mode in range(4):
            for sp in range(2):
                for p in precs:
                    if mode == 3 and p != 6:
                        continue
                    o = NewOStringStream()
                    if mode == 1:
                        o.Manip_fixed()
                    if mode == 2:
                        o.Manip_scientific()
                    if mode == 3:
                        o.setf_i_i(Ios_fixed | Ios_scientific, Ios_floatfield)
                    if sp == 1:
                        o.Manip_showpoint()
                    o.precision_l(p)
                    o.Put_d(v)
                    W('d %d%d %d [%s]\n' % (mode, sp, p, T(o.str_())))
        f = NewOStringStream()
        f.Put_f(f32(v))
        W('f [' + T(f.str_()) + ']\n')
    o = NewOStringStream()
    o.Put_c(c('[')).Manip_setw(10).Put_d(3.5).Put_CPtr(R_Lit('][')).Manip_left().Manip_setw(8).Manip_setfill(c('*')).Put_i(-42).Put_c(c(']'))
    o.Put_c(c('[')).Manip_right().Manip_setw(6).Put_CPtr(R_Lit('ab')).Put_CPtr(R_Lit('][')).Manip_setw(2).Put_CPtr(R_Lit('abcdef')).Put_c(c(']'))
    o.Put_c(c('[')).Manip_setw(5).Manip_setfill(c('0')).Put_u(7).Put_CPtr(R_Lit('][')).Put_i(123).Put_c(c(']'))
    o.Manip_boolalpha().Put_c(c('[')).Put_b(True).Put_c(c(' ')).Put_b(False).Put_c(c(']')).Manip_noboolalpha().Put_c(c('[')).Put_b(True).Put_b(False).Put_c(c(']'))
    o.Put_c(c('[')).Manip_setw(12).Manip_setfill(c(' ')).Manip_quoted_Str(S('a"b\\c')).Put_c(c(']'))
    o.Put_c(c('[')).Manip_quoted_CPtr(R_Lit('plain')).Put_c(c(']'))
    o.Put_c(c('[')).Manip_fixed().Manip_setprecision(3).Manip_setw(9).Put_d(2.0 / 3.0).Put_c(c(']'))
    o.Put_c(c('[')).Manip_defaultfloat().Manip_setprecision(4).Put_d(2.0 / 3.0).Put_c(c(']'))
    o.Put_c(c('[')).Put_l(-9223372036854775808).Put_c(c(' ')).Put_ul(18446744073709551615).Put_c(c(' ')).Put_s(-5).Put_c(c(']'))
    o.Put_c(c('[')).Put_c(c('x')).Put_c(65).Put_c(c(']'))
    W('w ' + T(o.str_()) + '\n')

    o = NewOStringStream_Str(S('abcdef'))
    o.Put_CPtr(R_Lit('XY'))
    W('init [%s] tellp %d\n' % (T(o.str_()), o.tellp()))
    q = NewOStringStream()
    q.Put_CPtr(R_Lit('hello world'))
    q.seekp(6)
    q.Put_CPtr(R_Lit('W'))
    q.seekp(-1, Ios_end)
    q.Put_CPtr(R_Lit('D'))
    q.seekp(2, Ios_beg)
    q.seekp(1, Ios_cur)
    q.Put_CPtr(R_Lit('L'))
    W('seekp [%s] tellp %d\n' % (T(q.str_()), q.tellp()))

    is_ = NewIStringStream_Str(S('  3.25 -7 abc\nline two\n\nlast'))
    d = Cell(-1.0)
    i = Cell(-1)
    s = NewStr()
    is_.Get_dr(d)
    W('x d ' + G(d.v) + ' ' + St(is_) + '\n')
    is_.Get_ir(i)
    W('x i %d %s\n' % (i.v, St(is_)))
    is_.Get_Str(s)
    W('x s ' + T(s) + ' ' + St(is_) + '\n')
    line = NewStr()
    for k in range(4):
        Streams_getline_IStream_Str(is_, line)
        W('x line [' + T(line) + '] ' + St(is_) + '\n')

    is_ = NewIStringStream_Str(S('12 4294967296 -2147483648 2147483648 -9223372036854775808 9000000000000000000 4294967295 7'))
    a, b, cc, e = Cell(-1), Cell(-1), Cell(-1), Cell(-1)
    is_.Get_ir(a).Get_ir(b)
    W('x ints %d %d %s\n' % (a.v, b.v, St(is_)))
    is_.clear()
    is_.Get_ir(cc).Get_ir(e)
    W('x ints %d %d %s\n' % (cc.v, e.v, St(is_)))
    is_.clear()
    L = Cell(-1)
    is_.Get_lr(L)
    W('x long %d %s\n' % (L.v, St(is_)))
    is_.clear()
    U = Cell(0)
    u = Cell(0)
    is_.Get_ulr(U)
    W('x ulong %d %s\n' % (U.v, St(is_)))
    is_.clear()
    is_.Get_ur(u)
    W('x uint %d %s\n' % (u.v, St(is_)))

    is_ = NewIStringStream_Str(S('a,b,,c'))
    f = NewStr()
    while Streams_getline_IStream_Str_c(is_, f, c(',')).ok():
        W('x field [' + T(f) + '] ' + St(is_) + '\n')
    W('x field end ' + St(is_) + '\n')

    is_ = NewIStringStream_Str(S('   \t\n  tail'))
    is_.Manip_ws()
    rest = NewStr()
    Streams_getline_IStream_Str(is_, rest)
    W('x ws [' + T(rest) + '] ' + St(is_) + '\n')
    e2 = NewIStringStream_Str(S('   '))
    e2.Manip_ws()
    W('x ws-end ' + St(e2) + '\n')

    is_ = NewIStringStream_Str(S('0123456789'))
    buf = bytearray(16)
    is_.read_CPtr_l(CPtr(buf, 0), 4)
    W('r [' + buf[0:4].decode('latin-1') + '] gc %d %s tellg %d\n' % (is_.gcount(), St(is_), is_.tellg()))
    W('r peek %d' % is_.peek())
    W(' get %d' % is_.get_())
    W(' get %d %s\n' % (is_.get_(), St(is_)))
    is_.ignore(3, c('8'))
    W('r ignore tellg %d %s\n' % (is_.tellg(), St(is_)))
    is_.seekg(-2, Ios_cur)
    W('r seekg cur tellg %d get %d\n' % (is_.tellg(), is_.get_()))
    is_.seekg(-3, Ios_end)
    W('r seekg end tellg %d get %d\n' % (is_.tellg(), is_.get_()))
    is_.read_CPtr_l(CPtr(buf, 0), 10)
    W('r short gc %d %s\n' % (is_.gcount(), St(is_)))
    is_.clear()
    is_.seekg(0, Ios_beg)
    W('r rewind tellg %d get %d %s\n' % (is_.tellg(), is_.get_(), St(is_)))
    is_.ignore(100, -1)
    W('r ignore-all ' + St(is_) + '\n')
    W('r get-end %d' % is_.get_())
    W(' ' + St(is_) + '\n')
    W('r str [' + T(is_.str_()) + ']\n')

    f = NewOFStream_Str_i(S('build/streamcheck.bin'), Ios_trunc | Ios_binary)
    W('f open ' + B(f.is_open()) + ' ' + St(f) + '\n')
    ai = [1, -2, 300000]
    ad = [1.5, -0.25]
    f.write_PtrT_l__I(NewPtrI_TA_l(ai, 0), 12)
    f.write_PtrT_l__D(NewPtrD_TA_l(ad, 0), 16)
    f.write_CPtr_l(R_Lit('MAGIC'), 5)
    W('f tellp %d\n' % f.tellp())
    f.Put_d(3.25).Put_c(c(' ')).Put_i(42).Put_c(c('\n'))
    W('f tellp %d\n' % f.tellp())
    f.close()
    W('f closed ' + B(f.is_open()) + '\n')

    f = NewOFStream_Str_i(S('build/streamcheck.bin'), Ios_app)
    f.Put_CPtr(R_Lit('tail\n'))
    f.Dispose()

    f = NewIFStream_Str_i(S('build/streamcheck.bin'), Ios_in | Ios_binary)
    W('f in open ' + B(f.is_open()) + ' ' + St(f) + '\n')
    ai = [0, 0, 0]
    f.read_PtrT_l__I(NewPtrI_TA_l(ai, 0), 12)
    W('f ints %d %d %d gc %d tellg %d\n' % (ai[0], ai[1], ai[2], f.gcount(), f.tellg()))
    ad = [0.0, 0.0]
    f.read_PtrT_l__D(NewPtrD_TA_l(ad, 0), 16)
    W('f dbl ' + G(ad[0]) + ' ' + G(ad[1]) + ' tellg %d\n' % f.tellg())
    m = bytearray(6)
    f.read_CPtr_l(CPtr(m, 0), 5)
    W('f magic ' + bytes(m[:m.index(0)]).decode('latin-1') + '\n')
    x = Cell(0.0)
    n = Cell(0)
    t = NewStr()
    f.Get_dr(x).Get_ir(n).Get_Str(t)
    W('f text ' + G(x.v) + ' %d ' % n.v + T(t) + ' ' + St(f) + '\n')
    f.seekg(-5, Ios_end)
    Streams_getline_IStream_Str(f, t)
    W('f end-5 [' + T(t) + '] ' + St(f) + '\n')
    Streams_getline_IStream_Str(f, t)
    W('f after ' + St(f) + '\n')
    f.clear()
    f.seekg(0, Ios_end)
    W('f size %d\n' % f.tellg())
    f.seekg(4, Ios_beg)
    bb = [0]
    f.read_PtrT_l__I(NewPtrI_TA_l(bb, 0), 4)
    W('f at4 %d tellg %d\n' % (bb[0], f.tellg()))
    f.seekg(1000, Ios_beg)
    f.read_PtrT_l__I(NewPtrI_TA_l(bb, 0), 4)
    W('f past-end gc %d %s\n' % (f.gcount(), St(f)))
    f.close()

    f = NewIFStream_Str_i(S('build/no-such-file.bin'), Ios_in)
    W('f missing open ' + B(f.is_open()) + ' ' + St(f) + '\n')


def Ec(e):
    return '%d (%s)' % (e.v, T(e.message()))


def Bsec():
    is_ = NewIStringStream_Str(S('x\nlast'))
    line = NewStr()
    for k in range(3):
        Streams_getline_IStream_Str(is_, line)
        W('b line [' + T(line) + '] ' + St(is_) + '\n')

    is_ = NewIStringStream_Str(S('12abc 99999999999999999999 18446744073709551615 -1 1e400 inf 0x1p3 -.5 1e 5'))
    i, L, U, u, d = Cell(-1), Cell(0), Cell(0), Cell(0), Cell(0.0)
    is_.Get_ir(i)
    W('b 12abc %d %s\n' % (i.v, St(is_)))
    is_.clear()
    is_.Get_lr(L)
    W('b long-over %d %s\n' % (L.v, St(is_)))
    is_.clear()
    is_.Get_ulr(U)
    W('b ulong-max %d %s\n' % (U.v, St(is_)))
    is_.clear()
    is_.Get_ur(u)
    W('b uint-neg %d %s\n' % (u.v, St(is_)))
    is_.clear()
    for k in range(5):
        is_.Get_dr(d)
        W('b dbl ' + G(d.v) + ' ' + St(is_) + '\n')
        is_.clear()
    is_.Get_ir(i)
    W('b last %d %s\n' % (i.v, St(is_)))
    i.v = 77
    is_.Get_ir(i)
    W('b after-eof %d %s\n' % (i.v, St(is_)))
    fl = Cell(0.0)
    is_.clear()
    is_.str_Str(S(' 0.1 '))
    is_.Get_fr(fl)
    W('b float ' + G(fl.v) + ' ' + St(is_) + '\n')
    ch = Cell(0)
    is_.str_Str(S('  q'))
    is_.Get_cr(ch)
    W('b char %d %s\n' % (ch.v, St(is_)))

    f = NewOFStream_Str_i(S('build/streamcheck.bin'), Ios_app)
    W('b app tellp %d\n' % f.tellp())
    f.close()
    g = NewOFStream_CPtr_i(R_Lit('build/no-dir/x.txt'), Ios_out)
    W('b bad-open ' + B(g.is_open()) + ' ' + St(g) + '\n')
    g.Put_CPtr(R_Lit('dropped'))
    h = NewOFStream()
    W('b unopened ' + B(h.is_open()) + ' ' + St(h) + '\n')
    h.open_Str_i(S('build/streamcheck.txt'))
    h.Put_d(1.25).Manip_endl()
    W('b reopened ' + B(h.is_open()) + ' ' + St(h) + ' tellp %d\n' % h.tellp())
    h.seekp(0, Ios_beg)
    h.Put_CPtr(R_Lit('9'))
    h.seekp(0, Ios_end)
    h.Put_CPtr(R_Lit('z'))
    h.Dispose()
    r = NewIFStream_FsPath_i(NewFsPath_Str(S('build/streamcheck.txt')), Ios_in)
    t = NewStr()
    Streams_getline_IStream_Str(r, t)
    W('b reread [' + T(t) + '] ')
    Streams_getline_IStream_Str(r, t)
    W('[' + T(t) + '] ' + St(r) + '\n')
    r.close()
    dr = NewIFStream_Str_i(S('build'), Ios_in)
    W('b dir-open ' + B(dr.is_open()) + ' ' + St(dr) + '\n')

    o = NewOStringStream()
    o.str_Str(S('abc'))
    o.Put_CPtr(R_Lit('XY'))
    W('b str-set [' + T(o.str_()) + '] tellp %d\n' % o.tellp())
    o.precision_l(3)
    o2 = NewOStringStream()
    o2.MoveFrom(o)
    o2.Put_d(1.0 / 3.0)
    o.Put_CPtr(R_Lit('q'))
    W('b moved [' + T(o2.str_()) + '] [' + T(o.str_()) + ']\n')
    i1 = NewIStringStream_Str(S('one two'))
    w = NewStr()
    i1.Get_Str(w)
    i2 = NewIStringStream()
    i2.MoveFrom(i1)
    i2.Get_Str(w)
    W('b imoved [' + T(w) + '] ' + St(i2) + ' str [' + T(i2.str_()) + ']\n')
    p = NewOStringStream()
    p.Put_d(1.5).Manip_scientific().Put_d(1.5).Manip_fixed().Put_d(1.5).Manip_defaultfloat().Put_d(1.5).Manip_showpoint().Put_d(1.5).Manip_noshowpoint().Put_d(1.5)
    p.Put_SetW(SetW(6)).Put_SetFill(SetFill(c('.'))).Put_SetPrec(SetPrec(2)).Put_d(3.14159).Put_SetW(Streams_setw(4)).Put_SetFill(Streams_setfill(c('#'))).Put_i(1)
    p.setf_i_i(Ios_left, Ios_adjustfield)
    p.width_l(5)
    p.Put_CPtr(R_Lit('ab'))
    p.unsetf(Ios_left)
    p.width_l(5)
    p.Put_CPtr(R_Lit('cd'))
    p.Put_f(f32(f32(1.0) / f32(3.0))).Put_uc(66).Put_u((2147483647 * 2) & 0xFFFFFFFF)
    W('b manip [' + T(p.str_()) + '] w %d f %d p %d\n' % (p.width_(), p.fill_(), p.precision_()))

    fs = NewFStream(S('build/streamcheck.fs'), Ios_in | Ios_out)
    fs.Put_CPtr(R_Lit('fstream'))
    W('b fstream ' + B(fs.is_open()) + ' tellp %d\n' % fs.tellp())
    fs.close()

    paths = ['', '/', 'a', 'a/', '/a/b.c', 'a/b.tar.gz', '.hidden', 'dir/.hidden', 'a/..', '..', '.',
             'a/./b/../c', '/../x', '//a//b//', 'a/b/', 'x.', 'dir.d/file', '/a/b/../../..', '../a/../..', './', 'a//b', '/a']
    for ps in paths:
        p = NewFsPath_Str(S(ps))
        W('p [' + ps + '] fn [' + T(p.filename()) + '] par [' + T(p.parent_path()) + '] stem [' + T(p.stem()) + '] ext [' + T(p.extension()) +
          '] abs ' + B(p.is_absolute()) + B(p.is_relative()) + ' has ' + B(p.has_filename()) + B(p.has_parent_path()) + B(p.has_extension()) +
          ' norm [' + T(p.lexically_normal()) + '] empty ' + B(p.empty()) + '\n')
        W('  join [' + T(p.Join_FsPath(NewFsPath_Str(S('x')))) + '] [' + T(p.Join_Str(S('/abs'))) + '] [' + T(p.Join_FsPath(NewFsPath())) + '] [' + T(p.Join_CPtr(R_Lit('y/z'))) + ']')
        q = p.Clone()
        q.replace_extension_FsPath(NewFsPath_Str(S('.out')))
        q2 = p.Clone()
        q2.replace_extension_Str(S('txt'))
        q3 = p.Clone()
        q3.replace_extension_FsPath()
        q4 = p.Clone()
        q4.replace_filename(NewFsPath_Str(S('new.inp')))
        q5 = p.Clone()
        q5.remove_filename()
        cs = p.c_str()
        cb = bytes(cs.B[cs.O:cs.B.index(0, cs.O)])
        cmpv = p.compare(NewFsPath_Str(S('a')))
        W(' rext [' + T(q) + '] [' + T(q2) + '] [' + T(q3) + '] rfn [' + T(q4) + '] rmfn [' + T(q5) + '] str [' + T(p.string()) + '] gs [' + T(p.generic_string()) +
          '] cs [' + cb.decode('latin-1') + '] cmp %d\n' % ((cmpv > 0) - (cmpv < 0)))
    a = NewFsPath_Str(S('x'))
    b = NewFsPath_CPtr(R_Lit('y'))
    a.Assign_FsPath(b)
    b.Assign_Str(S('w'))
    cc = NewFsPath()
    cc.MoveFrom(a)
    W('p assign [' + T(a) + '] [' + T(b) + '] [' + T(cc) + '] ')
    cc.clear()
    W('[' + T(cc) + '] ' + B(cc.empty()) + '\n')

    dirp = NewFsPath_Str(S('build/fsdir'))
    ec = NewErrorCode()
    Fs_remove_FsPath(NewFsPath_Str(S('build/fsdir/x/y/f.txt')))
    Fs_remove_FsPath(NewFsPath_Str(S('build/fsdir/x/y/g.txt')))
    Fs_remove_FsPath(NewFsPath_Str(S('build/fsdir/x/y/h.txt')))
    Fs_remove_FsPath_ErrorCode(NewFsPath_Str(S('build/fsdir/x/y')), ec)
    W('fs mkdir ' + B(Fs_create_directories_FsPath(NewFsPath_Str(S('build/fsdir/x/y')))) + ' again ' +
      B(Fs_create_directories_FsPath_ErrorCode(NewFsPath_Str(S('build/fsdir/x/y')), ec)) + ' ' + Ec(ec) + '\n')
    file = NewFsPath_Str(S('build/fsdir/x/y/f.txt'))
    o = NewOFStream_FsPath_i(file, Ios_out)
    o.Put_CPtr(R_Lit('0123456789'))
    o.close()
    W('fs exists ' + B(Fs_exists_FsPath(file)) + B(Fs_exists_FsPath(dirp)) + B(Fs_exists_FsPath(NewFsPath_Str(S('build/fsdir/none')))) + B(Fs_exists_FsPath_ErrorCode(file, ec)) +
      ' reg ' + B(Fs_is_regular_file_FsPath(file)) + B(Fs_is_regular_file_FsPath(dirp)) + ' dir ' + B(Fs_is_directory_FsPath(file)) + B(Fs_is_directory_FsPath(dirp)) + '\n')
    W('fs size %d ec %d ' % (Fs_file_size_FsPath(file), Fs_file_size_FsPath_ErrorCode(file, ec)) + Ec(ec))
    W(' missing %d ' % Fs_file_size_FsPath_ErrorCode(NewFsPath_Str(S('build/fsdir/none')), ec) + Ec(ec))
    W(' dir %d ' % Fs_file_size_FsPath_ErrorCode(dirp, ec) + Ec(ec) + '\n')
    for sp in ['build/fsdir/x/y/f.txt', 'build/fsdir', 'build/fsdir/none', '/dev/null']:
        s1 = Fs_status_FsPath(NewFsPath_Str(S(sp)))
        s2 = Fs_symlink_status_FsPath_ErrorCode(NewFsPath_Str(S(sp)), ec)
        s3 = Fs_symlink_status_FsPath(NewFsPath_Str(S(sp)))
        W('fs status %s %d %d %d ' % (sp, s1.type(), s2.type(), s3.Clone().type()) + B(Fs_exists_FileStatus(s1)) + B(Fs_is_regular_file_FileStatus(s1)) +
          B(Fs_is_directory_FileStatus(s1)) + B(Fs_is_symlink(s1)) + '\n')
    g = NewFsPath_Str(S('build/fsdir/x/y/g.txt'))
    Fs_create_hard_link(file, g, ec)
    W('fs link ' + Ec(ec))
    Fs_create_hard_link(file, g, ec)
    W(' again ' + Ec(ec))
    Fs_create_hard_link(NewFsPath_Str(S('build/fsdir/none')), NewFsPath_Str(S('build/fsdir/x/y/n.txt')), ec)
    W(' missing ' + Ec(ec) + ' size %d\n' % Fs_file_size_FsPath(g))
    h = NewFsPath_Str(S('build/fsdir/x/y/h.txt'))
    Fs_rename_FsPath_FsPath_ErrorCode(g, h, ec)
    W('fs rename ' + Ec(ec) + ' ' + B(Fs_exists_FsPath(g)) + B(Fs_exists_FsPath(h)))
    Fs_rename_FsPath_FsPath_ErrorCode(g, h, ec)
    W(' missing ' + Ec(ec))
    Fs_rename_FsPath_FsPath(h, g)
    W(' plain ' + B(Fs_exists_FsPath(g)) + B(Fs_exists_FsPath(h)))
    Fs_rename_FsPath_FsPath_ErrorCode(g, file, ec)
    W(' over ' + Ec(ec) + ' ' + B(Fs_exists_FsPath(g)) + B(Fs_exists_FsPath(file)) + '\n')
    e1 = B(Fs_equivalent_FsPath_FsPath(file, NewFsPath_Str(S('./build/fsdir/x/../x/y//f.txt')))) + B(Fs_equivalent_FsPath_FsPath(file, dirp))
    e1 += B(Fs_equivalent_FsPath_FsPath_ErrorCode(file, NewFsPath_Str(S('build/fsdir/none')), ec)) + ' ' + Ec(ec)
    e1 += ' ' + B(Fs_equivalent_FsPath_FsPath_ErrorCode(file, file, ec)) + ' ' + Ec(ec)
    W('fs equiv ' + e1 + '\n')
    k1 = T(Fs_canonical_FsPath_ErrorCode(NewFsPath_Str(S('build/fsdir/none')), ec)) + '] ' + Ec(ec)
    k2 = T(Fs_weakly_canonical_FsPath(NewFsPath_Str(S('build/../build/fsdir/./x/'))))
    k3 = T(Fs_weakly_canonical_FsPath_ErrorCode(NewFsPath_Str(S('/a/b/../c/.')), ec)) + '] ' + Ec(ec)
    k4 = T(Fs_canonical_FsPath_ErrorCode(NewFsPath_Str(S('/x/../y')), ec)) + '] ' + Ec(ec)
    k5 = T(Fs_canonical_FsPath(dirp))
    W('fs canon [' + k1 + ' [' + k2 + '] [' + k3 + ' [' + k4 + ' [' + k5 + ']\n')
    W('fs abs [' + T(Fs_absolute(NewFsPath_Str(S('rel/p')))) + '] [' + T(Fs_absolute(NewFsPath_Str(S('/abs/p')))) + '] cwd [' + T(Fs_current_path()) +
      '] tmp [' + T(Fs_temp_directory_path()) + ']\n')
    W('fs remove ' + B(Fs_remove_FsPath(file)) + B(Fs_remove_FsPath(file)) + B(Fs_remove_FsPath(dirp)))
    W(' ec ' + B(Fs_remove_FsPath_ErrorCode(NewFsPath_Str(S('build/fsdir/x')), ec)) + ' ' + Ec(ec))
    W(' ' + B(Fs_remove_FsPath_ErrorCode(NewFsPath_Str(S('build/fsdir/x/y')), ec)) + ' ' + Ec(ec))
    W(' ' + B(Fs_remove_FsPath_ErrorCode(NewFsPath_Str(S('build/fsdir/x/y')), ec)) + ' ' + Ec(ec) + '\n')
    e2 = NewErrorCode()
    W('ec ' + B(e2.Bool()) + ' %d' % e2.value())
    for v in [13, 17, 20, 21, 95, 5]:
        e2.v = v
        W(' ' + Ec(e2))
    e2.clear()
    W(' ' + B(e2.Bool()) + '\n')


if __name__ == '__main__':
    main()
