// misc.cpp - libstdc++ and glibc results for the rest of go/cpp's C runtime: std::from_chars (int, unsigned,
// long, double), strtol / atol / atoi / atof, ctime, get_nprocs (std::thread::hardware_concurrency) and the
// ctype functions. One line per case: kind <TAB> hex(input) <TAB> result. gocheck (misc) recomputes each.
// Written by Claude (Anthropic) for the SWMM6 ports; see AI-USE.md.
#include <charconv>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <ctime>
#include <cctype>
#include <thread>

static void hex(const char *p) { if (!*p) std::printf("-"); for (; *p; p++) std::printf("%02x", (unsigned char)*p); }

int main() {
    const char *in[] = { "12", "-12", "+12", " 12", "\t12", "", "-", "abc", "12abc", "2147483647", "2147483648",
        "-2147483648", "-2147483649", "4294967295", "4294967296", "9223372036854775807", "9223372036854775808",
        "-9223372036854775808", "-9223372036854775809", "18446744073709551615", "18446744073709551616",
        "99999999999999999999999", "007", "-0", "1.5", "1e3", "-1.25e-3", ".5", "5.", "inf", "-inf", "nan",
        "infinity", "1e400", "1e-400", "0x10", "3.14159 rest", "  -7  ", "\n5", "1,5", "--5", "+-5", "0.1",
        "123456789012345678901234567890", "2.2250738585072014e-308", "4.9e-324", "-0x10", "-.5", "-x", "1e-320",
        "2e-324", "3e-324", "-1e400", "  ", "0e999", "0.0e-999", "-nan", "1e", "1e+", "0X1p3", "00x1" };
    for (const char *s : in) {
        const char *e = s + std::strlen(s);
        { int v = 0x55555555; auto r = std::from_chars(s, e, v);
          std::printf("fci\t"); hex(s); std::printf("\t%d %ld %d\n", (int)r.ec, (long)(r.ptr - s), v); }
        { unsigned v = 0x55555555; auto r = std::from_chars(s, e, v);
          std::printf("fcu\t"); hex(s); std::printf("\t%d %ld %u\n", (int)r.ec, (long)(r.ptr - s), v); }
        { long v = 0x5555555555555555; auto r = std::from_chars(s, e, v);
          std::printf("fcl\t"); hex(s); std::printf("\t%d %ld %ld\n", (int)r.ec, (long)(r.ptr - s), v); }
        { double v = 0.25; auto r = std::from_chars(s, e, v); uint64_t b; std::memcpy(&b, &v, 8);
          std::printf("fcd\t"); hex(s); std::printf("\t%d %ld %016llx\n", (int)r.ec, (long)(r.ptr - s), (unsigned long long)b); }
        { char *end; long v = std::strtol(s, &end, 10);
          std::printf("strtol\t"); hex(s); std::printf("\t%ld %ld\n", v, (long)(end - s)); }
        std::printf("atoi\t"); hex(s); std::printf("\t%d\n", std::atoi(s));
        std::printf("atol\t"); hex(s); std::printf("\t%ld\n", std::atol(s));
        { char *end; double v = std::strtod(s, &end); uint64_t b; std::memcpy(&b, &v, 8);
          std::printf("strtod\t"); hex(s); std::printf("\t%016llx %ld\n", (unsigned long long)b, (long)(end - s)); }
    }
    for (long t : { 0L, 86399L, 951782400L, 1700000000L, 2147483647L, 4102444800L }) {
        char buf[32]; std::snprintf(buf, sizeof buf, "%ld", t);
        std::printf("ctime\t"); hex(buf); std::printf("\t"); hex(std::ctime(&t)); std::printf("\n");
    }
    std::printf("nprocs\t-\t%u\n", std::thread::hardware_concurrency());
    for (int c = -1; c < 256; c++) {
        char buf[8]; std::snprintf(buf, sizeof buf, "%d", c);
        std::printf("ctype\t"); hex(buf); std::printf("\t%d%d%d%d%d%d%d%d%d %d %d\n", !!isdigit(c), !!isalpha(c),
            !!isalnum(c), !!isspace(c), !!isupper(c), !!islower(c), !!ispunct(c), !!isprint(c), !!isxdigit(c),
            toupper(c), tolower(c));
    }
    return 0;
}
