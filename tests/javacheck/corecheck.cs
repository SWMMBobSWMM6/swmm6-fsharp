// corecheck.cs - the C# side of CoreCheck.java: the same operations on the C# runtime (cs/runtime) that CoreCheck runs
// on part A of the Java runtime (package cpp). run.sh requires identical output. Doubles print as their bit patterns,
// booleans as 0/1, strings in brackets.
// Written by Claude (Anthropic) for the SWMM6 ports; see AI-USE.md.
using System;
using System.Text;
using Cpp;

public static class CoreCheck
{
    static readonly StringBuilder outp = new StringBuilder();
    static void P(string tag, object v) { outp.Append(tag).Append('=').Append(v).Append('\n'); }
    static string d(double x) => BitConverter.DoubleToInt64Bits(x).ToString("x");
    static string b(bool x) => x ? "1" : "0";
    static string s(Str x) => "[" + x + "]";
    static string vd(Vec<double> v) { var sb = new StringBuilder(); sb.Append(v.size()).Append('/').Append(v.capacity()).Append(':'); for (int i = 0; i < v.n; i++) sb.Append(' ').Append(d(v.a[i])); return sb.ToString(); }
    static string vi(Vec<int> v) { var sb = new StringBuilder(); sb.Append(v.size()).Append('/').Append(v.capacity()).Append(':'); for (int i = 0; i < v.n; i++) sb.Append(' ').Append(v.a[i]); return sb.ToString(); }
    static string vs(Vec<Str> v) { var sb = new StringBuilder(); sb.Append(v.size()).Append('/').Append(v.capacity()).Append(':'); for (int i = 0; i < v.n; i++) sb.Append(' ').Append(s(v.a[i])); return sb.ToString(); }
    static string hex(byte[] a, int n) { var sb = new StringBuilder(); for (int i = 0; i < n; i++) sb.Append(a[i].ToString("x2")); return sb.ToString(); }
    static CPtr lit(string x) => R.Lit(x);

    sealed class Holder { public double x; public int k; }
    static RefAt<double> atX() => (object? o) => ref ((Holder)o!).x;
    static RefAt<double> atX2() => (object? o) => ref ((Holder)o!).x;
    sealed class Res : IDisposable { readonly string name; public Res(string n) { name = n; } public void Dispose() { P("dispose", name); } }

    public static void Main()
    {
        vec(); iter(); str(); cptr(); ptr(); misc();
        Console.Out.Write(outp.ToString());
    }

