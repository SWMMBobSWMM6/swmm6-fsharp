// Containers.cs - std::unordered_map / unordered_set with libstdc++'s exact node order (GCC 13 bits/hashtable.h:
// one singly linked list, a bucket array pointing at the node before each bucket's first node, new nodes at the
// front of their bucket, the prime rehash policy with growth factor 2), std::hash as libstdc++ computes it
// (identity for integers, _Hash_bytes for strings), std::map / std::set (sorted), std::stack, and their iterators.
// The engine iterates hashed containers, so their order reaches its output; tests/stdcheck compares this file's
// order with libstdc++'s.
// Written by Claude (Anthropic) for the SWMM6 ports; see AI-USE.md.
using System;
using System.Collections.Generic;

namespace Cpp
{
    public static class StdHash
    {
        const ulong Mul = (0xc6a4a793UL << 32) + 0x5bd1e995UL;
        static ulong ShiftMix(ulong v) => v ^ (v >> 47);

        /// <summary>libstdc++'s std::_Hash_bytes (libsupc++/hash_bytes.cc, the 64-bit branch).</summary>
        public static ulong HashBytes(byte[] b, int off, int len, ulong seed)
        {
            int aligned = len & ~7;
            ulong hash = seed ^ ((ulong)len * Mul);
            int p = off;
            for (; p < off + aligned; p += 8)
            {
                ulong data = ShiftMix(BitConverter.ToUInt64(b, p) * Mul) * Mul;
                hash ^= data;
                hash *= Mul;
            }
            int rest = len & 7;
            if (rest != 0)
            {
                ulong data = 0;
                for (int n = rest - 1; n >= 0; n--) data = (data << 8) + b[p + n];
                hash ^= data;
                hash *= Mul;
            }
            hash = ShiftMix(hash) * Mul;
            hash = ShiftMix(hash);
            return hash;
        }

        public static ulong Of(Str s) => HashBytes(s.b, 0, s.n, 0xc70f6907UL);
        public static ulong Of(int v) => (ulong)(long)v;
        public static ulong Of(long v) => (ulong)v;
        public static ulong Of(uint v) => v;
        public static ulong Of(ulong v) => v;

        public static Func<K, ulong> For<K>()
        {
            var t = typeof(K);
            if (t == typeof(Str)) return k => Of((Str)(object)k!);
            if (t == typeof(int)) return k => Of((int)(object)k!);
            if (t == typeof(long)) return k => Of((long)(object)k!);
            if (t == typeof(uint)) return k => (uint)(object)k!;
            if (t == typeof(ulong)) return k => (ulong)(object)k!;
            if (t == typeof(short)) return k => (ulong)(long)(short)(object)k!;
            if (t == typeof(sbyte)) return k => (ulong)(long)(sbyte)(object)k!;
            if (t.IsEnum) return k => (ulong)Convert.ToInt64(k);
            if (t == typeof(double)) return k => { double d = (double)(object)k!; if (d == 0.0) return 0; return HashBytes(BitConverter.GetBytes(d), 0, 8, 0xc70f6907UL); };
            // object identity: libstdc++ hashes the address, which no other program can reproduce
            return k => (ulong)System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(k!);
        }
    }

    public sealed class Hash<T> { public static ulong Of(T x) => StdHash.For<T>()(x); public ulong op_Call(T x) => Of(x); }
    public sealed class EqualTo<T> { public bool op_Call(T a, T b) => EqualityComparer<T>.Default.Equals(a, b); }
    public sealed class Less<T>
    {
        public static readonly Func<T, T, bool> Fn = MakeFn();
        static Func<T, T, bool> MakeFn()
        {
            if (typeof(T) == typeof(Str)) return (a, b) => ((Str)(object)a!).compare((Str)(object)b!) < 0;
            var c = Comparer<T>.Default;
            return (a, b) => c.Compare(a, b) < 0;
        }
        public bool op_Call(T a, T b) => Fn(a, b);
    }

    /// <summary>The prime rehash policy (libstdc++ src/c++11/hashtable_c++0x.cc and hashtable-aux.cc).</summary>
    public sealed class RehashPolicy
    {
        public double MaxLoad = 1.0;
        public ulong NextResize = 0;
        static readonly byte[] FastBkt = { 2, 2, 2, 3, 5, 5, 7, 7, 11, 11, 11, 11, 13, 13 };

