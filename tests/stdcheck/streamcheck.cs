// streamcheck.cs - the cases of streamcheck.cpp through cs/runtime/Streams.cs (section a must equal the C++ output
// byte for byte), plus section b: the C# port's own behaviour where it is not libstdc++'s (getline after the end,
// token-wise number extraction, app-mode tellp, the filesystem functions over .NET), which the Go port follows,
// and section c (ios::cur on a file; the C# port differs from libstdc++ there).
// Written by Claude (Anthropic); see AI-USE.md.
using System;
using System.Text;
using Cpp;

static class P
{
    static readonly StringBuilder sb = new StringBuilder();
    static double Bits(ulong u) => BitConverter.Int64BitsToDouble(unchecked((long)u));
    static string St(IosBase s) => "" + (s.good() ? 'g' : '-') + (s.eof() ? 'e' : '-') + (s.fail() ? 'f' : '-') + (s.bad() ? 'b' : '-');
    static string G(double v) { var o = new OStringStream(); o.Put(v); return o.str().ToString(); }
    static Str S(string s) => new Str(s);
    static string B(bool b) => b ? "1" : "0";
    static void W(string s) => sb.Append(s);

    static void Main(string[] args)
    {
        string sec = args.Length > 0 ? args[0] : "a";
        if (sec == "a") A();
        else if (sec == "b") Bsec();
        else C();
        var bytes = Encoding.Latin1.GetBytes(sb.ToString());
        Streams.cout.write(new CPtr(bytes, 0), bytes.Length);
    }

    static void C()
    {
        var f = new IFStream(S("build/streamcheck.bin"), Ios.@in | Ios.binary);
        var b = new int[1];
        f.read(new Ptr<int>(b, 0), 4);
        f.seekg(4, Ios.cur);
        f.read(new Ptr<int>(b, 0), 4);
        W("cur at8 " + b[0] + " tellg " + f.tellg() + " " + St(f) + "\n");
    }

