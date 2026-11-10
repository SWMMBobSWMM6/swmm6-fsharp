// StreamCheck.java - the cases of streamcheck.cpp / streamcheck.cs through the Java runtime's streams and file system
// (java/src/cpp: Streams, OStream, IStream, ..., FsPath, Fs), statement for statement as streamcheck.cs. Section a
// must equal the C++ output (but for the one glibc %#g quirk) and the C# and Go outputs; section b the C# and Go
// outputs (the C# port's own behaviour); section c the C++ output.
// Written by Claude (Anthropic) for the SWMM6 ports; see AI-USE.md.
import cpp.CPtr;
import cpp.ErrorCode;
import cpp.FileStatus;
import cpp.Fs;
import cpp.FsPath;
import cpp.FStream;
import cpp.IFStream;
import cpp.IStringStream;
import cpp.IosBase;
import cpp.Ios;
import cpp.OFStream;
import cpp.OStringStream;
import cpp.PtrD;
import cpp.PtrI;
import cpp.R;
import cpp.RefB;
import cpp.RefD;
import cpp.RefF;
import cpp.RefI;
import cpp.RefL;
import cpp.SetFill;
import cpp.SetPrec;
import cpp.SetW;
import cpp.Str;
import cpp.Streams;

import java.nio.charset.StandardCharsets;

public final class StreamCheck {
	static final StringBuilder sb = new StringBuilder();
	static double Bits(long u) { return Double.longBitsToDouble(u); }
	static String St(IosBase s) { return "" + (s.good() ? 'g' : '-') + (s.eof() ? 'e' : '-') + (s.fail() ? 'f' : '-') + (s.bad() ? 'b' : '-'); }
	static String G(double v) { OStringStream o = new OStringStream(); o.Put_d(v); return o.str_().toString(); }
	static Str S(String s) { return new Str(s); }
	static String B(boolean b) { return b ? "1" : "0"; }
	static String U(long v) { return Long.toUnsignedString(v); }
	static void W(String s) { sb.append(s); }

	public static void main(String[] args) {
		String sec = args.length > 0 ? args[0] : "a";
		if (sec.equals("a")) A();
		else if (sec.equals("b")) Bsec();
		else C();
		byte[] bytes = sb.toString().getBytes(StandardCharsets.ISO_8859_1);
		Streams.cout.write_CPtr_l(new CPtr(bytes, 0), bytes.length);
	}

	static void C() {
		IFStream f = new IFStream(S("build/streamcheck.bin"), Ios.in | Ios.binary);
		int[] b = new int[1];
		f.read_PtrT_l__I(new PtrI(b, 0), 4);
		f.seekg(4, Ios.cur);
		f.read_PtrT_l__I(new PtrI(b, 0), 4);
		W("cur at8 " + b[0] + " tellg " + f.tellg() + " " + St(f) + "\n");
	}

