// glibm2_rt.js - runtime for set 2 of the TypeScript translation of glibc 2.39's math routines (glibm2.ts): all of
// glibm_rt.ts, plus frexp and ldexp, which glibc's cbrt calls, and quiet. Hand ports of glibc 2.39's
// sysdeps/ieee754/dbl-64/s_frexp.c, s_scalbn.c and math/s_ldexp_template.c (exact operations: only exact scalings
// by powers of two and, for a subnormal result, one correctly rounded multiplication). Derived from the GNU C
// Library and so, like it, licensed under the GNU Lesser General Public License, version 2.1 or later
// (LGPL-2.1-or-later).
// Written by Claude (Anthropic) for the SWMM6 ports; see AI-USE.md.
export * from "./glibm_rt.js";
import { copysign } from "./glibm_rt.js";

const F = new Float64Array(1);
const W = new Int32Array(F.buffer);        // little-endian: W[0] low word, W[1] high word
const TWO54 = 18014398509481984, TWOM54 = 5.551115123125783e-17, HUGE = 1.0e+300, TINY = 1.0e-300;

/** A NaN as x86 passes it on: quieted, sign and payload kept. */
export function quiet(x) { F[0] = x; W[1] = W[1] | 0x80000; return F[0]; }

/** glibc's __frexp, the mantissa: 0.5 <= |m| < 1; zero, infinity and NaN come back as x + x. */
export function frexp(x) {
  F[0] = x;
  const ex = (W[1] >>> 20) & 0x7ff;
  if (ex !== 0x7ff && x !== 0) {
    if (ex === 0) F[0] = x * TWO54;                 // subnormal
    W[1] = (W[1] & 0x800fffff) | 0x3fe00000;
    return F[0];
  }
  return x + x;                                     // quiets a signaling NaN
}
/** glibc's __frexp, the exponent (0 for zero, infinity and NaN). */
export function frexpExp(x) {
  F[0] = x;
  const ex = (W[1] >>> 20) & 0x7ff;
  if (ex === 0x7ff || x === 0) return 0;
  if (ex !== 0) return ex - 1022;
  F[0] = x * TWO54;
  return ((W[1] >>> 20) & 0x7ff) - 1022 - 54;
}

/** glibc's __scalbn (dbl-64/s_scalbn.c). */
export function scalbn(x, n) {
  F[0] = x;
  let k = (W[1] >>> 20) & 0x7ff;
  if (k === 0) {                                    // 0 or subnormal x
    if ((W[1] & 0xfffff) === 0 && W[0] === 0) return x;
    x = x * TWO54;
    F[0] = x;
    k = ((W[1] >>> 20) & 0x7ff) - 54;
  }
  if (k === 0x7ff) return x + x;                    // NaN or Inf
  if (n < -50000) return TINY * copysign(TINY, x);
  if (n > 50000 || k + n > 0x7fe) return HUGE * copysign(HUGE, x);
  k += n;
  if (k > 0) { W[1] = (W[1] & 0x800fffff) | (k << 20); return F[0]; }
  if (k <= -54) return TINY * copysign(TINY, x);
  k += 54;
  W[1] = (W[1] & 0x800fffff) | (k << 20);
  return F[0] * TWOM54;
}
/** glibc's __ldexp (math/s_ldexp_template.c; errno not modelled). */
export function ldexp(value, exp) {
  if (value !== value || value === Infinity || value === -Infinity || value === 0) return value + value;
  return scalbn(value, exp);
}
