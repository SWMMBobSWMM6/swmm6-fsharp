// containercheck.cpp - what hashcheck.cpp and sortcheck.cpp leave out, for comparison with the TypeScript runtime
// (containercheck.mts): unordered_map with 64-bit keys (unsigned keys above 2^53, negative long long keys), std::hash
// of integers, unordered_set reserve / rehash / clear / erase while iterating, std::map and std::set order and
// bounds (string keys, pair keys), std::stack / std::queue, partial_sort with middle < last, sort and stable_sort of
// subranges, std::sort with std::less over doubles with signed zeros and over pair<int, string>, and the
// <algorithm> / <numeric> functions the runtime's Algo class provides.
// Written by Claude (Anthropic) for the SWMM6 ports; see AI-USE.md.
#include <algorithm>
#include <climits>
#include <cstdint>
#include <cstdio>
#include <functional>
#include <map>
#include <numeric>
#include <queue>
#include <set>
#include <stack>
#include <string>
#include <unordered_map>
#include <unordered_set>
#include <utility>
#include <vector>

static uint64_t s = 4242;
static uint64_t rnd() { s ^= s << 13; s ^= s >> 7; s ^= s << 17; return s; }
struct E { int key, id; };
static void ids(const char* tag, const std::vector<E>& v, size_t b, size_t e) {
    std::printf("%s:", tag);
    for (size_t i = b; i < e; i++) std::printf(" %d", v[i].id);
    std::printf("\n");
}
static void ints(const char* tag, const std::vector<int>& v, size_t n) {
    std::printf("%s %zu:", tag, n);
    for (size_t i = 0; i < n; i++) std::printf(" %d", v[i]);
    std::printf("\n");
}

