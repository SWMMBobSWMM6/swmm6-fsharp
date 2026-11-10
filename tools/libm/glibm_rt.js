// glibm_rt.js - runtime for the TypeScript translation of glibc 2.39's double-precision math routines.
//
// The translated routines (glibm.ts, written by tools/libm/gim2x.py from GCC's optimized GIMPLE) need four
// things JavaScript does not have: an exactly rounded fused multiply-add, the bit patterns of doubles, x86's
// float-to-integer conversions, and glibc's error helpers. All are here, plus a hand port of branred.c
// (huge-argument reduction for sin/cos/tan, compiled by glibc without FMA, so plain double arithmetic in
// source order reproduces it). Written by Claude (Anthropic); see AI-USE.md.
// Copied unchanged from the SWMM5 TypeScript port (swmm5-ports/ts/src/glibm_rt.ts) for the SWMM6 ports.
// Written by Claude (Anthropic) for the SWMM6 ports; see AI-USE.md.

const F = new Float64Array(1);
const W = new Int32Array(F.buffer);        // little-endian: W[0] low word, W[1] high word
const U = new BigUint64Array(F.buffer);
const S = new BigInt64Array(F.buffer);

function p2(e) {            // 2^e, built from its bit pattern (JavaScript has no hex floats)
  F[0] = 0; W[1] = (e + 1023) << 20; return F[0];
}

// ---- bit patterns
export function f2u(d) { F[0] = d; return U[0]; }
export function f2i(d) { F[0] = d; return S[0]; }
export function u2f(b) { U[0] = BigInt.asUintN(64, b); return F[0]; }
export function hi(d) { F[0] = d; return W[1]; }
export function lo(d) { F[0] = d; return W[0]; }

export function copysign(x, y) {   // bitwise, as glibc's __builtin_copysign
  F[0] = y; const sy = W[1] & 0x80000000;
  F[0] = x; W[1] = (W[1] & 0x7fffffff) | sy; return F[0];
}

// ---- x86-64 conversions (cvttsd2si): NaN and out-of-range give the "integer indefinite" value
export function d2i(x) {
  if (!(x > -2147483649 && x < 2147483648)) return -2147483648;
  return Math.trunc(x) | 0;
}
export function d2l(x) {          // 64-bit conversion, low 32 bits (for unsigned int results)
  if (!(x > -9223372036854775809 && x < 9223372036854775808)) return 0;
  const t = Math.trunc(x);
  return Number(BigInt.asUintN(32, BigInt(t))) | 0;
}
export function d2ll(x) {
  if (!(x > -9223372036854775809 && x < 9223372036854775808)) return -9223372036854775808n;
  return BigInt(Math.trunc(x));
}

// ---- glibc's math_err.c helpers (the value they return; errno is not modelled)
export function math_oflow(sign) { const y = p2(769); return (sign ? -y : y) * y; }
export function math_uflow(sign) { const y = p2(-767); return (sign ? -y : y) * y; }
export function math_may_uflow(sign) { const y = (1.5 * p2(-538)); return (sign ? -y : y) * y; }
export function math_divzero(sign) { return (sign ? -1.0 : 1.0) / 0.0; }
export function math_invalid(x) { return (x - x) / (x - x); }
export function math_check_oflow(y) { return y; }
export function math_check_uflow(y) { return y; }

// ---- exact fused multiply-add
// Fast path (all operands moderate): Boldo and Melquiond's emulation, fma = RN(th + RO(tl + ul)) with
// (uh, ul) = a*b exactly (Dekker), (th, tl) = c + uh exactly (Knuth), RO = round to odd. Anything else
// (zeros, infinities, NaN, very large or very small magnitudes) goes to an exact BigInt computation.
const SPLIT = 134217729;   // 2^27 + 1
const BIG = p2(450), SMALL = p2(-450), CBIG = p2(900), CSMALL = p2(-900);

