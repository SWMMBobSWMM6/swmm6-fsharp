// containercheck.mts - the cases of containercheck.cpp through the TypeScript runtime (ts/src/cpp: containers.ts,
// stdalgo.ts, algo.ts); output must equal containercheck.cpp's byte for byte. 64-bit keys are cpp.L values (a
// number when safe, a bigint otherwise), as the engine holds them.
// Written by Claude (Anthropic) for the SWMM6 ports; see AI-USE.md.
import * as cpp from '../../ts/src/cpp/index.js';

type L = number | bigint;
const M64 = (1n << 64n) - 1n;
const SAFE = BigInt(Number.MAX_SAFE_INTEGER);
let s = 4242n;
function rnd(): bigint { s ^= (s << 13n) & M64; s ^= s >> 7n; s ^= (s << 17n) & M64; return s; }
/** A bigint as a canonical cpp.L. */
function toL(x: bigint): L { return x >= -SAFE && x <= SAFE ? Number(x) : x; }
const out: string[] = [];
const p = (x: string): void => { out.push(x); };

function str(x: string): cpp.Str { const r = new cpp.Str(); for (let i = 0; i < x.length; i++) r.push_back(x.charCodeAt(i)); return r; }
function text(t: cpp.Str): string { let r = ''; for (let i = 0; i < t.n; i++) r += String.fromCharCode(t.b[i]); return r; }
class E { key: number; id: number; constructor(key: number, id: number) { this.key = key; this.id = id; } }
function ids(v: E[]): string { return ':' + v.map((e) => ' ' + e.id).join('') + '\n'; }
function ints(tag: string, a: number[], n: number): void { p(`${tag} ${n}:${a.slice(0, n).map((x) => ' ' + x).join('')}\n`); }
function pos(it: cpp.Iter<any>, b: cpp.Iter<any>): number { return Number(it.i) - Number(b.i); }

// 64-bit keys
for (let round = 0; round < 12; round++) {
	const mu = cpp.UMap.new_FuncKul_FuncKKb_FuncV<L, number>(null, null, () => 0);
	const ml = cpp.UMap.new_FuncKul_FuncKKb_FuncV<L, number>(null, null, () => 0);
	const ku: L[] = [], kl: L[] = [];
	const n = 1 + Number(rnd() % (round < 6 ? 50n : 2000n));
	for (let i = 0; i < n; i++) {
		const r = rnd();
		const k = toL(r % 3n === 0n ? r >> 40n : r);
		if (r % 4n === 0n) { const v = mu.ref_Index(k); v.set((v.get() + i) | 0); } else mu.emplace_K_V(k, i);
		ku.push(k);
		const l = toL(r % 5n === 0n ? (r % 1000n) - 500n : (r >> 1n) * ((r & 2n) !== 0n ? -1n : 1n));
		ml.emplace_K_V(l, i);
		kl.push(l);
		if (r % 9n === 0n) mu.erase_K(ku[Number((r >> 8n) % BigInt(ku.length))]);
		if (r % 10n === 0n) ml.erase_K(kl[Number((r >> 8n) % BigInt(kl.length))]);
		if (r % 23n === 0n && !mu.empty()) mu.erase_MItE(mu.begin());
	}
	let line = `umap ${round}: ${mu.bucket_count()} ${mu.size()}:`;
	for (const kv of mu.Items()) line += ` ${kv.first}=${kv.second}`;
	line += `\nlmap ${ml.bucket_count()} ${ml.size()}:`;
	for (const kv of ml.Items()) line += ` ${kv.first}=${kv.second}`;
	p(line + '\n');
}
p(`hash ${cpp.StdHash.Of_i(-1)} ${cpp.StdHash.Of_l(-9223372036854775808n)} ${cpp.StdHash.Of_ul(18446744073709551615n)} ${cpp.StdHash.Of_i(12345)} ${cpp.StdHash.Of_l(-9007199254740993n)}\n`);

// unordered_set: reserve, rehash, erase while iterating, clear
for (let round = 0; round < 8; round++) {
	const us = cpp.USet.new_FuncTul_FuncTTb<number>(null, null);
	us.reserve(Number(rnd() % 100n));
	const n = Number(rnd() % 400n);
	for (let i = 0; i < n; i++) us.insert(Number(rnd() % 1000n) - 500);
	const dump = (): string => { let x = ''; for (const v of us.Items()) x += ' ' + v; return x; };
	let line = `uset ${round}: ${us.bucket_count()} ${us.size()}:` + dump();
	if (round % 2 === 0) us.rehash(Number(us.size()) * 3 + round);
	else us.reserve(Math.trunc(Number(us.size()) / 2));
	line += `\nrehash ${us.bucket_count()}:` + dump();
	for (let it = us.begin(); !cpp.MIt.op_Eq(it, us.end());) { if (it.get_Deref() % 3 !== 0) it = us.erase_MItE(it); else it = cpp.MIt.op_Increment(it); }
	line += `\nerased ${us.bucket_count()} ${us.size()}:` + dump();
	const cp = us.Clone();
	us.clear();
	for (let i = 0; i < 20; i++) us.insert(Number(rnd() % 50n));
	line += `\ncleared ${us.bucket_count()} ${us.size()}:` + dump();
	line += `\ncopy ${cp.bucket_count()}:`;
	for (const v of cp.Items()) line += ' ' + v;
	p(line + '\n');
}

