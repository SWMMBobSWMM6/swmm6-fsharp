// scanf.c - glibc's sscanf on the engine's formats and their edge cases, one line per case:
//   hex(format) <TAB> hex(input) <TAB> target types <TAB> return value <TAB> each target's value (space-separated)
// Target types: i int, l long, u unsigned (read as an int), f float, d double, s char[64] (string), c char[64]
// (%c bytes), k char (one %c), n int (%n). Every target starts as 0x55 bytes, so an untouched one shows.
// gocheck (scanf) runs go/cpp's R_sscanf on the same case with the engine's kinds of targets and compares.
// Written by Claude (Anthropic) for the SWMM6 ports; see AI-USE.md.
#include <stdint.h>
#include <stdio.h>
#include <string.h>

typedef union { int i; long l; unsigned u; float f; double d; char s[64]; } slot;

static void hex(const char *p, int n) { for (int i = 0; i < n; i++) printf("%02x", (unsigned char)p[i]); if (n == 0) printf("-"); }

static void run(const char *fmt, const char *in, const char *types) {
    slot t[8];
    memset(t, 0x55, sizeof t);
    int r = sscanf(in, fmt, &t[0], &t[1], &t[2], &t[3], &t[4], &t[5], &t[6], &t[7]);
    hex(fmt, (int)strlen(fmt)); printf("\t"); hex(in, (int)strlen(in)); printf("\t%s\t%d\t", types, r);
    for (int k = 0; types[k]; k++) {
        slot *x = &t[k];
        switch (types[k]) {
        case 'i': case 'n': printf("%d", x->i); break;
        case 'u': printf("%d", (int)x->u); break;
        case 'l': printf("%ld", x->l); break;
        case 'f': { uint32_t b; memcpy(&b, &x->f, 4); printf("%08x", b); break; }
        case 'd': { uint64_t b; memcpy(&b, &x->d, 8); printf("%016llx", (unsigned long long)b); break; }
        case 'k': printf("%d", (int)(signed char)x->s[0]); break;
        case 's': case 'c': hex(x->s, 12); break;
        }
        printf(" ");
    }
    printf("\n");
}