        public ulong NextBkt(ulong n)
        {
            if (n < (ulong)FastBkt.Length)
            {
                if (n == 0) return 1;
                NextResize = (ulong)Math.Floor(FastBkt[n] * MaxLoad);
                return FastBkt[n];
            }
            var primes = PrimeList.P;
            int last = primes.Length - 1;          // __prime_list + __n_primes - 1 (the sentinel is excluded)
            int lo = 6, hi = last;
            while (lo < hi) { int mid = (lo + hi) / 2; if (primes[mid] < n) lo = mid + 1; else hi = mid; }
            if (lo == last) NextResize = ulong.MaxValue;
            else NextResize = (ulong)Math.Floor(primes[lo] * MaxLoad);
            return primes[lo];
        }

        public ulong BktForElements(ulong n) => (ulong)Math.Ceiling(n / MaxLoad);

        public (bool, ulong) NeedRehash(ulong nBkt, ulong nElt, ulong nIns)
        {
            if (nElt + nIns > NextResize)
            {
                double minBkts = Math.Max(nElt + nIns, NextResize != 0 ? 0UL : 11UL) / MaxLoad;
                if (minBkts >= nBkt)
                    return (true, NextBkt(Math.Max((ulong)Math.Floor(minBkts) + 1, nBkt * 2)));
                NextResize = (ulong)Math.Floor(nBkt * MaxLoad);
                return (false, 0);
            }
            return (false, 0);
        }
    }

    /// <summary>A hash table node; the before-begin sentinel is a node too.</summary>
    public sealed class HNode<E>
    {
        public HNode<E>? next;
        public E val = default!;
        public ulong code;
    }

    /// <summary>libstdc++'s _Hashtable for unique keys. E is the stored element (Pair&lt;K,V&gt; or K).</summary>
    /// <summary>A key stored in a map or set is the container's own copy, as in C++, when the key is a library value
    /// (a string, a vector, a pair). An engine class as key is a pointer in C++ (std::set&lt;const Column*&gt;), so it is
    /// stored as it is: the same object, compared by identity.</summary>
    public static class KeyCopy<T>
    {
        static readonly bool copy = Elem<T>.IsClass && typeof(T).Namespace == "Cpp";
        public static T Of(T x) => copy ? Elem<T>.Copy(x) : x;
    }

    public class HashTable<K, E>
    {
        protected readonly Func<E, K> key;
        protected readonly Func<K, ulong> hash;
        protected readonly Func<K, K, bool> eq;
        protected HNode<E>?[] buckets = new HNode<E>?[1];
        protected readonly HNode<E> beforeBegin = new HNode<E>();
        protected ulong nelem;
        protected readonly RehashPolicy pol = new RehashPolicy();

        public HashTable(Func<E, K> key, Func<K, ulong>? hash, Func<K, K, bool>? eq)
        {
            this.key = key;
            this.hash = hash ?? StdHash.For<K>();
            this.eq = eq ?? ((a, b) => EqualityComparer<K>.Default.Equals(a, b));
        }

        ulong Bkt(ulong code) => code % (ulong)buckets.Length;

        protected HNode<E>? Find(K k)
        {
            ulong code = hash(k);
            ulong b = Bkt(code);
            var before = buckets[b];
            if (before == null) return null;
            for (var p = before.next; p != null; p = p.next)
            {
                if (p.code == code && eq(k, key(p.val))) return p;
                if (p.next == null || Bkt(p.next.code) != b) break;
            }
            return null;
        }

        /// <summary>Insert e if its key is absent; returns the node holding the key and whether it was added.</summary>
        protected (HNode<E>, bool) Insert(E e)
        {
            K k = key(e);
            var found = Find(k);
            if (found != null) return (found, false);
            ulong code = hash(k);
            var (re, n) = pol.NeedRehash((ulong)buckets.Length, nelem, 1);
            if (re) Rehash(n);
            var node = new HNode<E> { val = e, code = code };
            InsertBucketBegin(Bkt(code), node);
            nelem++;
            return (node, true);
        }

        void InsertBucketBegin(ulong b, HNode<E> node)
        {
            if (buckets[b] != null)
            {
                node.next = buckets[b]!.next;
                buckets[b]!.next = node;
            }
            else
            {
                node.next = beforeBegin.next;
                beforeBegin.next = node;
                if (node.next != null) buckets[Bkt(node.next.code)] = node;
                buckets[b] = beforeBegin;
            }
        }

