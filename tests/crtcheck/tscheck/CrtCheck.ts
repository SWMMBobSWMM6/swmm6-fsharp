// CrtCheck.ts - the TypeScript side of tests/crtcheck (as gocheck/ and javacheck/ are the Go and Java sides):
// node CrtCheck.js libm N | printf | scanf | misc | units, reading what the C program of the same name wrote on stdin,
// and reporting where the TypeScript runtime (package cpp, ts/src/cpp) differs from glibc. The case lines and their
// checks are gocheck's and javacheck's, one for one; the libm table also gives a histogram of the ulp distances, and
// with HIST=1 the first large miss.
// Written by Claude (Anthropic) for the SWMM6 ports; see AI-USE.md.
import * as fs from 'node:fs';
import * as os from 'node:os';
import * as path from 'node:path';
import * as cpp from '../src/cpp/index.js';

const R = cpp.R;
type L = number | bigint;

// ------------------------------------------------------------------------------------------------ input
/** Lines of stdin, read in chunks (the C programs write megabytes). */
function* stdinLines(): Generator<string> {
	const buf = Buffer.alloc(1 << 20);
	let rest = '';
	for (;;) {
		let n: number;
		try { n = fs.readSync(0, buf, 0, buf.length, null); }
		catch (e) { if (e.code === 'EAGAIN') continue; if (e.code === 'EOF') n = 0; else throw e; }
		if (n === 0) break;
		const s = rest + buf.toString('latin1', 0, n);
		const parts = s.split('\n');
		rest = parts.pop();
		for (const p of parts) yield p;
	}
	if (rest.length > 0) yield rest;
}
/** Exactly n bytes of stdin (null at the end). */
const inBuf = Buffer.alloc(1 << 20);
let inLen = 0, inPos = 0;
function readBytes(n: number): Buffer {
	if (inLen - inPos < n) {
		inBuf.copy(inBuf, 0, inPos, inLen);
		inLen -= inPos; inPos = 0;
		while (inLen < n) {
			let r: number;
			try { r = fs.readSync(0, inBuf, inLen, inBuf.length - inLen, null); }
			catch (e) { if (e.code === 'EAGAIN') continue; if (e.code === 'EOF') r = 0; else throw e; }
			if (r === 0) return null;
			inLen += r;
		}
	}
	const b = inBuf.subarray(inPos, inPos + n);
	inPos += n;
	return b;
}

function unhex(s: string): Uint8Array {
	if (s === '-') return new Uint8Array(0);
	const b = new Uint8Array(s.length / 2);
	for (let i = 0; i < b.length; i++) b[i] = parseInt(s.substr(2 * i, 2), 16);
	return b;
}
function hex(b: ArrayLike<number>, off: number, n: number): string {
	let s = '';
	for (let i = 0; i < n; i++) s += ((b[off + i] & 255) < 16 ? '0' : '') + (b[off + i] & 255).toString(16);
	return s;
}
/** A C string holding these bytes. */
function lit(b: Uint8Array): cpp.CPtr { const c = new Uint8Array(b.length + 1); c.set(b); return new cpp.CPtr(c, 0); }
function latin1(b: Uint8Array): string { let s = ''; for (const c of b) s += String.fromCharCode(c); return s; }

const F64 = new Float64Array(1), U64 = new BigUint64Array(F64.buffer);
const F32 = new Float32Array(1), U32 = new Uint32Array(F32.buffer);
function dbits(x: number): string { F64[0] = x; return U64[0].toString(16).padStart(16, '0'); }
function fbits(x: number): string { F32[0] = x; return U32[0].toString(16).padStart(8, '0'); }
function bitsd(h: string): number { U64[0] = BigInt('0x' + h); return F64[0]; }

type Case = (f: string[]) => [string, string];   // [glibc, TypeScript]
function lines(what: string, check: Case): number {
	let n = 0, bad = 0;
	for (const line of stdinLines()) {
		const f = line.split('\t');
		n++;
		const [w, g] = check(f);
		if (w !== g) {
			bad++;
			if (bad <= 15) {
				let label = f[0];
				try { label = latin1(unhex(f[0])); } catch (e) { }
				console.log(`MISMATCH "${label}"\n  glibc: ${w}\n  TS:    ${g}`);
			}
		}
	}
	console.log(`${what} cases: ${n}, mismatches: ${bad}`);
	return bad;
}

// ------------------------------------------------------------------------------------------------ printf
// hex(format) types args size ret hex(output)
function printfCase(f: string[]): [string, string] {
	const format = lit(unhex(f[0]));
	const vals = f[2].split(',');
	const args: any[] = [];
	for (let i = 0; i < f[1].length; i++) {
		const v = vals[i];
		switch (f[1][i]) {
			case 'd': args.push(bitsd(v)); break;
			case 'i': args.push(parseInt(v, 16) | 0); break;
			case 'l': { const b = BigInt.asIntN(64, BigInt('0x' + v)); args.push(b >= -9007199254740991n && b <= 9007199254740991n ? Number(b) : b); break; }
			case 's': args.push(lit(unhex(v))); break;
			case 'S': args.push(cpp.CPtr.ZERO); break;
		}
	}
	const size = parseInt(f[3], 10);
	const buf = R.Buf_i(1024);
	(buf.B as Uint8Array).fill(0x55);
	(buf.B as Uint8Array)[0] = 0;
	const r = R.snprintf_CPtr_ul_CPtr_oA(buf, size, format, ...args);
	let out = '-';
	if (size > 0) {
		let n = 0;
		while ((buf.B as Uint8Array)[n] !== 0) n++;
		if (n > 0) out = hex(buf.B, 0, n);
	}
	return [f[4] + '\t' + f[5], r + '\t' + out];
}

