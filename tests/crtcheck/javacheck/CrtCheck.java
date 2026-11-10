// CrtCheck.java - the Java side of tests/crtcheck (as gocheck/ is the Go side): java CrtCheck libm N | printf | scanf
// | misc | units, reading what the C program of the same name wrote on stdin, and reporting where the Java runtime
// (package cpp, java/src/cpp) differs from glibc. The case lines and their checks are gocheck's, one for one.
// Written by Claude (Anthropic) for the SWMM6 ports; see AI-USE.md.
import cpp.*;

import java.io.BufferedInputStream;
import java.io.BufferedReader;
import java.io.DataInputStream;
import java.io.EOFException;
import java.io.IOException;
import java.io.InputStreamReader;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.function.DoubleBinaryOperator;

public final class CrtCheck {
	public static void main(String[] args) throws IOException {
		switch (args[0]) {
			case "libm": libm(Integer.parseInt(args[1])); break;
			case "printf": lines("printf", CrtCheck::printfCase); break;
			case "scanf": lines("scanf", CrtCheck::scanfCase); break;
			case "misc": lines("misc", CrtCheck::miscCase); break;
			case "units": units(); break;
			default: throw new IllegalArgumentException(args[0]);
		}
		R.FlushAll();
	}

	interface Case { String[] check(String[] f); }   // {want, got}

	static void lines(String what, Case check) throws IOException {
		BufferedReader in = new BufferedReader(new InputStreamReader(System.in, StandardCharsets.ISO_8859_1), 1 << 20);
		int n = 0, bad = 0;
		for (String line; (line = in.readLine()) != null; ) {
			String[] f = line.split("\t", -1);
			n++;
			String[] r = check.check(f);
			if (!r[0].equals(r[1])) {
				bad++;
				if (bad <= 15) {
					String label = f[0];
					try { label = new String(unhex(f[0]), StandardCharsets.ISO_8859_1); } catch (RuntimeException e) { }
					System.out.printf("MISMATCH \"%s\"%n  glibc: %s%n  Java:  %s%n", label, r[0], r[1]);
				}
			}
		}
		System.out.printf("%s cases: %d, mismatches: %d%n", what, n, bad);
	}

	static byte[] unhex(String s) {
		if (s.equals("-")) return new byte[0];
		byte[] b = new byte[s.length() / 2];
		for (int i = 0; i < b.length; i++) b[i] = (byte) Integer.parseInt(s.substring(2 * i, 2 * i + 2), 16);
		return b;
	}
	static String hex(byte[] b, int off, int n) {
		StringBuilder sb = new StringBuilder();
		for (int i = 0; i < n; i++) sb.append(String.format("%02x", b[off + i] & 255));
		return sb.toString();
	}
	/** A C string holding these bytes. */
	static CPtr lit(byte[] b) { byte[] c = new byte[b.length + 1]; System.arraycopy(b, 0, c, 0, b.length); return new CPtr(c, 0); }

	// ------------------------------------------------------------------------------------------------ printf
	// hex(format) types args size ret hex(output)
	static String[] printfCase(String[] f) {
		CPtr format = lit(unhex(f[0]));
		String[] vals = f[2].split(",");
		Object[] args = new Object[f[1].length()];
		for (int i = 0; i < f[1].length(); i++) {
			String v = vals[i];
			switch (f[1].charAt(i)) {
				case 'd': args[i] = Double.longBitsToDouble(Long.parseUnsignedLong(v, 16)); break;
				case 'i': args[i] = (int) Long.parseLong(v, 16); break;
				case 'l': args[i] = Long.parseUnsignedLong(v, 16); break;
				case 's': args[i] = lit(unhex(v)); break;
				case 'S': args[i] = CPtr.ZERO; break;
			}
		}
		long size = Long.parseLong(f[3]);
		CPtr buf = R.Buf_i(1024);
		java.util.Arrays.fill(buf.B, (byte) 0x55);
		buf.B[0] = 0;
		int r = R.snprintf_CPtr_ul_CPtr_oA(buf, size, format, args);
		String out = "-";
		if (size > 0) {
			int n = 0;
			while (buf.B[n] != 0) n++;
			if (n > 0) out = hex(buf.B, 0, n);
		}
		return new String[] { f[4] + "\t" + f[5], r + "\t" + out };
	}

