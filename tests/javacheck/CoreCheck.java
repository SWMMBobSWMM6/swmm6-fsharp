// CoreCheck.java - part A of the Java runtime (package cpp: Vec and its specialisations, Iter, RevIter, Ptr, CSpan,
// Opt, Str, CPtr, Pair, M, Arr, Mv, Dtor, Defer, the exceptions) run through the same operations as corecheck.cs runs
// through the C# runtime (cs/runtime). run.sh builds both and requires identical output. Doubles print as their bit
// patterns, booleans as 0/1, strings in brackets, so neither language's number formatting is involved.
// Written by Claude (Anthropic) for the SWMM6 ports; see AI-USE.md.
import cpp.*;

public class CoreCheck {
	static final StringBuilder out = new StringBuilder();
	/** C#'s double.NaN (the x86 default NaN, sign bit set), as the translator writes it. */
	static final double NAN = Double.longBitsToDouble(0xFFF8000000000000L);
	static void p(String tag, Object v) { out.append(tag).append('=').append(v).append('\n'); }
	static String d(double x) { return Long.toHexString(Double.doubleToRawLongBits(x)); }
	static String b(boolean x) { return x ? "1" : "0"; }
	static String s(Str x) { return "[" + x + "]"; }
	static String u(long x) { return Long.toUnsignedString(x); }
	static String vd(VecD v) { StringBuilder sb = new StringBuilder(); sb.append(v.size()).append('/').append(v.capacity()).append(':'); for (int i = 0; i < v.n; i++) sb.append(' ').append(d(v.a[i])); return sb.toString(); }
	static String vi(VecI v) { StringBuilder sb = new StringBuilder(); sb.append(v.size()).append('/').append(v.capacity()).append(':'); for (int i = 0; i < v.n; i++) sb.append(' ').append(v.a[i]); return sb.toString(); }
	static String vs(Vec<Str> v) { StringBuilder sb = new StringBuilder(); sb.append(v.size()).append('/').append(v.capacity()).append(':'); for (int i = 0; i < v.n; i++) sb.append(' ').append(s((Str) v.a[i])); return sb.toString(); }
	static String hex(byte[] a, int n) { StringBuilder sb = new StringBuilder(); for (int i = 0; i < n; i++) sb.append(String.format("%02x", a[i] & 0xFF)); return sb.toString(); }
	static CPtr lit(String x) { byte[] a = new byte[x.length() + 1]; for (int i = 0; i < x.length(); i++) a[i] = (byte) x.charAt(i); return new CPtr(a, 0); }

	static final class Holder { double x; int k; }
	static RefAtD atX() { return (Object o) -> new RefD() { public double get() { return ((Holder) o).x; } public void set(double v) { ((Holder) o).x = v; } }; }
	static RefAtD atX2() { return (Object o) -> new RefD() { public double get() { return ((Holder) o).x; } public void set(double v) { ((Holder) o).x = v; } }; }
	static final class Res implements IDisposable { final String name; Res(String n) { name = n; } public void Dispose() { p("dispose", name); } }

	public static void main(String[] args) {
		vec(); iter(); str(); cptr(); ptr(); misc();
		System.out.print(out);
	}

