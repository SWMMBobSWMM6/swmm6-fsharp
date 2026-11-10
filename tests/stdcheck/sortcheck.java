// sortcheck.java - the cases of sortcheck.cpp through java/src/cpp/StdAlgo.java; output must equal sortcheck.cpp's
// byte for byte. No argument (or O): the generic Sort / StableSort / PartialSort over an array of structs. With D, I
// or L: the same permutations from the primitive specialisations (Sort__I over the ids, ordered by their keys, and
// so on), which must give the same output too.
// Written by Claude (Anthropic) for the SWMM6 ports; see AI-USE.md.
import cpp.Fn;
import cpp.StdAlgo;

final class sortcheck {
	static final class E {
		final int key, id;
		E(int key, int id) { this.key = key; this.id = id; }
	}

	static long s = 1729;
	static long rnd() { s ^= s << 13; s ^= s >>> 7; s ^= s << 17; return s; }

	static void out(StringBuilder sb, String tag, int n, int pat, int[] ids) {
		sb.append(tag).append(' ').append(n).append(' ').append(pat).append(':');
		for (int id : ids) sb.append(' ').append(id);
		sb.append('\n');
	}

	interface Run { int[] apply(int[] key, int n, int which); }    // which: 0 sort, 1 stable_sort, 2 partial_sort

	public static void main(String[] args) throws Exception {
		String mode = args.length > 0 ? args[0] : "O";
		Run run;
		switch (mode) {
		case "O": run = (key, n, which) -> {
				E[] a = new E[n];
				for (int i = 0; i < n; i++) a[i] = new E(key[i], i);
				Fn.F2<E, E, Boolean> lt = (x, y) -> x.key < y.key;
				if (which == 0) StdAlgo.Sort(a, 0, n, lt);
				else if (which == 1) StdAlgo.StableSort(a, 0, n, lt);
				else StdAlgo.PartialSort(a, 0, n, n, lt);
				int[] ids = new int[n];
				for (int i = 0; i < n; i++) ids[i] = a[i].id;
				return ids;
			};
			break;
		case "I": run = (key, n, which) -> {
				int[] a = new int[n];
				for (int i = 0; i < n; i++) a[i] = i;
				Fn.F2<Integer, Integer, Boolean> lt = (x, y) -> key[x] < key[y];
				if (which == 0) StdAlgo.Sort__I(a, 0, n, lt);
				else if (which == 1) StdAlgo.StableSort__I(a, 0, n, lt);
				else StdAlgo.PartialSort__I(a, 0, n, n, lt);
				return a;
			};
			break;
		case "L": run = (key, n, which) -> {
				long[] a = new long[n];
				for (int i = 0; i < n; i++) a[i] = i;
				Fn.F2<Long, Long, Boolean> lt = (x, y) -> key[(int) (long) x] < key[(int) (long) y];
				if (which == 0) StdAlgo.Sort__L(a, 0, n, lt);
				else if (which == 1) StdAlgo.StableSort__L(a, 0, n, lt);
				else StdAlgo.PartialSort__L(a, 0, n, n, lt);
				int[] ids = new int[n];
				for (int i = 0; i < n; i++) ids[i] = (int) a[i];
				return ids;
			};
			break;
		case "D": run = (key, n, which) -> {
				double[] a = new double[n];
				for (int i = 0; i < n; i++) a[i] = i;
				Fn.F2<Double, Double, Boolean> lt = (x, y) -> key[(int) (double) x] < key[(int) (double) y];
				if (which == 0) StdAlgo.Sort__D(a, 0, n, lt);
				else if (which == 1) StdAlgo.StableSort__D(a, 0, n, lt);
				else StdAlgo.PartialSort__D(a, 0, n, n, lt);
				int[] ids = new int[n];
				for (int i = 0; i < n; i++) ids[i] = (int) a[i];
				return ids;
			};
			break;
		default: throw new IllegalArgumentException("mode: O, D, I or L");
		}

		java.util.ArrayList<Integer> ns = new java.util.ArrayList<>();
		for (int n = 0; n <= 300; n++) ns.add(n);
		for (int n : new int[] { 511, 512, 513, 1000, 4096, 10007 }) ns.add(n);
		StringBuilder sb = new StringBuilder();
		for (int n : ns) for (int pat = 0; pat < 7; pat++) {
			int[] key = new int[n];
			for (int i = 0; i < n; i++) {
				int k;
				switch (pat) {
				case 0: k = (int) Long.remainderUnsigned(rnd(), 5); break;            // many ties
				case 1: k = (int) Long.remainderUnsigned(rnd(), 1000000); break;      // few ties
				case 2: k = i / 3; break;                                             // sorted with runs
				case 3: k = (n - i) / 2; break;                                       // reversed with runs
				case 4: k = 7; break;                                                 // all equal
				case 5: k = (i < n / 2) ? i : n - i; break;                           // organ pipe
				default: k = (int) ((i * 7919L) % 31); break;                         // sawtooth
				}
				key[i] = k;
			}
			out(sb, "sort", n, pat, run.apply(key, n, 0));
			out(sb, "stable", n, pat, run.apply(key, n, 1));
			out(sb, "heap", n, pat, run.apply(key, n, 2));
		}
		System.out.write(sb.toString().getBytes(java.nio.charset.StandardCharsets.US_ASCII));
		System.out.flush();
	}
}