	// ------------------------------------------------------------------------------------------------ scanf
	// hex(format) hex(input) types ret values; the targets are the kinds the engine passes
	static String[] scanfCase(String[] f) {
		CPtr format = lit(unhex(f[0])), input = lit(unhex(f[1]));
		String types = f[2];
		int m = types.length();
		final int[] ints = new int[m];
		final long[] longs = new long[m];
		final float[] floats = new float[m];
		final double[] doubles = new double[m];
		CPtr[] bufs = new CPtr[m];
		Object[] args = new Object[m];
		int na = 0;
		for (int k = 0; k < m; k++) {
			final int kk = k;
			ints[k] = 0x55555555;
			longs[k] = 0x5555555555555555L;
			floats[k] = Float.intBitsToFloat(0x55555555);
			doubles[k] = Double.longBitsToDouble(0x5555555555555555L);
			switch (types.charAt(k)) {
				case 'i': case 'n': args[na++] = R.RefI(v -> ints[kk] = v); break;
				case 'u': args[na++] = R.RefL(v -> ints[kk] = (int) (long) v); break;     // a long setter, kept as unsigned int
				case 'l': args[na++] = R.RefL(v -> longs[kk] = v); break;
				case 'f': args[na++] = R.RefF(v -> floats[kk] = v); break;
				case 'd': args[na++] = R.RefD(v -> doubles[kk] = v); break;
				case 'k': ints[k] = 0x55; args[na++] = R.RefI(v -> ints[kk] = (byte) (int) v); break;   // a char
				case 's': case 'c':
					bufs[k] = R.Buf_i(64);
					java.util.Arrays.fill(bufs[k].B, (byte) 0x55);
					args[na++] = bufs[k];
					break;
			}
		}
		int r = R.sscanf(input, format, java.util.Arrays.copyOf(args, na));
		StringBuilder sb = new StringBuilder();
		for (int k = 0; k < m; k++) {
			switch (types.charAt(k)) {
				case 'i': case 'n': case 'u': case 'k': sb.append(ints[k]); break;
				case 'l': sb.append(longs[k]); break;
				case 'f': sb.append(String.format("%08x", Float.floatToRawIntBits(floats[k]))); break;
				case 'd': sb.append(String.format("%016x", Double.doubleToRawLongBits(doubles[k]))); break;
				case 's': case 'c': sb.append(hex(bufs[k].B, 0, 12)); break;
			}
			sb.append(' ');
		}
		String want = f[3] + "\t" + (f.length > 4 ? f[4] : "");
		return new String[] { want, r + "\t" + sb };
	}

	// ------------------------------------------------------------------------------------------------ misc
	// kind hex(input) result
	static String[] miscCase(String[] f) {
		byte[] in = unhex(f[1]);
		CPtr s = lit(in);
		CPtr e = CPtr.op_Add_CPtr_i(s, in.length);
		String got;
		switch (f[0]) {
			case "fci": { RefI.Cell v = new RefI.Cell(0x55555555); FromCharsResult r = Std.from_chars_CPtr_CPtr_ir(s, e, v);
				got = r.ec + " " + CPtr.Diff(r.ptr, s) + " " + v.v; break; }
			case "fcu": { RefI.Cell v = new RefI.Cell(0x55555555); FromCharsResult r = Std.from_chars_CPtr_CPtr_ur(s, e, v);
				got = r.ec + " " + CPtr.Diff(r.ptr, s) + " " + Integer.toUnsignedString(v.v); break; }
			case "fcl": { RefL.Cell v = new RefL.Cell(0x5555555555555555L); FromCharsResult r = Std.from_chars_CPtr_CPtr_lr(s, e, v);
				got = r.ec + " " + CPtr.Diff(r.ptr, s) + " " + v.v; break; }
			case "fcd": { RefD.Cell v = new RefD.Cell(0.25); FromCharsResult r = Std.from_chars_CPtr_CPtr_dr(s, e, v);
				got = r.ec + " " + CPtr.Diff(r.ptr, s) + " " + String.format("%016x", Double.doubleToRawLongBits(v.v)); break; }
			case "strtol": { RefO.Cell<CPtr> end = new RefO.Cell<>(s); long v = R.strtol(s, end, 10);
				got = v + " " + CPtr.Diff(end.v, s); break; }
			case "atoi": got = String.valueOf(R.atoi(s)); break;
			case "atol": got = String.valueOf(R.atol(s)); break;
			case "strtod": { RefO.Cell<CPtr> end = new RefO.Cell<>(s); double v = R.strtod_CPtr_CPtrr(s, end);
				got = String.format("%016x", Double.doubleToRawLongBits(v)) + " " + CPtr.Diff(end.v, s); break; }
			case "ctime": {
				long t = Long.parseLong(new String(in, StandardCharsets.ISO_8859_1));
				CPtr p = R.ctime_Ptrl(new PtrL(new long[] { t }, 0L));
				got = hex(p.B, p.O, (int) R.strlen(p));
				break;
			}
			case "nprocs": got = Integer.toUnsignedString(Sys.hardware_concurrency()); break;
			case "ctype": {
				int c = Integer.parseInt(new String(in, StandardCharsets.ISO_8859_1));
				got = "" + R.isdigit(c) + R.isalpha(c) + R.isalnum(c) + R.isspace(c) + R.isupper(c) + R.islower(c) + R.ispunct(c)
					+ R.isprint(c) + R.isxdigit(c) + " " + R.toupper(c) + " " + R.tolower(c);
				break;
			}
			default: throw new IllegalArgumentException(f[0]);
		}
		if (!got.equals(f[2])) got = got + "  (" + f[0] + " \"" + new String(in, StandardCharsets.ISO_8859_1) + "\")";
		return new String[] { f[2], got };
	}

