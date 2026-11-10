// differential test of the C# -> TypeScript translation of control flow and references (scratch)
namespace openswmm
{
    public delegate int FnI();
    public class BaseK
    {
        public int a; public long trace;
        protected BaseK(global::Cpp.Raw _) { trace = trace * 10 + 1; }
        public BaseK() : this(global::Cpp.Raw.V) { a = 5; trace = trace * 10 + 2; }
        public int Get() => a;                       // non-virtual, hidden in DerK
        public virtual int V() => 100 + a;
        public int CallGet() => Get();
        public int CallV() => V();
    }
    public class DerK : BaseK
    {
        public int b;
        protected DerK(global::Cpp.Raw _) : base(global::Cpp.Raw.V) { trace = trace * 10 + 3; }
        public DerK() : this(global::Cpp.Raw.V) { b = 7; trace = trace * 10 + 4; }
        public new int Get() => b;
        public override int V() => 200 + b + base.V();
    }
    public class Chain
    {
        public long t;
        public Chain(int x) : this(x, x * 2) { t = t * 100 + x; }
        public Chain(int x, int y) : this(x, y, x + y) { t = t * 100 + y; }
        public Chain(int x, int y, int z) { t = z; }
    }
    public class Holder { public int f; public int[] arr = new int[3]; public static int s; public Holder Next; public int Inc() { return ++f; } }
    public static class SInitA { public static int x = SInitB.y + 1; public static Holder h = new Holder(); }
    public static class SInitB { public static int y = Seed(); static int Seed() => 41; }
    public static class FlowTest
    {
        static void Swap(ref int p, ref int q) { int t = p; p = q; q = t; }
        static void SetOut(out long o, int k) { o = k * 3L; }
        static ref int Pick(int[] arr, int i) => ref arr[i];
        static int Thrower(int k) { if (k > 2) throw new System.InvalidOperationException("k"); return k; }

        public static long T1(long x)
        {
            // a local declared in one switch section and assigned in another
            long r = 0;
            for (int k = 0; k < 3; k++)
            {
                switch ((int)(x + k) % 3)
                {
                    case 0: int v = 10; r += v; break;
                    case 1: v = 20; r += v * 2; break;
                    default: v = 30; r = r * 3 + v; break;
                }
            }
            return r;
        }
        public static long T2(long x)
        {
            // closures over a for variable (one variable), a foreach variable and a body local (one per iteration)
            var fs = new FnI[6];
            for (int i = 0; i < 3; i++) { int j = i * 10; fs[i] = () => i + j; }
            int n = 0;
            foreach (var e in new int[] { 7, 8, 9 }) { fs[3 + n] = () => e + (int)x; n++; }
            long r = 0;
            foreach (var f in fs) r = r * 100 + f();
            return r;
        }
        public static long T3(long x)
        {
            int a = (int)x, b = 3;
            Swap(ref a, ref b);
            SetOut(out long o, a);
            var arr = new int[] { 1, 2, 3 };
            Pick(arr, 1) += 40;
            ref int r1 = ref arr[2];
            r1 *= 5;
            var h = new Holder();
            Swap(ref h.f, ref arr[0]);
            Swap(ref Holder.s, ref h.f);
            return a * 1000000L + b * 10000L + o * 100 + arr[0] + arr[1] * 7 + arr[2] * 11 + h.f * 13 + Holder.s * 17;
        }
        public static long T4(long x)
        {
            long t = 0;
            for (int k = 0; k < 5; k++)
            {
                try
                {
                    try { t = t * 10 + Thrower(k + (int)x); if (k == 1) continue; t += 1; }
                    finally { t = t * 2; }
                }
                catch (System.Exception) { t = t * 3 + 1; }
            }
            return t;
        }
        public static long T5(long x)
        {
            long s = 0; int k = 0;
        top:
            s += k;
            k++;
            if (k < (int)(x & 7) + 3) goto top;
            if (s > 10) goto big;
            s = -s;
            goto done;
        big:
            s *= 2;
        done:
            return s;
        }
        public static long T6(long x)
        {
            var h = x > 0 ? new Holder() { f = (int)x, Next = new Holder() { f = 2 } } : null;
            int? a = h?.f;
            int b = h?.Next?.f ?? -1;
            h?.Inc();
            long c = h?.Next.Inc() ?? 99;
            return (a ?? -5) * 1000 + b * 100 + c * 10 + (h == null ? 7 : h.f);
        }
        public static long T7(long x)
        {
            var d = new DerK();
            BaseK bk = d;
            return d.trace * 1000000 + bk.Get() * 10000 + d.Get() * 1000 + bk.V() + bk.CallGet() * 100000 + bk.CallV() * 10;
        }
        public static long T8(long x)
        {
            var c = new Chain((int)x);
            return c.t;
        }
        public static long T9(long x) => SInitA.x * 1000 + (SInitA.h != null ? 1 : 0) + SInitB.y;
        public static long T10(long x)
        {
            var h = new Holder() { f = 1 };
            FnI g = h.Inc;
            h = new Holder() { f = 100 };
            return g() * 1000 + g() + h.f;
        }
        public static long T11(long x)
        {
            switch (x)
            {
                case 9223372036854775807L: return 1;
                case -9223372036854775808L: return 2;
                case 0: return 3;
                default: return 4;
            }
        }
        public static long T12(long x)
        {
            int k = (int)x;
            int q = 0;
            try { q = 1000 / (k & 3); } catch (System.Exception) { q = -1; }
            uint u = (uint)k;
            int bits = 0;
            while (u != 0) { bits += (int)(u & 1); u >>= 1; }
            do { bits++; } while (bits < 3);
            return q * 100 + bits;
        }
        public static long T13(long x)
        {
            int i = 0; long s = 0;
            for (int j = 0, m = (int)(x & 15); j < m; j += 2, i++) { if (j == 6) continue; s += j * i; if (s > 50) break; }
            return s * 100 + i;
        }
    }
}