// ------------------------------------------------------------------------------------------------ scanf
// hex(format) hex(input) types ret values; the targets are the kinds the engine passes
function scanfCase(f: string[]): [string, string] {
	const format = lit(unhex(f[0])), input = lit(unhex(f[1]));
	const types = f[2];
	const m = types.length;
	const ints: number[] = [], longs: L[] = [], floats: number[] = [], doubles: number[] = [];
	const bufs: cpp.CPtr[] = [];
	const args: any[] = [];
	for (let k = 0; k < m; k++) {
		ints[k] = 0x55555555;
		longs[k] = 6148914691236517205n;
		U32[0] = 0x55555555; floats[k] = F32[0];
		doubles[k] = bitsd('5555555555555555');
		switch (types[k]) {
			case 'i': case 'n': args.push(R.RefI((v) => { ints[k] = v; })); break;
			case 'u': args.push(R.RefL((v) => { ints[k] = typeof v === 'number' ? v | 0 : Number(BigInt.asIntN(32, v)); })); break;   // a long setter, kept as unsigned int
			case 'l': args.push(R.RefL((v) => { longs[k] = v; })); break;
			case 'f': args.push(R.RefF((v) => { floats[k] = v; })); break;
			case 'd': args.push(R.RefD((v) => { doubles[k] = v; })); break;
			case 'k': ints[k] = 0x55; args.push(R.RefI((v) => { ints[k] = (v << 24) >> 24; })); break;   // a char
			case 's': case 'c':
				bufs[k] = R.Buf_i(64);
				(bufs[k].B as Uint8Array).fill(0x55);
				args.push(bufs[k]);
				break;
		}
	}
	const r = R.sscanf(input, format, ...args);
	let sb = '';
	for (let k = 0; k < m; k++) {
		switch (types[k]) {
			case 'i': case 'n': case 'u': case 'k': sb += ints[k]; break;
			case 'l': sb += String(longs[k]); break;
			case 'f': sb += fbits(floats[k]); break;
			case 'd': sb += dbits(doubles[k]); break;
			case 's': case 'c': sb += hex(bufs[k].B, 0, 12); break;
		}
		sb += ' ';
	}
	const want = f[3] + '\t' + (f.length > 4 ? f[4] : '');
	return [want, r + '\t' + sb];
}

// ------------------------------------------------------------------------------------------------ misc
// kind hex(input) result
function miscCase(f: string[]): [string, string] {
	const inp = unhex(f[1]);
	const s = lit(inp);
	const e = cpp.CPtr.op_Add_CPtr_i(s, inp.length);
	let got: string;
	switch (f[0]) {
		case 'fci': { const v = new cpp.Cell<number>(0x55555555); const r = cpp.Std.from_chars_CPtr_CPtr_ir(s, e, v); got = r.ec + ' ' + cpp.CPtr.Diff(r.ptr, s) + ' ' + v.v; break; }
		case 'fcu': { const v = new cpp.Cell<number>(0x55555555); const r = cpp.Std.from_chars_CPtr_CPtr_ur(s, e, v); got = r.ec + ' ' + cpp.CPtr.Diff(r.ptr, s) + ' ' + v.v; break; }
		case 'fcl': { const v = new cpp.Cell<L>(6148914691236517205n); const r = cpp.Std.from_chars_CPtr_CPtr_lr(s, e, v); got = r.ec + ' ' + cpp.CPtr.Diff(r.ptr, s) + ' ' + String(v.v); break; }
		case 'fcd': { const v = new cpp.Cell<number>(0.25); const r = cpp.Std.from_chars_CPtr_CPtr_dr(s, e, v); got = r.ec + ' ' + cpp.CPtr.Diff(r.ptr, s) + ' ' + dbits(v.v); break; }
		case 'strtol': { const end = new cpp.Cell<cpp.CPtr>(s); const v = R.strtol(s, end, 10); got = String(v) + ' ' + cpp.CPtr.Diff(end.v, s); break; }
		case 'atoi': got = String(R.atoi(s)); break;
		case 'atol': got = String(R.atol(s)); break;
		case 'strtod': { const end = new cpp.Cell<cpp.CPtr>(s); const v = R.strtod_CPtr_CPtrr(s, end); got = dbits(v) + ' ' + cpp.CPtr.Diff(end.v, s); break; }
		case 'ctime': {
			const t = parseInt(latin1(inp), 10);
			const p = R.ctime_Ptrl(cpp.Ptr.new_TA_l<L>([t], 0));
			got = hex(p.B, p.O, R.strlen(p));
			break;
		}
		case 'nprocs': got = String(cpp.Sys.hardware_concurrency()); break;
		case 'ctype': {
			const c = parseInt(latin1(inp), 10);
			got = '' + R.isdigit(c) + R.isalpha(c) + R.isalnum(c) + R.isspace(c) + R.isupper(c) + R.islower(c) + R.ispunct(c)
				+ R.isprint(c) + R.isxdigit(c) + ' ' + R.toupper(c) + ' ' + R.tolower(c);
			break;
		}
		default: throw new Error(f[0]);
	}
	if (got !== f[2]) got = got + '  (' + f[0] + ' "' + latin1(inp) + '")';
	return [f[2], got];
}

// ------------------------------------------------------------------------------------------------ libm
const FN = ['atan2', 'cbrt', 'cosh', 'sinh', 'tanh', 'log2', 'expm1', 'log1p', 'hypot', 'fmod', 'remainder', 'round',
	'nextafter', 'exp', 'log', 'pow', 'sin', 'cos', 'tan', 'asin', 'acos', 'atan', 'log10', 'floor', 'ceil', 'trunc', 'sqrt'];
const M = cpp.SysMath, LM = cpp.LibM;
const FF: ((x: number, y: number) => number)[] = [
	(x, y) => M.Atan2_d_d(x, y), (x) => M.Cbrt_d(x), (x) => M.Cosh_d(x), (x) => M.Sinh_d(x), (x) => M.Tanh_d(x),
	(x) => M.Log2_d(x), (x) => LM.expm1(x), (x) => LM.log1p(x), (x, y) => LM.hypot(x, y), (x, y) => LM.fmod(x, y),
	(x, y) => LM.remainder(x, y), (x) => LM.round(x), (x, y) => LM.nextafter(x, y), (x) => M.Exp_d(x), (x) => M.Log_d(x),
	(x, y) => M.Pow_d_d(x, y), (x) => M.Sin_d(x), (x) => M.Cos_d(x), (x) => M.Tan_d(x), (x) => M.Asin_d(x),
	(x) => M.Acos_d(x), (x) => M.Atan_d(x), (x) => M.Log10_d(x), (x) => M.Floor_d(x), (x) => M.Ceiling_d(x),
	(x) => M.Truncate_d(x), (x) => M.Sqrt_d(x)];
