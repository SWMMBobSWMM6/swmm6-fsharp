// hashcheck.cpp - libstdc++'s unordered_map / unordered_set: bucket counts and iteration order after inserts,
// operator[], erases, reserve and copies; std::hash of strings. Compared with cs/runtime/Containers.cs by hashcheck.cs.
// Written by Claude (Anthropic); see AI-USE.md.
#include <cstdio>
#include <cstdint>
#include <string>
#include <unordered_map>
#include <unordered_set>
#include <vector>
static uint64_t s = 52024;
static uint64_t rnd() { s ^= s << 13; s ^= s >> 7; s ^= s << 17; return s; }
static std::string name(uint64_t r) {
    static const char* pre[] = {"J", "C", "N", "OF", "P", "RG", "SUB_", "Link-", "node.", "W"};
    std::string x = pre[r % 10];
    x += std::to_string((r >> 8) % 5000);
    if ((r >> 20) % 3 == 0) x += "_" + std::to_string((r >> 24) % 97);
    return x;
}
int main() {
    for (int round = 0; round < 40; round++) {
        std::unordered_map<std::string, int> m;
        std::unordered_map<int, int> mi;
        std::unordered_set<std::string> st;
        int n = 1 + (int)(rnd() % (round < 20 ? 60 : 3000));
        if (round % 7 == 3) m.reserve(n);
        for (int i = 0; i < n; i++) {
            uint64_t r = rnd();
            std::string k = name(r);
            if (r % 5 == 0) m[k] += i; else m.emplace(k, i);
            mi.emplace((int)(r % 20000) - 10000, i);
            st.insert(k);
            if (r % 11 == 0 && !m.empty()) m.erase(name(rnd()));
            if (r % 13 == 0) mi.erase((int)(rnd() % 20000) - 10000);
            if (r % 17 == 0 && !m.empty()) m.erase(m.begin());
        }
        auto c = m;
        std::printf("map %d: %zu buckets %zu:", round, m.bucket_count(), m.size());
        for (auto& kv : m) std::printf(" %s=%d", kv.first.c_str(), kv.second);
        std::printf("\ncopy:");
        for (auto& kv : c) std::printf(" %s", kv.first.c_str());
        std::printf("\nimap %zu %zu:", mi.bucket_count(), mi.size());
        for (auto& kv : mi) std::printf(" %d=%d", kv.first, kv.second);
        std::printf("\nset %zu:", st.bucket_count());
        for (auto& k : st) std::printf(" %s", k.c_str());
        std::printf("\n");
    }
    for (const char* w : {"", "a", "abcdefg", "abcdefgh", "abcdefghi", "Junction_17", "OUTFALL-1"})
        std::printf("hash %s %zu\n", w, std::hash<std::string>{}(w));
}
