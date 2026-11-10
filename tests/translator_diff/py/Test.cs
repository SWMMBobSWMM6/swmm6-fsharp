// Differential tests for tools/cs2x/Cs2Py.cs: the same C# run as C# and as translated Python must print the same.
// Written by Claude (Anthropic) for the SWMM6 ports; see AI-USE.md.
#pragma warning disable
using System;

namespace openswmm.dt
{
    public delegate int IntFn();
    public delegate void RefFn(ref double x);

    public class Base
    {
        public int a;
        public int hid;
        public virtual int V() => 1;
        public int NV() => 10;
        public int Show() => hid;
        protected Base(global::Cpp.Raw _) { }
        public Base() : this(global::Cpp.Raw.V) { a = 5; hid = 100; }
    }
    public class Derived : Base
    {
        public new int hid;
        public override int V() => 2 + base.V();
        public new int NV() => 20;
        protected Derived(global::Cpp.Raw _) : base(_) { }
        public Derived() : this(global::Cpp.Raw.V) { hid = 7; }
        public int Both() => hid * 1000 + Show();
    }
    public class Holder
    {
        public double d;
        public int i;
        public int[] arr;
        public static int sfield = 3;
        public static double sd = 1.5;
        protected Holder(global::Cpp.Raw _) { }
        public Holder() : this(global::Cpp.Raw.V) { arr = new int[4]; }
    }
    public class MyErr : global::Cpp.CppException
    {
        public int code;
        protected MyErr(global::Cpp.Raw _) { }
        public MyErr() : this(global::Cpp.Raw.V) { code = 42; }
    }

    public static partial class _F
    {
        public static int Ctr = 0;
        public static int[] Table = new int[] { 3, 1, 4, 1, 5 };
        public static int TableSum = SumOf(Table);
        static int SumOf(int[] t) { int s = 0; for (int i = 0; i < t.Length; i++) s += t[i]; return s; }

        static string L(long x) => x.ToString();
        static string Bits(double d) => BitConverter.DoubleToInt64Bits(d).ToString();
        static int Next() { return ++Ctr; }

        public static string t01_int_wrap()
        {
            string s = "";
            int a = int.MaxValue; a = a + 1; s += a + ",";
            int b = int.MinValue; b--; s += b + ",";
            uint u = 0; u--; s += u + ",";
            u = uint.MaxValue; u++; s += u + ",";
            long l = long.MaxValue; l += 1; s += l + ",";
            ulong ul = 0; ul -= 1; s += ul + ",";
            ul = ulong.MaxValue; ul++; s += ul + ",";
            short sh = short.MaxValue; sh++; s += sh + ",";
            ushort us = 0; us--; s += us + ",";
            byte by = 255; by++; s += by + ",";
            sbyte sb = 127; sb++; s += sb + ",";
            int m0 = 123456789; int m = m0 * 1000; s += m + ",";
            long lm0 = 123456789L; long lm = lm0 * lm0 * 1000L; s += lm + ",";
            uint um0 = 4000000000u; uint um = um0 * 3u; s += um + ",";
            int mn = int.MinValue; int neg = -mn; s += neg + ",";
            int c0 = 65536; int c = c0 * c0; s += c + ",";
            return s;
        }

        public static string t02_conversions()
        {
            string s = "";
            int i = -1;
            int v200 = 200, v70k = 70000, vm2 = -2; s += (uint)i + "," + (ulong)i + "," + (long)(uint)i + "," + (byte)i + "," + (sbyte)v200 + "," + (short)v70k + "," + (ushort)vm2 + ",";
            long l = -5000000000L;
            s += (int)l + "," + (uint)l + "," + (ulong)l + ",";
            ulong ul = 0x8000000000000000UL;
            s += (long)ul + "," + (int)ul + "," + (double)ul + ",";
            char ch = 'A'; ch++; s += (int)ch + ",";
            int x = 300; byte bb = (byte)x; s += bb + ",";
            uint uu = 3000000000u; int ii = (int)uu; s += ii + ",";
            int big = int.MaxValue;
            long wide = big + 1; s += wide + ",";
            long wide2 = (long)big + 1; s += wide2 + ",";
            return s;
        }

