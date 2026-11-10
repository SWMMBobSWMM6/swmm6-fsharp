// lcheck.ts - the TypeScript side of lcheck.cs: the same operations through cpp.L and cpp.Conv, over the same edge
// values, printed the same way. A result that is not in canonical form (a number that is -0 or not a safe integer,
// a bigint that is a safe integer, a value out of its type's range) is printed with a "!" so that it can never match.
// Written by Claude (Anthropic) for the SWMM6 ports; see AI-USE.md.
import * as cpp from './cpp/index.js';
declare const process: any;     // Node (no @types/node here)

type L = cpp.L;
const out: string[] = [];
function P(s: string): void { out.push(s); }

const SAFE = 9007199254740991n;
/** A canonical long or ulong from a bigint. */
function c(x: bigint): L { return (x >= -SAFE && x <= SAFE) ? Number(x) : x; }
/** A long result as text, with "!" unless canonical. */
function sl(x: L): string { return chk(x, -(1n << 63n), (1n << 63n) - 1n); }
function su(x: L): string { return chk(x, 0n, (1n << 64n) - 1n); }
function chk(x: L, lo: bigint, hi: bigint): string {
	if (typeof x === 'number') {
		if (Object.is(x, -0)) return "-0!";
		if (!Number.isSafeInteger(x)) return String(x) + "!";
		if (BigInt(x) < lo || BigInt(x) > hi) return String(x) + "!range";
		return String(x);
	}
	if (typeof x !== 'bigint') return String(x) + "!type";
	if (x >= -SAFE && x <= SAFE) return String(x) + "!bigint";
	if (x < lo || x > hi) return String(x) + "!range";
	return String(x);
}
/** An int / uint / short ... result: a number in [lo, hi], never -0. */
function si(x: number, lo: number, hi: number): string {
	if (typeof x !== 'number' || Object.is(x, -0) || !Number.isInteger(x) || x < lo || x > hi) return String(x) + "!";
	return String(x);
}
const dv = new DataView(new ArrayBuffer(8));
function D(x: number): string {
	if (x !== x) return "nan";
	dv.setFloat64(0, x);
	return dv.getBigUint64(0).toString(16).padStart(16, '0');
}
function F(x: number): string { return Math.fround(x) === x || x !== x ? D(x) : D(x) + "!notfloat"; }
function T(f: () => string): string {
	try { return f(); }
	catch (e: any) {
		const k: string = e != null ? (e.kind ?? e.Kind ?? e.type ?? '') : '';
		if (e instanceof cpp.CppException) return "throw:CppException!";
		if (k.endsWith("Exception")) return "throw:" + k;
		const m = /(DivideByZero|Overflow)Exception/.exec(String(e && (e.message + ' ' + e.name)));
		return m ? "throw:" + m[0] : "throw:?" + String(e);
	}
}

