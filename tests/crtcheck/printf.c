// printf.c - glibc's snprintf on many format / argument pairs, one line per case:
//   hex(format) <TAB> argument types <TAB> arguments (hex bits / hex string) <TAB> buffer size <TAB> return value <TAB> hex(output)
// gocheck (printf) parses each line, formats the same arguments with go/cpp's R_snprintf and compares.
// Argument types: d double, i int, l long, s string, S NULL string.
// Written by Claude (Anthropic) for the SWMM6 ports; see AI-USE.md.
#include <stdint.h>
#include <stdio.h>
#include <string.h>
#include <math.h>
#include <limits.h>

static uint64_t s = 0x2545F4914F6CDD1DULL;
static uint64_t rnd(void) { s ^= s >> 12; s ^= s << 25; s ^= s >> 27; return s * 0x2545F4914F6CDD1DULL; }
static double bitsd(uint64_t b) { double d; memcpy(&d, &b, 8); return d; }
static uint64_t dbits(double d) { uint64_t b; memcpy(&b, &d, 8); return b; }

static void hex(const char *p, int n) { for (int i = 0; i < n; i++) printf("%02x", (unsigned char)p[i]); if (n == 0) printf("-"); }

static char out[1024];

static void emit(const char *fmt, const char *types, const char *args, int size, int r) {
    hex(fmt, (int)strlen(fmt)); printf("\t%s\t%s\t%d\t%d\t", types, args, size, r);
    int n = size == 0 ? 0 : (int)strlen(out);
    hex(out, n); printf("\n");
}

static void cd(const char *fmt, double v, int size) {
    char a[32]; snprintf(a, sizeof a, "%016llx", (unsigned long long)dbits(v));
    memset(out, 0x55, sizeof out); out[0] = 0;
    int r = snprintf(out, size, fmt, v); emit(fmt, "d", a, size, r);
}
static void ci(const char *fmt, int v) {
    char a[32]; snprintf(a, sizeof a, "%08x", (unsigned)v);
    int r = snprintf(out, sizeof out, fmt, v); emit(fmt, "i", a, sizeof out, r);
}
static void cl(const char *fmt, long v) {
    char a[32]; snprintf(a, sizeof a, "%016lx", (unsigned long)v);
    int r = snprintf(out, sizeof out, fmt, v); emit(fmt, "l", a, sizeof out, r);
}
static void cs(const char *fmt, const char *v) {
    char a[256]; int k = 0;
    for (const char *p = v; *p; p++) k += snprintf(a + k, sizeof a - k, "%02x", (unsigned char)*p);
    if (!*v) strcpy(a, "-");
    int r = snprintf(out, sizeof out, fmt, v); emit(fmt, "s", a, sizeof out, r);
}
static void cS(const char *fmt) {
    int r = snprintf(out, sizeof out, fmt, (char *)NULL); emit(fmt, "S", "-", sizeof out, r);
}
static void cid(const char *fmt, int w, double v) {
    char a[64]; snprintf(a, sizeof a, "%08x,%016llx", (unsigned)w, (unsigned long long)dbits(v));
    int r = snprintf(out, sizeof out, fmt, w, v); emit(fmt, "id", a, sizeof out, r);
}
static void ciid(const char *fmt, int w, int p, double v) {
    char a[64]; snprintf(a, sizeof a, "%08x,%08x,%016llx", (unsigned)w, (unsigned)p, (unsigned long long)dbits(v));
    int r = snprintf(out, sizeof out, fmt, w, p, v); emit(fmt, "iid", a, sizeof out, r);
}
static void cii(const char *fmt, int w, int v) {
    char a[64]; snprintf(a, sizeof a, "%08x,%08x", (unsigned)w, (unsigned)v);
    int r = snprintf(out, sizeof out, fmt, w, v); emit(fmt, "ii", a, sizeof out, r);
}

static double rdouble(int k) {
    switch (k % 8) {
    case 0: return bitsd(rnd());                                   // any bit pattern (NaNs, subnormals included)
    case 1: return (double)(int64_t)(rnd() % 2000001) / 1000.0 - 1000.0;
    case 2: return (double)(int64_t)(rnd() % 4001) / 16.0 - 125.0;  // exact binary fractions: ties at 1-4 decimals
    case 3: return bitsd((rnd() & 0x800FFFFFFFFFFFFFULL) | ((uint64_t)(1023 - 40 + rnd() % 80) << 52));
    case 4: return bitsd(rnd() & 0x800FFFFFFFFFFFFFULL);              // subnormals
    case 5: return (double)(rnd() % 100000) * 0.5;
    case 6: return bitsd((rnd() & 0x800FFFFFFFFFFFFFULL) | ((uint64_t)(1 + rnd() % 2046) << 52));
    default: return (double)(int64_t)(rnd() % 1000000) / 1e6;
    }
}

