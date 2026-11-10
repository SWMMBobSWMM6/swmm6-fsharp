// Str.cs - std::string (and std::string_view, which the engine only reads) as a mutable byte sequence, so
// lengths, comparisons and printf padding count bytes as libstdc++ does. c_str() hands out a NUL-terminated
// CPtr over the same buffer; npos is ulong.MaxValue as in C++.
// Written by Claude (Anthropic) for the SWMM6 ports; see AI-USE.md.
using System;
using System.Text;

namespace Cpp
{
    public sealed class Str : ICopy<Str>, IEquatable<Str>, IComparable<Str>
    {
        /// <summary>const char* -> std::string: the converting constructor C++ applies implicitly.</summary>
        public static implicit operator Str(CPtr p) => new Str(p);
        public const ulong npos = ulong.MaxValue;
        public byte[] b;       // capacity >= n + 1; b[n] == 0 whenever c_str() is taken
        public int n;

        public Str() { b = new byte[16]; }
        public Str(Str s) : this(s.b, 0, s.n) { }        // the copy constructor (std::string(view))
        /// <summary>string_view::remove_prefix / remove_suffix (a view here is a Str of its own).</summary>
        public void remove_prefix(ulong k) { int m = (int)k; Array.Copy(b, m, b, 0, n - m); n -= m; b[n] = 0; }
        public void remove_suffix(ulong k) { n -= (int)k; b[n] = 0; }
        public Str(string s) { var e = Encoding.Latin1.GetBytes(s); b = new byte[e.Length + 1]; Array.Copy(e, b, e.Length); n = e.Length; }
        public Str(byte[] src, int off, int len) { b = new byte[len + 1]; if (len != 0) Array.Copy(src, off, b, 0, len); n = len; }
        public Str(CPtr p) : this(p.B, p.O, (int)R.strlen(p)) { }
        public Str(CPtr p, ulong len) : this(p.B, p.O, (int)len) { }
        public Str(ulong count, int ch) { n = (int)count; b = new byte[n + 1]; for (int i = 0; i < n; i++) b[i] = (byte)ch; }
        public Str(Str s, ulong pos, ulong len = npos) { int p = (int)pos; int m = (int)Math.Min(len, (ulong)(s.n - p)); b = new byte[m + 1]; Array.Copy(s.b, p, b, 0, m); n = m; }
        public static Str FromBytes(byte[] x) => new Str(x, 0, x.Length);

        void Reserve(int need)
        {
            if (need + 1 <= b.Length) return;
            int cap = Math.Max(need + 1, b.Length * 2);
            Array.Resize(ref b, cap);
        }

        public ulong size() => (ulong)n;
        public ulong length() => (ulong)n;
        public bool empty() => n == 0;
        public void clear() { n = 0; }
        public CPtr c_str() { Reserve(n); b[n] = 0; return new CPtr(b, 0); }
        public CPtr data() => c_str();
        public ref byte this[ulong i] => ref b[i];
        public ref byte this[long i] => ref b[i];
        public ref byte this[int i] => ref b[i];
        public byte at(ulong i) { if (i >= (ulong)n) throw new OutOfRange("basic_string::at: __n (which is " + i + ") >= this->size() (which is " + n + ")"); return b[i]; }
        public ref byte front() => ref b[0];
        public ref byte back() => ref b[n - 1];
        public sbyte Char(ulong i) => (sbyte)b[i];

