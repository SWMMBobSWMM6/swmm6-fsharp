// bcheck.cpp - the members of part B of the Fortran runtime that sortcheck, hashcheck and containercheck leave out,
// in C++ (libstdc++), for comparison with bcheck.f90: unordered_map with an engine-style hasher and key-equal
// (case-insensitive, as openswmm::CiHash / CiEqual) - its order, copy, assignment, find / count / contains and
// erase; unordered_map from an initializer list; std::map and std::set copies, assignment, find and empty;
// std::queue copies; an unordered_set of pointers (sizes only: the order of an identity hash is the address);
// std::find / find_if / any_of / all_of / none_of over strings and structs, lower_bound with a comparator over
// structs, vector from an iterator range, std::copy, fill, fill_n, copy_n over pointers, transform with toupper,
// any_of over bytes and bools, and keys that stay the container's own after the caller's string changes.
// Written by Claude (Anthropic) for the SWMM6 ports; see AI-USE.md.
#include <algorithm>
#include <cctype>
#include <cstdint>
#include <cstdio>
#include <map>
#include <numeric>
#include <queue>
#include <set>
#include <string>
#include <unordered_map>
#include <unordered_set>
#include <vector>

struct CiHash {
    size_t operator()(const std::string& s) const {
        std::string t = s;
        for (auto& c : t) c = (char)std::tolower((unsigned char)c);
        return std::hash<std::string>{}(t);
    }
};
struct CiEqual {
    bool operator()(const std::string& a, const std::string& b) const {
        if (a.size() != b.size()) return false;
        for (size_t i = 0; i < a.size(); i++)
            if (std::tolower((unsigned char)a[i]) != std::tolower((unsigned char)b[i])) return false;
        return true;
    }
};
struct Day { double day; int id; };
static uint64_t s = 777;
static uint64_t rnd() { s ^= s << 13; s ^= s >> 7; s ^= s << 17; return s; }
static const char* pre[] = {"Node", "NODE", "node", "Link", "LINK", "Out", "out", "RG", "rg", "X"};

template <class M> static void dumpci(const char* tag, const M& m) {
    std::printf("%s %zu %zu:", tag, m.bucket_count(), m.size());
    for (auto& kv : m) std::printf(" %s=%d", kv.first.c_str(), kv.second);
    std::printf("\n");
}

