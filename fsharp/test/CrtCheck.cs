// CrtCheck.cs - tests/crtcheck/javacheck/CrtCheck.java in C#, for the .NET runtimes of the SWMM6 ports: dotnet
// crtcheck.dll libm N | printf | scanf | misc | units, reading what the C program of the same name (tests/crtcheck:
// libm.c, printf.c, scanf.c, misc.cpp) wrote on stdin and reporting where the runtime differs from glibc. The case
// lines and their checks are the Java program's, one for one; only the calls are the .NET runtime's (R.snprintf,
// R.sscanf with RefI/RefL/RefF/RefD targets, Std.from_chars with ref, R.strtol(s, ref end, b), ...).
// Built twice by run_runtime_checks.sh: with -define:FSRT against the F# runtime (fsharp/runtime, as one assembly)
// and without it against the C# runtime's sources (cs/runtime), the control. The libm functions are the ones each
// engine calls: the F# engine Cpp.LibM (glibc by P/Invoke) for everything but sqrt, floor, ceil and trunc; the C#
// engine .NET's Math and its own LibM class.
// The units section keeps the Java checks whose calls exist in the .NET runtimes (strtod details, binary files,
// sscanf targets, conversions and the string functions); the Java-only ones (boxing, R.Back, tuples, atomics) are left
// out.
// Written by Claude (Anthropic) for Robert Dickinson's SWMM6 ports; see fsharp/README.md.
using System;
using System.IO;
using System.Text;
using Cpp;

public static class CrtCheck
{
    public static int Main(string[] args)
    {
        switch (args[0])
        {
            case "libm": Libm(int.Parse(args[1])); break;
            case "printf": Lines("printf", PrintfCase); break;
            case "scanf": Lines("scanf", ScanfCase); break;
            case "misc": Lines("misc", MiscCase); break;
            case "units": Units(); break;
            default: throw new ArgumentException(args[0]);
        }
        R.FlushAll();
        Console.Out.Flush();
        return 0;
    }

    static readonly Encoding L1 = Encoding.Latin1;

    /// <summary>With CRT_DUMP set, every case's result is written to that file, one line per case, so that two
    /// runtimes can be compared case by case (the C# runtime differs from glibc on a few kinds of case).</summary>
    static void Lines(string what, Func<string[], string[]> check)
    {
        var inp = new StreamReader(Console.OpenStandardInput(), L1, false, 1 << 20);
        string dumpName = Environment.GetEnvironmentVariable("CRT_DUMP");
        StreamWriter dump = string.IsNullOrEmpty(dumpName) ? null : new StreamWriter(dumpName, false, L1);
        int n = 0, bad = 0;
        for (string line; (line = inp.ReadLine()) != null;)
        {
            string[] f = line.Split('\t');
            n++;
            string[] r = check(f);
            dump?.WriteLine(r[1]);
            if (r[0] != r[1])
            {
                bad++;
                if (bad <= 15)
                {
                    string label = f[0];
                    try { label = L1.GetString(Unhex(f[0])); } catch (Exception) { }
                    Console.WriteLine("MISMATCH \"" + label + "\"\n  glibc: " + r[0] + "\n  .NET:  " + r[1]);
                }
            }
        }
        dump?.Close();
        Console.WriteLine(what + " cases: " + n + ", mismatches: " + bad);
    }