        public static string t03_divmod()
        {
            string s = "";
            int[] a = { 7, -7, 7, -7, 0, int.MinValue };
            int[] b = { 2, 2, -2, -2, 3, 3 };
            for (int k = 0; k < a.Length; k++) s += (a[k] / b[k]) + ":" + (a[k] % b[k]) + ",";
            long la = -9000000000000000000L, lb = 7;
            s += (la / lb) + ":" + (la % lb) + ",";
            uint ua = 4000000000u, ub = 7u;
            s += (ua / ub) + ":" + (ua % ub) + ",";
            ulong uh = 18000000000000000000UL;
            s += (uh / 7) + ":" + (uh % 7) + ",";
            try { int z = 0; s += (5 / z); } catch (Exception) { s += "div0,"; }
            return s;
        }

        public static string t04_shifts()
        {
            string s = "";
            int a = 1; s += (a << 31) + "," + (a << 32) + "," + (a << 33) + ",";
            int n = -16; s += (n >> 2) + "," + (n >>> 2) + "," + (n >> 33) + ",";
            uint u = 0x80000000u; s += (u >> 31) + "," + (u << 1) + ",";
            long l = 1L; s += (l << 63) + "," + (l << 64) + ",";
            ulong ul = 1UL << 63; s += (ul >> 63) + ",";
            int cnt = 35; s += (a << cnt) + ",";
            byte by = 0xF0; s += (by << 4) + "," + (by >> 4) + ",";
            int m = ~5; uint um = ~5u; s += m + "," + um + ",";
            long h = 1469598103934665603L; h ^= 97; h *= 1099511628211L; s += h + ",";
            ulong uh = 14695981039346656037UL; uh ^= 97; uh *= 1099511628211UL; s += uh + ",";
            return s;
        }

        public static string t05_doubles()
        {
            string s = "";
            int i = 3, j = 0, k = -5;
            double d1 = (double)i * 2.5; s += Bits(d1) + ",";
            double d2 = (double)j * (double)k; s += Bits(d2) + ",";    // -0.0
            double d3 = -(double)j; s += Bits(d3) + ",";                   // -0.0
            int big = 2000000001;
            double d4 = (double)big * (double)big * 3.0; s += Bits(d4) + ",";
            double d5 = (double)big * (double)big + 1.0; s += Bits(d5) + ",";
            double zero = 0.0;
            s += Bits(1.0 / zero) + "," + Bits(-1.0 / zero) + "," + Bits(zero / zero) + ",";
            s += Bits(5.5 % 2.0) + "," + Bits(-5.5 % 2.0) + "," + Bits(5.0 % zero) + ",";
            double h = (double)i / (double)k; s += Bits(h) + ",";
            long lb = 9007199254740993L; double d6 = (double)lb; s += Bits(d6) + ",";
            double nz = -0.0; s += Bits(nz * 1.0) + ",";
            double acc = 0; for (int t = 0; t < 10; t++) acc += 0.1; s += Bits(acc) + ",";
            double y = 2.0; y *= i; y -= k; y /= 3; s += Bits(y) + ",";
            s += Bits(Math.Sqrt(2.0)) + "," + Bits(Math.Abs(-3.25)) + "," + Bits(Math.Floor(-2.5)) + ",";
            s += (i < 2.5 ? 1 : 0) + ",";
            return s;
        }

        public static string t06_float32()
        {
            string s = "";
            float f = 0.1f; s += Bits(f) + ",";
            float g = f * 3.0f; s += Bits(g) + ",";
            float h = 16777216.0f; h += 1.0f; s += Bits(h) + ",";
            double dd = 0.1; float fd = (float)dd; s += Bits(fd) + ",";
            int big = 16777217; float fi = big; s += Bits(fi) + ",";
            float q = 1.0f / 3.0f; s += Bits(q) + ",";
            float r = (float)(1e40); s += Bits(r) + ",";
            float acc = 0; for (int t = 0; t < 10; t++) acc += 0.1f; s += Bits(acc) + ",";
            double mixed = f * 2.0; s += Bits(mixed) + ",";
            return s;
        }