    static void vec()
    {
        var v = new Vec<double>();
        var caps = new StringBuilder();
        for (int i = 0; i < 20; i++) { v.push_back(i + 0.5); caps.Append(v.capacity()).Append(' '); }
        P("grow", caps);
        v.resize(5); P("resize5", vd(v));
        v.resize(9); P("resize9", vd(v));
        v.resize(12, 7.5); P("resize12", vd(v));
        var w = new Vec<double>(new double[] { 10, 20, 30 });
        v.insert(v.begin() + 2, 99); P("insT", vd(v));
        v.insert(v.begin() + 1, new double[] { 1, 2, 3 }); P("insTA", vd(v));
        v.insert(v.end(), w.begin(), w.end()); P("insII", vd(v));
        v.insert(v.begin(), 3UL, -1.0); P("insN", vd(v));
        double[] raw = { 5, 6, 7, 8 };
        v.insert(v.begin() + 4, new Ptr<double>(raw, 1), new Ptr<double>(raw, 3)); P("insP", vd(v));
        var r = v.erase(v.begin() + 3); P("erase1", vd(v) + " @" + r.i);
        r = v.erase(v.begin() + 1, v.begin() + 4); P("erase3", vd(v) + " @" + r.i);
        v.reserve(100); P("reserve", vd(v));
        v.shrink_to_fit(); P("shrink", vd(v));
        v.assign(3UL, 2.0); P("assignN", vd(v));
        v.assign(w.begin() + 1, w.end()); P("assignII", vd(v));
        v.pop_back(); P("pop", vd(v));
        v.swap(w); P("swapV", vd(v)); P("swapW", vd(w));
        var c = v.Clone(); P("clone", vd(c));
        var e = new Vec<double>(); e.reserve(10); e.Assign(v); P("assign", vd(e));
        var m = new Vec<double>(); m.MoveFrom(c); P("moveTo", vd(m)); P("moveFrom", vd(c));
        v.clear(); P("clear", vd(v) + " " + b(v.empty()));
        P("eqNaN", b(Vec<double>.Eq(new Vec<double>(new double[] { double.NaN, 0.0 }), new Vec<double>(new double[] { double.NaN, -0.0 }))));
        P("eqLen", b(Vec<double>.Eq(new Vec<double>(2UL), new Vec<double>(3UL))));
        P("ctorN", vd(new Vec<double>(4UL, 1.25)));
        try { w.at(7); } catch (OutOfRange x) { P("at", x.what()); }
        try { w.at(ulong.MaxValue); } catch (OutOfRange x) { P("at", x.what()); }
        var vi_ = new Vec<int>(new int[] { 1, 2, 3 });
        P("vi", vi(vi_)); vi_.push_back(4); P("vi4", vi(vi_));
        var vb = new Vec<byte>(new byte[] { 200, 1 });
        P("eqB", b(Vec<byte>.Eq(vb, new Vec<byte>(new byte[] { 200, 1 }))) + b(Vec<byte>.Eq(vb, new Vec<byte>(new byte[] { 201, 1 }))));
        var vz = new Vec<bool>(3UL, true); vz.resize(5); var zs = new StringBuilder(); for (int i = 0; i < vz.n; i++) zs.Append(b(vz.a[i])); P("vz", zs);

        var sv = new Vec<Str>(3UL, () => new Str());
        sv[1].append(lit("one"));
        sv.push_back(new Str("two"));
        var sc = sv.Clone();
        sc[1].append(lit("!"));
        P("strVec", vs(sv)); P("strClone", vs(sc));
        P("strEq", b(Vec<Str>.Eq(sv, sv.Clone())) + b(Vec<Str>.Eq(sv, sc)));
        sv.resize(6); P("strResize", vs(sv));
        sv.insert(sv.begin() + 1L, sc.begin(), sc.begin() + 2L);
        sc[1].append(lit("?"));
        P("strIns", vs(sv));
        sv.erase(sv.begin(), sv.begin() + 2L); P("strErase", vs(sv));
        var va = new Vec<double[]>(2UL, () => new double[3]);
        va[0] = new double[] { 1, 2, 3 };
        var vc = va.Clone();
        vc[0][1] = 42;
        P("arrVec", d(va[0][1]) + " " + d(vc[0][1]));
        var none = new Vec<double[]>();
        none.Assign(new Vec<double[]>(() => new double[3]));
        none.resize(2);
        P("mkAdopt", none[1].Length);
        var none2 = new Vec<double[]>();
        none2.MoveFrom(new Vec<double[]>(() => new double[4]));
        none2.resize(1);
        P("mkMove", none2[0].Length);
    }

    static void iter()
    {
        var w = new Vec<int>(new int[] { 1, 2, 3 });
        var it = w.begin();
        w.push_back(4);
        w[0] = 100;
        P("staleIter", it.Deref + " " + (it + 0L).Deref);
        var inc = w.begin(); ++inc;
        P("iterSub", (w.end() - w.begin()) + " " + (w.end() - 2L).Deref);
        P("iterCmp", b(w.begin() < w.end()) + b(inc == w.begin() + 1L) + b(w.begin() != w.end()));
        var sb = new StringBuilder();
        for (var ri = w.rbegin(); ri != w.rend(); ++ri) sb.Append(ri.Deref).Append(' ');
        P("rev", sb);
        var bi = new Iter<int>(new int[] { 7, 8, 9 }, 1L);
        P("bareIter", bi.Deref + " " + (bi + 1L).Deref + " " + bi[-1L]);
        Ptr<int> fromIt = w.begin() + 2L;
        P("ptrFromIter", fromIt.Deref + " " + fromIt.o);
        var sp = new CSpan<double>(new double[] { 1, 2, 3, 4, 5 }, 1L, 3L);
        P("span", sp.size() + " " + d(sp.front()) + " " + d(sp.back()) + " " + d(sp[1L]) + " " + sp.subspan(1).size() + " " + d(sp.subspan(1).front()) + " " + d(sp.last(2).front()) + " " + sp.first(2).size() + " " + b(default(CSpan<double>).empty()));
    }