        protected void Rehash(ulong n)
        {
            var nb = new HNode<E>?[n];
            var p = beforeBegin.next;
            beforeBegin.next = null;
            ulong bbeginBkt = 0;
            while (p != null)
            {
                var next = p.next;
                ulong b = p.code % n;
                if (nb[b] == null)
                {
                    p.next = beforeBegin.next;
                    beforeBegin.next = p;
                    nb[b] = beforeBegin;
                    if (p.next != null) nb[bbeginBkt] = p;
                    bbeginBkt = b;
                }
                else
                {
                    p.next = nb[b]!.next;
                    nb[b]!.next = p;
                }
                p = next;
            }
            buckets = nb;
        }

        protected bool EraseKey(K k)
        {
            ulong code = hash(k);
            ulong b = Bkt(code);
            var prev = buckets[b];
            if (prev == null) return false;
            var p = prev.next;
            while (p != null && !(p.code == code && eq(k, key(p.val))))
            {
                if (p.next == null || Bkt(p.next.code) != b) return false;
                prev = p;
                p = p.next;
            }
            if (p == null) return false;
            EraseNode(b, prev!, p);
            return true;
        }

        void EraseNode(ulong b, HNode<E> prev, HNode<E> n)
        {
            if (ReferenceEquals(prev, buckets[b]))
            {
                // removing the bucket's first node (_M_remove_bucket_begin)
                var next = n.next;
                ulong nextBkt = next != null ? Bkt(next.code) : 0;
                if (next == null || nextBkt != b)
                {
                    if (next != null) buckets[nextBkt] = buckets[b];
                    if (ReferenceEquals(buckets[b], beforeBegin)) beforeBegin.next = next;
                    buckets[b] = null;
                }
            }
            else if (n.next != null)
            {
                ulong nextBkt = Bkt(n.next.code);
                if (nextBkt != b) buckets[nextBkt] = prev;
            }
            prev.next = n.next;
            nelem--;
        }

        public void EraseAt(HNode<E> n)
        {
            ulong b = Bkt(n.code);
            var prev = buckets[b]!;
            while (!ReferenceEquals(prev.next, n)) prev = prev.next!;
            EraseNode(b, prev, n);
        }

        public ulong size() => nelem;
        public bool empty() => nelem == 0;
        public void clear() { buckets = new HNode<E>?[buckets.Length]; beforeBegin.next = null; nelem = 0; }
        public ulong bucket_count() => (ulong)buckets.Length;

        public void reserve(ulong n) => rehash(pol.BktForElements(n));
        public void rehash(ulong n)
        {
            // _M_rehash_policy._M_next_bkt(max(n, bkt_for_elements(size + 1))), only if it differs
            ulong want = pol.NextBkt(Math.Max(n, pol.BktForElements(nelem + 1)));
            if (want != (ulong)buckets.Length) Rehash(want);
        }

        public IEnumerable<E> Items()
        {
            for (var p = beforeBegin.next; p != null; p = p.next) yield return p.val;
        }

        public MIt<E> begin() => new MIt<E>(beforeBegin.next);
        public MIt<E> end() => new MIt<E>(null);
        public MIt<E> find(K k) => new MIt<E>(Find(k));
        public ulong count_(K k) => Find(k) != null ? 1UL : 0UL;
        public ulong count(K k) => count_(k);
        public bool contains(K k) => Find(k) != null;
        public ulong erase(K k) => EraseKey(k) ? 1UL : 0UL;
        public MIt<E> erase(MIt<E> it) { var nx = it.node!.next; EraseAt(it.node); return new MIt<E>(nx); }

        protected void CopyFrom(HashTable<K, E> o, Func<E, E> copy)
        {
            // _M_assign: same bucket count, same node order
            buckets = new HNode<E>?[o.buckets.Length];
            beforeBegin.next = null;
            nelem = 0;
            pol.NextResize = o.pol.NextResize;
            HNode<E> prev = beforeBegin;
            for (var p = o.beforeBegin.next; p != null; p = p.next)
            {
                var n = new HNode<E> { val = copy(p.val), code = p.code };
                prev.next = n;
                ulong b = Bkt(n.code);
                if (buckets[b] == null) buckets[b] = prev;
                prev = n;
                nelem++;
            }
        }
    }

