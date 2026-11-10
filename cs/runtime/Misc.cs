// Misc.cs - the rest of the small runtime the generated SWMM6 engine calls: moves and out-parameter copy-back
// (Mv), <algorithm> (Algo), x86-64 float-to-integer conversion (Conv), libm functions .NET does not route to glibc
// (LibM), raw memory on typed arrays (Mem, Bytes), tuples, atomics and locks for one thread, and C library extras.
// Written by Claude (Anthropic) for the SWMM6 ports; see AI-USE.md.
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Cpp
{
    /// <summary>The argument of the constructors that leave an object uninitialized for an __init method.</summary>
    public readonly struct Raw { public static readonly Raw V = default; }

    /// <summary>A C++ pointer to data member (T C::*): given the object, a reference to that field of it.</summary>
    public delegate ref TF MemPtr<TO, TF>(TO o);

    public interface IDestroyElems { void DestroyElems(); }

    /// <summary>Runs a C++ destructor: an engine class's Dispose (its destructor body, members, base), a file
    /// stream's close, a container's elements. The emitter calls it only for types whose destruction has an effect.</summary>
    public static class Dtor
    {
        public static void Run(object? o)
        {
            switch (o)
            {
                case null: return;
                case IDisposable d: d.Dispose(); return;
                case IDestroyElems c: c.DestroyElems(); return;
                case Array arr: foreach (var x in arr) Run(x); return;
            }
        }
    }

    /// <summary>A destructor deferred to the end of the enclosing C# block (a using declaration), unless the
    /// object is returned to the caller.</summary>
    public sealed class Defer : IDisposable
    {
        Action? a;
        public Defer(Action a) { this.a = a; }
        public void Cancel() { a = null; }
        public void Dispose() { var x = a; a = null; x?.Invoke(); }
    }

    public static class Mv
    {
        /// <summary>unique_ptr::reset(p) and unique_ptr assignment: store the new pointer, then delete the old one.</summary>
        public static void Reset<T>(ref T p, T q) where T : class? { var old = p; p = q; if (old != null && !ReferenceEquals(old, q)) Dtor.Run(old); }
        /// <summary>Copy a boxed out-parameter back into its variable after the call produced value v.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static R Back<R, T>(R v, ref T x, T[] box) { x = box[0]; return v; }

        /// <summary>std::move of a container or class into a new owner: the source is left empty.</summary>
        public static T Take<T>(ref T x) { var r = x; x = Elem<T>.Make(); return r; }
        /// <summary>std::move of a unique_ptr or std::function: the source is left null.</summary>
        public static T TakeRef<T>(ref T x) { var r = x; x = default!; return r; }

        public static Ptr<T> PostStep<T>(ref Ptr<T> p, int d) { var r = p; p = p + (long)d; return r; }
        public static Iter<T> PostStep<T>(ref Iter<T> p, int d) { var r = p; p = p + d; return r; }
        public static CPtr PostStep(ref CPtr p, int d) { var r = p; p = p + d; return r; }
        public static MIt<E> PostStep<E>(ref MIt<E> p, int d) { var r = p; p = d > 0 ? ++p : --p; return r; }
        public static RevIter<T> PostStep<T>(ref RevIter<T> p, int d) { var r = p; p = ++p; return r; }

        public static void Delete(object? o) { (o as IDisposable)?.Dispose(); }
    }

    public static class Conv
    {
        // x86-64 cvttsd2si: NaN and out-of-range give the "integer indefinite" value
        public static int D2I(double x) => (x > -2147483649.0 && x < 2147483648.0) ? (int)x : int.MinValue;
        public static long D2L(double x) => (x >= -9223372036854775808.0 && x < 9223372036854775808.0) ? (long)x : long.MinValue;
        // GCC's double -> unsigned int: the 64-bit conversion, truncated
        public static uint D2U(double x) => unchecked((uint)D2L(x));
        // GCC's double -> unsigned long: below 2^63 the signed conversion, above it the conversion of x - 2^63
        public static ulong D2UL(double x)
        {
            if (x < 9223372036854775808.0) return unchecked((ulong)D2L(x));
            return unchecked((ulong)D2L(x - 9223372036854775808.0) ^ 0x8000000000000000UL);
        }
        public static short D2S(double x) => unchecked((short)D2I(x));
        public static ushort D2US(double x) => unchecked((ushort)D2I(x));
        public static sbyte D2SB(double x) => unchecked((sbyte)D2I(x));
        public static byte D2B(double x) => unchecked((byte)D2I(x));
    }

    /// <summary>libm functions .NET implements in managed code or not at all: called from glibc directly.</summary>
    public static class LibM
    {
        // C nextafter: the next double after x toward y (y itself when equal, NaN if either is NaN)
        public static double nextafter(double x, double y)
        {
            if (double.IsNaN(x) || double.IsNaN(y)) return x + y;
            if (x == y) return y;
            return y > x ? System.Math.BitIncrement(x) : System.Math.BitDecrement(x);
        }
        const string M = "libm.so.6";
        [DllImport(M)] public static extern double hypot(double x, double y);
        [DllImport(M)] public static extern double fmod(double x, double y);
        [DllImport(M)] public static extern double round(double x);
        [DllImport(M)] public static extern long lround(double x);
        [DllImport(M)] public static extern double expm1(double x);
        [DllImport(M)] public static extern double log1p(double x);
        [DllImport(M)] public static extern double rint(double x);
        [DllImport(M)] public static extern double nearbyint(double x);
        [DllImport(M)] public static extern double erf(double x);
        [DllImport(M)] public static extern double erfc(double x);
        [DllImport(M)] public static extern double tgamma(double x);
        [DllImport(M)] public static extern double lgamma(double x);
        [DllImport(M)] public static extern double exp2(double x);
        [DllImport(M)] public static extern double remainder(double x, double y);
        [DllImport(M)] public static extern double fdim(double x, double y);
        [DllImport(M)] public static extern double fmax(double x, double y);
        [DllImport(M)] public static extern double fmin(double x, double y);
        [DllImport(M)] public static extern double log(double x);
    }

    public static class Std
    {
        /// <summary>std::rethrow_exception: throw the stored exception again, its original stack kept.</summary>
        public static void Rethrow(Exception e) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e).Throw(); }
        /// <summary>std::call_once: run f the first time only (single-threaded; an exception leaves the flag unset).</summary>
        public static void call_once(OnceFlag flag, Action f) { if (flag.done) return; f(); flag.done = true; }
        public static CSpan<T> Span<T>() => default;
        public static CSpan<T> Span<T>(Vec<T> v) => new CSpan<T>(v.a, 0, v.n);
        public static CSpan<T> Span<T>(T[] arr) => new CSpan<T>(arr, 0, arr.Length);
        public static CSpan<T> Span<T>(Ptr<T> p, ulong n) => new CSpan<T>(p.a, p.o, (long)n);
        public static CSpan<T> Span<T>(CSpan<T> s) => s;
        /// <summary>std::get&lt;i&gt; of a pair or tuple (structured bindings of map entries and emplace results).</summary>
        public static ref A get0<A, B>(Pair<A, B> p) => ref p.first;
        public static ref B get1<A, B>(Pair<A, B> p) => ref p.second;
        public static ref A get0<A, B>(Tup<A, B> t) => ref t.i0;
        public static ref B get1<A, B>(Tup<A, B> t) => ref t.i1;
        public static ref A get0<A, B, C>(Tup<A, B, C> t) => ref t.i0;
        public static ref B get1<A, B, C>(Tup<A, B, C> t) => ref t.i1;
        public static ref C get2<A, B, C>(Tup<A, B, C> t) => ref t.i2;
        public static readonly double QNaN = BitConverter.Int64BitsToDouble(0x7FF8000000000000L);   // C++'s quiet NaN
        public static readonly double SNaN = BitConverter.Int64BitsToDouble(0x7FF4000000000000L);
        public static readonly float QNaNf = BitConverter.Int32BitsToSingle(0x7FC00000);
        public static void swap<T>(ref T a, ref T b) { T t = a; a = b; b = t; }
        public static T exchange<T>(ref T a, T v) { T old = a; a = v; return old; }
        public static T release<T>(ref T a) { T r = a; a = default!; return r; }
        public static int iabs(int x) => x < 0 ? -x : x;
        public static long iabs(long x) => x < 0 ? -x : x;
        public static int Nop(object? x) => 0;
        public static bool OptCmp<T>(Opt<T> a, object? b, string op)
        {
            bool eq;
            if (b == null) eq = !a.has;
            else if (b is Opt<T> o) eq = a.has == o.has && (!a.has || EqualityComparer<T>.Default.Equals(a.val, o.val));
            else eq = a.has && EqualityComparer<T>.Default.Equals(a.val, (T)b);
            return op == "==" ? eq : !eq;
        }
        /// <summary>std::from_chars for double (chars_format::general), as libstdc++ has it: the value strtod reads
        /// from the same characters (both are correctly rounded), except that a sign other than '-', leading white
        /// space and a hexadecimal prefix are not read ("0x10" is 0 and stops at the x), and a value too large or too
        /// small for a double (an overflow to inf, or a nonzero number read as zero) is result_out_of_range (34) with
        /// the value left unchanged. As the Java and Go runtimes have it (java/src/cpp/Std.java).</summary>
        public static FromCharsResult from_chars(CPtr first, CPtr last, ref double v)
        {
            int n = last.O - first.O;
            var tmp = new byte[n + 2];
            if (n > 0) Array.Copy(first.B, first.O, tmp, 0, n);
            int k = tmp[0] == (byte)'-' ? 1 : 0;
            int c = tmp[k];
            int lc = c >= 'A' && c <= 'Z' ? c + 32 : c;
            if (!((c >= '0' && c <= '9') || c == '.' || lc == 'i' || lc == 'n')) return new FromCharsResult { ptr = first, ec = 22 };
            if (c == '0' && (tmp[k + 1] == (byte)'x' || tmp[k + 1] == (byte)'X')) tmp[k + 1] = 0;   // read the 0 alone
            var p = new CPtr(tmp, 0); var end = p;
            double r = R.strtod(p, ref end);
            if (end.O == 0) return new FromCharsResult { ptr = first, ec = 22 };
            var res = new FromCharsResult { ptr = first + end.O, ec = 0 };
            if (double.IsInfinity(r) && lc != 'i') return new FromCharsResult { ptr = res.ptr, ec = 34 };   // overflow
            if (r == 0)
            {
                // underflow: a nonzero digit before the exponent
                for (int j = k; j < end.O; j++)
                {
                    int d = tmp[j];
                    if (d == 'e' || d == 'E') break;
                    if (d >= '1' && d <= '9') return new FromCharsResult { ptr = res.ptr, ec = 34 };
                }
            }
            v = r;
            return res;
        }
        /// <summary>std::from_chars for integers, base 10: an optional '-' (signed types only), digits; an empty
        /// match is invalid_argument (22) with ptr = first; a value out of the type's range is
        /// result_out_of_range (34), all the digits consumed, v unchanged.</summary>
        static FromCharsResult IntChars(CPtr first, CPtr last, bool signed, System.Numerics.BigInteger min, System.Numerics.BigInteger max, out System.Numerics.BigInteger r, out bool ok)
        {
            int i = first.O; r = 0; ok = false; bool neg = false;
            if (signed && i < last.O && first.B[i] == (byte)'-') { neg = true; i++; }
            int start = i;
            while (i < last.O && first.B[i] >= (byte)'0' && first.B[i] <= (byte)'9') { r = r * 10 + (first.B[i] - (byte)'0'); i++; }
            if (i == start) return new FromCharsResult { ptr = first, ec = 22 };
            if (neg) r = -r;
            if (r < min || r > max) return new FromCharsResult { ptr = new CPtr(first.B, i), ec = 34 };
            ok = true;
            return new FromCharsResult { ptr = new CPtr(first.B, i), ec = 0 };
        }
        public static FromCharsResult from_chars(CPtr first, CPtr last, ref int v)
        { var res = IntChars(first, last, true, int.MinValue, int.MaxValue, out var r, out var ok); if (ok) v = (int)r; return res; }
        public static FromCharsResult from_chars(CPtr first, CPtr last, ref long v)
        { var res = IntChars(first, last, true, long.MinValue, long.MaxValue, out var r, out var ok); if (ok) v = (long)r; return res; }
        public static FromCharsResult from_chars(CPtr first, CPtr last, ref uint v)
        { var res = IntChars(first, last, false, uint.MinValue, uint.MaxValue, out var r, out var ok); if (ok) v = (uint)r; return res; }
        public static FromCharsResult from_chars(CPtr first, CPtr last, ref ulong v)
        { var res = IntChars(first, last, false, ulong.MinValue, ulong.MaxValue, out var r, out var ok); if (ok) v = (ulong)r; return res; }
    }

    public struct FromCharsResult { public CPtr ptr; public int ec; }

    public sealed class ErrorCode
    {
        public int v;
        public bool Bool() => v != 0;
        public int value() => v;
        public void clear() => v = 0;
        /// <summary>glibc's strerror text for the errno values the filesystem calls set.</summary>
        public Str message() => new Str(v switch
        {
            0 => "Success", 2 => "No such file or directory", 13 => "Permission denied", 17 => "File exists",
            20 => "Not a directory", 21 => "Is a directory", 95 => "Operation not supported",
            _ => "Unknown error " + v,
        });
    }

    public sealed class Atomic<T> : ICopy<Atomic<T>>
    {
        public T v;
        public Atomic() { v = default!; }
        public Atomic(T x) { v = x; }
        public T exchange(T x) { var o = v; v = x; return o; }
        public T load(int order = 0) => v;
        public void store(T x, int order = 0) { v = x; }
        public T fetch_add(T d, int order = 0) { var o = v; v = Num.Add(v, d); return o; }
        public T fetch_sub(T d, int order = 0) { var o = v; v = Num.Sub(v, d); return o; }
        // compare_exchange_weak/strong on one thread: never fails spuriously
        public bool compare_exchange(ref T expected, T desired)
        {
            if (EqualityComparer<T>.Default.Equals(v, expected)) { v = desired; return true; }
            expected = v;
            return false;
        }
        public Atomic<T> Clone() => new Atomic<T>(v);
        public void Assign(Atomic<T> o) { v = o.v; }
    }
    /// <summary>Wrapping integer and IEEE arithmetic on a generic value (std::atomic's fetch_add).</summary>
    public static class Num
    {
        public static T Add<T>(T a, T b) => (a, b) switch
        {
            (int x, int y) => (T)(object)unchecked(x + y),
            (long x, long y) => (T)(object)unchecked(x + y),
            (ulong x, ulong y) => (T)(object)unchecked(x + y),
            (uint x, uint y) => (T)(object)unchecked(x + y),
            (double x, double y) => (T)(object)(x + y),
            _ => throw new NotSupportedException("atomic add on " + typeof(T)),
        };
        public static T Sub<T>(T a, T b) => (a, b) switch
        {
            (int x, int y) => (T)(object)unchecked(x - y),
            (long x, long y) => (T)(object)unchecked(x - y),
            (ulong x, ulong y) => (T)(object)unchecked(x - y),
            (uint x, uint y) => (T)(object)unchecked(x - y),
            (double x, double y) => (T)(object)(x - y),
            _ => throw new NotSupportedException("atomic sub on " + typeof(T)),
        };
    }
    public sealed class Mutex { }
    /// <summary>The ports run single-threaded: the engine's one worker thread (the output writer, IOThread) is
    /// replaced by a synchronous call (Hand_IOThread.cs), so these never block.</summary>
    public sealed class CondVar { }
    public sealed class Thread { public bool joinable() => false; public void join() { } }
    public sealed class Lock { }

    public sealed class Tup<A, B> : ICopy<Tup<A, B>>
    {
        public A i0; public B i1;
        public Tup() { i0 = Elem<A>.Make(); i1 = Elem<B>.Make(); }
        public Tup(A a, B b) { i0 = a; i1 = b; }
        public Tup<A, B> Clone() => new Tup<A, B>(Elem<A>.Copy(i0), Elem<B>.Copy(i1));
        public void Assign(Tup<A, B> o) { i0 = Elem<A>.Copy(o.i0); i1 = Elem<B>.Copy(o.i1); }
    }
    public sealed class Tup<A, B, C> : ICopy<Tup<A, B, C>>
    {
        public A i0; public B i1; public C i2;
        public Tup() { i0 = Elem<A>.Make(); i1 = Elem<B>.Make(); i2 = Elem<C>.Make(); }
        public Tup(A a, B b, C c) { i0 = a; i1 = b; i2 = c; }
        public Tup<A, B, C> Clone() => new Tup<A, B, C>(Elem<A>.Copy(i0), Elem<B>.Copy(i1), Elem<C>.Copy(i2));
        public void Assign(Tup<A, B, C> o) { i0 = Elem<A>.Copy(o.i0); i1 = Elem<B>.Copy(o.i1); i2 = Elem<C>.Copy(o.i2); }
    }

    /// <summary>Raw memory on typed arrays: memcpy, memset, fread / fwrite and byte views of numbers.</summary>
    public static class Mem
    {
        /// <summary>calloc(n, size) cast to T*: n*size bytes of zeroed T. Zero bytes give an empty array, which,
        /// like glibc's unique pointer for a zero-size request, is never dereferenced. free() is the collector's.</summary>
        /// <summary>&amp;obj - p: the index of an object of an array of objects (pointers to objects are references).</summary>
        public static long Diff<T>(T obj, Ptr<T> p) where T : class => Array.IndexOf(p.a, obj) - p.o;
        public static long Diff<T>(Ptr<T> a, T obj) where T : class => a.o - Array.IndexOf(a.a, obj);
        public static Ptr<T> Calloc<T>(ulong n, ulong size) where T : struct =>
            new Ptr<T>(new T[(int)(n * size / (ulong)System.Runtime.CompilerServices.Unsafe.SizeOf<T>())], 0);
        public static void Free(object? p) { }
        /// <summary>The nbytes bytes at p. A null p with no bytes is no bytes: libstdc++ passes the data() of an
        /// empty vector, which is nullptr (Vec.data), with a length of 0 (os.write(v.data(), 0), memcpy(d, s, 0)).</summary>
        static Span<byte> Bytes<T>(Ptr<T> p, int nbytes) where T : struct =>
            p.a == null && nbytes == 0 ? Span<byte>.Empty :
            MemoryMarshal.AsBytes(new Span<T>(p.a, (int)p.o, p.a.Length - (int)p.o)).Slice(0, nbytes);
        static Span<byte> Bytes(CPtr p, int nbytes) => new Span<byte>(p.B, p.O, nbytes);

        public static Ptr<T> memcpy<T, U>(Ptr<T> d, Ptr<U> s, ulong n) where T : struct where U : struct { Bytes(s, (int)n).CopyTo(Bytes(d, (int)n)); return d; }
        public static Ptr<T> memcpy<T>(Ptr<T> d, CPtr s, ulong n) where T : struct { Bytes(s, (int)n).CopyTo(Bytes(d, (int)n)); return d; }
        public static CPtr memcpy<U>(CPtr d, Ptr<U> s, ulong n) where U : struct { Bytes(s, (int)n).CopyTo(Bytes(d, (int)n)); return d; }
        public static CPtr memcpy(CPtr d, CPtr s, ulong n) { if (n != 0) Array.Copy(s.B, s.O, d.B, d.O, (int)n); return d; }
        public static Ptr<T> memset<T>(Ptr<T> d, int c, ulong n) where T : struct { Bytes(d, (int)n).Fill((byte)c); return d; }
        public static CPtr memset(CPtr d, int c, ulong n) { new Span<byte>(d.B, d.O, (int)n).Fill((byte)c); return d; }

        public static ulong fwrite<T>(Ptr<T> p, ulong size, ulong count, CFile f) where T : struct
        {
            var b = Bytes(p, (int)(size * count)).ToArray();
            f.S.Write(b, 0, b.Length);
            return count;
        }
        public static ulong fwrite(CPtr p, ulong size, ulong count, CFile f) { if (size * count != 0) f.S.Write(p.B, p.O, (int)(size * count)); return count; }
        public static ulong fread<T>(Ptr<T> p, ulong size, ulong count, CFile f) where T : struct
        {
            int want = (int)(size * count);
            var b = new byte[want];
            int got = 0;
            while (got < want) { int k = f.S.Read(b, got, want - got); if (k <= 0) break; got += k; }
            if (got < want) f.ReadFailed();
            new Span<byte>(b, 0, got).CopyTo(Bytes(p, got));
            return size == 0 ? 0 : (ulong)got / size;
        }
        public static ulong fread(CPtr p, ulong size, ulong count, CFile f)
        {
            int want = (int)(size * count), got = 0;
            while (got < want) { int k = f.S.Read(p.B, p.O + got, want - got); if (k <= 0) break; got += k; }
            if (got < want) f.ReadFailed();
            return size == 0 ? 0 : (ulong)got / size;
        }
        public static byte[] BytesOf<T>(Ptr<T> p, int n) where T : struct => Bytes(p, n).ToArray();
        public static void BytesInto<T>(Ptr<T> p, byte[] src, int off, int n) where T : struct => new Span<byte>(src, off, n).CopyTo(Bytes(p, n));
    }

    public static class Bytes
    {
        /// <summary>The bytes of the numbers p points at, as a char* for reading (a copy).</summary>
        public static CPtr View<T>(Ptr<T> p) where T : struct
        {
            if (p.a == null && p.at == null) return CPtr.Null;     // (const char*) of a null pointer is null
            var span = MemoryMarshal.AsBytes(new Span<T>(p.a, (int)p.o, p.a.Length - (int)p.o));
            return new CPtr(span.ToArray(), 0);
        }
        public static CPtr View(CPtr p) => p;
    }

    public static class Bits
    {
        public static T Cast<T>(object x) where T : struct
        {
            switch (x)
            {
                case double d when typeof(T) == typeof(ulong): return (T)(object)(ulong)BitConverter.DoubleToInt64Bits(d);
                case double d when typeof(T) == typeof(long): return (T)(object)BitConverter.DoubleToInt64Bits(d);
                case ulong u when typeof(T) == typeof(double): return (T)(object)BitConverter.Int64BitsToDouble((long)u);
                case long l when typeof(T) == typeof(double): return (T)(object)BitConverter.Int64BitsToDouble(l);
                case float f when typeof(T) == typeof(uint): return (T)(object)(uint)BitConverter.SingleToInt32Bits(f);
                case uint u when typeof(T) == typeof(float): return (T)(object)BitConverter.Int32BitsToSingle((int)u);
            }
            throw new NotSupportedException("bit_cast " + x.GetType() + " -> " + typeof(T));
        }
    }

    /// <summary>A reference member of a class (int&amp; held by an object): the array cell it refers to.</summary>
    public delegate ref T RefGet<T>();

    /// <summary>A reference member of a class (bool&amp; held by an object): the place it refers to, an array cell or a
    /// field of some object (a lambda returning a ref to it).</summary>
    public sealed class Box<T>
    {
        public T[] a; public long o; readonly RefGet<T>? g;
        public Box(T[] a, long o) { this.a = a; this.o = o; }
        public Box(RefGet<T> g) { this.g = g; a = null!; }
        public static Box<T> Of(RefGet<T> g) => new Box<T>(g);
        public ref T V => ref (g != null ? ref g() : ref a[o]);
    }

    /// <summary>&lt;algorithm&gt; and &lt;numeric&gt; over vector / array iterators, pointers and map iterators.</summary>
    public static class Algo
    {
        public static void fill<T>(Iter<T> b, Iter<T> e, T v) { for (long i = b.i; i < e.i; i++) b.a[i] = Elem<T>.Copy(v); }
        public static void fill<T>(Ptr<T> b, Ptr<T> e, T v) { for (long i = b.o; i < e.o; i++) b.a[i] = Elem<T>.Copy(v); }
        public static Iter<T> fill_n<T>(Iter<T> b, ulong n, T v) { for (long i = 0; i < (long)n; i++) b.a[b.i + i] = Elem<T>.Copy(v); return b + (long)n; }
        public static Ptr<T> fill_n<T>(Ptr<T> b, ulong n, T v) { for (long i = 0; i < (long)n; i++) b.a[b.o + i] = Elem<T>.Copy(v); return b + (long)n; }
        public static bool any_of<T>(Iter<T> b, Iter<T> e, Func<T, bool> f) { for (long i = b.i; i < e.i; i++) if (f(b.a[i])) return true; return false; }
        public static bool all_of<T>(Iter<T> b, Iter<T> e, Func<T, bool> f) { for (long i = b.i; i < e.i; i++) if (!f(b.a[i])) return false; return true; }
        public static bool none_of<T>(Iter<T> b, Iter<T> e, Func<T, bool> f) => !any_of(b, e, f);
        public static Iter<T> find<T>(Iter<T> b, Iter<T> e, T v) { var c = EqualityComparer<T>.Default; for (long i = b.i; i < e.i; i++) if (c.Equals(b.a[i], v)) return b + (i - b.i); return e; }
        public static Iter<T> find_if<T>(Iter<T> b, Iter<T> e, Func<T, bool> f) { for (long i = b.i; i < e.i; i++) if (f(b.a[i])) return b + (i - b.i); return e; }
        public static Iter<T> find_if_not<T>(Iter<T> b, Iter<T> e, Func<T, bool> f) { for (long i = b.i; i < e.i; i++) if (!f(b.a[i])) return b + (i - b.i); return e; }
        public static long count<T>(Iter<T> b, Iter<T> e, T v) { var c = EqualityComparer<T>.Default; long n = 0; for (long i = b.i; i < e.i; i++) if (c.Equals(b.a[i], v)) n++; return n; }
        public static long count_if<T>(Iter<T> b, Iter<T> e, Func<T, bool> f) { long n = 0; for (long i = b.i; i < e.i; i++) if (f(b.a[i])) n++; return n; }
        public static double accumulate(Iter<double> b, Iter<double> e, double init) { for (long i = b.i; i < e.i; i++) init = init + b.a[i]; return init; }
        public static int accumulate(Iter<int> b, Iter<int> e, int init) { for (long i = b.i; i < e.i; i++) init = init + b.a[i]; return init; }
        public static long accumulate(Iter<long> b, Iter<long> e, long init) { for (long i = b.i; i < e.i; i++) init = init + b.a[i]; return init; }
        public static double accumulate(Iter<int> b, Iter<int> e, double init) { for (long i = b.i; i < e.i; i++) init = init + b.a[i]; return init; }
        public static ulong accumulate(Iter<ulong> b, Iter<ulong> e, ulong init) { for (long i = b.i; i < e.i; i++) init = init + b.a[i]; return init; }
        public static A accumulate<T, A>(Iter<T> b, Iter<T> e, A init, Func<A, T, A> op) { for (long i = b.i; i < e.i; i++) init = op(init, b.a[i]); return init; }
        public static Iter<T> lower_bound<T>(Iter<T> b, Iter<T> e, T v) => lower_bound<T>(b, e, v, Less<T>.Fn);
        public static Iter<T> lower_bound<T>(Iter<T> b, Iter<T> e, T v, Func<T, T, bool> lt)
        {
            long first = b.i, len = e.i - b.i;
            while (len > 0) { long half = len >> 1, mid = first + half; if (lt(b.a[mid], v)) { first = mid + 1; len = len - half - 1; } else len = half; }
            return b + (first - b.i);
        }
        public static Iter<T> lower_bound<T, V>(Iter<T> b, Iter<T> e, V v, Func<T, V, bool> lt)
        {
            long first = b.i, len = e.i - b.i;
            while (len > 0) { long half = len >> 1, mid = first + half; if (lt(b.a[mid], v)) { first = mid + 1; len = len - half - 1; } else len = half; }
            return b + (first - b.i);
        }
        public static Iter<T> upper_bound<T>(Iter<T> b, Iter<T> e, T v) => upper_bound<T>(b, e, v, Less<T>.Fn);
        public static Iter<T> upper_bound<T>(Iter<T> b, Iter<T> e, T v, Func<T, T, bool> lt)
        {
            long first = b.i, len = e.i - b.i;
            while (len > 0) { long half = len >> 1, mid = first + half; if (lt(v, b.a[mid])) len = half; else { first = mid + 1; len = len - half - 1; } }
            return b + (first - b.i);
        }
        public static Iter<T> upper_bound<T, V>(Iter<T> b, Iter<T> e, V v, Func<V, T, bool> lt)
        {
            long first = b.i, len = e.i - b.i;
            while (len > 0) { long half = len >> 1, mid = first + half; if (lt(v, b.a[mid])) len = half; else { first = mid + 1; len = len - half - 1; } }
            return b + (first - b.i);
        }
        public static bool binary_search<T>(Iter<T> b, Iter<T> e, T v) { var it = lower_bound<T>(b, e, v); return it.i != e.i && !Less<T>.Fn(v, it.Deref); }
        /// <summary>std::is_sorted: no element is less than the one before it.</summary>
        public static bool is_sorted<T>(Iter<T> b, Iter<T> e) { for (long i = b.i + 1; i < e.i; i++) if (Less<T>.Fn(b.a[i], b.a[i - 1])) return false; return true; }
        public static bool is_sorted<T>(Iter<T> b, Iter<T> e, Func<T, T, bool> lt) { for (long i = b.i + 1; i < e.i; i++) if (lt(b.a[i], b.a[i - 1])) return false; return true; }
        public static Iter<T> adjacent_find<T>(Iter<T> b, Iter<T> e)
        {
            var c = EqualityComparer<T>.Default;
            for (long i = b.i; i + 1 < e.i; i++) if (c.Equals(b.a[i], b.a[i + 1])) return b + (i - b.i);
            return e;
        }
        public static Iter<T> adjacent_find<T>(Iter<T> b, Iter<T> e, Func<T, T, bool> eq)
        {
            for (long i = b.i; i + 1 < e.i; i++) if (eq(b.a[i], b.a[i + 1])) return b + (i - b.i);
            return e;
        }
        /// <summary>std::unique: keeps the first of each run of equal elements (libstdc++'s order of moves).</summary>
        public static Iter<T> unique<T>(Iter<T> b, Iter<T> e)
        {
            var c = EqualityComparer<T>.Default;
            if (b.i == e.i) return e;
            long first = b.i, last = e.i;
            // adjacent_find
            long nxt = first;
            while (++nxt != last) { if (c.Equals(b.a[first], b.a[nxt])) break; first = nxt; }
            if (nxt == last) return e;
            long dest = first;
            first = nxt;
            while (++first != last) if (!c.Equals(b.a[dest], b.a[first])) b.a[++dest] = b.a[first];
            return b + (dest + 1 - b.i);
        }
        public static Iter<T> unique<T>(Iter<T> b, Iter<T> e, Func<T, T, bool> eq)
        {
            if (b.i == e.i) return e;
            long first = b.i, last = e.i, nxt = first;
            while (++nxt != last) { if (eq(b.a[first], b.a[nxt])) break; first = nxt; }
            if (nxt == last) return e;
            long dest = first;
            first = nxt;
            while (++first != last) if (!eq(b.a[dest], b.a[first])) b.a[++dest] = b.a[first];
            return b + (dest + 1 - b.i);
        }
        public static Iter<T> remove_if<T>(Iter<T> b, Iter<T> e, Func<T, bool> f)
        {
            long first = b.i;
            while (first != e.i && !f(b.a[first])) first++;
            if (first == e.i) return e;
            long res = first;
            for (long i = first + 1; i != e.i; i++) if (!f(b.a[i])) b.a[res++] = b.a[i];
            return b + (res - b.i);
        }
        public static Iter<T> remove<T>(Iter<T> b, Iter<T> e, T v) { var c = EqualityComparer<T>.Default; return remove_if(b, e, x => c.Equals(x, v)); }
        public static Iter<T> max_element<T>(Iter<T> b, Iter<T> e) => max_element(b, e, Less<T>.Fn);
        public static Iter<T> max_element<T>(Iter<T> b, Iter<T> e, Func<T, T, bool> lt)
        {
            if (b.i == e.i) return e;
            long best = b.i;
            for (long i = b.i + 1; i < e.i; i++) if (lt(b.a[best], b.a[i])) best = i;
            return b + (best - b.i);
        }
        public static Iter<T> min_element<T>(Iter<T> b, Iter<T> e) => min_element(b, e, Less<T>.Fn);
        public static Iter<T> min_element<T>(Iter<T> b, Iter<T> e, Func<T, T, bool> lt)
        {
            if (b.i == e.i) return e;
            long best = b.i;
            for (long i = b.i + 1; i < e.i; i++) if (lt(b.a[i], b.a[best])) best = i;
            return b + (best - b.i);
        }
        public static Iter<T> copy<T>(Iter<T> b, Iter<T> e, Iter<T> o) { for (long i = b.i; i < e.i; i++) o.a[o.i + (i - b.i)] = Elem<T>.Copy(b.a[i]); return o + (e.i - b.i); }
        public static Ptr<T> copy<T>(Iter<T> b, Iter<T> e, Ptr<T> o) { for (long i = b.i; i < e.i; i++) o.a[o.o + (i - b.i)] = Elem<T>.Copy(b.a[i]); return o + (e.i - b.i); }
        public static Ptr<T> copy<T>(Ptr<T> b, Ptr<T> e, Ptr<T> o) { for (long i = b.o; i < e.o; i++) o.a[o.o + (i - b.o)] = Elem<T>.Copy(b.a[i]); return o + (e.o - b.o); }
        public static Iter<T> copy<T>(Ptr<T> b, Ptr<T> e, Iter<T> o) { for (long i = b.o; i < e.o; i++) o.a[o.i + (i - b.o)] = Elem<T>.Copy(b.a[i]); return o + (e.o - b.o); }
        public static Iter<T> copy_n<T>(Iter<T> b, ulong n, Iter<T> o) => copy(b, b + (long)n, o);
        public static Ptr<T> copy_n<T>(Ptr<T> b, ulong n, Ptr<T> o) => copy(b, b + (long)n, o);
        /// <summary>transform over a string's bytes with a function returning int (::toupper and lambdas around
        /// it): each result is stored as a char, as the C++ assignment converts it.</summary>
        public static Iter<byte> transform(Iter<byte> b, Iter<byte> e, Iter<byte> o, Func<byte, int> f) { for (long i = b.i; i < e.i; i++) o.a[o.i + (i - b.i)] = unchecked((byte)f(b.a[i])); return o + (e.i - b.i); }
        public static Iter<byte> transform(Iter<byte> b, Iter<byte> e, Iter<byte> o, Func<byte, sbyte> f) { for (long i = b.i; i < e.i; i++) o.a[o.i + (i - b.i)] = unchecked((byte)f(b.a[i])); return o + (e.i - b.i); }
        public static Iter<U> transform<T, U>(Iter<T> b, Iter<T> e, Iter<U> o, Func<T, U> f) { for (long i = b.i; i < e.i; i++) o.a[o.i + (i - b.i)] = f(b.a[i]); return o + (e.i - b.i); }
        public static void replace<T>(Iter<T> b, Iter<T> e, T old, T nw) { var c = EqualityComparer<T>.Default; for (long i = b.i; i < e.i; i++) if (c.Equals(b.a[i], old)) b.a[i] = nw; }
        public static void reverse<T>(Iter<T> b, Iter<T> e) { Array.Reverse(b.a, (int)b.i, (int)(e.i - b.i)); }
        public static void iota(Iter<int> b, Iter<int> e, int v) { for (long i = b.i; i < e.i; i++) b.a[i] = v++; }
        public static void iota(Iter<ulong> b, Iter<ulong> e, ulong v) { for (long i = b.i; i < e.i; i++) b.a[i] = v++; }
        public static bool equal<T>(Iter<T> b, Iter<T> e, Iter<T> b2) { var c = EqualityComparer<T>.Default; for (long i = b.i; i < e.i; i++) if (!c.Equals(b.a[i], b2.a[b2.i + (i - b.i)])) return false; return true; }
        public static long distance<T>(Iter<T> a, Iter<T> b) => b.i - a.i;
        public static long distance<T>(Ptr<T> a, Ptr<T> b) => b.o - a.o;
        public static Iter<T> next<T>(Iter<T> a, long n = 1) => a + n;
        public static Iter<T> prev<T>(Iter<T> a, long n = 1) => a - n;
        public static Vec<T> VecFrom<T>(Iter<T> b, Iter<T> e) { var v = new Vec<T>(); v.a = new T[e.i - b.i]; v.n = (int)(e.i - b.i); for (long i = 0; i < v.n; i++) v.a[i] = Elem<T>.Copy(b.a[b.i + i]); return v; }
        public static Vec<T> VecFromPtr<T>(Ptr<T> b, Ptr<T> e) { var v = new Vec<T>(); v.a = new T[e.o - b.o]; v.n = (int)(e.o - b.o); for (long i = 0; i < v.n; i++) v.a[i] = Elem<T>.Copy(b.a[b.o + i]); return v; }
        public static Iter<T> begin<T>(Vec<T> v) => v.begin();
        public static Iter<T> end<T>(Vec<T> v) => v.end();
        public static Iter<T> begin<T>(T[] a) => new Iter<T>(a, 0);
        public static Iter<T> end<T>(T[] a) => new Iter<T>(a, a.Length);
        public static ulong size<T>(Vec<T> v) => v.size();
        public static ulong size<T>(T[] a) => (ulong)a.Length;
    }
}