function roundOdd(x, y) {
  const s = x + y;
  const bb = s - x;
  const e = (x - (s - bb)) + (y - bb);
  if (e === 0) return s;
  F[0] = s;
  if ((W[0] & 1) !== 0) return s;
  // even: step one unit in the last place toward the exact value
  const away = (e > 0) === (s > 0);
  let lo32 = W[0] >>> 0, hi32 = W[1] >>> 0;
  if (away) { lo32 = (lo32 + 1) >>> 0; if (lo32 === 0) hi32 = (hi32 + 1) >>> 0; }
  else { if (lo32 === 0) hi32 = (hi32 - 1) >>> 0; lo32 = (lo32 - 1) >>> 0; }
  W[0] = lo32 | 0; W[1] = hi32 | 0;
  return F[0];
}

export function fma(a, b, c) {
  if ((a === 0 || b === 0) && a * b === 0) return c + a * b;   // an exact (signed) zero product
  const aa = Math.abs(a), ab = Math.abs(b), ac = Math.abs(c);
  if (aa > SMALL && aa < BIG && ab > SMALL && ab < BIG && ac < CBIG && (ac > CSMALL || c === 0)) {
    const uh = a * b;
    // Dekker's split and product error
    let t = SPLIT * a; const ah = t - (t - a), al = a - ah;
    t = SPLIT * b; const bh = t - (t - b), bl = b - bh;
    const ul = ((ah * bh - uh) + ah * bl + al * bh) + al * bl;
    if (c === 0) {
      // fma(a, b, +-0): the rounded product, except that an exact zero keeps IEEE's sign rule
      return uh;
    }
    const th = c + uh;
    const bv = th - c;
    const tl = (c - (th - bv)) + (uh - bv);
    return th + roundOdd(tl, ul);
  }
  return fmaExact(a, b, c);
}

function decompose(x) {   // x = m * 2^e, m an integer
  F[0] = x;
  const hi32 = W[1] >>> 0, lo32 = W[0] >>> 0;
  const exp = (hi32 >>> 20) & 0x7ff;
  let m = (BigInt(hi32 & 0xfffff) << 32n) | BigInt(lo32);
  let e;
  if (exp === 0) e = -1074;
  else { m |= 1n << 52n; e = exp - 1075; }
  if (hi32 >>> 31) m = -m;
  return [m, e];
}

function bitlen(n) { return n === 0n ? 0 : n.toString(2).length; }

// x86's NaN results: a NaN operand comes back quieted (the first NaN of a, b, c here; with two NaN operands
// the hardware's choice depends on the instruction form the compiler picked), an invalid operation gives the
// "default NaN" with the sign bit set. JavaScript's NaN literal has the sign bit clear, so it is not used.
function quiet(x) { F[0] = x; W[1] |= 0x80000; return F[0]; }
const DEFAULT_NAN = (() => { W[1] = 0xfff80000 | 0; W[0] = 0; return F[0]; })();
function fmaExact(a, b, c) {
  if (Number.isNaN(a)) return quiet(a);
  if (Number.isNaN(b)) return quiet(b);
  if (Number.isNaN(c)) return quiet(c);
  const pInf = !Number.isFinite(a) || !Number.isFinite(b);
  if (pInf) {
    if (a === 0 || b === 0) return DEFAULT_NAN;
    const p = a * b;                                   // +-Infinity
    if (!Number.isFinite(c) && Math.sign(c) !== Math.sign(p)) return DEFAULT_NAN;
    return p;
  }
  if (!Number.isFinite(c)) return c;
  const [ma, ea] = decompose(a), [mb, eb] = decompose(b), [mc, ec] = decompose(c);
  const mp = ma * mb, ep = ea + eb;
  if (mp === 0n && mc === 0n) {
    const sp = (Object.is(a, -0) || a < 0) !== (Object.is(b, -0) || b < 0);
    const sc = Object.is(c, -0);
    return sp && sc ? -0 : 0;
  }
  const e0 = Math.min(mp === 0n ? ec : ep, mc === 0n ? ep : ec);
  const s = (mp === 0n ? 0n : mp << BigInt(ep - e0)) + (mc === 0n ? 0n : mc << BigInt(ec - e0));
  if (s === 0n) return 0;
  const neg = s < 0n;
  let mag = neg ? -s : s;
  const n = bitlen(mag);
  const E = n - 1 + e0;                                 // exponent of the leading bit
  let shift = E >= -1022 ? n - 53 : -1074 - e0;
  let q, k;
  if (shift > 0) {
    const sh = BigInt(shift);
    q = mag >> sh;
    const rem = mag & ((1n << sh) - 1n), half = 1n << (sh - 1n);
    if (rem > half || (rem === half && (q & 1n) === 1n)) q += 1n;
    k = e0 + shift;
  } else { q = mag; k = e0; }
  if (k + bitlen(q) - 1 > 1023) return neg ? -Infinity : Infinity;
  // q < 2^54 converts exactly, and 2^k (-1074 <= k <= 971) is representable, so the product is exact
  const r = Number(q) * pow2(k);
  return neg ? -r : r;
}