        public Str Clone() => new Str(b, 0, n);
        public void Assign(Str o) { if (ReferenceEquals(this, o)) return; Reserve(o.n); Array.Copy(o.b, b, o.n); n = o.n; }
        public void Assign(CPtr p) { int m = (int)R.strlen(p); var t = new byte[m + 1]; Array.Copy(p.B, p.O, t, 0, m); b = t; n = m; }
        public void Assign(int ch) { Reserve(1); b[0] = (byte)ch; n = 1; }
        public void MoveFrom(Str o) { if (ReferenceEquals(this, o)) return; b = o.b; n = o.n; o.b = new byte[16]; o.n = 0; }
        public Str assign(Str o) { Assign(o); return this; }
        public Str assign(CPtr p) { Assign(p); return this; }
        public Str assign(CPtr b0, CPtr e0) => assign(b0, (ulong)(e0.O - b0.O));     // assign(first, last)
        public Str assign(Ptr<sbyte> b0, Ptr<sbyte> e0) => assign((CPtr)b0, (ulong)(e0.o - b0.o));
        public Str assign(CPtr p, ulong len) { var t = new byte[(int)len + 1]; if (len != 0) Array.Copy(p.B, p.O, t, 0, (int)len); b = t; n = (int)len; return this; }
        public Str assign(ulong count, int ch) { Reserve((int)count); for (int i = 0; i < (int)count; i++) b[i] = (byte)ch; n = (int)count; return this; }

        public Str append(Str o) { Reserve(n + o.n); Array.Copy(o.b, 0, b, n, o.n); n += o.n; return this; }
        public Str append(Str o, ulong pos, ulong len) { int p = (int)pos; int m = (int)Math.Min(len, (ulong)(o.n - p)); Reserve(n + m); Array.Copy(o.b, p, b, n, m); n += m; return this; }
        public Str append(CPtr p) { int m = (int)R.strlen(p); Reserve(n + m); Array.Copy(p.B, p.O, b, n, m); n += m; return this; }
        public Str append(CPtr p, ulong len) { int m = (int)len; if (m == 0) return this; Reserve(n + m); Array.Copy(p.B, p.O, b, n, m); n += m; return this; }
        public Str append(ulong count, int ch) { int m = (int)count; Reserve(n + m); for (int i = 0; i < m; i++) b[n + i] = (byte)ch; n += m; return this; }
        public Str append(int ch) => append(1UL, ch);
        public void push_back(int ch) { Reserve(n + 1); b[n++] = (byte)ch; }
        public void pop_back() { n--; }
        public void resize(ulong m, int ch = 0) { int k = (int)m; Reserve(k); for (int i = n; i < k; i++) b[i] = (byte)ch; n = k; }
        public void reserve(ulong m) { Reserve((int)m); }
        public void shrink_to_fit() { }

        public Str substr(ulong pos = 0, ulong len = npos)
        {
            if (pos > (ulong)n) throw new OutOfRange(string.Format("basic_string::substr: __pos (which is {0}) > this->size() (which is {1})", pos, n));
            int m = (int)Math.Min(len, (ulong)n - pos);
            return new Str(b, (int)pos, m);
        }

        public Str erase(ulong pos = 0, ulong len = npos)
        {
            int p = (int)pos; int m = (int)Math.Min(len, (ulong)(n - p));
            Array.Copy(b, p + m, b, p, n - p - m);
            n -= m;
            return this;
        }
        public Str insert(ulong pos, Str s) { int p = (int)pos; Reserve(n + s.n); Array.Copy(b, p, b, p + s.n, n - p); Array.Copy(s.b, 0, b, p, s.n); n += s.n; return this; }
        public Str insert(ulong pos, CPtr s) => insert(pos, new Str(s));
        public Str insert(ulong pos, ulong count, int ch) => insert(pos, new Str(count, ch));
        public Str replace(ulong pos, ulong len, Str s)
        {
            int p = (int)pos; int m = (int)Math.Min(len, (ulong)(n - p));
            var t = new byte[n - m + s.n + 1];
            Array.Copy(b, 0, t, 0, p); Array.Copy(s.b, 0, t, p, s.n); Array.Copy(b, p + m, t, p + s.n, n - p - m);
            b = t; n = n - m + s.n;
            return this;
        }
        public Str replace(ulong pos, ulong len, CPtr s) => replace(pos, len, new Str(s));

        static bool In(byte c, Str set) { for (int i = 0; i < set.n; i++) if (set.b[i] == c) return true; return false; }

