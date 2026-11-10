// specs.c - glibc's snprintf on every printf conversion spec the engine's format strings use (extracted from
// cs/gen: the R.fprintf / R.snprintf / R.sprintf literals), each with many values of the argument type the engine
// passes, in ../printf.c's line format, so crtcheck.py printf checks them the same way:
//   hex(format) <TAB> argument types <TAB> arguments (hex bits / hex string) <TAB> buffer size <TAB> return <TAB> hex(output)
// The values are the kinds a report holds: plain decimals, exact binary fractions (rounding ties), wide magnitudes,
// negative zero, and any bit pattern (NaNs and subnormals included).
// Written by Claude (Anthropic) for the SWMM6 ports; see AI-USE.md.
#include <stdint.h>
#include <stdio.h>
#include <string.h>

static uint64_t s = 0x9E3779B97F4A7C15ULL;
static uint64_t rnd(void) { s ^= s >> 12; s ^= s << 25; s ^= s >> 27; return s * 0x2545F4914F6CDD1DULL; }
static double bitsd(uint64_t b) { double d; memcpy(&d, &b, 8); return d; }
static uint64_t dbits(double d) { uint64_t b; memcpy(&b, &d, 8); return b; }
static void hex(const char *p, int n) { for (int i = 0; i < n; i++) printf("%02x", (unsigned char)p[i]); if (n == 0) printf("-"); }
static char out[1024];
static void emit(const char *fmt, const char *types, const char *args, int r) {
    hex(fmt, (int)strlen(fmt)); printf("\t%s\t%s\t%d\t%d\t", types, args, (int)sizeof out, r);
    hex(out, (int)strlen(out)); printf("\n");
}

static double value(int k) {
    switch (k % 10) {
    case 0: return bitsd(rnd());
    case 1: return (double)(int64_t)(rnd() % 20000001) / 1000.0 - 10000.0;
    case 2: return (double)(int64_t)(rnd() % 80001) / 64.0 - 625.0;          // ties at 1..6 decimals
    case 3: return bitsd((rnd() & 0x800FFFFFFFFFFFFFULL) | ((uint64_t)(1023 - 30 + rnd() % 60) << 52));
    case 4: return (double)(rnd() % 1000000) * 0.0005;
    case 5: return -(double)(rnd() % 1000) / 8.0;
    case 6: return (rnd() & 1) ? -0.0 : 0.0;
    case 7: return (double)(int64_t)(rnd() % 2000001 - 1000000) * 1e-9;
    case 8: return (double)(rnd() % 100000000) / 3.0;
    default: return bitsd((rnd() & 0x800FFFFFFFFFFFFFULL) | ((uint64_t)(1 + rnd() % 2046) << 52));
    }
}

int main(void) {
    static const char *dfmt[] = { "% .9g", "%-10f", "%.0f", "%.10g", "%.17g", "%.1f", "%.2f", "%.3f", "%.3g", "%.4f",
        "%.6f", "%.6g", "%.9f", "%10.1f", "%10.2f", "%10.3f", "%10.4f", "%11.4f", "%12.2f", "%12.3f", "%12.3g",
        "%12.6f", "%13.6f", "%14.1f", "%14.2f", "%14.3e", "%14.3f", "%14.4f", "%14.6f", "%4.2f", "%5.1f", "%6.1f",
        "%6.2f", "%6.3f", "%7.2f", "%8.1f", "%8.2f", "%8.3f", "%8.4f", "%9.2f", "%9.3f", "%a", "%f", "%g",
        "x=%a,%a\n" };
    static const char *ifmt[] = { "%-4d", "%02d", "%04d", "%10d", "%3d", "%4d", "%5d", "%6d", "%d" };
    static const char *lfmt[] = { "%02ld", "%6ld", "%14ld", "%ld", "%llu", "%zu", "%016llx" };
    static const char *sfmt[] = { "%-10s", "%-12s", "%-14s", "%-16s", "%-20s", "%-24s", "%-30s", "%-3s", "%-44s",
        "%-5s", "%-7s", "%-9s", "%-s", "%10s", "%11s", "%12s", "%14s", "%20s", "%2s", "%3s", "%5s", "%6s", "%8s",
        "%9s", "%s" };
    static const char *sval[] = { "", "J1", "Outfall-1", "Conduit C12", "  spaced  ", "A_very_long_identifier_name_x",
        "N\xe9ud", "1234567890123456789012345678901234567890123456789" };
    char a[256];
    for (unsigned f = 0; f < sizeof dfmt / sizeof *dfmt; f++)
        for (int k = 0; k < 2000; k++) {
            double v = value(k), w = value(k + 3);
            int r;
            if (strchr(dfmt[f] + 1, '%')) {
                snprintf(a, sizeof a, "%016llx,%016llx", (unsigned long long)dbits(v), (unsigned long long)dbits(w));
                r = snprintf(out, sizeof out, dfmt[f], v, w); emit(dfmt[f], "dd", a, r);
            } else {
                snprintf(a, sizeof a, "%016llx", (unsigned long long)dbits(v));
                r = snprintf(out, sizeof out, dfmt[f], v); emit(dfmt[f], "d", a, r);
            }
        }
    for (int k = 0; k < 2000; k++) {                               // %.*g with the precision the engine passes
        int p = (int)(rnd() % 18);
        double v = value(k);
        snprintf(a, sizeof a, "%08x,%016llx", (unsigned)p, (unsigned long long)dbits(v));
        int r = snprintf(out, sizeof out, "%.*g", p, v); emit("%.*g", "id", a, r);
    }
    for (unsigned f = 0; f < sizeof ifmt / sizeof *ifmt; f++)
        for (int k = 0; k < 600; k++) {
            int v = k % 3 == 0 ? (int)rnd() : k % 3 == 1 ? (int)(rnd() % 200) - 100 : (int)(rnd() % 100000);
            snprintf(a, sizeof a, "%08x", (unsigned)v);
            int r = snprintf(out, sizeof out, ifmt[f], v); emit(ifmt[f], "i", a, r);
        }
    for (unsigned f = 0; f < sizeof lfmt / sizeof *lfmt; f++)
        for (int k = 0; k < 600; k++) {
            long v = k % 3 == 0 ? (long)rnd() : k % 3 == 1 ? (long)(rnd() % 200) - 100 : (long)(rnd() % 100000000);
            snprintf(a, sizeof a, "%016lx", (unsigned long)v);
            int r = snprintf(out, sizeof out, lfmt[f], v); emit(lfmt[f], "l", a, r);
        }
    for (unsigned f = 0; f < sizeof sfmt / sizeof *sfmt; f++)
        for (unsigned k = 0; k < sizeof sval / sizeof *sval; k++) {
            int n = 0;
            for (const char *p = sval[k]; *p; p++) n += snprintf(a + n, sizeof a - n, "%02x", (unsigned char)*p);
            if (!*sval[k]) strcpy(a, "-");
            int r = snprintf(out, sizeof out, sfmt[f], sval[k]); emit(sfmt[f], "s", a, r);
        }
    return 0;
}