    static void str()
    {
        var s_ = new Str(lit("Hello, World"));
        P("size", s_.size());
        P("find", s_.find(lit("o"), 0) + " " + s_.find('o', 5) + " " + s_.find('z', 0) + " " + s_.find(new Str(lit("World")), 0) + " " + s_.find(lit("World"), 8));
        P("findEmpty", s_.find(lit(""), 12) + " " + s_.find(lit(""), 13));
        P("rfind", s_.rfind('o', Str.npos) + " " + s_.rfind(lit("o"), 5) + " " + s_.rfind(lit("l"), Str.npos) + " " + s_.rfind(lit("Hello, World!"), Str.npos) + " " + new Str().rfind('a', Str.npos));
        P("firstOf", s_.find_first_of(lit(", "), 0) + " " + s_.find_first_not_of(lit("Hel"), 0) + " " + s_.find_first_not_of('H', 0) + " " + s_.find_first_of(lit("xyz"), 0));
        P("lastOf", s_.find_last_of(lit("lo"), Str.npos) + " " + s_.find_last_not_of(lit("dl"), Str.npos) + " " + s_.find_last_of('o', 6) + " " + s_.find_last_not_of('d', Str.npos) + " " + s_.find_last_of(lit("H"), 3));
        P("substr", s(s_.substr(7, Str.npos)) + s(s_.substr(0, 5)) + s(s_.substr(12, 3)));
        try { s_.substr(13, 1); } catch (OutOfRange x) { P("substrErr", x.what()); }
        try { s_.at(12); } catch (OutOfRange x) { P("atErr", x.what()); }
        var t = s_.Clone();
        t.erase(5, 2); P("erase", s(t));
        t.insert(5, lit(" there")); P("insert", s(t));
        t.replace(0, 5, lit("Bye")); P("replace", s(t));
        t.append('!'); t.append(3UL, '?'); t.push_back('#'); P("append", s(t));
        t.pop_back(); t.resize(24, '.'); P("resize", s(t) + " " + t.size());
        t.resize(4, 0); P("resize4", s(t));
        t.erase(1, Str.npos); P("eraseAll", s(t));
        t.insert(1, 2UL, 'x'); t.insert(0, new Str(lit(">>"))); P("insertN", s(t));
        P("compare", new Str(lit("abc")).compare(lit("abd")) + " " + new Str(lit("ab")).compare(lit("abc")) + " " + new Str(lit("abc")).compare(lit("ab")) + " " + new Str(new byte[] { 0xFF }, 0, 1).compare(lit("a")) + " " + new Str(lit("abc")).compare(new Str(lit("abc"))) + " " + s_.compare(7, 5, new Str(lit("World"))));
        P("startsEnds", b(s_.starts_with(lit("Hell"))) + b(s_.starts_with(new Str(lit("ello")))) + b(s_.starts_with('H')) + b(s_.ends_with(lit("World"))) + b(s_.ends_with('x')) + b(s_.contains(new Str(lit("o, W")))) + b(s_.contains('#')));
        P("cat", s(Str.Cat(s_, lit("!"))) + s(Str.Cat(lit("<"), s_)) + s(Str.Cat(s_, s_)) + s(Str.Cat(s_, '?')) + s(Str.Cat('^', s_)));
        P("toString", s(Str.to_string(-42)) + s(Str.to_string(ulong.MaxValue)) + s(Str.to_string(uint.MaxValue)) + s(Str.to_string(long.MinValue)) + s(Str.to_string(3.14159)) + s(Str.to_string(-0.0)) + s(Str.to_string(1e20)) + s(Str.to_string(2.5e-7)) + s(Str.to_string(0.1f)));
        var idx = new Ptr<ulong>(new ulong[1], 0L);
        P("stod", d(Str.stod(new Str(lit("  12.5e1xyz")), idx)) + " " + idx.Deref + " " + d(Str.stod(new Str(lit("-0.25")))));
        try { Str.stod(new Str(lit("abc"))); } catch (InvalidArgument x) { P("stodErr", x.what()); }
        P("stoi", Str.stoi(new Str(lit(" -17abc")), idx, 10) + " " + idx.Deref + " " + Str.stol(new Str(lit("99999999999"))));
        try { Str.stoi(new Str(lit("99999999999"))); } catch (OutOfRange x) { P("stoiErr", x.what()); }
        var lat = new Str("café€!");
        P("latin1", hex(lat.b, lat.n));
        var g = new Str(); for (int i = 0; i < 40; i++) g.push_back('a' + i % 26);
        P("grow", g.b.Length + " " + s(g));
        var cs = g.c_str(); P("cstr", b(ReferenceEquals(cs.B, g.b)) + " " + g.b[g.n]);
        var h = new Str(); h.MoveFrom(g); P("move", s(h) + s(g) + " " + g.b.Length);
        h.Assign(lit("assigned")); P("assignC", s(h) + " " + h.b.Length);
        h.Assign(new Str(lit("xy"))); P("assignS", s(h) + " " + h.b.Length);
        h.Assign('Q'); P("assignI", s(h));
        P("eq", b(h.EqLit(lit("Q"))) + b(h.EqLit(lit("QQ"))) + b(h.Equals(new Str(lit("Q")))) + b(h.Equals((Str?)null)));
        P("hash", new Str(lit("Hello, World")).GetHashCode() + " " + new Str(new byte[] { 0xE9, (byte)'a' }, 0, 2).GetHashCode());
        var v = new Str(lit("  view  ")); v.remove_prefix(2); v.remove_suffix(2); P("view", s(v));
        var a = new Str(); a.assign(lit("abcdef") + 1, lit("abcdef") + 4); P("assignRange", s(a));
        sbyte[] raw = { (sbyte)'q', (sbyte)'r', (sbyte)'s', (sbyte)'t' };
        a.assign(new Ptr<sbyte>(raw, 1), new Ptr<sbyte>(raw, 3)); P("assignPtr", s(a));
        a.assign(3UL, 'z'); P("assignN", s(a));
        a.append(new Str(lit("0123456")), 2, 3); a.append(lit("ABCDEF"), 2UL); P("appendSub", s(a));
        P("ctors", s(new Str(5UL, '-')) + s(new Str(lit("abcdef"), 3UL)) + s(new Str(new Str(lit("abcdef")), 2, 10)) + s((Str)lit("conv")));
        var bi = s_.begin(); P("strIter", (char)bi.Deref + " " + (s_.end() - bi));
    }