	static void vec() {
		VecD v = new VecD();
		StringBuilder caps = new StringBuilder();
		for (int i = 0; i < 20; i++) { v.push_back(i + 0.5); caps.append(v.capacity()).append(' '); }
		p("grow", caps);
		v.resize_ul(5); p("resize5", vd(v));
		v.resize_ul(9); p("resize9", vd(v));
		v.resize_ul_T(12, 7.5); p("resize12", vd(v));
		VecD w = new VecD(new double[] { 10, 20, 30 });
		v.insert_IterT_T(IterD.op_Add(v.begin(), 2), 99); p("insT", vd(v));
		v.insert_IterT_TA(IterD.op_Add(v.begin(), 1), new double[] { 1, 2, 3 }); p("insTA", vd(v));
		v.insert_IterT_IterT_IterT(v.end(), w.begin(), w.end()); p("insII", vd(v));
		v.insert_IterT_ul_T(v.begin(), 3, -1); p("insN", vd(v));
		double[] raw = { 5, 6, 7, 8 };
		v.insert_IterT_PtrT_PtrT(IterD.op_Add(v.begin(), 4), new PtrD(raw, 1), new PtrD(raw, 3)); p("insP", vd(v));
		IterD r = v.erase_IterT(IterD.op_Add(v.begin(), 3)); p("erase1", vd(v) + " @" + r.i);
		r = v.erase_IterT_IterT(IterD.op_Add(v.begin(), 1), IterD.op_Add(v.begin(), 4)); p("erase3", vd(v) + " @" + r.i);
		v.reserve(100); p("reserve", vd(v));
		v.shrink_to_fit(); p("shrink", vd(v));
		v.assign_ul_T(3, 2.0); p("assignN", vd(v));
		v.assign_IterT_IterT(IterD.op_Add(w.begin(), 1), w.end()); p("assignII", vd(v));
		v.pop_back(); p("pop", vd(v));
		v.swap(w); p("swapV", vd(v)); p("swapW", vd(w));
		VecD c = v.Clone(); p("clone", vd(c));
		VecD e = new VecD(); e.reserve(10); e.Assign_VecT(v); p("assign", vd(e));
		VecD m = new VecD(); m.MoveFrom_VecT(c); p("moveTo", vd(m)); p("moveFrom", vd(c));
		v.clear(); p("clear", vd(v) + " " + b(v.empty()));
		p("eqNaN", b(VecD.Eq(new VecD(new double[] { NAN, 0.0 }), new VecD(new double[] { NAN, -0.0 }))));
		p("eqLen", b(VecD.Eq(new VecD(2), new VecD(3))));
		p("ctorN", vd(new VecD(4, 1.25)));
		try { w.at(7); } catch (OutOfRange x) { p("at", x.what()); }
		try { w.at(-1L); } catch (OutOfRange x) { p("at", x.what()); }
		VecI vi = new VecI(new int[] { 1, 2, 3 });
		p("vi", vi(vi)); vi.push_back(4); p("vi4", vi(vi));
		VecB vb = new VecB(new byte[] { (byte) 200, 1 });
		p("eqB", b(VecB.Eq(vb, new VecB(new byte[] { (byte) 200, 1 }))) + b(VecB.Eq(vb, new VecB(new byte[] { (byte) 201, 1 }))));
		VecZ vz = new VecZ(3, true); vz.resize_ul(5); StringBuilder zs = new StringBuilder(); for (int i = 0; i < vz.n; i++) zs.append(b(vz.a[i])); p("vz", zs);

		// a vector of strings: elements made by the maker, copied with the vector
		Vec<Str> sv = new Vec<Str>(3, () -> new Str());
		((Str) sv.a[1]).append_CPtr(lit("one"));
		sv.push_back(new Str("two"));
		Vec<Str> sc = sv.Clone();
		((Str) sc.a[1]).append_CPtr(lit("!"));
		p("strVec", vs(sv)); p("strClone", vs(sc));
		p("strEq", b(Vec.Eq(sv, sv.Clone())) + b(Vec.Eq(sv, sc)));
		sv.resize_ul(6); p("strResize", vs(sv));
		sv.insert_IterT_IterT_IterT(Iter.op_Add(sv.begin(), 1L), sc.begin(), Iter.op_Add(sc.begin(), 2L));
		((Str) sc.a[1]).append_CPtr(lit("?"));
		p("strIns", vs(sv));
		sv.erase_IterT_IterT(sv.begin(), Iter.op_Add(sv.begin(), 2L)); p("strErase", vs(sv));
		// vector<array<double, 3>>: the copy is deep, and a vector without a maker takes the source's
		Vec<double[]> va = new Vec<double[]>(2, () -> new double[3]);
		va.a[0] = new double[] { 1, 2, 3 };
		Vec<double[]> vc = va.Clone();
		((double[]) vc.a[0])[1] = 42;
		p("arrVec", d(((double[]) va.a[0])[1]) + " " + d(((double[]) vc.a[0])[1]));
		Vec<double[]> none = new Vec<double[]>(() -> null);
		none.Assign_VecT(new Vec<double[]>(() -> new double[3]));
		none.resize_ul(2);
		p("mkAdopt", ((double[]) none.a[1]).length);
		Vec<double[]> none2 = new Vec<double[]>(() -> null);
		none2.MoveFrom_VecT(new Vec<double[]>(() -> new double[4]));
		none2.resize_ul(1);
		p("mkMove", ((double[]) none2.a[0]).length);
	}

