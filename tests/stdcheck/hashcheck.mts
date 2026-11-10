// hashcheck.mts - the operations of hashcheck.cpp through the TypeScript runtime's hash tables (ts/src/cpp,
// containers.ts: UMap, USet, HashTable, RehashPolicy, StdHash); output must equal hashcheck.cpp's: bucket counts
// and iteration order after inserts, operator[], erases, reserve and copies, and std::hash of strings.
// Strings are made with Str's own push_back and read through its public bytes (b, n).
// Written by Claude (Anthropic) for the SWMM6 ports; see AI-USE.md.
import * as cpp from '../../ts/src/cpp/index.js';

const M64 = (1n << 64n) - 1n;
let s = 52024n;
function rnd(): bigint { s ^= (s << 13n) & M64; s ^= s >> 7n; s ^= (s << 17n) & M64; return s; }
const pre = ['J', 'C', 'N', 'OF', 'P', 'RG', 'SUB_', 'Link-', 'node.', 'W'];

function str(x: string): cpp.Str {
	const r = new cpp.Str();
	for (let i = 0; i < x.length; i++) r.push_back(x.charCodeAt(i));
	return r;
}
function text(t: cpp.Str): string {
	let r = '';
	for (let i = 0; i < t.n; i++) r += String.fromCharCode(t.b[i]);
	return r;
}
function name(r: bigint): cpp.Str {
	let x = pre[Number(r % 10n)] + String((r >> 8n) % 5000n);
	if ((r >> 20n) % 3n === 0n) x += '_' + String((r >> 24n) % 97n);
	return str(x);
}

const out: string[] = [];
for (let round = 0; round < 40; round++) {
	const m = cpp.UMap.new_FuncKul_FuncKKb_FuncV<cpp.Str, number>(null, null, () => 0);
	const mi = cpp.UMap.new_FuncKul_FuncKKb_FuncV<number, number>(null, null, () => 0);
	const st = cpp.USet.new_FuncTul_FuncTTb<cpp.Str>(null, null);
	const n = 1 + Number(rnd() % (round < 20 ? 60n : 3000n));
	if (round % 7 === 3) m.reserve(n);
	for (let i = 0; i < n; i++) {
		const r = rnd();
		const k = name(r);
		if (r % 5n === 0n) { const v = m.ref_Index(k); v.set((v.get() + i) | 0); }
		else m.emplace_K_V(k, i);
		mi.emplace_K_V(Number(r % 20000n) - 10000, i);
		st.insert(k);
		if (r % 11n === 0n && !m.empty()) m.erase_K(name(rnd()));
		if (r % 13n === 0n) mi.erase_K(Number(rnd() % 20000n) - 10000);
		if (r % 17n === 0n && !m.empty()) m.erase_MItE(m.begin());
	}
	const c = m.Clone();
	let line = `map ${round}: ${m.bucket_count()} buckets ${m.size()}:`;
	for (const kv of m.Items()) line += ` ${text(kv.first)}=${kv.second}`;
	line += '\ncopy:';
	for (const kv of c.Items()) line += ` ${text(kv.first)}`;
	line += `\nimap ${mi.bucket_count()} ${mi.size()}:`;
	for (const kv of mi.Items()) line += ` ${kv.first}=${kv.second}`;
	line += `\nset ${st.bucket_count()}:`;
	for (const k of st.Items()) line += ` ${text(k)}`;
	out.push(line + '\n');
}
for (const w of ['', 'a', 'abcdefg', 'abcdefgh', 'abcdefghi', 'Junction_17', 'OUTFALL-1'])
	out.push(`hash ${w} ${cpp.StdHash.Of_Str(str(w))}\n`);
process.stdout.write(out.join(''));
