// corecheck.ts - the TypeScript side of corecheck.cs: the same operations on part A of the TypeScript runtime (Vec,
// Iter, RevIter, Ptr, CSpan, Str, CPtr, Pair, Opt, M, Arr, Mv, Dtor, Defer, Box, the exceptions), each C# call written
// the way ts/RUNTIME_HELPERS.md says the translated engine writes it (suffixed names, makers, Refs). run.sh requires
// the same output as the C#. Doubles print as their bit patterns, booleans as 0/1, strings in brackets.
// Written by Claude (Anthropic) for the SWMM6 ports; see AI-USE.md.
import * as cpp from './cpp/index.js';
declare const process: any;     // Node (no @types/node here)

type L = cpp.L;
const outp: string[] = [];
function P(tag: string, v: any): void { outp.push(tag + "=" + v); }
const dv = new DataView(new ArrayBuffer(8));
function d(x: number): string { dv.setFloat64(0, x); return dv.getBigUint64(0).toString(16); }
function bits(h: bigint): number { dv.setBigUint64(0, h); return dv.getFloat64(0); }
/** C#'s double.NaN (x86's default NaN: the sign bit set). */
const NaN_ = bits(0xFFF8000000000000n);
function b(x: boolean): string { return x ? "1" : "0"; }
function s(x: cpp.Str): string { return "[" + x + "]"; }
function vd(v: cpp.Vec<number>): string { let r = v.size() + "/" + v.capacity() + ":"; for (let i = 0; i < v.n; i++) r += " " + d(v.a[i]); return r; }
function vi(v: cpp.Vec<number>): string { let r = v.size() + "/" + v.capacity() + ":"; for (let i = 0; i < v.n; i++) r += " " + v.a[i]; return r; }
function vs(v: cpp.Vec<cpp.Str>): string { let r = v.size() + "/" + v.capacity() + ":"; for (let i = 0; i < v.n; i++) r += " " + s(v.a[i]); return r; }
function vl(v: cpp.Vec<L>): string { let r = v.size() + "/" + v.capacity() + ":"; for (let i = 0; i < v.n; i++) r += " " + v.a[i]; return r; }
function hex(a: Uint8Array, n: number): string { let r = ""; for (let i = 0; i < n; i++) r += a[i].toString(16).padStart(2, '0'); return r; }
function lit(x: string): cpp.CPtr { return (cpp as any).R.Lit(x); }
const mkD = (): number => 0, mkB = (): boolean => false, mkS = (): cpp.Str => new cpp.Str();
const I = cpp.Iter, NPOS = cpp.Str.npos;
/** C#'s ++p[i], p[i]--, ... on a CPtr: the int value of the expression, the low byte stored. */
function preInc(p: cpp.CPtr, i: number): number { const v = p.get(i) + 1; p.set(i, v); return v; }
function preDec(p: cpp.CPtr, i: number): number { const v = p.get(i) - 1; p.set(i, v); return v; }
function postInc(p: cpp.CPtr, i: number): number { const v = p.get(i); p.set(i, v + 1); return v; }
function postDec(p: cpp.CPtr, i: number): number { const v = p.get(i); p.set(i, v - 1); return v; }

class Holder { x: number = 0; k: number = 0; }
const atX: cpp.RefAt<number> = (o: any): cpp.Ref<number> => cpp.fieldRef(o as Holder, "x");
const atX2: cpp.RefAt<number> = (o: any): cpp.Ref<number> => cpp.fieldRef(o as Holder, "x");
class Res { readonly name: string; constructor(n: string) { this.name = n; } Dispose(): void { P("dispose", this.name); } }
class Node {
	id: number = 0;
	constructor(i?: number) { if (i !== undefined) this.id = i; }
	Clone(): Node { return new Node(this.id + 1000); }
	Assign(o: Node): void { this.id = o.id + 2000; }
}