	static void A() {
		double[] vals = { 0.0, Bits(0x8000000000000000L), 1.0, -1.0, 0.1, 0.5, 1.5, 2.5, 123.456, 1e-5,
			1.23456789e-7, 1e21, 1.7976931348623157e308, Bits(1), 2.2250738585072014e-308, 1.0 / 3.0, 2.0 / 3.0, 100.0,
			1e15, 123456789012345678.0, 0.000123456, 9.999999, 99.995, 0.125, Bits(0x7ff8000000000000L),
			Bits(0x7ff0000000000000L), Bits(0xfff0000000000000L), 1e100, 5e-310, 3.14159265358979, -0.0004567,
			1234567.0, 0.95, 0.05, 1e-300, 6.02214076e23 };
		int[] precs = { 0, 1, 2, 3, 4, 6, 8, 10, 15, 16, 17, 20, -1 };
		for (double v : vals) {
			for (int mode = 0; mode < 4; mode++)
				for (int sp = 0; sp < 2; sp++)
					for (int p : precs) {
						if (mode == 3 && p != 6) continue;
						OStringStream o = new OStringStream();
						if (mode == 1) o.Manip_fixed();
						if (mode == 2) o.Manip_scientific();
						if (mode == 3) o.setf_i_i(Ios.fixed | Ios.scientific, Ios.floatfield);
						if (sp == 1) o.Manip_showpoint();
						o.precision_l(p);
						o.Put_d(v);
						W("d " + mode + sp + " " + p + " [" + o.str_() + "]\n");
					}
			OStringStream f = new OStringStream();
			f.Put_f((float) v);
			W("f [" + f.str_() + "]\n");
		}
		{
			OStringStream o = new OStringStream();
			o.Put_c((byte) '[').Manip_setw(10).Put_d(3.5).Put_CPtr(R.Lit("][")).Manip_left().Manip_setw(8).Manip_setfill('*').Put_i(-42).Put_c((byte) ']');
			o.Put_c((byte) '[').Manip_right().Manip_setw(6).Put_CPtr(R.Lit("ab")).Put_CPtr(R.Lit("][")).Manip_setw(2).Put_CPtr(R.Lit("abcdef")).Put_c((byte) ']');
			o.Put_c((byte) '[').Manip_setw(5).Manip_setfill('0').Put_u(7).Put_CPtr(R.Lit("][")).Put_i(123).Put_c((byte) ']');
			o.Manip_boolalpha().Put_c((byte) '[').Put_b(true).Put_c((byte) ' ').Put_b(false).Put_c((byte) ']').Manip_noboolalpha().Put_c((byte) '[').Put_b(true).Put_b(false).Put_c((byte) ']');
			o.Put_c((byte) '[').Manip_setw(12).Manip_setfill(' ').Manip_quoted_Str(S("a\"b\\c")).Put_c((byte) ']');
			o.Put_c((byte) '[').Manip_quoted_CPtr(R.Lit("plain")).Put_c((byte) ']');
			o.Put_c((byte) '[').Manip_fixed().Manip_setprecision(3).Manip_setw(9).Put_d(2.0 / 3.0).Put_c((byte) ']');
			o.Put_c((byte) '[').Manip_defaultfloat().Manip_setprecision(4).Put_d(2.0 / 3.0).Put_c((byte) ']');
			o.Put_c((byte) '[').Put_l(Long.MIN_VALUE).Put_c((byte) ' ').Put_ul(-1L).Put_c((byte) ' ').Put_s((short) -5).Put_c((byte) ']');
			o.Put_c((byte) '[').Put_c((byte) 'x').Put_c((byte) 65).Put_c((byte) ']');
			W("w " + o.str_() + "\n");
		}
		{
			OStringStream o = new OStringStream(S("abcdef"));
			o.Put_CPtr(R.Lit("XY"));
			W("init [" + o.str_() + "] tellp " + o.tellp() + "\n");
			OStringStream q = new OStringStream();
			q.Put_CPtr(R.Lit("hello world"));
			q.seekp(6, Ios.beg);
			q.Put_CPtr(R.Lit("W"));
			q.seekp(-1, Ios.end);
			q.Put_CPtr(R.Lit("D"));
			q.seekp(2, Ios.beg);
			q.seekp(1, Ios.cur);
			q.Put_CPtr(R.Lit("L"));
			W("seekp [" + q.str_() + "] tellp " + q.tellp() + "\n");
		}
		{
			IStringStream is = new IStringStream(S("  3.25 -7 abc\nline two\n\nlast"));
			RefD.Cell d = new RefD.Cell(-1); RefI.Cell i = new RefI.Cell(-1); Str s = new Str();
			is.Get_dr(d); W("x d " + G(d.v) + " " + St(is) + "\n");
			is.Get_ir(i); W("x i " + i.v + " " + St(is) + "\n");
			is.Get_Str(s); W("x s " + s + " " + St(is) + "\n");
			Str line = new Str();
			for (int k = 0; k < 4; k++) {
				Streams.getline_IStream_Str(is, line);
				W("x line [" + line + "] " + St(is) + "\n");
			}
		}
		{
			IStringStream is = new IStringStream(S("12 4294967296 -2147483648 2147483648 -9223372036854775808 9000000000000000000 4294967295 7"));
			RefI.Cell a = new RefI.Cell(-1), b = new RefI.Cell(-1), c = new RefI.Cell(-1), e = new RefI.Cell(-1);
			is.Get_ir(a).Get_ir(b);
			W("x ints " + a.v + " " + b.v + " " + St(is) + "\n");
			is.clear(0);
			is.Get_ir(c).Get_ir(e);
			W("x ints " + c.v + " " + e.v + " " + St(is) + "\n");
			is.clear(0);
			RefL.Cell L = new RefL.Cell(-1);
			is.Get_lr(L);
			W("x long " + L.v + " " + St(is) + "\n");
			is.clear(0);
			RefL.Cell Uv = new RefL.Cell(0); RefI.Cell u = new RefI.Cell(0);
			is.Get_ulr(Uv);
			W("x ulong " + U(Uv.v) + " " + St(is) + "\n");
			is.clear(0);
			is.Get_ur(u);
			W("x uint " + Integer.toUnsignedString(u.v) + " " + St(is) + "\n");
		}
		{
			IStringStream is = new IStringStream(S("a,b,,c"));
			Str f = new Str();
			while (Streams.getline_IStream_Str_c(is, f, (byte) ',').ok()) W("x field [" + f + "] " + St(is) + "\n");
			W("x field end " + St(is) + "\n");
		}
		{
			IStringStream is = new IStringStream(S("   \t\n  tail"));
			is.Manip_ws();
			Str rest = new Str();
			Streams.getline_IStream_Str(is, rest);
			W("x ws [" + rest + "] " + St(is) + "\n");
			IStringStream e = new IStringStream(S("   "));
			e.Manip_ws();
			W("x ws-end " + St(e) + "\n");
		}
		{
			IStringStream is = new IStringStream(S("0123456789"));
			byte[] buf = new byte[16];
			is.read_CPtr_l(new CPtr(buf, 0), 4);
			W("r [" + new String(buf, 0, 4, StandardCharsets.ISO_8859_1) + "] gc " + is.gcount() + " " + St(is) + " tellg " + is.tellg() + "\n");
			W("r peek " + is.peek());
			W(" get " + is.get_());
			W(" get " + is.get_() + " " + St(is) + "\n");
			is.ignore(3, '8');
			W("r ignore tellg " + is.tellg() + " " + St(is) + "\n");
			is.seekg(-2, Ios.cur);
			W("r seekg cur tellg " + is.tellg() + " get " + is.get_() + "\n");
			is.seekg(-3, Ios.end);
			W("r seekg end tellg " + is.tellg() + " get " + is.get_() + "\n");
			is.read_CPtr_l(new CPtr(buf, 0), 10);
			W("r short gc " + is.gcount() + " " + St(is) + "\n");
			is.clear(0);
			is.seekg(0, Ios.beg);
			W("r rewind tellg " + is.tellg() + " get " + is.get_() + " " + St(is) + "\n");
			is.ignore(100, -1);
			W("r ignore-all " + St(is) + "\n");
			W("r get-end " + is.get_());
			W(" " + St(is) + "\n");
			W("r str [" + is.str_() + "]\n");
		}
		{
			{
				OFStream f = new OFStream(S("build/streamcheck.bin"), Ios.trunc | Ios.binary);
				W("f open " + B(f.is_open()) + " " + St(f) + "\n");
				int[] a = { 1, -2, 300000 };
				double[] d = { 1.5, -0.25 };
				f.write_PtrT_l__I(new PtrI(a, 0), 12);
				f.write_PtrT_l__D(new PtrD(d, 0), 16);
				f.write_CPtr_l(R.Lit("MAGIC"), 5);
				W("f tellp " + f.tellp() + "\n");
				f.Put_d(3.25).Put_c((byte) ' ').Put_i(42).Put_c((byte) '\n');
				W("f tellp " + f.tellp() + "\n");
				f.close();
				W("f closed " + B(f.is_open()) + "\n");
			}
			{
				OFStream f = new OFStream(S("build/streamcheck.bin"), Ios.app);
				f.Put_CPtr(R.Lit("tail\n"));
				f.Dispose();
			}
			{
				IFStream f = new IFStream(S("build/streamcheck.bin"), Ios.in | Ios.binary);
				W("f in open " + B(f.is_open()) + " " + St(f) + "\n");
				int[] a = new int[3];
				f.read_PtrT_l__I(new PtrI(a, 0), 12);
				W("f ints " + a[0] + " " + a[1] + " " + a[2] + " gc " + f.gcount() + " tellg " + f.tellg() + "\n");
				double[] d = new double[2];
				f.read_PtrT_l__D(new PtrD(d, 0), 16);
				W("f dbl " + G(d[0]) + " " + G(d[1]) + " tellg " + f.tellg() + "\n");
				byte[] m = new byte[6];
				f.read_CPtr_l(new CPtr(m, 0), 5);
				W("f magic " + R.Str(new CPtr(m, 0)) + "\n");
				RefD.Cell x = new RefD.Cell(0); RefI.Cell n = new RefI.Cell(0); Str t = new Str();
				f.Get_dr(x).Get_ir(n).Get_Str(t);
				W("f text " + G(x.v) + " " + n.v + " " + t + " " + St(f) + "\n");
				f.seekg(-5, Ios.end);
				Streams.getline_IStream_Str(f, t);
				W("f end-5 [" + t + "] " + St(f) + "\n");
				Streams.getline_IStream_Str(f, t);
				W("f after " + St(f) + "\n");
				f.clear(0);
				f.seekg(0, Ios.end);
				W("f size " + f.tellg() + "\n");
				f.seekg(4, Ios.beg);
				int[] b = new int[1];
				f.read_PtrT_l__I(new PtrI(b, 0), 4);
				W("f at4 " + b[0] + " tellg " + f.tellg() + "\n");
				f.seekg(1000, Ios.beg);
				f.read_PtrT_l__I(new PtrI(b, 0), 4);
				W("f past-end gc " + f.gcount() + " " + St(f) + "\n");
				f.close();
			}
			{
				IFStream f = new IFStream(S("build/no-such-file.bin"), Ios.in);
				W("f missing open " + B(f.is_open()) + " " + St(f) + "\n");
			}
		}
	}