    static void A()
    {
        double[] vals = { 0.0, Bits(0x8000000000000000UL), 1.0, -1.0, 0.1, 0.5, 1.5, 2.5, 123.456, 1e-5,
            1.23456789e-7, 1e21, 1.7976931348623157e308, Bits(1), 2.2250738585072014e-308, 1.0 / 3.0, 2.0 / 3.0, 100.0,
            1e15, 123456789012345678.0, 0.000123456, 9.999999, 99.995, 0.125, Bits(0x7ff8000000000000UL),
            Bits(0x7ff0000000000000UL), Bits(0xfff0000000000000UL), 1e100, 5e-310, 3.14159265358979, -0.0004567,
            1234567.0, 0.95, 0.05, 1e-300, 6.02214076e23 };
        int[] precs = { 0, 1, 2, 3, 4, 6, 8, 10, 15, 16, 17, 20, -1 };
        foreach (double v in vals)
        {
            for (int mode = 0; mode < 4; mode++)
                for (int sp = 0; sp < 2; sp++)
                    foreach (int p in precs)
                    {
                        if (mode == 3 && p != 6) continue;
                        var o = new OStringStream();
                        if (mode == 1) o.Manip_fixed();
                        if (mode == 2) o.Manip_scientific();
                        if (mode == 3) o.setf(Ios.@fixed | Ios.scientific, Ios.floatfield);
                        if (sp == 1) o.Manip_showpoint();
                        o.precision(p);
                        o.Put(v);
                        W("d " + mode + sp + " " + p + " [" + o.str() + "]\n");
                    }
            var f = new OStringStream();
            f.Put((float)v);
            W("f [" + f.str() + "]\n");
        }
        {
            var o = new OStringStream();
            o.Put((sbyte)'[').Manip_setw(10).Put(3.5).Put(R.Lit("][")).Manip_left().Manip_setw(8).Manip_setfill('*').Put(-42).Put((sbyte)']');
            o.Put((sbyte)'[').Manip_right().Manip_setw(6).Put(R.Lit("ab")).Put(R.Lit("][")).Manip_setw(2).Put(R.Lit("abcdef")).Put((sbyte)']');
            o.Put((sbyte)'[').Manip_setw(5).Manip_setfill('0').Put(7u).Put(R.Lit("][")).Put(123).Put((sbyte)']');
            o.Manip_boolalpha().Put((sbyte)'[').Put(true).Put((sbyte)' ').Put(false).Put((sbyte)']').Manip_noboolalpha().Put((sbyte)'[').Put(true).Put(false).Put((sbyte)']');
            o.Put((sbyte)'[').Manip_setw(12).Manip_setfill(' ').Manip_quoted(S("a\"b\\c")).Put((sbyte)']');
            o.Put((sbyte)'[').Manip_quoted(R.Lit("plain")).Put((sbyte)']');
            o.Put((sbyte)'[').Manip_fixed().Manip_setprecision(3).Manip_setw(9).Put(2.0 / 3.0).Put((sbyte)']');
            o.Put((sbyte)'[').Manip_defaultfloat().Manip_setprecision(4).Put(2.0 / 3.0).Put((sbyte)']');
            o.Put((sbyte)'[').Put(long.MinValue).Put((sbyte)' ').Put(ulong.MaxValue).Put((sbyte)' ').Put((short)-5).Put((sbyte)']');
            o.Put((sbyte)'[').Put((sbyte)'x').Put((sbyte)65).Put((sbyte)']');
            W("w " + o.str() + "\n");
        }
        {
            var o = new OStringStream(S("abcdef"));
            o.Put(R.Lit("XY"));
            W("init [" + o.str() + "] tellp " + o.tellp() + "\n");
            var q = new OStringStream();
            q.Put(R.Lit("hello world"));
            q.seekp(6);
            q.Put(R.Lit("W"));
            q.seekp(-1, Ios.end);
            q.Put(R.Lit("D"));
            q.seekp(2, Ios.beg);
            q.seekp(1, Ios.cur);
            q.Put(R.Lit("L"));
            W("seekp [" + q.str() + "] tellp " + q.tellp() + "\n");
        }
        {
            var @is = new IStringStream(S("  3.25 -7 abc\nline two\n\nlast"));
            double d = -1; int i = -1; var s = new Str();
            @is.Get(ref d); W("x d " + G(d) + " " + St(@is) + "\n");
            @is.Get(ref i); W("x i " + i + " " + St(@is) + "\n");
            @is.Get(s); W("x s " + s + " " + St(@is) + "\n");
            var line = new Str();
            for (int k = 0; k < 4; k++)
            {
                Streams.getline(@is, line);
                W("x line [" + line + "] " + St(@is) + "\n");
            }
        }
        {
            var @is = new IStringStream(S("12 4294967296 -2147483648 2147483648 -9223372036854775808 9000000000000000000 4294967295 7"));
            int a = -1, b = -1, c = -1, e = -1;
            @is.Get(ref a).Get(ref b);
            W("x ints " + a + " " + b + " " + St(@is) + "\n");
            @is.clear();
            @is.Get(ref c).Get(ref e);
            W("x ints " + c + " " + e + " " + St(@is) + "\n");
            @is.clear();
            long L = -1;
            @is.Get(ref L);
            W("x long " + L + " " + St(@is) + "\n");
            @is.clear();
            ulong U = 0; uint u = 0;
            @is.Get(ref U);
            W("x ulong " + U + " " + St(@is) + "\n");
            @is.clear();
            @is.Get(ref u);
            W("x uint " + u + " " + St(@is) + "\n");
        }
        {
            var @is = new IStringStream(S("a,b,,c"));
            var f = new Str();
            while (Streams.getline(@is, f, (sbyte)',').ok()) W("x field [" + f + "] " + St(@is) + "\n");
            W("x field end " + St(@is) + "\n");
        }
        {
            var @is = new IStringStream(S("   \t\n  tail"));
            @is.Manip_ws();
            var rest = new Str();
            Streams.getline(@is, rest);
            W("x ws [" + rest + "] " + St(@is) + "\n");
            var e = new IStringStream(S("   "));
            e.Manip_ws();
            W("x ws-end " + St(e) + "\n");
        }
        {
            var @is = new IStringStream(S("0123456789"));
            var buf = new byte[16];
            @is.read(new CPtr(buf, 0), 4);
            W("r [" + Encoding.Latin1.GetString(buf, 0, 4) + "] gc " + @is.gcount() + " " + St(@is) + " tellg " + @is.tellg() + "\n");
            W("r peek " + @is.peek());
            W(" get " + @is.get());
            W(" get " + @is.get() + " " + St(@is) + "\n");
            @is.ignore(3, '8');
            W("r ignore tellg " + @is.tellg() + " " + St(@is) + "\n");
            @is.seekg(-2, Ios.cur);
            W("r seekg cur tellg " + @is.tellg() + " get " + @is.get() + "\n");
            @is.seekg(-3, Ios.end);
            W("r seekg end tellg " + @is.tellg() + " get " + @is.get() + "\n");
            @is.read(new CPtr(buf, 0), 10);
            W("r short gc " + @is.gcount() + " " + St(@is) + "\n");
            @is.clear();
            @is.seekg(0, Ios.beg);
            W("r rewind tellg " + @is.tellg() + " get " + @is.get() + " " + St(@is) + "\n");
            @is.ignore(100, -1);
            W("r ignore-all " + St(@is) + "\n");
            W("r get-end " + @is.get());
            W(" " + St(@is) + "\n");
            W("r str [" + @is.str() + "]\n");
        }
        {
            {
                var f = new OFStream(S("build/streamcheck.bin"), Ios.trunc | Ios.binary);
                W("f open " + B(f.is_open()) + " " + St(f) + "\n");
                int[] a = { 1, -2, 300000 };
                double[] d = { 1.5, -0.25 };
                f.write(new Ptr<int>(a, 0), 12);
                f.write(new Ptr<double>(d, 0), 16);
                f.write(R.Lit("MAGIC"), 5);
                W("f tellp " + f.tellp() + "\n");
                f.Put(3.25).Put((sbyte)' ').Put(42).Put((sbyte)'\n');
                W("f tellp " + f.tellp() + "\n");
                f.close();
                W("f closed " + B(f.is_open()) + "\n");
            }
            {
                var f = new OFStream(S("build/streamcheck.bin"), Ios.app);
                f.Put(R.Lit("tail\n"));
                f.Dispose();
            }
            {
                var f = new IFStream(S("build/streamcheck.bin"), Ios.@in | Ios.binary);
                W("f in open " + B(f.is_open()) + " " + St(f) + "\n");
                var a = new int[3];
                f.read(new Ptr<int>(a, 0), 12);
                W("f ints " + a[0] + " " + a[1] + " " + a[2] + " gc " + f.gcount() + " tellg " + f.tellg() + "\n");
                var d = new double[2];
                f.read(new Ptr<double>(d, 0), 16);
                W("f dbl " + G(d[0]) + " " + G(d[1]) + " tellg " + f.tellg() + "\n");
                var m = new byte[6];
                f.read(new CPtr(m, 0), 5);
                W("f magic " + R.Str(new CPtr(m, 0)) + "\n");
                double x = 0; int n = 0; var t = new Str();
                f.Get(ref x).Get(ref n).Get(t);
                W("f text " + G(x) + " " + n + " " + t + " " + St(f) + "\n");
                f.seekg(-5, Ios.end);
                Streams.getline(f, t);
                W("f end-5 [" + t + "] " + St(f) + "\n");
                Streams.getline(f, t);
                W("f after " + St(f) + "\n");
                f.clear();
                f.seekg(0, Ios.end);
                W("f size " + f.tellg() + "\n");
                f.seekg(4, Ios.beg);
                var b = new int[1];
                f.read(new Ptr<int>(b, 0), 4);
                W("f at4 " + b[0] + " tellg " + f.tellg() + "\n");
                f.seekg(1000, Ios.beg);
                f.read(new Ptr<int>(b, 0), 4);
                W("f past-end gc " + f.gcount() + " " + St(f) + "\n");
                f.close();
            }
            {
                var f = new IFStream(S("build/no-such-file.bin"), Ios.@in);
                W("f missing open " + B(f.is_open()) + " " + St(f) + "\n");
            }
        }
    }