function vec(): void {
	const v = new cpp.Vec<number>(mkD);
	let caps = "";
	for (let i = 0; i < 20; i++) { v.push_back(i + 0.5); caps += v.capacity() + " "; }
	P("grow", caps);
	v.resize_ul(5); P("resize5", vd(v));
	v.resize_ul(9); P("resize9", vd(v));
	v.resize_ul_T(12, 7.5); P("resize12", vd(v));
	const w = cpp.Vec.new_TA([10, 20, 30], mkD);
	v.insert_IterT_T(I.op_Add(v.begin(), 2), 99); P("insT", vd(v));
	v.insert_IterT_TA(I.op_Add(v.begin(), 1), [1, 2, 3]); P("insTA", vd(v));
	v.insert_IterT_IterT_IterT(v.end(), w.begin(), w.end()); P("insII", vd(v));
	v.insert_IterT_ul_T(v.begin(), 3, -1.0); P("insN", vd(v));
	const raw = [5, 6, 7, 8];
	v.insert_IterT_PtrT_PtrT(I.op_Add(v.begin(), 4), cpp.Ptr.new_TA_l(raw, 1), cpp.Ptr.new_TA_l(raw, 3)); P("insP", vd(v));
	let r = v.erase_IterT(I.op_Add(v.begin(), 3)); P("erase1", vd(v) + " @" + r.i);
	r = v.erase_IterT_IterT(I.op_Add(v.begin(), 1), I.op_Add(v.begin(), 4)); P("erase3", vd(v) + " @" + r.i);
	v.reserve(100); P("reserve", vd(v));
	v.shrink_to_fit(); P("shrink", vd(v));
	v.assign_ul_T(3, 2.0); P("assignN", vd(v));
	v.assign_IterT_IterT(I.op_Add(w.begin(), 1), w.end()); P("assignII", vd(v));
	v.pop_back(); P("pop", vd(v));
	v.swap(w); P("swapV", vd(v)); P("swapW", vd(w));
	const c = v.Clone(); P("clone", vd(c));
	const e = new cpp.Vec<number>(mkD); e.reserve(10); e.Assign_VecT(v); P("assign", vd(e));
	const m = new cpp.Vec<number>(mkD); m.MoveFrom_VecT(c); P("moveTo", vd(m)); P("moveFrom", vd(c));
	v.clear(); P("clear", vd(v) + " " + b(v.empty()));
	P("eqNaN", b(cpp.Vec.Eq(cpp.Vec.new_TA([NaN_, 0.0], mkD), cpp.Vec.new_TA([NaN_, -0.0], mkD))));
	P("eqLen", b(cpp.Vec.Eq(cpp.Vec.new_ul(2, mkD), cpp.Vec.new_ul(3, mkD))));
	P("ctorN", vd(cpp.Vec.new_ul_T(4, 1.25, mkD)));
	try { w.at(7); } catch (x) { if (x instanceof cpp.OutOfRange) P("at", x.what()); else throw x; }
	try { w.at(18446744073709551615n); } catch (x) { if (x instanceof cpp.OutOfRange) P("at", x.what()); else throw x; }
	const vi_ = cpp.Vec.new_TA([1, 2, 3], mkD);
	P("vi", vi(vi_)); vi_.push_back(4); P("vi4", vi(vi_));
	const vb = cpp.Vec.new_TA([200, 1], mkD);
	P("eqB", b(cpp.Vec.Eq(vb, cpp.Vec.new_TA([200, 1], mkD))) + b(cpp.Vec.Eq(vb, cpp.Vec.new_TA([201, 1], mkD))));
	const vz = cpp.Vec.new_ul_T(3, true, mkB); vz.resize_ul(5); let zs = ""; for (let i = 0; i < vz.n; i++) zs += b(vz.a[i]); P("vz", zs);

	const sv = cpp.Vec.new_ul_FuncT(3, (): cpp.Str => new cpp.Str());
	sv.a[1].append_CPtr(lit("one"));
	sv.push_back(cpp.Str.new_str("two"));
	const sc = sv.Clone();
	sc.a[1].append_CPtr(lit("!"));
	P("strVec", vs(sv)); P("strClone", vs(sc));
	P("strEq", b(cpp.Vec.Eq(sv, sv.Clone())) + b(cpp.Vec.Eq(sv, sc)));
	sv.resize_ul(6); P("strResize", vs(sv));
	sv.insert_IterT_IterT_IterT(I.op_Add(sv.begin(), 1), sc.begin(), I.op_Add(sc.begin(), 2));
	sc.a[1].append_CPtr(lit("?"));
	P("strIns", vs(sv));
	sv.erase_IterT_IterT(sv.begin(), I.op_Add(sv.begin(), 2)); P("strErase", vs(sv));
	const va = cpp.Vec.new_ul_FuncT(2, (): number[] => [0, 0, 0]);
	va.a[0] = [1, 2, 3];
	const vc = va.Clone();
	vc.a[0][1] = 42;
	P("arrVec", d(va.a[0][1]) + " " + d(vc.a[0][1]));
	const none = new cpp.Vec<number[]>((): number[] => null);
	none.Assign_VecT(cpp.Vec.new_FuncT((): number[] => [0, 0, 0]));
	none.resize_ul(2);
	P("mkAdopt", none.a[1].length);
	const none2 = new cpp.Vec<number[]>((): number[] => null);
	none2.MoveFrom_VecT(cpp.Vec.new_FuncT((): number[] => [0, 0, 0, 0]));
	none2.resize_ul(1);
	P("mkMove", none2.a[0].length);
}

function iter(): void {
	const w = cpp.Vec.new_TA([1, 2, 3], mkD);
	const it = w.begin();
	w.push_back(4);
	w.a[0] = 100;
	P("staleIter", it.get_Deref() + " " + I.op_Add(it, 0).get_Deref());
	let inc = w.begin(); inc = I.op_Increment(inc);
	P("iterSub", I.op_Sub_IterT_IterT(w.end(), w.begin()) + " " + I.op_Sub_IterT_l(w.end(), 2).get_Deref());
	P("iterCmp", b(I.op_Lt(w.begin(), w.end())) + b(I.op_Eq(inc, I.op_Add(w.begin(), 1))) + b(I.op_Ne(w.begin(), w.end())));
	let sb = "";
	for (let ri = w.rbegin(); cpp.RevIter.op_Ne(ri, w.rend()); ri = cpp.RevIter.op_Increment(ri)) sb += ri.get_Deref() + " ";
	P("rev", sb);
	const bi = I.new_TA_l([7, 8, 9], 1);
	P("bareIter", bi.get_Deref() + " " + I.op_Add(bi, 1).get_Deref() + " " + bi.get_Item(-1));
	const fromIt = cpp.Ptr.from_IterT(I.op_Add(w.begin(), 2));
	P("ptrFromIter", fromIt.get_Deref() + " " + fromIt.o);
	const sp = new cpp.CSpan<number>([1, 2, 3, 4, 5], 1, 3);
	P("span", sp.size() + " " + d(sp.front()) + " " + d(sp.back()) + " " + d(sp.get_Item(1)) + " " + sp.subspan(1).size() + " " + d(sp.subspan(1).front()) + " " + d(sp.last(2).front()) + " " + sp.first(2).size() + " " + b(cpp.CSpan.ZERO.empty()));
}

