// Streams.cs - the iostreams the engine uses, with libstdc++'s formatting: a double is written with the printf
// format libstdc++'s num_put builds from the stream flags (%.*g by default, %.*f for fixed, %.*e for scientific,
// '#' for showpoint), through the glibc-exact formatter; width and fill are applied afterwards and width resets.
// Reading numbers goes through strtod / strtol as libstdc++'s num_get does for ordinary input.
// Written by Claude (Anthropic) for the SWMM6 ports; see AI-USE.md.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Cpp
{
    public static class Ios
    {
        // the libstdc++ values of ios_base::openmode, seekdir, fmtflags and iostate
        public const int @in = 8, @out = 16, binary = 4, app = 1, trunc = 32, ate = 2;
        public const int beg = 0, cur = 1, end = 2;
        public const int boolalpha = 1, dec = 2, @fixed = 4, hex = 8, @internal = 16, left = 32, oct = 64, right = 128,
            scientific = 256, showbase = 512, showpoint = 1024, showpos = 2048, skipws = 4096, unitbuf = 8192,
            uppercase = 16384, adjustfield = 176, basefield = 74, floatfield = 260;
        public const int goodbit = 0, badbit = 1, eofbit = 2, failbit = 4;
    }

    public sealed class SetPrec { public readonly int n; public SetPrec(int n) { this.n = n; } }
    public sealed class SetW { public readonly int n; public SetW(int n) { this.n = n; } }
    public sealed class SetFill { public readonly int c; public SetFill(int c) { this.c = c; } }

    public class IosBase
    {
        public bool failbit, eofbit, badbit;
        public long prec = 6;
        public int flagsv = 0;
        public long widthv = 0;
        public int fillc = ' ';
        public bool good() => !failbit && !eofbit && !badbit;
        public bool fail() => failbit || badbit;
        public bool bad() => badbit;
        public bool eof() => eofbit;
        public bool ok() => !fail();
        public bool notok() => fail();
        public void clear(int s = 0) { failbit = eofbit = badbit = false; }
        public long precision() => prec;
        public long precision(long p) { long o = prec; prec = p; return o; }
        public long width() => widthv;
        public long width(long w) { long o = widthv; widthv = w; return o; }
        public int fill() => fillc;
        public int fill(int c) { int o = fillc; fillc = c; return o; }
        public int setf(int f) { int o = flagsv; flagsv |= f; return o; }
        public int setf(int f, int mask) { int o = flagsv; flagsv = (flagsv & ~mask) | (f & mask); return o; }
        public void unsetf(int f) { flagsv &= ~f; }
        public object? rdbuf() => null;
        public void imbue(object? l) { }
        public void exceptions(int m) { }
    }

    public class OStream : IosBase
    {
        protected Stream? s;
        protected MemoryStream? mem;
        public OStream() { }
        public OStream(Stream s) { this.s = s; }

        protected virtual void Raw(byte[] b, int off, int n) { (s ?? (Stream?)mem)?.Write(b, off, n); }
        void Pad(byte[] body)
        {
            long w = widthv;
            widthv = 0;
            if (body.Length >= w) { Raw(body, 0, body.Length); return; }
            var pad = new byte[w - body.Length];
            for (int i = 0; i < pad.Length; i++) pad[i] = (byte)fillc;
            if ((flagsv & Ios.left) != 0) { Raw(body, 0, body.Length); Raw(pad, 0, pad.Length); }
            else { Raw(pad, 0, pad.Length); Raw(body, 0, body.Length); }
        }
        static byte[] B(string x) => Encoding.Latin1.GetBytes(x);

        public OStream Put(Str x) { Pad(x.Bytes()); return this; }
        public OStream Put(CPtr x) { Pad(R.Bytes(x)); return this; }
        public OStream Put(sbyte c) { Pad(new[] { (byte)c }); return this; }
        public OStream Put(byte c) { Pad(new[] { c }); return this; }
        public OStream Put(int v) { Pad(B(v.ToString(System.Globalization.CultureInfo.InvariantCulture))); return this; }
        public OStream Put(long v) { Pad(B(v.ToString(System.Globalization.CultureInfo.InvariantCulture))); return this; }
        public OStream Put(uint v) { Pad(B(v.ToString(System.Globalization.CultureInfo.InvariantCulture))); return this; }
        public OStream Put(ulong v) { Pad(B(v.ToString(System.Globalization.CultureInfo.InvariantCulture))); return this; }
        public OStream Put(short v) => Put((int)v);
        public OStream Put(bool v) { Pad(B((flagsv & Ios.boolalpha) != 0 ? (v ? "true" : "false") : (v ? "1" : "0"))); return this; }
        public OStream Put(float v) => Put((double)v);
        public OStream Put(double v)
        {
            // libstdc++ __num_base::_S_format_float: the conversion letter from the floatfield, '#' for showpoint
            bool fx = (flagsv & Ios.@fixed) != 0, sci = (flagsv & Ios.scientific) != 0;
            string spec = (flagsv & Ios.showpoint) != 0 ? "%#" : "%";
            char conv = fx && sci ? 'a' : fx ? 'f' : sci ? 'e' : 'g';
            byte[] body = fx && sci ? R.Format(R.Lit(spec + "a"), new object[] { v })
                                    : R.Format(R.Lit(spec + ".*" + conv), new object[] { (int)prec, v });
            Pad(body);
            return this;
        }
        public OStream Put(SetPrec p) { prec = p.n; return this; }
        public OStream Put(SetW w) { widthv = w.n; return this; }
        public OStream Put(SetFill f) { fillc = f.c; return this; }
        public OStream Put(OStream o) => this;
        public OStream Manip_endl() { Raw(new byte[] { 10 }, 0, 1); flush(); return this; }
        public OStream Manip_flush() { flush(); return this; }
        public OStream Manip_setprecision(long n) { prec = n; return this; }
        public OStream Manip_setw(long n) { widthv = n; return this; }
        public OStream Manip_setfill(int c) { fillc = c; return this; }
        // std::quoted(s): s between double quotes, with every '"' and '\\' escaped by a backslash; the width
        // applies to the whole quoted string, as in libstdc++ (__ostream_insert of the built string)
        public OStream Manip_quoted(Str x)
        {
            var b = new System.Collections.Generic.List<byte> { (byte)'"' };
            foreach (byte c in x.Bytes()) { if (c == '"' || c == '\\') b.Add((byte)'\\'); b.Add(c); }
            b.Add((byte)'"');
            Pad(b.ToArray());
            return this;
        }
        public OStream Manip_quoted(CPtr x) => Manip_quoted(new Str(x));
        public OStream Manip_fixed() { flagsv = (flagsv & ~(Ios.@fixed | Ios.scientific)) | Ios.@fixed; return this; }
        public OStream Manip_scientific() { flagsv = (flagsv & ~(Ios.@fixed | Ios.scientific)) | Ios.scientific; return this; }
        public OStream Manip_defaultfloat() { flagsv &= ~(Ios.@fixed | Ios.scientific); return this; }
        public OStream Manip_left() { flagsv = (flagsv & ~(Ios.left | Ios.right)) | Ios.left; return this; }
        public OStream Manip_right() { flagsv = (flagsv & ~(Ios.left | Ios.right)) | Ios.right; return this; }
        public OStream Manip_boolalpha() { flagsv |= Ios.boolalpha; return this; }
        public OStream Manip_noboolalpha() { flagsv &= ~Ios.boolalpha; return this; }
        public OStream Manip_showpoint() { flagsv |= Ios.showpoint; return this; }
        public OStream Manip_noshowpoint() { flagsv &= ~Ios.showpoint; return this; }
        public OStream write(CPtr p, long n) { if (n != 0) Raw(p.B, p.O, (int)n); return this; }
        public OStream write<T>(Ptr<T> p, long n) where T : struct { var b = Mem.BytesOf(p, (int)n); Raw(b, 0, b.Length); return this; }
        public virtual OStream flush() { s?.Flush(); return this; }
        public virtual long tellp() => s != null ? s.Position : (mem?.Position ?? 0);
        public OStream seekp(long pos, int dir = Ios.beg) { var st = s ?? (Stream?)mem; st?.Seek(pos, dir == Ios.beg ? SeekOrigin.Begin : dir == Ios.cur ? SeekOrigin.Current : SeekOrigin.End); return this; }
    }

    public class OStringStream : OStream
    {
        public OStringStream() { mem = new MemoryStream(); }
        public OStringStream(Str init) { mem = new MemoryStream(); var b = init.Bytes(); mem.Write(b, 0, b.Length); mem.Position = 0; }
        public OStringStream(int mode) : this() { }
        public Str str() => Str.FromBytes(mem!.ToArray());
        public void str(Str x) { mem = new MemoryStream(); var b = x.Bytes(); mem.Write(b, 0, b.Length); }
        public void MoveFrom(OStringStream o) { mem = o.mem; prec = o.prec; flagsv = o.flagsv; o.mem = new MemoryStream(); }
    }

    public class OFStream : OStream, IDisposable
    {
        public OFStream() { }
        public OFStream(Str path, int mode = Ios.@out) { open(path, mode); }
        public OFStream(CPtr path, int mode = Ios.@out) { open(new Str(path), mode); }
        public OFStream(FsPath path, int mode = Ios.@out) { open(path.p, mode); }
        public void open(Str path, int mode = Ios.@out)
        {
            try
            {
                var fm = (mode & Ios.app) != 0 ? FileMode.Append : FileMode.Create;
                s = new FileStream(path.ToString(), fm, FileAccess.Write, FileShare.ReadWrite, 1 << 16);
                failbit = false;
            }
            catch (Exception) { s = null; failbit = true; }
        }
        public void open(FsPath path, int mode = Ios.@out) => open(path.p, mode);
        public bool is_open() => s != null;
        public void close() { s?.Flush(); s?.Dispose(); s = null; }
        public void Dispose() => close();     // ~basic_ofstream closes the file
        public void MoveFrom(OFStream o) { s = o.s; o.s = null; }
    }

    public class IStream : IosBase
    {
        protected byte[] buf = Array.Empty<byte>();
        protected int pos, len;
        protected Stream? src;
        public long gcountv;
        /// <summary>An ifstream opened on a directory: libstdc++ opens it (open(2) of a directory for reading
        /// succeeds) and the first read fails with EISDIR, which basic_filebuf::underflow throws as ios_base::failure
        /// and the extraction catches: badbit and failbit, not eofbit.</summary>
        protected bool dir;

        protected bool Fill()
        {
            if (pos < len) return true;
            if (dir) { badbit = true; failbit = true; return false; }
            if (src == null) return false;
            if (buf.Length == 0) buf = new byte[1 << 16];
            len = src.Read(buf, 0, buf.Length);
            pos = 0;
            return len > 0;
        }
        /// <summary>A read found no more input: the end of the input sets eofbit; a failed read (dir) set badbit instead.</summary>
        protected void AtEnd() { if (!dir) eofbit = true; }
        public int peek() { if (!Fill()) { AtEnd(); return -1; } return buf[pos]; }
        public int get() { if (!Fill()) { AtEnd(); failbit = true; return -1; } gcountv = 1; return buf[pos++]; }
        public IStream get(ref sbyte c) { int x = get(); if (x >= 0) c = (sbyte)x; return this; }
        public IStream ignore(long n = 1, int delim = -1) { for (long i = 0; i < n; i++) { int c = get(); if (c < 0 || c == delim) break; } failbit = failbit && !eofbit; return this; }
        public long gcount() => gcountv;

        public IStream read(CPtr p, long n)
        {
            long got = 0;
            while (got < n) { if (!Fill()) break; int k = (int)Math.Min(n - got, len - pos); Array.Copy(buf, pos, p.B, p.O + got, k); pos += k; got += k; }
            gcountv = got;
            if (got < n) { AtEnd(); failbit = true; }
            return this;
        }
        public IStream read<T>(Ptr<T> p, long n) where T : struct
        {
            var tmp = new byte[n];
            read(new CPtr(tmp, 0), n);
            Mem.BytesInto(p, tmp, 0, (int)gcountv);
            return this;
        }

        public bool ReadLine(Str s, int delim)
        {
            s.clear();
            bool any = false;
            while (true)
            {
                if (!Fill()) { AtEnd(); if (!any) failbit = true; return any; }
                int c = buf[pos++];
                any = true;
                if (c == delim) return true;
                s.push_back(c);
            }
        }

        void SkipWs() { while (Fill() && (buf[pos] == ' ' || buf[pos] == '\t' || buf[pos] == '\n' || buf[pos] == '\r' || buf[pos] == '\v' || buf[pos] == '\f')) pos++; }
        Str Token()
        {
            SkipWs();
            var t = new Str();
            while (Fill() && !(buf[pos] == ' ' || buf[pos] == '\t' || buf[pos] == '\n' || buf[pos] == '\r' || buf[pos] == '\v' || buf[pos] == '\f')) t.push_back(buf[pos++]);
            if (t.n == 0) { failbit = true; if (!Fill()) AtEnd(); }
            else if (!Fill()) AtEnd();
            return t;
        }
        public IStream Get(Str s) { var t = Token(); if (t.n > 0) s.Assign(t); return this; }
        /// <summary>is &gt;&gt; std::ws: skip whitespace (eof is set if the input ends there, failbit is not).</summary>
        public IStream Manip_ws() { while (true) { if (!Fill()) { AtEnd(); break; } int c = buf[pos]; if (c == ' ' || (c >= 9 && c <= 13)) pos++; else break; } return this; }
        /// <summary>is &gt;&gt; v for a double, as libstdc++ does it: the sentry skips white space (at the end of the
        /// input: eof and fail, v unchanged); num_get's _M_extract_float takes the characters a C-locale number can
        /// have (a sign, one of any leading zeros, digits, one '.', and an 'e' after a digit with its sign), stopping
        /// at the first other one, which stays in the stream ("0x10" is read as 0, then x10 is left); and the gnu
        /// __convert_to_v converts them with strtod: not all of them converted (nothing, "1e", "inf") is failbit
        /// with v = 0, and an overflow (strtod's +-inf) is failbit with v = +-DBL_MAX. An underflow is no error.
        /// eofbit when the number ends the input.</summary>
        public IStream Get(ref double v)
        {
            if (!good()) { failbit = true; return this; }     // the sentry: a stream not good extracts nothing
            SkipWs();
            if (!Fill()) { AtEnd(); failbit = true; return this; }
            var acc = new List<byte>(32);
            bool more = true;
            int c = buf[pos];
            void Next() { pos++; more = Fill(); c = more ? buf[pos] : -1; }
            if (c == '+' || c == '-') { acc.Add((byte)c); Next(); }
            bool mantissa = false, dec = false, sci = false;
            while (more && c == '0') { if (!mantissa) { acc.Add((byte)'0'); mantissa = true; } Next(); }
            while (more)
            {
                if (c >= '0' && c <= '9') { acc.Add((byte)c); mantissa = true; }
                else if (c == '.' && !dec && !sci) { acc.Add((byte)'.'); dec = true; }
                else if ((c == 'e' || c == 'E') && !sci && mantissa)
                {
                    acc.Add((byte)'e'); sci = true;
                    Next();
                    if (!more) break;
                    if (c == '+' || c == '-') acc.Add((byte)c);
                    else continue;              // look at this character again
                }
                else break;
                Next();
            }
            if (!more) AtEnd();
            acc.Add(0);
            var a = acc.ToArray();
            int n = R.ScanDouble(a, 0, int.MaxValue, out double r, false);
            if (n == 0 || a[n] != 0) { v = 0; failbit = true; }
            else if (double.IsPositiveInfinity(r)) { v = double.MaxValue; failbit = true; }
            else if (double.IsNegativeInfinity(r)) { v = -double.MaxValue; failbit = true; }
            else v = r;
            return this;
        }
        public IStream Get(ref float v) { double d = 0; Get(ref d); v = (float)d; return this; }
        public IStream Get(ref int v)
        {
            var t = Token(); if (t.n == 0) { v = 0; return this; }
            var p = t.c_str(); var end = p;
            long r = R.strtol(p, ref end, 10);
            if (end.O != t.n || r < int.MinValue || r > int.MaxValue) { failbit = true; v = r < int.MinValue ? int.MinValue : r > int.MaxValue ? int.MaxValue : 0; return this; }
            v = (int)r; return this;
        }
        public IStream Get(ref long v)
        {
            var t = Token(); if (t.n == 0) { v = 0; return this; }
            var p = t.c_str(); var end = p;
            long r = R.strtol(p, ref end, 10);
            if (end.O != t.n) { failbit = true; v = 0; return this; }
            v = r; return this;
        }
        public IStream Get(ref ulong v) { long x = 0; Get(ref x); v = (ulong)x; return this; }
        public IStream Get(ref uint v) { long x = 0; Get(ref x); v = (uint)x; return this; }
        public IStream Get(ref sbyte c) { SkipWs(); int x = get(); if (x >= 0) c = (sbyte)x; return this; }
        public long tellg() => src != null ? src.Position - (len - pos) : pos;
        public IStream seekg(long off, int dir = Ios.beg)
        {
            eofbit = false;
            if (src != null)
            {
                // ios::cur is relative to the read position, not to the stream's, which is past the buffered block
                long np = dir == Ios.beg ? off : dir == Ios.cur ? tellg() + off : src.Length + off;
                if (np < 0) { failbit = true; return this; }
                src.Seek(np, SeekOrigin.Begin); pos = len = 0;
            }
            else { pos = (int)(dir == Ios.beg ? off : dir == Ios.cur ? pos + off : len + off); }
            return this;
        }
    }

    public class IStringStream : IStream
    {
        public IStringStream() { }
        public IStringStream(Str s) { str(s); }
        public IStringStream(Str s, int mode) { str(s); }
        public void str(Str s) { buf = s.Bytes(); pos = 0; len = buf.Length; failbit = eofbit = false; }
        public Str str() => new Str(buf, 0, len);
        public void MoveFrom(IStringStream o) { buf = o.buf; pos = o.pos; len = o.len; }
    }

    public class IFStream : IStream, IDisposable
    {
        public IFStream() { }
        public IFStream(Str path, int mode = Ios.@in) { open(path, mode); }
        public IFStream(CPtr path, int mode = Ios.@in) { open(new Str(path), mode); }
        public IFStream(FsPath path, int mode = Ios.@in) { open(path.p, mode); }
        public void open(Str path, int mode = Ios.@in)
        {
            dir = false;
            try
            {
                if (Directory.Exists(path.ToString()))
                {
                    // libstdc++ opens a directory for reading; every read then fails (see IStream.dir)
                    src = Stream.Null; dir = true; failbit = false; pos = len = 0;
                    return;
                }
                src = new FileStream(path.ToString(), FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16); failbit = false; pos = len = 0;
            }
            catch (Exception) { src = null; failbit = true; }
        }
        public void open(FsPath path, int mode = Ios.@in) => open(path.p, mode);
        public bool is_open() => src != null;
        public void close() { src?.Dispose(); src = null; dir = false; }
        public void Dispose() => close();
        public void MoveFrom(IFStream o) { src = o.src; buf = o.buf; pos = o.pos; len = o.len; dir = o.dir; o.src = null; o.dir = false; }
    }

    public class FStream : OStream, IDisposable
    {
        public FStream(Str path, int mode) { try { s = new FileStream(path.ToString(), FileMode.OpenOrCreate, FileAccess.ReadWrite); } catch (Exception) { failbit = true; } }
        public bool is_open() => s != null;
        public void close() { s?.Dispose(); s = null; }
        public void Dispose() => close();
    }

    public static class Streams
    {
        public static readonly OStream cout = new OStream(Console.OpenStandardOutput());
        public static readonly OStream cerr = new OStream(Console.OpenStandardError());
        public static readonly OStream clog = cerr;
        public static IStream getline(IStream s, Str line) { s.ReadLine(line, '\n'); return s; }
        public static IStream getline(IStream s, Str line, sbyte delim) { s.ReadLine(line, (byte)delim); return s; }
        public static IStream getline(IStream s, Str line, int delim) { s.ReadLine(line, (byte)delim); return s; }
        public static SetPrec setprecision(int n) => new SetPrec(n);
        public static SetW setw(int n) => new SetW(n);
        public static SetFill setfill(int c) => new SetFill(c);
    }
}