    static byte[] Unhex(string s)
    {
        if (s == "-") return new byte[0];
        var b = new byte[s.Length / 2];
        for (int i = 0; i < b.Length; i++) b[i] = Convert.ToByte(s.Substring(2 * i, 2), 16);
        return b;
    }
    static string Hex(byte[] b, int off, int n)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < n; i++) sb.Append(b[off + i].ToString("x2"));
        return sb.ToString();
    }
    /// <summary>A C string holding these bytes.</summary>
    static CPtr Lit(byte[] b) { var c = new byte[b.Length + 1]; Array.Copy(b, c, b.Length); return new CPtr(c, 0); }
    static string Bits(double d) => BitConverter.DoubleToInt64Bits(d).ToString("x16");
    static string Bits(float f) => BitConverter.SingleToInt32Bits(f).ToString("x8");

    // ------------------------------------------------------------------------------------------------ printf
    // hex(format) types args size ret hex(output)
    static string[] PrintfCase(string[] f)
    {
        CPtr format = Lit(Unhex(f[0]));
        string[] vals = f[2].Split(',');
        var a = new object[f[1].Length];
        for (int i = 0; i < f[1].Length; i++)
        {
            string v = vals[i];
            switch (f[1][i])
            {
                case 'd': a[i] = BitConverter.Int64BitsToDouble(unchecked((long)Convert.ToUInt64(v, 16))); break;
                case 'i': a[i] = unchecked((int)Convert.ToUInt64(v, 16)); break;
                case 'l': a[i] = unchecked((long)Convert.ToUInt64(v, 16)); break;
                case 's': a[i] = Lit(Unhex(v)); break;
                case 'S': a[i] = CPtr.Null; break;
            }
        }
        long size = long.Parse(f[3]);
        CPtr buf = R.Buf(1024);
        Array.Fill(buf.B, (byte)0x55);
        buf.B[0] = 0;
        int r = R.snprintf(buf, (ulong)size, format, a);
        string o = "-";
        if (size > 0)
        {
            int n = 0;
            while (buf.B[n] != 0) n++;
            if (n > 0) o = Hex(buf.B, 0, n);
        }
        return new[] { f[4] + "\t" + f[5], r + "\t" + o };
    }

    // ------------------------------------------------------------------------------------------------ scanf
    // hex(format) hex(input) types ret values; the targets are the kinds the engine passes
    static string[] ScanfCase(string[] f)
    {
        CPtr format = Lit(Unhex(f[0])), input = Lit(Unhex(f[1]));
        string types = f[2];
        int m = types.Length;
        var ints = new int[m];
        var longs = new long[m];
        var floats = new float[m];
        var doubles = new double[m];
        var bufs = new CPtr[m];
        var a = new object[m];
        int na = 0;
        for (int k = 0; k < m; k++)
        {
            int kk = k;
            ints[k] = 0x55555555;
            longs[k] = 0x5555555555555555L;
            floats[k] = BitConverter.Int32BitsToSingle(0x55555555);
            doubles[k] = BitConverter.Int64BitsToDouble(0x5555555555555555L);
            switch (types[k])
            {
                case 'i': case 'n': a[na++] = new RefI { Set = v => ints[kk] = v }; break;
                case 'u': a[na++] = new RefL { Set = v => ints[kk] = unchecked((int)v) }; break;   // a long setter, kept as unsigned int
                case 'l': a[na++] = new RefL { Set = v => longs[kk] = v }; break;
                case 'f': a[na++] = new RefF { Set = v => floats[kk] = v }; break;
                case 'd': a[na++] = new RefD { Set = v => doubles[kk] = v }; break;
                case 'k': ints[k] = 0x55; a[na++] = new RefI { Set = v => ints[kk] = unchecked((sbyte)v) }; break;   // a char
                case 's': case 'c':
                    bufs[k] = R.Buf(64);
                    Array.Fill(bufs[k].B, (byte)0x55);
                    a[na++] = bufs[k];
                    break;
            }
        }
        var used = new object[na];
        Array.Copy(a, used, na);
        int r = R.sscanf(input, format, used);
        var sb = new StringBuilder();
        for (int k = 0; k < m; k++)
        {
            switch (types[k])
            {
                case 'i': case 'n': case 'u': case 'k': sb.Append(ints[k]); break;
                case 'l': sb.Append(longs[k]); break;
                case 'f': sb.Append(Bits(floats[k])); break;
                case 'd': sb.Append(Bits(doubles[k])); break;
                case 's': case 'c': sb.Append(Hex(bufs[k].B, 0, 12)); break;
            }
            sb.Append(' ');
        }
        string want = f[3] + "\t" + (f.Length > 4 ? f[4] : "");
        return new[] { want, r + "\t" + sb };
    }

    // ------------------------------------------------------------------------------------------------ misc
    // kind hex(input) result
    static string[] MiscCase(string[] f)
    {
        byte[] inb = Unhex(f[1]);
        CPtr s = Lit(inb);
        CPtr e = s + inb.Length;
        string got;
        switch (f[0])
        {
            case "fci": { int v = 0x55555555; var r = Std.from_chars(s, e, ref v); got = r.ec + " " + CPtr.Diff(r.ptr, s) + " " + v; break; }
            case "fcu": { uint v = 0x55555555; var r = Std.from_chars(s, e, ref v); got = r.ec + " " + CPtr.Diff(r.ptr, s) + " " + v; break; }
            case "fcl": { long v = 0x5555555555555555L; var r = Std.from_chars(s, e, ref v); got = r.ec + " " + CPtr.Diff(r.ptr, s) + " " + v; break; }
            case "fcd": { double v = 0.25; var r = Std.from_chars(s, e, ref v); got = r.ec + " " + CPtr.Diff(r.ptr, s) + " " + Bits(v); break; }
            case "strtol": { CPtr end = s; long v = R.strtol(s, ref end, 10); got = v + " " + CPtr.Diff(end, s); break; }
            case "atoi": got = R.atoi(s).ToString(); break;
            case "atol": got = R.atol(s).ToString(); break;
            case "strtod": { CPtr end = s; double v = R.strtod(s, ref end); got = Bits(v) + " " + CPtr.Diff(end, s); break; }
            case "ctime":
                {
                    long t = long.Parse(L1.GetString(inb));
                    CPtr p = R.ctime(t);
                    got = Hex(p.B, p.O, (int)R.strlen(p));
                    break;
                }
            case "nprocs": got = Sys.hardware_concurrency().ToString(); break;
            case "ctype":
                {
                    int c = int.Parse(L1.GetString(inb));
                    got = "" + R.isdigit(c) + R.isalpha(c) + R.isalnum(c) + R.isspace(c) + R.isupper(c) + R.islower(c) + R.ispunct(c)
                        + R.isprint(c) + R.isxdigit(c) + " " + R.toupper(c) + " " + R.tolower(c);
                    break;
                }
            default: throw new ArgumentException(f[0]);
        }
        if (got != f[2]) got = got + "  (" + f[0] + " \"" + L1.GetString(inb) + "\")";
        return new[] { f[2], got };
    }

    // ------------------------------------------------------------------------------------------------ libm
    static readonly string[] FN = { "atan2", "cbrt", "cosh", "sinh", "tanh", "log2", "expm1", "log1p", "hypot", "fmod", "remainder",
        "round", "nextafter", "exp", "log", "pow", "sin", "cos", "tan", "asin", "acos", "atan", "log10", "floor", "ceil", "trunc", "sqrt" };