function str(): void {
	const s_ = cpp.Str.new_CPtr(lit("Hello, World"));
	P("size", s_.size());
	const o = 111, z = 122;      // 'o', 'z'
	P("find", s_.find_CPtr_ul(lit("o"), 0) + " " + s_.find_i_ul(o, 5) + " " + s_.find_i_ul(z, 0) + " " + s_.find_Str_ul(cpp.Str.new_CPtr(lit("World")), 0) + " " + s_.find_CPtr_ul(lit("World"), 8));
	P("findEmpty", s_.find_CPtr_ul(lit(""), 12) + " " + s_.find_CPtr_ul(lit(""), 13));
	P("rfind", s_.rfind_i_ul(o, NPOS) + " " + s_.rfind_CPtr_ul(lit("o"), 5) + " " + s_.rfind_CPtr_ul(lit("l"), NPOS) + " " + s_.rfind_CPtr_ul(lit("Hello, World!"), NPOS) + " " + new cpp.Str().rfind_i_ul(97, NPOS));
	P("firstOf", s_.find_first_of_CPtr_ul(lit(", "), 0) + " " + s_.find_first_not_of_CPtr_ul(lit("Hel"), 0) + " " + s_.find_first_not_of_i_ul(72, 0) + " " + s_.find_first_of_CPtr_ul(lit("xyz"), 0));
	P("lastOf", s_.find_last_of_CPtr_ul(lit("lo"), NPOS) + " " + s_.find_last_not_of_CPtr_ul(lit("dl"), NPOS) + " " + s_.find_last_of_i_ul(o, 6) + " " + s_.find_last_not_of_i_ul(100, NPOS) + " " + s_.find_last_of_CPtr_ul(lit("H"), 3));
	P("substr", s(s_.substr(7, NPOS)) + s(s_.substr(0, 5)) + s(s_.substr(12, 3)));
	try { s_.substr(13, 1); } catch (x) { if (x instanceof cpp.OutOfRange) P("substrErr", x.what()); else throw x; }
	try { s_.at(12); } catch (x) { if (x instanceof cpp.OutOfRange) P("atErr", x.what()); else throw x; }
	const t = s_.Clone();
	t.erase(5, 2); P("erase", s(t));
	t.insert_ul_CPtr(5, lit(" there")); P("insert", s(t));
	t.replace_ul_ul_CPtr(0, 5, lit("Bye")); P("replace", s(t));
	t.append_i(33); t.append_ul_i(3, 63); t.push_back(35); P("append", s(t));
	t.pop_back(); t.resize(24, 46); P("resize", s(t) + " " + t.size());
	t.resize(4, 0); P("resize4", s(t));
	t.erase(1, NPOS); P("eraseAll", s(t));
	t.insert_ul_ul_i(1, 2, 120); t.insert_ul_Str(0, cpp.Str.new_CPtr(lit(">>"))); P("insertN", s(t));
	P("compare", cpp.Str.new_CPtr(lit("abc")).compare_CPtr(lit("abd")) + " " + cpp.Str.new_CPtr(lit("ab")).compare_CPtr(lit("abc")) + " " + cpp.Str.new_CPtr(lit("abc")).compare_CPtr(lit("ab")) + " " + cpp.Str.new_ucA_i_i([0xFF], 0, 1).compare_CPtr(lit("a")) + " " + cpp.Str.new_CPtr(lit("abc")).compare_Str(cpp.Str.new_CPtr(lit("abc"))) + " " + s_.compare_ul_ul_Str(7, 5, cpp.Str.new_CPtr(lit("World"))));
	P("startsEnds", b(s_.starts_with_CPtr(lit("Hell"))) + b(s_.starts_with_Str(cpp.Str.new_CPtr(lit("ello")))) + b(s_.starts_with_i(72)) + b(s_.ends_with_CPtr(lit("World"))) + b(s_.ends_with_i(120)) + b(s_.contains_Str(cpp.Str.new_CPtr(lit("o, W")))) + b(s_.contains_i(35)));
	P("cat", s(cpp.Str.Cat_Str_CPtr(s_, lit("!"))) + s(cpp.Str.Cat_CPtr_Str(lit("<"), s_)) + s(cpp.Str.Cat_Str_Str(s_, s_)) + s(cpp.Str.Cat_Str_i(s_, 63)) + s(cpp.Str.Cat_i_Str(94, s_)));
	P("toString", s(cpp.Str.to_string_i(-42)) + s(cpp.Str.to_string_ul(18446744073709551615n)) + s(cpp.Str.to_string_u(4294967295)) + s(cpp.Str.to_string_l(-9223372036854775808n)) + s(cpp.Str.to_string_d(3.14159)) + s(cpp.Str.to_string_d(-0.0)) + s(cpp.Str.to_string_d(1e20)) + s(cpp.Str.to_string_d(2.5e-7)) + s(cpp.Str.to_string_f(Math.fround(0.1))));
	const idx = cpp.Ptr.new_TA_l<L>([0], 0);
	P("stod", d(cpp.Str.stod(cpp.Str.new_CPtr(lit("  12.5e1xyz")), idx)) + " " + idx.get_Deref() + " " + d(cpp.Str.stod(cpp.Str.new_CPtr(lit("-0.25")))));
	try { cpp.Str.stod(cpp.Str.new_CPtr(lit("abc"))); } catch (x) { if (x instanceof cpp.InvalidArgument) P("stodErr", x.what()); else throw x; }
	P("stoi", cpp.Str.stoi(cpp.Str.new_CPtr(lit(" -17abc")), idx, 10) + " " + idx.get_Deref() + " " + cpp.Str.stol(cpp.Str.new_CPtr(lit("99999999999"))));
	try { cpp.Str.stoi(cpp.Str.new_CPtr(lit("99999999999"))); } catch (x) { if (x instanceof cpp.OutOfRange) P("stoiErr", x.what()); else throw x; }
	const lat = cpp.Str.new_str("café€!");
	P("latin1", hex(lat.b, lat.n));
	const g = new cpp.Str(); for (let i = 0; i < 40; i++) g.push_back(97 + i % 26);
	P("grow", g.b.length + " " + s(g));
	const cs = g.c_str(); P("cstr", b(cs.B === g.b) + " " + g.b[g.n]);
	const h = new cpp.Str(); h.MoveFrom(g); P("move", s(h) + s(g) + " " + g.b.length);
	h.Assign_CPtr(lit("assigned")); P("assignC", s(h) + " " + h.b.length);
	h.Assign_Str(cpp.Str.new_CPtr(lit("xy"))); P("assignS", s(h) + " " + h.b.length);
	h.Assign_i(81); P("assignI", s(h));
	P("eq", b(h.EqLit(lit("Q"))) + b(h.EqLit(lit("QQ"))) + b(h.Equals_Str(cpp.Str.new_CPtr(lit("Q")))) + b(h.Equals_Str(null)));
	P("hash", cpp.Str.new_CPtr(lit("Hello, World")).GetHashCode() + " " + cpp.Str.new_ucA_i_i([0xE9, 97], 0, 2).GetHashCode());
	const v = cpp.Str.new_CPtr(lit("  view  ")); v.remove_prefix(2); v.remove_suffix(2); P("view", s(v));
	const a = new cpp.Str(); a.assign_CPtr_CPtr(cpp.CPtr.op_Add_CPtr_i(lit("abcdef"), 1), cpp.CPtr.op_Add_CPtr_i(lit("abcdef"), 4)); P("assignRange", s(a));
	const raw = [113, 114, 115, 116];
	a.assign_Ptrc_Ptrc(cpp.Ptr.new_TA_l(raw, 1), cpp.Ptr.new_TA_l(raw, 3)); P("assignPtr", s(a));
	a.assign_ul_i(3, 122); P("assignN", s(a));
	a.append_Str_ul_ul(cpp.Str.new_CPtr(lit("0123456")), 2, 3); a.append_CPtr_ul(lit("ABCDEF"), 2); P("appendSub", s(a));
	P("ctors", s(cpp.Str.new_ul_i(5, 45)) + s(cpp.Str.new_CPtr_ul(lit("abcdef"), 3)) + s(cpp.Str.new_Str_ul_ul(cpp.Str.new_CPtr(lit("abcdef")), 2, 10)) + s(cpp.Str.from_CPtr(lit("conv"))));
	const bi = s_.begin(); P("strIter", String.fromCharCode(bi.get_Deref()) + " " + I.op_Sub_IterT_IterT(s_.end(), bi));
}