    /// <summary>An iterator over a hashed or sorted container: a node (null at end) or a position in a list.</summary>
    public struct MIt<E> : IEquatable<MIt<E>>
    {
        public MIt<E> Clone() => this;      // a value: copying it is the copy
        public readonly HNode<E>? node;
        public readonly List<E>? list;
        public readonly int pos;
        public MIt(HNode<E>? n) { node = n; list = null; pos = 0; }
        public MIt(List<E> l, int p) { node = null; list = l; pos = p; }
        public E Deref => list != null ? list[pos] : node!.val;
        public static MIt<E> operator ++(MIt<E> x) => x.list != null ? new MIt<E>(x.list, x.pos + 1) : new MIt<E>(x.node!.next);
        public static MIt<E> operator --(MIt<E> x) => new MIt<E>(x.list!, x.pos - 1);
        public static bool operator ==(MIt<E> a, MIt<E> b) => a.list != null || b.list != null
            ? (a.list ?? b.list) != null && PosOf(a) == PosOf(b) : ReferenceEquals(a.node, b.node);
        public static bool operator !=(MIt<E> a, MIt<E> b) => !(a == b);
        static int PosOf(MIt<E> x) => x.list != null ? x.pos : int.MaxValue;
        public bool Equals(MIt<E> o) => this == o;
        public override bool Equals(object? o) => o is MIt<E> x && this == x;
        public override int GetHashCode() => pos;
    }

    public class UMap<K, V> : HashTable<K, Pair<K, V>>, ICopy<UMap<K, V>>
    {
        /// <summary>How to make a mapped value that V alone cannot describe (a std::array); see Vec.mk.</summary>
        public Func<V>? mk;
        public UMap(Func<K, ulong>? h = null, Func<K, K, bool>? eq = null, Func<V>? mk = null) : base(p => p.first, h, eq) { this.mk = mk; }
        V MakeV() => mk != null ? mk() : Elem<V>.Make();
        public UMap(Pair<K, V>[] il, Func<K, ulong>? h = null, Func<K, K, bool>? eq = null) : this(h, eq) { foreach (var p in il) Insert(p.Clone()); }

        /// <summary>operator[]: the value, inserted default-constructed if absent.</summary>
        public ref V Index(K k)
        {
            var f = Find(k);
            if (f == null) f = Insert(new Pair<K, V>(KeyCopy<K>.Of(k), MakeV())).Item1;   // the map owns a copy of the key
            return ref f.val.second;
        }
        public ref V at(K k)
        {
            var f = Find(k);
            if (f == null) throw new OutOfRange("_Map_base::at");
            return ref f.val.second;
        }
        public Pair<MIt<Pair<K, V>>, bool> insert(Pair<K, V> p) { var (n, added) = Insert(new Pair<K, V>(KeyCopy<K>.Of(p.first), p.second)); return new Pair<MIt<Pair<K, V>>, bool>(new MIt<Pair<K, V>>(n), added); }
        public Pair<MIt<Pair<K, V>>, bool> emplace(K k, V v) => insert(new Pair<K, V>(k, v));
        public Pair<MIt<Pair<K, V>>, bool> emplace(Pair<K, V> p) => insert(p);
        public Pair<MIt<Pair<K, V>>, bool> try_emplace(K k, V v) => insert(new Pair<K, V>(k, v));
        public Pair<MIt<Pair<K, V>>, bool> try_emplace(K k) => insert(new Pair<K, V>(k, MakeV()));
        public Pair<MIt<Pair<K, V>>, bool> insert_or_assign(K k, V v)
        {
            var f = Find(k);
            if (f != null) { f.val.second = v; return new Pair<MIt<Pair<K, V>>, bool>(new MIt<Pair<K, V>>(f), false); }
            return insert(new Pair<K, V>(k, v));
        }
        public UMap<K, V> Clone() { var m = new UMap<K, V>(hash, eq, mk); m.CopyFrom(this, p => p.Clone()); return m; }
        public void Assign(UMap<K, V> o) { if (!ReferenceEquals(this, o)) CopyFrom(o, p => p.Clone()); }
        public void MoveFrom(UMap<K, V> o) { if (!ReferenceEquals(this, o)) { CopyFrom(o, p => p); o.clear(); } }
    }

