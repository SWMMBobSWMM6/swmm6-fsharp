// sortcheck.mts - the cases of sortcheck.cpp through ts/src/cpp/stdalgo.ts (cpp.StdAlgo); output must equal
// sortcheck.cpp's byte for byte. Mode O (the default): Sort / StableSort / PartialSort over an array of structs.
// Mode N: the same permutations from sorting the ids themselves (numbers) by their keys, with the bounds passed
// as bigints (a cpp.L may be one), which must give the same output too.
// Written by Claude (Anthropic) for the SWMM6 ports; see AI-USE.md.
import { StdAlgo } from '../../ts/src/cpp/stdalgo.js';

const M64 = (1n << 64n) - 1n;
let s = 1729n;
function rnd(): bigint { s ^= (s << 13n) & M64; s ^= s >> 7n; s ^= (s << 17n) & M64; return s; }

class E { key: number; id: number; constructor(key: number, id: number) { this.key = key; this.id = id; } }

type Run = (key: number[], n: number, which: number) => number[];   // which: 0 sort, 1 stable_sort, 2 partial_sort

const mode = process.argv[2] ?? 'O';
let run: Run;
if (mode === 'O') {
	run = (key, n, which) => {
		const a: E[] = [];
		for (let i = 0; i < n; i++) a.push(new E(key[i], i));
		const lt = (x: E, y: E): boolean => x.key < y.key;
		if (which === 0) StdAlgo.Sort(a, 0, n, lt);
		else if (which === 1) StdAlgo.StableSort(a, 0, n, lt);
		else StdAlgo.PartialSort(a, 0, n, n, lt);
		return a.map((e) => e.id);
	};
} else if (mode === 'N') {
	run = (key, n, which) => {
		const a: number[] = [];
		for (let i = 0; i < n; i++) a.push(i);
		const lt = (x: number, y: number): boolean => key[x] < key[y];
		const N = BigInt(n);
		if (which === 0) StdAlgo.Sort(a, 0n, N, lt);
		else if (which === 1) StdAlgo.StableSort(a, 0n, N, lt);
		else StdAlgo.PartialSort(a, 0n, N, N, lt);
		return a;
	};
} else throw new Error('mode: O or N');

const ns: number[] = [];
for (let n = 0; n <= 300; n++) ns.push(n);
for (const n of [511, 512, 513, 1000, 4096, 10007]) ns.push(n);
const out: string[] = [];
const line = (tag: string, n: number, pat: number, ids: number[]): void => { out.push(`${tag} ${n} ${pat}:${ids.map((x) => ' ' + x).join('')}\n`); };
for (const n of ns) for (let pat = 0; pat < 7; pat++) {
	const key: number[] = new Array(n);
	for (let i = 0; i < n; i++) {
		let k: number;
		switch (pat) {
		case 0: k = Number(rnd() % 5n); break;                 // many ties
		case 1: k = Number(rnd() % 1000000n); break;           // few ties
		case 2: k = Math.trunc(i / 3); break;                  // sorted with runs
		case 3: k = Math.trunc((n - i) / 2); break;            // reversed with runs
		case 4: k = 7; break;                                  // all equal
		case 5: k = (i < Math.trunc(n / 2)) ? i : n - i; break;   // organ pipe
		default: k = (i * 7919) % 31; break;                   // sawtooth
		}
		key[i] = k;
	}
	line('sort', n, pat, run(key, n, 0));
	line('stable', n, pat, run(key, n, 1));
	line('heap', n, pat, run(key, n, 2));
}
process.stdout.write(out.join(''));