// std::map / std::set
{
	const pre = ['J', 'c', 'N', 'OF', '_P', 'rg', 'SUB', 'Link-', 'node.', 'W~'];
	const ms = cpp.Map.new_FuncKKb_FuncV<cpp.Str, number>(null, () => 0);
	for (let i = 0; i < 300; i++) {
		const r = rnd();
		const k = str(pre[Number(r % 10n)] + String((r >> 8n) % 60n));
		if (r % 3n === 0n) { const v = ms.ref_Index(k); v.set((v.get() + i) | 0); } else ms.emplace_K_V(k, i);
		if (r % 7n === 0n) ms.erase_K(str(pre[Number((r >> 4n) % 10n)] + String((r >> 16n) % 60n)));
	}
	let line = `smap ${ms.size()}:`;
	for (const kv of ms.Items()) line += ` ${text(kv.first)}=${kv.second}`;
	line += '\nbounds:';
	for (const q of ['', 'J', 'J3', 'Link-', 'W~9', 'zz', 'node.5', 'OF10']) {
		const lb = ms.lower_bound(str(q)), ub = ms.upper_bound(str(q));
		line += ` ${q}:${cpp.MIt.op_Eq(lb, ms.end()) ? 'end' : text(lb.get_Deref().first)}/${cpp.MIt.op_Eq(ub, ms.end()) ? 'end' : text(ub.get_Deref().first)}`;
	}
	p(line + '\n');
	const mi = cpp.Map.new_FuncKKb_FuncV<number, number>(null, () => 0);
	for (let i = 0; i < 200; i++) {
		const r = rnd();
		const v = mi.ref_Index(Number(r % 300n) - 150); v.set((v.get() + i) | 0);
		if (r % 5n === 0n) mi.erase_K(Number((r >> 9n) % 300n) - 150);
	}
	line = `imap ${mi.size()}:`;
	for (const kv of mi.Items()) line += ` ${kv.first}=${kv.second}`;
	p(line + '\n');
	const sp = cpp.Set.new_FuncTTb<cpp.Pair<L, L>>(null);
	for (let i = 0; i < 200; i++) { const r = rnd(); sp.insert(cpp.Pair.new_A_B<L, L>(toL((r % 7n) - 3n), toL(((r >> 8n) % 1000n) - 500n))); }
	line = `pset ${sp.size()}:`;
	for (const x of sp.Items()) line += ` (${x.first},${x.second})`;
	p(line + '\n');
	const ss = cpp.Set.new_FuncTTb<cpp.Str>(null);
	for (let i = 0; i < 100; i++) {
		const r = rnd();
		ss.insert(str(pre[Number(r % 10n)] + String((r >> 8n) % 20n)));
		if (r % 4n === 0n) ss.erase_T(str(pre[Number((r >> 3n) % 10n)] + String((r >> 12n) % 20n)));
	}
	line = `sset ${ss.size()}:`;
	for (const x of ss.Items()) line += ' ' + text(x);
	p(line + '\n');
}

// std::stack / std::queue
{
	const st = new cpp.Stk<number>();
	const qu = new cpp.Que<number>();
	let line = 'stackqueue:';
	for (let i = 0; i < 500; i++) {
		const r = rnd();
		if (r % 3n !== 0n || st.empty()) st.push(Number(r % 100n)); else { line += ` s${st.top()}`; st.pop(); }
		if (r % 4n !== 0n || qu.empty()) qu.push(Number(r % 100n)); else { line += ` q${qu.front()}/${qu.back()}`; qu.pop(); }
	}
	p(line + ` | ${st.size()} ${qu.size()}\n`);
}