    public class USet<T> : HashTable<T, T>, ICopy<USet<T>>
    {
        public USet(Func<T, ulong>? h = null, Func<T, T, bool>? eq = null) : base(x => x, h, eq) { }
        public USet(T[] il, Func<T, ulong>? h = null, Func<T, T, bool>? eq = null) : this(h, eq) { foreach (var x in il) Insert(x); }
        public Pair<MIt<T>, bool> insert(T x) { var (n, added) = Insert(KeyCopy<T>.Of(x)); return new Pair<MIt<T>, bool>(new MIt<T>(n), added); }
        public Pair<MIt<T>, bool> emplace(T x) => insert(x);
        public USet<T> Clone() { var m = new USet<T>(hash, eq); m.CopyFrom(this, x => Elem<T>.Copy(x)); return m; }
        public void Assign(USet<T> o) { if (!ReferenceEquals(this, o)) CopyFrom(o, x => Elem<T>.Copy(x)); }
        public void MoveFrom(USet<T> o) { if (!ReferenceEquals(this, o)) { CopyFrom(o, x => x); o.clear(); } }
    }

    /// <summary>A hasher of the engine's own (struct CiHash { size_t operator()(...) }): the generated class
    /// implements this by calling its operator().</summary>
    public interface IHashFn<K> { ulong HashOf(K k); }
    /// <summary>A two-argument predicate of the engine's own (a key-equal such as CiEqual).</summary>
    public interface IBinFn<K> { bool Call2(K a, K b); }
    /// <summary>std::equal_to, for a container whose hasher is the engine's but whose key-equal is the default.</summary>
    public sealed class StdEq<K> : IBinFn<K> { public bool Call2(K a, K b) => EqualityComparer<K>.Default.Equals(a, b); }

    /// <summary>std::unordered_map&lt;K, V, H, E&gt; with the engine's hasher H and key-equal E: the same table,
    /// hashed by H, so lookups and the iteration order are the C++ ones.</summary>
    public sealed class UMapC<K, V, H, E> : UMap<K, V>, ICopy<UMapC<K, V, H, E>>
        where H : IHashFn<K>, new() where E : IBinFn<K>, new()
    {
        static readonly H h0 = new H();
        static readonly E e0 = new E();
        public UMapC(Func<V>? mk = null) : base(k => h0.HashOf(k), (a, b) => e0.Call2(a, b), mk) { }
        public UMapC(Pair<K, V>[] il) : this() { foreach (var p in il) Insert(p.Clone()); }
        public new UMapC<K, V, H, E> Clone() { var m = new UMapC<K, V, H, E>(mk); m.CopyFrom(this, p => p.Clone()); return m; }
        public void Assign(UMapC<K, V, H, E> o) => base.Assign(o);
        public void MoveFrom(UMapC<K, V, H, E> o) => base.MoveFrom(o);
    }

    /// <summary>std::unordered_set&lt;T, H, E&gt; with the engine's hasher and key-equal.</summary>
    public sealed class USetC<T, H, E> : USet<T>, ICopy<USetC<T, H, E>>
        where H : IHashFn<T>, new() where E : IBinFn<T>, new()
    {
        static readonly H h0 = new H();
        static readonly E e0 = new E();
        public USetC() : base(k => h0.HashOf(k), (a, b) => e0.Call2(a, b)) { }
        public USetC(T[] il) : this() { foreach (var x in il) Insert(x); }
        public new USetC<T, H, E> Clone() { var m = new USetC<T, H, E>(); m.CopyFrom(this, x => Elem<T>.Copy(x)); return m; }
        public void Assign(USetC<T, H, E> o) => base.Assign(o);
        public void MoveFrom(USetC<T, H, E> o) => base.MoveFrom(o);
    }

    /// <summary>std::map: keys kept sorted by the comparator (a red-black tree's order is the sorted order).</summary>
    public sealed class Map<K, V> : ICopy<Map<K, V>>
    {
        readonly Func<K, K, bool> lt;
        List<Pair<K, V>> items = new List<Pair<K, V>>();
        public Func<V>? mk;
        public Map(Func<K, K, bool>? less = null, Func<V>? mk = null) { lt = less ?? Less<K>.Fn; this.mk = mk; }
        public Map(Pair<K, V>[] il, Func<K, K, bool>? less = null) : this(less) { foreach (var p in il) insert(p.Clone()); }