	static void iter() {
		VecI w = new VecI(new int[] { 1, 2, 3 });
		IterI it = w.begin();
		w.push_back(4);
		w.a[0] = 100;
		p("staleIter", it.get_Deref() + " " + IterI.op_Add(it, 0L).get_Deref());
		p("iterSub", IterI.op_Sub_IterT_IterT(w.end(), w.begin()) + " " + IterI.op_Sub_IterT_l(w.end(), 2L).get_Deref());
		p("iterCmp", b(IterI.op_Lt(w.begin(), w.end())) + b(IterI.op_Eq(IterI.op_Increment(w.begin()), IterI.op_Add(w.begin(), 1L))) + b(IterI.op_Ne(w.begin(), w.end())));
		StringBuilder sb = new StringBuilder();
		for (RevIterI ri = w.rbegin(); !RevIterI.op_Eq(ri, w.rend()); ri = RevIterI.op_Increment(ri)) sb.append(ri.get_Deref()).append(' ');
		p("rev", sb);
		IterI bi = new IterI(new int[] { 7, 8, 9 }, 1L);
		p("bareIter", bi.get_Deref() + " " + IterI.op_Add(bi, 1L).get_Deref() + " " + bi.get_Item(-1L));
		PtrI fromIt = PtrI.from_IterT(IterI.op_Add(w.begin(), 2L));
		p("ptrFromIter", fromIt.get_Deref() + " " + fromIt.o);
		CSpanD sp = new CSpanD(new double[] { 1, 2, 3, 4, 5 }, 1L, 3L);
		p("span", sp.size() + " " + d(sp.front()) + " " + d(sp.back()) + " " + d(sp.get_Item(1L)) + " " + sp.subspan(1L, -1L).size() + " " + d(sp.subspan(1L, -1L).front()) + " " + d(sp.last(2L).front()) + " " + sp.first(2L).size() + " " + b(CSpanD.ZERO.empty()));
	}

