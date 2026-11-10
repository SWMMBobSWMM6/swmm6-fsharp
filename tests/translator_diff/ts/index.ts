// TEST-ONLY minimal cpp runtime for the numeric differential test (scratch; not the ports' runtime).
// Implements cpp.L exactly as ts/RUNTIME_HELPERS.md specifies.
export type L = number | bigint;
const MAX = 9007199254740991;
const canon = (b: bigint): L => (b >= -9007199254740991n && b <= 9007199254740991n) ? Number(b) : b;
const big = (x: L): bigint => typeof x === 'bigint' ? x : BigInt(x);
const n0 = (r: number): number => r === 0 ? 0 : r;          // no -0
const isN = (x: L): x is number => typeof x === 'number';
function wrapS(b: bigint): L { return canon(BigInt.asIntN(64, b)); }
function wrapU(b: bigint): L { return canon(BigInt.asUintN(64, b)); }
class OverflowException extends Error {}
class DivideByZeroException extends Error {}
function f32big(b: bigint): number {
  // b rounded once to the nearest float (ties to even), as cvtsi2ss / the .NET ulong->float conversion do
  const neg = b < 0n; let m = neg ? -b : b;
  const bits = m.toString(2).length;
  if (bits <= 24) return Math.fround(Number(b));
  const sh = BigInt(bits - 24); let q = m >> sh; const r = m - (q << sh); const half = 1n << (sh - 1n);
  if (r > half || (r === half && (q & 1n) === 1n)) q += 1n;
  const v = Number(q) * Math.pow(2, Number(sh));
  return Math.fround(neg ? -v : v);
}
export const L = {
  addL: (a: L, b: L): L => { if (isN(a) && isN(b)) { const r = a + b; if (r >= -MAX && r <= MAX) return r; } return wrapS(big(a) + big(b)); },
  subL: (a: L, b: L): L => { if (isN(a) && isN(b)) { const r = a - b; if (r >= -MAX && r <= MAX) return n0(r); } return wrapS(big(a) - big(b)); },
  mulL: (a: L, b: L): L => { if (isN(a) && isN(b)) { const r = a * b; if (r >= -MAX && r <= MAX) return n0(r); } return wrapS(big(a) * big(b)); },
  addUL: (a: L, b: L): L => { if (isN(a) && isN(b)) { const r = a + b; if (r <= MAX) return r; } return wrapU(big(a) + big(b)); },
  subUL: (a: L, b: L): L => { if (isN(a) && isN(b)) { const r = a - b; if (r >= 0) return n0(r); } return wrapU(big(a) - big(b)); },
  mulUL: (a: L, b: L): L => { if (isN(a) && isN(b)) { const r = a * b; if (r <= MAX) return n0(r); } return wrapU(big(a) * big(b)); },
  divL: (a: L, b: L): L => { const x = big(a), y = big(b); if (y === 0n) throw new DivideByZeroException(); if (x === -(1n << 63n) && y === -1n) throw new OverflowException(); return canon(x / y); },
  remL: (a: L, b: L): L => { const x = big(a), y = big(b); if (y === 0n) throw new DivideByZeroException(); if (x === -(1n << 63n) && y === -1n) throw new OverflowException(); return canon(x % y); },
  divUL: (a: L, b: L): L => { const y = big(b); if (y === 0n) throw new DivideByZeroException(); return canon(big(a) / y); },
  remUL: (a: L, b: L): L => { const y = big(b); if (y === 0n) throw new DivideByZeroException(); return canon(big(a) % y); },
  andL: (a: L, b: L): L => wrapS(big(a) & big(b)), orL: (a: L, b: L): L => wrapS(big(a) | big(b)), xorL: (a: L, b: L): L => wrapS(big(a) ^ big(b)),
  andUL: (a: L, b: L): L => wrapU(big(a) & big(b)), orUL: (a: L, b: L): L => wrapU(big(a) | big(b)), xorUL: (a: L, b: L): L => wrapU(big(a) ^ big(b)),
  shlL: (a: L, n: number): L => wrapS(big(a) << BigInt(n & 63)), shlUL: (a: L, n: number): L => wrapU(big(a) << BigInt(n & 63)),
  shrL: (a: L, n: number): L => wrapS(big(a) >> BigInt(n & 63)), shrUL: (a: L, n: number): L => wrapU(big(a) >> BigInt(n & 63)),
  ushrL: (a: L, n: number): L => wrapS(BigInt.asUintN(64, big(a)) >> BigInt(n & 63)),
  negL: (a: L): L => wrapS(-big(a)), notL: (a: L): L => wrapS(~big(a)), notUL: (a: L): L => wrapU(~big(a)),
  incL: (a: L): L => L.addL(a, 1), decL: (a: L): L => L.subL(a, 1), incUL: (a: L): L => L.addUL(a, 1), decUL: (a: L): L => L.subUL(a, 1),
  divI: (a: number, b: number): number => { if (b === 0) throw new DivideByZeroException(); if (a === -2147483648 && b === -1) throw new OverflowException(); return (a / b) | 0; },
  remI: (a: number, b: number): number => { if (b === 0) throw new DivideByZeroException(); if (a === -2147483648 && b === -1) throw new OverflowException(); return (a % b) | 0; },
  divU: (a: number, b: number): number => { if (b === 0) throw new DivideByZeroException(); return (a / b) >>> 0; },
  remU: (a: number, b: number): number => { if (b === 0) throw new DivideByZeroException(); return (a % b) >>> 0; },
  toI32: (x: L): number => isN(x) ? x | 0 : Number(BigInt.asIntN(32, x)),
  toU32: (x: L): number => isN(x) ? x >>> 0 : Number(BigInt.asUintN(32, x)),
  toI64: (x: L): L => isN(x) ? x : wrapS(x),
  toU64: (x: L): L => isN(x) && x >= 0 ? x : wrapU(big(x)),
  toF64: (x: L): number => Number(x),
  toF32: (x: L): number => isN(x) ? Math.fround(x) : f32big(x),
  ix: (x: L): number => Number(x),
};
export class Cell<T> { v: T; constructor(v: T) { this.v = v; } get(): T { return this.v; } set(x: T) { this.v = x; } }
export class Ref<T> { get(): T { return undefined as any; } set(_v: T): void { } }
export class ArrRef<T> { a: T[]; i: number; constructor(a: T[], i: number) { this.a = a; this.i = i; } get(): T { return this.a[this.i]; } set(v: T) { this.a[this.i] = v; } }
export function fieldRef(o: any, name: string) { return { get: () => o[name], set: (v: any) => { o[name] = v; } }; }
export class Raw { static V = new Raw(); }
export class SysException extends Error { kind: string; constructor(kind: string, msg: string) { super(msg); this.kind = kind; } }
