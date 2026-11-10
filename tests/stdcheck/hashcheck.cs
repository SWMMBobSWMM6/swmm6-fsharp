// hashcheck.cs - the same operations through cs/runtime/Containers.cs; output must equal hashcheck.cpp's.
// Written by Claude (Anthropic); see AI-USE.md.
using System;
using System.Text;
using Cpp;
static class P {
    static ulong s = 52024;
    static ulong rnd() { s ^= s << 13; s ^= s >> 7; s ^= s << 17; return s; }
    static readonly string[] pre = { "J", "C", "N", "OF", "P", "RG", "SUB_", "Link-", "node.", "W" };
    static Str name(ulong r) {
        string x = pre[r % 10] + ((r >> 8) % 5000).ToString();
        if ((r >> 20) % 3 == 0) x += "_" + ((r >> 24) % 97).ToString();
        return new Str(x);
    }
    static void Main() {
        var sb = new StringBuilder();
        for (int round = 0; round < 40; round++) {
            var m = new UMap<Str, int>();
            var mi = new UMap<int, int>();
            var st = new USet<Str>();
            int n = 1 + (int)(rnd() % (ulong)(round < 20 ? 60 : 3000));
            if (round % 7 == 3) m.reserve((ulong)n);
            for (int i = 0; i < n; i++) {
                ulong r = rnd();
                var k = name(r);
                if (r % 5 == 0) m.Index(k) += i; else m.emplace(k, i);
                mi.emplace((int)(r % 20000) - 10000, i);
                st.insert(k);
                if (r % 11 == 0 && !m.empty()) m.erase(name(rnd()));
                if (r % 13 == 0) mi.erase((int)(rnd() % 20000) - 10000);
                if (r % 17 == 0 && !m.empty()) m.erase(m.begin());
            }
            var c = m.Clone();
            sb.Append("map ").Append(round).Append(": ").Append(m.bucket_count()).Append(" buckets ").Append(m.size()).Append(':');
            foreach (var kv in m.Items()) sb.Append(' ').Append(kv.first).Append('=').Append(kv.second);
            sb.Append("\ncopy:");
            foreach (var kv in c.Items()) sb.Append(' ').Append(kv.first);
            sb.Append("\nimap ").Append(mi.bucket_count()).Append(' ').Append(mi.size()).Append(':');
            foreach (var kv in mi.Items()) sb.Append(' ').Append(kv.first).Append('=').Append(kv.second);
            sb.Append("\nset ").Append(st.bucket_count()).Append(':');
            foreach (var k in st.Items()) sb.Append(' ').Append(k);
            sb.Append('\n');
        }
        foreach (var w in new[] { "", "a", "abcdefg", "abcdefgh", "abcdefghi", "Junction_17", "OUTFALL-1" })
            sb.Append("hash ").Append(w).Append(' ').Append(StdHash.Of(new Str(w))).Append('\n');
        var o = Console.OpenStandardOutput(); var b = Encoding.ASCII.GetBytes(sb.ToString()); o.Write(b, 0, b.Length);
    }
}