	// ------------------------------------------------------------------------------------------------ libm
	static final String[] FN = { "atan2", "cbrt", "cosh", "sinh", "tanh", "log2", "expm1", "log1p", "hypot", "fmod", "remainder",
		"round", "nextafter", "exp", "log", "pow", "sin", "cos", "tan", "asin", "acos", "atan", "log10", "floor", "ceil", "trunc", "sqrt" };
	static final DoubleBinaryOperator[] F = {
		SysMath::Atan2_d, (x, y) -> SysMath.Cbrt_d(x), (x, y) -> SysMath.Cosh_d(x), (x, y) -> SysMath.Sinh_d(x),
		(x, y) -> SysMath.Tanh_d(x), (x, y) -> SysMath.Log2_d(x), (x, y) -> LibM.expm1(x), (x, y) -> LibM.log1p(x),
		LibM::hypot, LibM::fmod, LibM::remainder, (x, y) -> LibM.round(x), LibM::nextafter,
		(x, y) -> SysMath.Exp_d(x), (x, y) -> SysMath.Log_d(x), SysMath::Pow_d, (x, y) -> SysMath.Sin_d(x),
		(x, y) -> SysMath.Cos_d(x), (x, y) -> SysMath.Tan_d(x), (x, y) -> SysMath.Asin_d(x), (x, y) -> SysMath.Acos_d(x),
		(x, y) -> SysMath.Atan_d(x), (x, y) -> SysMath.Log10_d(x), (x, y) -> SysMath.Floor_d(x), (x, y) -> SysMath.Ceiling_d(x),
		(x, y) -> SysMath.Truncate_d(x), (x, y) -> SysMath.Sqrt_d(x) };
	static final int NSPEC = 24;

	/** The distance between two doubles in units in the last place (across zero too). */
	static long ulps(double a, double b) {
		long ia = Double.doubleToRawLongBits(a), ib = Double.doubleToRawLongBits(b);
		if (ia < 0) ia = Long.MIN_VALUE - ia;
		if (ib < 0) ib = Long.MIN_VALUE - ib;
		return ia > ib ? ia - ib : ib - ia;
	}

	static void libm(int n) throws IOException {
		DataInputStream in = new DataInputStream(new BufferedInputStream(System.in, 1 << 20));
		int per = n + NSPEC * NSPEC;
		System.out.printf("%-10s %8s %10s %9s %8s  %s%n", "function", "cases", "mismatch", "NaN-sign", "max ulp", "first mismatch (x, y: glibc vs Java)");
		long total = 0;
		boolean hist = System.getenv("HIST") != null;
		for (int fi = 0; fi < FN.length; fi++) {
			int bad = 0, nanSign = 0;
			int[] h = new int[5];                         // 1 ulp, 2, 3-10, >10, NaN vs number
			long maxU = 0;
			boolean nanNum = false;
			String first = "", big = "";
			for (int i = 0; i < per; i++) {
				double x, y;
				long want;
				try {
					x = Double.longBitsToDouble(Long.reverseBytes(in.readLong()));
					y = Double.longBitsToDouble(Long.reverseBytes(in.readLong()));
					want = Long.reverseBytes(in.readLong());
				} catch (EOFException e) {
					System.err.println("short input");
					System.exit(2);
					return;
				}
				long got = Double.doubleToRawLongBits(F[fi].applyAsDouble(x, y));
				if (got == want) continue;
				bad++;
				double w = Double.longBitsToDouble(want), g = Double.longBitsToDouble(got);
				String desc = x + ", " + y + ": " + w + " vs " + g;
				if (Double.isNaN(w) && Double.isNaN(g)) nanSign++;
				else if (!Double.isNaN(w) && !Double.isNaN(g)) {
					long d = ulps(w, g);
					if (Long.compareUnsigned(d, maxU) > 0) maxU = d;
					if (d == 1) h[0]++;
					else if (d == 2) h[1]++;
					else if (d <= 10) h[2]++;
					else { h[3]++; if (big.isEmpty()) big = desc; }
				} else {
					nanNum = true;
					h[4]++;
					if (big.isEmpty()) big = desc;
				}
				if (first.isEmpty()) first = desc;
			}
			total += bad;
			System.out.printf("%-10s %8d %10d %9d %8s  %s%n", FN[fi], per, bad, nanSign, nanNum ? "NaN/num" : Long.toUnsignedString(maxU), first);
			if (bad > 0 && hist)
				System.out.printf("           ulp 1: %d, 2: %d, 3-10: %d, >10: %d, NaN vs number: %d  %s%n", h[0], h[1], h[2], h[3], h[4], big);
		}
		System.out.printf("total mismatches: %d%n", total);
	}