function cptr(): void {
	const buf = new Uint8Array(8);
	const p = new cpp.CPtr(buf, 0);
	p.set(0, 200);
	p.set(1, 200); const r1 = 200;
	p.set(2, 127);
	const r2 = preInc(p, 2);
	p.set(3, -128);
	const r3 = postDec(p, 3);
	p.set(4, 300);
	const r4 = postInc(p, 5);
	const r5 = preDec(p, 6);
	P("cptrElems", p.get(0) + " " + r1 + " " + p.get(1) + " " + r2 + " " + p.get(2) + " " + r3 + " " + p.get(3) + " " + p.get(4) + " " + r4 + " " + p.get(5) + " " + r5 + " " + p.get(6) + " " + p.ref(4).get() + " " + p.get_Item(4));
	const q = cpp.CPtr.op_Add_CPtr_i(p, 3);
	const q1 = cpp.CPtr.op_Increment(q), q2 = cpp.CPtr.op_Decrement(q);
	P("cptrArith", cpp.CPtr.Diff(q, p) + " " + b(cpp.CPtr.op_Lt(p, q)) + b(cpp.CPtr.op_Eq(q, cpp.CPtr.op_Add_CPtr_l(p, 3))) + b(cpp.CPtr.op_Ne(p, new cpp.CPtr(new Uint8Array(8), 0))) + b(cpp.CPtr.op_Eq(cpp.CPtr.Null, new cpp.CPtr(null, 5))) + b(cpp.CPtr.Null.get_IsNull()) + b(p.get_IsNull()) + " " + q1.O + " " + q2.O + " " + cpp.CPtr.op_Sub(q, 2).O + " " + cpp.CPtr.op_Add_CPtr_ul(q, 4).O);
	P("cptrStr", cpp.CPtr.op_Add_CPtr_i(lit("text"), 1).toString() + " " + cpp.CPtr.Null.toString());
	const pb = [97, 98, 99, 0];
	const fromC = cpp.CPtr.from_Ptrc(cpp.Ptr.new_TA_l(pb, 1));
	const fromUc = cpp.CPtr.from_Ptruc(cpp.Ptr.new_TA_l([97, 98, 99, 0], 1));
	P("fromPtr", fromC.toString() + " " + fromUc.get(1));
}