int main() {
    // unordered_map with the engine's hasher and key-equal
    for (int round = 0; round < 6; round++) {
        std::unordered_map<std::string, int, CiHash, CiEqual> m;
        int n = 1 + (int)(rnd() % (round < 3 ? 40 : 900));
        for (int i = 0; i < n; i++) {
            uint64_t r = rnd();
            std::string k = std::string(pre[r % 10]) + std::to_string((r >> 8) % 300);
            if (r % 3 == 0) m[k] += i; else m.emplace(k, i);
            if (r % 7 == 0) m.erase(m.begin());
        }
        dumpci("ci", m);
        auto c = m;
        dumpci("ci copy", c);
        std::unordered_map<std::string, int, CiHash, CiEqual> a;
        a["zz"] = 1;
        a = m;
        dumpci("ci assign", a);
        std::printf("ci find:");
        for (const char* q : {"node1", "NODE17", "Link5", "x0", "nothing", "OUT12", "rg299"}) {
            auto it = m.find(q);
            std::printf(" %s:%s/%zu/%d", q, it == m.end() ? "end" : it->first.c_str(), m.count(q), (int)m.contains(q));
        }
        std::printf("\n");
    }
    // unordered_map from an initializer list (the first of equal keys stays)
    {
        std::unordered_map<int, int> m = {{3, 30}, {1, 10}, {3, 99}, {7, 70}, {-2, 5}, {14, 140}, {1, 11}};
        std::printf("il %zu %zu:", m.bucket_count(), m.size());
        for (auto& kv : m) std::printf(" %d=%d", kv.first, kv.second);
        std::unordered_map<int, int> b;
        b[5] = 1;
        b = m;
        b[8] = 80;
        std::printf(" | assign %zu %zu:", b.bucket_count(), b.size());
        for (auto& kv : b) std::printf(" %d=%d", kv.first, kv.second);
        std::printf("\n");
    }
    // std::map, std::set: copies, assignment, find, empty; keys that stay the container's own
    {
        std::map<std::string, int> m;
        std::printf("map empty %d\n", (int)m.empty());
        std::string k = "b";
        m[k] = 1;
        k += "x";                       // the map holds its own copy of "b"
        m.emplace(k, 2);
        m.emplace("a", 3);
        m.emplace("b", 4);               // present: nothing changes
        auto c = m;
        c["c"] = 5;
        std::map<std::string, int> a;
        a["q"] = 9;
        a = c;
        a["a"] = 7;
        std::printf("map:");
        for (auto& kv : m) std::printf(" %s=%d", kv.first.c_str(), kv.second);
        std::printf(" | copy:");
        for (auto& kv : c) std::printf(" %s=%d", kv.first.c_str(), kv.second);
        std::printf(" | assign:");
        for (auto& kv : a) std::printf(" %s=%d", kv.first.c_str(), kv.second);
        auto f = m.find("bx");
        std::printf(" | find %s %d %d\n", f == m.end() ? "end" : f->first.c_str(), (int)(m.find("zz") == m.end()), (int)m.empty());
        std::set<std::string> st;
        for (const char* w : {"pear", "apple", "fig", "apple", "kiwi"}) st.insert(w);
        auto sc = st;
        sc.insert("date");
        std::set<std::string> sa;
        sa.insert("zz");
        sa = sc;
        std::printf("set:");
        for (auto& x : st) std::printf(" %s", x.c_str());
        std::printf(" | copy:");
        for (auto& x : sc) std::printf(" %s", x.c_str());
        std::printf(" | assign:");
        for (auto& x : sa) std::printf(" %s", x.c_str());
        std::printf("\n");
    }
    // std::queue copies
    {
        std::queue<int> q;
        for (int i = 0; i < 200; i++) { q.push(i * 3); if (i % 3 == 2) q.pop(); }
        auto c = q;
        std::queue<int> a;
        a.push(-1);
        a = q;
        q.pop();
        std::printf("queue %zu %d | copy %zu %d %d | assign %zu %d\n", q.size(), q.front(), c.size(), c.front(), c.back(), a.size(), a.front());
    }
    // an unordered_set of pointers: identity (sizes and membership only)
    {
        std::vector<int> objs(50);
        std::unordered_set<const int*> us;
        int added = 0;
        for (int i = 0; i < 200; i++) if (us.insert(&objs[(i * 7) % 50]).second) added++;
        std::printf("ptrset %zu %d %zu %zu\n", us.size(), added, us.count(&objs[3]), us.count(nullptr));
    }
    // fill over bytes, floats, bools, ints; any_of and accumulate over a double array's begin / end
    {
        char cb[6] = "abcde";
        std::fill(cb + 1, cb + 3, 'z');
        float fl[4] = {1, 2, 3, 4};
        std::fill(fl + 2, fl + 4, 0.5f);
        bool bz[4] = {false, false, false, false};
        std::fill(bz + 1, bz + 2, true);
        int iv[5] = {1, 2, 3, 4, 5};
        std::fill(iv, iv + 2, -9);
        double dd[5] = {0.5, 1.5, -2.0, 3.0, 4.0};
        std::printf("fills %s %.1f %.1f %d%d%d%d %d %d any %d %d sum %.1f n %td\n", cb, fl[2], fl[1], (int)bz[0], (int)bz[1],
                    (int)bz[2], (int)bz[3], iv[0], iv[2], (int)std::any_of(std::begin(dd), std::end(dd), [](double x) { return x < -1; }),
                    (int)std::any_of(std::begin(dd), std::end(dd), [](double x) { return x > 5; }),
                    std::accumulate(std::begin(dd), std::end(dd), 0.0), std::end(dd) - std::begin(dd));
    }
    // algorithms
    {
        std::vector<std::string> v = {"alpha", "beta", "", "gamma", "beta", "delta"};
        auto f = std::find(v.begin(), v.end(), std::string("beta")) - v.begin();
        auto g = std::find(v.begin(), v.end(), std::string("omega")) - v.begin();
        auto h = std::find_if(v.begin(), v.end(), [](const std::string& x) { return x.size() == 5 && x[0] == 'g'; }) - v.begin();
        std::printf("strs find %td %td find_if %td any %d all %d none %d\n", f, g, h,
                    (int)std::any_of(v.begin(), v.end(), [](const std::string& x) { return x.empty(); }),
                    (int)std::all_of(v.begin(), v.end(), [](const std::string& x) { return x.size() < 6; }),
                    (int)std::none_of(v.begin() + 3, v.end(), [](const std::string& x) { return x == "alpha"; }));
        std::vector<std::string> w(v.begin() + 1, v.begin() + 4);
        v[1] = "changed";
        std::printf("vecfrom %zu:", w.size());
        for (auto& x : w) std::printf(" [%s]", x.c_str());
        std::printf("\n");
        std::vector<Day> days;
        for (int i = 0; i < 40; i++) days.push_back({(double)(i / 3) * 1.5, i});
        std::printf("days:");
        for (double q : {-1.0, 0.0, 1.5, 2.0, 7.5, 18.0, 30.0}) {
            auto it = std::lower_bound(days.begin(), days.end(), q, [](const Day& d, double x) { return d.day < x; });
            std::printf(" %td", it - days.begin());
        }
        std::printf("\n");
        std::vector<double> a(10), b(10, -1.0);
        for (int i = 0; i < 10; i++) a[i] = i * 0.25;
        auto e = std::copy(a.begin() + 2, a.begin() + 7, b.begin() + 1) - b.begin();
        std::fill(a.begin() + 8, a.end(), 9.5);
        double raw[12] = {0};
        double* p = std::fill_n(raw + 2, 3, 4.25);
        double* p2 = std::copy_n(a.data() + 6, 4, raw + 6);
        std::fill(raw, raw + 2, -7.0);
        std::vector<double> vd(a.begin(), a.begin() + 4);
        std::printf("copy %td fill_n %td copy_n %td:", e, p - raw, p2 - raw);
        for (double x : b) std::printf(" %.2f", x);
        std::printf(" |");
        for (double x : a) std::printf(" %.2f", x);
        std::printf(" |");
        for (double x : raw) std::printf(" %.2f", x);
        std::printf(" | %zu %.2f\n", vd.size(), vd[3]);
        std::string t = "Junction_17-out";
        std::string u(t.size(), '.');
        std::transform(t.begin(), t.end(), u.begin(), ::toupper);
        std::transform(t.begin(), t.begin() + 4, t.begin(), [](unsigned char c) { return (unsigned char)(c + 1); });
        bool zs[5] = {false, false, true, false, false};
        std::printf("transform %s %s any %d %d %d\n", u.c_str(), t.c_str(),
                    (int)std::any_of(t.begin(), t.end(), [](unsigned char c) { return c == '-'; }),
                    (int)std::any_of(std::begin(zs), std::end(zs), [](bool x) { return x; }),
                    (int)std::any_of(std::begin(zs), std::begin(zs) + 2, [](bool x) { return x; }));
    }
}