// partial_sort with middle < last; sort and stable_sort of subranges
const lt = (a: E, b: E): boolean => a.key < b.key;
for (const n of [0, 1, 2, 5, 16, 17, 33, 100, 257, 1000]) for (let pat = 0; pat < 2; pat++) {
	const v: E[] = [];
	for (let i = 0; i < n; i++) v.push(new E(pat === 0 ? Number(rnd() % 4n) : Number(rnd() % 100000n), i));
	for (const m of [0, 1, Math.trunc(n / 3), Math.trunc(n / 2), n - 1, n]) {
		if (m < 0 || m > n) continue;
		const c = v.slice(); cpp.StdAlgo.PartialSort(c, 0, m, n, lt);
		p(`psort ${n} ${pat} ${m} ` + ids(c));
	}
	const a = v.slice(); cpp.StdAlgo.Sort(a, Math.trunc(n / 4), Math.trunc((3 * n) / 4), lt);
	p(`subsort ${n} ${pat} ` + ids(a));
	const b = v.slice(); cpp.StdAlgo.StableSort(b, Math.trunc(n / 4), Math.trunc((3 * n) / 4), lt);
	p(`substable ${n} ${pat} ` + ids(b));
}

// std::sort with std::less: doubles with signed zeros and ties; pair<int, string>
{
	const d: number[] = [];
	for (let i = 0; i < 300; i++) { const r = rnd(); let x = (Number(r % 9n) - 4) * 0.5; if (x === 0 && ((r >> 8n) % 2n) !== 0n) x = -0; d.push(x); }
	cpp.StdAlgo.Sort(d, 0, d.length, cpp.Less.Fn);
	p('dsort:' + d.map((x) => ' ' + (Object.is(x, -0) ? '-0.0' : x.toFixed(1))).join('') + '\n');
	const w = ['b', 'a', 'ab', '', 'ba', 'B', 'aa'];
	const ps: cpp.Pair<number, cpp.Str>[] = [];
	for (let i = 0; i < 100; i++) { const r = rnd(); ps.push(cpp.Pair.new_A_B(Number(r % 5n), str(w[Number((r >> 8n) % 7n)]))); }
	cpp.StdAlgo.Sort(ps, 0, ps.length, cpp.Less.Fn);
	p('psort2:' + ps.map((x) => ` ${x.first}${text(x.second)}`).join('') + '\n');
}

// <algorithm> / <numeric>
for (let round = 0; round < 6; round++) {
	const n = Number(rnd() % 60n);
	const v = new cpp.Vec<number>(() => 0);
	for (let i = 0; i < n; i++) v.push_back(Number(rnd() % 10n) - 3);
	const A = cpp.Algo;
	const mn = A.min_element_IterT_IterT(v.begin(), v.end()), mx = A.max_element_IterT_IterT(v.begin(), v.end());
	p(`algo ${round}: min ${pos(mn, v.begin())} max ${pos(mx, v.begin())} count ${A.count(v.begin(), v.end(), 2)} find ${pos(A.find(v.begin(), v.end(), 4), v.begin())} sorted ${A.is_sorted_IterT_IterT(v.begin(), v.end()) ? 1 : 0} sum ${A.accumulate_Iteri_Iteri_i(v.begin(), v.end(), 0)}\n`);
	p(`pred ${A.any_of(v.begin(), v.end(), (x) => x > 5) ? 1 : 0} ${A.all_of(v.begin(), v.end(), (x) => x > -3) ? 1 : 0} ${A.none_of(v.begin(), v.end(), (x) => x === 6) ? 1 : 0} ${A.count_if(v.begin(), v.end(), (x) => x < 0)} ${pos(A.adjacent_find_IterT_IterT(v.begin(), v.end()), v.begin())} ${A.equal(v.begin(), v.end(), v.begin()) ? 1 : 0}\n`);
	const w = A.VecFrom(v.begin(), v.end());
	A.replace(w.begin(), w.end(), 1, 7);
	A.reverse(w.begin(), w.end());
	ints('replrev', w.a, w.n);
	const r = A.VecFrom(v.begin(), v.end());
	const re = A.remove_if(r.begin(), r.end(), (x) => x % 3 === 0);
	ints('remove_if', r.a, pos(re, r.begin()));
	const u = A.VecFrom(v.begin(), v.end());
	cpp.StdAlgo.Sort(u.a, 0, u.n, cpp.Less.Fn);
	let line = 'bounds:';
	for (let q = -4; q <= 7; q++)
		line += ` ${q}:${pos(A.lower_bound_IterT_IterT_T(u.begin(), u.end(), q), u.begin())}/${pos(A.upper_bound_IterT_IterT_T(u.begin(), u.end(), q), u.begin())}`;
	p(line + '\n');
	const ue = A.unique_IterT_IterT(u.begin(), u.end());
	ints('unique', u.a, pos(ue, u.begin()));
	const io = new cpp.Vec<L>(() => 0);
	for (let i = 0; i < 5; i++) io.push_back(0);
	A.iota_Iterul_Iterul_ul(io.begin(), io.end(), 18446744073709551613n);
	p('iota:' + io.a.slice(0, 5).map((x) => ' ' + x).join('') + '\n');
}
process.stdout.write(out.join(''));
