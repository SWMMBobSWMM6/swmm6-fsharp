// check2.go - compare set 2 of the Go glibc ports (glibm2.go: Cbrt, Hypot, Atan2, Cosh, Sinh, Tanh, Log2,
// Expm1, Log1p, and the runtime's Frexp and Ldexp) bit for bit with system-libm vectors (mkvectors2.c).
// Run from go/ as: go run ../tools/libm/test/check2.go VECTORS.bin   (it imports openswmm/glibm)
// A NaN result must match glibc's in sign and payload too. With a second argument "native" it measures Go's
// own math package against the same vectors instead. Written by Claude (Anthropic) for the SWMM6 ports.

//go:build ignore

package main

import (
	"encoding/binary"
	"fmt"
	"math"
	"os"
	"time"

	M "openswmm/glibm"
)

func main() {
	b, err := os.ReadFile(os.Args[1])
	if err != nil {
		panic(err)
	}
	native := len(os.Args) > 2 && os.Args[2] == "native"
	p := 0
	f64 := func() float64 { v := math.Float64frombits(binary.LittleEndian.Uint64(b[p:])); p += 8; return v }
	total := 0
	for p < len(b) {
		s := p
		for b[p] != 0 {
			p++
		}
		name := string(b[s:p])
		p++
		n := int(binary.LittleEndian.Uint32(b[p:]))
		p += 4
		na := 1
		if name == "hypot" || name == "atan2" || name == "ldexp" {
			na = 2
		}
		bad, nanSign, nanPay, shown := 0, 0, 0, 0
		t0 := time.Now()
		for i := 0; i < n; i++ {
			x := f64()
			y := 0.0
			if na > 1 {
				y = f64()
			}
			want := f64()
			wantE := 0.0
			if name == "frexp" {
				wantE = f64()
			}
			var got float64
			gotE := int32(0)
			if native {
				got = nativeCall(name, x, y)
			} else {
				switch name {
				case "cbrt":
					got = M.Cbrt(x)
				case "hypot":
					got = M.Hypot(x, y)
				case "atan2":
					got = M.Atan2(x, y)
				case "cosh":
					got = M.Cosh(x)
				case "sinh":
					got = M.Sinh(x)
				case "tanh":
					got = M.Tanh(x)
				case "log2":
					got = M.Log2(x)
				case "expm1":
					got = M.Expm1(x)
				case "log1p":
					got = M.Log1p(x)
				case "frexp":
					got, gotE = M.Frexp(x)
				case "ldexp":
					got = M.Ldexp(x, int32(y))
				}
			}
			gb, wb := math.Float64bits(got), math.Float64bits(want)
			if math.IsNaN(got) && math.IsNaN(want) {
				if gb>>63 != wb>>63 {
					nanSign++
				} else if gb != wb {
					nanPay++
				}
				continue
			}
			if gb != wb || (name == "frexp" && !native && float64(gotE) != wantE) {
				bad++
				if shown < 4 {
					shown++
					fmt.Printf("  %s(%v [%016x], %v [%016x]) = %v [%016x], libm %v [%016x]\n", name, x, math.Float64bits(x), y, math.Float64bits(y), got, gb, want, wb)
				}
			}
		}
		total += bad + nanSign + nanPay
		if native {
			fmt.Printf("Go math.%-6s %d of %d differ from glibc (%.2f%%)\n", name, bad, n, 100*float64(bad)/float64(n))
			continue
		}
		fmt.Printf("%-6s %d of %d differ, %d NaN sign, %d NaN payload   (%.3f us/call)\n", name, bad, n, nanSign, nanPay,
			float64(time.Since(t0).Nanoseconds())/1e3/float64(n))
	}
	if !native {
		fmt.Printf("total differences: %d\n", total)
	}
}

func nativeCall(name string, x, y float64) float64 {
	switch name {
	case "cbrt":
		return math.Cbrt(x)
	case "hypot":
		return math.Hypot(x, y)
	case "atan2":
		return math.Atan2(x, y)
	case "cosh":
		return math.Cosh(x)
	case "sinh":
		return math.Sinh(x)
	case "tanh":
		return math.Tanh(x)
	case "log2":
		return math.Log2(x)
	case "expm1":
		return math.Expm1(x)
	case "log1p":
		return math.Log1p(x)
	case "frexp":
		m, _ := math.Frexp(x)
		return m
	}
	return math.Ldexp(x, int(y))
}
