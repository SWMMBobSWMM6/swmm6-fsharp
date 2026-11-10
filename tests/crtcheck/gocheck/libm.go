// libm.go - reads the records ../libm.c writes (x, y, glibc's result) and recomputes each with go/cpp's math
// functions: per function, the number of results whose bits differ, how many of those are NaNs of another sign,
// and the largest difference in units in the last place.
// Written by Claude (Anthropic) for the SWMM6 ports; see AI-USE.md.
package main

import (
	"bufio"
	"encoding/binary"
	"fmt"
	"io"
	"math"
	"os"

	"openswmm/cpp"
)

var fns = []struct {
	name string
	f    func(x, y float64) float64
}{
	{"atan2", cpp.Math_Atan2_d}, {"cbrt", u(cpp.Math_Cbrt_d)}, {"cosh", u(cpp.Math_Cosh_d)}, {"sinh", u(cpp.Math_Sinh_d)},
	{"tanh", u(cpp.Math_Tanh_d)}, {"log2", u(cpp.Math_Log2_d)}, {"expm1", u(cpp.LibM_expm1)}, {"log1p", u(cpp.LibM_log1p)},
	{"hypot", cpp.LibM_hypot}, {"fmod", cpp.LibM_fmod}, {"remainder", cpp.LibM_remainder}, {"round", u(cpp.LibM_round)},
	{"nextafter", cpp.LibM_nextafter}, {"exp", u(cpp.Math_Exp_d)}, {"log", u(cpp.Math_Log_d)}, {"pow", cpp.Math_Pow_d},
	{"sin", u(cpp.Math_Sin_d)}, {"cos", u(cpp.Math_Cos_d)}, {"tan", u(cpp.Math_Tan_d)}, {"asin", u(cpp.Math_Asin_d)},
	{"acos", u(cpp.Math_Acos_d)}, {"atan", u(cpp.Math_Atan_d)}, {"log10", u(cpp.Math_Log10_d)},
	{"floor", u(cpp.Math_Floor_d)}, {"ceil", u(cpp.Math_Ceiling_d)}, {"trunc", u(cpp.Math_Truncate_d)},
	{"sqrt", u(cpp.Math_Sqrt_d)},
}

func u(f func(float64) float64) func(x, y float64) float64 {
	return func(x, _ float64) float64 { return f(x) }
}

// ulps is the distance between two doubles of the same sign in units in the last place.
func ulps(a, b float64) uint64 {
	ia, ib := int64(math.Float64bits(a)), int64(math.Float64bits(b))
	if ia < 0 {
		ia = math.MinInt64 - ia
	}
	if ib < 0 {
		ib = math.MinInt64 - ib
	}
	if ia > ib {
		return uint64(ia - ib)
	}
	return uint64(ib - ia)
}

func libmMain(n int) {
	in := bufio.NewReaderSize(os.Stdin, 1<<20)
	var rec [24]byte
	per := n + nspec*nspec
	fmt.Printf("%-10s %8s %10s %9s %8s  %s\n", "function", "cases", "mismatch", "NaN-sign", "max ulp", "first mismatch (x, y: glibc vs Go)")
	total := 0
	for _, fn := range fns {
		bad, nanSign := 0, 0
		var hist [5]int // 1 ulp, 2, 3-10, >10, NaN vs number
		big := ""
		var maxU uint64
		first := ""
		for i := 0; i < per; i++ {
			if _, err := io.ReadFull(in, rec[:]); err != nil {
				fmt.Fprintln(os.Stderr, "short input:", err)
				os.Exit(2)
			}
			x := math.Float64frombits(binary.LittleEndian.Uint64(rec[0:]))
			y := math.Float64frombits(binary.LittleEndian.Uint64(rec[8:]))
			want := binary.LittleEndian.Uint64(rec[16:])
			got := math.Float64bits(fn.f(x, y))
			if got == want {
				continue
			}
			bad++
			w, g := math.Float64frombits(want), math.Float64frombits(got)
			if math.IsNaN(w) && math.IsNaN(g) {
				nanSign++
			} else if !math.IsNaN(w) && !math.IsNaN(g) {
				d := ulps(w, g)
				if d > maxU {
					maxU = d
				}
				switch {
				case d == 1:
					hist[0]++
				case d == 2:
					hist[1]++
				case d <= 10:
					hist[2]++
				default:
					hist[3]++
					if big == "" {
						big = fmt.Sprintf("%v, %v: %v vs %v", x, y, w, g)
					}
				}
			} else {
				maxU = math.MaxUint64
				hist[4]++
				if big == "" {
					big = fmt.Sprintf("%v, %v: %v vs %v", x, y, w, g)
				}
			}
			if first == "" {
				first = fmt.Sprintf("%v, %v: %v vs %v", x, y, w, g)
			}
		}
		total += bad
		mu := fmt.Sprint(maxU)
		if maxU == math.MaxUint64 {
			mu = "NaN/num"
		}
		fmt.Printf("%-10s %8d %10d %9d %8s  %s\n", fn.name, per, bad, nanSign, mu, first)
		if bad > 0 && os.Getenv("HIST") != "" {
			fmt.Printf("           ulp 1: %d, 2: %d, 3-10: %d, >10: %d, NaN vs number: %d  %s\n", hist[0], hist[1], hist[2], hist[3], hist[4], big)
		}
	}
	fmt.Printf("total mismatches: %d\n", total)
}

const nspec = 24
