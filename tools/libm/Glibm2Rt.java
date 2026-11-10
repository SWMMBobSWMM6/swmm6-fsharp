// Glibm2Rt.java - runtime additions for set 2 of the Java translation of glibc 2.39's math routines (Glibm2.java):
// frexp and ldexp, which glibc's cbrt calls, and quiet. Hand ports of glibc 2.39's sysdeps/ieee754/dbl-64/s_frexp.c,
// s_scalbn.c and math/s_ldexp_template.c (exact operations: only exact scalings by powers of two and, for a
// subnormal result, one correctly rounded multiplication). frexp's two results come from two pure functions, so
// nothing here keeps state between calls. Derived from the GNU C Library and so, like it, licensed under the GNU
// Lesser General Public License, version 2.1 or later (LGPL-2.1-or-later).
// Written by Claude (Anthropic) for the SWMM6 ports; see AI-USE.md.
package cpp;

class Glibm2Rt extends GlibmRt {
  /** A NaN as x86 passes it on: quieted, sign and payload kept. */
  static double quiet(double x) { return Double.longBitsToDouble(Double.doubleToRawLongBits(x) | (1L << 51)); }

  /** glibc's __frexp, the mantissa: 0.5 <= |m| < 1; zero, infinity and NaN come back as x + x. */
  static double frexp(double x) {
    long ix = Double.doubleToRawLongBits(x);
    int ex = (int) (0x7ff & (ix >> 52));
    if (ex != 0x7ff && x != 0.0) {
      if (ex == 0) {                                   // subnormal
        x *= 0x1p54;
        ix = Double.doubleToRawLongBits(x);
      }
      return Double.longBitsToDouble((ix & 0x800fffffffffffffL) | 0x3fe0000000000000L);
    }
    return x + x;                                      // quiets a signaling NaN
  }
  /** glibc's __frexp, the exponent (0 for zero, infinity and NaN). */
  static int frexpExp(double x) {
    long ix = Double.doubleToRawLongBits(x);
    int ex = (int) (0x7ff & (ix >> 52));
    if (ex != 0x7ff && x != 0.0) {
      if (ex == 0) {
        x *= 0x1p54;
        ix = Double.doubleToRawLongBits(x);
        return (int) (0x7ff & (ix >> 52)) - 1022 - 54;
      }
      return ex - 1022;
    }
    return 0;
  }

  private static final double TWO54 = 0x1p54, TWOM54 = 0x1p-54, HUGE = 1.0e+300, TINY = 1.0e-300;
  /** glibc's __scalbn (dbl-64/s_scalbn.c). */
  static double scalbn(double x, int n) {
    long ix = Double.doubleToRawLongBits(x);
    long k = (ix >> 52) & 0x7ff;
    if (k == 0) {                                      // 0 or subnormal x
      if ((ix & 0xfffffffffffffL) == 0) return x;
      x *= TWO54;
      ix = Double.doubleToRawLongBits(x);
      k = ((ix >> 52) & 0x7ff) - 54;
    }
    if (k == 0x7ff) return x + x;                      // NaN or Inf
    if (n < -50000) return TINY * copysign(TINY, x);
    if (n > 50000 || k + n > 0x7fe) return HUGE * copysign(HUGE, x);
    k += n;
    if (k > 0) return Double.longBitsToDouble((ix & 0x800fffffffffffffL) | (k << 52));
    if (k <= -54) return TINY * copysign(TINY, x);
    k += 54;
    x = Double.longBitsToDouble((ix & 0x800fffffffffffffL) | (k << 52));
    return x * TWOM54;
  }
  /** glibc's __ldexp (math/s_ldexp_template.c; errno not modelled). */
  static double ldexp(double value, int exp) {
    if (Double.isNaN(value) || Double.isInfinite(value) || value == 0) return value + value;
    return scalbn(value, exp);
  }
}
