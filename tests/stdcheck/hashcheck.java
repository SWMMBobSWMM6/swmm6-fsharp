// hashcheck.java - the operations of hashcheck.cpp through the Java runtime's hash tables (java/src/cpp: UMap,
// USet, HashTable, RehashPolicy, StdHash); output must equal hashcheck.cpp's: bucket counts and iteration order
// after inserts, operator[], erases, reserve and copies, and std::hash of strings.
// Written by Claude (Anthropic) for the SWMM6 ports; see AI-USE.md.
import cpp.Pair;
import cpp.RefO;
import cpp.StdHash;
import cpp.Str;
import cpp.UMap;
import cpp.USet;

final class hashcheck {
	static long s = 52024;
	static long rnd() { s ^= s << 13; s ^= s >>> 7; s ^= s << 17; return s; }
	static final String[] pre = { "J", "C", "N", "OF", "P", "RG", "SUB_", "Link-", "node.", "W" };

	/** A Str from Java text (through its public bytes, so this needs nothing of Str but the fields). */
	static Str str(String x) {
		Str r = new Str();
		byte[] b = new byte[x.length() + 1];
		for (int i = 0; i < x.length(); i++) b[i] = (byte) x.charAt(i);
		r.b = b;
		r.n = x.length();
		return r;
	}

	static String text(Str s) { return new String(s.b, 0, s.n, java.nio.charset.StandardCharsets.ISO_8859_1); }

	static Str name(long r) {
		String x = pre[(int) Long.remainderUnsigned(r, 10)] + Long.remainderUnsigned(r >>> 8, 5000);
		if (Long.remainderUnsigned(r >>> 20, 3) == 0) x += "_" + Long.remainderUnsigned(r >>> 24, 97);
		return str(x);
	}

	public static void main(String[] args) throws Exception {
		StringBuilder sb = new StringBuilder();
		for (int round = 0; round < 40; round++) {
			UMap<Str, Integer> m = new UMap<Str, Integer>(null, null, () -> 0);
			UMap<Integer, Integer> mi = new UMap<Integer, Integer>(null, null, () -> 0);
			USet<Str> st = new USet<Str>(null, null);
			int n = 1 + (int) Long.remainderUnsigned(rnd(), round < 20 ? 60 : 3000);
			if (round % 7 == 3) m.reserve(n);
			for (int i = 0; i < n; i++) {
				long r = rnd();
				Str k = name(r);
				if (Long.remainderUnsigned(r, 5) == 0) { RefO<Integer> v = m.ref_Index(k); v.set(v.get() + i); }
				else m.emplace_K_V(k, i);
				mi.emplace_K_V((int) Long.remainderUnsigned(r, 20000) - 10000, i);
				st.insert(k);
				if (Long.remainderUnsigned(r, 11) == 0 && !m.empty()) m.erase_K(name(rnd()));
				if (Long.remainderUnsigned(r, 13) == 0) mi.erase_K((int) Long.remainderUnsigned(rnd(), 20000) - 10000);
				if (Long.remainderUnsigned(r, 17) == 0 && !m.empty()) m.erase_MItE(m.begin());
			}
			UMap<Str, Integer> c = m.Clone();
			sb.append("map ").append(round).append(": ").append(m.bucket_count()).append(" buckets ").append(m.size()).append(':');
			for (Pair<Str, Integer> kv : m.Items()) sb.append(' ').append(text(kv.first)).append('=').append((int) kv.second);
			sb.append("\ncopy:");
			for (Pair<Str, Integer> kv : c.Items()) sb.append(' ').append(text(kv.first));
			sb.append("\nimap ").append(mi.bucket_count()).append(' ').append(mi.size()).append(':');
			for (Pair<Integer, Integer> kv : mi.Items()) sb.append(' ').append((int) kv.first).append('=').append((int) kv.second);
			sb.append("\nset ").append(st.bucket_count()).append(':');
			for (Str k : st.Items()) sb.append(' ').append(text(k));
			sb.append('\n');
		}
		for (String w : new String[] { "", "a", "abcdefg", "abcdefgh", "abcdefghi", "Junction_17", "OUTFALL-1" })
			sb.append("hash ").append(w).append(' ').append(Long.toUnsignedString(StdHash.Of_Str(str(w)))).append('\n');
		System.out.write(sb.toString().getBytes(java.nio.charset.StandardCharsets.US_ASCII));
		System.out.flush();
	}
}