	static void str() {
		Str s = new Str(lit("Hello, World"));
		p("size", s.size());
		p("find", s.find_CPtr_ul(lit("o"), 0L) + " " + s.find_i_ul('o', 5L) + " " + u(s.find_i_ul('z', 0L)) + " " + s.find_Str_ul(new Str(lit("World")), 0L) + " " + u(s.find_CPtr_ul(lit("World"), 8L)));
		p("findEmpty", s.find_CPtr_ul(lit(""), 12L) + " " + u(s.find_CPtr_ul(lit(""), 13L)));
		p("rfind", s.rfind_i_ul('o', Str.npos) + " " + s.rfind_CPtr_ul(lit("o"), 5L) + " " + s.rfind_CPtr_ul(lit("l"), Str.npos) + " " + u(s.rfind_CPtr_ul(lit("Hello, World!"), Str.npos)) + " " + u(new Str().rfind_i_ul('a', Str.npos)));
		p("firstOf", s.find_first_of_CPtr_ul(lit(", "), 0L) + " " + s.find_first_not_of_CPtr_ul(lit("Hel"), 0L) + " " + s.find_first_not_of_i_ul('H', 0L) + " " + u(s.find_first_of_CPtr_ul(lit("xyz"), 0L)));
		p("lastOf", s.find_last_of_CPtr_ul(lit("lo"), Str.npos) + " " + s.find_last_not_of_CPtr_ul(lit("dl"), Str.npos) + " " + s.find_last_of_i_ul('o', 6L) + " " + s.find_last_not_of_i_ul('d', Str.npos) + " " + s.find_last_of_CPtr_ul(lit("H"), 3L));
		p("substr", s(s.substr(7L, Str.npos)) + s(s.substr(0L, 5L)) + s(s.substr(12L, 3L)));
		try { s.substr(13L, 1L); } catch (OutOfRange x) { p("substrErr", x.what()); }
		try { s.at(12L); } catch (OutOfRange x) { p("atErr", x.what()); }
		Str t = s.Clone();
		t.erase(5L, 2L); p("erase", s(t));
		t.insert_ul_CPtr(5L, lit(" there")); p("insert", s(t));
		t.replace_ul_ul_CPtr(0L, 5L, lit("Bye")); p("replace", s(t));
		t.append_i('!'); t.append_ul_i(3L, '?'); t.push_back('#'); p("append", s(t));
		t.pop_back(); t.resize(24L, '.'); p("resize", s(t) + " " + t.size());
		t.resize(4L, 0); p("resize4", s(t));
		t.erase(1L, Str.npos); p("eraseAll", s(t));
		t.insert_ul_ul_i(1L, 2L, 'x'); t.insert_ul_Str(0L, new Str(lit(">>"))); p("insertN", s(t));
		p("compare", new Str(lit("abc")).compare_CPtr(lit("abd")) + " " + new Str(lit("ab")).compare_CPtr(lit("abc")) + " " + new Str(lit("abc")).compare_CPtr(lit("ab")) + " " + new Str(new byte[] { (byte) 0xFF }, 0, 1).compare_CPtr(lit("a")) + " " + new Str(lit("abc")).compare_Str(new Str(lit("abc"))) + " " + s.compare_ul_ul_Str(7L, 5L, new Str(lit("World"))));
		p("startsEnds", b(s.starts_with_CPtr(lit("Hell"))) + b(s.starts_with_Str(new Str(lit("ello")))) + b(s.starts_with_i('H')) + b(s.ends_with_CPtr(lit("World"))) + b(s.ends_with_i('x')) + b(s.contains_Str(new Str(lit("o, W")))) + b(s.contains_i('#')));
		p("cat", s(Str.Cat_Str_CPtr(s, lit("!"))) + s(Str.Cat_CPtr_Str(lit("<"), s)) + s(Str.Cat_Str_Str(s, s)) + s(Str.Cat_Str_i(s, '?')) + s(Str.Cat_i_Str('^', s)));
		p("toString", s(Str.to_string_i(-42)) + s(Str.to_string_ul(-1L)) + s(Str.to_string_u(-1)) + s(Str.to_string_l(Long.MIN_VALUE)) + s(Str.to_string_d(3.14159)) + s(Str.to_string_d(-0.0)) + s(Str.to_string_d(1e20)) + s(Str.to_string_d(2.5e-7)) + s(Str.to_string_f(0.1f)));
		PtrL idx = new PtrL(new long[1], 0L);
		p("stod", d(Str.stod(new Str(lit("  12.5e1xyz")), idx)) + " " + idx.get_Deref() + " " + d(Str.stod(new Str(lit("-0.25")), PtrL.ZERO)));
		try { Str.stod(new Str(lit("abc")), PtrL.ZERO); } catch (InvalidArgument x) { p("stodErr", x.what()); }
		p("stoi", Str.stoi(new Str(lit(" -17abc")), idx, 10) + " " + idx.get_Deref() + " " + Str.stol(new Str(lit("99999999999")), PtrL.ZERO, 10));
		try { Str.stoi(new Str(lit("99999999999")), PtrL.ZERO, 10); } catch (OutOfRange x) { p("stoiErr", x.what()); }
		Str lat = new Str("café€!");
		p("latin1", hex(lat.b, lat.n));
		Str g = new Str(); for (int i = 0; i < 40; i++) g.push_back('a' + i % 26);
		p("grow", g.b.length + " " + s(g));
		CPtr cs = g.c_str(); p("cstr", b(cs.B == g.b) + " " + g.b[g.n]);
		Str h = new Str(); h.MoveFrom(g); p("move", s(h) + s(g) + " " + g.b.length);
		h.Assign_CPtr(lit("assigned")); p("assignC", s(h) + " " + h.b.length);
		h.Assign_Str(new Str(lit("xy"))); p("assignS", s(h) + " " + h.b.length);
		h.Assign_i('Q'); p("assignI", s(h));
		p("eq", b(h.EqLit(lit("Q"))) + b(h.EqLit(lit("QQ"))) + b(h.Equals_Str(new Str(lit("Q")))) + b(h.Equals_Str(null)));
		p("hash", new Str(lit("Hello, World")).hashCode() + " " + new Str(new byte[] { (byte) 0xE9, 'a' }, 0, 2).hashCode());
		Str v = new Str(lit("  view  ")); v.remove_prefix(2L); v.remove_suffix(2L); p("view", s(v));
		Str a = new Str(); a.assign_CPtr_CPtr(CPtr.op_Add_CPtr_i(lit("abcdef"), 1), CPtr.op_Add_CPtr_i(lit("abcdef"), 4)); p("assignRange", s(a));
		byte[] raw = { 'q', 'r', 's', 't' };
		a.assign_Ptrc_Ptrc(new PtrB(raw, 1L), new PtrB(raw, 3L)); p("assignPtr", s(a));
		a.assign_ul_i(3L, 'z'); p("assignN", s(a));
		a.append_Str_ul_ul(new Str(lit("0123456")), 2L, 3L); a.append_CPtr_ul(lit("ABCDEF"), 2L); p("appendSub", s(a));
		p("ctors", s(new Str(5L, '-')) + s(new Str(lit("abcdef"), 3L)) + s(new Str(new Str(lit("abcdef")), 2L, 10L)) + s(Str.from_CPtr(lit("conv"))));
		IterB bi = s.begin(); p("strIter", (char) bi.get_Deref() + " " + IterB.op_Sub_IterT_IterT(s.end(), bi));
	}

