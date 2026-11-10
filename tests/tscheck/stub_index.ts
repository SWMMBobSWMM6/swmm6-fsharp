// stub_index.ts - stands in for ts/src/cpp/index.ts when run.sh checks part A alone: part A's files, plus the few
// members of parts B and C that part A calls at run time (SysException, Less.Fn, R.strtod_CPtr_CPtrr, R.ERange) and
// that the checks call (R.Lit, R.Str). The stubs do only what the checks need (strtod reads plain decimal numbers and
// never sets ERange). run.sh with
// REAL=1 uses the real index.ts and every part instead.
// Written by Claude (Anthropic) for the SWMM6 ports; see AI-USE.md.
export * from './a_long.js';
export * from './a_ref.js';
export * from './a_str.js';
export * from './a_std.js';
import { CPtr, Str } from './a_str.js';
import type { Ref } from './a_ref.js';

export class SysException extends Error {
	readonly kind: string;
	constructor(kind: string, message: string) { super(message); this.kind = kind; }
}
export class Less {
	static Fn = (a: any, b: any): boolean => {
		if (a instanceof Str) return a.compare_Str(b) < 0;
		if (typeof a === 'number') return a < b || (a !== a && b === b);
		if (typeof a === 'bigint') return a < b;
		if (typeof a === 'boolean') return !a && b;
		if (a != null && typeof a.CompareTo === 'function') return a.CompareTo(b) < 0;
		throw new Error("Less: no order");
	};
}
export class R {
	static Lit(s: string): CPtr { const b = new Uint8Array(s.length + 1); for (let i = 0; i < s.length; i++) b[i] = s.charCodeAt(i) & 0xFF; return new CPtr(b, 0); }
	static Str(p: CPtr): string { return p.toString(); }
	static readonly ERange = false;
	static strtod_CPtr_CPtrr(p: CPtr, end: Ref<CPtr>): number {
		const s = p.toString();
		const m = /^[ \t\n\v\f\r]*([+-]?(\d+\.?\d*|\.\d+)([eE][+-]?\d+)?)/.exec(s);
		if (m == null) { end.set(p); return 0; }
		end.set(new CPtr(p.B, p.O + m[0].length));
		return Number(m[1]);
	}
}