        public ulong find(Str s, ulong pos = 0)
        {
            if (s.n == 0) return pos <= (ulong)n ? pos : npos;
            for (long i = (long)pos; i + s.n <= n; i++)
            {
                int j = 0;
                while (j < s.n && b[i + j] == s.b[j]) j++;
                if (j == s.n) return (ulong)i;
            }
            return npos;
        }
        public ulong find(CPtr s, ulong pos = 0) => find(new Str(s), pos);
        public ulong find(int ch, ulong pos = 0) { for (long i = (long)pos; i < n; i++) if (b[i] == (byte)ch) return (ulong)i; return npos; }
        public ulong rfind(Str s, ulong pos = npos)
        {
            if (s.n > n) return npos;
            long start = (long)Math.Min(pos, (ulong)(n - s.n));
            for (long i = start; i >= 0; i--)
            {
                int j = 0;
                while (j < s.n && b[i + j] == s.b[j]) j++;
                if (j == s.n) return (ulong)i;
            }
            return npos;
        }
        public ulong rfind(CPtr s, ulong pos = npos) => rfind(new Str(s), pos);
        public ulong rfind(int ch, ulong pos = npos) { if (n == 0) return npos; long i = (long)Math.Min(pos, (ulong)(n - 1)); for (; i >= 0; i--) if (b[i] == (byte)ch) return (ulong)i; return npos; }
        public ulong find_first_of(Str set, ulong pos = 0) { for (long i = (long)pos; i < n; i++) if (In(b[i], set)) return (ulong)i; return npos; }
        public ulong find_first_of(CPtr set, ulong pos = 0) => find_first_of(new Str(set), pos);
        public ulong find_first_of(int ch, ulong pos = 0) => find(ch, pos);
        public ulong find_first_not_of(Str set, ulong pos = 0) { for (long i = (long)pos; i < n; i++) if (!In(b[i], set)) return (ulong)i; return npos; }
        public ulong find_first_not_of(CPtr set, ulong pos = 0) => find_first_not_of(new Str(set), pos);
        public ulong find_first_not_of(int ch, ulong pos = 0) { for (long i = (long)pos; i < n; i++) if (b[i] != (byte)ch) return (ulong)i; return npos; }
        public ulong find_last_of(Str set, ulong pos = npos) { if (n == 0) return npos; long i = (long)Math.Min(pos, (ulong)(n - 1)); for (; i >= 0; i--) if (In(b[i], set)) return (ulong)i; return npos; }
        public ulong find_last_of(CPtr set, ulong pos = npos) => find_last_of(new Str(set), pos);
        public ulong find_last_of(int ch, ulong pos = npos) => rfind(ch, pos);
        public ulong find_last_not_of(Str set, ulong pos = npos) { if (n == 0) return npos; long i = (long)Math.Min(pos, (ulong)(n - 1)); for (; i >= 0; i--) if (!In(b[i], set)) return (ulong)i; return npos; }
        public ulong find_last_not_of(CPtr set, ulong pos = npos) => find_last_not_of(new Str(set), pos);
        public ulong find_last_not_of(int ch, ulong pos = npos) { if (n == 0) return npos; long i = (long)Math.Min(pos, (ulong)(n - 1)); for (; i >= 0; i--) if (b[i] != (byte)ch) return (ulong)i; return npos; }

        public bool starts_with(Str s) { if (s.n > n) return false; for (int i = 0; i < s.n; i++) if (b[i] != s.b[i]) return false; return true; }
        public bool starts_with(CPtr s) => starts_with(new Str(s));
        public bool starts_with(int ch) => n > 0 && b[0] == (byte)ch;
        public bool ends_with(Str s) { if (s.n > n) return false; for (int i = 0; i < s.n; i++) if (b[n - s.n + i] != s.b[i]) return false; return true; }
        public bool ends_with(CPtr s) => ends_with(new Str(s));
        public bool ends_with(int ch) => n > 0 && b[n - 1] == (byte)ch;
        public bool contains(Str s) => find(s) != npos;
        public bool contains(int ch) => find(ch) != npos;