	// ------------------------------------------------------------------------------------------------ units
	static int un, ubad;
	static void check(String name, boolean ok, Object... detail) {
		un++;
		if (!ok) { ubad++; System.out.println("FAIL " + name + " " + java.util.Arrays.deepToString(detail)); }
	}

	static void units() throws IOException {
		// sscanf into pointer parameters: the stash and R.Back, as the engine's parsers use them
		{
			RefL.Cell v = new RefL.Cell(-9);
			RefB.Cell flag = new RefB.Cell((byte) 'q');
			int r = R.Back_i_T1r_T2r__LB(R.sscanf(R.Lit("123 Y"), R.Lit("%ld %c"), R.Slot, R.Slot), v, flag);
			check("slot %ld %c", r == 2 && v.v == 123 && flag.v == 'Y', r, v.v, flag.v);
			v.v = -9; flag.v = 'q';
			r = R.Back_i_T1r_T2r__LB(R.sscanf(R.Lit("77"), R.Lit("%ld %c"), R.Slot, R.Slot), v, flag);
			check("slot partial", r == 1 && v.v == 77 && flag.v == 'q', r, v.v, flag.v);
			RefF.Cell x = new RefF.Cell(99.99f);
			r = R.Back_i_T1r__B(R.sscanf(R.Lit("1.5 X"), R.Lit("%f %c"), R.RefF(s -> x.v = s), R.Slot), flag);
			check("RefF + slot", r == 2 && x.v == 1.5f && flag.v == 'X', r, x.v, flag.v);
			RefS.Cell h = new RefS.Cell((short) 0);
			r = R.Back_i_T1r__S(R.sscanf(R.Lit("-12"), R.Lit("%d"), R.Slot), h);
			check("slot converted", r == 1 && h.v == -12, r, h.v);
			RefD.Cell d = new RefD.Cell(0);
			r = R.Back_i_T1r__D(R.sscanf(R.Lit("2.5"), R.Lit("%lf"), R.Slot), d);
			check("slot double", r == 1 && d.v == 2.5, r, d.v);
			CPtr s1 = R.Buf_i(80);
			r = R.sscanf(R.Lit("  SWMM5 x"), R.Lit("%s"), s1);
			check("%s CPtr", r == 1 && R.strcmp(s1, R.Lit("SWMM5")) == 0, r);
			RefI.Cell ci = new RefI.Cell(0);
			RefD.Cell cd = new RefD.Cell(0);
			r = R.sscanf(R.Lit("7 0.5"), R.Lit("%d %lf"), ci, cd);
			check("Ref cells as targets", r == 2 && ci.v == 7 && cd.v == 0.5, r, ci.v, cd.v);
			PtrB pb = new PtrB(new byte[16], 2L);
			r = R.sscanf(R.Lit("abc"), R.Lit("%s"), pb);
			check("%s into a char array pointer", r == 1 && pb.a[2] == 'a' && pb.a[4] == 'c' && pb.a[5] == 0, r);
		}

		// strtod details: hex floats, NaN payloads, the end pointer
		{
			RefO.Cell<CPtr> end = new RefO.Cell<>(CPtr.ZERO);
			CPtr s = R.Lit("  0x1.8p1rest");
			check("strtod hex", R.strtod(s, end) == 3.0 && CPtr.Diff(end.v, s) == 9);
			check("strtod nan payload", Double.doubleToRawLongBits(R.strtod_CPtr(R.Lit("nan(0x5)"))) == 0x7ff8000000000005L);
			s = R.Lit("abc");
			check("strtod no number", R.strtod(s, end) == 0.0 && CPtr.op_Eq(end.v, s));
			check("strtod huge exponent", R.strtod_CPtr(R.Lit("1e99999999999")) == Double.POSITIVE_INFINITY
				&& R.strtod_CPtr(R.Lit("1e-99999999999")) == 0.0);
		}

		// raw memory
		{
			long[] b = new long[1];
			Mem.memcpy_PtrT_PtrU_ul__LD(new PtrL(b, 0L), new PtrD(new double[] { -2.75 }, 0L), 8);
			check("memcpy double->u64", b[0] == Double.doubleToRawLongBits(-2.75));
			CPtr view = Bytes.View_PtrT__L(new PtrL(new long[] { 0x0102030405060708L }, 0L));
			check("bytes view", view.B.length == 8 && view.B[0] == 8 && view.B[7] == 1);
			double[] f = new double[1];
			Mem.memcpy_PtrT_CPtr_ul__D(new PtrD(f, 0L), view, 8);
			check("memcpy CPtr->double", Double.doubleToRawLongBits(f[0]) == 0x0102030405060708L);
			PtrD c = Mem.Calloc__D(5, 8);
			check("calloc", c.a.length == 5 && c.o == 0);
			Object[] arr = { new Object(), new Object(), new Object() };
			check("diff", Mem.Diff_T_PtrT(arr[2], new Ptr<Object>(arr, 1L)) == 1);
			int[] u = new int[2];
			Mem.memcpy_PtrT_PtrU_ul__IB(new PtrI(u, 1L), new PtrB(new byte[] { 1, 2, 3, 4, 5 }, 1L), 4);
			check("memcpy bytes->u32 offset", u[0] == 0 && u[1] == 0x05040302, u[0], u[1]);
			int[] w = { 0x11111111, 0x22222222 };
			Mem.memcpy_PtrT_CPtr_ul__I(new PtrI(w, 0L), new CPtr(new byte[] { 1, 2, 3, 4, 5, 6 }, 0), 6);
			check("memcpy partial element", w[0] == 0x04030201 && w[1] == 0x22220605, Integer.toHexString(w[0]), Integer.toHexString(w[1]));
			CPtr zv = Bytes.View_PtrT__Z(new PtrZ(new boolean[] { true, false, true }, 1L));
			check("bool view", zv.B.length == 2 && zv.B[0] == 0 && zv.B[1] == 1);
			CPtr dv = Bytes.View_PtrT__D(new PtrD(new double[] { 1.0, 2.0 }, 1L));
			check("double view offset", dv.B.length == 8 && dv.B[7] == 0x40 && dv.B[6] == 0);
			short[] sh = new short[2];
			Mem.memcpy_PtrT_CPtr_ul__S(new PtrS(sh, 0L), new CPtr(new byte[] { (byte) 0xFF, 0x7F, 0x01, (byte) 0x80 }, 0), 4);
			check("memcpy shorts", sh[0] == 0x7FFF && sh[1] == (short) 0x8001);
		}

		// binary files
		{
			Path dir = Files.createTempDirectory("crtcheck");
			try {
				CPtr name = R.Lit(dir.resolve("t.bin").toString());
				CFile fp = R.fopen(name, R.Lit("w+b"));
				Mem.fwrite_CPtr_ul_ul_CFile(R.Lit("STAMP"), 1, 5, fp);
				R.fwriteI1(42, fp);
				long pos = R.ftell(fp);
				R.fwriteD1(3.5, fp);
				R.fwriteF1(0.25f, fp);
				Mem.fwrite_PtrT_ul_ul_CFile__I(new PtrI(new int[] { 7, -8, 9 }, 0L), 4, 3, fp);
				R.fseek_CFile_l_i(fp, pos, 0);
				R.fwriteD1(4.5, fp);                          // overwrite in place
				R.rewind(fp);
				CPtr stamp = R.BufOf(16, new int[0]);
				long got = Mem.fread_CPtr_ul_ul_CFile(stamp, 1, 5, fp);
				RefI.Cell i = new RefI.Cell(0);
				RefD.Cell d = new RefD.Cell(0);
				RefF.Cell fl = new RefF.Cell(0);
				boolean ok = got == 5 && R.strncmp(stamp, R.Lit("STAMP"), 5) == 0 && R.freadI1(i, fp) == 1 && i.v == 42
					&& R.freadD1(d, fp) == 1 && d.v == 4.5 && R.freadF1(fl, fp) == 1 && fl.v == 0.25f;
				check("binary round trip", ok, got, i.v, d.v, fl.v);
				int[] ints = new int[4];
				long k = Mem.fread_PtrT_ul_ul_CFile__I(new PtrI(ints, 0L), 4, 4, fp);
				check("short fread", k == 3 && ints[0] == 7 && ints[1] == -8 && ints[2] == 9 && R.feof(fp) == 1, k, ints[0], ints[1], ints[2]);
				check("fgetc at end", R.fgetc(fp) == -1);
				check("ftell at end", R.ftell(fp) == 5 + 4 + 8 + 4 + 12);
				check("fseek negative", R.fseek_CFile_l_i(fp, -1, 0) == -1);
				check("fseek end", R.fseek_CFile_l_i(fp, -4, 2) == 0 && R.ftell(fp) == 29 && R.feof(fp) == 0);
				RefI.Cell last = new RefI.Cell(0);
				check("fread after seek", R.freadI1(last, fp) == 1 && last.v == 9);
				check("fclose", R.fclose(fp) == 0);
				fp = R.fopen(name, R.Lit("r"));
				CPtr line = R.Buf_i(8);
				check("fgets", !R.fgets(line, 4, fp).get_IsNull() && R.strcmp(line, R.Lit("STA")) == 0);
				int[] part = { 0x11111111, 0x22222222 };
				R.fseek_CFile_l_i(fp, -6, 2);
				long pk = Mem.fread_PtrT_ul_ul_CFile__I(new PtrI(part, 0L), 4, 2, fp);
				// the last 6 bytes are the top two of -8 and all of 9: ff ff 09 00 | 00 00
				check("fread partial element", pk == 1 && part[0] == 0x0009FFFF && part[1] == 0x22220000 && R.feof(fp) == 1,
					pk, Integer.toHexString(part[0]), Integer.toHexString(part[1]));
				R.fclose(fp);
				check("fopen missing", R.fopen(R.Lit(dir.resolve("none").toString()), R.Lit("r")) == null);
				// glibc: a directory opens for reading, and the first read fails (ferror set, feof not); r+ and w fail
				CFile dfp = R.fopen(R.Lit(dir.toString()), R.Lit("r"));
				check("fopen directory", dfp != null && R.fgets(R.Buf_i(8), 8, dfp).get_IsNull() && R.feof(dfp) == 0
					&& RFs.ferror(dfp) == 1 && RFs.fgetc(dfp) == -1 && R.feof(dfp) == 0 && R.fclose(dfp) == 0
					&& R.fopen(R.Lit(dir.toString()), R.Lit("r+")) == null && R.fopen(R.Lit(dir.toString()), R.Lit("w")) == null);
				check("ferror of a file", RFs.ferror(R.fopen(name, R.Lit("r"))) == 0);
				check("fopen bad mode", R.fopen(name, R.Lit("x")) == null);
				// append mode: writes go at the end, no seeking before the old end
				CPtr an = R.Lit(dir.resolve("a.txt").toString());
				fp = R.fopen(an, R.Lit("w"));
				R.fprintf(fp, R.Lit("one\n"));
				R.fclose(fp);
				fp = R.fopen(an, R.Lit("a"));
				check("append position", R.ftell(fp) == 4);
				check("append no seek back", R.fseek_CFile_l_i(fp, 0, 0) == -1);
				R.fprintf(fp, R.Lit("two\n"));
				R.fclose(fp);
				check("append content", new String(Files.readAllBytes(dir.resolve("a.txt")), StandardCharsets.ISO_8859_1).equals("one\ntwo\n"));
				// fprintf of every Java argument type the translator boxes
				fp = R.fopen(R.Lit(dir.resolve("t.txt").toString()), R.Lit("w"));
				R.fprintf(fp, R.Lit("%-6s|%3d|%5.2f|%c|%zu|%s|%d|%hu|%u|%c|%d|%s|%x\n"), R.Lit("ab"), (byte) -5, 1.25f, new U8((byte) 'Z'),
					new U64(-1L), new Str("str"), 7, new U16((char) 65535), new U32(-1), 'q', true, "jstr", (short) -1);
				R.FlushAll();
				R.fclose(fp);
				String txt = new String(Files.readAllBytes(dir.resolve("t.txt")), StandardCharsets.ISO_8859_1);
				check("fprintf Java types", txt.equals("ab    | -5| 1.25|Z|18446744073709551615|str|7|65535|4294967295|q|1|jstr|ffffffff\n"), txt);
				check("fprintf null file", R.fprintf(null, R.Lit("x")) == -1);
				CPtr sb = R.Buf_i(8);
				check("snprintf truncation", R.snprintf_CPtr_ul_CPtr_oA(sb, 4, R.Lit("%d"), 123456) == 6 && R.strcmp(sb, R.Lit("123")) == 0);
				check("remove", R.remove(an) == 0 && R.remove(an) == -1);
				check("File.Exists", R.File_Exists(dir.resolve("t.txt").toString()) && !R.File_Exists(dir.toString()) && !R.File_Exists(""));
			} finally {
				try (var st = Files.list(dir)) { st.forEach(p -> p.toFile().delete()); }
				Files.deleteIfExists(dir);
			}
		}

		// tuples, atomics, variants, once
		{
			double[] t1 = { 1, 2 }, t2 = { 1, 2 };
			TupN a = new TupN(1, 2.5, new PtrD(t1, 0L), true, 3);
			TupN b = new TupN(1, 2.5, new PtrD(t1, 0L), true, 3);
			TupN c = new TupN(1, 2.5, new PtrD(t2, 0L), true, 3);
			TupN d = new TupN(1, 2.5, new PtrD(t1, 0L), true, 4);
			check("tuple equal", a.CompareTo(b) == 0 && a.Equals(b));
			check("tuple identity", a.CompareTo(c) != 0 && a.CompareTo(c) == -c.CompareTo(a));
			check("tuple enum", a.CompareTo(d) < 0 && d.CompareTo(a) > 0);
			AtomicL m = new AtomicL();
			check("fetch_add", m.fetch_add(5, 0) == 0 && m.fetch_add(2, 0) == 5 && m.v == 7);
			AtomicD fa = new AtomicD(1.5);
			RefD.Cell old = new RefD.Cell(1.0);
			boolean ok1 = fa.compare_exchange(old, 3.0);
			boolean ok2 = fa.compare_exchange(old, 3.0);
			check("compare_exchange", !ok1 && old.v == 1.5 && ok2 && fa.v == 3.0);
			fa.ref_v().set(0.5);
			check("ref_v", fa.v == 0.5);
			AtomicZ z = new AtomicZ(true);
			z.ref_v().set(false);
			check("atomic bool", !z.v && !z.Clone().v);
			AtomicI ai = new AtomicI(0);
			ai.ref_v().set(ai.ref_v().get() + 1);
			check("atomic int", ai.v == 1);
			AtomicL ul = new AtomicL(-1L);
			ul.fetch_add(1L, 0);
			check("atomic ulong wraps", ul.v == 0);
			Variant v = new Variant();
			check("variant default", Boolean.FALSE.equals(v.v) && v.idx == 0);
			Str s = new Str("abc");
			v.Set_Str(s);
			s.append_i('d');
			Variant w = v.Clone();
			check("variant string", v.idx == 3 && v.v.toString().equals("abc") && w.v != v.v && w.v.toString().equals("abc"));
			v.Set_d(2.5);
			check("variant double", v.idx == 2 && (Double) v.v == 2.5 && v.index() == 2);
			OnceFlag once = new OnceFlag();
			int[] calls = { 0 };
			Std.call_once(once, () -> calls[0]++);
			Std.call_once(once, () -> calls[0]++);
			check("call_once", calls[0] == 1);
			RefI.Cell x = new RefI.Cell(1), y = new RefI.Cell(2);
			Std.swap__I(x, y);
			check("swap", x.v == 2 && y.v == 1);
			double[] da = { 1, 2 };
			Std.swap__D(new RefD.Arr(da, 0), new RefD.Arr(da, 1));
			check("swap array elements", da[0] == 2 && da[1] == 1);
			TimePoint t0 = TimePoint.now();
			check("duration", TimePoint.op_Sub(TimePoint.now(), t0).Clone().count() >= 0 && TimePoint.ZERO.ns == 0);
			check("qnan", Double.doubleToRawLongBits(Std.QNaN) == 0x7FF8000000000000L);
			check("nop", Std.Nop(3) == 0);
			Pair<Integer, Double> pr = new Pair<>(4, 0.5);
			check("get0/get1", Std.get0_PairAB__ID(pr) == 4 && Std.get1_PairAB__ID(pr) == 0.5);
			boolean thrown = false;
			try { Std.Rethrow(new java.io.IOException("x")); } catch (Throwable e) { thrown = e instanceof java.io.IOException; }
			check("Rethrow", thrown);
			thrown = false;
			try { R.<Integer>thr(new IllegalStateException("y")); } catch (IllegalStateException e) { thrown = true; }
			check("thr", thrown);
			check("as", R.as("s", String.class) != null && R.as(3, String.class) == null && R.as(null, String.class) == null);
			ErrorCode ec = new ErrorCode();
			ec.v = 2;
			check("error_code", ec.Bool() && ec.message().toString().equals("No such file or directory"));
			Object se = new SysException("NotImplementedException", "m");
			check("SysException", ((SysException) se).getMessage().equals("m") && !(se instanceof CppException) && se instanceof RuntimeException);
		}

		// conversions and math corners
		{
			check("D2I", Conv.D2I(Double.NaN) == Integer.MIN_VALUE && Conv.D2I(2147483647.9) == 2147483647 && Conv.D2I(-2147483648.9) == Integer.MIN_VALUE
				&& Conv.D2I(2147483648.0) == Integer.MIN_VALUE && Conv.D2I(-3.7) == -3);
			check("D2L", Conv.D2L(9.3e18) == Long.MIN_VALUE && Conv.D2L(-9.2e18) == -9200000000000000000L && Conv.D2L(Double.NaN) == Long.MIN_VALUE);
			check("D2UL", Conv.D2UL(1.8446744073709550e19) == -2048L && Conv.D2UL(3.5) == 3 && Conv.D2UL(-1.0) == -1L);
			check("D2SB", Conv.D2SB(200.0) == (byte) -56 && Conv.D2SB(1e10) == 0);
			check("u2d", R.u2d(-1L) == 18446744073709551616.0 && R.u2d((1L << 63) + 1025) == 9223372036854777856.0 && R.u2d(5) == 5.0);
			check("u2d rounding", R.u2d(0x8000000000000400L) == 9223372036854775808.0 && R.u2d(0x8000000000000C00L) == 9223372036854779904.0);
			check("u2f", R.u2f(-1L) == 18446744073709551616.0f && R.u2f(0x8000008000000000L) == 9223372036854775808.0f
				&& R.u2f(0x8000008000000001L) == 9223373136366403584.0f);
			check("signbit", R.signbit(-0.0) && !R.signbit(0.0) && R.signbit(Double.longBitsToDouble(0xFFF8000000000000L)));
			check("CopySign NaN sign", SysMath.CopySign_d(1.0, Double.longBitsToDouble(0xFFF8000000000000L)) == -1.0);
			check("Truncate", SysMath.Truncate_d(-2.7) == -2.0 && Double.doubleToRawLongBits(SysMath.Truncate_d(-0.5)) == 0x8000000000000000L);
			check("round", LibM.round(2.5) == 3.0 && LibM.round(-2.5) == -3.0 && LibM.round(0.49999999999999994) == 0.0
				&& Double.doubleToRawLongBits(LibM.round(-0.4)) == 0x8000000000000000L && LibM.lround(-2.5) == -3);
			check("fmax/fmin", LibM.fmax(Double.NaN, 1.0) == 1.0 && LibM.fmin(2.0, Double.NaN) == 2.0 && LibM.fmax(-0.0, 0.0) == 0.0
				&& Double.doubleToRawLongBits(LibM.fmin(0.0, -0.0)) == 0x8000000000000000L);
			check("Log2 powers", SysMath.Log2_d(1024.0) == 10.0 && SysMath.Log2_d(Double.MIN_VALUE) == -1074.0 && SysMath.Log2_d(1.0) == 0.0);
		}

		// char arrays and strings
		{
			CPtr p = R.LitN("OPENSWMM_HS_V1", 16);
			check("LitN", p.B.length == 16 && p.B[14] == 0 && p.B[15] == 0);
			CPtr q = R.CopyBuf(p);
			q.B[0] = 'X';
			check("CopyBuf", p.B[0] == 'O' && q.O == p.O);
			R.memcpyBuf(q, p);
			check("memcpyBuf", q.B[0] == 'O');
			CPtr dst = R.BufOf(6, new int[] { 'a', 'b' });
			R.strncpy(dst, R.Lit("xy"), 5);
			check("strncpy pads", dst.B[0] == 'x' && dst.B[1] == 'y' && dst.B[2] == 0 && dst.B[4] == 0);
			check("strncmp", R.strncmp(R.Lit("FLOWX"), R.Lit("FLOWY"), 4) == 0 && R.strncmp(R.Lit("a"), R.Lit("b"), 4) < 0);
			check("memcmp", R.memcmp(R.Lit("SWMM5-HOTSTART4"), R.Lit("SWMM5-HOTSTART3"), 15) > 0);
			check("bufs", R.Bufs(5, 24).length == 5 && R.Bufs(5, 24)[4].B.length == 24);
			String path = System.getenv("PATH");
			check("getenv", path != null && R.strcmp(R.getenv(R.Lit("PATH")), R.Lit(path)) == 0 && R.getenv(R.Lit("CRTCHECK_NONE_SET")).get_IsNull());
			check("strtol0", R.strtol0(R.Lit(" 24h"), 10) == 24 && R.atol(R.Lit("-5")) == -5);
			PtrL tp = new PtrL(new long[1], 0L);
			long t = R.time_Ptrl(tp);
			check("time", t > 1700000000L && tp.a[0] == t && R.difftime(t + 3, t) == 3.0 && R.time_Ptrl(PtrL.ZERO) >= t);
			check("strcmp unsigned", R.strcmp(new CPtr(new byte[] { (byte) 0xE9, 0 }, 0), R.Lit("e")) > 0);
			CPtr hay = R.Lit("a,b,,c");
			check("strchr", CPtr.Diff(R.strchr(hay, ','), hay) == 1 && CPtr.Diff(R.strchr(hay, 0), hay) == 6 && R.strchr(hay, 'z').get_IsNull());
			check("strrchr", CPtr.Diff(R.strrchr(hay, ','), hay) == 4);
			check("strstr", CPtr.Diff(R.strstr(hay, R.Lit(",,")), hay) == 3 && R.strstr(hay, R.Lit("x")).get_IsNull() && CPtr.op_Eq(R.strstr(hay, R.Lit("")), hay));
			check("strcspn", R.strcspn(hay, R.Lit(",")) == 1 && R.strcspn(R.Lit("abc"), R.Lit("x")) == 3);
			StringBuilder toks = new StringBuilder();
			for (CPtr tk = R.strtok(hay, R.Lit(",")); !tk.get_IsNull(); tk = R.strtok(CPtr.ZERO, R.Lit(","))) toks.append(R.Str(tk)).append('|');
			check("strtok", toks.toString().equals("a|b|c|"), toks);
			CPtr cat = R.Buf_i(16);
			R.strcpy(cat, R.Lit("ab"));
			R.strcat(cat, R.Lit("cd"));
			check("strcpy/strcat", R.Str(cat).equals("abcd") && R.strlen(cat) == 4);
			R.strcpy(CPtr.op_Add_CPtr_i(cat, 1), cat);
			check("strcpy overlap", R.Str(cat).equals("aabcd"));
			check("toupper", R.toupper('a') == 'A' && R.toupper('{') == '{');
			check("atoi wrap", R.atoi(R.Lit("4294967297")) == 1 && R.atoi(R.Lit("99999999999999999999")) == -1);
		}
		System.out.printf("units cases: %d, failures: %d%n", un, ubad);
	}
}