function ptr(): void {
	const arr = [1, 2, 3, 4, 5];
	const p = cpp.Ptr.new_TA_l(arr, 1);
	P("ptrIdx", d(p.get_Item_i(1)) + " " + d(p.get_Item_l(2)) + " " + d(p.get_Deref()) + " " + d(cpp.Ptr.op_Add_PtrT_l(p, 3).get_Deref()) + " " + d(cpp.Ptr.op_Sub_PtrT_l(p, 1).get_Deref()));
	p.ref_Item_i(0).set(20); let pi = p; pi = cpp.Ptr.op_Increment(pi); pi.ref_Deref().set(30);
	P("ptrSet", d(arr[1]) + " " + d(arr[2]));
	const q = cpp.Ptr.op_Add_PtrT_l(p, 2);
	P("ptrCmp", cpp.Ptr.op_Sub_PtrT_PtrT(q, p) + " " + b(cpp.Ptr.op_Eq(q, cpp.Ptr.new_TA_l(arr, 3))) + b(cpp.Ptr.op_Eq(q, cpp.Ptr.new_TA_l([0, 0, 0, 0, 0], 3))) + b(cpp.Ptr.op_Lt(p, q)) + b(cpp.Ptr.op_Eq(cpp.Ptr.ZERO, cpp.Ptr.new_TA_l<number>(null, 7))) + b(cpp.Ptr.ZERO.get_IsNull()) + b(cpp.Ptr.op_Ne(p, q)));
	const h = new Holder(); h.x = 1.5;
	const f1 = cpp.Ptr.new_o_RefAtT(h, atX), f2 = cpp.Ptr.new_o_RefAtT(h, atX), f3 = cpp.Ptr.new_o_RefAtT(h, atX2), f4 = cpp.Ptr.new_o_RefAtT(new Holder(), atX);
	f1.ref_Deref().set(2.5);
	P("fieldPtr", d(h.x) + " " + d(f2.get_Deref()) + " " + d(cpp.Ptr.op_Add_PtrT_l(f1, 3).get_Item_i(9)) + " " + b(f1.get_IsNull()));
	P("fieldEq", b(cpp.Ptr.op_Eq(f1, f2)) + b(cpp.Ptr.op_Eq(f1, f3)) + b(cpp.Ptr.op_Eq(f1, f4)) + b(cpp.Ptr.op_Eq(f1, cpp.Ptr.op_Add_PtrT_l(f2, 1))) + b(cpp.Ptr.op_Eq(f1, p)));
	const ps = cpp.Ptr.new_TA_l([cpp.Str.new_CPtr(lit("a")), cpp.Str.new_CPtr(lit("b"))], 0);
	P("ptrGen", s(cpp.Ptr.op_Add_PtrT_l(ps, 1).get_Deref()) + " " + b(cpp.Ptr.ZERO.get_IsNull()) + b(cpp.Ptr.op_Ne(ps, cpp.Ptr.op_Add_PtrT_l(ps, 1))));
}

