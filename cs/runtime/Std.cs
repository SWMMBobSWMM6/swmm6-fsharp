// Std.cs - the parts of the C++ standard library the SWMM6 engine uses, with libstdc++'s observable behaviour:
// std::vector (Vec), raw pointers into arrays (Ptr), std::pair, std::optional, std::array helpers, std::min/max.
// Strings are in Str.cs, containers keyed by hash in UMap.cs, sorting in StdAlgo.cs, streams in Streams.cs.
// Written by Claude (Anthropic) for the SWMM6 ports; see AI-USE.md.
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Cpp
{
    /// <summary>Copy semantics for the engine's classes: Clone is the copy constructor, Assign the copy
    /// assignment. The emitter generates both for every engine class.</summary>
    public interface ICopy<T>
    {
        T Clone();
        void Assign(T other);
    }

    /// <summary>How to make, copy and assign elements of a container: plain values copy by assignment; classes
    /// are created with their default constructor and copied with Clone.</summary>
    public static class Elem<T>
    {
        public static readonly bool IsClass = typeof(ICopy<T>).IsAssignableFrom(typeof(T));
        public static Func<T> Make = DefaultMake();

        static Func<T> DefaultMake()
        {
            if (typeof(T).IsValueType) return () => default(T)!;
            var ctor = typeof(T).GetConstructor(Type.EmptyTypes);
            if (ctor != null) return () => (T)ctor.Invoke(null);
            return () => default(T)!;
        }

        static readonly bool IsArr = typeof(T).IsArray;
        /// <summary>Copying a T means more than copying the reference: a class, or an array (std::array, T[n]).</summary>
        public static readonly bool NeedsCopy = IsClass || typeof(T).IsArray;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T Copy(T x) => IsClass && x != null ? ((ICopy<T>)x!).Clone() : IsArr && x != null ? (T)ArrCopy.Deep(x!) : x;
    }

    /// <summary>A C array member copied with its object (T a[n][m], or an array of classes): every row and element.</summary>
    public static class ArrCopy
    {
        static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, Func<object, object>> elemCopy = new();
        public static object Deep(object x)
        {
            var a = (Array)x;
            var b = (Array)a.Clone();
            var et = a.GetType().GetElementType()!;
            if (et.IsArray)
            {
                for (int i = 0; i < a.Length; i++) { var v = a.GetValue(i); if (v != null) b.SetValue(Deep(v), i); }
            }
            else if (!et.IsValueType || typeof(ICopy<>).MakeGenericType(et).IsAssignableFrom(et))
            {
                var f = elemCopy.GetOrAdd(et, t =>
                {
                    var m = typeof(Elem<>).MakeGenericType(t).GetMethod("Copy")!;
                    return o => m.Invoke(null, new[] { o })!;
                });
                for (int i = 0; i < a.Length; i++) { var v = a.GetValue(i); if (v != null) b.SetValue(f(v), i); }
            }
            return b;
        }
    }

    /// <summary>std::vector&lt;T&gt;: contiguous storage with a ref indexer, so v[i] = x and double&amp; r = v[i]
    /// work as in C++. Element copies follow C++: copying the vector or inserting a named object copies it.</summary>
    public sealed class Vec<T> : ICopy<Vec<T>>, IDestroyElems
    {
        public T[] a;
        public int n;
        /// <summary>How to make a new element when T alone cannot say: a std::array or C array element
        /// (vector&lt;array&lt;double, 3&gt;&gt; holds double[] in C#, and the 3 is known only to the generated code).</summary>
        public Func<T>? mk;

        public Vec() { a = Array.Empty<T>(); }
        public Vec(Func<T> mk) { a = Array.Empty<T>(); this.mk = mk; }
        public Vec(ulong count, Func<T> mk) { this.mk = mk; a = new T[count]; n = (int)count; Fill(0, n); }
        /// <summary>~vector: each element in order, first to last (libstdc++ _Destroy).</summary>
        public void DestroyElems() { for (int i = 0; i < n; i++) Dtor.Run(a[i]); }
        public Vec(ulong count) { a = new T[count]; n = (int)count; Fill(0, n); }
        public Vec(ulong count, T v) { a = new T[count]; n = (int)count; for (int i = 0; i < n; i++) a[i] = Elem<T>.Copy(v); }
        public Vec(T[] init) { a = new T[init.Length]; n = init.Length; for (int i = 0; i < n; i++) a[i] = Elem<T>.Copy(init[i]); }
        public static Vec<T> Adopt(T[] arr) { var v = new Vec<T>(); v.a = arr; v.n = arr.Length; return v; }

        void Fill(int from, int to)
        {
            if (mk != null)
                for (int i = from; i < to; i++) a[i] = mk();
            else if (Elem<T>.IsClass || (!typeof(T).IsValueType))
                for (int i = from; i < to; i++) a[i] = Elem<T>.Make();
        }

        public ref T this[ulong i] { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => ref a[i]; }
        public ref T this[long i] { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => ref a[i]; }
        public ref T this[int i] { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => ref a[i]; }

        public ulong size() => (ulong)n;
        public bool empty() => n == 0;
        public ulong capacity() => (ulong)a.Length;
        public ref T at(ulong i) { if (i >= (ulong)n) throw new OutOfRange("vector::_M_range_check: __n (which is " + i + ") >= this->size() (which is " + n + ")"); return ref a[i]; }
        public ref T front() => ref a[0];
        public ref T back() => ref a[n - 1];
        /// <summary>libstdc++'s data() is the start of the storage, which is nullptr until the vector first allocates
        /// (capacity 0): DynamicWave's nodeNewSurfAreaDataMut() is null in an FV run, and the structure code tests
        /// it (scatterOrificeSurfArea: if (node_new_surf_area == nullptr) return).</summary>
        public Ptr<T> data() => a.Length == 0 ? default : new Ptr<T>(a, 0);

        void Grow(int need)
        {
            if (need <= a.Length) return;
            // libstdc++: new capacity = size + max(size, 1) when push_back runs out
            int cap = Math.Max(need, n + Math.Max(n, 1));
            Array.Resize(ref a, cap);
        }

        public void reserve(ulong c) { if ((int)c > a.Length) Array.Resize(ref a, (int)c); }
        public void push_back(T v) { Grow(n + 1); a[n++] = v; }        // the emitter copies a named argument
        public ref T emplace_back(T v) { Grow(n + 1); a[n++] = v; return ref a[n - 1]; }
        public void pop_back() { n--; a[n] = default!; }
        public void clear() { Array.Clear(a, 0, n); n = 0; }
        public void resize(ulong c)
        {
            int m = (int)c;
            if (m > n) { Grow(m); Fill(n, m); }
            else Array.Clear(a, m, n - m);
            n = m;
        }
        public void resize(ulong c, T v)
        {
            int m = (int)c;
            if (m > n) { Grow(m); for (int i = n; i < m; i++) a[i] = Elem<T>.Copy(v); }
            else Array.Clear(a, m, n - m);
            n = m;
        }
        public void assign(ulong c, T v)
        {
            int m = (int)c;
            if (m > a.Length) a = new T[m];
            else Array.Clear(a, 0, a.Length);
            for (int i = 0; i < m; i++) a[i] = Elem<T>.Copy(v);
            n = m;
        }
        public void assign(Iter<T> b, Iter<T> e)
        {
            int m = (int)(e.i - b.i);
            var src = b.a; int o = (int)b.i;
            var na = new T[m];
            for (int i = 0; i < m; i++) na[i] = Elem<T>.Copy(src[o + i]);
            a = na; n = m;
        }
        public void assign(T[] il) { var na = new T[il.Length]; for (int i = 0; i < il.Length; i++) na[i] = Elem<T>.Copy(il[i]); a = na; n = il.Length; }
        public void shrink_to_fit() { if (a.Length != n) Array.Resize(ref a, n); }
        public void swap(Vec<T> o) { (a, o.a) = (o.a, a); (n, o.n) = (o.n, n); }

        public Iter<T> begin() => new Iter<T>(this, 0);
        public Iter<T> end() => new Iter<T>(this, n);
        public Iter<T> cbegin() => begin();
        public Iter<T> cend() => end();
        public RevIter<T> rbegin() => new RevIter<T>(this, n);
        public RevIter<T> rend() => new RevIter<T>(this, 0);

        public Iter<T> insert(Iter<T> pos, T v)
        {
            int p = (int)pos.i;
            Grow(n + 1);
            Array.Copy(a, p, a, p + 1, n - p);
            a[p] = v; n++;
            return new Iter<T>(this, p);
        }
        public Iter<T> insert(Iter<T> pos, Iter<T> b, Iter<T> e)
        {
            int p = (int)pos.i, m = (int)(e.i - b.i);
            var tmp = new T[m];
            for (int i = 0; i < m; i++) tmp[i] = Elem<T>.Copy(b.a[b.i + i]);
            Grow(n + m);
            Array.Copy(a, p, a, p + m, n - p);
            Array.Copy(tmp, 0, a, p, m);
            n += m;
            return new Iter<T>(this, p);
        }
        /// <summary>insert(pos, {a, b, c}): the initializer list's elements, copied.</summary>
        public Iter<T> insert(Iter<T> pos, T[] il)
        {
            int p = (int)pos.i, m = il.Length;
            var tmp = new T[m];
            for (int i = 0; i < m; i++) tmp[i] = Elem<T>.Copy(il[i]);
            Grow(n + m);
            Array.Copy(a, p, a, p + m, n - p);
            Array.Copy(tmp, 0, a, p, m);
            n += m;
            return new Iter<T>(this, p);
        }
        public Iter<T> insert(Iter<T> pos, ulong cnt, T v)
        {
            int p = (int)pos.i, m = (int)cnt;
            Grow(n + m);
            Array.Copy(a, p, a, p + m, n - p);
            for (int i = 0; i < m; i++) a[p + i] = Elem<T>.Copy(v);
            n += m;
            return new Iter<T>(this, p);
        }
        public Iter<T> erase(Iter<T> pos)
        {
            int p = (int)pos.i;
            Array.Copy(a, p + 1, a, p, n - p - 1);
            n--; a[n] = default!;
            return new Iter<T>(this, p);
        }
        public Iter<T> erase(Iter<T> b, Iter<T> e)
        {
            int p = (int)b.i, q = (int)e.i;
            Array.Copy(a, q, a, p, n - q);
            int m = q - p;
            Array.Clear(a, n - m, m);
            n -= m;
            return new Iter<T>(this, p);
        }

        public Vec<T> Clone()
        {
            var v = new Vec<T>();
            v.mk = mk;
            v.a = new T[n];
            v.n = n;
            if (Elem<T>.NeedsCopy) for (int i = 0; i < n; i++) v.a[i] = Elem<T>.Copy(a[i]);
            else Array.Copy(a, v.a, n);
            return v;
        }
        public void Assign(Vec<T> o)
        {
            if (ReferenceEquals(this, o)) return;
            var na = new T[o.n];
            if (Elem<T>.NeedsCopy) for (int i = 0; i < o.n; i++) na[i] = Elem<T>.Copy(o.a[i]);
            else Array.Copy(o.a, na, o.n);
            a = na; n = o.n;
            mk ??= o.mk;
        }
        /// <summary>Move-assign: take o's storage and leave o empty, as libstdc++ does.</summary>
        public void MoveFrom(Vec<T> o)
        {
            if (ReferenceEquals(this, o)) return;
            a = o.a; n = o.n;
            mk ??= o.mk;
            o.a = Array.Empty<T>(); o.n = 0;
        }
        public void Assign(T[] il) => assign(il);
        public void MoveFrom(T[] il) => assign(il);          // v = {a, b, c}
        public Iter<T> insert(Iter<T> pos, Ptr<T> b, Ptr<T> e)
        {
            int p = (int)pos.i, m = (int)(e.o - b.o);
            var tmp = new T[m];
            for (int i = 0; i < m; i++) tmp[i] = Elem<T>.Copy(b.a[b.o + i]);
            Grow(n + m);
            Array.Copy(a, p, a, p + m, n - p);
            Array.Copy(tmp, 0, a, p, m);
            n += m;
            return new Iter<T>(this, p);
        }

        public static bool Eq(Vec<T> x, Vec<T> y)
        {
            if (x.n != y.n) return false;
            var c = EqualityComparer<T>.Default;
            for (int i = 0; i < x.n; i++) if (!c.Equals(x.a[i], y.a[i])) return false;
            return true;
        }
    }

    /// <summary>A vector iterator: the container and an index (contiguous, so it is also a pointer).</summary>
    public struct Iter<T> : IEquatable<Iter<T>>
    {
        public Iter<T> Clone() => this;      // a value: copying it is the copy
        public readonly Vec<T>? v;
        public readonly T[] a;
        public readonly long i;
        public Iter(Vec<T> v, long i) { this.v = v; a = v.a; this.i = i; }
        public Iter(T[] a, long i) { v = null; this.a = a; this.i = i; }
        public ref T Deref { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => ref a[i]; }
        public ref T this[long k] => ref a[i + k];
        public static Iter<T> operator +(Iter<T> x, long k) => x.v != null ? new Iter<T>(x.v, x.i + k) : new Iter<T>(x.a, x.i + k);
        public static Iter<T> operator -(Iter<T> x, long k) => x + (-k);
        public static long operator -(Iter<T> x, Iter<T> y) => x.i - y.i;
        public static Iter<T> operator ++(Iter<T> x) => x + 1;
        public static Iter<T> operator --(Iter<T> x) => x + (-1);
        public static bool operator ==(Iter<T> x, Iter<T> y) => x.i == y.i;
        public static bool operator !=(Iter<T> x, Iter<T> y) => x.i != y.i;
        public static bool operator <(Iter<T> x, Iter<T> y) => x.i < y.i;
        public static bool operator >(Iter<T> x, Iter<T> y) => x.i > y.i;
        public static bool operator <=(Iter<T> x, Iter<T> y) => x.i <= y.i;
        public static bool operator >=(Iter<T> x, Iter<T> y) => x.i >= y.i;
        public bool Equals(Iter<T> o) => i == o.i;
        public override bool Equals(object? o) => o is Iter<T> x && x.i == i;
        public override int GetHashCode() => (int)i;
    }

    public struct RevIter<T>
    {
        public RevIter<T> Clone() => this;      // a value: copying it is the copy
        public readonly Vec<T> v;
        public readonly long i;      // one past the element it designates, as std::reverse_iterator
        public RevIter(Vec<T> v, long i) { this.v = v; this.i = i; }
        public ref T Deref => ref v.a[i - 1];
        public static RevIter<T> operator ++(RevIter<T> x) => new RevIter<T>(x.v, x.i - 1);
        public static bool operator ==(RevIter<T> x, RevIter<T> y) => x.i == y.i;
        public static bool operator !=(RevIter<T> x, RevIter<T> y) => x.i != y.i;
        public override bool Equals(object? o) => o is RevIter<T> x && x.i == i;
        public override int GetHashCode() => (int)i;
    }

    /// <summary>A raw pointer to T inside an array: the array and an offset. Null has a null array.</summary>
    /// <summary>A reference to one field of an object, for a pointer made by &amp;obj.field.</summary>
    public delegate ref T RefAt<T>(object? o);

    public struct Ptr<T> : IEquatable<Ptr<T>>
    {
        public Ptr<T> Clone() => this;      // a value: copying it is the copy
        public readonly T[] a;
        public readonly long o;
        // &obj.field: the object and how to reach the field (a and o unused). C# cannot point into an object, so the
        // pointer keeps the object and a getter of the field's reference; reads and writes go to the field itself.
        public readonly object? ob;
        public readonly RefAt<T>? at;
        public Ptr(T[] a, long o) { this.a = a; this.o = o; ob = null; at = null; }
        public Ptr(object? ob, RefAt<T> at) { a = null!; o = 0; this.ob = ob; this.at = at; }
        Ptr(T[] a, long o, object? ob, RefAt<T>? at) { this.a = a; this.o = o; this.ob = ob; this.at = at; }
        public bool IsNull => a == null && at == null;
        public ref T this[long i] { [MethodImpl(MethodImplOptions.AggressiveInlining)] get { if (at != null) return ref at(ob); return ref a[o + i]; } }
        public ref T this[ulong i] { [MethodImpl(MethodImplOptions.AggressiveInlining)] get { if (at != null) return ref at(ob); return ref a[o + (long)i]; } }
        public ref T this[int i] { [MethodImpl(MethodImplOptions.AggressiveInlining)] get { if (at != null) return ref at(ob); return ref a[o + i]; } }
        public ref T Deref { [MethodImpl(MethodImplOptions.AggressiveInlining)] get { if (at != null) return ref at(ob); return ref a[o]; } }
        public static Ptr<T> operator +(Ptr<T> p, long k) => new Ptr<T>(p.a, p.o + k, p.ob, p.at);
        public static Ptr<T> operator +(Ptr<T> p, ulong k) => new Ptr<T>(p.a, p.o + (long)k, p.ob, p.at);
        public static Ptr<T> operator -(Ptr<T> p, long k) => new Ptr<T>(p.a, p.o - k, p.ob, p.at);
        public static long operator -(Ptr<T> p, Ptr<T> q) => p.o - q.o;
        public static Ptr<T> operator ++(Ptr<T> p) => new Ptr<T>(p.a, p.o + 1, p.ob, p.at);
        public static Ptr<T> operator --(Ptr<T> p) => new Ptr<T>(p.a, p.o - 1, p.ob, p.at);
        public static bool operator ==(Ptr<T> p, Ptr<T> q)
        {
            if (p.at != null || q.at != null)
                return q.at != null && p.at != null && ReferenceEquals(p.ob, q.ob) && p.at.Method == q.at.Method && p.o == q.o;
            return ReferenceEquals(p.a, q.a) && (p.a == null || p.o == q.o);
        }
        public static bool operator !=(Ptr<T> p, Ptr<T> q) => !(p == q);
        public static bool operator <(Ptr<T> p, Ptr<T> q) => p.o < q.o;
        public static bool operator >(Ptr<T> p, Ptr<T> q) => p.o > q.o;
        public static bool operator <=(Ptr<T> p, Ptr<T> q) => p.o <= q.o;
        public static bool operator >=(Ptr<T> p, Ptr<T> q) => p.o >= q.o;
        public bool Equals(Ptr<T> q) => this == q;
        public override bool Equals(object? x) => x is Ptr<T> q && this == q;
        public override int GetHashCode() => (int)o;
        public static implicit operator Ptr<T>(Iter<T> it) => new Ptr<T>(it.a, it.i);
    }

    /// <summary>std::pair: a mutable class so p.first = x works through references.</summary>
    public sealed class Pair<A, B> : ICopy<Pair<A, B>>, IComparable<Pair<A, B>>
    {
        /// <summary>std::pair's operator&lt;: first, then second.</summary>
        public int CompareTo(Pair<A, B>? o)
        {
            if (Less<A>.Fn(first, o!.first)) return -1;
            if (Less<A>.Fn(o.first, first)) return 1;
            if (Less<B>.Fn(second, o.second)) return -1;
            if (Less<B>.Fn(o.second, second)) return 1;
            return 0;
        }
        public static bool operator <(Pair<A, B> x, Pair<A, B> y) => x.CompareTo(y) < 0;
        public static bool operator >(Pair<A, B> x, Pair<A, B> y) => x.CompareTo(y) > 0;
        public static bool operator <=(Pair<A, B> x, Pair<A, B> y) => x.CompareTo(y) <= 0;
        public static bool operator >=(Pair<A, B> x, Pair<A, B> y) => x.CompareTo(y) >= 0;
        public A first;
        public B second;
        public Pair() { first = Elem<A>.Make(); second = Elem<B>.Make(); }
        public Pair(A a, B b) { first = a; second = b; }
        public Pair<A, B> Clone() => new Pair<A, B>(Elem<A>.Copy(first), Elem<B>.Copy(second));
        public void Assign(Pair<A, B> o) { first = Elem<A>.Copy(o.first); second = Elem<B>.Copy(o.second); }
        public static bool Eq(Pair<A, B> x, Pair<A, B> y) =>
            EqualityComparer<A>.Default.Equals(x.first, y.first) && EqualityComparer<B>.Default.Equals(x.second, y.second);
    }

    /// <summary>std::optional&lt;T&gt;.</summary>
    public sealed class Opt<T> : ICopy<Opt<T>>, IDestroyElems
    {
        public bool has;
        public T val;
        public Opt() { val = default!; }
        public void DestroyElems() { if (has) Dtor.Run(val); }
        public Opt(T v) { has = true; val = v; }
        public bool has_value() => has;
        public ref T value() { if (!has) throw new BadOptionalAccess(); return ref val; }
        public ref T Deref => ref val;
        public T value_or(T d) => has ? val : d;
        public void reset() { has = false; val = default!; }
        public void MoveFrom(Opt<T> o) { if (ReferenceEquals(this, o)) return; has = o.has; val = o.val; }
        public ref T emplace(T v) { has = true; val = v; return ref val; }
        public Opt<T> Clone() => has ? new Opt<T>(Elem<T>.Copy(val)) : new Opt<T>();
        public void Assign(Opt<T> o) { has = o.has; val = o.has ? Elem<T>.Copy(o.val) : default!; }
        public void Set(T v) { has = true; val = v; }
    }

    /// <summary>std::array, std::fill, std::copy and the small helpers on plain arrays.</summary>
    public static class Arr
    {
        /// <summary>T a[n][m]: n rows of m (each a fresh array).</summary>
        public static T[][] Make2<T>(int n, int m)
        {
            var a = new T[n][];
            for (int i = 0; i < n; i++) a[i] = Make<T>(m);
            return a;
        }
        /// <summary>std::array&lt;std::array&lt;T, m&gt;, n&gt; and the like: n elements, each made by mk.</summary>
        public static T[] MakeWith<T>(int n, Func<T> mk) { var a = new T[n]; for (int i = 0; i < n; i++) a[i] = mk(); return a; }
        public static T[] Make<T>(int n)
        {
            var a = new T[n];
            if (Elem<T>.IsClass || !typeof(T).IsValueType) for (int i = 0; i < n; i++) a[i] = Elem<T>.Make();
            return a;
        }
        public static T[] Clone<T>(T[] a)
        {
            if (a == null) return null!;
            var b = new T[a.Length];
            if (Elem<T>.IsClass) for (int i = 0; i < a.Length; i++) b[i] = Elem<T>.Copy(a[i]);
            else Array.Copy(a, b, a.Length);
            return b;
        }
        public static void Assign<T>(T[] dst, T[] src)
        {
            if (Elem<T>.IsClass) for (int i = 0; i < src.Length; i++) ((ICopy<T>)dst[i]!).Assign(src[i]);
            else Array.Copy(src, dst, src.Length);
        }
        public static void Fill<T>(T[] a, long b, long e, T v) { for (long i = b; i < e; i++) a[i] = Elem<T>.Copy(v); }
        public static bool Eq<T>(T[] x, T[] y)
        {
            if (x.Length != y.Length) return false;
            var c = EqualityComparer<T>.Default;
            for (int i = 0; i < x.Length; i++) if (!c.Equals(x[i], y[i])) return false;
            return true;
        }
    }

    /// <summary>std::min / std::max / std::clamp exactly as libstdc++ writes them (the comparisons decide which
    /// argument comes back, which matters for -0.0 and NaN).</summary>
    public static class M
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static double min(double a, double b) => (b < a) ? b : a;
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static double max(double a, double b) => (a < b) ? b : a;
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float min(float a, float b) => (b < a) ? b : a;
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static float max(float a, float b) => (a < b) ? b : a;
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static int min(int a, int b) => (b < a) ? b : a;
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static int max(int a, int b) => (a < b) ? b : a;
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static long min(long a, long b) => (b < a) ? b : a;
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static long max(long a, long b) => (a < b) ? b : a;
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static ulong min(ulong a, ulong b) => (b < a) ? b : a;
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static ulong max(ulong a, ulong b) => (a < b) ? b : a;
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static uint min(uint a, uint b) => (b < a) ? b : a;
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static uint max(uint a, uint b) => (a < b) ? b : a;
        public static byte min(byte a, byte b) => (b < a) ? b : a;
        public static byte max(byte a, byte b) => (a < b) ? b : a;
        public static sbyte min(sbyte a, sbyte b) => (b < a) ? b : a;
        public static sbyte max(sbyte a, sbyte b) => (a < b) ? b : a;
        public static short min(short a, short b) => (b < a) ? b : a;
        public static short max(short a, short b) => (a < b) ? b : a;
        public static ushort min(ushort a, ushort b) => (b < a) ? b : a;
        public static ushort max(ushort a, ushort b) => (a < b) ? b : a;
        public static T min<T>(T a, T b, Func<T, T, bool> lt) => lt(b, a) ? b : a;
        public static T max<T>(T a, T b, Func<T, T, bool> lt) => lt(a, b) ? b : a;
        // std::clamp(v, lo, hi): (v < lo) ? lo : (hi < v) ? hi : v
        public static double clamp(double v, double lo, double hi) => (v < lo) ? lo : (hi < v) ? hi : v;
        public static int clamp(int v, int lo, int hi) => (v < lo) ? lo : (hi < v) ? hi : v;
        public static long clamp(long v, long lo, long hi) => (v < lo) ? lo : (hi < v) ? hi : v;
        // initializer-list forms: the first smallest / first largest
        public static double min(double[] il) { double r = il[0]; for (int i = 1; i < il.Length; i++) if (il[i] < r) r = il[i]; return r; }
        public static double max(double[] il) { double r = il[0]; for (int i = 1; i < il.Length; i++) if (r < il[i]) r = il[i]; return r; }
        public static int min(int[] il) { int r = il[0]; for (int i = 1; i < il.Length; i++) if (il[i] < r) r = il[i]; return r; }
        public static int max(int[] il) { int r = il[0]; for (int i = 1; i < il.Length; i++) if (r < il[i]) r = il[i]; return r; }
    }

    public class CppException : Exception
    {
        public readonly Str msg;
        public CppException() : base("std::exception") { msg = new Str("std::exception"); }
        public CppException(Str m) : base(m.ToString()) { msg = m.Clone(); }
        public virtual CPtr what() => msg.c_str();
    }
    public class LogicError : CppException { public LogicError(Str m) : base(m) { } public LogicError(string m) : base(new Str(m)) { } }
    public class RuntimeError : CppException { public RuntimeError(Str m) : base(m) { } public RuntimeError(string m) : base(new Str(m)) { } }
    public class InvalidArgument : LogicError { public InvalidArgument(Str m) : base(m) { } public InvalidArgument(string m) : base(m) { } }
    public class OutOfRange : LogicError { public OutOfRange(Str m) : base(m) { } public OutOfRange(string m) : base(m) { } }
    public class LengthError : LogicError { public LengthError(Str m) : base(m) { } public LengthError(string m) : base(m) { } }
    public class DomainError : LogicError { public DomainError(Str m) : base(m) { } public DomainError(string m) : base(m) { } }
    public class RangeError : RuntimeError { public RangeError(Str m) : base(m) { } public RangeError(string m) : base(m) { } }
    public class OverflowError : RuntimeError { public OverflowError(Str m) : base(m) { } public OverflowError(string m) : base(m) { } }
    public class BadAlloc : CppException { public BadAlloc() : base(new Str("std::bad_alloc")) { } }
    public class BadOptionalAccess : CppException { public BadOptionalAccess() : base(new Str("bad optional access")) { } }
    public class BadFunctionCall : CppException { public BadFunctionCall() : base(new Str("bad_function_call")) { } }
}