	static String Ec(ErrorCode e) { return e.v + " (" + e.message() + ")"; }

	static void Bsec() {
		{
			IStringStream is = new IStringStream(S("x\nlast"));
			Str line = new Str();
			for (int k = 0; k < 3; k++) { Streams.getline_IStream_Str(is, line); W("b line [" + line + "] " + St(is) + "\n"); }
		}
		{
			IStringStream is = new IStringStream(S("12abc 99999999999999999999 18446744073709551615 -1 1e400 inf 0x1p3 -.5 1e 5"));
			RefI.Cell i = new RefI.Cell(-1); RefL.Cell L = new RefL.Cell(0); RefL.Cell Uv = new RefL.Cell(0); RefI.Cell u = new RefI.Cell(0); RefD.Cell d = new RefD.Cell(0);
			is.Get_ir(i); W("b 12abc " + i.v + " " + St(is) + "\n"); is.clear(0);
			is.Get_lr(L); W("b long-over " + L.v + " " + St(is) + "\n"); is.clear(0);
			is.Get_ulr(Uv); W("b ulong-max " + U(Uv.v) + " " + St(is) + "\n"); is.clear(0);
			is.Get_ur(u); W("b uint-neg " + Integer.toUnsignedString(u.v) + " " + St(is) + "\n"); is.clear(0);
			for (int k = 0; k < 5; k++) { is.Get_dr(d); W("b dbl " + G(d.v) + " " + St(is) + "\n"); is.clear(0); }
			is.Get_ir(i); W("b last " + i.v + " " + St(is) + "\n");
			i.v = 77; is.Get_ir(i); W("b after-eof " + i.v + " " + St(is) + "\n");
			RefF.Cell fl = new RefF.Cell(0); is.clear(0); is.str_Str(S(" 0.1 ")); is.Get_fr(fl); W("b float " + G(fl.v) + " " + St(is) + "\n");
			RefB.Cell c = new RefB.Cell((byte) 0); is.str_Str(S("  q")); is.Get_cr(c); W("b char " + c.v + " " + St(is) + "\n");
		}
		{
			OFStream f = new OFStream(S("build/streamcheck.bin"), Ios.app);
			W("b app tellp " + f.tellp() + "\n");
			f.close();
			OFStream g = new OFStream(R.Lit("build/no-dir/x.txt"), Ios.out);
			W("b bad-open " + B(g.is_open()) + " " + St(g) + "\n");
			g.Put_CPtr(R.Lit("dropped"));
			OFStream h = new OFStream();
			W("b unopened " + B(h.is_open()) + " " + St(h) + "\n");
			h.open_Str_i(S("build/streamcheck.txt"), Ios.out);
			h.Put_d(1.25).Manip_endl();
			W("b reopened " + B(h.is_open()) + " " + St(h) + " tellp " + h.tellp() + "\n");
			h.seekp(0, Ios.beg);
			h.Put_CPtr(R.Lit("9"));
			h.seekp(0, Ios.end);
			h.Put_CPtr(R.Lit("z"));
			h.Dispose();
			IFStream r = new IFStream(new FsPath(S("build/streamcheck.txt")), Ios.in);
			Str t = new Str();
			Streams.getline_IStream_Str(r, t);
			W("b reread [" + t + "] ");
			Streams.getline_IStream_Str(r, t);
			W("[" + t + "] " + St(r) + "\n");
			r.close();
			IFStream dir = new IFStream(S("build"), Ios.in);
			W("b dir-open " + B(dir.is_open()) + " " + St(dir) + "\n");
		}
		{
			OStringStream o = new OStringStream();
			o.str_Str(S("abc"));
			o.Put_CPtr(R.Lit("XY"));
			W("b str-set [" + o.str_() + "] tellp " + o.tellp() + "\n");
			o.precision_l(3);
			OStringStream o2 = new OStringStream();
			o2.MoveFrom(o);
			o2.Put_d(1.0 / 3.0);
			o.Put_CPtr(R.Lit("q"));
			W("b moved [" + o2.str_() + "] [" + o.str_() + "]\n");
			IStringStream i1 = new IStringStream(S("one two"));
			Str w = new Str();
			i1.Get_Str(w);
			IStringStream i2 = new IStringStream();
			i2.MoveFrom(i1);
			i2.Get_Str(w);
			W("b imoved [" + w + "] " + St(i2) + " str [" + i2.str_() + "]\n");
			OStringStream p = new OStringStream();
			p.Put_d(1.5).Manip_scientific().Put_d(1.5).Manip_fixed().Put_d(1.5).Manip_defaultfloat().Put_d(1.5).Manip_showpoint().Put_d(1.5).Manip_noshowpoint().Put_d(1.5);
			p.Put_SetW(new SetW(6)).Put_SetFill(new SetFill('.')).Put_SetPrec(new SetPrec(2)).Put_d(3.14159).Put_SetW(Streams.setw(4)).Put_SetFill(Streams.setfill('#')).Put_i(1);
			p.setf_i_i(Ios.left, Ios.adjustfield); p.width_l(5); p.Put_CPtr(R.Lit("ab")); p.unsetf(Ios.left); p.width_l(5); p.Put_CPtr(R.Lit("cd"));
			p.Put_f(1.0f / 3.0f).Put_uc((byte) 66).Put_u(2147483647 * 2);
			W("b manip [" + p.str_() + "] w " + p.width_() + " f " + p.fill_() + " p " + p.precision_() + "\n");
		}
		{
			FStream fs = new FStream(S("build/streamcheck.fs"), Ios.in | Ios.out);
			fs.Put_CPtr(R.Lit("fstream"));
			W("b fstream " + B(fs.is_open()) + " tellp " + fs.tellp() + "\n");
			fs.close();
		}
		String[] paths = { "", "/", "a", "a/", "/a/b.c", "a/b.tar.gz", ".hidden", "dir/.hidden", "a/..", "..", ".",
			"a/./b/../c", "/../x", "//a//b//", "a/b/", "x.", "dir.d/file", "/a/b/../../..", "../a/../..", "./", "a//b", "/a" };
		for (String ps : paths) {
			FsPath p = new FsPath(S(ps));
			W("p [" + ps + "] fn [" + p.filename() + "] par [" + p.parent_path() + "] stem [" + p.stem() + "] ext [" + p.extension() +
				"] abs " + B(p.is_absolute()) + B(p.is_relative()) + " has " + B(p.has_filename()) + B(p.has_parent_path()) + B(p.has_extension()) +
				" norm [" + p.lexically_normal() + "] empty " + B(p.empty()) + "\n");
			W("  join [" + p.Join_FsPath(new FsPath(S("x"))) + "] [" + p.Join_Str(S("/abs")) + "] [" + p.Join_FsPath(new FsPath()) + "] [" + p.Join_CPtr(R.Lit("y/z")) + "]");
			FsPath q = p.Clone(); q.replace_extension_FsPath(new FsPath(S(".out")));
			FsPath q2 = p.Clone(); q2.replace_extension_Str(S("txt"));
			FsPath q3 = p.Clone(); q3.replace_extension_FsPath(null);
			FsPath q4 = p.Clone(); q4.replace_filename(new FsPath(S("new.inp")));
			FsPath q5 = p.Clone(); q5.remove_filename();
			W(" rext [" + q + "] [" + q2 + "] [" + q3 + "] rfn [" + q4 + "] rmfn [" + q5 + "] str [" + p.string() + "] gs [" + p.generic_string() +
				"] cs [" + R.Str(p.c_str()) + "] cmp " + Integer.signum(p.compare(new FsPath(S("a")))) + "\n");
		}
		{
			FsPath a = new FsPath(S("x")); FsPath b = new FsPath(R.Lit("y"));
			a.Assign_FsPath(b); b.Assign_Str(S("w"));
			FsPath c = new FsPath(); c.MoveFrom(a);
			W("p assign [" + a + "] [" + b + "] [" + c + "] ");
			c.clear();
			W("[" + c + "] " + B(c.empty()) + "\n");
		}
		{
			FsPath dir = new FsPath(S("build/fsdir"));
			ErrorCode ec = new ErrorCode();
			Fs.remove_FsPath(new FsPath(S("build/fsdir/x/y/f.txt")));
			Fs.remove_FsPath(new FsPath(S("build/fsdir/x/y/g.txt")));
			Fs.remove_FsPath(new FsPath(S("build/fsdir/x/y/h.txt")));
			Fs.remove_FsPath_ErrorCode(new FsPath(S("build/fsdir/x/y")), ec);
			W("fs mkdir " + B(Fs.create_directories_FsPath(new FsPath(S("build/fsdir/x/y")))) + " again " + B(Fs.create_directories_FsPath_ErrorCode(new FsPath(S("build/fsdir/x/y")), ec)) + " " + Ec(ec) + "\n");
			FsPath file = new FsPath(S("build/fsdir/x/y/f.txt"));
			OFStream o = new OFStream(file, Ios.out);
			o.Put_CPtr(R.Lit("0123456789"));
			o.close();
			W("fs exists " + B(Fs.exists_FsPath(file)) + B(Fs.exists_FsPath(dir)) + B(Fs.exists_FsPath(new FsPath(S("build/fsdir/none")))) + B(Fs.exists_FsPath_ErrorCode(file, ec)) +
				" reg " + B(Fs.is_regular_file_FsPath(file)) + B(Fs.is_regular_file_FsPath(dir)) + " dir " + B(Fs.is_directory_FsPath(file)) + B(Fs.is_directory_FsPath(dir)) + "\n");
			W("fs size " + U(Fs.file_size_FsPath(file)) + " ec " + U(Fs.file_size_FsPath_ErrorCode(file, ec)) + " " + Ec(ec));
			W(" missing " + U(Fs.file_size_FsPath_ErrorCode(new FsPath(S("build/fsdir/none")), ec)) + " " + Ec(ec));
			W(" dir " + U(Fs.file_size_FsPath_ErrorCode(dir, ec)) + " " + Ec(ec) + "\n");
			for (String sp : new String[] { "build/fsdir/x/y/f.txt", "build/fsdir", "build/fsdir/none", "/dev/null" }) {
				FileStatus s1 = Fs.status_FsPath(new FsPath(S(sp))); FileStatus s2 = Fs.symlink_status_FsPath_ErrorCode(new FsPath(S(sp)), ec); FileStatus s3 = Fs.symlink_status_FsPath(new FsPath(S(sp)));
				W("fs status " + sp + " " + s1.type() + " " + s2.type() + " " + s3.Clone().type() + " " + B(Fs.exists_FileStatus(s1)) + B(Fs.is_regular_file_FileStatus(s1)) + B(Fs.is_directory_FileStatus(s1)) + B(Fs.is_symlink(s1)) + "\n");
			}
			FsPath g = new FsPath(S("build/fsdir/x/y/g.txt"));
			Fs.create_hard_link(file, g, ec); W("fs link " + Ec(ec));
			Fs.create_hard_link(file, g, ec); W(" again " + Ec(ec));
			Fs.create_hard_link(new FsPath(S("build/fsdir/none")), new FsPath(S("build/fsdir/x/y/n.txt")), ec); W(" missing " + Ec(ec) + " size " + U(Fs.file_size_FsPath(g)) + "\n");
			FsPath h = new FsPath(S("build/fsdir/x/y/h.txt"));
			Fs.rename_FsPath_FsPath_ErrorCode(g, h, ec); W("fs rename " + Ec(ec) + " " + B(Fs.exists_FsPath(g)) + B(Fs.exists_FsPath(h)));
			Fs.rename_FsPath_FsPath_ErrorCode(g, h, ec); W(" missing " + Ec(ec));
			Fs.rename_FsPath_FsPath(h, g); W(" plain " + B(Fs.exists_FsPath(g)) + B(Fs.exists_FsPath(h)));
			Fs.rename_FsPath_FsPath_ErrorCode(g, file, ec); W(" over " + Ec(ec) + " " + B(Fs.exists_FsPath(g)) + B(Fs.exists_FsPath(file)) + "\n");
			W("fs equiv " + B(Fs.equivalent_FsPath_FsPath(file, new FsPath(S("./build/fsdir/x/../x/y//f.txt")))) + B(Fs.equivalent_FsPath_FsPath(file, dir)) + B(Fs.equivalent_FsPath_FsPath_ErrorCode(file, new FsPath(S("build/fsdir/none")), ec)) + " " + Ec(ec) +
				" " + B(Fs.equivalent_FsPath_FsPath_ErrorCode(file, file, ec)) + " " + Ec(ec) + "\n");
			W("fs canon [" + Fs.canonical_FsPath_ErrorCode(new FsPath(S("build/fsdir/none")), ec) + "] " + Ec(ec) + " [" + Fs.weakly_canonical_FsPath(new FsPath(S("build/../build/fsdir/./x/"))) + "] [" +
				Fs.weakly_canonical_FsPath_ErrorCode(new FsPath(S("/a/b/../c/.")), ec) + "] " + Ec(ec) + " [" + Fs.canonical_FsPath_ErrorCode(new FsPath(S("/x/../y")), ec) + "] " + Ec(ec) + " [" + Fs.canonical_FsPath(dir) + "]\n");
			W("fs abs [" + Fs.absolute(new FsPath(S("rel/p"))) + "] [" + Fs.absolute(new FsPath(S("/abs/p"))) + "] cwd [" + Fs.current_path() + "] tmp [" + Fs.temp_directory_path() + "]\n");
			W("fs remove " + B(Fs.remove_FsPath(file)) + B(Fs.remove_FsPath(file)) + B(Fs.remove_FsPath(dir)));
			W(" ec " + B(Fs.remove_FsPath_ErrorCode(new FsPath(S("build/fsdir/x")), ec)) + " " + Ec(ec));
			W(" " + B(Fs.remove_FsPath_ErrorCode(new FsPath(S("build/fsdir/x/y")), ec)) + " " + Ec(ec));
			W(" " + B(Fs.remove_FsPath_ErrorCode(new FsPath(S("build/fsdir/x/y")), ec)) + " " + Ec(ec) + "\n");
			ErrorCode e2 = new ErrorCode();
			W("ec " + B(e2.Bool()) + " " + e2.value());
			for (int v : new int[] { 13, 17, 20, 21, 95, 5 }) { e2.v = v; W(" " + Ec(e2)); }
			e2.clear(); W(" " + B(e2.Bool()) + "\n");
		}
	}
}
