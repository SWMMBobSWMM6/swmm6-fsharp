// streamcheck_go - the cases of streamcheck.cpp / streamcheck.cs through go/cpp/streams.go and fs.go. Section a
// must equal the C++ output, section b the C# output (the C# port's own behaviour), section c the C++ output.
// Written by Claude (Anthropic); see AI-USE.md.
package main

import (
	"math"
	"os"
	"strconv"
	"strings"

	"openswmm/cpp"
)

var sb strings.Builder

func W(s string)               { sb.WriteString(s) }
func S(s string) *cpp.Str      { return cpp.StrOf(s) }
func I(v int64) string         { return strconv.FormatInt(v, 10) }
func U(v uint64) string        { return strconv.FormatUint(v, 10) }
func Bits(u uint64) float64    { return math.Float64frombits(u) }
func B(b bool) string          { return map[bool]string{true: "1", false: "0"}[b] }
func Pth(p *cpp.FsPath) string { return p.ToString() }
func St(s *cpp.IosBase) string {
	r := ""
	r += map[bool]string{true: "g", false: "-"}[s.Good()]
	r += map[bool]string{true: "e", false: "-"}[s.Eof()]
	r += map[bool]string{true: "f", false: "-"}[s.Fail()]
	r += map[bool]string{true: "b", false: "-"}[s.Bad()]
	return r
}
func G(v float64) string { o := cpp.NewOStringStream(); o.Put_d(v); return o.Str_().String() }

func main() {
	sec := "a"
	if len(os.Args) > 1 {
		sec = os.Args[1]
	}
	switch sec {
	case "a":
		A()
	case "b":
		Bsec()
	default:
		C()
	}
	b := []byte(sb.String())
	cpp.Streams_cout.Write_CPtr_l(cpp.CPtr{B: b}, int64(len(b)))
}

func C() {
	f := cpp.NewIFStream_Str_i(S("build/streamcheck.bin"), cpp.Ios_in|cpp.Ios_binary)
	b := make([]int32, 1)
	cpp.IStream_read_PtrT_l(f, cpp.NewPtr_TA_l(b, 0), 4)
	f.Seekg(4, cpp.Ios_cur)
	cpp.IStream_read_PtrT_l(f, cpp.NewPtr_TA_l(b, 0), 4)
	W("cur at8 " + I(int64(b[0])) + " tellg " + I(f.Tellg()) + " " + St(&f.IosBase) + "\n")
}