int main(void) {
    static const char *intIn[] = { "12", "  -7", "+3", "abc", "", "   ", "2147483647", "2147483648", "-2147483649",
        "99999999999999999999", "-99999999999999999999", "12abc", "0x1A", "-", "+", "- 5", "007", "\t\n 42\n" };
    for (unsigned k = 0; k < sizeof intIn / sizeof *intIn; k++) {
        run("%d", intIn[k], "i"); run("%ld", intIn[k], "l"); run("%u", intIn[k], "u"); run("%lu", intIn[k], "l");
        run("%2d", intIn[k], "i"); run("%6ld", intIn[k], "l"); run("%*d %d", intIn[k], "i"); run(" %d", intIn[k], "i");
    }
    static const char *timeIn[] = { "12:30:45", "12:30", "12", "12:", ":30", "1 : 2 : 3", "07:05:09x", "", "1:2:3.5x",
        "1:2:3.5", "1:2:", "24:00:00" };
    for (unsigned k = 0; k < sizeof timeIn / sizeof *timeIn; k++) {
        run("%d:%d:%d", timeIn[k], "iii"); run("%d:%d%n", timeIn[k], "iin"); run("%d:%d:%lf%c", timeIn[k], "iidk");
        run("%d:%d:%lf%n", timeIn[k], "iidn"); run(" %2d:%2d", timeIn[k], "ii");
    }
    static const char *dateIn[] = { "1/2/2020", "10-11-2020", "5 6 7", "5/", "1/2/2020 rest", "1/2", "2024-01-02T03:04:05",
        "2024-01-02 03:04:05", "2024-01-02", "2024-01-02X03:04:05", "2024-01-02  T 03:04:05", "JAN/01/2020", "Ja1",
        "12/3", "jan-5-1999", "1/2/2020 10:11:12", "20200105", "2020 1 5", "2020,01,05", "-1/2/3" };
    for (unsigned k = 0; k < sizeof dateIn / sizeof *dateIn; k++) {
        run("%d%c%d%c%d", dateIn[k], "ikiki"); run("%d%c%d", dateIn[k], "iki"); run("%u%c%u%c%u", dateIn[k], "ukuku");
        run("%d/%d/%d%n", dateIn[k], "iiin"); run("%d-%d-%d%*[ T]%d:%d:%d", dateIn[k], "iiiiii");
        run("%d/%d/%d %d:%d:%d", dateIn[k], "iiiiii"); run("%3[A-Za-z]%c%u%c%u", dateIn[k], "skuku");
        run("%3s%c%d%c%d", dateIn[k], "skiki"); run("%4d%2d%2d", dateIn[k], "iii"); run("%4d %2d %2d", dateIn[k], "iii");
        run("%4d,%2d,%2d", dateIn[k], "iii"); run("%4d%2d%4d%3d", dateIn[k], "iiii");
    }
    static const char *fIn[] = { "123.456789012", "  1.5e3", "1e", "1e+", "1e+x", "-.5", ".", "inf", "nan", "infinity",
        "-nan", "-inf", "INF", "NaN(123)", "nan()", "0x1p3", "0x", "0x.8", "1.5.3", "12345678901", "0.1", "3.4028235e38",
        "3.4028236e38", "1e39", "1e-46", "1e-45", "16777217", "0.30000001192092896", "1e310", "4.9e-324", "2e-324",
        "2.2250738585072014e-308", "1,5", "+.e3", "+5.", "5.e2", "  -0", "1E5", "1.17549435e-38",
        "0.000000000000000000000000000000000000000000001", "7.006492321624085354618647916449580656401e-46",
        "123456789012345678901234567890", "1.5 X", "1.5", "2", "-3.25 #" };
    for (unsigned k = 0; k < sizeof fIn / sizeof *fIn; k++) {
        run("%f", fIn[k], "f"); run("%lf", fIn[k], "d"); run("%8lf", fIn[k], "d"); run("%5f", fIn[k], "f");
        run("%le", fIn[k], "d"); run("%lg", fIn[k], "d"); run("%e", fIn[k], "f"); run("%3lf", fIn[k], "d");
        run("%f %c", fIn[k], "fk");
    }
    static const char *sIn[] = { "hello world", "  abc", "", "x", "a b", "a", "  a   b  c", "ABCDEFG123456789", "AB 12",
        "abcdef", "foo,12", ",12", "foo", "abcdefg", "]a]b", "-x-", "SWMM5 file", "FLOW CFS", "123 Y", "123",
        "  ID1  5 6 7 8 9 2.5", "N1 1 2 3 A B C D", "N1 1 2 3 A", " %", "5 %", "5 x", "5", "abc5", "ab", "abd5" };
    for (unsigned k = 0; k < sizeof sIn / sizeof *sIn; k++) {
        run("%s", sIn[k], "s"); run("%s %s", sIn[k], "ss"); run("%5s%6ld", sIn[k], "sl"); run("%2s", sIn[k], "s");
        run("%[^,],%d", sIn[k], "si"); run("%[a-c-e]", sIn[k], "s"); run("%[]a]", sIn[k], "s"); run("%[-x]", sIn[k], "s");
        run("%*s %s", sIn[k], "s"); run("%c", sIn[k], "c"); run("%3c", sIn[k], "c"); run(" %c", sIn[k], "k");
        run("%ld %c", sIn[k], "lk"); run("%d %%", sIn[k], "i"); run("abc%d", sIn[k], "i"); run("SWMM5", sIn[k], "");
        run("%63s %d %d %d %d %d %f", sIn[k], "siiiiif"); run("%79s %d %d %d %79s %79s %79s %79s", sIn[k], "siiissss");
        run("%79s %d %d %d %79s", sIn[k], "siiis"); run("%79s %d %d %d", sIn[k], "siii");
        run("%s %d %d %d %d %d %d %lf", sIn[k], "siiiiiid"); run("%6ld %2d %4s", sIn[k], "lis");
        run("%2d %4s %2s %4d", sIn[k], "issi"); run("%3s%6ld%2d%4s", sIn[k], "slis"); run("%%%d", sIn[k], "i");
        run("%*c%c", sIn[k], "k"); run("%n%d%n", sIn[k], "nin"); run(" %n", sIn[k], "n");
    }
    static const char *rIn[] = { " 0101 123456 A B", "0101123456AB", ",0101,123456,A,B", "12 34 56", " 1 2 3 x y",
        "0101 12 A" };
    for (unsigned k = 0; k < sizeof rIn / sizeof *rIn; k++) {
        run(" %2d%2d %6ld %c %c", rIn[k], "iilkk"); run("%2d%2d%6ld%c%c", rIn[k], "iilkk");
        run(",%2d%2d,%6ld,%c,%c", rIn[k], "iilkk"); run("%6ld,%2d,%4s", rIn[k], "lis"); run("%2d,%4s,%2s,%4d", rIn[k], "issi");
    }
    return 0;
}