    static string Ec(ErrorCode e) => e.v + " (" + e.message() + ")";

    static void Bsec()
    {
        {
            var @is = new IStringStream(S("x\nlast"));
            var line = new Str();
            for (int k = 0; k < 3; k++) { Streams.getline(@is, line); W("b line [" + line + "] " + St(@is) + "\n"); }
        }
        {
            var @is = new IStringStream(S("12abc 99999999999999999999 18446744073709551615 -1 1e400 inf 0x1p3 -.5 1e 5"));
            int i = -1; long L = 0; ulong U = 0; uint u = 0; double d = 0;
            @is.Get(ref i); W("b 12abc " + i + " " + St(@is) + "\n"); @is.clear();
            @is.Get(ref L); W("b long-over " + L + " " + St(@is) + "\n"); @is.clear();
            @is.Get(ref U); W("b ulong-max " + U + " " + St(@is) + "\n"); @is.clear();
            @is.Get(ref u); W("b uint-neg " + u + " " + St(@is) + "\n"); @is.clear();
            for (int k = 0; k < 5; k++) { @is.Get(ref d); W("b dbl " + G(d) + " " + St(@is) + "\n"); @is.clear(); }
            @is.Get(ref i); W("b last " + i + " " + St(@is) + "\n");
            i = 77; @is.Get(ref i); W("b after-eof " + i + " " + St(@is) + "\n");
            float fl = 0; @is.clear(); @is.str(S(" 0.1 ")); @is.Get(ref fl); W("b float " + G(fl) + " " + St(@is) + "\n");
            sbyte c = 0; @is.str(S("  q")); @is.Get(ref c); W("b char " + c + " " + St(@is) + "\n");
        }
        {
            var f = new OFStream(S("build/streamcheck.bin"), Ios.app);
            W("b app tellp " + f.tellp() + "\n");
            f.close();
            var g = new OFStream(R.Lit("build/no-dir/x.txt"), Ios.@out);
            W("b bad-open " + B(g.is_open()) + " " + St(g) + "\n");
            g.Put(R.Lit("dropped"));
            var h = new OFStream();
            W("b unopened " + B(h.is_open()) + " " + St(h) + "\n");
            h.open(S("build/streamcheck.txt"));
            h.Put(1.25).Manip_endl();
            W("b reopened " + B(h.is_open()) + " " + St(h) + " tellp " + h.tellp() + "\n");
            h.seekp(0, Ios.beg);
            h.Put(R.Lit("9"));
            h.seekp(0, Ios.end);
            h.Put(R.Lit("z"));
            h.Dispose();
            var r = new IFStream(new FsPath(S("build/streamcheck.txt")), Ios.@in);
            var t = new Str();
            Streams.getline(r, t);
            W("b reread [" + t + "] ");
            Streams.getline(r, t);
            W("[" + t + "] " + St(r) + "\n");
            r.close();
            var dir = new IFStream(S("build"), Ios.@in);
            W("b dir-open " + B(dir.is_open()) + " " + St(dir) + "\n");
        }
        {
            var o = new OStringStream();
            o.str(S("abc"));
            o.Put(R.Lit("XY"));
            W("b str-set [" + o.str() + "] tellp " + o.tellp() + "\n");
            o.precision(3);
            var o2 = new OStringStream();
            o2.MoveFrom(o);
            o2.Put(1.0 / 3.0);
            o.Put(R.Lit("q"));
            W("b moved [" + o2.str() + "] [" + o.str() + "]\n");
            var i1 = new IStringStream(S("one two"));
            var w = new Str();
            i1.Get(w);
            var i2 = new IStringStream();
            i2.MoveFrom(i1);
            i2.Get(w);
            W("b imoved [" + w + "] " + St(i2) + " str [" + i2.str() + "]\n");
            var p = new OStringStream();
            p.Put(1.5).Manip_scientific().Put(1.5).Manip_fixed().Put(1.5).Manip_defaultfloat().Put(1.5).Manip_showpoint().Put(1.5).Manip_noshowpoint().Put(1.5);
            p.Put(new SetW(6)).Put(new SetFill('.')).Put(new SetPrec(2)).Put(3.14159).Put(Streams.setw(4)).Put(Streams.setfill('#')).Put(1);
            p.setf(Ios.left, Ios.adjustfield); p.width(5); p.Put(R.Lit("ab")); p.unsetf(Ios.left); p.width(5); p.Put(R.Lit("cd"));
            p.Put(1.0f / 3.0f).Put((byte)66).Put(2147483647u * 2u);
            W("b manip [" + p.str() + "] w " + p.width() + " f " + p.fill() + " p " + p.precision() + "\n");
        }
        {
            var fs = new FStream(S("build/streamcheck.fs"), Ios.@in | Ios.@out);
            fs.Put(R.Lit("fstream"));
            W("b fstream " + B(fs.is_open()) + " tellp " + fs.tellp() + "\n");
            fs.close();
        }
        string[] paths = { "", "/", "a", "a/", "/a/b.c", "a/b.tar.gz", ".hidden", "dir/.hidden", "a/..", "..", ".",
            "a/./b/../c", "/../x", "//a//b//", "a/b/", "x.", "dir.d/file", "/a/b/../../..", "../a/../..", "./", "a//b", "/a" };
        foreach (var ps in paths)
        {
            var p = new FsPath(S(ps));
            W("p [" + ps + "] fn [" + p.filename() + "] par [" + p.parent_path() + "] stem [" + p.stem() + "] ext [" + p.extension() +
              "] abs " + B(p.is_absolute()) + B(p.is_relative()) + " has " + B(p.has_filename()) + B(p.has_parent_path()) + B(p.has_extension()) +
              " norm [" + p.lexically_normal() + "] empty " + B(p.empty()) + "\n");
            W("  join [" + p.Join(new FsPath(S("x"))) + "] [" + p.Join(S("/abs")) + "] [" + p.Join(new FsPath()) + "] [" + p.Join(R.Lit("y/z")) + "]");
            var q = p.Clone(); q.replace_extension(new FsPath(S(".out")));
            var q2 = p.Clone(); q2.replace_extension(S("txt"));
            var q3 = p.Clone(); q3.replace_extension();
            var q4 = p.Clone(); q4.replace_filename(new FsPath(S("new.inp")));
            var q5 = p.Clone(); q5.remove_filename();
            W(" rext [" + q + "] [" + q2 + "] [" + q3 + "] rfn [" + q4 + "] rmfn [" + q5 + "] str [" + p.@string() + "] gs [" + p.generic_string() +
              "] cs [" + R.Str(p.c_str()) + "] cmp " + Math.Sign(p.compare(new FsPath(S("a")))) + "\n");
        }
        {
            var a = new FsPath(S("x")); var b = new FsPath(R.Lit("y"));
            a.Assign(b); b.Assign(S("w"));
            var c = new FsPath(); c.MoveFrom(a);
            W("p assign [" + a + "] [" + b + "] [" + c + "] ");
            c.clear();
            W("[" + c + "] " + B(c.empty()) + "\n");
        }
        {
            var dir = new FsPath(S("build/fsdir"));
            var ec = new ErrorCode();
            Fs.remove(new FsPath(S("build/fsdir/x/y/f.txt")));
            Fs.remove(new FsPath(S("build/fsdir/x/y/g.txt")));
            Fs.remove(new FsPath(S("build/fsdir/x/y/h.txt")));
            Fs.remove(new FsPath(S("build/fsdir/x/y")), ec);
            W("fs mkdir " + B(Fs.create_directories(new FsPath(S("build/fsdir/x/y")))) + " again " + B(Fs.create_directories(new FsPath(S("build/fsdir/x/y")), ec)) + " " + Ec(ec) + "\n");
            var file = new FsPath(S("build/fsdir/x/y/f.txt"));
            var o = new OFStream(file);
            o.Put(R.Lit("0123456789"));
            o.close();
            W("fs exists " + B(Fs.exists(file)) + B(Fs.exists(dir)) + B(Fs.exists(new FsPath(S("build/fsdir/none")))) + B(Fs.exists(file, ec)) +
              " reg " + B(Fs.is_regular_file(file)) + B(Fs.is_regular_file(dir)) + " dir " + B(Fs.is_directory(file)) + B(Fs.is_directory(dir)) + "\n");
            W("fs size " + Fs.file_size(file) + " ec " + Fs.file_size(file, ec) + " " + Ec(ec));
            W(" missing " + Fs.file_size(new FsPath(S("build/fsdir/none")), ec) + " " + Ec(ec));
            W(" dir " + Fs.file_size(dir, ec) + " " + Ec(ec) + "\n");
            foreach (var sp in new[] { "build/fsdir/x/y/f.txt", "build/fsdir", "build/fsdir/none", "/dev/null" })
            {
                var s1 = Fs.status(new FsPath(S(sp))); var s2 = Fs.symlink_status(new FsPath(S(sp)), ec); var s3 = Fs.symlink_status(new FsPath(S(sp)));
                W("fs status " + sp + " " + s1.type() + " " + s2.type() + " " + s3.Clone().type() + " " + B(Fs.exists(s1)) + B(Fs.is_regular_file(s1)) + B(Fs.is_directory(s1)) + B(Fs.is_symlink(s1)) + "\n");
            }
            var g = new FsPath(S("build/fsdir/x/y/g.txt"));
            Fs.create_hard_link(file, g, ec); W("fs link " + Ec(ec));
            Fs.create_hard_link(file, g, ec); W(" again " + Ec(ec));
            Fs.create_hard_link(new FsPath(S("build/fsdir/none")), new FsPath(S("build/fsdir/x/y/n.txt")), ec); W(" missing " + Ec(ec) + " size " + Fs.file_size(g) + "\n");
            var h = new FsPath(S("build/fsdir/x/y/h.txt"));
            Fs.rename(g, h, ec); W("fs rename " + Ec(ec) + " " + B(Fs.exists(g)) + B(Fs.exists(h)));
            Fs.rename(g, h, ec); W(" missing " + Ec(ec));
            Fs.rename(h, g); W(" plain " + B(Fs.exists(g)) + B(Fs.exists(h)));
            Fs.rename(g, file, ec); W(" over " + Ec(ec) + " " + B(Fs.exists(g)) + B(Fs.exists(file)) + "\n");
            W("fs equiv " + B(Fs.equivalent(file, new FsPath(S("./build/fsdir/x/../x/y//f.txt")))) + B(Fs.equivalent(file, dir)) + B(Fs.equivalent(file, new FsPath(S("build/fsdir/none")), ec)) + " " + Ec(ec) +
              " " + B(Fs.equivalent(file, file, ec)) + " " + Ec(ec) + "\n");
            W("fs canon [" + Fs.canonical(new FsPath(S("build/fsdir/none")), ec) + "] " + Ec(ec) + " [" + Fs.weakly_canonical(new FsPath(S("build/../build/fsdir/./x/"))) + "] [" +
              Fs.weakly_canonical(new FsPath(S("/a/b/../c/.")), ec) + "] " + Ec(ec) + " [" + Fs.canonical(new FsPath(S("/x/../y")), ec) + "] " + Ec(ec) + " [" + Fs.canonical(dir) + "]\n");
            W("fs abs [" + Fs.absolute(new FsPath(S("rel/p"))) + "] [" + Fs.absolute(new FsPath(S("/abs/p"))) + "] cwd [" + Fs.current_path() + "] tmp [" + Fs.temp_directory_path() + "]\n");
            W("fs remove " + B(Fs.remove(file)) + B(Fs.remove(file)) + B(Fs.remove(dir)));
            W(" ec " + B(Fs.remove(new FsPath(S("build/fsdir/x")), ec)) + " " + Ec(ec));
            W(" " + B(Fs.remove(new FsPath(S("build/fsdir/x/y")), ec)) + " " + Ec(ec));
            W(" " + B(Fs.remove(new FsPath(S("build/fsdir/x/y")), ec)) + " " + Ec(ec) + "\n");
            var e2 = new ErrorCode();
            W("ec " + B(e2.Bool()) + " " + e2.value());
            foreach (int v in new[] { 13, 17, 20, 21, 95, 5 }) { e2.v = v; W(" " + Ec(e2)); }
            e2.clear(); W(" " + B(e2.Bool()) + "\n");
        }
    }
}
