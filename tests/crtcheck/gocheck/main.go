// main.go - the Go side of tests/crtcheck: go run . libm N | printf | scanf, reading what the C program of the
// same name wrote on stdin, and reporting where go/cpp differs from glibc.
// Written by Claude (Anthropic) for the SWMM6 ports; see AI-USE.md.
package main

import (
	"bufio"
	"encoding/hex"
	"fmt"
	"math"
	"os"
	"strconv"
	"strings"

	"openswmm/cpp"
)

func main() {
	switch os.Args[1] {
	case "libm":
		n, _ := strconv.Atoi(os.Args[2])
		libmMain(n)
	case "printf":
		lines(printfCase)
	case "scanf":
		lines(scanfCase)
	case "misc":
		lines(miscCase)
	case "units":
		unitsMain()
	}
}

// miscCase: kind hex(input) result
func miscCase(f []string) (string, string) {
	in := unhex(f[1])
	s := lit(in)
	e := cpp.CPtr_Add_CPtr_i(s, int32(len(in)))
	var got string
	switch f[0] {
	case "fci":
		v := int32(0x55555555)
		r := cpp.Std_from_chars_CPtr_CPtr_ir(s, e, &v)
		got = fmt.Sprintf("%d %d %d", r.Ec, cpp.CPtr_Diff(r.Ptr, s), v)
	case "fcu":
		v := uint32(0x55555555)
		r := cpp.Std_from_chars_CPtr_CPtr_ur(s, e, &v)
		got = fmt.Sprintf("%d %d %d", r.Ec, cpp.CPtr_Diff(r.Ptr, s), v)
	case "fcl":
		v := int64(0x5555555555555555)
		r := cpp.Std_from_chars_CPtr_CPtr_lr(s, e, &v)
		got = fmt.Sprintf("%d %d %d", r.Ec, cpp.CPtr_Diff(r.Ptr, s), v)
	case "fcd":
		v := 0.25
		r := cpp.Std_from_chars_CPtr_CPtr_dr(s, e, &v)
		got = fmt.Sprintf("%d %d %016x", r.Ec, cpp.CPtr_Diff(r.Ptr, s), math.Float64bits(v))
	case "strtol":
		end := s
		v := cpp.R_strtol(s, &end, 10)
		got = fmt.Sprintf("%d %d", v, cpp.CPtr_Diff(end, s))
	case "atoi":
		got = fmt.Sprint(cpp.R_atoi(s))
	case "atol":
		got = fmt.Sprint(cpp.R_atol(s))
	case "strtod":
		end := s
		v := cpp.R_strtod_CPtr_CPtrr(s, &end)
		got = fmt.Sprintf("%016x %d", math.Float64bits(v), cpp.CPtr_Diff(end, s))
	case "ctime":
		t, _ := strconv.ParseInt(string(in), 10, 64)
		p := cpp.R_ctime_Ptrl(cpp.NewPtr_TA_l([]int64{t}, 0))
		got = hex.EncodeToString(p.B[p.O : int(p.O)+int(cpp.R_strlen(p))])
	case "nprocs":
		got = fmt.Sprint(cpp.Sys_hardware_concurrency())
	case "ctype":
		c64, _ := strconv.Atoi(string(in))
		c := int32(c64)
		got = fmt.Sprintf("%d%d%d%d%d%d%d%d%d %d %d", cpp.R_isdigit(c), cpp.R_isalpha(c), cpp.R_isalnum(c),
			cpp.R_isspace(c), cpp.R_isupper(c), cpp.R_islower(c), cpp.R_ispunct(c), cpp.R_isprint(c),
			cpp.R_isxdigit(c), cpp.R_toupper(c), cpp.R_tolower(c))
	}
	if got != f[2] {
		got = fmt.Sprintf("%s  (%s %q)", got, f[0], in)
	}
	return f[2], got
}

func unhex(s string) []byte {
	if s == "-" {
		return nil
	}
	b, err := hex.DecodeString(s)
	if err != nil {
		panic(err)
	}
	return b
}

func lit(b []byte) cpp.CPtr { return cpp.Lit(string(b)) }