const Ls: L[] = [
	0n, 1n, -1n, 2n, -2n, 3n, 7n, -7n, 100n, -100n, 2147483647n, -2147483648n, 2147483648n, -2147483649n, 4294967295n, 4294967296n,
	4503599627370496n, 9007199254740991n, -9007199254740991n, 9007199254740992n, -9007199254740992n, 9007199254740993n,
	-9007199254740993n, 4611686018427387904n, 9223372036854775807n, -9223372036854775808n, -9223372036854775807n, 123456789012345678n,
	-987654321098765432n, 0x5555555555555555n, 0x7FFFFFFFFFFFFFFEn, 94906265n, 3037000499n, -3037000500n,
].map(c);
const Us: L[] = [
	0n, 1n, 2n, 3n, 7n, 100n, 2147483647n, 2147483648n, 4294967295n, 4294967296n, 4503599627370496n, 9007199254740991n,
	9007199254740992n, 9007199254740993n, 4611686018427387904n, 9223372036854775807n, 9223372036854775808n,
	9223372036854775809n, 18446744073709551615n, 18446744073709551614n, 0xAAAAAAAAAAAAAAAAn, 12345678901234567890n,
	0xcbf29ce484222325n, 0x100000001b3n, 4294967311n, 0x9E3779B97F4A7C15n,
].map(c);
const Is = [0, 1, -1, 2, -2, 7, -7, 3, 2147483647, -2147483648, -2147483647, 46341, 65536, 100000, -100000];
const Ws = [0, 1, 2, 7, 3, 2147483647, 2147483648, 4294967295, 4000000000, 65535, 65536];
const Sh = [0, 1, 2, 7, 31, 32, 33, 52, 53, 62, 63, 64, 65, -1, 127];
const F32L: L[] = [
	16777217n, 33554435n, (1n << 60n) + (1n << 36n) + 1n, (1n << 60n) + (1n << 36n), (1n << 60n) + (3n << 36n), (1n << 60n) + (3n << 36n) - 1n,
	-((1n << 60n) + (1n << 36n) + 1n), 0x7FFFFFC000000000n, 0x7FFFFFBFFFFFFFFFn, 0x7FFFFFE000000000n, (1n << 55n) + (1n << 31n) + 1n,
	(1n << 54n) + 1n, -((1n << 54n) + 3n),
].map(c);
const F32U: L[] = [
	0xFFFFFF8000000000n, 0xFFFFFF7FFFFFFFFFn, 0xFFFFFF8000000001n, 0xFFFFFFFFFFFFFFFFn, 0x8000008000000000n, 0x8000008000000001n,
	0x8000018000000000n, (1n << 63n) + (1n << 39n) + 1n,
].map(c);
const Ds = [
	0.0, -0.0, 0.5, -0.5, 1.5, -1.5, 2.5, 1e10, -1e10, 2147483647.0, 2147483647.9, 2147483648.0, -2147483648.0,
	-2147483648.9, -2147483649.0, 4294967295.0, 4294967296.0, 9223372036854775808.0, -9223372036854775808.0,
	9223372036854774784.0, 18446744073709551616.0, 18446744073709549568.0, 1e19, 1e20, -1e19, NaN, NaN,
	Infinity, -Infinity, 5e-324, 123.456, -123.456, 127.9, 128.0, -128.5, 255.5, 256.0,
	65535.5, 32767.9, -32768.9, 4503599627370495.5, 9007199254740994.0, 1.7976931348623157e308, -1.0, -3e9, 3e9,
	-4294967296.5, 9007199254740991.0, -9007199254740991.0, 9007199254740992.0, -9007199254740992.0,
];