int main(void) {
    static const char *dfmt[] = { "%f", "%.0f", "%.1f", "%.2f", "%.3f", "%.4f", "%.6f", "%10.4f", "%-10.2f|", "%+.3e",
        "%e", "%.0e", "%#.0e", "%E", "%g", "%.3g", "%.6g", "%.9g", "%.10g", "%.17g", "% .9g", "%#g", "%G", "%a", "%A",
        "%.0a", "%.1a", "%.3a", "%.12a", "%.13a", "%.15a", "%#a", "%#.0a", "%20a", "%-20a|", "%020a", "%+a", "% a",
        "%010.3f", "%-+12.5e|", "%012g", "%7.2f", "%8.4f", "%.6e", "%#.3g", "%.0g", "%#.0f", "%+08.2f", "%-8.3g|",
        "%.20f", "%.30e", "x=%5.1f%%", "%lf", "%Lf" };
    int nd = sizeof dfmt / sizeof dfmt[0];
    static const double dspec[] = { 0.0, -0.0, 1.0, -1.0, 0.5, 1.5, 2.5, -2.5, 0.125, 0.375, 1e-5, 9.9999995e-5,
        99999.5, 999999.5, 1e15, 1e16, 1e17, 1e21, 1e22, 1e23, 123456789.0, 0.1, 0.2, 0.3, 1.0 / 3.0, 2.0 / 3.0,
        4.9e-324, 2.2250738585072014e-308, 2.225073858507201e-308, 1.7976931348623157e308, INFINITY, -INFINITY,
        NAN, -NAN, 0x1.fffffffffffffp+0, 0x1.f8p+0, 0x1.08p+0, 0x1.18p+0, 0x1.0000000000008p+0, 9.5, 0.95, 0.05 };
    int ns = sizeof dspec / sizeof dspec[0];
    for (int f = 0; f < nd; f++) {
        if (strcmp(dfmt[f], "%Lf") == 0) continue;   // long double: not the engine's
        for (int k = 0; k < ns; k++) cd(dfmt[f], dspec[k], sizeof out);
        for (int k = 0; k < 3000; k++) cd(dfmt[f], rdouble(k), sizeof out);
    }
    // truncation by the buffer size
    for (int z = 0; z < 12; z++) cd("value %10.4f end", 3.14159, z);

    static const char *ifmt[] = { "%d", "%5d", "%-5d|", "%05d", "%+d", "% d", "%.3d", "%.0d", "%u", "%x", "%X",
        "%#x", "%#X", "%#o", "%o", "%08x", "%c", "%3c|", "%-3c|", "%i", "%+.4i", "%hd", "%hhd", "%hu", "%hhu",
        "%#.0o", "%.0x", "%-08d|", "% 05d", "%%%d%%" };
    int ni = sizeof ifmt / sizeof ifmt[0];
    static const int ispec[] = { 0, 1, -1, 7, -7, 42, 65, 255, 256, 32767, 32768, -32768, 65535, 99999, 100000,
        INT_MAX, INT_MIN, 1000000, -1000000, 0x7FFF, 0x12345678 };
    for (int f = 0; f < ni; f++) {
        for (unsigned k = 0; k < sizeof ispec / sizeof ispec[0]; k++) ci(ifmt[f], ispec[k]);
        for (int k = 0; k < 300; k++) ci(ifmt[f], (int)rnd());
    }
    static const char *lfmt[] = { "%ld", "%lu", "%llx", "%016llx", "%lld", "%llu", "%lx", "%20ld", "%-20ld|", "%zu",
        "%jd", "%+ld", "%lo" };
    int nl = sizeof lfmt / sizeof lfmt[0];
    static const long lspec[] = { 0, 1, -1, LONG_MAX, LONG_MIN, 4294967296L, -4294967296L, 1234567890123L };
    for (int f = 0; f < nl; f++) {
        for (unsigned k = 0; k < sizeof lspec / sizeof lspec[0]; k++) cl(lfmt[f], lspec[k]);
        for (int k = 0; k < 300; k++) cl(lfmt[f], (long)rnd());
    }
    static const char *sfmt[] = { "%s", "%10s", "%-10s|", "%.3s", "%5.2s|", "%.0s|", "%-44s|", "[%s]", "%.10s", "%010s" };
    static const char *sval[] = { "", "a", "hello", "hello world", "Conduit C1", "  spaced  ", "0123456789abcdef" };
    for (unsigned f = 0; f < sizeof sfmt / sizeof sfmt[0]; f++) {
        for (unsigned k = 0; k < sizeof sval / sizeof sval[0]; k++) cs(sfmt[f], sval[k]);
        cS(sfmt[f]);
    }
    cS("%.5s|"); cS("%.6s|"); cS("%7s|"); cS("%-8.2s|");
    for (int k = 0; k < 400; k++) {
        int w = (int)(rnd() % 41) - 20, p = (int)(rnd() % 25) - 5;
        double v = rdouble(k);
        cid("%*f|", w, v); cid("%-*g|", w, v); cid("%.*f|", p, v); cid("%.*e|", p, v); cid("%.*g|", p, v);
        cid("%*a|", w, v); cid("%.*a|", p % 14, v);
        ciid("%*.*f|", w, p, v); ciid("%*.*g|", w, p, v);
        cii("%*d|", w, (int)rnd()); cii("%.*d|", p, (int)rnd() % 1000); cii("%*c|", w, 'A' + k % 26);
    }
    return 0;
}
