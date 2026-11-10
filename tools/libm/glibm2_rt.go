// glibm2_rt.go - runtime additions for set 2 of the Go translation of glibc 2.39's math routines (glibm2.go):
// frexp and ldexp, which glibc's cbrt calls. Hand ports of glibc 2.39's sysdeps/ieee754/dbl-64/s_frexp.c,
// s_scalbn.c and math/s_ldexp_template.c (exact operations: only exact scalings by powers of two and, for a
// subnormal result, one correctly rounded multiplication). Derived from the GNU C Library and so, like it,
// licensed under the GNU Lesser General Public License, version 2.1 or later (LGPL-2.1-or-later).
// Written by Claude (Anthropic) for the SWMM6 ports; see AI-USE.md.

package glibm

import "math"

// frexp is glibc's __frexp: x = m * 2^e with 0.5 <= |m| < 1; zero, infinity and NaN come back as x + x with e = 0
func frexp(x float64, e *int32) float64 {
	ix := int64(math.Float64bits(x))
	ex := int32(0x7ff & (ix >> 52))
	var ee int32
	if ex != 0x7ff && x != 0 {
		ee = ex - 1022
		if ex == 0 { // subnormal
			x = float64(x * 0x1p54)
			ix = int64(math.Float64bits(x))
			ex = int32(0x7ff & (ix >> 52))
			ee = ex - 1022 - 54
		}
		ix = (ix & int64(-0x7ff0000000000001)) | 0x3fe0000000000000 // keep sign and mantissa, exponent -1
		x = math.Float64frombits(uint64(ix))
	} else {
		x = float64(x + x) // quiets a signaling NaN
	}
	*e = ee
	return x
}

var (
	sbTwo54  = math.Float64frombits(0x4350000000000000)
	sbTwom54 = math.Float64frombits(0x3C90000000000000)
	sbHuge   = 1.0e+300
	sbTiny   = 1.0e-300
)

// scalbn is glibc's __scalbn (dbl-64/s_scalbn.c)
func scalbn(x float64, n int32) float64 {
	ix := int64(math.Float64bits(x))
	k := (ix >> 52) & 0x7ff
	if k == 0 { // 0 or subnormal x
		if ix&0xfffffffffffff == 0 {
			return x
		}
		x = float64(x * sbTwo54)
		ix = int64(math.Float64bits(x))
		k = ((ix >> 52) & 0x7ff) - 54
	}
	if k == 0x7ff {
		return float64(x + x) // NaN or Inf
	}
	if n < -50000 {
		return float64(sbTiny * math.Copysign(sbTiny, x))
	}
	if n > 50000 || k+int64(n) > 0x7fe {
		return float64(sbHuge * math.Copysign(sbHuge, x))
	}
	k += int64(n)
	if k > 0 {
		return math.Float64frombits(uint64((ix & int64(-0x7ff0000000000001)) | (k << 52)))
	}
	if k <= -54 {
		return float64(sbTiny * math.Copysign(sbTiny, x))
	}
	k += 54
	x = math.Float64frombits(uint64((ix & int64(-0x7ff0000000000001)) | (k << 52)))
	return float64(x * sbTwom54)
}

// ldexp is glibc's __ldexp (math/s_ldexp_template.c; errno not modelled)
func ldexp(value float64, exp int32) float64 {
	if math.IsInf(value, 0) || math.IsNaN(value) || value == 0 {
		return float64(value + value)
	}
	return scalbn(value, exp)
}

// Frexp and Ldexp are glibc's frexp and ldexp, exported for the tests (test/check2.go)
func Frexp(x float64) (float64, int32) {
	var e int32
	m := frexp(x, &e)
	return m, e
}
func Ldexp(x float64, n int32) float64 { return ldexp(x, n) }

// nc returns its argument; it makes an operation on two constants a run-time one (see gim2x.py, GoEmitter.binop)
func nc(x float64) float64 { return x }

// quiet is a NaN as x86 passes it on: quieted, sign and payload kept
func quiet(x float64) float64 { return math.Float64frombits(math.Float64bits(x) | 1<<51) }
