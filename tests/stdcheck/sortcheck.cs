// sortcheck.cs - the same cases through cs/runtime/StdAlgo.cs; output must equal sortcheck.cpp's byte for byte.
// Written by Claude (Anthropic); see AI-USE.md.
using System;
using System.Collections.Generic;
using System.Text;
using Cpp;
struct E { public int key, id; }
static class P {
    static ulong s = 1729;
    static ulong rnd() { s ^= s << 13; s ^= s >> 7; s ^= s << 17; return s; }
    static void Out(StringBuilder sb, string tag, int n, int pat, E[] v) {
        sb.Append(tag).Append(' ').Append(n).Append(' ').Append(pat).Append(':');
        foreach (var e in v) sb.Append(' ').Append(e.id);
        sb.Append('\n');
    }
    static void Main() {
        var ns = new List<int>();
        for (int n = 0; n <= 300; n++) ns.Add(n);
        foreach (int n in new[] { 511, 512, 513, 1000, 4096, 10007 }) ns.Add(n);
        Func<E, E, bool> lt = (a, b) => a.key < b.key;
        var sb = new StringBuilder();
        foreach (int n in ns) for (int pat = 0; pat < 7; pat++) {
            var v = new E[n];
            for (int i = 0; i < n; i++) {
                int k;
                switch (pat) {
                case 0: k = (int)(rnd() % 5); break;
                case 1: k = (int)(rnd() % 1000000); break;
                case 2: k = i / 3; break;
                case 3: k = (n - i) / 2; break;
                case 4: k = 7; break;
                case 5: k = (i < n / 2) ? i : n - i; break;
                default: k = (int)((i * 7919L) % 31); break;
                }
                v[i] = new E { key = k, id = i };
            }
            var a = (E[])v.Clone(); StdAlgo.Sort(a, 0, n, lt); Out(sb, "sort", n, pat, a);
            var b = (E[])v.Clone(); StdAlgo.StableSort(b, 0, n, lt); Out(sb, "stable", n, pat, b);
            var c = (E[])v.Clone(); StdAlgo.PartialSort(c, 0, n, n, lt); Out(sb, "heap", n, pat, c);
        }
        var o = Console.OpenStandardOutput(); var bytes = Encoding.ASCII.GetBytes(sb.ToString()); o.Write(bytes, 0, bytes.Length);
    }
}