	static void cptr() {
		byte[] buf = new byte[8];
		CPtr p = new CPtr(buf, 0);
		p.set(0, 200);
		int r1 = p.assign(1, 200);
		p.set(2, 127);
		int r2 = p.preInc(2);
		p.set(3, -128);
		int r3 = p.postDec(3);
		p.ref(4).set(300);
		int r4 = p.postInc(5);
		int r5 = p.preDec(6);
		p("cptrElems", p.get(0) + " " + r1 + " " + p.get(1) + " " + r2 + " " + p.get(2) + " " + r3 + " " + p.get(3) + " " + p.get(4) + " " + r4 + " " + p.get(5) + " " + r5 + " " + p.get(6) + " " + p.ref(4).get() + " " + p.get(4L));
		CPtr q = CPtr.op_Add_CPtr_i(p, 3);
		p("cptrArith", CPtr.Diff(q, p) + " " + b(CPtr.op_Lt(p, q)) + b(CPtr.op_Eq(q, CPtr.op_Add_CPtr_l(p, 3L))) + b(CPtr.op_Ne(p, new CPtr(new byte[8], 0))) + b(CPtr.op_Eq(CPtr.ZERO, new CPtr(null, 5))) + b(CPtr.ZERO.get_IsNull()) + b(p.get_IsNull()) + " " + CPtr.op_Increment(q).O + " " + CPtr.op_Decrement(q).O + " " + CPtr.op_Sub(q, 2).O + " " + CPtr.op_Add_CPtr_ul(q, 4L).O);
		p("cptrStr", CPtr.op_Add_CPtr_i(lit("text"), 1).toString() + " " + CPtr.ZERO.toString());
		PtrB pb = new PtrB(new byte[] { 'a', 'b', 'c', 0 }, 1L);
		p("fromPtr", CPtr.from_Ptrc(pb).toString() + " " + CPtr.from_Ptruc(pb).get(1));
	}