int main() {
    // 64-bit keys
    for (int round = 0; round < 12; round++) {
        std::unordered_map<uint64_t, int> mu;
        std::unordered_map<long long, int> ml;
        std::vector<uint64_t> ku;
        std::vector<long long> kl;
        int n = 1 + (int)(rnd() % (round < 6 ? 50 : 2000));
        for (int i = 0; i < n; i++) {
            uint64_t r = rnd();
            uint64_t k = (r % 3 == 0) ? (r >> 40) : r;
            if (r % 4 == 0) mu[k] += i; else mu.emplace(k, i);
            ku.push_back(k);
            long long l = (r % 5 == 0) ? (long long)(r % 1000) - 500 : (long long)(r >> 1) * ((r & 2) ? -1 : 1);
            ml.emplace(l, i);
            kl.push_back(l);
            if (r % 9 == 0) mu.erase(ku[(r >> 8) % ku.size()]);
            if (r % 10 == 0) ml.erase(kl[(r >> 8) % kl.size()]);
            if (r % 23 == 0 && !mu.empty()) mu.erase(mu.begin());
        }
        std::printf("umap %d: %zu %zu:", round, mu.bucket_count(), mu.size());
        for (auto& kv : mu) std::printf(" %llu=%d", (unsigned long long)kv.first, kv.second);
        std::printf("\nlmap %zu %zu:", ml.bucket_count(), ml.size());
        for (auto& kv : ml) std::printf(" %lld=%d", kv.first, kv.second);
        std::printf("\n");
    }
    std::printf("hash %zu %zu %zu %zu %zu\n", std::hash<int>{}(-1), std::hash<long long>{}(LLONG_MIN),
                std::hash<unsigned long long>{}(ULLONG_MAX), std::hash<int>{}(12345), std::hash<long long>{}(-9007199254740993LL));

    // unordered_set: reserve, rehash, erase while iterating, clear
    for (int round = 0; round < 8; round++) {
        std::unordered_set<int> us;
        us.reserve(rnd() % 100);
        int n = (int)(rnd() % 400);
        for (int i = 0; i < n; i++) us.insert((int)(rnd() % 1000) - 500);
        std::printf("uset %d: %zu %zu:", round, us.bucket_count(), us.size());
        for (int x : us) std::printf(" %d", x);
        if (round % 2 == 0) us.rehash(us.size() * 3 + round);
        else us.reserve(us.size() / 2);
        std::printf("\nrehash %zu:", us.bucket_count());
        for (int x : us) std::printf(" %d", x);
        for (auto it = us.begin(); it != us.end();) { if (*it % 3 != 0) it = us.erase(it); else ++it; }
        std::printf("\nerased %zu %zu:", us.bucket_count(), us.size());
        for (int x : us) std::printf(" %d", x);
        auto cp = us;
        us.clear();
        for (int i = 0; i < 20; i++) us.insert((int)(rnd() % 50));
        std::printf("\ncleared %zu %zu:", us.bucket_count(), us.size());
        for (int x : us) std::printf(" %d", x);
        std::printf("\ncopy %zu:", cp.bucket_count());
        for (int x : cp) std::printf(" %d", x);
        std::printf("\n");
    }

    // std::map / std::set
    {
        static const char* pre[] = {"J", "c", "N", "OF", "_P", "rg", "SUB", "Link-", "node.", "W~"};
        std::map<std::string, int> ms;
        for (int i = 0; i < 300; i++) {
            uint64_t r = rnd();
            std::string k = std::string(pre[r % 10]) + std::to_string((r >> 8) % 60);
            if (r % 3 == 0) ms[k] += i; else ms.emplace(k, i);
            if (r % 7 == 0) ms.erase(std::string(pre[(r >> 4) % 10]) + std::to_string((r >> 16) % 60));
        }
        std::printf("smap %zu:", ms.size());
        for (auto& kv : ms) std::printf(" %s=%d", kv.first.c_str(), kv.second);
        std::printf("\nbounds:");
        for (const char* q : {"", "J", "J3", "Link-", "W~9", "zz", "node.5", "OF10"}) {
            auto lb = ms.lower_bound(q), ub = ms.upper_bound(q);
            std::printf(" %s:%s/%s", q, lb == ms.end() ? "end" : lb->first.c_str(), ub == ms.end() ? "end" : ub->first.c_str());
        }
        std::printf("\n");
        std::map<int, int> mi;
        for (int i = 0; i < 200; i++) { uint64_t r = rnd(); mi[(int)(r % 300) - 150] += i; if (r % 5 == 0) mi.erase((int)((r >> 9) % 300) - 150); }
        std::printf("imap %zu:", mi.size());
        for (auto& kv : mi) std::printf(" %d=%d", kv.first, kv.second);
        std::printf("\n");
        std::set<std::pair<long long, long long>> sp;
        for (int i = 0; i < 200; i++) { uint64_t r = rnd(); sp.insert({(long long)(r % 7) - 3, (long long)((r >> 8) % 1000) - 500}); }
        std::printf("pset %zu:", sp.size());
        for (auto& p : sp) std::printf(" (%lld,%lld)", p.first, p.second);
        std::printf("\n");
        std::set<std::string> ss;
        for (int i = 0; i < 100; i++) { uint64_t r = rnd(); ss.insert(std::string(pre[r % 10]) + std::to_string((r >> 8) % 20)); if (r % 4 == 0) ss.erase(std::string(pre[(r >> 3) % 10]) + std::to_string((r >> 12) % 20)); }
        std::printf("sset %zu:", ss.size());
        for (auto& x : ss) std::printf(" %s", x.c_str());
        std::printf("\n");
    }

    // std::stack / std::queue
    {
        std::stack<int> st;
        std::queue<int> qu;
        std::printf("stackqueue:");
        for (int i = 0; i < 500; i++) {
            uint64_t r = rnd();
            if (r % 3 != 0 || st.empty()) st.push((int)(r % 100)); else { std::printf(" s%d", st.top()); st.pop(); }
            if (r % 4 != 0 || qu.empty()) qu.push((int)(r % 100)); else { std::printf(" q%d/%d", qu.front(), qu.back()); qu.pop(); }
        }
        std::printf(" | %zu %zu\n", st.size(), qu.size());
    }

    // partial_sort with middle < last; sort and stable_sort of subranges
    auto lt = [](const E& a, const E& b) { return a.key < b.key; };
    for (int n : {0, 1, 2, 5, 16, 17, 33, 100, 257, 1000}) for (int pat = 0; pat < 2; pat++) {
        std::vector<E> v(n);
        for (int i = 0; i < n; i++) v[i] = {pat == 0 ? (int)(rnd() % 4) : (int)(rnd() % 100000), i};
        for (int m : {0, 1, n / 3, n / 2, n - 1, n}) {
            if (m < 0 || m > n) continue;
            auto c = v; std::partial_sort(c.begin(), c.begin() + m, c.end(), lt);
            std::printf("psort %d %d %d ", n, pat, m); ids("", c, 0, n);
        }
        auto a = v; std::sort(a.begin() + n / 4, a.begin() + (3 * n) / 4, lt);
        std::printf("subsort %d %d ", n, pat); ids("", a, 0, n);
        auto b = v; std::stable_sort(b.begin() + n / 4, b.begin() + (3 * n) / 4, lt);
        std::printf("substable %d %d ", n, pat); ids("", b, 0, n);
    }

    // std::sort with std::less: doubles with signed zeros and ties; pair<int, string>
    {
        std::vector<double> d;
        for (int i = 0; i < 300; i++) { uint64_t r = rnd(); double x = (double)((int)(r % 9) - 4) * 0.5; if (x == 0 && (r >> 8) % 2) x = -0.0; d.push_back(x); }
        std::sort(d.begin(), d.end());
        std::printf("dsort:");
        for (double x : d) std::printf(" %.1f", x);
        std::printf("\n");
        static const char* w[] = {"b", "a", "ab", "", "ba", "B", "aa"};
        std::vector<std::pair<int, std::string>> ps;
        for (int i = 0; i < 100; i++) { uint64_t r = rnd(); ps.push_back({(int)(r % 5), w[(r >> 8) % 7]}); }
        std::sort(ps.begin(), ps.end());
        std::printf("psort2:");
        for (auto& p : ps) std::printf(" %d%s", p.first, p.second.c_str());
        std::printf("\n");
    }

    // <algorithm> / <numeric>
    for (int round = 0; round < 6; round++) {
        int n = (int)(rnd() % 60);
        std::vector<int> v(n);
        for (int i = 0; i < n; i++) v[i] = (int)(rnd() % 10) - 3;
        auto mn = std::min_element(v.begin(), v.end()), mx = std::max_element(v.begin(), v.end());
        std::printf("algo %d: min %td max %td count %td find %td sorted %d sum %d\n", round, mn - v.begin(), mx - v.begin(),
                    std::count(v.begin(), v.end(), 2), std::find(v.begin(), v.end(), 4) - v.begin(),
                    (int)std::is_sorted(v.begin(), v.end()), std::accumulate(v.begin(), v.end(), 0));
        std::printf("pred %d %d %d %td %td %d\n", (int)std::any_of(v.begin(), v.end(), [](int x) { return x > 5; }),
                    (int)std::all_of(v.begin(), v.end(), [](int x) { return x > -3; }),
                    (int)std::none_of(v.begin(), v.end(), [](int x) { return x == 6; }),
                    std::count_if(v.begin(), v.end(), [](int x) { return x < 0; }),
                    std::adjacent_find(v.begin(), v.end()) - v.begin(), (int)std::equal(v.begin(), v.end(), v.begin()));
        auto w = v;
        std::replace(w.begin(), w.end(), 1, 7);
        std::reverse(w.begin(), w.end());
        ints("replrev", w, w.size());
        auto r = v;
        auto re = std::remove_if(r.begin(), r.end(), [](int x) { return x % 3 == 0; });
        ints("remove_if", r, re - r.begin());
        auto u = v;
        std::sort(u.begin(), u.end());
        std::printf("bounds:");
        for (int q = -4; q <= 7; q++)
            std::printf(" %d:%td/%td", q, std::lower_bound(u.begin(), u.end(), q) - u.begin(), std::upper_bound(u.begin(), u.end(), q) - u.begin());
        std::printf("\n");
        auto ue = std::unique(u.begin(), u.end());
        ints("unique", u, ue - u.begin());
        std::vector<unsigned long long> io(5);
        std::iota(io.begin(), io.end(), 18446744073709551613ULL);
        std::printf("iota:");
        for (auto x : io) std::printf(" %llu", x);
        std::printf("\n");
    }
}
