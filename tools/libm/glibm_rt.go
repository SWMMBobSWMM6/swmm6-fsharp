// glibm_rt.go - runtime for the Go translation of glibc 2.39's double-precision math routines.
//
// The translated routines (glibm.go, written by tools/libm/gim2x.py from GCC's optimized GIMPLE) use
// math.FMA (exactly rounded, like the hardware FMA glibc's x86-64 variants run on), bit casts, x86's
// float-to-integer conversions and glibc's error helpers. All are here, plus a hand port of branred.c
// (huge-argument reduction for sin/cos/tan, compiled by glibc without FMA, so plain double arithmetic in
// source order reproduces it). Every product below is wrapped in float64(...): the Go spec allows the
// compiler to fuse x*y + z into one rounding unless x*y is explicitly converted. Written by Claude
// (Anthropic); see AI-USE.md.

package glibm

import (
	"math"
	"strconv"
)

var (
	qNaN    = math.Float64frombits(0x7ff8000000000000) // GIMPLE's NaN constant (math.NaN() is 0x7ff8000000000001)
	ksNaN   = math.Float64frombits(0x7ff8000000000000) // __kernel_standard's NAN, returned by the SVID wrappers
	negZero = math.Copysign(0, -1)                     // Go constants have no negative zero
)

type number interface {
	~int32 | ~uint32 | ~int64 | ~uint64 | ~float32 | ~float64
}

func p2(e int) float64 { return math.Float64frombits(uint64(e+1023) << 52) }

// x86-64 cvttsd2si: NaN and out-of-range values give the "integer indefinite" value
func d2i(x float64) int32 {
	if !(x > -2147483649.0 && x < 2147483648.0) {
		return math.MinInt32
	}
	return int32(x)
}

func d2l(x float64) int64 {
	if !(x > -9223372036854775809.0 && x < 9223372036854775808.0) {
		return math.MinInt64
	}
	return int64(x)
}

func btoi[T number](b bool) T {
	if b {
		return 1
	}
	return 0
}

// GIMPLE's MIN_EXPR / MAX_EXPR / ?: (not Go's min and max, which treat NaN and signed zeros differently)
func mn[T number](a, b T) T {
	if a < b {
		return a
	}
	return b
}

func mx[T number](a, b T) T {
	if a > b {
		return a
	}
	return b
}

func ifv[T any](c bool, a, b T) T {
	if c {
		return a
	}
	return b
}

func iabs[T ~int32 | ~int64](x T) T {
	if x < 0 {
		return -x
	}
	return x
}

// glibc's math_err.c helpers (the value they return; errno is not modelled)
func math_oflow(sign uint32) float64 {
	y := p2(769)
	return float64(ifv(sign != 0, -y, y) * y)
}
func math_uflow(sign uint32) float64 {
	y := p2(-767)
	return float64(ifv(sign != 0, -y, y) * y)
}
func math_may_uflow(sign uint32) float64 {
	y := float64(1.5 * p2(-538))
	return float64(ifv(sign != 0, -y, y) * y)
}
func math_divzero(sign uint32) float64 {
	z := 0.0
	return ifv(sign != 0, -1.0, 1.0) / z
}
func math_invalid(x float64) float64     { return (x - x) / (x - x) }
func math_check_oflow(y float64) float64 { return y }
func math_check_uflow(y float64) float64 { return y }

// tables: concatenated 16-hex-digit bit patterns
func u64tab(hex string) []uint64 {
	out := make([]uint64, len(hex)/16)
	for i := range out {
		v, err := strconv.ParseUint(hex[16*i:16*i+16], 16, 64)
		if err != nil {
			panic(err)
		}
		out[i] = v
	}
	return out
}

func f64tab(hex string) []float64 {
	u := u64tab(hex)
	out := make([]float64, len(u))
	for i, v := range u {
		out[i] = math.Float64frombits(v)
	}
	return out
}

// branred.c: x = n*pi/2 + (a + aa), returns n mod 4
var (
	t576  = p2(576)
	tm600 = p2(-600)
	tm24  = p2(-24)
	big   = 6755399441055744.0
	big1  = 27021597764222976.0
	mp1   = math.Float64frombits(0x3FF921FB58000000)
	mp2   = math.Float64frombits(0xBE4DDE9740000000)
	hp0b  = math.Float64frombits(0x3FF921FB54442D18)
	hp1b  = math.Float64frombits(0x3C91A62633145C07)
	split = 134217729.0
)

func half(xx float64, toverp []float64) (b, bb, sum float64) {
	var r [6]float64
	var s, t, gor float64
	k := int((math.Float64bits(xx) >> 52) & 2047)
	k = (k - 450) / 24
	if k < 0 {
		k = 0
	}
	gor = math.Float64frombits(math.Float64bits(t576) - (uint64(k*24) << 52))
	for i := 0; i < 6; i++ {
		r[i] = float64(float64(xx*toverp[k+i]) * gor)
		gor = float64(gor * tm24)
	}
	for i := 0; i < 3; i++ {
		s = (r[i] + big) - big
		sum += s
		r[i] -= s
	}
	t = 0
	for i := 0; i < 6; i++ {
		t += r[5-i]
	}
	bb = (((((r[0] - t) + r[1]) + r[2]) + r[3]) + r[4]) + r[5]
	s = (t + big) - big
	sum += s
	t -= s
	b = t + bb
	bb = (t - b) + bb
	s = (sum + big1) - big1
	sum -= s
	return
}

func branred(x float64, toverp []float64, a, aa *float64) int32 {
	x = float64(x * tm600)
	t := float64(x * split)
	x1 := t - (t - x)
	x2 := x - x1
	b1, bb1, sum1 := half(x1, toverp)
	b2, bb2, sum2 := half(x2, toverp)
	sum := sum1 + sum2
	b := b1 + b2
	var bb float64
	if math.Abs(b1) > math.Abs(b2) {
		bb = (b1 - b) + b2
	} else {
		bb = (b2 - b) + b1
	}
	if b > 0.5 {
		b -= 1.0
		sum += 1.0
	} else if b < -0.5 {
		b += 1.0
		sum -= 1.0
	}
	s := b + (bb + bb1 + bb2)
	t = ((b - s) + bb) + (bb1 + bb2)
	b = float64(s * split)
	t1 := b - (b - s)
	t2 := s - t1
	b = float64(s * hp0b)
	bb = (((float64(t1*mp1) - b) + float64(t1*mp2)) + float64(t2*mp1)) + (float64(t2*mp2) + float64(s*hp1b) + float64(t*hp0b))
	s = b + bb
	t = (b - s) + bb
	*a, *aa = s, t
	return d2i(sum) & 3
}

// Exact operations the engine also routes through this package (correctly rounded on every platform).
func Fabs(x float64) float64  { return math.Abs(x) }
func Sqrt(x float64) float64  { return math.Sqrt(x) }
func Floor(x float64) float64 { return math.Floor(x) }
func Ceil(x float64) float64  { return math.Ceil(x) }