func A() {
	vals := []float64{0.0, Bits(0x8000000000000000), 1.0, -1.0, 0.1, 0.5, 1.5, 2.5, 123.456, 1e-5,
		1.23456789e-7, 1e21, 1.7976931348623157e308, Bits(1), 2.2250738585072014e-308, 1.0 / 3.0, 2.0 / 3.0, 100.0,
		1e15, 123456789012345678.0, 0.000123456, 9.999999, 99.995, 0.125, Bits(0x7ff8000000000000),
		Bits(0x7ff0000000000000), Bits(0xfff0000000000000), 1e100, 5e-310, 3.14159265358979, -0.0004567,
		1234567.0, 0.95, 0.05, 1e-300, 6.02214076e23}
	third, twoThirds := vals[15], vals[16]
	precs := []int{0, 1, 2, 3, 4, 6, 8, 10, 15, 16, 17, 20, -1}
	for _, v := range vals {
		for mode := 0; mode < 4; mode++ {
			for sp := 0; sp < 2; sp++ {
				for _, p := range precs {
					if mode == 3 && p != 6 {
						continue
					}
					o := cpp.NewOStringStream()
					if mode == 1 {
						o.Manip_fixed()
					}
					if mode == 2 {
						o.Manip_scientific()
					}
					if mode == 3 {
						o.Setf_i_i(cpp.Ios_fixed|cpp.Ios_scientific, cpp.Ios_floatfield)
					}
					if sp == 1 {
						o.Manip_showpoint()
					}
					o.Precision_l(int64(p))
					o.Put_d(v)
					W("d " + I(int64(mode)) + I(int64(sp)) + " " + I(int64(p)) + " [" + o.Str_().String() + "]\n")
				}
			}
		}
		f := cpp.NewOStringStream()
		f.Put_f(float32(v))
		W("f [" + f.Str_().String() + "]\n")
	}
	{
		o := cpp.NewOStringStream()
		o.Put_c('[').Manip_setw(10).Put_d(3.5).Put_CPtr(cpp.Lit("][")).Manip_left().Manip_setw(8).Manip_setfill('*').Put_i(-42).Put_c(']')
		o.Put_c('[').Manip_right().Manip_setw(6).Put_CPtr(cpp.Lit("ab")).Put_CPtr(cpp.Lit("][")).Manip_setw(2).Put_CPtr(cpp.Lit("abcdef")).Put_c(']')
		o.Put_c('[').Manip_setw(5).Manip_setfill('0').Put_u(7).Put_CPtr(cpp.Lit("][")).Put_i(123).Put_c(']')
		o.Manip_boolalpha().Put_c('[').Put_b(true).Put_c(' ').Put_b(false).Put_c(']').Manip_noboolalpha().Put_c('[').Put_b(true).Put_b(false).Put_c(']')
		o.Put_c('[').Manip_setw(12).Manip_setfill(' ').Manip_quoted_Str(S("a\"b\\c")).Put_c(']')
		o.Put_c('[').Manip_quoted_CPtr(cpp.Lit("plain")).Put_c(']')
		o.Put_c('[').Manip_fixed().Manip_setprecision(3).Manip_setw(9).Put_d(twoThirds).Put_c(']')
		o.Put_c('[').Manip_defaultfloat().Manip_setprecision(4).Put_d(twoThirds).Put_c(']')
		o.Put_c('[').Put_l(math.MinInt64).Put_c(' ').Put_ul(math.MaxUint64).Put_c(' ').Put_s(-5).Put_c(']')
		o.Put_c('[').Put_c('x').Put_c(65).Put_c(']')
		W("w " + o.Str_().String() + "\n")
		_ = third
	}
	{
		o := cpp.NewOStringStream_Str(S("abcdef"))
		o.Put_CPtr(cpp.Lit("XY"))
		W("init [" + o.Str_().String() + "] tellp " + I(o.Tellp()) + "\n")
		q := cpp.NewOStringStream()
		q.Put_CPtr(cpp.Lit("hello world"))
		q.Seekp(6, cpp.Ios_beg)
		q.Put_CPtr(cpp.Lit("W"))
		q.Seekp(-1, cpp.Ios_end)
		q.Put_CPtr(cpp.Lit("D"))
		q.Seekp(2, cpp.Ios_beg)
		q.Seekp(1, cpp.Ios_cur)
		q.Put_CPtr(cpp.Lit("L"))
		W("seekp [" + q.Str_().String() + "] tellp " + I(q.Tellp()) + "\n")
	}
	{
		is := cpp.NewIStringStream_Str(S("  3.25 -7 abc\nline two\n\nlast"))
		d, i, s := -1.0, int32(-1), cpp.NewStr()
		is.Get_dr(&d)
		W("x d " + G(d) + " " + St(&is.IosBase) + "\n")
		is.Get_ir(&i)
		W("x i " + I(int64(i)) + " " + St(&is.IosBase) + "\n")
		is.Get_Str(s)
		W("x s " + s.String() + " " + St(&is.IosBase) + "\n")
		line := cpp.NewStr()
		for k := 0; k < 4; k++ {
			cpp.Streams_getline_IStream_Str(is, line)
			W("x line [" + line.String() + "] " + St(&is.IosBase) + "\n")
		}
	}
	{
		is := cpp.NewIStringStream_Str(S("12 4294967296 -2147483648 2147483648 -9223372036854775808 9000000000000000000 4294967295 7"))
		a, b, c, e := int32(-1), int32(-1), int32(-1), int32(-1)
		is.Get_ir(&a).Get_ir(&b)
		W("x ints " + I(int64(a)) + " " + I(int64(b)) + " " + St(&is.IosBase) + "\n")
		is.Clear(0)
		is.Get_ir(&c).Get_ir(&e)
		W("x ints " + I(int64(c)) + " " + I(int64(e)) + " " + St(&is.IosBase) + "\n")
		is.Clear(0)
		L := int64(-1)
		is.Get_lr(&L)
		W("x long " + I(L) + " " + St(&is.IosBase) + "\n")
		is.Clear(0)
		var Uv uint64
		var u uint32
		is.Get_ulr(&Uv)
		W("x ulong " + U(Uv) + " " + St(&is.IosBase) + "\n")
		is.Clear(0)
		is.Get_ur(&u)
		W("x uint " + U(uint64(u)) + " " + St(&is.IosBase) + "\n")
	}
	{
		is := cpp.NewIStringStream_Str(S("a,b,,c"))
		f := cpp.NewStr()
		for cpp.Streams_getline_IStream_Str_c(is, f, ',').Ok() {
			W("x field [" + f.String() + "] " + St(&is.IosBase) + "\n")
		}
		W("x field end " + St(&is.IosBase) + "\n")
	}
	{
		is := cpp.NewIStringStream_Str(S("   \t\n  tail"))
		is.Manip_ws()
		rest := cpp.NewStr()
		cpp.Streams_getline_IStream_Str(is, rest)
		W("x ws [" + rest.String() + "] " + St(&is.IosBase) + "\n")
		e := cpp.NewIStringStream_Str(S("   "))
		e.Manip_ws()
		W("x ws-end " + St(&e.IosBase) + "\n")
	}
	{
		is := cpp.NewIStringStream_Str(S("0123456789"))
		buf := make([]byte, 16)
		is.Read_CPtr_l(cpp.CPtr{B: buf}, 4)
		W("r [" + string(buf[:4]) + "] gc " + I(is.Gcount()) + " " + St(&is.IosBase) + " tellg " + I(is.Tellg()) + "\n")
		W("r peek " + I(int64(is.Peek())))
		W(" get " + I(int64(is.Get_())))
		W(" get " + I(int64(is.Get_())) + " " + St(&is.IosBase) + "\n")
		is.Ignore(3, '8')
		W("r ignore tellg " + I(is.Tellg()) + " " + St(&is.IosBase) + "\n")
		is.Seekg(-2, cpp.Ios_cur)
		W("r seekg cur tellg " + I(is.Tellg()) + " get " + I(int64(is.Get_())) + "\n")
		is.Seekg(-3, cpp.Ios_end)
		W("r seekg end tellg " + I(is.Tellg()) + " get " + I(int64(is.Get_())) + "\n")
		is.Read_CPtr_l(cpp.CPtr{B: buf}, 10)
		W("r short gc " + I(is.Gcount()) + " " + St(&is.IosBase) + "\n")
		is.Clear(0)
		is.Seekg(0, cpp.Ios_beg)
		W("r rewind tellg " + I(is.Tellg()) + " get " + I(int64(is.Get_())) + " " + St(&is.IosBase) + "\n")
		is.Ignore(100, -1)
		W("r ignore-all " + St(&is.IosBase) + "\n")
		W("r get-end " + I(int64(is.Get_())))
		W(" " + St(&is.IosBase) + "\n")
		W("r str [" + is.Str_().String() + "]\n")
	}
	{
		{
			f := cpp.NewOFStream_Str_i(S("build/streamcheck.bin"), cpp.Ios_trunc|cpp.Ios_binary)
			W("f open " + B(f.Is_open()) + " " + St(&f.IosBase) + "\n")
			a := []int32{1, -2, 300000}
			d := []float64{1.5, -0.25}
			cpp.OStream_write_PtrT_l(f, cpp.NewPtr_TA_l(a, 0), 12)
			cpp.OStream_write_PtrT_l(f, cpp.NewPtr_TA_l(d, 0), 16)
			f.Write_CPtr_l(cpp.Lit("MAGIC"), 5)
			W("f tellp " + I(f.Tellp()) + "\n")
			f.Put_d(3.25).Put_c(' ').Put_i(42).Put_c('\n')
			W("f tellp " + I(f.Tellp()) + "\n")
			f.Close()
			W("f closed " + B(f.Is_open()) + "\n")
		}
		{
			f := cpp.NewOFStream_Str_i(S("build/streamcheck.bin"), cpp.Ios_app)
			f.Put_CPtr(cpp.Lit("tail\n"))
			f.Dispose()
		}
		{
			f := cpp.NewIFStream_Str_i(S("build/streamcheck.bin"), cpp.Ios_in|cpp.Ios_binary)
			W("f in open " + B(f.Is_open()) + " " + St(&f.IosBase) + "\n")
			a := make([]int32, 3)
			cpp.IStream_read_PtrT_l(f, cpp.NewPtr_TA_l(a, 0), 12)
			W("f ints " + I(int64(a[0])) + " " + I(int64(a[1])) + " " + I(int64(a[2])) + " gc " + I(f.Gcount()) + " tellg " + I(f.Tellg()) + "\n")
			d := make([]float64, 2)
			cpp.IStream_read_PtrT_l(f, cpp.NewPtr_TA_l(d, 0), 16)
			W("f dbl " + G(d[0]) + " " + G(d[1]) + " tellg " + I(f.Tellg()) + "\n")
			m := make([]byte, 6)
			f.Read_CPtr_l(cpp.CPtr{B: m}, 5)
			W("f magic " + cpp.CStr(cpp.CPtr{B: m}) + "\n")
			x, n, t := 0.0, int32(0), cpp.NewStr()
			f.Get_dr(&x).Get_ir(&n).Get_Str(t)
			W("f text " + G(x) + " " + I(int64(n)) + " " + t.String() + " " + St(&f.IosBase) + "\n")
			f.Seekg(-5, cpp.Ios_end)
			cpp.Streams_getline_IStream_Str(f, t)
			W("f end-5 [" + t.String() + "] " + St(&f.IosBase) + "\n")
			cpp.Streams_getline_IStream_Str(f, t)
			W("f after " + St(&f.IosBase) + "\n")
			f.Clear(0)
			f.Seekg(0, cpp.Ios_end)
			W("f size " + I(f.Tellg()) + "\n")
			f.Seekg(4, cpp.Ios_beg)
			b := make([]int32, 1)
			cpp.IStream_read_PtrT_l(f, cpp.NewPtr_TA_l(b, 0), 4)
			W("f at4 " + I(int64(b[0])) + " tellg " + I(f.Tellg()) + "\n")
			f.Seekg(1000, cpp.Ios_beg)
			cpp.IStream_read_PtrT_l(f, cpp.NewPtr_TA_l(b, 0), 4)
			W("f past-end gc " + I(f.Gcount()) + " " + St(&f.IosBase) + "\n")
			f.Close()
		}
		{
			f := cpp.NewIFStream_Str_i(S("build/no-such-file.bin"), cpp.Ios_in)
			W("f missing open " + B(f.Is_open()) + " " + St(&f.IosBase) + "\n")
		}
	}
}