function misc(): void {
	const M = cpp.M;
	P("min", d(M.min_d_d(0.0, -0.0)) + " " + d(M.min_d_d(-0.0, 0.0)) + " " + d(M.max_d_d(-0.0, 0.0)) + " " + d(M.min_d_d(NaN_, 1.0)) + " " + d(M.min_d_d(1.0, NaN_)) + " " + d(M.max_d_d(NaN_, 1.0)) + " " + d(M.max_d_d(1.0, NaN_)));
	P("clamp", d(M.clamp_d_d_d(NaN_, 0.0, 1.0)) + " " + d(M.clamp_d_d_d(-0.0, 0.0, 1.0)) + " " + d(M.clamp_d_d_d(5.0, 0.0, 1.0)) + " " + M.clamp_i_i_i(-5, 0, 10));
	P("unsigned", M.max_ul_ul(18446744073709551615n, 1) + " " + M.min_ul_ul(18446744073709551615n, 1) + " " + M.max_u_u(4294967295, 1) + " " + M.min_uc_uc(200, 100) + " " + M.max_uc_uc(200, 100) + " " + M.max_i_i(-3, 2) + " " + M.min_l_l(-3, 2));
	P("listMinMax", d(M.max_dA([1, NaN_, 3])) + " " + d(M.min_dA([NaN_, 1, -2])) + " " + M.max_iA([4, 9, 2]) + " " + M.min_iA([4, 9, 2]));
	const p1 = cpp.Pair.new_A_B(1, cpp.Str.new_CPtr(lit("b"))), p2 = cpp.Pair.new_A_B(1, cpp.Str.new_CPtr(lit("a")));
	P("pairCmp", p1.CompareTo(p2) + " " + p2.CompareTo(p1) + " " + p1.CompareTo(p1.Clone()) + " " + b(cpp.Pair.op_Lt(p2, p1)) + " " + b(cpp.Pair.Eq(p1, p1.Clone())) + b(cpp.Pair.Eq(p1, p2)));
	const n1 = cpp.Pair.new_A_B(NaN_, 1), n2 = cpp.Pair.new_A_B(0.0, 1);
	P("pairNaN", n1.CompareTo(n2) + " " + n2.CompareTo(n1) + " " + n1.CompareTo(cpp.Pair.new_A_B(NaN_, 2)) + " " + cpp.Pair.new_A_B(-0.0, 1).CompareTo(n2) + " " + b(cpp.Pair.Eq(n1, cpp.Pair.new_A_B(NaN_, 1))));
	const pc = p1.Clone(); pc.second.append_CPtr(lit("!")); P("pairClone", s(p1.second) + s(pc.second));
	const pm = new cpp.Pair<cpp.Str, cpp.Str>(mkS, mkS); pm.first.append_CPtr(lit("m")); P("pairMake", s(pm.first) + s(pm.second));
	p2.Assign(p1); p1.second.append_CPtr(lit("+")); P("pairAssign", s(p2.second));

	const o = new cpp.Opt<number>(mkD);
	try { o.value(); } catch (x) { if (x instanceof cpp.BadOptionalAccess) P("optErr", x.what()); else throw x; }
	P("opt", d(o.value_or(9)) + " " + b(o.has_value()));
	o.emplace(3.5); const oc = o.Clone(); o.reset();
	P("opt2", d(oc.value()) + " " + b(o.has) + " " + d(o.val));
	const os = cpp.Opt.new_T(cpp.Str.new_CPtr(lit("x")), mkS); const osc = os.Clone(); osc.val.append_CPtr(lit("y"));
	const osa = new cpp.Opt<cpp.Str>(mkS); osa.Assign(os); os.val.append_CPtr(lit("z"));
	P("optStr", s(os.val) + s(osc.val) + s(osa.val));

	const da = cpp.Arr.Make(3, mkD); da[1] = NaN_;
	const db = cpp.Arr.Clone(da);
	P("arrD", b(cpp.Arr.Eq(da, db)) + " " + b(db !== da) + " " + cpp.Arr.Make2(2, 3, mkD)[1].length);
	const sa = cpp.Arr.MakeWith(2, (): cpp.Str => cpp.Str.new_CPtr(lit("s")));
	const sb = cpp.Arr.Clone(sa); sb[0].append_CPtr(lit("!"));
	const sd = cpp.Arr.Make(2, mkS); cpp.Arr.Assign(sd, sa); sa[1].append_CPtr(lit("?"));
	P("arrStr", s(sa[0]) + s(sb[0]) + s(sd[1]) + s(sa[1]) + " " + (Array.isArray(sa) && sa[0] instanceof cpp.Str ? "Str[]" : "?"));
	const grid = [[1, 2], [3, 4]];
	const gc = cpp.Arr.Clone(grid); gc[0][0] = 9;
	P("arrShallow", d(grid[0][0]));
	const vg = new cpp.Vec<number[][]>((): number[][] => null); vg.push_back(grid); const vgc = vg.Clone(); vgc.a[0][1][1] = 7;
	P("vecDeep", d(grid[1][1]));

	const src = new cpp.Cell<cpp.Str>(cpp.Str.new_CPtr(lit("taken")));
	const got = cpp.Mv.Take(src, mkS);
	P("take", s(got) + s(src.v));
	const src2 = new cpp.Cell<cpp.Str>(cpp.Str.new_CPtr(lit("ref")));
	P("takeRef", s(cpp.Mv.TakeRef(src2)) + " " + b(src2.v == null));
	const x_ = new cpp.Cell<number>(1);
	const box = [6.5];
	P("back", d(cpp.Mv.Back(2.5, x_, box)) + " " + d(x_.v));
	const holder = new cpp.Cell<Res>(new Res("first"));
	cpp.Mv.Reset(holder, new Res("second"));
	cpp.Mv.Reset(holder, holder.v);
	cpp.Mv.Reset(holder, null);
	cpp.Mv.Delete(new Res("deleted"));
	cpp.Dtor.Run([new Res("arr0"), null, new Res("arr1")]);
	const pp = new cpp.Cell<cpp.Ptr<number>>(cpp.Ptr.new_TA_l([1, 2, 3], 0));
	const before = cpp.Mv.PostStep_PtrTr_i(pp, 1);
	P("postStep", d(before.get_Deref()) + " " + d(pp.v.get_Deref()));
	const df = new cpp.Defer((): void => P("defer", "ran"));
	df.Dispose(); df.Dispose();
	const dc = new cpp.Defer((): void => P("defer", "cancelled?")); dc.Cancel(); dc.Dispose();

	const es: cpp.CppException[] = [new cpp.CppException(), cpp.LogicError.new_Str(cpp.Str.new_CPtr(lit("logic"))), cpp.RuntimeError.new_str("runtime"), cpp.InvalidArgument.new_str("inv"), cpp.OutOfRange.new_Str(cpp.Str.new_CPtr(lit("oor"))), cpp.LengthError.new_str("len"), cpp.DomainError.new_str("dom"), cpp.RangeError.new_str("range"), cpp.OverflowError.new_str("over"), new cpp.BadAlloc(), new cpp.BadOptionalAccess(), new cpp.BadFunctionCall(), new cpp.BadVariantAccess()];
	let sbx = "";
	for (const ex of es) sbx += ex.what().toString() + "|" + b(ex instanceof cpp.LogicError) + b(ex instanceof cpp.RuntimeError) + " ";
	P("exc", sbx);
	P("excMsg", cpp.RuntimeError.new_str("msg here").message);
}