func lines(check func(f []string) (string, string)) {
	in := bufio.NewScanner(os.Stdin)
	in.Buffer(make([]byte, 1<<20), 1<<20)
	n, bad := 0, 0
	for in.Scan() {
		f := strings.Split(in.Text(), "\t")
		n++
		want, got := check(f)
		if want != got {
			bad++
			if bad <= 15 {
				label := f[0]
				if b, err := hex.DecodeString(f[0]); err == nil {
					label = string(b)
				}
				fmt.Printf("MISMATCH %q\n  glibc: %s\n  Go:    %s\n", label, want, got)
			}
		}
	}
	fmt.Printf("%s cases: %d, mismatches: %d\n", os.Args[1], n, bad)
}

// printfCase: hex(format) types args size ret hex(output)
func printfCase(f []string) (string, string) {
	format := lit(unhex(f[0]))
	var args []any
	vals := strings.Split(f[2], ",")
	for i, t := range f[1] {
		v := vals[i]
		switch t {
		case 'd':
			b, _ := strconv.ParseUint(v, 16, 64)
			args = append(args, math.Float64frombits(b))
		case 'i':
			b, _ := strconv.ParseUint(v, 16, 32)
			args = append(args, int32(uint32(b)))
		case 'l':
			b, _ := strconv.ParseUint(v, 16, 64)
			args = append(args, int64(b))
		case 's':
			args = append(args, lit(unhex(v)))
		case 'S':
			args = append(args, cpp.CPtr{})
		}
	}
	size, _ := strconv.Atoi(f[3])
	buf := cpp.R_Buf_i(1024)
	for i := range buf.B {
		buf.B[i] = 0x55
	}
	buf.B[0] = 0
	r := cpp.R_snprintf_CPtr_ul_CPtr_oA(buf, uint64(size), format, args...)
	out := "-"
	if size > 0 {
		n := 0
		for buf.B[n] != 0 {
			n++
		}
		if n > 0 {
			out = hex.EncodeToString(buf.B[:n])
		}
	}
	return f[4] + "\t" + f[5], strconv.Itoa(int(r)) + "\t" + out
}

// scanfCase: hex(format) hex(input) types ret values
func scanfCase(f []string) (string, string) {
	format, input, types := lit(unhex(f[0])), lit(unhex(f[1])), f[2]
	const fill32, fill64 = 0x55555555, 0x5555555555555555
	ints := make([]int32, len(types))
	longs := make([]int64, len(types))
	floats := make([]float32, len(types))
	doubles := make([]float64, len(types))
	bufs := make([]cpp.CPtr, len(types))
	var args []any
	for k, t := range types {
		k := k
		ints[k], longs[k] = fill32, fill64
		floats[k], doubles[k] = math.Float32frombits(fill32), math.Float64frombits(fill64)
		switch t {
		case 'i', 'n':
			args = append(args, cpp.R_RefI(func(v int32) { ints[k] = v }))
		case 'u': // as the engine does: a long setter, kept as unsigned int
			args = append(args, cpp.R_RefL(func(v int64) { ints[k] = int32(uint32(v)) }))
		case 'l':
			args = append(args, cpp.R_RefL(func(v int64) { longs[k] = v }))
		case 'f':
			args = append(args, cpp.R_RefF(func(v float32) { floats[k] = v }))
		case 'd':
			args = append(args, cpp.R_RefD(func(v float64) { doubles[k] = v }))
		case 'k': // a char: an int setter, kept as signed char
			ints[k] = 0x55
			args = append(args, cpp.R_RefI(func(v int32) { ints[k] = int32(int8(v)) }))
		case 's', 'c':
			bufs[k] = cpp.R_Buf_i(64)
			for i := range bufs[k].B {
				bufs[k].B[i] = 0x55
			}
			args = append(args, bufs[k])
		}
	}
	r := cpp.R_sscanf(input, format, args...)
	var sb strings.Builder
	for k, t := range types {
		switch t {
		case 'i', 'n', 'u', 'k':
			fmt.Fprintf(&sb, "%d", ints[k])
		case 'l':
			fmt.Fprintf(&sb, "%d", longs[k])
		case 'f':
			fmt.Fprintf(&sb, "%08x", math.Float32bits(floats[k]))
		case 'd':
			fmt.Fprintf(&sb, "%016x", math.Float64bits(doubles[k]))
		case 's', 'c':
			sb.WriteString(hex.EncodeToString(bufs[k].B[:12]))
		}
		sb.WriteString(" ")
	}
	want := f[3] + "\t"
	if len(f) > 4 {
		want += f[4]
	}
	return want, strconv.Itoa(int(r)) + "\t" + sb.String()
}
