// Differential tests (second batch) for tools/cs2x/Cs2Py.cs.
// Written by Claude (Anthropic) for the SWMM6 ports; see AI-USE.md.
#pragma warning disable
using System;

namespace openswmm.dt
{
    public class Res : IDisposable
    {
        public static string log = "";
        public int id;
        protected Res(global::Cpp.Raw _) { }
        public Res() : this(global::Cpp.Raw.V) { }
        public void Dispose() { log += "D" + id; }
    }
    public class Late
    {
        public static int x = Early.y * 2;
    }
    public class Early
    {
        public static int y = 21;
    }
    public class Kw
    {
        public int from;
        public int lambda;
        public int print;
        public int __dunder;
        protected Kw(global::Cpp.Raw _) { }
        public Kw() : this(global::Cpp.Raw.V) { }
        public int @in() => from + lambda;
        public void __init_d() { from = 1; lambda = 2; print = 3; __dunder = 4; }
    }
    public class VBase
    {
        protected VBase(global::Cpp.Raw _) { }
        public VBase() : this(global::Cpp.Raw.V) { }
        public virtual int F() => 1;
        public int CallF() => F();
    }
    public class VMid : VBase
    {
        protected VMid(global::Cpp.Raw _) : base(_) { }
        public VMid() : this(global::Cpp.Raw.V) { }
        public new virtual int F() => 2;
    }
    public class VLeaf : VMid
    {
        protected VLeaf(global::Cpp.Raw _) : base(_) { }
        public VLeaf() : this(global::Cpp.Raw.V) { }
        public override int F() => 3;
    }

    public static partial class _F
    {
        static string Bits2(double d) => BitConverter.DoubleToInt64Bits(d).ToString();
        static int calls;
        static int Side(int v) { calls++; return v; }

        public static string t20_narrow_compound()
        {
            string s = "";
            byte b = 100; b += 200; s += b + ",";
            b = 10; b -= 20; s += b + ",";
            b = 16; b *= 17; s += b + ",";
            sbyte sb = 100; sb += 100; s += sb + ",";
            short sh = 30000; sh += 30000; s += sh + ",";
            ushort us = 1; us -= 2; s += us + ",";
            char c = 'a'; c += (char)2; s += (int)c + ",";
            int ci = 'z' - 'a'; s += ci + ",";
            uint u = 5; u -= 7; s += u + ",";
            u = 3; u *= 0x80000001; s += u + ",";
            int x = 0x40000000; x <<= 2; s += x + ",";
            x = -1; x >>= 4; s += x + ",";
            uint ux = 0xFFFFFFFF; ux >>= 4; s += ux + ",";
            long l = 1; l <<= 63; s += l + ",";
            int m = 7; m %= -3; s += m + ",";
            int[] rv = { -7, 7, -1, 0, int.MinValue };
            foreach (var q in rv) s += (q % 3) + ":" + (q / 3) + ":" + (q % 1) + ";";
            m = -7; m /= 2; s += m + ",";
            int bits = -1; bits &= 0xFF; s += bits + ",";
            int nb = -256; s += (nb & 0xFF) + "," + (nb | 3) + "," + (nb ^ -1) + ",";
            float f = 1.0f; f *= 1.1f; s += Bits2(f) + ",";
            float g = 1.0f; double dd = 0.1; g += (float)dd; s += Bits2(g) + ",";
            double d = 1.0; d /= 0.0; s += Bits2(d) + ",";
            int[] arr = { 1, 2, 3 };
            int k = 0;
            arr[k++] *= 1000000; s += arr[0] + ":" + k + ",";
            arr[Side(1)] += Side(5); s += arr[1] + ":" + calls + ",";
            return s;
        }

        public static string t21_mixed_sign()
        {
            string s = "";
            uint a = 1;
            s += (a - 2 > 5 ? "T" : "F") + ",";
            int i = -1; uint u = 1;
            s += (i < u ? "T" : "F") + ",";          // long comparison: -1 < 1
            long sum = i + u; s += sum + ",";
            ulong ul = 5; long sl = -3;
            s += (long)(ul + (ulong)sl) + ",";
            ulong big = 0xFFFFFFFF00000000UL;
            s += (int)(big >> 32) + "," + (uint)(big >> 32) + ",";
            int p = int.MaxValue, q = 1;
            double dsum = (double)(p + q); s += Bits2(dsum) + ",";
            int r = p > 0 ? p + q : p - q; s += r + ",";
            long lr = (long)(p + q); s += lr + ",";
            long lr2 = (long)p + q; s += lr2 + ",";
            s += Math.Abs(-5) + "," + Bits2(Math.Floor(-0.5)) + ",";
            s += global::Cpp.M.min(p > 0 ? 3 : 4, 2) + ",";
            uint w = 0; w--; ulong ww = w; s += ww + ",";
            return s;
        }

        public static string t22_names()
        {
            string s = "";
            int len = 5; int str = 3; int range = 2; int M = 7; int Str = 1; int self = 9; int None_ = 4; int abs = 6;
            s += (len + str + range + M + Str + self + None_ + abs) + ",";
            var kw = new Kw(); kw.__init_d();
            s += kw.@in() + "," + kw.print + "," + kw.__dunder + ",";
            VBase v = new VLeaf();
            s += v.F() + "," + v.CallF() + "," + ((VMid)v).F() + ",";
            return s;
        }

        public static string t23_using_static()
        {
            Res.log = "";
            using (var r = new Res() { id = 1 })
            {
                Res.log += "B";
            }
            {
                using var r2 = new Res() { id = 2 };
                Res.log += "C";
            }
            return Res.log + "," + Late.x + "," + Early.y;
        }

        public static string t24_loops2()
        {
            string s = "";
            for (int i = 0, j = 5; i < j; i++, j--) s += i * 10 + j + ";";
            s += ",";
            int n = 0;
            while (true) { n++; if (n > 3) { if (n == 4) break; } }
            s += n + ",";
            int[] src = { 5, 0, 2 };
            int idx = 0, got;
            do { got = src[idx++]; s += got; } while (got != 0 && idx < 3);
            s += ",";
            int[][] grid = new int[2][];
            grid[0] = new int[] { 1, 2 }; grid[1] = new int[3];
            grid[1][2] = 8;
            s += grid[0][1] + grid[1][2] + "," + grid[1].Length + ",";
            int acc = 0;
            foreach (var g in grid[0]) acc += g;
            s += acc + ",";
            Func<int, bool> ok = null;
            int found = 0;
            for (int t = 0; t < 5; t++) { ok = z => z == t; if (ok(3)) found = t; }
            s += found + "," + ok(4) + ",";
            int cnt = 0;
            for (int e = 10; e > 0; e -= 3) cnt++;
            s += cnt + ",";
            return s;
        }

        public static int sval;
        public static string t25_globals_in_lambdas()
        {
            sval = 0;
            Action bump = () => { sval += 2; };
            bump(); bump();
            Func<int> get = () => sval++;
            int a = get();
            int b2 = get();
            return sval + "," + a + "," + b2;
        }

        public static string t26_logical_pre()
        {
            string s = "";
            int x = 0;
            int[] arr = { 1, 2, 3 };
            int k = 0;
            if (arr.Length > 2 && (x = arr[k++] + 10) > 5) s += "A" + x + k;
            bool f = false;
            if (f && arr[k++] > 0) s += "B";
            s += "," + k + ",";
            Res nullRes = null;
            s += (nullRes?.id ?? -1) + ",";
            var h = new Holder() { i = 5, d = 2.5 };
            s += h.i + "," + Bits2(h.d) + ",";
            return s;
        }
    }
}