	static void ptr() {
		double[] arr = { 1, 2, 3, 4, 5 };
		PtrD p = new PtrD(arr, 1L);
		p("ptrIdx", d(p.get_Item_i(1)) + " " + d(p.get_Item_l(2L)) + " " + d(p.get_Deref()) + " " + d(PtrD.op_Add_PtrT_l(p, 3L).get_Deref()) + " " + d(PtrD.op_Sub_PtrT_l(p, 1L).get_Deref()));
		p.ref_Item_i(0).set(20.0); PtrD.op_Increment(p).set_Deref(30);
		p("ptrSet", d(arr[1]) + " " + d(arr[2]));
		PtrD q = PtrD.op_Add_PtrT_l(p, 2L);
		p("ptrCmp", PtrD.op_Sub_PtrT_PtrT(q, p) + " " + b(PtrD.op_Eq(q, new PtrD(arr, 3L))) + b(PtrD.op_Eq(q, new PtrD(new double[5], 3L))) + b(PtrD.op_Lt(p, q)) + b(PtrD.op_Eq(PtrD.ZERO, new PtrD((double[]) null, 7L))) + b(PtrD.ZERO.get_IsNull()) + b(PtrD.op_Ne(p, q)));
		Holder h = new Holder(); h.x = 1.5;
		RefAtD at = atX();
		PtrD f1 = new PtrD(h, at), f2 = new PtrD(h, at), f3 = new PtrD(h, atX2()), f4 = new PtrD(new Holder(), at);
		f1.ref_Deref().set(2.5);
		p("fieldPtr", d(h.x) + " " + d(f2.get_Deref()) + " " + d(PtrD.op_Add_PtrT_l(f1, 3L).get_Item_i(9)) + " " + b(f1.get_IsNull()));
		p("fieldEq", b(PtrD.op_Eq(f1, f2)) + b(PtrD.op_Eq(f1, f3)) + b(PtrD.op_Eq(f1, f4)) + b(PtrD.op_Eq(f1, PtrD.op_Add_PtrT_l(f2, 1L))) + b(PtrD.op_Eq(f1, p)));
		Ptr<Str> ps = new Ptr<Str>(new Str[] { new Str(lit("a")), new Str(lit("b")) }, 0L);
		p("ptrGen", s(Ptr.op_Add_PtrT_l(ps, 1L).get_Deref()) + " " + b(Ptr.<Str>zero().get_IsNull()) + b(Ptr.op_Ne(ps, Ptr.op_Add_PtrT_l(ps, 1L))));
	}