function pow2(k) {
  if (k >= -1022) return u2f(BigInt(k + 1023) << 52n);
  return u2f(1n << BigInt(k + 1074));
}

// ---- branred.c: x = n*pi/2 + (a + aa), returns n mod 4 (glibc compiles this without FMA)
let TOVERP = null;
export function setToverp(t) { TOVERP = t; }
export let brA = 0, brAA = 0;
const t576 = p2(576), tm600 = p2(-600), tm24 = p2(-24), big = 6755399441055744.0, big1 = 27021597764222976.0;
const mp1 = u2f(0x3FF921FB58000000n), mp2 = u2f(0xBE4DDE9740000000n);
const hp0b = u2f(0x3FF921FB54442D18n), hp1b = u2f(0x3C91A62633145C07n);
const SPLIT_BR = 134217729.0;
export function branred(x) {
  const toverp = TOVERP;
  const r = new Float64Array(6);
  let s, t, sum, b, bb, k;
  x *= tm600;
  t = x * SPLIT_BR;
  const x1 = t - (t - x);
  const x2 = x - x1;
  const half = (xx) => {
    let sm = 0;
    k = (hi(xx) >> 20) & 2047;
    k = ((k - 450) / 24) | 0;
    if (k < 0) k = 0;
    F[0] = t576; W[1] = (W[1] - ((k * 24) << 20)) | 0; let gor = F[0];
    for (let i = 0; i < 6; i++) { r[i] = xx * toverp[k + i] * gor; gor *= tm24; }
    for (let i = 0; i < 3; i++) { s = (r[i] + big) - big; sm += s; r[i] -= s; }
    t = 0;
    for (let i = 0; i < 6; i++) t += r[5 - i];
    bb = (((((r[0] - t) + r[1]) + r[2]) + r[3]) + r[4]) + r[5];
    s = (t + big) - big;
    sm += s;
    t -= s;
    b = t + bb;
    bb = (t - b) + bb;
    s = (sm + big1) - big1;
    sm -= s;
    return [b, bb, sm];
  };
  const [b1, bb1, sum1] = half(x1);
  const [b2, bb2, sum2] = half(x2);
  sum = sum1 + sum2;
  b = b1 + b2;
  bb = (Math.abs(b1) > Math.abs(b2)) ? (b1 - b) + b2 : (b2 - b) + b1;
  if (b > 0.5) { b -= 1.0; sum += 1.0; }
  else if (b < -0.5) { b += 1.0; sum -= 1.0; }
  s = b + (bb + bb1 + bb2);
  t = ((b - s) + bb) + (bb1 + bb2);
  b = s * SPLIT_BR;
  const t1 = b - (b - s);
  const t2 = s - t1;
  b = s * hp0b;
  bb = (((t1 * mp1 - b) + t1 * mp2) + t2 * mp1) + (t2 * mp2 + s * hp1b + t * hp0b);
  s = b + bb;
  t = (b - s) + bb;
  brA = s; brAA = t;
  return d2i(sum) & 3;
}

// ---- tables: concatenated 16-hex-digit bit patterns
export function u64tab(hex) {
  const n = hex.length / 16, out = new BigUint64Array(n);
  for (let i = 0; i < n; i++) out[i] = BigInt('0x' + hex.substr(16 * i, 16));
  return out;
}
export function f64tab(hex) { return new Float64Array(u64tab(hex).buffer); }
export function f64c(hex) { return u2f(BigInt('0x' + hex)); }