    static void cptr()
    {
        var buf = new byte[8];
        var p = new CPtr(buf, 0);
        p[0] = 200;
        int r1 = (p[1] = 200);
        p[2] = 127;
        int r2 = ++p[2];
        p[3] = -128;
        int r3 = p[3]--;
        p[4] = 300;
        int r4 = p[5]++;
        int r5 = --p[6];
        P("cptrElems", p[0] + " " + r1 + " " + p[1] + " " + r2 + " " + p[2] + " " + r3 + " " + p[3] + " " + p[4] + " " + r4 + " " + p[5] + " " + r5 + " " + p[6] + " " + p[4] + " " + p[4]);
        var q = p + 3;
        var q1 = q; ++q1; var q2 = q; --q2;
        P("cptrArith", CPtr.Diff(q, p) + " " + b(p < q) + b(q == p + 3L) + b(p != new CPtr(new byte[8], 0)) + b(CPtr.Null == new CPtr(null!, 5)) + b(CPtr.Null.IsNull) + b(p.IsNull) + " " + q1.O + " " + q2.O + " " + (q - 2).O + " " + (q + 4UL).O);
        P("cptrStr", (lit("text") + 1).ToString() + " " + CPtr.Null.ToString());
        var pb = new sbyte[] { (sbyte)'a', (sbyte)'b', (sbyte)'c', 0 };
        CPtr fromC = new Ptr<sbyte>(pb, 1L);
        CPtr fromUc = new Ptr<byte>(new byte[] { (byte)'a', (byte)'b', (byte)'c', 0 }, 1L);
        P("fromPtr", fromC.ToString() + " " + fromUc[1]);
    }

