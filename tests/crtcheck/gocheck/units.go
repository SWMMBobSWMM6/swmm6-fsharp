// units.go - checks of the parts of go/cpp's runtime that have no C library counterpart to compare with:
// sscanf stash slots and R_Back, raw memory, binary file I/O, tuples, atomics, variants, char arrays, call_once.
// Written by Claude (Anthropic) for the SWMM6 ports; see AI-USE.md.
package main

import (
	"fmt"
	"math"
	"os"
	"path/filepath"

	"openswmm/cpp"
)

type unitEnum int32

func unitsMain() {
	n, bad := 0, 0
	check := func(name string, ok bool, detail ...any) {
		n++
		if !ok {
			bad++
			fmt.Println("FAIL", name, fmt.Sprint(detail...))
		}
	}

	// sscanf into pointer parameters: the stash and R_Back, as the engine's parsers use them
	{
		var v int64 = -9
		var flag int8 = 'q'
		r := cpp.R_Back_i_T1r_T2r[int64, int8](cpp.R_sscanf(cpp.R_Lit("123 Y"), cpp.R_Lit("%ld %c"), cpp.Obj(cpp.R_Slot), cpp.Obj(cpp.R_Slot)), &v, &flag)
		check("slot %ld %c", r == 2 && v == 123 && flag == 'Y', r, v, flag)
		v, flag = -9, 'q'
		r = cpp.R_Back_i_T1r_T2r[int64, int8](cpp.R_sscanf(cpp.R_Lit("77"), cpp.R_Lit("%ld %c"), cpp.Obj(cpp.R_Slot), cpp.Obj(cpp.R_Slot)), &v, &flag)
		check("slot partial", r == 1 && v == 77 && flag == 'q', r, v, flag)
		var x float32
		r = cpp.R_Back_i_T1r[int8](cpp.R_sscanf(cpp.R_Lit("1.5 X"), cpp.R_Lit("%f %c"), cpp.Obj(cpp.R_RefF(func(s float32) { x = s })), cpp.Obj(cpp.R_Slot)), &flag)
		check("RefF + slot", r == 2 && x == 1.5 && flag == 'X', r, x, flag)
		var h int16
		r = cpp.R_Back_i_T1r[int16](cpp.R_sscanf(cpp.R_Lit("-12"), cpp.R_Lit("%d"), cpp.Obj(cpp.R_Slot)), &h)
		check("slot converted", r == 1 && h == -12, r, h)
		var d float64
		r = cpp.R_Back_i_T1r[float64](cpp.R_sscanf(cpp.R_Lit("2.5"), cpp.R_Lit("%lf"), cpp.Obj(cpp.R_Slot)), &d)
		check("slot double", r == 1 && d == 2.5, r, d)
		s1 := cpp.R_Buf_i(80)
		r = cpp.R_sscanf(cpp.R_Lit("  SWMM5 x"), cpp.R_Lit("%s"), any(s1))
		check("%s CPtr", r == 1 && cpp.R_strcmp(s1, cpp.R_Lit("SWMM5")) == 0, r)
	}

	// raw memory
	{
		b := make([]uint64, 1)
		src := []float64{-2.75}
		cpp.Mem_memcpy_PtrT_PtrU_ul[uint64, float64](cpp.NewPtr_TA_l(b, 0), cpp.NewPtr_TA_l(src, 0), 8)
		check("memcpy double->u64", b[0] == math.Float64bits(-2.75))
		view := cpp.Bytes_View_PtrT[uint64](cpp.NewPtr_TA_l([]uint64{0x0102030405060708}, 0))
		check("bytes view", len(view.B) == 8 && view.B[0] == 8 && view.B[7] == 1)
		f := make([]float64, 1)
		cpp.Mem_memcpy_PtrT_CPtr_ul[float64](cpp.NewPtr_TA_l(f, 0), view, 8)
		check("memcpy CPtr->double", math.Float64bits(f[0]) == 0x0102030405060708)
		c := cpp.Mem_Calloc[float64](5, 8)
		check("calloc", len(c.A) == 5 && c.O == 0)
		type obj struct{ k int }
		arr := []*obj{{1}, {2}, {3}}
		check("diff", cpp.Mem_Diff_T_PtrT(arr[2], cpp.NewPtr_TA_l(arr, 1)) == 1)
		u := make([]uint32, 2)
		cpp.Mem_memcpy_PtrT_PtrU_ul[uint32, uint8](cpp.NewPtr_TA_l(u, 1), cpp.NewPtr_TA_l([]uint8{1, 2, 3, 4, 5}, 1), 4)
		check("memcpy bytes->u32 offset", u[0] == 0 && u[1] == 0x05040302, u)
		check("bit cast", cpp.Bits_Cast[uint64](1.0) == 0x3FF0000000000000 && cpp.Bits_Cast[float32](uint32(0x3F800000)) == 1)
	}

	// binary files
	{
		dir, _ := os.MkdirTemp("", "crtcheck")
		defer os.RemoveAll(dir)
		name := cpp.R_Lit(filepath.Join(dir, "t.bin"))
		fp := cpp.R_fopen(name, cpp.R_Lit("w+b"))
		cpp.Mem_fwrite_CPtr_ul_ul_CFile(cpp.R_Lit("STAMP"), 1, 5, fp)
		cpp.R_fwriteI1(42, fp)
		pos := cpp.R_ftell(fp)
		cpp.R_fwriteD1(3.5, fp)
		cpp.R_fwriteF1(0.25, fp)
		cpp.Mem_fwrite_PtrT_ul_ul_CFile[int32](cpp.NewPtr_TA_l([]int32{7, -8, 9}, 0), 4, 3, fp)
		cpp.R_fseek_CFile_l_i(fp, pos, 0)
		cpp.R_fwriteD1(4.5, fp) // overwrite in place
		cpp.R_rewind(fp)
		stamp := cpp.R_BufOf(16, []int32{})
		got := cpp.Mem_fread_CPtr_ul_ul_CFile(stamp, 1, 5, fp)
		var i int32
		var d float64
		var fl float32
		ok := got == 5 && cpp.R_strncmp(stamp, cpp.R_Lit("STAMP"), 5) == 0 && cpp.R_freadI1(&i, fp) == 1 && i == 42 &&
			cpp.R_freadD1(&d, fp) == 1 && d == 4.5 && cpp.R_freadF1(&fl, fp) == 1 && fl == 0.25
		check("binary round trip", ok, got, i, d, fl)
		ints := make([]int32, 4)
		k := cpp.Mem_fread_PtrT_ul_ul_CFile[int32](cpp.NewPtr_TA_l(ints, 0), 4, 4, fp)
		check("short fread", k == 3 && ints[0] == 7 && ints[1] == -8 && ints[2] == 9 && cpp.R_feof(fp) == 1, k, ints)
		check("fgetc at end", cpp.R_fgetc(fp) == -1)
		check("fclose", cpp.R_fclose(fp) == 0)
		fp = cpp.R_fopen(name, cpp.R_Lit("r"))
		line := cpp.R_Buf_i(8)
		check("fgets", !cpp.R_fgets(line, 4, fp).IsNull() && cpp.R_strcmp(line, cpp.R_Lit("STA")) == 0)
		cpp.R_fclose(fp)
		check("fopen missing", cpp.R_fopen(cpp.R_Lit(filepath.Join(dir, "none")), cpp.R_Lit("r")) == nil)
		fp = cpp.R_fopen(cpp.R_Lit(filepath.Join(dir, "t.txt")), cpp.R_Lit("w"))
		cpp.R_fprintf(fp, cpp.R_Lit("%-6s|%3d|%5.2f|%c|%zu|%s|%d\n"), any(cpp.R_Lit("ab")), any(int8(-5)), any(float32(1.25)),
			any(uint8('Z')), any(uint64(18446744073709551615)), any(cpp.StrOf("str")), any(unitEnum(7)))
		cpp.R_FlushAll()
		cpp.R_fclose(fp)
		txt, _ := os.ReadFile(filepath.Join(dir, "t.txt"))
		check("fprintf Go types", string(txt) == "ab    | -5| 1.25|Z|18446744073709551615|str|7\n", string(txt))
	}

	// tuples, atomics, variants, once
	{
		t1 := []float64{1, 2}
		t2 := []float64{1, 2}
		a := cpp.NewTupN_oA(any(int32(1)), any(2.5), any(cpp.NewPtr_TA_l(t1, 0)), any(true), any(unitEnum(3)))
		b := cpp.NewTupN_oA(any(int32(1)), any(2.5), any(cpp.NewPtr_TA_l(t1, 0)), any(true), any(unitEnum(3)))
		c := cpp.NewTupN_oA(any(int32(1)), any(2.5), any(cpp.NewPtr_TA_l(t2, 0)), any(true), any(unitEnum(3)))
		d := cpp.NewTupN_oA(any(int32(1)), any(2.5), any(cpp.NewPtr_TA_l(t1, 0)), any(true), any(unitEnum(4)))
		less := cpp.Less_Fn[*cpp.TupN]()
		check("tuple equal", a.CompareTo(b) == 0 && !less(a, b) && !less(b, a))
		check("tuple identity", a.CompareTo(c) != 0 && a.CompareTo(c) == -c.CompareTo(a))
		check("tuple enum", less(a, d) && !less(d, a))
		m := cpp.NewAtomic[uint64]()
		check("fetch_add", m.Fetch_add(5, 0) == 0 && m.Fetch_add(2, 0) == 5 && m.V == 7)
		f := cpp.NewAtomic_T(1.5)
		old := 1.0
		ok1 := f.Compare_exchange(&old, 3.0)
		ok2 := f.Compare_exchange(&old, 3.0)
		check("compare_exchange", !ok1 && old == 1.5 && ok2 && f.V == 3.0)
		v := cpp.Make[*cpp.Variant]()
		check("variant default", v.V == false && v.Idx == 0)
		s := cpp.StrOf("abc")
		v.Set_Str(s)
		s.Append_i('d')
		w := v.Clone()
		check("variant string", v.Idx == 3 && v.V.(*cpp.Str).String() == "abc" && w.V.(*cpp.Str) != v.V.(*cpp.Str))
		once := cpp.NewOnceFlag()
		calls := 0
		cpp.Std_call_once(once, func() { calls++ })
		cpp.Std_call_once(once, func() { calls++ })
		check("call_once", calls == 1)
		x, y := 1, 2
		cpp.Std_swap(&x, &y)
		check("swap", x == 2 && y == 1)
		sp := cpp.Std_Span_TA([]int32{4, 5, 6})
		check("span", !sp.Empty() && *sp.Subspan(1, math.MaxUint64).At(1) == 6 && sp.Last(2).Size() == 2)
		t0 := cpp.TimePoint_now()
		check("duration", cpp.TimePoint_Sub(cpp.TimePoint_now(), t0).Clone().Count() >= 0)
		check("qnan", math.Float64bits(cpp.Std_QNaN) == 0x7FF8000000000000)
		check("nop", cpp.Std_Nop(any(3)) == 0)
	}

	// char arrays and strings
	{
		p := cpp.R_LitN("OPENSWMM_HS_V1", 16)
		check("LitN", len(p.B) == 16 && p.B[14] == 0 && p.B[15] == 0)
		q := cpp.R_CopyBuf(p)
		q.B[0] = 'X'
		check("CopyBuf", p.B[0] == 'O' && q.O == p.O)
		cpp.R_memcpyBuf(q, p)
		check("memcpyBuf", q.B[0] == 'O')
		dst := cpp.R_BufOf(6, []int32{'a', 'b'})
		cpp.R_strncpy(dst, cpp.R_Lit("xy"), 5)
		check("strncpy pads", dst.B[0] == 'x' && dst.B[1] == 'y' && dst.B[2] == 0 && dst.B[4] == 0)
		check("strncmp", cpp.R_strncmp(cpp.R_Lit("FLOWX"), cpp.R_Lit("FLOWY"), 4) == 0 && cpp.R_strncmp(cpp.R_Lit("a"), cpp.R_Lit("b"), 4) < 0)
		check("memcmp", cpp.R_memcmp(cpp.R_Lit("SWMM5-HOTSTART4"), cpp.R_Lit("SWMM5-HOTSTART3"), 15) > 0)
		check("bufs", len(cpp.R_Bufs(5, 24)) == 5 && len(cpp.R_Bufs(5, 24)[4].B) == 24)
		os.Setenv("CRTCHECK_X", "on")
		check("getenv", cpp.R_strcmp(cpp.R_getenv(cpp.R_Lit("CRTCHECK_X")), cpp.R_Lit("on")) == 0 &&
			cpp.R_getenv(cpp.R_Lit("CRTCHECK_NONE")).IsNull())
		check("strtol0", cpp.R_strtol0(cpp.R_Lit(" 24h"), 10) == 24 && cpp.R_atol(cpp.R_Lit("-5")) == -5)
		var tm int64
		t := cpp.R_time_Ptrl(cpp.PtrTo(&tm))
		check("time", t > 1700000000 && tm == t && cpp.R_difftime(t+3, t) == 3)
		ec := cpp.NewErrorCode()
		ec.V = 2
		check("error_code", ec.Bool() && ec.Message().String() == "No such file or directory")
	}
	fmt.Printf("units cases: %d, failures: %d\n", n, bad)
}