const NSPEC = 24;
const B8 = new Float64Array(1), I8 = new BigInt64Array(B8.buffer);
function sbits(x: number): bigint { B8[0] = x; return I8[0]; }
/** The distance between two doubles in units in the last place (across zero too). */
function ulps(a: number, b: number): bigint {
	let ia = sbits(a), ib = sbits(b);
	if (ia < 0n) ia = -9223372036854775808n - ia;
	if (ib < 0n) ib = -9223372036854775808n - ib;
	return ia > ib ? ia - ib : ib - ia;
}
function libm(n: number): number {
	const per = n + NSPEC * NSPEC;
	console.log(`${'function'.padEnd(10)} ${'cases'.padStart(8)} ${'mismatch'.padStart(10)} ${'NaN-sign'.padStart(9)} ${'max ulp'.padStart(8)}  ${'ulp 1 / 2 / 3-10 / >10 / NaN-vs-number'.padEnd(26)}  first mismatch (x, y: glibc vs TS)`);
	let total = 0;
	const showBig = process.env.HIST !== undefined;
	for (let fi = 0; fi < FN.length; fi++) {
		let bad = 0, nanSign = 0, maxU = 0n, nanNum = false;
		const h = [0, 0, 0, 0, 0];
		let first = '', big = '';
		const fn = FF[fi];
		for (let i = 0; i < per; i++) {
			const b = readBytes(24);
			if (b === null) { console.error('short input'); process.exit(2); }
			const x = b.readDoubleLE(0), y = b.readDoubleLE(8);
			const want = b.readBigInt64LE(16);
			const r = fn(x, y);
			const got = sbits(r);
			if (got === want) continue;
			bad++;
			B8[0] = 0; I8[0] = want;
			const w = B8[0];
			const desc = `${x}, ${y}: ${w} vs ${r}`;
			if (w !== w && r !== r) nanSign++;
			else if (w === w && r === r) {
				const d = ulps(w, r);
				if (d > maxU) maxU = d;
				if (d === 1n) h[0]++; else if (d === 2n) h[1]++; else if (d <= 10n) h[2]++;
				else { h[3]++; if (big === '') big = desc; }
			} else { nanNum = true; h[4]++; if (big === '') big = desc; }
			if (first === '') first = desc;
		}
		total += bad;
		console.log(`${FN[fi].padEnd(10)} ${String(per).padStart(8)} ${String(bad).padStart(10)} ${String(nanSign).padStart(9)} ${(nanNum ? 'NaN/num' : String(maxU)).padStart(8)}  ${(bad ? h.join(' / ') : '').padEnd(26)}  ${first}`);
		if (bad > 0 && showBig && big !== '') console.log(`           first large: ${big}`);
	}
	console.log(`total mismatches: ${total}`);
	return total;
}

// ------------------------------------------------------------------------------------------------ units
let un = 0, ubad = 0;
function check(name: string, ok: boolean, ...detail: any[]): void {
	un++;
	if (!ok) { ubad++; console.log('FAIL ' + name + ' ' + JSON.stringify(detail, (k, v) => (typeof v === 'bigint' ? v.toString() + 'n' : v))); }
}
function u8(p: cpp.CPtr): Uint8Array { return p.B as Uint8Array; }

