// streamcheck.cpp - libstdc++'s iostreams on the cases the engine's streams depend on: operator<< of numbers
// with precision / fixed / scientific / hexfloat / showpoint, width, fill and adjustment, quoted strings, and the
// extraction side (>> double, int, string, std::ws, getline, read, peek/get/ignore, seekg/tellg) on string and
// file streams. The C# (streamcheck.cs, cs/runtime/Streams.cs) and Go (streamcheck_go, go/cpp/streams.go) programs
// must print the same bytes.
// Written by Claude (Anthropic); see AI-USE.md.
#include <cstdint>
#include <cstring>
#include <fstream>
#include <iomanip>
#include <iostream>
#include <sstream>
#include <string>

static double bits(uint64_t u) { double d; std::memcpy(&d, &u, 8); return d; }

static std::string st(std::ios& s) {
    std::string r;
    r += s.good() ? 'g' : '-';
    r += s.eof() ? 'e' : '-';
    r += s.fail() ? 'f' : '-';
    r += s.bad() ? 'b' : '-';
    return r;
}

// section a: what all three programs print alike; section c: ios::cur on a file
int main(int argc, char** argv) {
    std::string sec = argc > 1 ? argv[1] : "a";
    if (sec == "c") {
        std::ifstream f("build/streamcheck.bin", std::ios::binary);
        int32_t b = 0;
        f.read((char*)&b, 4);
        f.seekg(4, std::ios::cur);
        f.read((char*)&b, 4);
        std::cout << "cur at8 " << b << " tellg " << f.tellg() << ' ' << st(f) << '\n';
        return 0;
    }
    std::ostringstream out;
    const double vals[] = {0.0, bits(0x8000000000000000ULL), 1.0, -1.0, 0.1, 0.5, 1.5, 2.5, 123.456, 1e-5,
        1.23456789e-7, 1e21, 1.7976931348623157e308, bits(1), 2.2250738585072014e-308, 1.0 / 3.0, 2.0 / 3.0, 100.0,
        1e15, 123456789012345678.0, 0.000123456, 9.999999, 99.995, 0.125, bits(0x7ff8000000000000ULL),
        bits(0x7ff0000000000000ULL), bits(0xfff0000000000000ULL), 1e100, 5e-310, 3.14159265358979, -0.0004567,
        1234567.0, 0.95, 0.05, 1e-300, 6.02214076e23};
    const int precs[] = {0, 1, 2, 3, 4, 6, 8, 10, 15, 16, 17, 20, -1};
    for (double v : vals) {
        for (int mode = 0; mode < 4; mode++)
            for (int sp = 0; sp < 2; sp++)
                for (int p : precs) {
                    if (mode == 3 && p != 6) continue; // hexfloat ignores the precision
                    std::ostringstream o;
                    if (mode == 1) o << std::fixed;
                    if (mode == 2) o << std::scientific;
                    if (mode == 3) o.setf(std::ios::fixed | std::ios::scientific, std::ios::floatfield);
                    if (sp) o << std::showpoint;
                    o.precision(p);
                    o << v;
                    out << "d " << mode << sp << ' ' << p << " [" << o.str() << "]\n";
                }
        std::ostringstream f;
        f << (float)v;
        out << "f [" << f.str() << "]\n";
    }
    // width, fill, adjustment
    {
        std::ostringstream o;
        o << '[' << std::setw(10) << 3.5 << "][" << std::left << std::setw(8) << std::setfill('*') << -42 << ']';
        o << '[' << std::right << std::setw(6) << "ab" << "][" << std::setw(2) << "abcdef" << ']';
        o << '[' << std::setw(5) << std::setfill('0') << 7u << "][" << 123 << ']';
        o << std::boolalpha << '[' << true << ' ' << false << ']' << std::noboolalpha << '[' << true << false << ']';
        o << '[' << std::setw(12) << std::setfill(' ') << std::quoted(std::string("a\"b\\c")) << ']';
        o << '[' << std::quoted("plain") << ']';
        o << '[' << std::fixed << std::setprecision(3) << std::setw(9) << 2.0 / 3.0 << ']';
        o << '[' << std::defaultfloat << std::setprecision(4) << 2.0 / 3.0 << ']';
        o << '[' << (long long)-9223372036854775807LL - 1 << ' ' << 18446744073709551615ULL << ' ' << (short)-5 << ']';
        o << '[' << 'x' << (signed char)65 << ']';
        out << "w " << o.str() << '\n';
    }
    // ostringstream(init) overwrites from the start; str() is every byte
    {
        std::ostringstream o(std::string("abcdef"));
        o << "XY";
        out << "init [" << o.str() << "] tellp " << o.tellp() << '\n';
        std::ostringstream q;
        q << "hello world";
        q.seekp(6);
        q << "W";
        q.seekp(-1, std::ios::end);
        q << "D";
        q.seekp(2, std::ios::beg);
        q.seekp(1, std::ios::cur);
        q << "L";
        out << "seekp [" << q.str() << "] tellp " << q.tellp() << '\n';
    }
    // extraction from a string
    {
        std::istringstream is("  3.25 -7 abc\nline two\n\nlast");
        double d = -1; int i = -1; std::string s;
        is >> d; out << "x d " << d << ' ' << st(is) << '\n';
        is >> i; out << "x i " << i << ' ' << st(is) << '\n';
        is >> s; out << "x s " << s << ' ' << st(is) << '\n';
        std::string line;
        for (int k = 0; k < 4; k++) {
            std::getline(is, line);
            out << "x line [" << line << "] " << st(is) << '\n';
        }
    }
    {
        std::istringstream is("12 4294967296 -2147483648 2147483648 -9223372036854775808 9000000000000000000 4294967295 7");
        int a = -1, b = -1, c = -1, e = -1;
        is >> a >> b;
        out << "x ints " << a << ' ' << b << ' ' << st(is) << '\n';
        is.clear();
        is >> c >> e;
        out << "x ints " << c << ' ' << e << ' ' << st(is) << '\n';
        is.clear();
        long long L = -1;
        is >> L;
        out << "x long " << L << ' ' << st(is) << '\n';
        is.clear();
        unsigned long long U = 0; unsigned u = 0;
        is >> U;
        out << "x ulong " << U << ' ' << st(is) << '\n';
        is.clear();
        is >> u;
        out << "x uint " << u << ' ' << st(is) << '\n';
    }
    {
        std::istringstream is("a,b,,c");
        std::string f;
        while (std::getline(is, f, ',')) out << "x field [" << f << "] " << st(is) << '\n';
        out << "x field end " << st(is) << '\n';
    }
    {
        std::istringstream is("   \t\n  tail");
        is >> std::ws;
        std::string rest;
        std::getline(is, rest);
        out << "x ws [" << rest << "] " << st(is) << '\n';
        std::istringstream e("   ");
        e >> std::ws;
        out << "x ws-end " << st(e) << '\n';
    }
    {
        std::istringstream is("0123456789");
        char buf[16] = {0};
        is.read(buf, 4);
        out << "r [" << std::string(buf, 4) << "] gc " << is.gcount() << ' ' << st(is) << " tellg " << is.tellg() << '\n';
        out << "r peek " << is.peek() << " get " << is.get() << " get " << is.get() << ' ' << st(is) << '\n';
        is.ignore(3, '8');
        out << "r ignore tellg " << is.tellg() << ' ' << st(is) << '\n';
        is.seekg(-2, std::ios::cur);
        out << "r seekg cur tellg " << is.tellg() << " get " << is.get() << '\n';
        is.seekg(-3, std::ios::end);
        out << "r seekg end tellg " << is.tellg() << " get " << is.get() << '\n';
        is.read(buf, 10);
        out << "r short gc " << is.gcount() << ' ' << st(is) << '\n';
        is.clear();
        is.seekg(0, std::ios::beg);
        out << "r rewind tellg " << is.tellg() << " get " << is.get() << ' ' << st(is) << '\n';
        is.ignore(100, -1);
        out << "r ignore-all " << st(is) << '\n';
        out << "r get-end " << is.get() << ' ' << st(is) << '\n';
        out << "r str [" << is.str() << "]\n";
    }
    // file streams: binary write, append, read back, seekg from the beginning and the end
    {
        {
            std::ofstream f("build/streamcheck.bin", std::ios::trunc | std::ios::binary);
            out << "f open " << f.is_open() << ' ' << st(f) << '\n';
            int32_t a[3] = {1, -2, 300000};
            double d[2] = {1.5, -0.25};
            f.write((const char*)a, sizeof a);
            f.write((const char*)d, sizeof d);
            f.write("MAGIC", 5);
            out << "f tellp " << f.tellp() << '\n';
            f << 3.25 << ' ' << 42 << '\n';
            out << "f tellp " << f.tellp() << '\n';
            f.close();
            out << "f closed " << f.is_open() << '\n';
        }
        {
            std::ofstream f("build/streamcheck.bin", std::ios::app);
            f << "tail\n";
        }
        {
            std::ifstream f("build/streamcheck.bin", std::ios::binary);
            out << "f in open " << f.is_open() << ' ' << st(f) << '\n';
            int32_t a[3] = {0, 0, 0};
            f.read((char*)a, sizeof a);
            out << "f ints " << a[0] << ' ' << a[1] << ' ' << a[2] << " gc " << f.gcount() << " tellg " << f.tellg() << '\n';
            double d[2] = {0, 0};
            f.read((char*)d, sizeof d);
            out << "f dbl " << d[0] << ' ' << d[1] << " tellg " << f.tellg() << '\n';
            char m[6] = {0};
            f.read(m, 5);
            out << "f magic " << m << '\n';
            double x = 0; int n = 0; std::string t;
            f >> x >> n >> t;
            out << "f text " << x << ' ' << n << ' ' << t << ' ' << st(f) << '\n';
            f.seekg(-5, std::ios::end);
            std::getline(f, t);
            out << "f end-5 [" << t << "] " << st(f) << '\n';
            std::getline(f, t);
            out << "f after " << st(f) << '\n';
            f.clear();
            f.seekg(0, std::ios::end);
            out << "f size " << f.tellg() << '\n';
            f.seekg(4, std::ios::beg);
            int32_t b = 0;
            f.read((char*)&b, 4);
            out << "f at4 " << b << " tellg " << f.tellg() << '\n';
            f.seekg(1000, std::ios::beg);
            f.read((char*)&b, 4);
            out << "f past-end gc " << f.gcount() << ' ' << st(f) << '\n';
        }
        {
            std::ifstream f("build/no-such-file.bin");
            out << "f missing open " << f.is_open() << ' ' << st(f) << '\n';
        }
    }
    std::cout << out.str();
    return 0;
}