        public static string t07_f2i()
        {
            string s = "";
            double[] v = { 2.9, -2.9, 1e10, -1e10, double.NaN, 2147483647.5, -2147483648.9 };
            foreach (var x in v) s += (int)x + "," + (long)x + ",";
            double d37 = 3.7, d300 = 300.5, d40k = -40000.0; s += (uint)d37 + "," + (byte)d300 + "," + (short)d40k + ",";
            s += global::Cpp.Conv.D2I(1e20) + ",";
            return s;
        }

        public static string t08_loops()
        {
            string s = "";
            // for with continue: the step runs
            for (int i = 0; i < 6; i++) { if (i % 2 == 0) continue; s += i; }
            s += ",";
            // do-while with continue: the condition runs
            int k = 0;
            do { k++; if (k == 2) continue; s += k; } while (k < 5);
            s += ",";
            // while with an assignment in the condition
            int[] arr = { 4, 3, 0, 9 };
            int p = 0, v;
            while ((v = arr[p++]) != 0) s += v;
            s += ",";
            // the variable used after the loop
            int j;
            for (j = 0; j < 4; j++) { }
            s += j + ",";
            // nested loops, break
            for (int a = 0; a < 3; a++)
                for (int b = 0; b < 3; b++) { if (b > a) break; s += a * 10 + b + ";"; }
            s += ",";
            // a counter declared before the loop and not used after
            {
                int q = 2;
                for (; q < 5; ++q) s += q;
            }
            s += ",";
            // a counter declared before the loop and reused by an outer loop
            int r = 0;
            for (int outer = 0; outer < 2; outer++)
                for (; r < 3; r++) s += r;
            s += "," + r + ",";
            // down-counting
            for (int d = 3; d >= 0; d--) s += d;
            s += ",";
            // a bound the body changes
            int lim = 3;
            for (int e = 0; e < lim; e++) { if (e == 0) lim = 5; s += e; }
            s += ",";
            // unsigned down loop with wrap
            uint cntu = 0;
            for (uint uu = 3; uu < 10; uu--) cntu++;
            s += cntu + ",";
            // field bound
            var h = new Holder();
            h.i = 4;
            for (int z = 0; z < h.i; z++) { if (z == 1) h.i = 2; s += z; }
            s += ",";
            for (int z = 0; z < Holder.sfield; z++) s += z;
            return s;
        }

        public static string t09_switch()
        {
            string s = "";
            for (int i = 0; i < 7; i++)
            {
                switch (i)
                {
                    case 0: s += "a"; break;
                    case 1:
                    case 2: s += "b"; if (i == 2) break; s += "c"; break;
                    default: s += "d"; break;
                    case 4: s += "e"; continue;
                    case 5: if (i == 5) { s += "f"; break; } else { s += "g"; break; }
                }
                s += i;
            }
            long key = 1L << 40;
            switch (key) { case 1L << 40: s += "L"; break; default: s += "M"; break; }
            return s;
        }

        public static int t10_goto()
        {
            int n = 0, i = 0;
            L1:
            if (i < 5)
            {
                n += i;
                i++;
                if (n > 6) goto L2;
                goto L1;
            }
            n += 100;
            L2:
            n += 1000;
            return n;
        }

        public static string t11_lambdas()
        {
            string s = "";
            var fns = new IntFn[3];
            for (int i = 0; i < 3; i++) { int c = i * 10; fns[i] = () => c; }
            for (int i = 0; i < 3; i++) s += fns[i]() + ",";
            int total = 0;
            IntFn add = () => { total += 5; return total; };
            add(); add();
            s += total + ",";
            var fs2 = new IntFn[3];
            for (int i = 0; i < 3; i++) { int c = i; fs2[i] = () => { c += 100; return c; }; }
            s += fs2[1]() + "," + fs2[1]() + "," + fs2[2]() + ",";
            Func<int, int> sq = x => x * x;
            s += sq(7) + ",";
            Func<int, int> fact = null;
            fact = x => x <= 1 ? 1 : x * fact(x - 1);
            s += fact(6) + ",";
            int loopShared = 0;
            var fs3 = new IntFn[2];
            for (int i = 0; i < 2; i++) fs3[i] = () => loopShared + 1;
            loopShared = 50;
            s += fs3[0]() + ",";
            return s;
        }