namespace Cpp
{
    /// <summary>std::chrono time points and durations, used only for timing (profiling counters, the run's
    /// elapsed time, timestamps in temporary file names); none of them reaches the results.</summary>
    public struct TimePoint
    {
        public TimePoint Clone() => this;      // a value: copying it is the copy
        public long ns;
        public static TimePoint now() => new TimePoint { ns = (long)(System.Diagnostics.Stopwatch.GetTimestamp() * (1e9 / System.Diagnostics.Stopwatch.Frequency)) };
        public static Duration operator -(TimePoint a, TimePoint b) => new Duration(a.ns - b.ns);
        public Duration time_since_epoch() => new Duration(ns);
    }

    /// <summary>A duration held in nanoseconds; count() gives seconds (the engine counts durations as
    /// std::chrono::duration&lt;double&gt;).</summary>
    public struct Duration
    {
        public Duration Clone() => this;      // a value: copying it is the copy
        public long ns;
        public Duration(long n) { ns = n; }
        public Duration(Duration d) { ns = d.ns; }
        public double count() => ns * 1e-9;
        public static Duration operator +(Duration a, Duration b) => new Duration(a.ns + b.ns);
        public static Duration operator -(Duration a, Duration b) => new Duration(a.ns - b.ns);
    }
}

namespace Cpp
{
    public static class Sys
    {
        [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "get_nprocs")]
        static extern int get_nprocs();
        /// <summary>std::thread::hardware_concurrency: libstdc++ on Linux asks glibc's get_nprocs (processors online,
        /// not the cgroup or affinity limit .NET's ProcessorCount reports). The engine prints it in THREADS warnings.</summary>
        public static uint hardware_concurrency()
        {
            try { int n = get_nprocs(); return n > 0 ? (uint)n : 0u; } catch (Exception) { return (uint)Environment.ProcessorCount; }
        }
    }
}