func Ec(e *cpp.ErrorCode) string { return I(int64(e.V)) + " (" + e.Message().String() + ")" }

func sign(x int32) string {
	switch {
	case x < 0:
		return "-1"
	case x > 0:
		return "1"
	}
	return "0"
}

func Bsec() {
	{
		is := cpp.NewIStringStream_Str(S("x\nlast"))
		line := cpp.NewStr()
		for k := 0; k < 3; k++ {
			cpp.Streams_getline_IStream_Str(is, line)
			W("b line [" + line.String() + "] " + St(&is.IosBase) + "\n")
		}
	}
	{
		is := cpp.NewIStringStream_Str(S("12abc 99999999999999999999 18446744073709551615 -1 1e400 inf 0x1p3 -.5 1e 5"))
		i, L, Uv, u, d := int32(-1), int64(0), uint64(0), uint32(0), 0.0
		is.Get_ir(&i)
		W("b 12abc " + I(int64(i)) + " " + St(&is.IosBase) + "\n")
		is.Clear(0)
		is.Get_lr(&L)
		W("b long-over " + I(L) + " " + St(&is.IosBase) + "\n")
		is.Clear(0)
		is.Get_ulr(&Uv)
		W("b ulong-max " + U(Uv) + " " + St(&is.IosBase) + "\n")
		is.Clear(0)
		is.Get_ur(&u)
		W("b uint-neg " + U(uint64(u)) + " " + St(&is.IosBase) + "\n")
		is.Clear(0)
		for k := 0; k < 5; k++ {
			is.Get_dr(&d)
			W("b dbl " + G(d) + " " + St(&is.IosBase) + "\n")
			is.Clear(0)
		}
		is.Get_ir(&i)
		W("b last " + I(int64(i)) + " " + St(&is.IosBase) + "\n")
		i = 77
		is.Get_ir(&i)
		W("b after-eof " + I(int64(i)) + " " + St(&is.IosBase) + "\n")
		var fl float32
		is.Clear(0)
		is.Str_Str(S(" 0.1 "))
		is.Get_fr(&fl)
		W("b float " + G(float64(fl)) + " " + St(&is.IosBase) + "\n")
		var c int8
		is.Str_Str(S("  q"))
		is.Get_cr(&c)
		W("b char " + I(int64(c)) + " " + St(&is.IosBase) + "\n")
	}
	{
		f := cpp.NewOFStream_Str_i(S("build/streamcheck.bin"), cpp.Ios_app)
		W("b app tellp " + I(f.Tellp()) + "\n")
		f.Close()
		g := cpp.NewOFStream_CPtr_i(cpp.Lit("build/no-dir/x.txt"), cpp.Ios_out)
		W("b bad-open " + B(g.Is_open()) + " " + St(&g.IosBase) + "\n")
		g.Put_CPtr(cpp.Lit("dropped"))
		h := cpp.NewOFStream()
		W("b unopened " + B(h.Is_open()) + " " + St(&h.IosBase) + "\n")
		h.Open_Str_i(S("build/streamcheck.txt"), cpp.Ios_out)
		h.Put_d(1.25).Manip_endl()
		W("b reopened " + B(h.Is_open()) + " " + St(&h.IosBase) + " tellp " + I(h.Tellp()) + "\n")
		h.Seekp(0, cpp.Ios_beg)
		h.Put_CPtr(cpp.Lit("9"))
		h.Seekp(0, cpp.Ios_end)
		h.Put_CPtr(cpp.Lit("z"))
		h.Dispose()
		r := cpp.NewIFStream_FsPath_i(cpp.NewFsPath_Str(S("build/streamcheck.txt")), cpp.Ios_in)
		t := cpp.NewStr()
		cpp.Streams_getline_IStream_Str(r, t)
		W("b reread [" + t.String() + "] ")
		cpp.Streams_getline_IStream_Str(r, t)
		W("[" + t.String() + "] " + St(&r.IosBase) + "\n")
		r.Close()
		dir := cpp.NewIFStream_Str_i(S("build"), cpp.Ios_in)
		W("b dir-open " + B(dir.Is_open()) + " " + St(&dir.IosBase) + "\n")
	}
	{
		o := cpp.NewOStringStream()
		o.Str_Str(S("abc"))
		o.Put_CPtr(cpp.Lit("XY"))
		W("b str-set [" + o.Str_().String() + "] tellp " + I(o.Tellp()) + "\n")
		o.Precision_l(3)
		o2 := cpp.NewOStringStream()
		o2.MoveFrom(o)
		o2.Put_d(1.0 / 3.0)
		o.Put_CPtr(cpp.Lit("q"))
		W("b moved [" + o2.Str_().String() + "] [" + o.Str_().String() + "]\n")
		i1 := cpp.NewIStringStream_Str(S("one two"))
		w := cpp.NewStr()
		i1.Get_Str(w)
		i2 := cpp.NewIStringStream()
		i2.MoveFrom(i1)
		i2.Get_Str(w)
		W("b imoved [" + w.String() + "] " + St(&i2.IosBase) + " str [" + i2.Str_().String() + "]\n")
		p := cpp.NewOStringStream()
		p.Put_d(1.5).Manip_scientific().Put_d(1.5).Manip_fixed().Put_d(1.5).Manip_defaultfloat().Put_d(1.5).Manip_showpoint().Put_d(1.5).Manip_noshowpoint().Put_d(1.5)
		p.Put_SetW(cpp.NewSetW(6)).Put_SetFill(cpp.NewSetFill('.')).Put_SetPrec(cpp.NewSetPrec(2)).Put_d(3.14159).Put_SetW(cpp.Streams_setw(4)).Put_SetFill(cpp.Streams_setfill('#')).Put_i(1)
		p.Setf_i_i(cpp.Ios_left, cpp.Ios_adjustfield)
		p.Width_l(5)
		p.Put_CPtr(cpp.Lit("ab"))
		p.Unsetf(cpp.Ios_left)
		p.Width_l(5)
		p.Put_CPtr(cpp.Lit("cd"))
		p.Put_f(float32(1.0) / float32(3.0)).Put_uc(66).Put_u(2147483647 * 2)
		W("b manip [" + p.Str_().String() + "] w " + I(p.Width_()) + " f " + I(int64(p.Fill_())) + " p " + I(p.Precision_()) + "\n")
	}
	{
		fs := cpp.NewFStream(S("build/streamcheck.fs"), cpp.Ios_in|cpp.Ios_out)
		fs.Put_CPtr(cpp.Lit("fstream"))
		W("b fstream " + B(fs.Is_open()) + " tellp " + I(fs.Tellp()) + "\n")
		fs.Close()
	}
	paths := []string{"", "/", "a", "a/", "/a/b.c", "a/b.tar.gz", ".hidden", "dir/.hidden", "a/..", "..", ".",
		"a/./b/../c", "/../x", "//a//b//", "a/b/", "x.", "dir.d/file", "/a/b/../../..", "../a/../..", "./", "a//b", "/a"}
	for _, ps := range paths {
		p := cpp.NewFsPath_Str(S(ps))
		W("p [" + ps + "] fn [" + Pth(p.Filename()) + "] par [" + Pth(p.Parent_path()) + "] stem [" + Pth(p.Stem()) + "] ext [" + Pth(p.Extension()) +
			"] abs " + B(p.Is_absolute()) + B(p.Is_relative()) + " has " + B(p.Has_filename()) + B(p.Has_parent_path()) + B(p.Has_extension()) +
			" norm [" + Pth(p.Lexically_normal()) + "] empty " + B(p.Empty()) + "\n")
		W("  join [" + Pth(p.Join_FsPath(cpp.NewFsPath_Str(S("x")))) + "] [" + Pth(p.Join_Str(S("/abs"))) + "] [" + Pth(p.Join_FsPath(cpp.NewFsPath())) + "] [" + Pth(p.Join_CPtr(cpp.Lit("y/z"))) + "]")
		q := p.Clone()
		q.Replace_extension_FsPath(cpp.NewFsPath_Str(S(".out")))
		q2 := p.Clone()
		q2.Replace_extension_Str(S("txt"))
		q3 := p.Clone()
		q3.Replace_extension_FsPath(nil)
		q4 := p.Clone()
		q4.Replace_filename(cpp.NewFsPath_Str(S("new.inp")))
		q5 := p.Clone()
		q5.Remove_filename()
		W(" rext [" + Pth(q) + "] [" + Pth(q2) + "] [" + Pth(q3) + "] rfn [" + Pth(q4) + "] rmfn [" + Pth(q5) + "] str [" + p.String().String() + "] gs [" + p.Generic_string().String() +
			"] cs [" + cpp.CStr(p.C_str()) + "] cmp " + sign(p.Compare(cpp.NewFsPath_Str(S("a")))) + "\n")
	}
	{
		a := cpp.NewFsPath_Str(S("x"))
		b := cpp.NewFsPath_CPtr(cpp.Lit("y"))
		a.Assign_FsPath(b)
		b.Assign_Str(S("w"))
		c := cpp.NewFsPath()
		c.MoveFrom(a)
		W("p assign [" + Pth(a) + "] [" + Pth(b) + "] [" + Pth(c) + "] ")
		c.Clear()
		W("[" + Pth(c) + "] " + B(c.Empty()) + "\n")
	}
	{
		P := func(s string) *cpp.FsPath { return cpp.NewFsPath_Str(S(s)) }
		dir := P("build/fsdir")
		ec := cpp.NewErrorCode()
		cpp.Fs_remove_FsPath(P("build/fsdir/x/y/f.txt"))
		cpp.Fs_remove_FsPath(P("build/fsdir/x/y/g.txt"))
		cpp.Fs_remove_FsPath(P("build/fsdir/x/y/h.txt"))
		cpp.Fs_remove_FsPath_ErrorCode(P("build/fsdir/x/y"), ec)
		W("fs mkdir " + B(cpp.Fs_create_directories_FsPath(P("build/fsdir/x/y"))) + " again " + B(cpp.Fs_create_directories_FsPath_ErrorCode(P("build/fsdir/x/y"), ec)) + " " + Ec(ec) + "\n")
		file := P("build/fsdir/x/y/f.txt")
		o := cpp.NewOFStream_FsPath_i(file, cpp.Ios_out)
		o.Put_CPtr(cpp.Lit("0123456789"))
		o.Close()
		W("fs exists " + B(cpp.Fs_exists_FsPath(file)) + B(cpp.Fs_exists_FsPath(dir)) + B(cpp.Fs_exists_FsPath(P("build/fsdir/none"))) + B(cpp.Fs_exists_FsPath_ErrorCode(file, ec)) +
			" reg " + B(cpp.Fs_is_regular_file_FsPath(file)) + B(cpp.Fs_is_regular_file_FsPath(dir)) + " dir " + B(cpp.Fs_is_directory_FsPath(file)) + B(cpp.Fs_is_directory_FsPath(dir)) + "\n")
		W("fs size " + U(cpp.Fs_file_size_FsPath(file)) + " ec " + U(cpp.Fs_file_size_FsPath_ErrorCode(file, ec)) + " " + Ec(ec))
		W(" missing " + U(cpp.Fs_file_size_FsPath_ErrorCode(P("build/fsdir/none"), ec)) + " " + Ec(ec))
		W(" dir " + U(cpp.Fs_file_size_FsPath_ErrorCode(dir, ec)) + " " + Ec(ec) + "\n")
		for _, sp := range []string{"build/fsdir/x/y/f.txt", "build/fsdir", "build/fsdir/none", "/dev/null"} {
			s1 := cpp.Fs_status_FsPath(P(sp))
			s2 := cpp.Fs_symlink_status_FsPath_ErrorCode(P(sp), ec)
			s3 := cpp.Fs_symlink_status_FsPath(P(sp))
			W("fs status " + sp + " " + I(int64(s1.Type())) + " " + I(int64(s2.Type())) + " " + I(int64(s3.Clone().Type())) + " " + B(cpp.Fs_exists_FileStatus(s1)) + B(cpp.Fs_is_regular_file_FileStatus(s1)) + B(cpp.Fs_is_directory_FileStatus(s1)) + B(cpp.Fs_is_symlink(s1)) + "\n")
		}
		g := P("build/fsdir/x/y/g.txt")
		cpp.Fs_create_hard_link(file, g, ec)
		W("fs link " + Ec(ec))
		cpp.Fs_create_hard_link(file, g, ec)
		W(" again " + Ec(ec))
		cpp.Fs_create_hard_link(P("build/fsdir/none"), P("build/fsdir/x/y/n.txt"), ec)
		W(" missing " + Ec(ec) + " size " + U(cpp.Fs_file_size_FsPath(g)) + "\n")
		h := P("build/fsdir/x/y/h.txt")
		cpp.Fs_rename_FsPath_FsPath_ErrorCode(g, h, ec)
		W("fs rename " + Ec(ec) + " " + B(cpp.Fs_exists_FsPath(g)) + B(cpp.Fs_exists_FsPath(h)))
		cpp.Fs_rename_FsPath_FsPath_ErrorCode(g, h, ec)
		W(" missing " + Ec(ec))
		cpp.Fs_rename_FsPath_FsPath(h, g)
		W(" plain " + B(cpp.Fs_exists_FsPath(g)) + B(cpp.Fs_exists_FsPath(h)))
		cpp.Fs_rename_FsPath_FsPath_ErrorCode(g, file, ec)
		W(" over " + Ec(ec) + " " + B(cpp.Fs_exists_FsPath(g)) + B(cpp.Fs_exists_FsPath(file)) + "\n")
		W("fs equiv " + B(cpp.Fs_equivalent_FsPath_FsPath(file, P("./build/fsdir/x/../x/y//f.txt"))) + B(cpp.Fs_equivalent_FsPath_FsPath(file, dir)) + B(cpp.Fs_equivalent_FsPath_FsPath_ErrorCode(file, P("build/fsdir/none"), ec)) + " " + Ec(ec) +
			" " + B(cpp.Fs_equivalent_FsPath_FsPath_ErrorCode(file, file, ec)) + " " + Ec(ec) + "\n")
		W("fs canon [" + Pth(cpp.Fs_canonical_FsPath_ErrorCode(P("build/fsdir/none"), ec)) + "] " + Ec(ec) + " [" + Pth(cpp.Fs_weakly_canonical_FsPath(P("build/../build/fsdir/./x/"))) + "] [" +
			Pth(cpp.Fs_weakly_canonical_FsPath_ErrorCode(P("/a/b/../c/."), ec)) + "] " + Ec(ec) + " [" + Pth(cpp.Fs_canonical_FsPath_ErrorCode(P("/x/../y"), ec)) + "] " + Ec(ec) + " [" + Pth(cpp.Fs_canonical_FsPath(dir)) + "]\n")
		W("fs abs [" + Pth(cpp.Fs_absolute(P("rel/p"))) + "] [" + Pth(cpp.Fs_absolute(P("/abs/p"))) + "] cwd [" + Pth(cpp.Fs_current_path()) + "] tmp [" + Pth(cpp.Fs_temp_directory_path()) + "]\n")
		W("fs remove " + B(cpp.Fs_remove_FsPath(file)) + B(cpp.Fs_remove_FsPath(file)) + B(cpp.Fs_remove_FsPath(dir)))
		W(" ec " + B(cpp.Fs_remove_FsPath_ErrorCode(P("build/fsdir/x"), ec)) + " " + Ec(ec))
		W(" " + B(cpp.Fs_remove_FsPath_ErrorCode(P("build/fsdir/x/y"), ec)) + " " + Ec(ec))
		W(" " + B(cpp.Fs_remove_FsPath_ErrorCode(P("build/fsdir/x/y"), ec)) + " " + Ec(ec) + "\n")
		e2 := cpp.NewErrorCode()
		W("ec " + B(e2.Bool()) + " " + I(int64(e2.Value())))
		for _, v := range []int32{13, 17, 20, 21, 95, 5} {
			e2.V = v
			W(" " + Ec(e2))
		}
		e2.Clear()
		W(" " + B(e2.Bool()) + "\n")
	}
}