    static void ptr()
    {
        double[] arr = { 1, 2, 3, 4, 5 };
        var p = new Ptr<double>(arr, 1L);
        P("ptrIdx", d(p[1]) + " " + d(p[2L]) + " " + d(p.Deref) + " " + d((p + 3L).Deref) + " " + d((p - 1L).Deref));
        p[0] = 20; var pi = p; ++pi; pi.Deref = 30;
        P("ptrSet", d(arr[1]) + " " + d(arr[2]));
        var q = p + 2L;
        P("ptrCmp", (q - p) + " " + b(q == new Ptr<double>(arr, 3L)) + b(q == new Ptr<double>(new double[5], 3L)) + b(p < q) + b(default(Ptr<double>) == new Ptr<double>((double[])null!, 7L)) + b(default(Ptr<double>).IsNull) + b(p != q));
        var h = new Holder(); h.x = 1.5;
        var at = atX();
        Ptr<double> f1 = new Ptr<double>(h, at), f2 = new Ptr<double>(h, at), f3 = new Ptr<double>(h, atX2()), f4 = new Ptr<double>(new Holder(), at);
        f1.Deref = 2.5;
        P("fieldPtr", d(h.x) + " " + d(f2.Deref) + " " + d((f1 + 3L)[9]) + " " + b(f1.IsNull));
        P("fieldEq", b(f1 == f2) + b(f1 == f3) + b(f1 == f4) + b(f1 == f2 + 1L) + b(f1 == p));
        var ps = new Ptr<Str>(new Str[] { new Str(lit("a")), new Str(lit("b")) }, 0L);
        P("ptrGen", s((ps + 1L).Deref) + " " + b(default(Ptr<Str>).IsNull) + b(ps != ps + 1L));
    }