#if FSRT
    // the F# engine: tools/cs2x/FsFn.cs LibmCalls sends Math.Exp, Pow, Sin, ... to Cpp.LibM (fsharp/runtime/LibM.fs)
    static readonly Func<double, double, double>[] F = {
        (x, y) => LibM.atan2(x, y), (x, y) => LibM.cbrt(x), (x, y) => LibM.cosh(x), (x, y) => LibM.sinh(x),
        (x, y) => LibM.tanh(x), (x, y) => LibM.log2(x), (x, y) => LibM.expm1(x), (x, y) => LibM.log1p(x),
        (x, y) => LibM.hypot(x, y), (x, y) => LibM.fmod(x, y), (x, y) => LibM.remainder(x, y), (x, y) => LibM.round(x),
        (x, y) => LibM.nextafter(x, y),
        (x, y) => LibM.exp(x), (x, y) => LibM.log(x), (x, y) => LibM.pow(x, y), (x, y) => LibM.sin(x),
        (x, y) => LibM.cos(x), (x, y) => LibM.tan(x), (x, y) => LibM.asin(x), (x, y) => LibM.acos(x),
        (x, y) => LibM.atan(x), (x, y) => LibM.log10(x), (x, y) => Math.Floor(x), (x, y) => Math.Ceiling(x),
        (x, y) => Math.Truncate(x), (x, y) => Math.Sqrt(x) };
#else
    // the C# engine: .NET's Math, and cs/runtime's LibM for the functions .NET lacks or rounds differently
    static readonly Func<double, double, double>[] F = {
        (x, y) => Math.Atan2(x, y), (x, y) => Math.Cbrt(x), (x, y) => Math.Cosh(x), (x, y) => Math.Sinh(x),
        (x, y) => Math.Tanh(x), (x, y) => Math.Log2(x), (x, y) => LibM.expm1(x), (x, y) => LibM.log1p(x),
        (x, y) => LibM.hypot(x, y), (x, y) => LibM.fmod(x, y), (x, y) => LibM.remainder(x, y), (x, y) => LibM.round(x),
        (x, y) => LibM.nextafter(x, y),
        (x, y) => Math.Exp(x), (x, y) => Math.Log(x), (x, y) => Math.Pow(x, y), (x, y) => Math.Sin(x),
        (x, y) => Math.Cos(x), (x, y) => Math.Tan(x), (x, y) => Math.Asin(x), (x, y) => Math.Acos(x),
        (x, y) => Math.Atan(x), (x, y) => Math.Log10(x), (x, y) => Math.Floor(x), (x, y) => Math.Ceiling(x),
        (x, y) => Math.Truncate(x), (x, y) => Math.Sqrt(x) };