namespace Cpp
{
    /// <summary>A unique number per object for this run: orders pointers the way only their identity matters
    /// (a std::map key holding table pointers is looked up, never iterated in address order).</summary>
    public static class Ident
    {
        sealed class Id { public long v; }
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, Id> ids = new();
        static long next;
        public static long Of(object? o) => o == null ? 0 : ids.GetValue(o, _ => new Id { v = System.Threading.Interlocked.Increment(ref next) }).v;
    }

    /// <summary>std::tuple of more than three elements (a map key compared element by element, as
    /// std::tuple's operator&lt; does).</summary>
    public sealed class TupN : ICopy<TupN>, IComparable<TupN>, IEquatable<TupN>
    {
        public object[] v;
        public TupN() { v = Array.Empty<object>(); }
        public TupN(params object[] items) { v = items; }
        public TupN Clone() => new TupN((object[])v.Clone());
        public void Assign(TupN o) { v = (object[])o.v.Clone(); }
        public void MoveFrom(TupN o) { v = o.v; }
        static int Cmp(object? a, object? b)
        {
            switch (a)
            {
                case null: return b == null ? 0 : -1;
                case double x: { double y = (double)b!; return x < y ? -1 : y < x ? 1 : 0; }
                case int x: return x.CompareTo((int)b!);
                case long x: return x.CompareTo((long)b!);
                case bool x: return x.CompareTo((bool)b!);
                case Ptr<double> p:
                    {
                        var q = (Ptr<double>)b!;
                        int c = Ident.Of(p.a).CompareTo(Ident.Of(q.a));
                        return c != 0 ? c : p.o.CompareTo(q.o);
                    }
                case IComparable c0: return c0.CompareTo(b);
            }
            throw new NotSupportedException("tuple element " + a.GetType());
        }
        public int CompareTo(TupN? o)
        {
            for (int i = 0; i < v.Length; i++) { int c = Cmp(v[i], o!.v[i]); if (c != 0) return c; }
            return 0;
        }
        public bool Equals(TupN? o) => o != null && CompareTo(o) == 0;
        public override bool Equals(object? o) => o is TupN t && Equals(t);
        public override int GetHashCode() => v.Length;
    }
}

namespace Cpp
{
    /// <summary>Debugging aid: with the environment variable SWMM6_CS_TRACE set, each exception a translated
    /// catch handler catches is printed to stderr (the engine itself turns most of them into error codes).</summary>
    public static class Dbg
    {
        static readonly bool on = Environment.GetEnvironmentVariable("SWMM6_CS_TRACE") != null;
        public static void Caught(Exception e) { if (on) Console.Error.WriteLine("[caught] " + e); }
    }
}

namespace Cpp
{
    /// <summary>std::once_flag: whether call_once has run its function.</summary>
    public sealed class OnceFlag { public bool done; }
    public static partial class StdOnce { }
}