const X = cpp.L;
for (const a of Ls) for (const b of Ls) {
	const p = a + " " + b + " ";
	P("addL " + p + sl(X.addL(a, b)));
	P("subL " + p + sl(X.subL(a, b)));
	P("mulL " + p + sl(X.mulL(a, b)));
	P("divL " + p + T(() => sl(X.divL(a, b))));
	P("remL " + p + T(() => sl(X.remL(a, b))));
	P("andL " + p + sl(X.andL(a, b)));
	P("orL " + p + sl(X.orL(a, b)));
	P("xorL " + p + sl(X.xorL(a, b)));
}
for (const a of Ls) for (const n of Sh) {
	const p = a + " " + n + " ";
	P("shlL " + p + sl(X.shlL(a, n)));
	P("shrL " + p + sl(X.shrL(a, n)));
	P("ushrL " + p + sl(X.ushrL(a, n)));
}
for (const a of Ls) {
	P("negL " + a + " " + sl(X.negL(a)));
	P("notL " + a + " " + sl(X.notL(a)));
	P("incL " + a + " " + sl(X.incL(a)));
	P("decL " + a + " " + sl(X.decL(a)));
	P("toI32 " + a + " " + si(X.toI32(a), -2147483648, 2147483647));
	P("toU32 " + a + " " + si(X.toU32(a), 0, 4294967295));
	P("toI16 " + a + " " + si(X.toI16(a), -32768, 32767));
	P("toU16 " + a + " " + si(X.toU16(a), 0, 65535));
	P("toI8 " + a + " " + si(X.toI8(a), -128, 127));
	P("toU8 " + a + " " + si(X.toU8(a), 0, 255));
	P("toU64 " + a + " " + su(X.toU64(a)));
	P("toF64 " + a + " " + D(X.toF64(a)));
	P("toF32 " + a + " " + F(X.toF32(a)));
}
for (const a of F32L) P("toF32 " + a + " " + F(X.toF32(a)));
for (const a of Us) for (const b of Us) {
	const p = a + " " + b + " ";
	P("addUL " + p + su(X.addUL(a, b)));
	P("subUL " + p + su(X.subUL(a, b)));
	P("mulUL " + p + su(X.mulUL(a, b)));
	P("divUL " + p + T(() => su(X.divUL(a, b))));
	P("remUL " + p + T(() => su(X.remUL(a, b))));
	P("andUL " + p + su(X.andUL(a, b)));
	P("orUL " + p + su(X.orUL(a, b)));
	P("xorUL " + p + su(X.xorUL(a, b)));
}
for (const a of Us) for (const n of Sh) {
	const p = a + " " + n + " ";
	P("shlUL " + p + su(X.shlUL(a, n)));
	P("shrUL " + p + su(X.shrUL(a, n)));
}
for (const a of Us) {
	P("negUL " + a + " " + su(X.negUL(a)));
	P("notUL " + a + " " + su(X.notUL(a)));
	P("incUL " + a + " " + su(X.incUL(a)));
	P("decUL " + a + " " + su(X.decUL(a)));
	P("toI32 " + a + " " + si(X.toI32(a), -2147483648, 2147483647));
	P("toU32 " + a + " " + si(X.toU32(a), 0, 4294967295));
	P("toI16 " + a + " " + si(X.toI16(a), -32768, 32767));
	P("toU16 " + a + " " + si(X.toU16(a), 0, 65535));
	P("toI8 " + a + " " + si(X.toI8(a), -128, 127));
	P("toU8 " + a + " " + si(X.toU8(a), 0, 255));
	P("toI64 " + a + " " + sl(X.toI64(a)));
	P("toF64 " + a + " " + D(X.toF64(a)));
	P("toF32 " + a + " " + F(X.toF32(a)));
}
for (const a of F32U) P("toF32 " + a + " " + F(X.toF32(a)));
for (const a of Is) {
	P("toU64 " + a + " " + su(X.toU64(a)));
	for (const b of Is) {
		const p = a + " " + b + " ";
		P("divI " + p + T(() => si(X.divI(a, b), -2147483648, 2147483647)));
		P("remI " + p + T(() => si(X.remI(a, b), -2147483648, 2147483647)));
	}
}
for (const a of Ws) for (const b of Ws) {
	const p = a + " " + b + " ";
	P("divU " + p + T(() => si(X.divU(a, b), 0, 4294967295)));
	P("remU " + p + T(() => si(X.remU(a, b), 0, 4294967295)));
}
const C = cpp.Conv;
for (const x of Ds) {
	const p = D(x) + " ";
	P("D2I " + p + si(C.D2I(x), -2147483648, 2147483647));
	P("D2L " + p + sl(C.D2L(x)));
	P("D2U " + p + si(C.D2U(x), 0, 4294967295));
	P("D2UL " + p + su(C.D2UL(x)));
	P("D2S " + p + si(C.D2S(x), -32768, 32767));
	P("D2US " + p + si(C.D2US(x), 0, 65535));
	P("D2SB " + p + si(C.D2SB(x), -128, 127));
	P("D2B " + p + si(C.D2B(x), 0, 255));
	// the C# R.D2I / R.D2L family has Conv's semantics: part C's R can return these
	P("RD2I " + p + si(C.D2I(x), -2147483648, 2147483647));
	P("RD2L " + p + sl(C.D2L(x)));
}
// ix: an index as a number (no C# counterpart prints it; checked here)
for (const a of [...Ls, ...Us]) if (X.ix(a) !== Number(a)) P("ix! " + a);
process.stdout.write(out.join("\n") + "\n");