	static void misc() {
		p("min", d(M.min_d_d(0.0, -0.0)) + " " + d(M.min_d_d(-0.0, 0.0)) + " " + d(M.max_d_d(-0.0, 0.0)) + " " + d(M.min_d_d(NAN, 1)) + " " + d(M.min_d_d(1, NAN)) + " " + d(M.max_d_d(NAN, 1)) + " " + d(M.max_d_d(1, NAN)));
		p("clamp", d(M.clamp_d_d_d(NAN, 0, 1)) + " " + d(M.clamp_d_d_d(-0.0, 0.0, 1)) + " " + d(M.clamp_d_d_d(5, 0, 1)) + " " + M.clamp_i_i_i(-5, 0, 10));
		p("unsigned", u(M.max_ul_ul(-1L, 1L)) + " " + u(M.min_ul_ul(-1L, 1L)) + " " + Integer.toUnsignedString(M.max_u_u(-1, 1)) + " " + (M.min_uc_uc((byte) 200, (byte) 100) & 0xFF) + " " + (M.max_uc_uc((byte) 200, (byte) 100) & 0xFF) + " " + M.max_i_i(-3, 2) + " " + M.min_l_l(-3L, 2L));
		p("listMinMax", d(M.max_dA(new double[] { 1, NAN, 3 })) + " " + d(M.min_dA(new double[] { NAN, 1, -2 })) + " " + M.max_iA(new int[] { 4, 9, 2 }) + " " + M.min_iA(new int[] { 4, 9, 2 }));
		Pair<Integer, Str> p1 = new Pair<Integer, Str>(1, new Str(lit("b"))), p2 = new Pair<Integer, Str>(1, new Str(lit("a")));
		p("pairCmp", p1.CompareTo(p2) + " " + p2.CompareTo(p1) + " " + p1.CompareTo(p1.Clone()) + " " + b(Pair.op_Lt(p2, p1)) + " " + b(Pair.Eq(p1, p1.Clone())) + b(Pair.Eq(p1, p2)));
		Pair<Double, Integer> n1 = new Pair<Double, Integer>(NAN, 1), n2 = new Pair<Double, Integer>(0.0, 1);
		p("pairNaN", n1.CompareTo(n2) + " " + n2.CompareTo(n1) + " " + n1.CompareTo(new Pair<Double, Integer>(NAN, 2)) + " " + new Pair<Double, Integer>(-0.0, 1).CompareTo(n2) + " " + b(Pair.Eq(n1, new Pair<Double, Integer>(NAN, 1))));
		Pair<Integer, Str> pc = p1.Clone(); pc.second.append_CPtr(lit("!")); p("pairClone", s(p1.second) + s(pc.second));
		Pair<Str, Str> pm = new Pair<Str, Str>(() -> new Str(), () -> new Str()); pm.first.append_CPtr(lit("m")); p("pairMake", s(pm.first) + s(pm.second));
		p2.Assign(p1); p1.second.append_CPtr(lit("+")); p("pairAssign", s(p2.second));

		OptD o = new OptD();
		try { o.value(); } catch (BadOptionalAccess x) { p("optErr", x.what()); }
		p("opt", d(o.value_or(9)) + " " + b(o.has_value()));
		o.emplace(3.5); OptD oc = o.Clone(); o.reset();
		p("opt2", d(oc.value()) + " " + b(o.has) + " " + d(o.val));
		Opt<Str> os = new Opt<Str>(new Str(lit("x"))); Opt<Str> osc = os.Clone(); osc.val.append_CPtr(lit("y"));
		Opt<Str> osa = new Opt<Str>(); osa.Assign(os); os.val.append_CPtr(lit("z"));
		p("optStr", s(os.val) + s(osc.val) + s(osa.val));

		double[] da = Arr.Make__D(3); da[1] = NAN;
		double[] db = Arr.Clone__D(da);
		p("arrD", b(Arr.Eq__D(da, db)) + " " + b(db != da) + " " + Arr.Make2__I(2, 3)[1].length);
		Str[] sa = Arr.Make(2, () -> new Str(lit("s")));
		Str[] sb = Arr.Clone(sa); sb[0].append_CPtr(lit("!"));
		Str[] sd = Arr.Make(2, () -> new Str()); Arr.Assign(sd, sa); sa[1].append_CPtr(lit("?"));
		p("arrStr", s(sa[0]) + s(sb[0]) + s(sd[1]) + s(sa[1]) + " " + sa.getClass().getSimpleName());
		double[][] grid = { { 1, 2 }, { 3, 4 } };
		double[][] gc = Arr.Clone(grid); gc[0][0] = 9;
		p("arrShallow", d(grid[0][0]));
		Vec<double[][]> vg = new Vec<double[][]>(() -> null); vg.push_back(grid); Vec<double[][]> vgc = vg.Clone(); ((double[][]) vgc.a[0])[1][1] = 7;
		p("vecDeep", d(grid[1][1]));

		RefO.Cell<Str> src = new RefO.Cell<Str>(new Str(lit("taken")));
		Str got = Mv.Take(src, () -> new Str());
		p("take", s(got) + s(src.v));
		RefO.Cell<Str> src2 = new RefO.Cell<Str>(new Str(lit("ref")));
		p("takeRef", s(Mv.TakeRef(src2)) + " " + b(src2.v == null));
		RefD.Cell x = new RefD.Cell(1);
		double[] box = { 6.5 };
		p("back", d(Mv.Back__DD(2.5, x, box)) + " " + d(x.v));
		RefO.Cell<Res> holder = new RefO.Cell<Res>(new Res("first"));
		Mv.Reset(holder, new Res("second"));
		Mv.Reset(holder, holder.v);
		Mv.Reset(holder, null);
		Mv.Delete(new Res("deleted"));
		Dtor.Run(new Object[] { new Res("arr0"), null, new Res("arr1") });
		RefO.Cell<PtrD> pp = new RefO.Cell<PtrD>(new PtrD(new double[] { 1, 2, 3 }, 0L));
		PtrD before = Mv.PostStep_PtrTr_i__D(pp, 1);
		p("postStep", d(before.get_Deref()) + " " + d(pp.v.get_Deref()));
		Defer df = new Defer(() -> p("defer", "ran"));
		df.Dispose(); df.Dispose();
		Defer dc = new Defer(() -> p("defer", "cancelled?")); dc.Cancel(); dc.Dispose();

		CppException[] es = { new CppException(), new LogicError(new Str(lit("logic"))), new RuntimeError("runtime"), new InvalidArgument("inv"), new OutOfRange(new Str(lit("oor"))), new LengthError("len"), new DomainError("dom"), new RangeError("range"), new OverflowError("over"), new BadAlloc(), new BadOptionalAccess(), new BadFunctionCall(), new BadVariantAccess() };
		StringBuilder sbx = new StringBuilder();
		for (CppException e : es) sbx.append(e.what()).append('|').append(b(e instanceof LogicError)).append(b(e instanceof RuntimeError)).append(' ');
		p("exc", sbx);
		p("excMsg", new RuntimeError("msg here").getMessage());
	}
}