function extra(): void {
	const M = cpp.M;
	const big = (x: bigint): L => (x >= -9007199254740991n && x <= 9007199254740991n) ? Number(x) : x;
	const vl_ = cpp.Vec.new_TA<L>([-9223372036854775808n, 1n << 60n, -5], mkD);
	const vlc = vl_.Clone(); vlc.push_back(9223372036854775807n);
	P("vecLong", vl(vl_) + " | " + vl(vlc) + " " + b(cpp.Vec.Eq(vl_, vlc)) + b(cpp.Vec.Eq(vl_, vl_.Clone())));
	P("mLong", M.max_l_l(-9223372036854775808n, -1) + " " + M.min_l_l(1n << 60n, (1n << 60n) + 1n) + " " + M.clamp_l_l_l(9223372036854775807n, 0, 1n << 55n) + " " + M.max_ul_ul(18446744073709551614n, 18446744073709551615n) + " " + M.min_ul_ul(9007199254740993n, big(9007199254740992n)));
	const vd_ = cpp.Vec.new_TA([1, 2, 3, 4, 5], mkD);
	vd_.resize_ul(2); vd_.resize_ul(4); P("regrow", vd(vd_) + " " + d(vd_.a[4]));
	vd_.pop_back(); vd_.erase_IterT(vd_.begin()); P("popErase", vd(vd_) + " " + d(vd_.a[2]) + " " + d(vd_.a[3]));
	const vc_ = new cpp.Vec<number>(mkD); vc_.reserve(3); vc_.push_back(1); vc_.push_back(2); vc_.push_back(3); vc_.push_back(4); P("reserveEmpty", vd(vc_));
	vc_.assign_ul_T(2, 9.0); P("assignSmall", vd(vc_) + " " + d(vc_.a[3]));
	vc_.assign_ul_T(10, 8.0); P("assignBig", vd(vc_));
	const vb_ = new cpp.Vec<boolean>(mkB); vb_.resize_ul(3); vb_.a[1] = true; vb_.push_back(true); vb_.clear(); vb_.resize_ul(2);
	P("boolVec", vb_.size() + " " + b(vb_.a[0]) + b(vb_.a[1]) + " " + vb_.capacity());
	const vi2 = new cpp.Vec<number>(mkD); vi2.insert_IterT_TA(vi2.end(), [1, 2, 3]); vi2.insert_IterT_IterT_IterT(I.op_Add(vi2.begin(), 1), vi2.begin(), vi2.end()); P("insSelf", vi(vi2));
	vi2.insert_IterT_ul_T(vi2.begin(), 0, 5); vi2.erase_IterT_IterT(I.op_Add(vi2.begin(), 2), I.op_Add(vi2.begin(), 2)); P("insNone", vi(vi2));
	const mkN = (): Node => new Node();
	const vn = cpp.Vec.new_ul(2, mkN); vn.a[0].id = 1; vn.a[1].id = 2; vn.push_back(new Node(3));
	const vnc = vn.Clone(); const vna = new cpp.Vec<Node>(mkN); vna.Assign_VecT(vn);
	P("nodeVec", vn.a[0].id + " " + vnc.a[0].id + " " + vna.a[2].id + " " + b(cpp.Vec.Eq(vn, vn)) + b(cpp.Vec.Eq(vn, vnc)));
	vn.resize_ul(5); P("nodeResize", vn.size() + " " + vn.a[4].id + " " + b(vn.a[3] !== vn.a[4]));
	vn.resize_ul(1); P("nodeShrink", vn.size() + " " + b(vn.a[1] == null) + " " + vn.capacity());
	const an = cpp.Arr.Make(2, mkN); an[0].id = 7; const anc = cpp.Arr.Clone(an); const and = cpp.Arr.Make(2, mkN); cpp.Arr.Assign(and, an);
	P("nodeArr", anc[0].id + " " + and[0].id + " " + b(an[1] != null));
	const vv = new cpp.Vec<cpp.Vec<number>>((): cpp.Vec<number> => new cpp.Vec<number>(mkD)); vv.push_back(cpp.Vec.new_TA([1, 2], mkD)); const vvc = vv.Clone(); vvc.a[0].push_back(3);
	P("vecVec", vi(vv.a[0]) + " | " + vi(vvc.a[0]));
	const w = cpp.Vec.new_TA([5, 6, 7], mkD);
	let ri = w.rbegin(); ri = cpp.RevIter.op_Increment(ri); P("revDeref", ri.get_Deref() + " " + b(cpp.RevIter.op_Eq(ri, w.rbegin())) + b(cpp.RevIter.op_Ne(ri, w.rend())));
	const r0 = new cpp.ArrRef(w.a, 1); w.push_back(8); r0.set(60); P("staleRef", vi(w));
	w.ref_back().set(80); w.ref_at(0).set(50); P("refBack", vi(w));
	let e0 = w.end(); e0 = I.op_Sub_IterT_l(e0, 1); P("iterMinus", e0.get_Deref() + " " + I.op_Sub_IterT_IterT(e0, w.begin()));
	const pp = cpp.Ptr.new_TA_l(w.a, 3); P("ptrUl", pp.get_Item_ul(18446744073709551615n) + " " + pp.get_Item_l(-2) + " " + cpp.Ptr.op_Add_PtrT_ul(pp, 18446744073709551615n).get_Deref() + " " + cpp.Ptr.op_Sub_PtrT_l(pp, 3).get_Deref());
	const bx = cpp.Box.new_TA_l([1, 2, 3], 2); bx.ref_V().set(30); const hb = new Holder(); const bf = cpp.Box.Of((): cpp.Ref<number> => cpp.fieldRef(hb, "x")); bf.ref_V().set(4.5);
	P("box", d(bx.a[2]) + " " + d(hb.x) + " " + d(bf.ref_V().get()));
	const sp = new cpp.CSpan<number>([1, 2, 3, 4], 1, 3); P("spanNpos", sp.subspan(1).size() + " " + sp.subspan(0, 2).size() + " " + sp.subspan(2, 18446744073709551615n).front() + " " + sp.data().get_Deref());
	const t = cpp.Str.new_CPtr(lit("a,b,,c"));
	P("findNpos", t.find_i_ul(44, 6) + " " + t.rfind_i_ul(44, 3) + " " + t.find_last_of_CPtr_ul(lit(","), 2) + " " + t.find_first_not_of_CPtr_ul(lit("abc,"), 0) + " " + t.find_last_not_of_i_ul(44, NPOS) + " " + t.find_CPtr_ul(lit(""), 0) + " " + t.rfind_CPtr_ul(lit(""), NPOS) + " " + t.rfind_CPtr_ul(lit(",,"), NPOS));
	P("strEdge", s(t.substr(6, 5)) + s(t.substr(2, 0)) + " " + cpp.Str.new_ucA_i_i([0x80], 0, 1).compare_Str(cpp.Str.new_CPtr(lit("\x7F"))) + " " + s(cpp.Str.Cat_Str_Str(new cpp.Str(), new cpp.Str())) + " " + new cpp.Str().size());
	const u = cpp.Str.new_CPtr(lit("abc")); u.append_Str(u); u.insert_ul_Str(1, u); P("selfOps", s(u) + " " + u.size());
	u.erase(2, 3); u.replace_ul_ul_Str(1, 100, cpp.Str.new_CPtr(lit("XY"))); P("eraseReplace", s(u));
	const e = cpp.Str.new_CPtr(lit("xyz")); e.resize(1); e.resize(3, 113); P("resizeKeep", s(e) + " " + e.b.length);
	const cp = e.c_str(); e.push_back(33); P("cstrAfter", s(cpp.Str.new_CPtr(cp)) + " " + s(e));
	const td = cpp.Str.to_string_d;
	P("toStrD", s(td(0.0078125)) + s(td(0.0234375)) + s(td(1e-7)) + s(td(5e-7)) + s(td(-5e-7)) + s(td(123456789.123456789)) + s(td(Infinity)) + s(td(-Infinity)) + s(td(5e-324)) + s(td(0.5)) + s(td(2.0000005)) + s(td(9007199254740993.0)));
	P("toStrBig", s(td(1.7976931348623157e308)));
	P("toStrL", s(cpp.Str.to_string_l(big(9007199254740993n))) + s(cpp.Str.to_string_l(big(-9007199254740993n))) + s(cpp.Str.to_string_ul(0)) + s(cpp.Str.to_string_i(-2147483648)));
	const hb2 = new Uint8Array([1, 2, 0xF0, 0]);
	const cq = new cpp.CPtr(hb2, 1);
	P("cptrMore", cq.get(1) + " " + cpp.CPtr.op_Sub(cq, 1).get(0) + " " + b(cpp.CPtr.op_Gt(cq, new cpp.CPtr(hb2, 0))) + b(cpp.CPtr.op_Le(cq, cq)) + b(cpp.CPtr.op_Ge(cq, new cpp.CPtr(hb2, 2))) + " " + cpp.CPtr.Diff(new cpp.CPtr(hb2, 0), cq) + " " + cpp.Str.new_CPtr(lit("été")).size() + " " + lit("ÿ").get(0));
	const o1 = cpp.Opt.new_T(5, mkD); const o2 = new cpp.Opt<number>(mkD); o2.MoveFrom(o1); const o3 = new cpp.Opt<number>(mkD); o3.Assign(new cpp.Opt<number>(mkD));
	P("optMore", o2.value() + " " + b(o1.has) + " " + b(o3.has) + " " + o2.value_or(9) + " " + new cpp.Opt<number>(mkD).value_or(9));
	const on = new cpp.Opt<Node>(mkN); on.emplace(new Node(4)); const onc = on.Clone(); P("optNode", on.val.id + " " + onc.val.id);
	P("pairNum", b(cpp.Pair.Eq(cpp.Pair.new_A_B<L, number>(big(1n << 60n), 0.0), cpp.Pair.new_A_B<L, number>(big(1n << 60n), -0.0))) + " " + cpp.Pair.new_A_B<L, number>(big(1n << 60n), 1).CompareTo(cpp.Pair.new_A_B<L, number>(big(-(1n << 60n)), 2)));
	const sb8 = [-1, 65, 0];
	const cs8 = cpp.CPtr.from_Ptrc(cpp.Ptr.new_TA_l(sb8, 0));
	P("sbyteView", cs8.get(0) + " " + cs8.get(1) + " " + cpp.Str.new_CPtr(cs8).size() + " " + cpp.Str.new_CPtr(cs8).b[0]);
}

vec(); iter(); str(); cptr(); ptr(); misc(); extra();
process.stdout.write(outp.join("\n") + "\n");