        int Lower(K k)
        {
            int lo = 0, hi = items.Count;
            while (lo < hi) { int mid = (lo + hi) / 2; if (lt(items[mid].first, k)) lo = mid + 1; else hi = mid; }
            return lo;
        }
        int Find(K k) { int i = Lower(k); return (i < items.Count && !lt(k, items[i].first)) ? i : -1; }

        public ref V Index(K k)
        {
            int i = Lower(k);
            if (i == items.Count || lt(k, items[i].first)) items.Insert(i, new Pair<K, V>(KeyCopy<K>.Of(k), mk != null ? mk() : Elem<V>.Make()));
            return ref items[i].second;
        }
        public ref V at(K k) { int i = Find(k); if (i < 0) throw new OutOfRange("map::at"); return ref items[i].second; }
        public Pair<MIt<Pair<K, V>>, bool> insert(Pair<K, V> p)
        {
            int i = Lower(p.first);
            if (i < items.Count && !lt(p.first, items[i].first)) return new Pair<MIt<Pair<K, V>>, bool>(new MIt<Pair<K, V>>(items, i), false);
            items.Insert(i, new Pair<K, V>(KeyCopy<K>.Of(p.first), p.second));
            return new Pair<MIt<Pair<K, V>>, bool>(new MIt<Pair<K, V>>(items, i), true);
        }
        public Pair<MIt<Pair<K, V>>, bool> emplace(K k, V v) => insert(new Pair<K, V>(k, v));
        public Pair<MIt<Pair<K, V>>, bool> emplace(Pair<K, V> p) => insert(p);
        public Pair<MIt<Pair<K, V>>, bool> try_emplace(K k, V v) => insert(new Pair<K, V>(k, v));
        public Pair<MIt<Pair<K, V>>, bool> insert_or_assign(K k, V v)
        {
            int i = Find(k);
            if (i >= 0) { items[i].second = v; return new Pair<MIt<Pair<K, V>>, bool>(new MIt<Pair<K, V>>(items, i), false); }
            return insert(new Pair<K, V>(k, v));
        }
        public MIt<Pair<K, V>> find(K k) { int i = Find(k); return new MIt<Pair<K, V>>(items, i < 0 ? items.Count : i); }
        public MIt<Pair<K, V>> lower_bound(K k) => new MIt<Pair<K, V>>(items, Lower(k));
        public MIt<Pair<K, V>> upper_bound(K k) { int i = Lower(k); while (i < items.Count && !lt(k, items[i].first)) i++; return new MIt<Pair<K, V>>(items, i); }
        public ulong count(K k) => Find(k) >= 0 ? 1UL : 0UL;
        public bool contains(K k) => Find(k) >= 0;
        public ulong erase(K k) { int i = Find(k); if (i < 0) return 0; items.RemoveAt(i); return 1; }
        public MIt<Pair<K, V>> erase(MIt<Pair<K, V>> it) { items.RemoveAt(it.pos); return new MIt<Pair<K, V>>(items, it.pos); }
        public ulong size() => (ulong)items.Count;
        public bool empty() => items.Count == 0;
        public void clear() => items.Clear();
        public MIt<Pair<K, V>> begin() => new MIt<Pair<K, V>>(items, 0);
        public MIt<Pair<K, V>> end() => new MIt<Pair<K, V>>(items, items.Count);
        public IEnumerable<Pair<K, V>> Items() => items;
        public Map<K, V> Clone() { var m = new Map<K, V>(lt, mk); foreach (var p in items) m.items.Add(p.Clone()); return m; }
        public void Assign(Map<K, V> o) { if (ReferenceEquals(this, o)) return; items = new List<Pair<K, V>>(); foreach (var p in o.items) items.Add(p.Clone()); }
        public void MoveFrom(Map<K, V> o) { if (ReferenceEquals(this, o)) return; items = o.items; o.items = new List<Pair<K, V>>(); }
    }

