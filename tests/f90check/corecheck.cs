// corecheck.cs - the C# side of corecheck.f90: the same operations on the C# runtime (cs/runtime) that corecheck.f90
// runs on part A of the Fortran runtime (f90/runtime/cpp_rt_a.f90, module cpp_rt_a). run.sh requires identical
// output. Only members that the Fortran engine uses (part A's entries of f90/runtime_parts.txt) are exercised, with
// the element types and copy modes the engine gives them. Doubles print as their bit patterns, booleans as 0/1,
// strings in brackets. Lines whose tag starts with "stod" need part C's strtod (run.sh drops them when part C is
// a stub).
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
    static string vb(Vec<sbyte> v) { var sb = new StringBuilder(); sb.Append(v.size()).Append('/').Append(v.capacity()).Append(':'); for (int i = 0; i < v.n; i++) sb.Append(' ').Append(v.a[i]); return sb.ToString(); }
    static string vz(Vec<bool> v) { var sb = new StringBuilder(); sb.Append(v.size()).Append('/').Append(v.capacity()).Append(':'); for (int i = 0; i < v.n; i++) sb.Append(b(v.a[i])); return sb.ToString(); }
    static string vl(Vec<long> v) { var sb = new StringBuilder(); sb.Append(v.size()).Append('/').Append(v.capacity()).Append(':'); for (int i = 0; i < v.n; i++) sb.Append(' ').Append(v.a[i]); return sb.ToString(); }
    static string vsh(Vec<short> v) { var sb = new StringBuilder(); sb.Append(v.size()).Append('/').Append(v.capacity()).Append(':'); for (int i = 0; i < v.n; i++) sb.Append(' ').Append(v.a[i]); return sb.ToString(); }
    static string vf(Vec<float> v) { var sb = new StringBuilder(); sb.Append(v.size()).Append('/').Append(v.capacity()).Append(':'); for (int i = 0; i < v.n; i++) sb.Append(' ').Append(d(v.a[i])); return sb.ToString(); }
    static string vs(Vec<Str> v) { var sb = new StringBuilder(); sb.Append(v.size()).Append('/').Append(v.capacity()).Append(':'); for (int i = 0; i < v.n; i++) sb.Append(' ').Append(v.a[i] == null ? "null" : s(v.a[i])); return sb.ToString(); }
    static string hex(byte[] a, int n) { var sb = new StringBuilder(); for (int i = 0; i < n; i++) sb.Append(a[i].ToString("x2")); return sb.ToString(); }
    static CPtr lit(string x) => R.Lit(x);
    static Str S(string x) => new Str(lit(x));

    sealed class Holder { public double x; public int k; public ulong kl; public Str s; }
    static RefAt<double> atX() => (object? o) => ref ((Holder)o!).x;
    static RefAt<double> atX2() => (object? o) => ref ((Holder)o!).x;
    static RefAt<int> atK() => (object? o) => ref ((Holder)o!).k;
    static RefAt<ulong> atKL() => (object? o) => ref ((Holder)o!).kl;
    static RefAt<Str> atS() => (object? o) => ref ((Holder)o!).s;

    public static void Main()
    {
        vecd(); veci(); vecsmall(); veco(); iter(); str(); tostr(); cptr(); ptr(); misc(); extra(); nums(); fuzz();
        Console.Out.Write(outp.ToString());
    }

    static void vecd()
    {
        var v = new Vec<double>();
        P("empty0", v.size() + " " + v.capacity() + " " + b(v.empty()));
        var caps = new StringBuilder();
        for (int i = 0; i < 20; i++) { v.push_back(i + 0.5); caps.Append(v.capacity()).Append(' '); }
        P("grow", caps);
        P("frontBack", d(v.front()) + " " + d(v.back()));
        v.resize(5); P("resize5", vd(v));
        v.resize(9); P("resize9", vd(v));
        v.resize(12, 7.5); P("resize12", vd(v));
        v.resize(12, 1.0); P("resizeSame", vd(v));
        var w = new Vec<double>(new double[] { 10, 20, 30 });
        P("w", vd(w));
        var it = v.insert(v.begin() + 2, 99); P("insT", vd(v) + " @" + it.i);
        it = v.insert(v.begin() + 1, new double[] { 1, 2, 3 }); P("insTA", vd(v) + " @" + it.i);
        double[] raw = { 5, 6, 7, 8 };
        it = v.insert(v.begin() + 4, new Ptr<double>(raw, 1), new Ptr<double>(raw, 3)); P("insP", vd(v) + " @" + it.i);
        it = v.insert(v.end(), new double[] { 77 }); P("insEnd", vd(v) + " @" + it.i);
        var r = v.erase(v.begin() + 3); P("erase1", vd(v) + " @" + r.i);
        r = v.erase(v.end() + (-1L)); P("eraseLast", vd(v) + " @" + r.i);
        v.reserve(100); P("reserve", vd(v));
        v.reserve(10); P("reserveLess", vd(v));
        v.shrink_to_fit(); P("shrink", vd(v));
        v.assign(3UL, 2.0); P("assignN", vd(v));
        v.assign(30UL, 4.0); P("assignBig", v.size() + "/" + v.capacity());
        v.assign(2UL, -1.0); P("assignSmall", vd(v));
        v.swap(w); P("swapV", vd(v)); P("swapW", vd(w));
        var c = v.Clone(); P("clone", vd(c));
        var e = new Vec<double>(); e.reserve(10); e.Assign(v); P("assign", vd(e));
        e.push_back(1.0); e.Assign(v); P("assignAgain", vd(e));
        var eq3 = new Vec<double>(3UL); eq3.Assign(v); P("assignSameCap", vd(eq3));
        e.Assign(e); P("assignSelf", vd(e));
        var m = new Vec<double>(); m.MoveFrom(c); P("moveTo", vd(m)); P("moveFrom", vd(c));
        m.MoveFrom(m); P("moveSelf", vd(m));
        var ma = new Vec<double>(); ma.MoveFrom(new double[] { 4, 5 }); P("moveArr", vd(ma));
        var dp = v.data(); P("data", d(dp[1]) + " " + dp.o);
        v.clear(); P("clear", vd(v) + " " + b(v.empty()));
        v.resize(2); P("afterClear", vd(v));
        P("eqNaN", b(Vec<double>.Eq(new Vec<double>(new double[] { double.NaN, 0.0 }), new Vec<double>(new double[] { double.NaN, -0.0 }))));
        P("eqLen", b(Vec<double>.Eq(new Vec<double>(2UL), new Vec<double>(3UL))));
        P("eqVal", b(Vec<double>.Eq(new Vec<double>(new double[] { 1, 2 }), new Vec<double>(new double[] { 1, 3 }))));
        P("ctorN", vd(new Vec<double>(4UL, 1.25)) + " " + vd(new Vec<double>(0UL)) + " " + vd(new Vec<double>(3UL)));
        var e1 = new Vec<double>(); var e2 = new Vec<double>();
        P("emptyShared", b(e1.data() == e2.data()) + b(new Vec<double>(0UL).data() == e1.data()));
        var g = new Vec<double>(); g.resize(3); var gp = g.data(); g.push_back(1); P("growNewArr", b(gp == g.data()) + " " + g.capacity());
    }

    static void veci()
    {
        var w = new Vec<int>(new int[] { 1, 2, 3 });
        P("vi", vi(w)); w.push_back(4); P("vi4", vi(w));
        try { w.at(7); } catch (OutOfRange x) { P("at", R.Str(x.what())); }
        try { w.at(ulong.MaxValue); } catch (OutOfRange x) { P("at", R.Str(x.what())); }
        w.at(1) = 20; P("atSet", vi(w) + " " + w.back());
        var u = new Vec<int>(new int[] { 7, 8 });
        var it = w.insert(w.begin() + 1, u.begin(), u.end()); P("insII", vi(w) + " @" + it.i);
        it = w.insert(w.begin() + 2, w.begin(), w.begin() + 3); P("insSelf", vi(w) + " @" + it.i);
        it = w.insert(w.begin(), 5); P("insT", vi(w) + " @" + it.i);
        it = w.erase(w.begin() + 1, w.begin() + 4); P("eraseRange", vi(w) + " @" + it.i);
        it = w.erase(w.begin() + 2, w.begin() + 2); P("eraseNone", vi(w) + " @" + it.i);
        it = w.erase(w.begin()); P("erase1", vi(w) + " @" + it.i);
        w.resize(9, -3); P("resizeT", vi(w));
        w.resize(4, 9); P("resizeDown", vi(w));
        w.resize(6); P("resizeUp", vi(w));
        var z = new Vec<int>(5UL); P("ctorN", vi(z) + " " + vi(new Vec<int>(3UL, 6)));
        z.assign(2UL, 1); P("assignN", vi(z));
        z.swap(w); P("swap", vi(z) + " | " + vi(w));
        w.shrink_to_fit(); P("shrink", vi(w));
        w.reserve(3); w.reserve(40); P("reserve", vi(w));
        var c = z.Clone(); c.push_back(1); P("clone", vi(z) + " | " + vi(c));
        var a = new Vec<int>(); a.Assign(z); P("assign", vi(a));
        var m = new Vec<int>(); m.MoveFrom(a); P("move", vi(m) + " | " + vi(a));
        P("eq", b(Vec<int>.Eq(m, z)) + b(Vec<int>.Eq(m, c)));
        var dp = m.data(); P("data", dp[2] + " " + dp.o + " " + m.a[0] + " " + m.back());
        m.clear(); P("clear", vi(m) + " " + b(m.empty()));
        var ib = m.begin(); var ie = m.end(); P("iterEmpty", (ie - ib) + "");
    }

    static void vecsmall()
    {
        var bv = new Vec<sbyte>();
        for (int i = 0; i < 5; i++) bv.push_back((sbyte)(i * 60));
        P("vb", vb(bv));
        bv.resize(7); bv.resize(8, (sbyte)-1); P("vbResize", vb(bv));
        var bc = bv.Clone(); bc.insert(bc.begin() + 1, (sbyte)9); P("vbClone", vb(bv) + " | " + vb(bc));
        bc.erase(bc.begin() + 3); P("vbErase", vb(bc));
        var ba = new Vec<sbyte>(); ba.assign(bv.begin() + 1, bv.begin() + 4); P("vbAssignII", vb(ba));
        ba.assign(3UL, (sbyte)5); P("vbAssignN", vb(ba));
        var bm = new Vec<sbyte>(); bm.MoveFrom(ba); P("vbMove", vb(bm) + " | " + vb(ba));
        bm.Assign(bv); P("vbAssign", vb(bm));
        bm.reserve(30); bm.shrink_to_fit(); P("vbShrink", vb(bm));
        var bd = bm.data(); P("vbData", bd[2] + " " + b(bm.empty()));
        P("vbEq", b(Vec<sbyte>.Eq(bm, bv)) + b(Vec<sbyte>.Eq(bm, bc)) + " " + vb(new Vec<sbyte>(2UL)) + " " + vb(new Vec<sbyte>(2UL, (sbyte)-7)));
        bm.clear(); P("vbClear", vb(bm) + " " + (bv.end().i - bv.begin().i));
        var zv = new Vec<bool>(3UL, true); zv.resize(5); P("vz", vz(zv));
        zv.push_back(true); zv.resize(8, true); zv.resize(7, false); P("vzPush", vz(zv));
        var zc = zv.Clone(); zc.assign(2UL, true); P("vzClone", vz(zv) + " | " + vz(zc));
        var za = new Vec<bool>(); za.Assign(zv); za.reserve(20); za.shrink_to_fit(); P("vzAssign", vz(za));
        var zm = new Vec<bool>(); zm.MoveFrom(za); zm.clear(); P("vzMove", vz(zm) + " | " + vz(za) + " " + (zv.end().i - zv.begin().i));
        var lv = new Vec<long>(); lv.push_back(long.MinValue); lv.push_back(-1); lv.resize(4, 7); P("vl", vl(lv));
        var lc = lv.Clone(); lc.assign(1UL, 3); lv.reserve(9); lv.shrink_to_fit(); P("vlClone", vl(lv) + " | " + vl(lc));
        var la = new Vec<long>(); la.Assign(lv); la.resize(2); la.resize(3); P("vlAssign", vl(la));
        var lm = new Vec<long>(); lm.MoveFrom(la); lm.clear(); P("vlMove", vl(lm) + " | " + vl(la) + " " + (lv.end().i - lv.begin().i) + " " + vl(new Vec<long>(2UL)));
        var sv = new Vec<short>(); sv.resize(3); sv.resize(5, (short)-2); sv.reserve(8); P("vs", vsh(sv));
        var sc = sv.Clone(); sc.assign(2UL, (short)300); sv.shrink_to_fit(); P("vsClone", vsh(sv) + " | " + vsh(sc));
        var sa = new Vec<short>(); sa.Assign(sc); sa.resize(1); P("vsAssign", vsh(sa) + " " + sa.size());
        var fv = new Vec<float>(); fv.push_back(0.1f); fv.push_back(-2.5f); fv.resize(4); P("vf", vf(fv));
        var fc = fv.Clone(); fc.assign(2UL, 1.5f); P("vfClone", vf(fv) + " | " + vf(fc));
        var fa = new Vec<float>(); fa.Assign(fv); fa.clear(); fa.push_back(3f); P("vfAssign", vf(fa) + " " + (fv.end().i - fv.begin().i) + " " + fv.size() + " " + d(fv.data()[1]));
    }

    static void veco()
    {
        // Vec<Str> made by a Func (mode 2: copies are clones)
        var sv = new Vec<Str>(3UL, () => new Str());
        sv[1].append(lit("one"));
        sv.push_back(S("two"));
        var sc = sv.Clone();
        sc[1].append(lit("!"));
        P("strVec", vs(sv)); P("strClone", vs(sc));
        P("strEq", b(Vec<Str>.Eq(sv, sv.Clone())) + b(Vec<Str>.Eq(sv, sc)));
        sv.resize(6); P("strResize", vs(sv));
        var it = sv.insert(sv.begin() + 1L, sc.begin(), sc.begin() + 2L);
        sc[1].append(lit("?"));
        P("strIns", vs(sv) + " @" + it.i);
        it = sv.erase(sv.begin(), sv.begin() + 2L); P("strErase", vs(sv) + " @" + it.i);
        it = sv.erase(sv.begin() + 1L); P("strErase1", vs(sv) + " @" + it.i);
        it = sv.insert(sv.begin() + 2L, S("ins")); P("strInsT", vs(sv) + " @" + it.i);
        try { sv.at(99); } catch (OutOfRange x) { P("strAt", R.Str(x.what())); }
        sv.at(0).append(lit("@")); P("strAtOk", s(sv.at(0)) + " " + s(sv.front()) + " " + s(sv.back()));
        sv.emplace_back(S("emp")).append(lit("+")); P("strEmplace", vs(sv));
        sv.pop_back(); P("strPop", vs(sv) + " " + b(sv.empty()));
        var sa = new Vec<Str>(); sa.Assign(sv); sv[0].append(lit("#")); P("strAssign", vs(sa));
        sa.assign(2UL, S("rep")); sa[0].append(lit("1")); P("strAssignN", vs(sa));
        sa.assign(sv.begin() + 1L, sv.end()); sv[1].append(lit("&")); P("strAssignII", vs(sa));
        sa.resize(5, S("pad")); sa[3].append(lit("3")); P("strResizeT", vs(sa));
        sa.resize(2); P("strShrink", vs(sa));
        var sm = new Vec<Str>(); sm.MoveFrom(sa); P("strMove", vs(sm) + " | " + vs(sa));
        sm.swap(sa); P("strSwap", vs(sm) + " | " + vs(sa));
        sa.reserve(11); P("strReserve", vs(sa)); sa.shrink_to_fit(); P("strFit", vs(sa));
        var rv = new StringBuilder();
        for (var ri = sv.rbegin(); ri != sv.rend(); ++ri) rv.Append(s(ri.Deref));
        P("strRev", rv);
        var sd = sv.data(); P("strData", s(sd[1]) + " " + b(sd.IsNull));
        var st = new Vec<Str>(new Str[] { S("x"), S("y") }); P("strTA", vs(st));
        var mvt = new Vec<Str>(); mvt.MoveFrom(new Str[] { S("p"), S("q"), S("r") }); P("strMoveTA", vs(mvt));
        var nul = new Vec<Str>(2UL); P("strMake", vs(nul));
        var nul2 = new Vec<Str>(2UL, S("v")); nul2[0].append(lit("0")); P("strCtorT", vs(nul2));
        sv.clear(); P("strClear", vs(sv) + " " + b(sv.empty()));
        // Vec<double[]> (mode 3: copies are deep)
        var va = new Vec<double[]>(2UL, () => new double[3]);
        va[0] = new double[] { 1, 2, 3 };
        var vc = va.Clone();
        vc[0][1] = 42;
        P("arrVec", d(va[0][1]) + " " + d(vc[0][1]) + " " + va[1].Length);
        var none = new Vec<double[]>();
        none.Assign(new Vec<double[]>(() => new double[3]));
        none.resize(2);
        P("mkAdopt", none[1].Length);
        var none2 = new Vec<double[]>();
        none2.MoveFrom(new Vec<double[]>(() => new double[4]));
        none2.resize(1);
        P("mkMove", none2[0].Length);
        var none3 = new Vec<double[]>(); none3.resize(2); P("mkNull", b(none3[0] == null) + " " + none3.size());
        var deep = new Vec<double[][]>(); deep.push_back(new double[][] { new double[] { 1, 2 }, null! }); var dc = deep.Clone(); dc[0][0][1] = 7;
        P("vecDeep", d(deep[0][0][1]) + " " + d(dc[0][0][1]) + " " + b(dc[0][1] == null));
        // Vec<CPtr> (mode 0: values)
        var cb = new byte[] { 65, 66, 67, 0 };
        var cv = new Vec<CPtr>(); cv.push_back(new CPtr(cb, 0)); cv.push_back(new CPtr(cb, 2)); cv.resize(3);
        var cc = cv.Clone();
        P("cptrVec", R.Str(cc[1]) + " " + b(cc[2].IsNull) + " " + b(Vec<CPtr>.Eq(cv, cc)) + b(Vec<CPtr>.Eq(cv, new Vec<CPtr>(3UL))));
        // Vec<Pair<int, double>> (mode 2; Equals is identity)
        var pv = new Vec<Pair<int, double>>(); pv.push_back(new Pair<int, double>(1, 2.5));
        var pc = pv.Clone(); pc[0].second = 9;
        P("pairVec", d(pv[0].second) + " " + d(pc[0].second) + " " + b(Vec<Pair<int, double>>.Eq(pv, pv)) + b(Vec<Pair<int, double>>.Eq(pv, pc)));
        // Vec<Vec<double>> (mode 2: an element's clone is the vector's copy)
        var vv = new Vec<Vec<double>>(2UL); vv[0].push_back(1); var vvc = vv.Clone(); vvc[0].push_back(2);
        P("vecVec", vd(vv[0]) + " | " + vd(vvc[0]) + " " + vd(vvc[1]));
    }

    static void iter()
    {
        var w = new Vec<double>(new double[] { 1, 2, 3 });
        var it = w.begin();
        w.push_back(4);
        w[0] = 100;
        P("staleIter", d(it.Deref) + " " + d((it + 0L).Deref) + " " + d((++it).Deref));
        var inc = w.begin(); ++inc;
        P("iterSub", (w.end() - w.begin()) + " " + d((w.end() + (-2L)).Deref));
        P("iterCmp", b(inc == w.begin() + 1L) + b(w.begin() == w.end()) + b(w.end() == w.begin() + 4L));
        var bi = new Iter<double>(new double[] { 7, 8, 9 }, 1L);
        P("bareIter", d(bi.Deref) + " " + d((bi + 1L).Deref) + " " + d((++bi).Deref) + " " + d(bi.Clone().Deref));
        Ptr<double> fromIt = w.begin() + 2L;
        P("ptrFromIter", d(fromIt.Deref) + " " + fromIt.o);
        var wi = new Vec<int>(new int[] { 4, 5, 6, 7 });
        var ii = wi.end() - 2L;
        P("iterI", ii.a[ii.i] + " " + (wi.end() - wi.begin()) + " " + ((wi.begin() + 3L) - wi.begin()) + " " + ii.Clone().i);
        var bs = new Vec<sbyte>(); bs.push_back(3); bs.push_back(4);
        var bb = bs.begin(); P("iterB", bb.a[(bb + 1L).i] + " " + b(bb + 2L == bs.end()) + b(bb == bb.Clone()));
        var so = new Vec<Str>(new Str[] { S("a"), S("b"), S("c") });
        var oi = so.begin(); oi = ++oi;
        P("iterO", s(oi.Deref) + " " + s((oi + 1L).Deref) + " " + b(oi + 2L == so.end()) + b(oi.Clone() == oi) + " " + s(oi.Clone().Deref));
        var rb = so.rbegin(); var rb1 = ++rb; var rb2 = ++rb1; var rb3 = ++rb2; P("revIter", s(so.rbegin().Deref) + " " + s(rb.Deref) + " " + b(so.rbegin() == so.rend()) + b(rb3 == so.rend()));
        var sp = new CSpan<Str>(new Str[] { S("a") }, 0L, 1L);
        P("span", b(default(CSpan<Str>).empty()) + b(sp.empty()) + b(new CSpan<Str>(new Str[] { S("a") }, 1L, 0L).empty()));
    }

    static void str()
    {
        var s_ = S("Hello, World");
        P("size", s_.size() + " " + b(s_.empty()) + " " + b(new Str().empty()));
        P("find", s_.find(lit("o"), 0) + " " + s_.find('o', 5) + " " + s_.find('z', 0) + " " + s_.find(lit("World"), 0) + " " + s_.find(lit("World"), 8) + " " + s_.find(lit("d"), 11) + " " + s_.find('d', 12));
        P("findEmpty", s_.find(lit(""), 12) + " " + s_.find(lit(""), 13) + " " + s_.find(lit(""), 0));
        P("rfind", s_.rfind('o', Str.npos) + " " + s_.rfind(lit("o"), 5) + " " + s_.rfind(lit("l"), Str.npos) + " " + s_.rfind(lit("Hello, World!"), Str.npos) + " " + new Str().rfind('a', Str.npos) + " " + s_.rfind(lit("H"), 0) + " " + s_.rfind('l', 2) + " " + s_.rfind(lit(""), 3) + " " + s_.rfind(lit(""), Str.npos));
        P("firstOf", s_.find_first_of(lit(", "), 0) + " " + s_.find_first_not_of(lit("Hel"), 0) + " " + s_.find_first_not_of('H', 0) + " " + s_.find_first_of(lit("xyz"), 0) + " " + s_.find_first_of(lit("lo"), 4) + " " + s_.find_first_not_of(lit("Helo, Wrd"), 0) + " " + s_.find_first_not_of('d', 11));
        P("lastOf", s_.find_last_not_of(lit("dl"), Str.npos) + " " + s_.find_last_of('o', 6) + " " + s_.find_last_of('W', Str.npos) + " " + s_.find_last_not_of(lit("Helo, Wrd"), Str.npos) + " " + s_.find_last_not_of(lit("x"), 3) + " " + new Str().find_last_not_of(lit("x"), Str.npos) + " " + new Str().find_last_of('x', Str.npos));
        P("substr", s(s_.substr(7, Str.npos)) + s(s_.substr(0, 5)) + s(s_.substr(12, 3)) + s(s_.substr(3, 0)));
        try { s_.substr(13, 1); } catch (OutOfRange x) { P("substrErr", R.Str(x.what())); }
        try { s_.substr(ulong.MaxValue, 1); } catch (OutOfRange x) { P("substrErr2", R.Str(x.what())); }
        var t = s_.Clone();
        t.erase(5, 2); P("erase", s(t));
        t.erase(3, Str.npos); P("eraseAll", s(t));
        t.erase(0, 0); P("eraseNone", s(t) + " " + s(t.erase(1, 1)));
        P("compare", S("abc").compare(0, 3, S("abd")) + " " + S("ab").compare(0, 9, S("abc")) + " " + S("abc").compare(0, 3, S("ab")) + " " + new Str(new byte[] { 0xFF }, 0, 1).compare(0, 1, S("a")) + " " + S("abc").compare(1, 2, S("bc")) + " " + s_.compare(7, 5, S("World")) + " " + S("xa").compare(1, 1, S("b")));
        try { S("ab").compare(3, 1, S("x")); } catch (OutOfRange x) { P("compareErr", R.Str(x.what())); }
        P("starts", b(s_.starts_with(lit("Hell"))) + b(s_.starts_with(S("ello"))) + b(s_.starts_with(lit(""))) + b(s_.starts_with(S("Hello, World!"))) + b(s_.starts_with(S("Hello, World"))));
        P("cat", s(Str.Cat(s_, lit("!"))) + s(Str.Cat(lit("<"), s_)) + s(Str.Cat(s_, s_)) + s(Str.Cat(s_, '?')) + s(Str.Cat(new Str(), new Str())));
        var g = new Str(); var lens = new StringBuilder();
        for (int i = 0; i < 40; i++) { g.push_back('a' + i % 26); lens.Append(g.b.Length).Append(' '); }
        P("grow", lens + s(g));
        var cs = g.c_str(); P("cstr", b(ReferenceEquals(cs.B, g.b)) + " " + g.b[g.n] + " " + R.Str(cs + 38));
        var cs2 = g.data(); P("data", b(cs2 == cs) + " " + cs2.O);
        var h = new Str(); h.MoveFrom(g); P("move", s(h) + s(g) + " " + g.b.Length);
        h.MoveFrom(h); P("moveSelf", s(h));
        h.Assign(lit("assigned")); P("assignC", s(h) + " " + h.b.Length);
        h.Assign(S("xy")); P("assignS", s(h) + " " + h.b.Length);
        h.Assign(h); P("assignSelf", s(h));
        var hb = h.b; h.Assign(S("0123456789")); P("assignGrow", s(h) + " " + h.b.Length + " " + b(ReferenceEquals(hb, h.b)));
        P("eq", b(h.EqLit(lit("0123456789"))) + b(h.EqLit(lit("012345678"))) + b(h.EqLit(lit("0123456788"))) + b(h.Equals(S("0123456789"))) + b(h.Equals((Str?)null)) + b(h.Equals(S("0123456780"))) + b(new Str().EqLit(lit(""))));
        var v = S("  view  "); v.remove_prefix(2); P("view", s(v) + " " + v.b[v.n]);
        var a = new Str(); a.assign(new Ptr<sbyte>(new sbyte[] { (sbyte)'q', (sbyte)'r', (sbyte)'s', (sbyte)'t' }, 1), new Ptr<sbyte>(new sbyte[] { 0, 0, 0, 0 }, 3)); P("assignPtr", s(a) + " " + a.b.Length);
        var a2 = new Str(); P("assignCl", s(a2.assign(lit("abcdef") + 1, 3UL)) + " " + a2.b.Length);
        a2.append(S("0123456")).append(lit("ABCDEF")).append(lit("xyz"), 2UL).append('!'); P("append", s(a2) + " " + a2.b.Length);
        a2.append(a2); P("appendSelf", s(a2));
        a2.append(a2.c_str() + 3); P("appendSelfC", s(a2));
        P("ctors", s(new Str(5UL, '-')) + s(new Str(lit("abcdef"), 3UL)) + s(new Str(S("abcdef"))) + s((Str)lit("conv")) + s(new Str(0UL, 'x')) + " " + new Str(5UL, '-').b.Length + " " + new Str(lit("abc")).b.Length + " " + new Str().b.Length);
        var bi = s_.begin(); P("strIter", (char)bi.a[bi.i] + " " + (s_.end().i - bi.i) + " " + b(bi + 12L == s_.end()) + b(bi.Clone() == bi));
        var r = S("abc"); r.resize(6, '.'); P("resize", s(r) + " " + r.b.Length); r.resize(2, 'z'); P("resizeDown", s(r)); r.resize(4, 0); P("resizeNul", hex(r.b, r.n));
        r.reserve(50); P("reserve", s(r) + " " + r.b.Length); r.reserve(3); P("reserve2", r.b.Length + "");
        r.clear(); P("clear", s(r) + " " + r.size() + " " + r.b.Length);
        var f = S("front"); f.front() = (byte)'F'; f.back() = (byte)'T'; P("frontBack", s(f) + " " + (char)f.front() + (char)f.back());
        f.pop_back(); f.pop_back(); P("pop", s(f));
        P("toStringFn", "<" + S("plain text").ToString() + ">" + "<" + new Str().ToString() + ">");
        var idx = new Ptr<ulong>(new ulong[1], 0L);
        P("stod", d(Str.stod(S("  12.5e1xyz"), idx)) + " " + idx[0] + " " + d(Str.stod(S("-0.25"), default)) + " " + d(Str.stod(S("1e400"), idx)) + " " + idx[0] + " " + d(Str.stod(S("0x1p-2"), idx)) + " " + idx[0]);
        try { Str.stod(S("abc"), default); } catch (InvalidArgument x) { P("stodErr", R.Str(x.what())); }
        var hd = new Holder(); var fi = new Ptr<ulong>(hd, atKL()); Str.stod(S("3.5 "), fi); P("stodField", hd.kl);
    }

    static void tostr()
    {
        P("toStringI", s(Str.to_string(-42)) + s(Str.to_string(0)) + s(Str.to_string(int.MinValue)) + s(Str.to_string(int.MaxValue)));
        P("toStringL", s(Str.to_string(long.MinValue)) + s(Str.to_string(long.MaxValue)) + s(Str.to_string(-1L)));
        P("toStringU", s(Str.to_string(uint.MaxValue)) + s(Str.to_string(0u)) + s(Str.to_string(2147483648u)));
        P("toStringUL", s(Str.to_string(ulong.MaxValue)) + s(Str.to_string(0UL)) + s(Str.to_string(9223372036854775808UL)) + s(Str.to_string(10000000000000000000UL)));
        double[] ds = { 3.14159, -0.0, 0.0, 1e20, 2.5e-7, 5e-7, 1.5e-6, 2.5e-6, 0.0000015, 1e300, -1e300, 5e-324, double.Epsilon * 3, 123.456, 999999.9999995, 0.1, 0.5, 1.5, 2.5, -2.5e-7, -1e-7, double.NaN, -double.NaN, double.PositiveInfinity, double.NegativeInfinity, double.MaxValue, 4503599627370497.5, 0.000001, 0.0000005000000000000001, 1e15 + 0.3, 2.0000005, 2.0000015 };
        var sb = new StringBuilder();
        foreach (var x in ds) sb.Append(s(Str.to_string(x)));
        P("toStringD", sb);
    }

    static void cptr()
    {
        var buf = new byte[8];
        var p = new CPtr(buf, 0);
        var q = p + 3;
        var q2 = p + 5L;
        P("cptrArith", CPtr.Diff(q, p) + " " + CPtr.Diff(p, q2) + " " + b(p < q) + b(q < p) + b(q == p + 3L) + b(p != new CPtr(new byte[8], 0)) + b(CPtr.Null == new CPtr(null!, 5)) + b(CPtr.Null.IsNull) + b(p.IsNull) + b(q != q2) + b(q.Clone() == q) + " " + (q + (-2)).O + " " + (q + 4L).O);
        var pb = new sbyte[] { (sbyte)'a', (sbyte)'b', (sbyte)'c', 0 };
        CPtr fromC = new Ptr<sbyte>(pb, 1L);
        CPtr fromUc = new Ptr<byte>(new byte[] { (byte)'a', (byte)'b', (byte)'c', 0 }, 2L);
        P("fromPtr", R.Str(fromC) + " " + R.Str(fromUc) + " " + fromUc.O + " " + b(((CPtr)default(Ptr<byte>)).IsNull));
    }

    static void ptr()
    {
        double[] arr = { 1, 2, 3, 4, 5 };
        var p = new Ptr<double>(arr, 1L);
        P("ptrIdx", d(p[1]) + " " + d(p[2L]) + " " + d(p[3UL]) + " " + d(p.Deref) + " " + d((p + 3L).Deref) + " " + d((p - 1L).Deref));
        p[0] = 20; var pi = p + 1L; pi.Deref = 30;
        P("ptrSet", d(arr[1]) + " " + d(arr[2]));
        var q = p + 2L;
        P("ptrCmp", b(q == new Ptr<double>(arr, 3L)) + b(q == new Ptr<double>(new double[5], 3L)) + b(default(Ptr<double>) == new Ptr<double>((double[])null!, 7L)) + b(default(Ptr<double>).IsNull) + b(p != q) + b(p.IsNull) + b(q - 2L == p));
        var h = new Holder(); h.x = 1.5;
        var at = atX();
        Ptr<double> f1 = new Ptr<double>(h, at), f2 = new Ptr<double>(h, at), f3 = new Ptr<double>(h, atX2()), f4 = new Ptr<double>(new Holder(), at), f5 = new Ptr<double>(null, at);
        f1.Deref = 2.5;
        P("fieldPtr", d(h.x) + " " + d(f2.Deref) + " " + d((f1 + 3L)[9]) + " " + d(f1[2L]) + " " + d(f1[5UL]) + " " + b(f1.IsNull));
        P("fieldEq", b(f1 == f2) + b(f1 == f3) + b(f1 == f4) + b(f1 == f2 + 1L) + b(f1 == p) + b(p == f1) + b(f5 == new Ptr<double>(null, at)) + b(f1 != f2) + b(f1 - 1L != f2));
        int[] ia = { 10, 11, 12 };
        var ip = new Ptr<int>(ia, 0L);
        h.k = 5;
        var fk = new Ptr<int>(h, atK()); fk[0] += 1;
        P("ptrI", ip.Deref + " " + ip[2] + " " + ip[1L] + " " + ip[2UL] + " " + (ip + 1L).Deref + " " + (ip + 2L - 1L).Deref + " " + b(ip != ip + 1L) + b(ip != new Ptr<int>(ia, 0L)) + b(default(Ptr<int>).IsNull) + b(fk.IsNull) + " " + h.k + " " + fk.Deref + " " + b(fk != new Ptr<int>(h, atK())));
        var fl = new Ptr<ulong>(h, atKL()); fl[0] = ulong.MaxValue;
        var lp = new Ptr<ulong>(new ulong[] { 1, 2 }, 1L);
        P("ptrL", h.kl + " " + lp[0] + " " + fl[3]);
        var ps = new Ptr<Str>(new Str[] { S("a"), S("b"), S("c") }, 0L);
        P("ptrO", s((ps + 1L).Deref) + s(ps[2]) + s(ps[1L]) + s(ps[2UL]) + " " + b(default(Ptr<Str>).IsNull) + b(ps.IsNull) + b(ps != ps + 1L) + b(ps != ps));
        var pbb = new Ptr<byte>(new byte[] { 1, 2, 250 }, 1L);
        P("ptrB", pbb[0] + " " + pbb[1] + " " + (pbb + 1L)[0] + " " + (pbb - 1L)[0]);
        var pf = new Ptr<float>(new float[] { 0.5f, 1.5f, 2.5f }, 0L);
        P("ptrF", d(pf[1]) + " " + d((pf + 2L)[0]) + " " + d(pf[2UL]));
        var psh = new Ptr<short>(new short[] { -7, 8 }, 0L); var pz = new Ptr<bool>(new bool[] { false, true }, 0L);
        P("ptrSZ", psh[0] + " " + psh[1] + " " + b(pz[1]) + b(pz[0UL]) + b(pz[1UL]));
    }

    static void misc()
    {
        P("min", d(M.min(0.0, -0.0)) + " " + d(M.min(-0.0, 0.0)) + " " + d(M.max(-0.0, 0.0)) + " " + d(M.max(0.0, -0.0)) + " " + d(M.min(double.NaN, 1.0)) + " " + d(M.min(1.0, double.NaN)) + " " + d(M.max(double.NaN, 1.0)) + " " + d(M.max(1.0, double.NaN)));
        P("clamp", d(M.clamp(double.NaN, 0.0, 1.0)) + " " + d(M.clamp(-0.0, 0.0, 1.0)) + " " + d(M.clamp(5.0, 0.0, 1.0)) + " " + d(M.clamp(-5.0, 0.0, 1.0)) + " " + M.clamp(-5, 0, 10) + " " + M.clamp(15, 0, 10) + " " + M.clamp(5, 0, 10));
        P("ints", M.max(-3, 2) + " " + M.min(-3, 2) + " " + M.max(-3L, 2L) + " " + M.max(long.MinValue, long.MaxValue));
        P("unsigned", M.max(ulong.MaxValue, 1UL) + " " + M.min(ulong.MaxValue, 1UL) + " " + M.max(uint.MaxValue, 1u) + " " + M.max(1u, 2147483648u) + " " + M.min((byte)200, (byte)100) + " " + M.min((byte)100, (byte)200) + " " + M.min(9223372036854775808UL, 5UL));
        P("listMinMax", d(M.max(new double[] { 1, double.NaN, 3 })) + " " + d(M.min(new double[] { double.NaN, 1, -2 })) + " " + d(M.max(new double[] { -0.0, 0.0 })) + " " + M.max(new int[] { 4, 9, 2 }) + " " + d(M.min(new double[] { 2, -0.0, 0.0 })));
        Pair<int, Str> p1 = new Pair<int, Str>(1, S("b"));
        var pc = p1.Clone(); pc.second.append(lit("!")); pc.first = 5; P("pairClone", p1.first + s(p1.second) + " " + pc.first + s(pc.second));
        var pp = new Pair<Str, Str>(S("x"), S("y")); var pa = new Pair<Str, Str>(S(""), S("")); pa.Assign(pp); pp.first.append(lit("+")); P("pairAssign", s(pa.first) + s(pa.second) + s(pp.first));
        var pm = new Pair<Str, Str>(); pm.first.append(lit("m")); var pmc = pm.Clone(); pmc.second.append(lit("c"));
        var pn = new Pair<int, double>(); var pz = new Pair<double[], Str>();
        P("pairMake", s(pm.first) + s(pm.second) + s(pmc.first) + s(pmc.second) + " " + pn.first + " " + d(pn.second) + " " + b(pz.first == null) + s(pz.second));
        var vs = new Pair<Vec<double>, double[]>(new Vec<double>(new double[] { 1 }), new double[] { 2 }); var vsc = vs.Clone(); vsc.first.push_back(3); vsc.second[0] = 4;
        P("pairDeep", vd(vs.first) + " " + d(vs.second[0]) + " | " + vd(vsc.first) + " " + d(vsc.second[0]));
        var o = new Opt<double>();
        P("opt", d(o.value_or(9)) + " " + b(o.has));
        var o2 = new Opt<double>(3.5); var oc = o2.Clone();
        P("opt2", d(oc.value_or(0)) + " " + b(oc.has) + " " + d(o.Clone().value_or(-1)) + " " + b(o.Clone().has));
        var oa = new Opt<double>(1.0); oa.Assign(o); P("optAssign", b(oa.has) + " " + d(oa.val)); oa.Assign(o2); P("optAssign2", b(oa.has) + " " + d(oa.val));
        var om = new Opt<double>(); om.MoveFrom(o2); P("optMove", b(om.has) + " " + d(om.val));
        var os = new Opt<Str>(); P("optStr0", b(os.has) + b(os.val == null));
        os.Set(S("x")); var osc = os.Clone(); osc.val.append(lit("y"));
        var osa = new Opt<Str>(); osa.Assign(os); os.val.append(lit("z"));
        P("optStr", s(os.val) + s(osc.val) + s(osa.val) + b(osa.has));
        os.emplace(S("e")).append(lit("!")); P("optEmplace", s(os.val) + b(os.has));
        os.reset(); P("optReset", b(os.has) + b(os.val == null) + b(os.Clone().has) + b(os.Clone().val == null));
        osa.Assign(os); P("optAssignEmpty", b(osa.has) + b(osa.val == null));
        var ocp = new Opt<CPtr>(); P("optValue0", b(ocp.val.IsNull) + b(ocp.has)); ocp.Set(lit("abc") + 1); var ocpc = ocp.Clone(); ocp.reset(); P("optValue", R.Str(ocpc.val) + " " + b(ocp.val.IsNull));
        var va = new Variant(); P("variant0", va.idx + " " + va.v);
        va.Set(42); var vc = va.Clone(); va.Set(2.5); P("variant", va.idx + " " + d((double)va.v) + " " + vc.idx + " " + vc.v);
        var vstr = S("str"); va.Set(vstr); vstr.append(lit("!")); var vc2 = va.Clone(); ((Str)va.v).append(lit("?"));
        P("variantStr", va.idx + " " + s((Str)va.v) + " " + s((Str)vc2.v));
        var vm = new Variant(); vm.MoveFrom(va); P("variantMove", vm.idx + " " + b(ReferenceEquals(vm.v, va.v)) + " " + s((Str)vm.v));
        var vaa = new Variant(); vaa.Set(true); vc.Assign(vaa); P("variantAssign", vc.idx + " " + vc.v);
        var da = Arr.Make<double>(3); da[1] = double.NaN;
        var db = Arr.Clone(da);
        P("arrD", b(Arr.Eq(da, db)) + " " + b(!ReferenceEquals(db, da)) + " " + b(Arr.Eq(da, new double[3])) + b(Arr.Eq(da, new double[2])) + " " + Arr.Make2<int>(2, 3)[1].Length + " " + Arr.Make2<double>(3, 1).Length + " " + Arr.Make<int>(4).Length + " " + Arr.Make<long>(2)[1] + " " + b(Arr.Make<bool>(2)[0]));
        var sa = Arr.MakeWith(2, () => S("s"));
        var sb = Arr.Clone(sa); sb[0].append(lit("!"));
        var sd = Arr.Make<Str>(2); Arr.Assign(sd, sa); sa[1].append(lit("?"));
        P("arrStr", s(sa[0]) + s(sb[0]) + s(sd[1]) + s(sa[1]) + " " + s(Arr.Make2<Str>(2, 2)[1][1]));
        double[][] grid = { new double[] { 1, 2 }, new double[] { 3, 4 } };
        var gc = Arr.Clone(grid); gc[0][0] = 9;
        P("arrShallow", d(grid[0][0]));
        var vcp = new CPtr[] { lit("ab"), lit("cd") }; var vcc = Arr.Clone(vcp); vcc[0] = vcc[0] + 1; P("arrValues", R.Str(vcp[0]) + R.Str(vcc[0]));
        var vca = new CPtr[2]; Arr.Assign(vca, vcp); P("arrAssignValues", R.Str(vca[1]));
        P("arrNull", b(Arr.Clone((double[])null!) == null));
        var ints = new int[] { 1, 2, 3 }; var ints2 = new int[3]; Arr.Assign(ints2, ints); ints[0] = 7; P("arrAssignI", ints2[0] + " " + Arr.Clone(ints)[0]);
        var fd = new double[3]; Array.Fill(fd, -0.5); var fl = new long[2]; Array.Fill(fl, long.MinValue); P("fill", d(fd[2]) + " " + fl[1]);
        CppException[] es = { new RuntimeError(S("runtime")), new InvalidArgument(S("inv")), new OutOfRange(S("oor")) };
        var sbx = new StringBuilder();
        foreach (var ex in es) sbx.Append(R.Str(ex.what())).Append('|').Append(b(ex is LogicError)).Append(b(ex is RuntimeError)).Append(b(ex is OutOfRange)).Append(' ');
        P("exc", sbx);
        var src = S("msg"); var ce = new RuntimeError(src); src.append(lit("!")); P("excCopy", R.Str(ce.what()));
        P("excDefault", R.Str(new CppException().what()));
    }

    static void extra()
    {
        var ab = new byte[] { 1, 200 }; var ab2 = new byte[3]; Arr.Assign(ab2, ab); var abc = Arr.Clone(ab); abc[0] = 9;
        var ad = new double[] { 1.5, -0.0 }; var ad2 = new double[2]; Arr.Assign(ad2, ad);
        var al = new long[] { long.MinValue, 3 }; var al2 = new long[2]; Arr.Assign(al2, al); var alc = Arr.Clone(al);
        var ash = new short[] { -5, 7 }; var ash2 = new short[2]; Arr.Assign(ash2, ash); var ashc = Arr.Clone(ash);
        var az = new bool[] { true, false }; var az2 = new bool[2]; Arr.Assign(az2, az); var azc = Arr.Clone(az);
        var af = new float[3];
        P("arrKinds", ab2[0] + " " + ab2[1] + " " + ab2[2] + " " + abc[0] + ab[0] + " " + d(ad2[0]) + " " + d(ad2[1]) + " " + al2[0] + " " + alc[1] + " " + ash2[0] + " " + ashc[1] + " " + b(az2[0]) + b(az2[1]) + b(azc[0]) + " " + af.Length + " " + d(af[2]));
        var hs = new Holder(); hs.s = S("field");
        var fs = new Ptr<Str>(hs, atS()); fs.Deref.append(lit("!"));
        var before = s(hs.s);
        fs[3] = S("new");
        P("ptrOField", before + s(hs.s) + " " + b(fs.IsNull) + b(fs != new Ptr<Str>(hs, atS())) + " " + s(fs[0]));
    }

    static void nums()
    {
        ulong[] us = { 0, 1, 9007199254740993UL, 9223372036854775807UL, 9223372036854775808UL, 9223372036854776832UL, 9223372036854776833UL, 18446744073709551615UL, 18446744073709550592UL, 18446744073709549568UL, 12345678901234567890UL };
        var sb = new StringBuilder();
        foreach (var u in us) sb.Append(d((double)u)).Append(' ');
        P("u2d", sb);
        ulong[] xs = { 0, 1, 7, 10, 9223372036854775807UL, 9223372036854775808UL, 18446744073709551615UL, 18446744073709551614UL, 12345678901234567890UL };
        ulong[] ys = { 1, 3, 10, 9223372036854775807UL, 9223372036854775808UL, 18446744073709551615UL, 4294967296UL };
        sb.Clear();
        foreach (var x in xs) foreach (var y in ys) sb.Append(x / y).Append(',').Append(x % y).Append(' ');
        P("udiv", sb);
        uint[] ux = { 0, 5, 4294967295u, 2147483648u, 123456789u };
        uint[] uy = { 1, 2, 7, 2147483648u, 4294967295u };
        sb.Clear();
        foreach (var x in ux) foreach (var y in uy) sb.Append(x % y).Append(' ');
        P("urem4", sb);
        double[] ds = { 0.0, -0.0, 1.0, double.NaN, -double.NaN, double.PositiveInfinity, double.NegativeInfinity, double.MaxValue, double.Epsilon };
        sb.Clear();
        foreach (var x in ds) sb.Append(b(double.IsNaN(x))).Append(b(double.IsInfinity(x))).Append(b(double.IsFinite(x))).Append(' ');
        P("dtests", sb);
        P("nanBits", d(double.NaN) + " " + d(-double.NaN));
    }
    // ---- a differential fuzz: the same pseudo-random operations on both sides, hashed (FNV-1a over the state)
    static ulong rs = 12345;
    static ulong Rnd() { rs = rs * 6364136223846793005UL + 1442695040888963407UL; return rs >> 33; }
    static int Rn(int n) => (int)(Rnd() % (ulong)n);
    static ulong H(ulong h, long v) => (h ^ (ulong)v) * 1099511628211UL;
    static readonly string[] lits = { "a", "bc", "", "cde", "eab" };
    static string Pick() => lits[Rn(5)];

    static void fuzz()
    {
        var v = new Vec<int>(); var w = new Vec<int>();
        ulong h = 14695981039346656037UL;
        var sb = new StringBuilder();
        for (int step = 0; step < 4000; step++)
        {
            int op = Rn(15);
            switch (op)
            {
                case 0: case 1: { int x = Rn(1000) - 500; v.push_back(x); break; }
                case 2: { int p = Rn(v.n + 1); int x = Rn(100); v.insert(v.begin() + p, x); break; }
                case 3: if (v.n > 0) { int p = Rn(v.n); v.erase(v.begin() + p); } break;
                case 4: { int p = Rn(v.n + 1); int q = p + Rn(v.n - p + 1); v.erase(v.begin() + p, v.begin() + q); break; }
                case 5: { int p = Rn(v.n + 1); int a = Rn(w.n + 1); int b2 = a + Rn(w.n - a + 1); v.insert(v.begin() + p, w.begin() + a, w.begin() + b2); break; }
                case 6: v.resize((ulong)Rn(30)); break;
                case 7: { ulong c = (ulong)Rn(30); int x = Rn(9); v.resize(c, x); break; }
                case 8: { ulong c = (ulong)Rn(12); int x = Rn(50); v.assign(c, x); break; }
                case 9: v.reserve((ulong)Rn(60)); break;
                case 10: v.shrink_to_fit(); break;
                case 11: v.swap(w); break;
                case 12: w.Assign(v); break;
                case 13: w = v.Clone(); break;
                case 14: if (Rn(5) == 0) v.clear(); else { var m = new Vec<int>(); m.MoveFrom(v); w.MoveFrom(m); } break;
            }
            h = H(h, (long)v.size()); h = H(h, (long)v.capacity()); h = H(h, (long)w.size()); h = H(h, (long)w.capacity());
            for (int i = 0; i < v.n; i++) h = H(h, v.a[i]);
            if (step % 1000 == 999) sb.Append(h.ToString("x")).Append(' ');
        }
        P("fuzzVec", sb + b(Vec<int>.Eq(v, w)));
        var s1 = new Str(); var s2 = S("abc");
        h = 14695981039346656037UL; sb.Clear();
        for (int step = 0; step < 4000; step++)
        {
            int op = Rn(16);
            long r = 0;
            switch (op)
            {
                case 0: case 1: s1.push_back('a' + Rn(5)); break;
                case 2: s1.append(lit(Pick())); break;
                case 3: if (s1.n > 0) { int p = Rn(s1.n + 1); ulong l = (ulong)Rn(4); s1.erase((ulong)p, l); } break;
                case 4: { ulong k = (ulong)Rn(25); int ch = 'a' + Rn(5); s1.resize(k, ch); break; }
                case 5: if (s1.n > 0) s1.remove_prefix((ulong)Rn(s1.n + 1)); break;
                case 6: s1.Assign(lit(Pick())); break;
                case 7: { int ch = 'a' + Rn(5); ulong p = (ulong)Rn(s1.n + 2); r = (long)s1.find(ch, p); break; }
                case 8: { int ch = 'a' + Rn(5); ulong p = Rn(3) == 0 ? Str.npos : (ulong)Rn(s1.n + 2); r = (long)s1.rfind(ch, p); break; }
                case 9: { var set = lit(Pick()); ulong p = (ulong)Rn(s1.n + 2); r = (long)s1.find_first_of(set, p); break; }
                case 10: { var set = lit(Pick()); ulong p = Rn(3) == 0 ? Str.npos : (ulong)Rn(s1.n + 2); r = (long)s1.find_last_not_of(set, p); break; }
                case 11: { var t = lit(Pick()); ulong p = (ulong)Rn(s1.n + 2); r = (long)s1.find(t, p); break; }
                case 12: { var t = lit(Pick()); ulong p = Rn(3) == 0 ? Str.npos : (ulong)Rn(s1.n + 2); r = (long)s1.rfind(t, p); break; }
                case 13: { int p = Rn(s1.n + 1); ulong l = (ulong)Rn(6); r = s1.compare((ulong)p, l, s2); break; }
                case 14: { r = s1.starts_with(s2) ? 1 : 0; ulong p = (ulong)Rn(s1.n + 1); ulong l = (ulong)Rn(5); s2 = s1.substr(p, l); break; }
                case 15: { var t = s1.Clone(); t.append(s2); s1.MoveFrom(t); break; }
            }
            h = H(h, r); h = H(h, s1.n); h = H(h, s1.b.Length);
            for (int i = 0; i < s1.n; i++) h = H(h, s1.b[i]);
            if (step % 1000 == 999) sb.Append(h.ToString("x")).Append(' ');
        }
        P("fuzzStr", sb + s(s1));
        var vs1 = new Vec<Str>(() => new Str()); var vs2 = new Vec<Str>();
        h = 14695981039346656037UL; sb.Clear();
        for (int step = 0; step < 3000; step++)
        {
            int op = Rn(9);
            switch (op)
            {
                case 0: vs1.push_back(S(Pick())); break;
                case 1: { int p = Rn(vs1.n + 1); var t = S(Pick()); vs1.insert(vs1.begin() + p, t); break; }
                case 2: if (vs1.n > 0) { int p = Rn(vs1.n); vs1.erase(vs1.begin() + p); } break;
                case 3: vs1.resize((ulong)Rn(12)); break;
                case 4: if (vs1.n > 0) { int k = Rn(vs1.n); vs1[k].append(lit(Pick())); } break;
                case 5: vs2.Assign(vs1); break;
                case 6: { int p = Rn(vs1.n + 1); int a = Rn(vs2.n + 1); int b2 = a + Rn(vs2.n - a + 1); vs1.insert(vs1.begin() + p, vs2.begin() + a, vs2.begin() + b2); break; }
                case 7: { var c = vs1.Clone(); vs2.MoveFrom(c); break; }
                case 8: { int p = Rn(vs1.n + 1); int q = p + Rn(vs1.n - p + 1); vs1.erase(vs1.begin() + p, vs1.begin() + q); break; }
            }
            h = H(h, (long)vs1.size()); h = H(h, (long)vs1.capacity()); h = H(h, (long)vs2.size());
            for (int i = 0; i < vs1.n; i++) { h = H(h, vs1.a[i].n); for (int j = 0; j < vs1.a[i].n; j++) h = H(h, vs1.a[i].b[j]); }
            if (step % 1000 == 999) sb.Append(h.ToString("x")).Append(' ');
        }
        P("fuzzVecStr", sb + b(Vec<Str>.Eq(vs1, vs2)));
    }
}