        static void Bump(ref int x) { x += 3; }
        static void Out(out double d) { d = 2.5; }
        static ref int Elem(int[] a, int i) => ref a[i];
        static void Swap(ref int a, ref int b) { int t = a; a = b; b = t; }

        public static string t12_refs()
        {
            string s = "";
            int x = 1; Bump(ref x); s += x + ",";
            double d; Out(out d); s += Bits(d) + ",";
            int[] arr = { 1, 2, 3 };
            Bump(ref arr[1]); s += arr[1] + ",";
            Elem(arr, 2) = 9; s += arr[2] + ",";
            Elem(arr, 0) += 4; s += arr[0] + ",";
            ref int r = ref arr[0]; r = 77; s += arr[0] + ",";
            var h = new Holder(); h.i = 4; Bump(ref h.i); s += h.i + ",";
            Bump(ref Holder.sfield); s += Holder.sfield + ",";
            int p = 1, q = 2; Swap(ref p, ref q); s += p + ":" + q + ",";
            RefFn rf = (ref double z) => { z *= 2; };
            double w = 3; rf(ref w); s += Bits(w) + ",";
            return s;
        }

        public static string t13_classes()
        {
            string s = "";
            Base b = new Derived();
            s += b.V() + "," + b.NV() + "," + ((Derived)b).NV() + "," + ((Derived)b).Both() + "," + b.a + ",";
            Base b2 = new Base();
            s += b2.V() + "," + (b2 is Derived ? 1 : 0) + "," + (b is Derived ? 1 : 0) + ",";
            Derived dd = b as Derived;
            s += (dd != null ? 1 : 0) + ",";
            Holder hh = null;
            s += (hh?.arr == null ? "null" : "x") + ",";
            return s;
        }

        public static string t14_order()
        {
            string s = "";
            int i = 0;
            int[] a = new int[4];
            a[i++] += 5; s += i + ":" + a[0] + ",";
            Ctr = 0;
            s += F2(Next(), Next()) + ",";
            int x = 5;
            int y = x++ + x++; s += x + ":" + y + ",";
            int z = ++x * 2; s += z + ",";
            var h = new Holder();
            h.i = 10;
            int w = h.i++ + h.i; s += w + ":" + h.i + ",";
            h.arr[1] = 3;
            h.arr[1] *= 1000000000; s += h.arr[1] + ",";
            int t = 0;
            t = (t = 4) + t; s += t + ",";
            double dd = 1.0;
            dd += dd++ ; s += Bits(dd) + ",";
            return s;
        }
        static int F2(int a, int b) => a * 10 + b;

        public static string t15_exceptions()
        {
            string s = "";
            try { throw new MyErr(); }
            catch (MyErr e) { s += e.code + ","; }
            finally { s += "fin,"; }
            try { try { throw new MyErr(); } catch (global::Cpp.CppException) { s += "inner,"; throw; } }
            catch (MyErr e2) { s += "outer" + e2.code + ","; }
            return s;
        }

        public static string t16_static()
        {
            return TableSum + "," + Table.Length + "," + Bits(Holder.sd);
        }

        public static string t17_misc()
        {
            string s = "";
            bool f1 = true, f2 = false;
            s += (f1 & f2) + "," + (f1 | f2) + "," + (f1 ^ f2) + "," + (!f1) + ",";
            bool both = ((f1 ? 1 : 0) & (f2 ? 1 : 0)) != 0; s += both + ",";
            int mx = global::Cpp.M.max(3, Next()); s += mx + ",";
            double cl = global::Cpp.M.clamp(5.5, 0.0, 2.0); s += Bits(cl) + ",";
            char c = 'x'; s += c + ",";
            s += "q" + 'z' + ",";
            int cond = 5;
            int res = cond > 3 ? cond * 2 : cond - 1; s += res + ",";
            long acc = 0;
            for (long li = 0; li < 5L; li++) acc += li * li;
            s += acc + ",";
            ulong mask = 0;
            for (int bit = 0; bit < 64; bit += 9) mask |= 1UL << bit;
            s += mask + ",";
            return s;
        }
    }
}