function units(): number {
	// sscanf into pointer parameters: the stash and R.Back, as the engine's parsers use them
	{
		const v = new cpp.Cell<L>(-9), flag = new cpp.Cell<number>(113);
		let r = R.Back_i_T1r_T2r__l_c(R.sscanf(R.Lit('123 Y'), R.Lit('%ld %c'), R.Slot, R.Slot), v, flag);
		check('slot %ld %c', r === 2 && v.v === 123 && flag.v === 89, r, v.v, flag.v);
		v.v = -9; flag.v = 113;
		r = R.Back_i_T1r_T2r__l_c(R.sscanf(R.Lit('77'), R.Lit('%ld %c'), R.Slot, R.Slot), v, flag);
		check('slot partial', r === 1 && v.v === 77 && flag.v === 113, r, v.v, flag.v);
		let x = 99.98999786376953;
		r = R.Back_i_T1r__c(R.sscanf(R.Lit('1.5 X'), R.Lit('%f %c'), R.RefF((s) => { x = s; }), R.Slot), flag);
		check('RefF + slot', r === 2 && x === 1.5 && flag.v === 88, r, x, flag.v);
		r = R.Back_i_T1r__c(R.sscanf(R.Lit('1.5 é'), R.Lit('%f %c'), R.RefF((s) => { x = s; }), R.Slot), flag);
		check('slot signed char', r === 2 && flag.v === -23, r, flag.v);
		const h = new cpp.Cell<number>(0);
		r = R.Back_i_T1r__s(R.sscanf(R.Lit('-12'), R.Lit('%d'), R.Slot), h);
		check('slot converted', r === 1 && h.v === -12, r, h.v);
		let thrown = '';
		try { R.Back_i_T1r__s(R.sscanf(R.Lit('70000'), R.Lit('%d'), R.Slot), h); } catch (e) { thrown = e.kind; }
		check('slot overflow is checked (Convert.ChangeType)', thrown === 'OverflowException', thrown);
		const d = new cpp.Cell<number>(0);
		r = R.Back_i_T1r__d(R.sscanf(R.Lit('2.5'), R.Lit('%lf'), R.Slot), d);
		check('slot double', r === 1 && d.v === 2.5, r, d.v);
		const big = new cpp.Cell<L>(0);
		r = R.Back_i_T1r__l(R.sscanf(R.Lit('9007199254740993'), R.Lit('%ld'), R.Slot), big);
		check('slot long beyond 2^53', r === 1 && big.v === 9007199254740993n, r, big.v);
		const ub = new cpp.Cell<L>(0);
		r = R.Back_i_T1r__ul(R.sscanf(R.Lit('18446744073709551615'), R.Lit('%lu'), R.Slot), ub);
		check('slot ulong max', r === 1 && ub.v === 18446744073709551615n, r, ub.v);
		const s1 = R.Buf_i(80);
		r = R.sscanf(R.Lit('  SWMM5 x'), R.Lit('%s'), s1);
		check('%s CPtr', r === 1 && R.strcmp(s1, R.Lit('SWMM5')) === 0, r);
		const ci = new cpp.Cell<number>(0), cd = new cpp.Cell<number>(0);
		r = R.sscanf(R.Lit('7 0.5'), R.Lit('%d %lf'), ci, cd);
		check('Ref cells as targets', r === 2 && ci.v === 7 && cd.v === 0.5, r, ci.v, cd.v);
		const arr = new Array(16).fill(0);
		r = R.sscanf(R.Lit('abc'), R.Lit('%s'), cpp.Ptr.new_TA_l(arr, 2));
		check('%s into a char array pointer', r === 1 && arr[2] === 97 && arr[4] === 99 && arr[5] === 0, r, arr);
		let lv: L = 0;
		r = R.sscanf(R.Lit('-9223372036854775809 x'), R.Lit('%ld'), R.RefL((q) => { lv = q; }));
		check('%ld clamps to LONG_MIN', r === 1 && (lv as L) === -9223372036854775808n, r, lv);
	}

	// strtod details: hex floats, NaN payloads, the end pointer
	{
		const end = new cpp.Cell<cpp.CPtr>(cpp.CPtr.ZERO);
		let s = R.Lit('  0x1.8p1rest');
		check('strtod hex', R.strtod_CPtr_CPtrr(s, end) === 3.0 && cpp.CPtr.Diff(end.v, s) === 9);
		check('strtod nan payload', dbits(R.strtod_CPtr(R.Lit('nan(0x5)'))) === '7ff8000000000005');
		check('strtod -nan', dbits(R.strtod_CPtr(R.Lit('-nan'))) === 'fff8000000000000');
		s = R.Lit('abc');
		check('strtod no number', R.strtod_CPtr_CPtrr(s, end) === 0 && cpp.CPtr.op_Eq(end.v, s));
		check('strtod huge exponent', R.strtod_CPtr(R.Lit('1e99999999999')) === Infinity && R.strtod_CPtr(R.Lit('1e-99999999999')) === 0);
		check('strtod hex subnormal', R.strtod_CPtr(R.Lit('0x1p-1074')) === 5e-324 && R.strtod_CPtr(R.Lit('0x1.8p-1074')) === 1e-323
			&& R.strtod_CPtr(R.Lit('0x1p-1075')) === 0 && R.strtod_CPtr(R.Lit('0x1.0000000000001p-1075')) === 5e-324);
		check('strtod hex rounding', R.strtod_CPtr(R.Lit('0x1.00000000000008p0')) === 1 && R.strtod_CPtr(R.Lit('0x1.00000000000018p0')) === 1.0000000000000004
			&& R.strtod_CPtr(R.Lit('0x1.fffffffffffff8p1023')) === Infinity);
		let fv = 0;
		R.sscanf(R.Lit('16777217'), R.Lit('%f'), R.RefF((q) => { fv = q; }));
		check('strtof of an odd integer', fv === 16777216, fv);
		R.sscanf(R.Lit('1.00000005960464477539062500000000001'), R.Lit('%f'), R.RefF((q) => { fv = q; }));
		check('strtof just above a float midpoint', fv === 1.0000001192092896, fv);
		R.sscanf(R.Lit('1.000000059604644775390625'), R.Lit('%f'), R.RefF((q) => { fv = q; }));
		check('strtof at a float midpoint (ties to even)', fv === 1, fv);
		R.sscanf(R.Lit('3.40282356779733661637539395458142568448e38'), R.Lit('%f'), R.RefF((q) => { fv = q; }));
		check('strtof at the overflow midpoint', fv === Infinity, fv);
	}

	// printf details the gate's formats depend on
	{
		const b = R.Buf_i(256);
		const fmt = (f: string, ...a: any[]): string => { R.snprintf_CPtr_ul_CPtr_oA(b, 256, R.Lit(f), ...a); return R.Str(b); };
		check('%llu of a ulong beyond 2^53', fmt('%llu', 18446744073709551615n) === '18446744073709551615', fmt('%llu', 18446744073709551615n));
		check('%016llx', fmt('%016llx', 81985529216486895n) === '0123456789abcdef');
		check('%zu of a number', fmt('%zu', 12345) === '12345');
		check('%ld of a negative bigint', fmt('%ld', -9223372036854775808n) === '-9223372036854775808');
		check('%d of a uint pattern', fmt('%d', 4294967295) === '-1');
		check('%u of an int', fmt('%u', -1) === '4294967295');
		check('%c of a char code', fmt('[%c]', 65) === '[A]');
		check('%s of a Str', fmt('%s|', cpp.Str.new_CPtr(R.Lit('abc'))) === 'abc|');
		check('%s of a null pointer', fmt('%s|%.3s|', cpp.CPtr.ZERO, cpp.CPtr.ZERO) === '(null)||');
		check('%.4f ties to even', fmt('%.4f %.4f %.1f', 0.00005, 1.00015, 0.25) === '0.0001 1.0002 0.2', fmt('%.4f %.4f %.1f', 0.00005, 1.00015, 0.25));
		check('%.0f halves', fmt('%.0f %.0f %.0f %.0f', 0.5, 1.5, 2.5, -2.5) === '0 2 2 -2');
		check('%e ties', fmt('%.0e %.1e %.2e', 2.5, 0.125, 1.125) === '2e+00 1.2e-01 1.12e+00', fmt('%.0e %.1e %.2e', 2.5, 0.125, 1.125));
		check('%#g quirk', fmt('%#g %#.3g', 999999.5, 999.5) === '1.e+06 1.e+03', fmt('%#g %#.3g', 999999.5, 999.5));
		check('%f of 1e300', R.snprintf_CPtr_ul_CPtr_oA(R.Buf_i(400), 400, R.Lit('%.2f'), 1e300) === 304);
		check('%a', fmt('%a %a %.0a %A', 1.0, 0.1, 1.5, -2.0) === '0x1p+0 0x1.999999999999ap-4 0x2p+0 -0X1P+1', fmt('%a %a %.0a %A', 1.0, 0.1, 1.5, -2.0));
		check('-nan', fmt('%f %f', R.f64bits(0xfff80000, 0), R.f64bits(0x7ff80000, 0)) === '-nan nan');
		check('%f of a float value', fmt('%.10f', Math.fround(0.1)) === '0.1000000015');
		check('snprintf truncation', R.snprintf_CPtr_ul_CPtr_oA(b, 4, R.Lit('%d'), 123456) === 6 && R.Str(b) === '123');
		check('Format', R.Str(new cpp.CPtr(Uint8Array.from([...R.Format(R.Lit('x%dy'), [5]), 0]), 0)) === 'x5y');
	}

	// raw memory
	{
		const ul: L[] = [0];
		cpp.Mem.memcpy_PtrT_PtrU_ul__ul_d(cpp.Ptr.new_TA_l(ul, 0), cpp.Ptr.new_TA_l([-2.75], 0), 8);
		check('memcpy double->u64', ul[0] === 0xc006000000000000n, ul[0]);
		const view = cpp.Bytes.View_PtrT__l(cpp.Ptr.new_TA_l<L>([72623859790382856n], 0));
		check('bytes view', u8(view).length === 8 && u8(view)[0] === 8 && u8(view)[7] === 1);
		const f = [0];
		cpp.Mem.memcpy_PtrT_CPtr_ul__d(cpp.Ptr.new_TA_l(f, 0), view, 8);
		check('memcpy CPtr->double', dbits(f[0]) === '0102030405060708');
		const c = cpp.Mem.Calloc__d(5, 8);
		check('calloc', c.a.length === 5 && c.o === 0 && c.a[4] === 0);
		const objs = [{}, {}, {}];
		check('diff', cpp.Mem.Diff_T_PtrT(objs[2], cpp.Ptr.new_TA_l(objs, 1)) === 1);
		const u = [0, 0];
		cpp.Mem.memcpy_PtrT_PtrU_ul__u_uc(cpp.Ptr.new_TA_l(u, 1), cpp.Ptr.new_TA_l([1, 2, 3, 4, 5], 1), 4);
		check('memcpy bytes->u32 offset', u[0] === 0 && u[1] === 0x05040302, u);
		const w = [0x11111111, 0x22222222];
		cpp.Mem.memcpy_PtrT_CPtr_ul__i(cpp.Ptr.new_TA_l(w, 0), new cpp.CPtr(Uint8Array.from([1, 2, 3, 4, 5, 6]), 0), 6);
		check('memcpy partial element', w[0] === 0x04030201 && w[1] === 0x22220605, w[0].toString(16), w[1].toString(16));
		const zv = cpp.Bytes.View_PtrT__b(cpp.Ptr.new_TA_l([true, false, true], 1));
		check('bool view', u8(zv).length === 2 && u8(zv)[0] === 0 && u8(zv)[1] === 1);
		const dv = cpp.Bytes.View_PtrT__d(cpp.Ptr.new_TA_l([1.0, 2.0], 1));
		check('double view offset', u8(dv).length === 8 && u8(dv)[7] === 0x40 && u8(dv)[6] === 0);
		const sh = [0, 0];
		cpp.Mem.memcpy_PtrT_CPtr_ul__s(cpp.Ptr.new_TA_l(sh, 0), new cpp.CPtr(Uint8Array.from([0xff, 0x7f, 0x01, 0x80]), 0), 4);
		check('memcpy shorts', sh[0] === 0x7fff && sh[1] === -32767, sh);
		const sb = [0, 0, 0];
		cpp.Mem.memcpy_PtrT_CPtr_ul__c(cpp.Ptr.new_TA_l(sb, 0), new cpp.CPtr(Uint8Array.from([0xff, 0x80, 0x7f]), 0), 3);
		check('memcpy sbytes', sb[0] === -1 && sb[1] === -128 && sb[2] === 127, sb);
		const bigs: L[] = [0];
		cpp.Mem.memcpy_PtrT_CPtr_ul__ul(cpp.Ptr.new_TA_l(bigs, 0), new cpp.CPtr(Uint8Array.from([1, 0, 0, 0, 0, 0, 0x20, 0]), 0), 8);
		check('memcpy u64 beyond 2^53', bigs[0] === 9007199254740993n, bigs[0]);
		const fl = [0];
		cpp.Mem.memset_PtrT_i_ul__f(cpp.Ptr.new_TA_l(fl, 0), 0xff, 4);
		check('memset float', fl[0] !== fl[0]);
		const field = { x: 1.5 };
		const fp = cpp.Ptr.new_o_RefAtT<number>(field, (o: any) => cpp.fieldRef(o, 'x'));
		cpp.Mem.memcpy_PtrT_CPtr_ul__d(fp, cpp.Bytes.View_PtrT__d(cpp.Ptr.new_TA_l([2.25], 0)), 8);
		check('memcpy into a field pointer', field.x === 2.25, field.x);
		check('bit casts', cpp.Bits.Cast__ul(-0.0) === 9223372036854775808n && cpp.Bits.Cast__d(4607182418800017408n) === 1
			&& cpp.Bits.Cast__u(1.5) === 0x3fc00000 && cpp.Bits.Cast__f(0x3fc00000) === 1.5);
		check('Num wraps', cpp.Num.Add__ul(18446744073709551615n, 1) === 0 && cpp.Num.Add__i(2147483647, 1) === -2147483648
			&& cpp.Num.Sub__u(0, 1) === 4294967295 && cpp.Num.Add__l(9007199254740991, 1) === 9007199254740992n);
	}

	// binary and text files
	{
		const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'crtcheck-'));
		try {
			const name = R.Lit(path.join(dir, 't.bin'));
			let fp = R.fopen(name, R.Lit('w+b'));
			cpp.Mem.fwrite_CPtr_ul_ul_CFile(R.Lit('STAMP'), 1, 5, fp);
			R.fwriteI1(42, fp);
			const pos = R.ftell(fp);
			R.fwriteD1(3.5, fp);
			R.fwriteF1(0.25, fp);
			cpp.Mem.fwrite_PtrT_ul_ul_CFile__i(cpp.Ptr.new_TA_l([7, -8, 9], 0), 4, 3, fp);
			R.fseek_CFile_l_i(fp, pos, 0);
			R.fwriteD1(4.5, fp);                          // overwrite in place
			R.rewind(fp);
			const stamp = R.Buf_i(16);
			const got = cpp.Mem.fread_CPtr_ul_ul_CFile(stamp, 1, 5, fp);
			const i = new cpp.Cell<number>(0), d = new cpp.Cell<number>(0), fl = new cpp.Cell<number>(0);
			const ok = got === 5 && R.strncmp(stamp, R.Lit('STAMP'), 5) === 0 && R.freadI1(i, fp) === 1 && i.v === 42
				&& R.freadD1(d, fp) === 1 && d.v === 4.5 && R.freadF1(fl, fp) === 1 && fl.v === 0.25;
			check('binary round trip', ok, got, i.v, d.v, fl.v);
			const ints = [0, 0, 0, 0];
			const k = cpp.Mem.fread_PtrT_ul_ul_CFile__i(cpp.Ptr.new_TA_l(ints, 0), 4, 4, fp);
			check('short fread', k === 3 && ints[0] === 7 && ints[1] === -8 && ints[2] === 9 && R.feof(fp) === 1, k, ints);
			check('fgetc at end', R.fgetc(fp) === -1);
			check('ftell at end', R.ftell(fp) === 5 + 4 + 8 + 4 + 12);
			check('fseek negative', R.fseek_CFile_l_i(fp, -1, 0) === -1);
			check('fseek end', R.fseek_CFile_l_i(fp, -4, 2) === 0 && R.ftell(fp) === 29 && R.feof(fp) === 0);
			const last = new cpp.Cell<number>(0);
			check('fread after seek', R.freadI1(last, fp) === 1 && last.v === 9);
			check('fclose', R.fclose(fp) === 0);
			fp = R.fopen(name, R.Lit('r'));
			const line = R.Buf_i(8);
			check('fgets', !R.fgets(line, 4, fp).get_IsNull() && R.strcmp(line, R.Lit('STA')) === 0);
			const part = [0x11111111, 0x22222222];
			R.fseek_CFile_l_i(fp, -6, 2);
			const pk = cpp.Mem.fread_PtrT_ul_ul_CFile__i(cpp.Ptr.new_TA_l(part, 0), 4, 2, fp);
			// the last 6 bytes are the top two of -8 and all of 9: ff ff 09 00 | 00 00
			check('fread partial element', pk === 1 && part[0] === 0x0009ffff && part[1] === 0x22220000 && R.feof(fp) === 1, pk, part[0].toString(16), part[1].toString(16));
			R.fclose(fp);
			check('fopen missing', R.fopen(R.Lit(path.join(dir, 'none')), R.Lit('r')) === null);
			{
				// glibc opens a directory for reading and the first read fails with EISDIR: the error indicator is set,
				// end-of-file is not; any mode that writes refuses it (lessons/24)
				const d = R.fopen(R.Lit(dir), R.Lit('r'));
				const lb = R.Buf_i(8);
				check('fopen directory', d !== null && R.fgets(lb, 8, d).get_IsNull() && R.feof(d) === 0 && R.ferror(d) !== 0
					&& R.fopen(R.Lit(dir), R.Lit('w')) === null && R.fopen(R.Lit(dir), R.Lit('r+')) === null);
				if (d !== null) R.fclose(d);
			}
			check('fopen bad mode', R.fopen(name, R.Lit('x')) === null);
			// append mode: writes go at the end, no seeking before the old end
			const an = R.Lit(path.join(dir, 'a.txt'));
			fp = R.fopen(an, R.Lit('w'));
			R.fprintf(fp, R.Lit('one\n'));
			R.fclose(fp);
			fp = R.fopen(an, R.Lit('a'));
			check('append position', R.ftell(fp) === 4);
			check('append no seek back', R.fseek_CFile_l_i(fp, 0, 0) === -1);
			R.fprintf(fp, R.Lit('two\n'));
			R.fclose(fp);
			check('append content', fs.readFileSync(path.join(dir, 'a.txt'), 'latin1') === 'one\ntwo\n');
			// a buffered file is not on disk until flushed (as C#'s BufferedStream); FlushAll writes it
			const tn = path.join(dir, 't.txt');
			fp = R.fopen(R.Lit(tn), R.Lit('w'));
			R.fprintf(fp, R.Lit('%-6s|%3d|%5.2f|%c|%zu|%s|%d|%hu|%u|%x\n'), R.Lit('ab'), -5, Math.fround(1.25), 90, 18446744073709551615n,
				cpp.Str.new_CPtr(R.Lit('str')), true, 65535, -1, -1);
			check('buffered until flushed', fs.readFileSync(tn).length === 0);
			R.FlushAll();
			const txt = fs.readFileSync(tn, 'latin1');
			check('fprintf argument kinds', txt === 'ab    | -5| 1.25|Z|18446744073709551615|str|1|65535|4294967295|ffffffff\n', txt);
			R.fclose(fp);
			check('fprintf null file', R.fprintf(null, R.Lit('x')) === -1);
			const big = new Uint8Array(200000).fill(65);
			fp = R.fopen(R.Lit(tn), R.Lit('w+'));
			R.fwriteC(new cpp.CPtr(big, 0), 0, 100, fp);
			R.fwriteC(new cpp.CPtr(big, 0), 0, 200000, fp);
			R.fseek_CFile_l_i(fp, 50, 0);
			const g1 = R.Buf_i(8);
			check('large write then read back', cpp.Mem.fread_CPtr_ul_ul_CFile(g1, 1, 4, fp) === 4 && R.Str(g1) === 'AAAA' && R.ftell(fp) === 54);
			R.fclose(fp);
			check('large write size', fs.statSync(tn).size === 200100);
			check('remove', R.remove(an) === 0 && R.remove(an) === -1);
			check('File.Exists', R.File_Exists(tn) && !R.File_Exists(dir) && !R.File_Exists(''));
			const tpl = R.Lit(path.join(dir, 'tmpXXXXXX'));
			check('mkstemp', R.mkstemp(tpl) === 3 && fs.existsSync(R.Str(tpl)) && !R.Str(tpl).endsWith('XXXXXX'));
		} finally {
			fs.rmSync(dir, { recursive: true, force: true });
		}
	}

	// tuples, atomics, variants, once, std helpers
	{
		const t1 = [1, 2], t2 = [1, 2];
		const a = cpp.TupN.new_oA([1, 2.5, cpp.Ptr.new_TA_l(t1, 0), true, 3]);
		const b = cpp.TupN.new_oA(1, 2.5, cpp.Ptr.new_TA_l(t1, 0), true, 3);
		const c = cpp.TupN.new_oA([1, 2.5, cpp.Ptr.new_TA_l(t2, 0), true, 3]);
		const d = cpp.TupN.new_oA([1, 2.5, cpp.Ptr.new_TA_l(t1, 0), true, 4]);
		check('tuple equal', a.CompareTo(b) === 0 && a.Equals(b));
		check('tuple identity', a.CompareTo(c) !== 0 && a.CompareTo(c) === -c.CompareTo(a));
		check('tuple enum', a.CompareTo(d) < 0 && d.CompareTo(a) > 0);
		const m = new cpp.Atomic<L>(() => 0);
		check('fetch_add', m.fetch_add__l(5, 0) === 0 && m.fetch_add__l(2, 0) === 5 && m.v === 7);
		const ul = cpp.Atomic.new_T<L>(18446744073709551615n, () => 0);
		ul.fetch_add__ul(1, 0);
		check('atomic ulong wraps', ul.v === 0, ul.v);
		const fa = cpp.Atomic.new_T<number>(1.5, () => 0);
		const old = new cpp.Cell<number>(1.0);
		const ok1 = fa.compare_exchange(old, 3.0), ok2 = fa.compare_exchange(old, 3.0);
		check('compare_exchange', !ok1 && old.v === 1.5 && ok2 && fa.v === 3.0);
		const na = cpp.Atomic.new_T<number>(NaN, () => 0);
		check('compare_exchange NaN equals NaN', na.compare_exchange(new cpp.Cell<number>(NaN), 1) && na.v === 1);
		const v = new cpp.Variant();
		check('variant default', v.v === false && v.idx === 0);
		const s = cpp.Str.new_CPtr(R.Lit('abc'));
		v.Set_Str(s);
		s.append_CPtr(R.Lit('d'));
		const w = v.Clone();
		check('variant string', v.idx === 3 && v.v.toString() === 'abc' && w.v !== v.v && w.v.toString() === 'abc');
		v.Set_d(2.5);
		check('variant double', v.idx === 2 && v.v === 2.5 && v.index() === 2);
		const once = new cpp.OnceFlag();
		let calls = 0;
		cpp.Std.call_once(once, () => { calls++; });
		cpp.Std.call_once(once, () => { calls++; });
		check('call_once', calls === 1);
		const x = new cpp.Cell<number>(1), y = new cpp.Cell<number>(2);
		cpp.Std.swap(x, y);
		check('swap', x.v === 2 && y.v === 1);
		const t0 = cpp.TimePoint.now();
		check('duration', cpp.TimePoint.op_Sub(cpp.TimePoint.now(), t0).Clone().count() >= 0 && cpp.TimePoint.ZERO.ns === 0);
		check('qnan', dbits(cpp.Std.QNaN) === '7ff8000000000000');
		check('nop', cpp.Std.Nop(3) === 0);
		const pr = cpp.Pair.new_A_B(4, 0.5);
		check('get0/get1', cpp.Std.get0_PairAB(pr) === 4 && cpp.Std.get1_PairAB(pr) === 0.5);
		let thrown = false;
		try { cpp.Std.Rethrow(new RangeError('x')); } catch (e) { thrown = e instanceof RangeError; }
		check('Rethrow', thrown);
		thrown = false;
		try { R.thr(new TypeError('y')); } catch (e) { thrown = e instanceof TypeError; }
		check('thr', thrown);
		const ec = new cpp.ErrorCode();
		ec.v = 2;
		check('error_code', ec.Bool() && ec.message().toString() === 'No such file or directory');
		const se: any = new cpp.SysException('NotImplementedException', 'm');
		check('SysException', se.message === 'm' && se.kind === 'NotImplementedException' && !(se instanceof cpp.CppException) && se instanceof Error);
		const fr = cpp.FromCharsResult.ZERO;
		check('FromCharsResult.ZERO', fr.ec === 0 && fr.ptr.get_IsNull());
		check('Raw.V', cpp.Raw.V !== null && cpp.Raw.V === cpp.Raw.V);
		check('Ident', cpp.Ident.Of(t1) === cpp.Ident.Of(t1) && cpp.Ident.Of(t1) !== cpp.Ident.Of(t2) && cpp.Ident.Of(null) === 0);
		check('hardware_concurrency', cpp.Sys.hardware_concurrency() >= 1);
		check('Thread', !new cpp.Thread().joinable());
	}

	// conversions and math corners
	{
		check('R.D2I', R.D2I(NaN) === -2147483648 && R.D2I(2147483647.9) === 2147483647 && R.D2I(-3.7) === -3 && R.D2I(2147483648) === -2147483648);
		check('R.D2L', R.D2L(9.3e18) === -9223372036854775808n && R.D2L(-9.2e18) === -9200000000000000000n && R.D2L(NaN) === -9223372036854775808n && R.D2L(-0.5) === 0);
		check('signbit', R.signbit(-0) && !R.signbit(0) && R.signbit(R.f64bits(0xfff80000, 0)));
		check('f32bits', R.f32bits(0x3f800000) === 1 && R.f32bits(0x00000001) === 1.401298464324817e-45);
		check('isInf', R.isInf(-Infinity) && !R.isInf(NaN) && !R.isInf(1e308));
		check('CopySign NaN sign', M.CopySign_d_d(1.0, R.f64bits(0xfff80000, 0)) === -1.0);
		check('Truncate', M.Truncate_d(-2.7) === -2.0 && dbits(M.Truncate_d(-0.5)) === '8000000000000000');
		check('round', LM.round(2.5) === 3.0 && LM.round(-2.5) === -3.0 && LM.round(0.49999999999999994) === 0.0
			&& dbits(LM.round(-0.4)) === '8000000000000000' && LM.lround(-2.5) === -3 && LM.lround(1e300) === -9223372036854775808n);
		check('rint', LM.rint(2.5) === 2 && LM.rint(3.5) === 4 && dbits(LM.rint(-0.5)) === '8000000000000000');
		check('fmax/fmin', LM.fmax(NaN, 1.0) === 1.0 && LM.fmin(2.0, NaN) === 2.0 && LM.fmax(-0.0, 0.0) === 0.0 && dbits(LM.fmin(0.0, -0.0)) === '8000000000000000');
		check('Log2 powers', M.Log2_d(1024.0) === 10.0 && M.Log2_d(5e-324) === -1074.0 && M.Log2_d(1.0) === 0.0 && M.Log2_d(2.2250738585072014e-308) === -1022);
		check('toStr', R.toStr(1.5) === '1.5' && R.toStr(1e20) === '1E+20' && R.toStr(0.00001) === '1E-05' && R.toStr(true) === 'True' && R.toStr(-0) === '-0');
	}

	// char arrays and strings
	{
		const p = R.LitN('OPENSWMM_HS_V1', 16);
		check('LitN', u8(p).length === 16 && u8(p)[14] === 0 && u8(p)[15] === 0);
		const q = R.CopyBuf(p);
		u8(q)[0] = 88;
		check('CopyBuf', u8(p)[0] === 79 && q.O === p.O);
		const l1 = R.Lit('lit'), l2 = R.Lit('lit');
		u8(l1)[0] = 76;
		check('Lit gives a fresh buffer each time', l1.B !== l2.B && R.Str(R.Lit('lit')) === 'lit');
		check('Bufs', R.Bufs(5, 24).length === 5 && u8(R.Bufs(5, 24)[4]).length === 24);
		const tok = R.Lit('  a,b;;c ');
		const t1 = R.strtok(tok, R.Lit(' ,;')), t2 = R.strtok(cpp.CPtr.ZERO, R.Lit(' ,;')), t3 = R.strtok(cpp.CPtr.ZERO, R.Lit(' ,;'));
		check('strtok', R.Str(t1) === 'a' && R.Str(t2) === 'b' && R.Str(t3) === 'c' && R.strtok(cpp.CPtr.ZERO, R.Lit(' ,;')).get_IsNull());
		const hay = R.Lit('one two three');
		check('strstr / strchr / strrchr', cpp.CPtr.Diff(R.strstr(hay, R.Lit('two')), hay) === 4 && R.strstr(hay, R.Lit('four')).get_IsNull()
			&& cpp.CPtr.Diff(R.strchr(hay, 116), hay) === 4 && cpp.CPtr.Diff(R.strrchr(hay, 116), hay) === 8 && R.strchr(hay, 122).get_IsNull()
			&& cpp.CPtr.Diff(R.strchr(hay, 0), hay) === 13);
		check('strcmp unsigned', R.strcmp(R.Lit('é'), R.Lit('a')) > 0 && R.strcmp(R.Lit('ab'), R.Lit('abc')) < 0);
		const dst = R.Buf_i(32);
		R.strcpy(dst, R.Lit('ab'));
		R.strcat(dst, R.Lit('cd'));
		check('strcpy / strcat', R.Str(dst) === 'abcd' && R.strlen(dst) === 4);
		const ov = R.LitN('abcdef', 16);
		R.strcpy(cpp.CPtr.op_Add_CPtr_i(ov, 2), ov);
		check('strcpy overlapping (memmove)', R.Str(ov) === 'ababcdef', R.Str(ov));
		check('strcspn', R.strcspn(R.Lit('abc,d'), R.Lit(',;')) === 3 && R.strcspn(R.Lit('abc'), R.Lit(',')) === 3);
		check('toupper', R.toupper(97) === 65 && R.toupper(65) === 65 && R.toupper(-1) === -1);
		check('difftime', R.difftime(10, 4) === 6);
		check('time', Math.abs(Number(R.time_()) - Date.now() / 1000) < 5);
		const tp: L[] = [0];
		R.time_Ptrl(cpp.Ptr.new_TA_l(tp, 0));
		check('time(&t)', tp[0] === R.time_() || Number(tp[0]) + 1 === Number(R.time_()));
		const sp = new cpp.CPtr(Uint8Array.from([49, 50, 0]), 0);
		check('atof / strtod_CPtr', R.atof(sp) === 12 && R.strtod_CPtr(R.Lit('-.5e1')) === -5);
		const sb = [104, 105, 0, 0];
		check('a number array as a C string', R.strlen(new cpp.CPtr(sb, 0)) === 2 && R.Str(cpp.CPtr.from_Ptrc(cpp.Ptr.new_TA_l(sb, 0))) === 'hi');
		R.snprintf_CPtr_ul_CPtr_oA(cpp.CPtr.from_Ptrc(cpp.Ptr.new_TA_l(sb, 0)), 4, R.Lit('%c%c'), 200, 66);
		check('snprintf into a number array', (sb[0] & 255) === 200 && sb[1] === 66 && sb[2] === 0, sb);
	}
	console.log(`unit checks: ${un}, failures: ${ubad}`);
	return ubad;
}

const mode = process.argv[2];
let bad = 0;
switch (mode) {
	case 'libm': bad = libm(parseInt(process.argv[3], 10)); break;
	case 'printf': bad = lines('printf', printfCase); break;
	case 'scanf': bad = lines('scanf', scanfCase); break;
	case 'misc': bad = lines('misc', miscCase); break;
	case 'units': bad = units(); break;
	default: throw new Error('mode: libm N | printf | scanf | misc | units');
}
R.FlushAll();