    public sealed class Set<T> : ICopy<Set<T>>
    {
        readonly Func<T, T, bool> lt;
        List<T> items = new List<T>();
        public Set(Func<T, T, bool>? less = null) { lt = less ?? Less<T>.Fn; }
        public Set(T[] il, Func<T, T, bool>? less = null) : this(less) { foreach (var x in il) insert(x); }
        int Lower(T k) { int lo = 0, hi = items.Count; while (lo < hi) { int mid = (lo + hi) / 2; if (lt(items[mid], k)) lo = mid + 1; else hi = mid; } return lo; }
        public Pair<MIt<T>, bool> insert(T x)
        {
            int i = Lower(x);
            if (i < items.Count && !lt(x, items[i])) return new Pair<MIt<T>, bool>(new MIt<T>(items, i), false);
            items.Insert(i, KeyCopy<T>.Of(x));
            return new Pair<MIt<T>, bool>(new MIt<T>(items, i), true);
        }
        public Pair<MIt<T>, bool> emplace(T x) => insert(x);
        public MIt<T> find(T k) { int i = Lower(k); return new MIt<T>(items, (i < items.Count && !lt(k, items[i])) ? i : items.Count); }
        public ulong count(T k) { int i = Lower(k); return (i < items.Count && !lt(k, items[i])) ? 1UL : 0UL; }
        public bool contains(T k) => count(k) > 0;
        public ulong erase(T k) { int i = Lower(k); if (i < items.Count && !lt(k, items[i])) { items.RemoveAt(i); return 1; } return 0; }
        public MIt<T> erase(MIt<T> it) { items.RemoveAt(it.pos); return new MIt<T>(items, it.pos); }
        public ulong size() => (ulong)items.Count;
        public bool empty() => items.Count == 0;
        public void clear() => items.Clear();
        public MIt<T> begin() => new MIt<T>(items, 0);
        public MIt<T> end() => new MIt<T>(items, items.Count);
        public IEnumerable<T> Items() => items;
        public Set<T> Clone() { var m = new Set<T>(lt); foreach (var x in items) m.items.Add(Elem<T>.Copy(x)); return m; }
        public void Assign(Set<T> o) { if (ReferenceEquals(this, o)) return; items = new List<T>(); foreach (var x in o.items) items.Add(Elem<T>.Copy(x)); }
        public void MoveFrom(Set<T> o) { if (ReferenceEquals(this, o)) return; items = o.items; o.items = new List<T>(); }
    }

    /// <summary>std::stack over std::deque: push, pop, top.</summary>
    public sealed class Stk<T> : ICopy<Stk<T>>
    {
        readonly List<T> items = new List<T>();
        public void push(T x) => items.Add(x);
        public void pop() => items.RemoveAt(items.Count - 1);
        public ref T top() { var span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(items); return ref span[items.Count - 1]; }
        public bool empty() => items.Count == 0;
        public ulong size() => (ulong)items.Count;
        public Stk<T> Clone() { var s = new Stk<T>(); foreach (var x in items) s.items.Add(Elem<T>.Copy(x)); return s; }
        public void Assign(Stk<T> o) { if (ReferenceEquals(this, o)) return; items.Clear(); foreach (var x in o.items) items.Add(Elem<T>.Copy(x)); }
        public void MoveFrom(Stk<T> o) { if (ReferenceEquals(this, o)) return; items.Clear(); items.AddRange(o.items); o.items.Clear(); }
    }

    /// <summary>std::queue over std::deque: first in, first out.</summary>
    public sealed class Que<T> : ICopy<Que<T>>
    {
        readonly List<T> items = new List<T>();
        int head;
        public void push(T x) => items.Add(x);
        public void pop() { items[head] = default!; head++; if (head > 64 && head * 2 > items.Count) { items.RemoveRange(0, head); head = 0; } }
        public ref T front() { var span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(items); return ref span[head]; }
        public ref T back() { var span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(items); return ref span[items.Count - 1]; }
        public bool empty() => items.Count == head;
        public ulong size() => (ulong)(items.Count - head);
        public Que<T> Clone() { var q = new Que<T>(); for (int i = head; i < items.Count; i++) q.items.Add(Elem<T>.Copy(items[i])); return q; }
        public void Assign(Que<T> o) { if (ReferenceEquals(this, o)) return; items.Clear(); head = 0; for (int i = o.head; i < o.items.Count; i++) items.Add(Elem<T>.Copy(o.items[i])); }
        public void MoveFrom(Que<T> o) { if (ReferenceEquals(this, o)) return; items.Clear(); head = 0; for (int i = o.head; i < o.items.Count; i++) items.Add(o.items[i]); o.items.Clear(); o.head = 0; }
    }
}
