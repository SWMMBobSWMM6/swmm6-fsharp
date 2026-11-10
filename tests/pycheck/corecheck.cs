// corecheck.cs - the C# side of corecheck.py: the same operations on the C# runtime (cs/runtime) that corecheck.py runs
// on part A of the Python runtime (package cpp: py/cpp/core_*.py). run.sh requires identical output. Doubles print as
// their bit patterns, booleans as 0/1, strings in brackets. Started from tests/javacheck/corecheck.cs, with the
// numeric helpers (nums) and more string, vector and pointer cases (more) added.
// Written by Claude (Anthropic) for the SWMM6 ports; see AI-USE.md.
using System;
using System.Collections.Generic;
using System.Linq;
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
        vec(); iter(); str(); cptr(); ptr(); misc(); nums(); more(); fuzz();
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
        P("arrStr", s(sa[0]) + s(sb[0]) + s(sd[1]) + s(sa[1]));
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
        var tsb = new StringBuilder();
        foreach (var tv in new double[] { 1e15, 9.99999999999999e14, 123456789012345.0, 1234567890123456.0, 0.0001, 0.00001, 0.000123, 1.5, 100.0, 1e100, 1.7976931348623157e308, 5e-324, 2.2250738585072014e-308, -2.5e-5, 0.1 + 0.2, 1.0 / 3, 2.0 / 3, 123.456, 1e21, 1e-7, -0.0, 0.0,
            1e16, 1e17, 1.5e17, 12345678901234567.0, 123456789012345678.0, 1e18, 9.99e17, 1.2e18, 99999999999999990.0, 1e-4, 9.9999e-5, 1234567.0e-9 })
            tsb.Append(tv).Append(' ').Append("x" + tv + 1 + true + "|");
        P("netText", tsb);
        uint ub = 0; ulong qb = 0; long lb = 0;
        P("backUQ", Mv.Back(7, ref ub, new uint[] { 0xFFFFFFFFu }) + " " + ub + " " + Mv.Back(2.5, ref qb, new ulong[] { 0x8000000000000000UL }) + " " + qb + " " + Mv.Back(true, ref lb, new long[] { -5 }) + " " + lb);
    }

    // ---------------------------------------------------------------------------------------------- numeric helpers
    // Every helper of core_num.py against the C# operation it stands for, over edge values.
    static readonly ulong[] edgeBits = {
        0x0000000000000000, 0x8000000000000000, 0x3FF0000000000000, 0xBFF0000000000000, 0x3FE0000000000000,
        0xBFE0000000000000, 0x3FF8000000000000, 0xBFF8000000000000, 0x4004000000000000, 0xC004000000000000,
        0x7FF0000000000000, 0xFFF0000000000000, 0x7FF8000000000000, 0xFFF8000000000000, 0x7FF8000000000123,
        0x7FF4000000000000, 0xFFF0000000000001, 0x0000000000000001, 0x8000000000000001, 0x000B8157268FDAF0,
        0x7FEFFFFFFFFFFFFF, 0xFFEFFFFFFFFFFFFF, 0x3FB999999999999A, 0x3FD5555555555555,
        0x47EFFFFFE0000000, 0x47EFFFFFF0000000, 0x47EFFFFFEFFFFFFF, 0xC7EFFFFFF0000000, 0x36A0000000000000,
        0x3690000000000000, 0x3690000000000001, 0x380FFFFFE0000000, 0x380FFFFFF0000000, 0x4170000010000000,
        0x4170000030000000, 0x3FF0000010000000, 0x3FF0000030000000, 0x3FF0000010000001,
    };
    static readonly double[] edgeVals = {
        2147483647.0, 2147483647.5, 2147483647.9999998, 2147483648.0, -2147483648.0, -2147483648.5, -2147483648.9999998,
        -2147483649.0, -2147483649.5, 4294967295.0, 4294967295.5, 4294967296.0, 4294967297.0, 9223372036854775808.0,
        9223372036854774784.0, -9223372036854775808.0, -9223372036854777856.0, 18446744073709551616.0,
        18446744073709549568.0, 13835058055282163712.0, 1e19, -1e19, 32767.9, 32768.0, -32768.9, -32769.0, 65535.5,
        65536.0, 65537.7, 127.9, 128.0, -128.9, -129.0, 255.5, 256.0, 257.9, -1.5, -255.5, -256.5, -65535.5, 1e308,
        -1e308, 0.1, 16777217.0, 16777219.0, 123456789.123, -987654321.987, 3e9, -3e9, 5e18, -5e18, 1.5e19,
    };
    static IEnumerable<double> Edges()
    {
        foreach (var bits in edgeBits) yield return BitConverter.Int64BitsToDouble((long)bits);
        foreach (var x in edgeVals) yield return x;
    }
    static readonly long[] edgeLongs = {
        0, 1, -1, 127, 128, -128, -129, 255, 256, 32767, 32768, -32768, -32769, 65535, 65536, 2147483647, 2147483648,
        -2147483648, -2147483649, 4294967295, 4294967296, 16777217, 16777219, 9007199254740993, -9007199254740993,
        1152921504606846977, long.MaxValue, long.MinValue, long.MinValue + 1, 0x0040000040000001, 0x0040000040000000,
        -0x0040000040000001, 0x7FFFFF8000000000, 0x7FFFFFC000000000, 0x7FFFFFBFFFFFFFFF, 12345678901234567,
    };
    static readonly ulong[] edgeULongs = {
        0, 1, 9223372036854775808UL, 9223372036854775809UL, 18446744073709551615UL, 0x8000008000000001UL,
        0x8000008000000000UL, 0x8000018000000000UL, 0xFFFFFF7FFFFFFFFFUL, 0xFFFFFF8000000000UL, 13835058055282163713UL,
    };
    static string sv(double x) => d(x);

    static void nums()
    {
        foreach (var x in Edges())
        {
            var sb = new StringBuilder();
            sb.Append(d((double)(float)x)).Append(' ').Append(b(double.IsNegative(x))).Append(' ');
            sb.Append(unchecked((int)x)).Append(' ').Append(unchecked((long)x)).Append(' ').Append(unchecked((uint)x)).Append(' ')
              .Append(unchecked((ulong)x)).Append(' ').Append(unchecked((short)x)).Append(' ').Append(unchecked((ushort)x)).Append(' ')
              .Append(unchecked((sbyte)x)).Append(' ').Append(unchecked((byte)x)).Append(" |");
            sb.Append(' ').Append(Conv.D2I(x)).Append(' ').Append(Conv.D2L(x)).Append(' ').Append(Conv.D2U(x)).Append(' ').Append(Conv.D2UL(x))
              .Append(' ').Append(Conv.D2S(x)).Append(' ').Append(Conv.D2US(x)).Append(' ').Append(Conv.D2SB(x)).Append(' ').Append(Conv.D2B(x))
              .Append(' ').Append(R.D2I(x)).Append(' ').Append(R.D2L(x));
            sb.Append(" | ").Append(((uint)BitConverter.SingleToInt32Bits((float)x)).ToString("x")).Append(' ').Append(x.ToString());
            P("dbl " + d(x), sb);
        }
        foreach (var l in edgeLongs)
            P("long " + l, d((double)(float)l) + " " + unchecked((int)l) + " " + unchecked((uint)l) + " " + unchecked((short)l) + " " + unchecked((ushort)l)
              + " " + unchecked((sbyte)l) + " " + unchecked((byte)l) + " " + unchecked((ulong)l) + " " + d((double)l));
        foreach (var u in edgeULongs)
            P("ulong " + u, d((double)(float)u) + " " + unchecked((int)u) + " " + unchecked((uint)u) + " " + unchecked((long)u) + " " + unchecked((short)u) + " " + unchecked((sbyte)u) + " " + d((double)u));
        uint[] fbits = { 0, 0x80000000, 0x3F800000, 0x7F800000, 0xFF800000, 0x7FC00000, 0xFFC00000, 0x7FA00000, 0x7FC00001, 0x00000001, 0x7F7FFFFF, 0x3DCCCCCD };
        foreach (var fb in fbits) P("fbits " + fb.ToString("x"), d((double)BitConverter.Int32BitsToSingle((int)fb)));

        // a / b for b = +0.0, -0.0, (int) 0
        var sbz = new StringBuilder();
        foreach (var x in Edges().Take(20)) sbz.Append(d(x / 0.0)).Append(' ').Append(d(x / -0.0)).Append(' ').Append(d(x / 0)).Append(' ');
        foreach (var l in new long[] { 5, -5, 0, long.MinValue }) sbz.Append(d(l / 0.0)).Append(' ').Append(d(l / -0.0)).Append(' ');
        P("fdiv0", sbz);
        double zero = 0.0;
        P("nanConst", d(zero / zero) + " " + d(Std.QNaN) + " " + d(Math.Sqrt(-1.0)) + " " + d(double.PositiveInfinity - double.PositiveInfinity));

        // C# long / and % (truncating, the sign of the dividend)
        long[] ds = { 7, -7, 0, 1, -1, 6, -6, 12345678901234, -12345678901234, long.MaxValue, long.MinValue, long.MinValue + 1, 2147483648, -2147483649 };
        long[] dv = { 2, -2, 3, -3, 1, -1, 7, -97, long.MaxValue, long.MinValue, 2147483648, 4294967296 };
        var sbd = new StringBuilder();
        foreach (var a in ds) foreach (var bb in dv)
        {
            if (a == long.MinValue && bb == -1) { sbd.Append("ovf "); continue; }
            sbd.Append(a / bb).Append(',').Append(a % bb).Append(' ');
        }
        P("idiv", sbd);
        try { long z = 0; P("idiv0", (5L / z).ToString()); } catch (DivideByZeroException) { P("idiv0", "throws"); }
        try { long z = 0; P("irem0", (5L % z).ToString()); } catch (DivideByZeroException) { P("irem0", "throws"); }

        // C# % on doubles (C fmod)
        double[] fm = { 5.5, -5.5, 2.0, 1e300, 0.0, -0.0, double.PositiveInfinity, double.NegativeInfinity, double.NaN, BitConverter.Int64BitsToDouble(unchecked((long)0xFFF8000000000000UL)), 5e-324, 1e-310, 7.0, 0.3 };
        var sbf = new StringBuilder();
        foreach (var a in fm) foreach (var bb in fm) sbf.Append(d(a % bb)).Append(' ');
        P("fmod", sbf);
    }

    // ---------------------------------------------------------------------------------------------- more cases
    sealed class HolderB { public bool flag; }
    static string vb(Vec<bool> v) { var sb = new StringBuilder(); sb.Append(v.size()).Append('/').Append(v.capacity()).Append(':'); for (int i = 0; i < v.n; i++) sb.Append(' ').Append(b(v.a[i])); return sb.ToString(); }
    static string vl(Vec<long> v) { var sb = new StringBuilder(); sb.Append(v.size()).Append('/').Append(v.capacity()).Append(':'); for (int i = 0; i < v.n; i++) sb.Append(' ').Append(v.a[i]); return sb.ToString(); }
    static string vp(Vec<Pair<int, Str>> v) { var sb = new StringBuilder(); sb.Append(v.size()).Append('/').Append(v.capacity()).Append(':'); for (int i = 0; i < v.n; i++) sb.Append(' ').Append(v.a[i].first).Append(s(v.a[i].second)); return sb.ToString(); }

    static void more()
    {
        var t = new Str(lit("  a,b;;c  "));
        P("s1", t.find_first_not_of(' ', 0) + " " + t.find_first_not_of(lit(" a"), 0) + " " + t.find_first_not_of(lit(" "), 20) + " " + t.find_last_not_of(lit(" "), Str.npos)
            + " " + t.find_last_not_of(lit(" c"), 7) + " " + t.find_last_not_of(' ', 1) + " " + new Str().find_last_not_of(lit(" "), Str.npos) + " " + t.find_first_not_of(lit(""), 3)
            + " " + t.find_last_not_of(lit(""), 4) + " " + t.find_first_not_of(lit(" abc,;"), 0) + " " + t.find_first_not_of('x', 9) + " " + t.find_last_not_of('x', Str.npos));
        P("s2", t.rfind(lit(";"), 6) + " " + t.rfind(lit(";;"), Str.npos) + " " + t.rfind(lit(""), 4) + " " + t.rfind(lit(""), 99) + " " + t.rfind(';', 5) + " " + t.rfind('x', Str.npos)
            + " " + t.find(lit(";"), 6) + " " + t.find(lit("c "), 0) + " " + t.find(lit("zz"), 11) + " " + t.find(';', 11) + " " + t.find(lit(""), 10) + " " + t.rfind(lit("  "), 0)
            + " " + t.find_first_of(lit(";,"), 4) + " " + t.find_last_of(lit("ab"), Str.npos) + " " + t.find_last_of(';', 5) + " " + t.find_first_of(lit(""), 0) + " " + t.find_last_of(lit("xyz"), 3));
        var u = new Str(lit("ab")); u.append(u); u.append(u); P("selfApp", s(u) + " " + u.b.Length);
        u.insert(1, u); P("selfIns", s(u) + " " + u.b.Length + " " + s(Str.Cat(u, u)));
        u.replace(2, 5, lit("XY")); P("repl", s(u)); u.replace(0, 100, new Str(lit("whole"))); P("repl2", s(u)); u.replace(5, 0, lit("!")); P("repl3", s(u));
        P("substrEdge", s(u.substr(u.size(), Str.npos)) + s(u.substr(2, 0)) + s(u.substr(1, 2)));
        P("cmp2", new Str(lit("b")).compare(lit("abc")) + " " + new Str().compare(new Str()) + " " + new Str(lit("abc")).compare(1, 2, new Str(lit("bc"))) + " " + new Str(lit("abc")).compare(0, 9, new Str(lit("abcd"))) + " " + new Str(lit("abé")).compare(lit("abz")));
        var e = new Str(lit("0123456789")); e.erase(3, 2); P("erase2", s(e)); e.erase(5, Str.npos); P("erase3", s(e)); e.erase(0, 0); P("erase4", s(e) + " " + e.n);
        e.resize(8, 'z'); P("resz", s(e)); e.resize(2, 'q'); P("resz2", s(e)); e.resize(4); P("resz3", hex(e.b, e.n));
        P("emptyOps", b(new Str().EqLit(lit(""))) + b(new Str().starts_with(lit(""))) + b(new Str().starts_with(new Str())) + b(new Str(lit("x")).starts_with(lit("xy"))) + b(new Str().Equals(new Str())) + b(new Str(lit("a")).Equals(new Str(lit("b")))));
        var hi = new Str(); hi.push_back(-61); hi.push_back(0xA9); hi.push_back(300); P("hiBytes", hex(hi.b, hi.n) + " " + hi.Char(0) + " " + hi.Char(2) + " " + hi.front() + " " + hi.back() + " " + hi.GetHashCode());
        var longS = new Str(); for (int i = 0; i < 300; i++) longS.push_back(33 + i % 90);
        P("longHash", longS.GetHashCode() + " " + longS.b.Length);
        var cl = new Str(lit("abc")); cl.clear(); var cp = cl.c_str(); P("clearCstr", cp[0] + " " + cl.n + " " + cl.b.Length);
        var ap = new Str(); ap.append(lit("x")); ap.append(65); ap.append(new Str(lit("12345")), 1, 99); P("appendMix", s(ap));
        var aptr = new Str(); var src = new byte[] { (byte)'h', 0, (byte)'i', (byte)'!' }; aptr.append(new CPtr(src, 0), 4UL); P("appendNul", hex(aptr.b, aptr.n) + " " + aptr.n);
        var asg = new Str(lit("long string here")); asg.assign(new CPtr(src, 2), 2UL); P("assignCul", s(asg) + " " + asg.b.Length);
        var sw = new Str(lit("starts")); P("startsC", b(sw.starts_with(lit("sta"))) + b(sw.starts_with(lit("starts!"))) + b(sw.starts_with(new Str(lit("starts")))) + b(sw.EqLit(lit("starts"))) + b(sw.EqLit(lit("start"))));
        P("catMix", s(Str.Cat(lit(""), new Str())) + s(Str.Cat(new Str(), lit("x"))) + s(Str.Cat(new Str(lit("a")), 0x62)) + s(Str.Cat(new Str(lit("a")), -1)).Length);

        // vectors
        var vd2 = new Vec<double>(); vd2.reserve(3); vd2.push_back(1); vd2.push_back(2); vd2.push_back(3); vd2.push_back(4); P("vcap", vd(vd2));
        vd2.insert(vd2.begin(), vd2.begin() + 1L, vd2.end()); P("vselfins", vd(vd2));
        vd2.erase(vd2.begin(), vd2.end()); P("veraseall", vd(vd2));
        vd2.resize(2); vd2.push_back(5); vd2.resize(1); vd2.resize(4, 9); P("vresz", vd(vd2));
        vd2.assign(10UL, 0.25); P("vassignBig", vd(vd2));
        vd2.assign(2UL, 0.5); P("vassignSmall", vd(vd2));
        vd2.MoveFrom(new double[] { 7, 8 }); P("vmoveTA", vd(vd2));
        vd2.insert(vd2.begin() + 1L, 2UL, 1.5); P("vinsN", vd(vd2));
        var back = vd2.back(); var front = vd2.front(); var dat = vd2.data(); P("vends", d(back) + " " + d(front) + " " + d(dat[1]) + " " + d(vd2.at(3)));
        vd2.emplace_back(6.5); P("vemplace", vd(vd2));
        var vb2 = new Vec<bool>(); vb2.push_back(true); vb2.resize(3); vb2.pop_back(); vb2.insert(vb2.begin(), true); P("vbool", vb(vb2) + " " + b(Vec<bool>.Eq(vb2, vb2.Clone())));
        var vl2 = new Vec<long>(2UL); vl2[1] = long.MinValue; vl2.push_back(long.MaxValue); vl2.erase(vl2.begin()); P("vlong", vl(vl2));
        var pv = new Vec<Pair<int, Str>>(); pv.push_back(new Pair<int, Str>(1, new Str(lit("one")))); pv.push_back(new Pair<int, Str>(2, new Str(lit("two"))));
        var pvc = pv.Clone(); pvc[0].second.append(lit("!")); pvc[1].first = 22;
        P("vpair", vp(pv) + " |" + vp(pvc) + " " + b(Vec<Pair<int, Str>>.Eq(pv, pv)) + b(Vec<Pair<int, Str>>.Eq(pv, pvc)));
        var vs2 = new Vec<Str>(); vs2.resize(2); vs2[0].append(lit("x")); P("vstrResize", vs(vs2) + " " + b(!ReferenceEquals(vs2[0], vs2[1])));
        vs2.insert(vs2.begin() + 1L, 2UL, new Str(lit("c"))); vs2[1].append(lit("1")); P("vstrInsN", vs(vs2));
        try { vs2.at(5); } catch (OutOfRange x) { P("vstrAt", R.Str(x.what())); }
        var sbr = new StringBuilder(); for (var ri = vs2.rbegin(); ri != vs2.rend(); ++ri) sbr.Append(s(ri.Deref)); P("vstrRev", sbr);
        vs2.erase(vs2.begin() + 1L); vs2.pop_back(); P("vstrErase", vs(vs2));
        var vv = new Vec<Vec<double>>(2UL, new Vec<double>(new double[] { 1 })); vv[0].push_back(2); P("vvec", vd(vv[0]) + " " + vd(vv[1]));
        var vvc = vv.Clone(); vvc[1].push_back(3); P("vvecClone", vd(vv[1]) + " " + vd(vvc[1]));
        var vs3 = new Vec<Str>(new Str[] { new Str(lit("a")), new Str(lit("b")) }); var vs4 = new Vec<Str>(); vs4.Assign(vs3); vs3[0].append(lit("!")); P("vstrAssign", vs(vs4) + " " + b(Vec<Str>.Eq(vs3, vs4)));
        vs4.MoveFrom(new Str[] { new Str(lit("m")) }); P("vstrMoveTA", vs(vs4));
        vs4.swap(vs3); P("vstrSwap", vs(vs4) + " " + vs(vs3));
        var vd3 = new Vec<double>(new double[] { double.NaN, 0.0 }); P("vEqNaN", b(Vec<double>.Eq(vd3, new Vec<double>(new double[] { double.NaN, 0.0 }))) + b(Vec<double>.Eq(vd3, new Vec<double>(new double[] { double.NaN, 1.0 }))));

        // pointers, boxes, optionals, moves
        int[] ia = { 1, 2, 3, 4 };
        var pi = new Ptr<int>(ia, 1L);
        pi[1] = 30; (pi + 2L)[0] = 40;
        P("ptrI", ia[2] + " " + ia[3] + " " + (pi - 1L).Deref + " " + ((pi + 2L) - pi) + " " + b(pi != new Ptr<int>(ia, 1L)) + b(pi < pi + 1L) + b(pi == new Ptr<int>(new int[4], 1L)));
        var hb = new HolderB(); var bx = Box<bool>.Of(() => ref hb.flag); bx.V = true; bool[] arrb = new bool[2]; var bx2 = new Box<bool>(arrb, 1L); bx2.V = true;
        P("box", b(hb.flag) + b(bx.V) + b(arrb[1]) + b(bx2.V));
        var od = new Opt<double>(); od.emplace(2.5); var od2 = new Opt<double>(); od2.Assign(od); od.reset(); var od3 = new Opt<double>(); od3.MoveFrom(od2);
        P("optD", d(od.val) + b(od.has) + d(od2.val) + b(od3.has) + d(od3.value_or(1)) + " " + d(new Opt<double>(4.5).value()) + " " + d(new Opt<double>().Clone().val));
        double xd = 3.5; var taken = Mv.Take(ref xd); P("takeD", d(taken) + " " + d(xd));
        var cpm = new CPtr(new byte[] { 1, 2, 3 }, 0); var cpold = Mv.PostStep(ref cpm, 2); P("postStepC", cpold.O + " " + cpm.O + " " + cpm[0]);
        var itv = new Vec<int>(new int[] { 5, 6, 7 }); var itp = itv.begin(); var itold = Mv.PostStep(ref itp, 1); P("postStepI", itold.Deref + " " + itp.Deref);
        var vr = new Vec<Res>(); vr.push_back(new Res("v0")); vr.push_back(new Res("v1")); Dtor.Run(vr);
        var orr = new Opt<Res>(new Res("opt")); Dtor.Run(orr); Dtor.Run(new Opt<Res>());
        Dtor.Run(new Res[][] { new Res[] { new Res("nested") } });

        // arrays
        var fa = new double[5]; Arr.Fill(fa, 1, 3, 2.5); var fb2 = new double[5]; Arr.Assign(fb2, new double[] { 9, 8 });
        P("arrFill", d(fa[0]) + d(fa[1]) + d(fa[2]) + d(fa[3]) + " " + d(fb2[0]) + d(fb2[1]) + d(fb2[2]) + " " + b(Arr.Eq(new int[] { 1, 2 }, new int[] { 1, 2 })) + b(Arr.Eq(new int[] { 1, 2 }, new int[] { 1 })));
        var sarr = new Str[3]; var shared = new Str(lit("sh")); Array.Fill(sarr, shared); shared.append(lit("!")); P("arrayFill", s(sarr[0]) + s(sarr[2]) + b(ReferenceEquals(sarr[0], sarr[1])));
        var sfill = new Str[3]; Arr.Fill(sfill, 0, 3, shared); shared.append(lit("?")); P("arrFillStr", s(sfill[0]) + b(ReferenceEquals(sfill[0], sfill[1])));
        P("arrMake2", Arr.Make2<double>(3, 2).Length + " " + d(Arr.Make2<double>(3, 2)[2][1]) + " " + Arr.Make<Str>(2)[1].n + " " + b(Arr.Eq(new Str[] { new Str(lit("q")) }, new Str[] { new Str(lit("q")) })));

        // CPtr over lists of signed bytes, and more arithmetic
        sbyte[] sb8 = { -128, 127, -1, 0 };
        CPtr cs8 = new Ptr<sbyte>(sb8, 0L);
        int pd = ++cs8[0]; int pd2 = cs8[2]--;
        P("cptrS", pd + " " + pd2 + " " + cs8[0] + " " + cs8[1] + " " + cs8[2] + " " + b(CPtr.Diff(cs8 + 2, cs8) == 2) + b((cs8 + 2) > cs8) + b(cs8 <= cs8) + b((cs8 + 1) >= (cs8 + 2)));
    }

    // ---------------------------------------------------------------------------------------------- randomized
    // The same pseudo-random operations on both sides (splitmix64), so many more cases than the hand-picked ones.
    static ulong rs = 12345;
    static ulong Next() { unchecked { ulong z = (rs += 0x9E3779B97F4A7C15UL); z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL; z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL; return z ^ (z >> 31); } }
    static int Rnd(int k) => (int)(Next() % (ulong)k);
    static Str RStr() { int n = Rnd(9); var x = new Str(); for (int i = 0; i < n; i++) x.push_back("ab c"[Rnd(4)]); return x; }
    static ulong RPos() => Rnd(8) == 0 ? Str.npos : (ulong)Rnd(12);

    static void fuzz()
    {
        var sb = new StringBuilder();
        for (int it = 0; it < 400; it++)
        {
            var a = RStr(); var x = RStr(); ulong pos = (ulong)Rnd(12); ulong rpos = RPos(); int ch = "ab c"[Rnd(4)];
            var xc = x.c_str();
            sb.Clear();
            sb.Append(a.find(x, pos)).Append(' ').Append(a.find(xc, pos)).Append(' ').Append(a.find(ch, pos)).Append(' ')
              .Append(a.rfind(x, rpos)).Append(' ').Append(a.rfind(xc, rpos)).Append(' ').Append(a.rfind(ch, rpos)).Append(' ')
              .Append(a.find_first_of(xc, pos)).Append(' ').Append(a.find_first_of(x, pos)).Append(' ').Append(a.find_first_not_of(xc, pos)).Append(' ').Append(a.find_first_not_of(x, pos)).Append(' ').Append(a.find_first_not_of(ch, pos)).Append(' ')
              .Append(a.find_last_of(xc, rpos)).Append(' ').Append(a.find_last_of(x, rpos)).Append(' ').Append(a.find_last_not_of(xc, rpos)).Append(' ').Append(a.find_last_not_of(x, rpos)).Append(' ').Append(a.find_last_not_of(ch, rpos)).Append(' ')
              .Append(a.compare(x)).Append(' ').Append(a.compare(xc)).Append(' ').Append(b(a.starts_with(x))).Append(b(a.starts_with(xc))).Append(b(a.ends_with(x))).Append(b(a.ends_with(xc))).Append(b(a.Equals(x))).Append(b(a.EqLit(xc))).Append(b(a.contains(x))).Append(' ')
              .Append(a.GetHashCode()).Append(' ').Append(a.CompareTo(x));
            ulong p2 = (ulong)Rnd((int)a.size() + 1);
            var e1 = a.Clone(); e1.erase(p2, (ulong)Rnd(5)); var e2 = a.Clone(); e2.insert(p2, x); var e3 = a.Clone(); e3.replace(p2, (ulong)Rnd(5), x);
            var e4 = a.Clone(); e4.append(x); var e5 = a.substr(p2, (ulong)Rnd(6)); var e6 = a.Clone(); e6.resize((ulong)Rnd(12), ch);
            var e7 = new Str(); e7.Assign(a); e7.append(xc); e7.append(xc, (ulong)x.size()); e7.push_back(ch);
            sb.Append(' ').Append(s(e1)).Append(s(e2)).Append(s(e3)).Append(s(e4)).Append(s(e5)).Append(s(e6)).Append(s(e7)).Append(e2.b.Length).Append(' ').Append(e4.b.Length).Append(' ').Append(e6.b.Length).Append(' ').Append(e7.b.Length);
            P("fz" + it, sb);
        }

        var v = new Vec<int>();
        for (int it = 0; it < 400; it++)
        {
            int op = Rnd(12), n = v.n;
            switch (op)
            {
                case 0: v.push_back(Rnd(100)); break;
                case 1: if (n > 0) v.pop_back(); break;
                case 2: v.insert(v.begin() + Rnd(n + 1), Rnd(100)); break;
                case 3: { var src = new Vec<int>(new int[] { Rnd(9), Rnd(9), Rnd(9) }); int k = Rnd(4); v.insert(v.begin() + Rnd(n + 1), src.begin(), src.begin() + k); break; }
                case 4: v.insert(v.begin() + Rnd(n + 1), (ulong)Rnd(4), Rnd(100)); break;
                case 5: if (n > 0) v.erase(v.begin() + Rnd(n)); break;
                case 6: { int p = Rnd(n + 1); int q = p + Rnd(n - p + 1); v.erase(v.begin() + p, v.begin() + q); break; }
                case 7: v.resize((ulong)Rnd(20)); break;
                case 8: v.resize((ulong)Rnd(20), Rnd(100)); break;
                case 9: v.reserve((ulong)Rnd(40)); break;
                case 10: if (Rnd(3) == 0) v.shrink_to_fit(); else v.assign((ulong)Rnd(10), Rnd(100)); break;
                case 11: if (Rnd(4) == 0) v.clear(); else v.insert(v.begin() + Rnd(n + 1), new int[] { Rnd(9), Rnd(9) }); break;
            }
            P("vf" + it, op + " " + vi(v));
        }

        var w = new Vec<Str>(); var pool = new Vec<Str>();
        for (int it = 0; it < 200; it++)
        {
            int op = Rnd(8), n = w.n;
            switch (op)
            {
                case 0: pool.push_back(RStr()); w.push_back(pool.back().Clone()); break;
                case 1: if (n > 0) w.pop_back(); break;
                case 2: if (pool.n > 0) { int k = Rnd(pool.n + 1); w.insert(w.begin() + Rnd(n + 1), pool.begin(), pool.begin() + k); } break;
                case 3: w.insert(w.begin() + Rnd(n + 1), (ulong)Rnd(3), RStr()); break;
                case 4: if (n > 0) { int p = Rnd(n); w.erase(w.begin() + p, w.begin() + p + Rnd(n - p + 1)); } break;
                case 5: w.resize((ulong)Rnd(8)); break;
                case 6: w.resize((ulong)Rnd(8), RStr()); break;
                case 7: { var c = w.Clone(); if (c.n > 0) c[Rnd(c.n)].append(lit("#")); w.Assign(Rnd(2) == 0 ? c : w); break; }
            }
            if (pool.n > 0) pool[Rnd(pool.n)].append(lit("*"));
            if (w.n > 0) w[Rnd(w.n)].push_back('+');
            P("wf" + it, op + " " + vs(w));
        }

        for (int it = 0; it < 300; it++)
        {
            ulong bits = Next(); double x = BitConverter.Int64BitsToDouble((long)bits);
            double y = (double)(long)Next() / (double)(1UL << Rnd(64));
            long l = (long)Next() >> Rnd(64), m = (long)Next() >> Rnd(64);
            sb.Clear();
            sb.Append(d((double)(float)x)).Append(' ').Append(d((double)(float)y)).Append(' ').Append(unchecked((int)y)).Append(' ').Append(unchecked((long)y)).Append(' ')
              .Append(unchecked((uint)y)).Append(' ').Append(unchecked((ulong)y)).Append(' ').Append(unchecked((short)y)).Append(' ').Append(unchecked((sbyte)y)).Append(' ')
              .Append(Conv.D2UL(y)).Append(' ').Append(Conv.D2I(y)).Append(' ').Append(Conv.D2L(x)).Append(' ').Append(d((double)(float)l)).Append(' ').Append(d((double)(float)unchecked((ulong)l))).Append(' ');
            if (m != 0 && !(l == long.MinValue && m == -1)) sb.Append(l / m).Append(',').Append(l % m); else sb.Append('z');
            sb.Append(' ').Append(x.ToString()).Append(' ').Append(y.ToString()).Append(' ').Append((y * 1e-300).ToString()).Append(' ').Append(((double)l).ToString()).Append(' ').Append((x * 1e30).ToString());
            sb.Append(' ').Append(d(x % y)).Append(' ').Append(d(y % x)).Append(' ').Append(d(y / 0.0)).Append(' ').Append(d(x / -0.0)).Append(' ').Append(unchecked((int)l)).Append(' ').Append(unchecked((ushort)l));
            P("nf" + it, sb);
        }
    }
}