    static void misc()
    {
        P("min", d(M.min(0.0, -0.0)) + " " + d(M.min(-0.0, 0.0)) + " " + d(M.max(-0.0, 0.0)) + " " + d(M.min(double.NaN, 1.0)) + " " + d(M.min(1.0, double.NaN)) + " " + d(M.max(double.NaN, 1.0)) + " " + d(M.max(1.0, double.NaN)));
        P("clamp", d(M.clamp(double.NaN, 0.0, 1.0)) + " " + d(M.clamp(-0.0, 0.0, 1.0)) + " " + d(M.clamp(5.0, 0.0, 1.0)) + " " + M.clamp(-5, 0, 10));
        P("unsigned", M.max(ulong.MaxValue, 1UL) + " " + M.min(ulong.MaxValue, 1UL) + " " + M.max(uint.MaxValue, 1u) + " " + M.min((byte)200, (byte)100) + " " + M.max((byte)200, (byte)100) + " " + M.max(-3, 2) + " " + M.min(-3L, 2L));
        P("listMinMax", d(M.max(new double[] { 1, double.NaN, 3 })) + " " + d(M.min(new double[] { double.NaN, 1, -2 })) + " " + M.max(new int[] { 4, 9, 2 }) + " " + M.min(new int[] { 4, 9, 2 }));
        Pair<int, Str> p1 = new Pair<int, Str>(1, new Str(lit("b"))), p2 = new Pair<int, Str>(1, new Str(lit("a")));
        P("pairCmp", p1.CompareTo(p2) + " " + p2.CompareTo(p1) + " " + p1.CompareTo(p1.Clone()) + " " + b(p2 < p1) + " " + b(Pair<int, Str>.Eq(p1, p1.Clone())) + b(Pair<int, Str>.Eq(p1, p2)));
        Pair<double, int> n1 = new Pair<double, int>(double.NaN, 1), n2 = new Pair<double, int>(0.0, 1);
        P("pairNaN", n1.CompareTo(n2) + " " + n2.CompareTo(n1) + " " + n1.CompareTo(new Pair<double, int>(double.NaN, 2)) + " " + new Pair<double, int>(-0.0, 1).CompareTo(n2) + " " + b(Pair<double, int>.Eq(n1, new Pair<double, int>(double.NaN, 1))));
        var pc = p1.Clone(); pc.second.append(lit("!")); P("pairClone", s(p1.second) + s(pc.second));
        var pm = new Pair<Str, Str>(); pm.first.append(lit("m")); P("pairMake", s(pm.first) + s(pm.second));
        p2.Assign(p1); p1.second.append(lit("+")); P("pairAssign", s(p2.second));

        var o = new Opt<double>();
        try { o.value(); } catch (BadOptionalAccess x) { P("optErr", x.what()); }
        P("opt", d(o.value_or(9)) + " " + b(o.has_value()));
        o.emplace(3.5); var oc = o.Clone(); o.reset();
        P("opt2", d(oc.value()) + " " + b(o.has) + " " + d(o.val));
        var os = new Opt<Str>(new Str(lit("x"))); var osc = os.Clone(); osc.val.append(lit("y"));
        var osa = new Opt<Str>(); osa.Assign(os); os.val.append(lit("z"));
        P("optStr", s(os.val) + s(osc.val) + s(osa.val));

        var da = Arr.Make<double>(3); da[1] = double.NaN;
        var db = Arr.Clone(da);
        P("arrD", b(Arr.Eq(da, db)) + " " + b(!ReferenceEquals(db, da)) + " " + Arr.Make2<int>(2, 3)[1].Length);
        var sa = Arr.MakeWith(2, () => new Str(lit("s")));
        var sb = Arr.Clone(sa); sb[0].append(lit("!"));
        var sd = Arr.Make<Str>(2); Arr.Assign(sd, sa); sa[1].append(lit("?"));
        P("arrStr", s(sa[0]) + s(sb[0]) + s(sd[1]) + s(sa[1]) + " " + sa.GetType().Name);
        double[][] grid = { new double[] { 1, 2 }, new double[] { 3, 4 } };
        var gc = Arr.Clone(grid); gc[0][0] = 9;
        P("arrShallow", d(grid[0][0]));
        var vg = new Vec<double[][]>(); vg.push_back(grid); var vgc = vg.Clone(); vgc[0][1][1] = 7;
        P("vecDeep", d(grid[1][1]));

        Str src = new Str(lit("taken"));
        Str got = Mv.Take(ref src);
        P("take", s(got) + s(src));
        Str? src2 = new Str(lit("ref"));
        P("takeRef", s(Mv.TakeRef(ref src2)!) + " " + b(src2 == null));
        double x_ = 1;
        double[] box = { 6.5 };
        P("back", d(Mv.Back(2.5, ref x_, box)) + " " + d(x_));
        Res? holder = new Res("first");
        Mv.Reset(ref holder, new Res("second"));
        Mv.Reset(ref holder, holder);
        Mv.Reset(ref holder, null);
        Mv.Delete(new Res("deleted"));
        Dtor.Run(new object?[] { new Res("arr0"), null, new Res("arr1") });
        var pp = new Ptr<double>(new double[] { 1, 2, 3 }, 0L);
        var before = Mv.PostStep(ref pp, 1);
        P("postStep", d(before.Deref) + " " + d(pp.Deref));
        var df = new Defer(() => P("defer", "ran"));
        df.Dispose(); df.Dispose();
        var dc = new Defer(() => P("defer", "cancelled?")); dc.Cancel(); dc.Dispose();

        CppException[] es = { new CppException(), new LogicError(new Str(lit("logic"))), new RuntimeError("runtime"), new InvalidArgument("inv"), new OutOfRange(new Str(lit("oor"))), new LengthError("len"), new DomainError("dom"), new RangeError("range"), new OverflowError("over"), new BadAlloc(), new BadOptionalAccess(), new BadFunctionCall(), new BadVariantAccess() };
        var sbx = new StringBuilder();
        foreach (var ex in es) sbx.Append(R.Str(ex.what())).Append('|').Append(b(ex is LogicError)).Append(b(ex is RuntimeError)).Append(' ');
        P("exc", sbx);
        P("excMsg", new RuntimeError("msg here").Message);
    }
}