#endif
    const int NSPEC = 24;

    /// <summary>The distance between two doubles in units in the last place (across zero too).</summary>
    static ulong Ulps(double a, double b)
    {
        long ia = BitConverter.DoubleToInt64Bits(a), ib = BitConverter.DoubleToInt64Bits(b);
        if (ia < 0) ia = unchecked(long.MinValue - ia);
        if (ib < 0) ib = unchecked(long.MinValue - ib);
        return unchecked((ulong)(ia > ib ? ia - ib : ib - ia));
    }

    static string D(double d) => double.IsNaN(d) ? "NaN" : d.ToString("R", System.Globalization.CultureInfo.InvariantCulture);

    static void Libm(int n)
    {
        var inp = new BinaryReader(new BufferedStream(Console.OpenStandardInput(), 1 << 20));
        int per = n + NSPEC * NSPEC;
        Console.WriteLine(string.Format("{0,-10} {1,8} {2,10} {3,9} {4,8}  {5}", "function", "cases", "mismatch", "NaN-sign", "max ulp", "first mismatch (x, y: glibc vs .NET)"));
        long total = 0;
        for (int fi = 0; fi < FN.Length; fi++)
        {
            int bad = 0, nanSign = 0;
            ulong maxU = 0;
            bool nanNum = false;
            string first = "";
            for (int i = 0; i < per; i++)
            {
                double x, y;
                long want;
                try
                {
                    x = inp.ReadDouble();
                    y = inp.ReadDouble();
                    want = inp.ReadInt64();
                }
                catch (EndOfStreamException)
                {
                    Console.Error.WriteLine("short input");
                    Environment.Exit(2);
                    return;
                }
                long got = BitConverter.DoubleToInt64Bits(F[fi](x, y));
                if (got == want) continue;
                bad++;
                double w = BitConverter.Int64BitsToDouble(want), g = BitConverter.Int64BitsToDouble(got);
                string desc = D(x) + ", " + D(y) + ": " + D(w) + " vs " + D(g);
                if (double.IsNaN(w) && double.IsNaN(g)) nanSign++;
                else if (!double.IsNaN(w) && !double.IsNaN(g)) { ulong d = Ulps(w, g); if (d > maxU) maxU = d; }
                else nanNum = true;
                if (first.Length == 0) first = desc;
            }
            total += bad;
            Console.WriteLine(string.Format("{0,-10} {1,8} {2,10} {3,9} {4,8}  {5}", FN[fi], per, bad, nanSign, nanNum ? "NaN/num" : maxU.ToString(), first));
        }
        Console.WriteLine("total mismatches: " + total);
    }

    // ------------------------------------------------------------------------------------------------ units
    static int un, ubad;
    static void Check(string name, bool ok, params object[] detail)
    {
        un++;
        if (!ok) { ubad++; Console.WriteLine("FAIL " + name + " [" + string.Join(", ", detail) + "]"); }
    }

    static void Units()
    {
        // sscanf into the targets the engine passes
        {
            CPtr s1 = R.Buf(80);
            int r = R.sscanf(R.Lit("  SWMM5 x"), R.Lit("%s"), s1);
            Check("%s CPtr", r == 1 && R.strcmp(s1, R.Lit("SWMM5")) == 0, r);
            int ci = 0; double cd = 0;
            r = R.sscanf(R.Lit("7 0.5"), R.Lit("%d %lf"), new RefI { Set = v => ci = v }, new RefD { Set = v => cd = v });
            Check("Ref cells as targets", r == 2 && ci == 7 && cd == 0.5, r, ci, cd);
            float x = 99.99f; int c = 'q';
            r = R.sscanf(R.Lit("1.5 X"), R.Lit("%f %c"), new RefF { Set = v => x = v }, new RefI { Set = v => c = v });
            Check("RefF + %c", r == 2 && x == 1.5f && c == 'X', r, x, c);
            long l = -9;
            r = R.sscanf(R.Lit("77"), R.Lit("%ld %c"), new RefL { Set = v => l = v }, new RefI { Set = v => c = v });
            Check("partial", r == 1 && l == 77 && c == 'X', r, l, c);
            var pb = new Ptr<byte>(new byte[16], 2L);
            r = R.sscanf(R.Lit("abc"), R.Lit("%s"), (CPtr)pb);
            Check("%s into a char array pointer", r == 1 && pb.a[2] == 'a' && pb.a[4] == 'c' && pb.a[5] == 0, r);
        }

        // strtod details: hex floats, NaN payloads, the end pointer
        {
            CPtr end = CPtr.Null;
            CPtr s = R.Lit("  0x1.8p1rest");
            Check("strtod hex", R.strtod(s, ref end) == 3.0 && CPtr.Diff(end, s) == 9);
            Check("strtod nan payload", BitConverter.DoubleToInt64Bits(R.strtod(R.Lit("nan(0x5)"))) == 0x7ff8000000000005L);
            s = R.Lit("abc");
            Check("strtod no number", R.strtod(s, ref end) == 0.0 && end == s);
            Check("strtod huge exponent", R.strtod(R.Lit("1e99999999999")) == double.PositiveInfinity
                && R.strtod(R.Lit("1e-99999999999")) == 0.0);
        }

        // binary files
        {
            string dir = Path.Combine(Path.GetTempPath(), "crtcheck_" + Environment.ProcessId);
            Directory.CreateDirectory(dir);
            try
            {
                CPtr name = R.Lit(Path.Combine(dir, "t.bin"));
                CFile fp = R.fopen(name, R.Lit("w+b"));
                Mem.fwrite(R.Lit("STAMP"), 1UL, 5UL, fp);
                R.fwriteI1(42, fp);
                long pos = R.ftell(fp);
                R.fwriteD1(3.5, fp);
                R.fwriteF1(0.25f, fp);
                Mem.fwrite(new Ptr<int>(new[] { 7, -8, 9 }, 0L), 4UL, 3UL, fp);
                R.fseek(fp, pos, 0);
                R.fwriteD1(4.5, fp);                          // overwrite in place
                R.rewind(fp);
                CPtr stamp = R.BufOf(16, new int[0]);
                ulong got = Mem.fread(stamp, 1UL, 5UL, fp);
                int i = 0; double d = 0; float fl = 0;
                bool ok = got == 5 && R.strncmp(stamp, R.Lit("STAMP"), 5UL) == 0 && R.freadI1(ref i, fp) == 1 && i == 42
                    && R.freadD1(ref d, fp) == 1 && d == 4.5 && R.freadF1(ref fl, fp) == 1 && fl == 0.25f;
                Check("binary round trip", ok, got, i, d, fl);
                var ints = new int[4];
                ulong k = Mem.fread(new Ptr<int>(ints, 0L), 4UL, 4UL, fp);
                Check("short fread", k == 3 && ints[0] == 7 && ints[1] == -8 && ints[2] == 9 && R.feof(fp) == 1, k, ints[0], ints[1], ints[2]);
                Check("fgetc at end", R.fgetc(fp) == -1);
                Check("ftell at end", R.ftell(fp) == 5 + 4 + 8 + 4 + 12);
                Check("fseek negative", R.fseek(fp, -1L, 0) == -1);
                Check("fseek end", R.fseek(fp, -4L, 2) == 0 && R.ftell(fp) == 29 && R.feof(fp) == 0);
                int last = 0;
                Check("fread after seek", R.freadI1(ref last, fp) == 1 && last == 9);
                Check("fclose", R.fclose(fp) == 0);
                fp = R.fopen(name, R.Lit("r"));
                CPtr line = R.Buf(8);
                Check("fgets", !R.fgets(line, 4, fp).IsNull && R.strcmp(line, R.Lit("STA")) == 0);
                var part = new[] { 0x11111111, 0x22222222 };
                R.fseek(fp, -6L, 2);
                ulong pk = Mem.fread(new Ptr<int>(part, 0L), 4UL, 2UL, fp);
                // the last 6 bytes are the top two of -8 and all of 9: ff ff 09 00 | 00 00
                Check("fread partial element", pk == 1 && part[0] == 0x0009FFFF && part[1] == 0x22220000 && R.feof(fp) == 1,
                    pk, part[0].ToString("x"), part[1].ToString("x"));
                R.fclose(fp);
                Check("fopen missing", R.fopen(R.Lit(Path.Combine(dir, "none")), R.Lit("r")) == null);
                // glibc: a directory opens for reading, and the first read fails (ferror set, feof not); r+ and w fail
                CFile dfp = R.fopen(R.Lit(dir), R.Lit("r"));
                Check("fopen directory", dfp != null && R.fgets(R.Buf(8), 8, dfp).IsNull && R.feof(dfp) == 0
                    && R.ferror(dfp) == 1 && R.fgetc(dfp) == -1 && R.feof(dfp) == 0 && R.fclose(dfp) == 0
                    && R.fopen(R.Lit(dir), R.Lit("r+")) == null && R.fopen(R.Lit(dir), R.Lit("w")) == null);
                var nf = R.fopen(name, R.Lit("r"));
                Check("ferror of a file", R.ferror(nf) == 0);
                R.fclose(nf);
                Check("fopen bad mode", R.fopen(name, R.Lit("x")) == null);
                // append mode: writes go at the end, no seeking before the old end
                CPtr an = R.Lit(Path.Combine(dir, "a.txt"));
                fp = R.fopen(an, R.Lit("w"));
                R.fprintf(fp, R.Lit("one\n"));
                R.fclose(fp);
                fp = R.fopen(an, R.Lit("a"));
                Check("append position", R.ftell(fp) == 4);
                Check("append no seek back", R.fseek(fp, 0L, 0) == -1);
                R.fprintf(fp, R.Lit("two\n"));
                R.fclose(fp);
                Check("append content", L1.GetString(File.ReadAllBytes(Path.Combine(dir, "a.txt"))) == "one\ntwo\n");
                // fprintf of the argument types the engine passes
                fp = R.fopen(R.Lit(Path.Combine(dir, "t.txt")), R.Lit("w"));
                R.fprintf(fp, R.Lit("%-6s|%3d|%5.2f|%zu|%d|%u|%ld|%x|%s\n"), R.Lit("ab"), -5, 1.25, ulong.MaxValue, 7, uint.MaxValue, -3L, -1, R.Lit("z"));
                R.FlushAll();
                R.fclose(fp);
                string txt = L1.GetString(File.ReadAllBytes(Path.Combine(dir, "t.txt")));
                Check("fprintf types", txt == "ab    | -5| 1.25|18446744073709551615|7|4294967295|-3|ffffffff|z\n", txt);
                Check("fprintf null file", R.fprintf(null, R.Lit("x")) == -1);
                CPtr sb = R.Buf(8);
                Check("snprintf truncation", R.snprintf(sb, 4UL, R.Lit("%d"), 123456) == 6 && R.strcmp(sb, R.Lit("123")) == 0);
                Check("remove", R.remove(an) == 0 && R.remove(an) == -1);
            }
            finally
            {
                foreach (var p in Directory.GetFiles(dir)) File.Delete(p);
                Directory.Delete(dir);
            }
        }

        // conversions and math corners
        {
            Check("D2I", Conv.D2I(double.NaN) == int.MinValue && Conv.D2I(2147483647.9) == 2147483647 && Conv.D2I(-2147483648.9) == int.MinValue
                && Conv.D2I(2147483648.0) == int.MinValue && Conv.D2I(-3.7) == -3);
            Check("D2L", Conv.D2L(9.3e18) == long.MinValue && Conv.D2L(-9.2e18) == -9200000000000000000L && Conv.D2L(double.NaN) == long.MinValue);
            Check("D2UL", Conv.D2UL(1.8446744073709550e19) == unchecked((ulong)-2048L) && Conv.D2UL(3.5) == 3 && Conv.D2UL(-1.0) == ulong.MaxValue);
            Check("D2SB", Conv.D2SB(200.0) == -56 && Conv.D2SB(1e10) == 0);
            Check("round", LibM.round(2.5) == 3.0 && LibM.round(-2.5) == -3.0 && LibM.round(0.49999999999999994) == 0.0
                && BitConverter.DoubleToInt64Bits(LibM.round(-0.4)) == unchecked((long)0x8000000000000000UL) && LibM.lround(-2.5) == -3);
            Check("fmax/fmin", LibM.fmax(double.NaN, 1.0) == 1.0 && LibM.fmin(2.0, double.NaN) == 2.0 && LibM.fmax(-0.0, 0.0) == 0.0
                && BitConverter.DoubleToInt64Bits(LibM.fmin(0.0, -0.0)) == unchecked((long)0x8000000000000000UL));
            Check("Truncate", Math.Truncate(-2.7) == -2.0 && BitConverter.DoubleToInt64Bits(Math.Truncate(-0.5)) == unchecked((long)0x8000000000000000UL));
            Check("CopySign NaN sign", Math.CopySign(1.0, BitConverter.Int64BitsToDouble(unchecked((long)0xFFF8000000000000UL))) == -1.0);
        }

        // char arrays and strings
        {
            CPtr p = R.LitN("OPENSWMM_HS_V1", 16);
            Check("LitN", p.B.Length == 16 && p.B[14] == 0 && p.B[15] == 0);
            CPtr q = R.CopyBuf(p);
            q.B[0] = (byte)'X';
            Check("CopyBuf", p.B[0] == 'O' && q.O == p.O);
            R.memcpyBuf(q, p);
            Check("memcpyBuf", q.B[0] == 'O');
            CPtr dst = R.BufOf(6, new int[] { 'a', 'b' });
            R.strncpy(dst, R.Lit("xy"), 5UL);
            Check("strncpy pads", dst.B[0] == 'x' && dst.B[1] == 'y' && dst.B[2] == 0 && dst.B[4] == 0);
            Check("strncmp", R.strncmp(R.Lit("FLOWX"), R.Lit("FLOWY"), 4UL) == 0 && R.strncmp(R.Lit("a"), R.Lit("b"), 4UL) < 0);
            Check("memcmp", R.memcmp(R.Lit("SWMM5-HOTSTART4"), R.Lit("SWMM5-HOTSTART3"), 15UL) > 0);
            Check("bufs", R.Bufs(5, 24).Length == 5 && R.Bufs(5, 24)[4].B.Length == 24);
            string path = Environment.GetEnvironmentVariable("PATH");
            Check("getenv", path != null && R.strcmp(R.getenv(R.Lit("PATH")), R.Lit(path)) == 0 && R.getenv(R.Lit("CRTCHECK_NONE_SET")).IsNull);
            Check("strtol0", R.strtol0(R.Lit(" 24h"), 10) == 24 && R.atol(R.Lit("-5")) == -5);
            var tp = new Ptr<long>(new long[1], 0L);
            long t = R.time(tp);
            Check("time", t > 1700000000L && tp.a[0] == t && R.difftime(t + 3, t) == 3.0);
            Check("strcmp unsigned", R.strcmp(new CPtr(new byte[] { 0xE9, 0 }, 0), R.Lit("e")) > 0);
            CPtr hay = R.Lit("a,b,,c");
            Check("strchr", CPtr.Diff(R.strchr(hay, ','), hay) == 1 && CPtr.Diff(R.strchr(hay, 0), hay) == 6 && R.strchr(hay, 'z').IsNull);
            Check("strrchr", CPtr.Diff(R.strrchr(hay, ','), hay) == 4);
            Check("strstr", CPtr.Diff(R.strstr(hay, R.Lit(",,")), hay) == 3 && R.strstr(hay, R.Lit("x")).IsNull && R.strstr(hay, R.Lit("")) == hay);
            Check("strcspn", R.strcspn(hay, R.Lit(",")) == 1 && R.strcspn(R.Lit("abc"), R.Lit("x")) == 3);
            var toks = new StringBuilder();
            for (CPtr tk = R.strtok(hay, R.Lit(",")); !tk.IsNull; tk = R.strtok(CPtr.Null, R.Lit(","))) toks.Append(R.Str(tk)).Append('|');
            Check("strtok", toks.ToString() == "a|b|c|", toks);
            CPtr cat = R.Buf(16);
            R.strcpy(cat, R.Lit("ab"));
            R.strcat(cat, R.Lit("cd"));
            Check("strcpy/strcat", R.Str(cat) == "abcd" && R.strlen(cat) == 4);
            Check("toupper", R.toupper('a') == 'A' && R.toupper('{') == '{');
            Check("atoi wrap", R.atoi(R.Lit("4294967297")) == 1 && R.atoi(R.Lit("99999999999999999999")) == -1);
        }
        Console.WriteLine("units cases: " + un + ", failures: " + ubad);
    }
}