namespace Cpp
{
    /// <summary>std::variant&lt;bool, int, double, std::string&gt; (the engine's user-flag value): the alternative held,
    /// as a boxed value, and its index in the declared list.</summary>
    public sealed class Variant : ICopy<Variant>
    {
        public object v = false;
        public int idx;
        public Variant() { }
        public Variant(bool x) { Set(x); }
        public Variant(int x) { Set(x); }
        public Variant(double x) { Set(x); }
        public Variant(Str x) { Set(x); }
        public void Set(bool x) { v = x; idx = 0; }
        public void Set(int x) { v = x; idx = 1; }
        public void Set(double x) { v = x; idx = 2; }
        public void Set(Str x) { v = x.Clone(); idx = 3; }
        public ulong index() => (ulong)idx;
        public bool Holds<T>() => v is T;
        public T Get<T>() { if (v is T t) return t; throw new BadVariantAccess(); }
        public Variant Clone() { var c = new Variant(); c.Assign(this); return c; }
        public void Assign(Variant o) { idx = o.idx; v = o.v is Str s ? s.Clone() : o.v; }
        public void MoveFrom(Variant o) { idx = o.idx; v = o.v; }
    }
    public class BadVariantAccess : CppException { public BadVariantAccess() : base(new Str("std::get: wrong index for variant")) { } }
}

namespace Cpp
{
    /// <summary>std::span&lt;T&gt;: a view of n elements of an array starting at o.</summary>
    public struct CSpan<T>
    {
        public T[] a; public long o; public long n;
        public CSpan(T[] a, long o, long n) { this.a = a; this.o = o; this.n = n; }
        public ref T this[long i] => ref a[o + i];
        public ulong size() => (ulong)n;
        public bool empty() => n == 0;
        public Ptr<T> data() => new Ptr<T>(a ?? Array.Empty<T>(), o);
        public ref T front() => ref a[o];
        public ref T back() => ref a[o + n - 1];
        public CSpan<T> subspan(ulong off, ulong cnt = ulong.MaxValue) => new CSpan<T>(a, o + (long)off, cnt == ulong.MaxValue ? n - (long)off : (long)cnt);
        public CSpan<T> first(ulong cnt) => new CSpan<T>(a, o, (long)cnt);
        public CSpan<T> last(ulong cnt) => new CSpan<T>(a, o + n - (long)cnt, (long)cnt);
        public CSpan<T> Clone() => this;
    }
}
