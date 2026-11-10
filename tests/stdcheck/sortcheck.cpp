// sortcheck.cpp - libstdc++'s std::sort, std::stable_sort and std::partial_sort on keys with many ties; prints
// the permutation each leaves, for comparison with cs/runtime/StdAlgo.cs (sortcheck.cs).
// Written by Claude (Anthropic); see AI-USE.md.
#include <algorithm>
#include <cstdio>
#include <cstdint>
#include <vector>
struct E { int key, id; };
static uint64_t s = 1729;
static uint64_t rnd() { s ^= s << 13; s ^= s >> 7; s ^= s << 17; return s; }
static void out(const char* tag, int n, int pat, const std::vector<E>& v) {
    std::printf("%s %d %d:", tag, n, pat);
    for (auto& e : v) std::printf(" %d", e.id);
    std::printf("\n");
}
int main() {
    std::vector<int> ns;
    for (int n = 0; n <= 300; n++) ns.push_back(n);
    for (int n : {511, 512, 513, 1000, 4096, 10007}) ns.push_back(n);
    auto lt = [](const E& a, const E& b) { return a.key < b.key; };
    for (int n : ns) for (int pat = 0; pat < 7; pat++) {
        std::vector<E> v(n);
        for (int i = 0; i < n; i++) {
            int k;
            switch (pat) {
            case 0: k = (int)(rnd() % 5); break;            // many ties
            case 1: k = (int)(rnd() % 1000000); break;      // few ties
            case 2: k = i / 3; break;                       // sorted with runs
            case 3: k = (n - i) / 2; break;                 // reversed with runs
            case 4: k = 7; break;                           // all equal
            case 5: k = (i < n / 2) ? i : n - i; break;     // organ pipe
            default: k = (int)((i * 7919) % 31); break;     // sawtooth
            }
            v[i] = {k, i};
        }
        auto a = v; std::sort(a.begin(), a.end(), lt); out("sort", n, pat, a);
        auto b = v; std::stable_sort(b.begin(), b.end(), lt); out("stable", n, pat, b);
        auto c = v; std::partial_sort(c.begin(), c.end(), c.end(), lt); out("heap", n, pat, c);
    }
}