        /// <summary>char_traits&lt;char&gt;::compare is memcmp: unsigned bytes, then length.</summary>
        public int compare(Str o)
        {
            int m = Math.Min(n, o.n);
            for (int i = 0; i < m; i++) if (b[i] != o.b[i]) return b[i] < o.b[i] ? -1 : 1;
            long d = (long)n - o.n;
            return d > int.MaxValue ? int.MaxValue : d < int.MinValue ? int.MinValue : (int)d;
        }
        public int compare(CPtr o) => compare(new Str(o));
        public int compare(ulong pos, ulong len, Str o) => substr(pos, len).compare(o);
        public int CompareTo(Str? o) => compare(o!);
        public bool Equals(Str? o)
        {
            if (o is null || o.n != n) return false;
            for (int i = 0; i < n; i++) if (b[i] != o.b[i]) return false;
            return true;
        }
        public override bool Equals(object? o) => o is Str s && Equals(s);
        public override int GetHashCode() { int h = n; for (int i = 0; i < n; i++) h = h * 31 + b[i]; return h; }
        public bool EqLit(CPtr p) { int m = (int)R.strlen(p); if (m != n) return false; for (int i = 0; i < n; i++) if (b[i] != p.B[p.O + i]) return false; return true; }

        public static Str Cat(Str x, Str y) { var r = new Str(x.b, 0, x.n); r.append(y); return r; }
        public static Str Cat(Str x, CPtr y) { var r = new Str(x.b, 0, x.n); r.append(y); return r; }
        public static Str Cat(CPtr x, Str y) { var r = new Str(x); r.append(y); return r; }
        public static Str Cat(Str x, int ch) { var r = new Str(x.b, 0, x.n); r.push_back(ch); return r; }
        public static Str Cat(int ch, Str y) { var r = new Str(1, ch); r.append(y); return r; }

        public byte[] Bytes() { var r = new byte[n]; Array.Copy(b, r, n); return r; }
        public override string ToString() => Encoding.Latin1.GetString(b, 0, n);

        public Iter<byte> begin() => new Iter<byte>(b, 0);
        public Iter<byte> end() => new Iter<byte>(b, n);

        // ---- std::to_string / std::stod and friends (libstdc++: to_string(double) is "%f") ----
        public static Str to_string(int v) => new Str(v.ToString(System.Globalization.CultureInfo.InvariantCulture));
        public static Str to_string(long v) => new Str(v.ToString(System.Globalization.CultureInfo.InvariantCulture));
        public static Str to_string(ulong v) => new Str(v.ToString(System.Globalization.CultureInfo.InvariantCulture));
        public static Str to_string(uint v) => new Str(v.ToString(System.Globalization.CultureInfo.InvariantCulture));
        public static Str to_string(double v) => FromBytes(R.Format(R.Lit("%f"), new object[] { v }));
        public static Str to_string(float v) => FromBytes(R.Format(R.Lit("%f"), new object[] { (double)v }));

        /// <summary>std::stod (libstdc++'s __stoa over strtod): invalid_argument("stod") when nothing converts,
        /// out_of_range("stod") when strtod sets ERANGE (overflow, or an underflow: "1e999", "1e-400", and a
        /// subnormal that is not exact, such as 2.2250738585072011e-308).</summary>
        public static double stod(Str s, Ptr<ulong> idx = default)
        {
            var p = s.c_str(); var end = p;
            double v = R.strtod(p, ref end);
            if (end.O == p.O) throw new InvalidArgument("stod");
            if (R.ERange) throw new OutOfRange("stod");
            if (!idx.IsNull) idx.Deref = (ulong)(end.O - p.O);
            return v;
        }
        public static int stoi(Str s, Ptr<ulong> idx = default, int bas = 10)
        {
            var p = s.c_str(); var end = p;
            long v = R.strtol(p, ref end, bas);
            if (end.O == p.O) throw new InvalidArgument("stoi");
            if (v < int.MinValue || v > int.MaxValue) throw new OutOfRange("stoi");
            if (!idx.IsNull) idx.Deref = (ulong)(end.O - p.O);
            return (int)v;
        }
        public static long stol(Str s, Ptr<ulong> idx = default, int bas = 10)
        {
            var p = s.c_str(); var end = p;
            long v = R.strtol(p, ref end, bas);
            if (end.O == p.O) throw new InvalidArgument("stol");
            if (!idx.IsNull) idx.Deref = (ulong)(end.O - p.O);
            return v;
        }
    }
}
