// libm.c - inputs and results of the system libm (glibc) for the math functions the Go port of the SWMM6 engine
// computes itself (go/cpp/libm.go). Writes binary records (x, y, f(x[, y])) as raw little-endian doubles to
// stdout, N per function in the order of FN below, then a set of special values; gocheck/libm.go reads them and
// recomputes. Inputs come from integer bit patterns and exact operations only, so they need no libm themselves.
// Written by Claude (Anthropic) for the SWMM6 ports; see AI-USE.md.
#include <math.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

static uint64_t s = 0x9E3779B97F4A7C15ULL;
static uint64_t rnd(void) { s ^= s >> 12; s ^= s << 25; s ^= s >> 27; return s * 0x2545F4914F6CDD1DULL; }
static double bits(uint64_t b) { double d; memcpy(&d, &b, 8); return d; }
// uniform in [a, b)
static double U(double a, double b) { double u = (double)(rnd() >> 11) * 0x1.0p-53; return a + (b - a) * u; }
// log-uniform: a random exponent in [e1, e2], random mantissa, a random sign if sg
static double L(int e1, int e2, int sg) {
    uint64_t r = rnd();
    uint64_t e = (uint64_t)(e1 + 1023 + (int)(rnd() % (uint64_t)(e2 - e1 + 1)));
    uint64_t b = (e << 52) | (r & 0xFFFFFFFFFFFFFULL);
    if (sg && (rnd() & 1)) b |= 1ULL << 63;
    return bits(b);
}
static double Q(double a, double b) { return floor(U(a, b) * 4.0) / 4.0; }   // multiples of 1/4: many halves

static const double SPEC[] = { 0.0, -0.0, 1.0, -1.0, 0.5, -0.5, 2.0, 1e-310, -1e-310, 4.9e-324, 1.7976931348623157e308,
    -1.7976931348623157e308, INFINITY, -INFINITY, NAN, -NAN, 3.0, 710.0, -745.0, 1e-20, 0.75, 1.5, 2.5, -2.5 };
#define NSPEC (int)(sizeof SPEC / sizeof SPEC[0])

enum { ATAN2, CBRT, COSH, SINH, TANH, LOG2, EXPM1, LOG1P, HYPOT, FMOD, REMAINDER, ROUND, NEXTAFTER,
       EXP, LOG, POW, SIN, COS, TAN, ASIN, ACOS, ATAN, LOG10, FLOOR, CEIL, TRUNC, SQRT, NFN };

static void gen(int fn, int i, double *x, double *y) {
    int k = i % 4;
    *y = 0;
    switch (fn) {
    case ATAN2: case HYPOT:
        if (k == 0) { *x = U(-10, 10); *y = U(-10, 10); }
        else if (k == 1) { *x = L(-60, 60, 1); *y = L(-60, 60, 1); }
        else if (k == 2) { *x = L(-1022, 1023, 1); *y = L(-1022, 1023, 1); }
        else { *x = U(-1000, 1000); *y = L(-30, 30, 1); }
        break;
    case CBRT: *x = k == 0 ? U(-10, 10) : k == 1 ? L(-1022, 1023, 1) : k == 2 ? U(0, 1000) : L(-60, 60, 1); break;
    case COSH: case SINH: *x = k == 0 ? U(-1, 1) : k == 1 ? U(-30, 30) : k == 2 ? U(-711, 711) : L(-60, 0, 1); break;
    case TANH: *x = k == 0 ? U(-1, 1) : k == 1 ? U(-25, 25) : k == 2 ? L(-60, 4, 1) : U(-3, 3); break;
    case LOG2: case LOG: case LOG10:
        *x = k == 0 ? L(-1022, 1023, 0) : k == 1 ? U(0.5, 2) : k == 2 ? U(0, 100) : U(0.9, 1.1); break;
    case EXPM1: *x = k == 0 ? U(-1, 1) : k == 1 ? U(-50, 50) : k == 2 ? U(-750, 710) : L(-60, -1, 1); break;
    case LOG1P: *x = k == 0 ? U(-1, 1) : k == 1 ? L(-60, -1, 1) : k == 2 ? L(-1, 1023, 0) : U(0, 100); break;
    case FMOD: case REMAINDER:
        if (k < 2) { *x = L(-30, 60, 1); *y = L(-30, 30, 1); }
        else if (k == 2) { *x = U(-100, 100); *y = U(-10, 10); }
        else { *x = L(-1022, 1023, 1); *y = L(-1022, 1023, 1); }
        break;
    case ROUND: case FLOOR: case CEIL: case TRUNC: *x = k < 2 ? Q(-1000, 1000) : k == 2 ? U(-1e6, 1e6) : L(-60, 60, 1); break;
    case NEXTAFTER: *x = L(-1022, 1023, 1); *y = k == 0 ? *x : k == 1 ? -*x : k == 2 ? L(-1022, 1023, 1) : U(-1, 1); break;
    case EXP: *x = k == 0 ? U(-745, 710) : k == 1 ? U(-1, 1) : k == 2 ? U(-20, 20) : L(-60, -1, 1); break;
    case POW:
        if (k == 0) { *x = L(-20, 20, 0); *y = U(-50, 50); }
        else if (k == 1) { *x = U(0, 10); *y = U(-10, 10); }
        else if (k == 2) { *x = U(-10, 0); *y = floor(U(-20, 20)); }
        else { *x = U(0.5, 2); *y = U(-1000, 1000); }
        break;
    case SIN: case COS: case TAN: *x = k == 0 ? U(-10, 10) : k == 1 ? L(-30, 30, 1) : k == 2 ? L(30, 1023, 1) : U(-1e6, 1e6); break;
    case ASIN: case ACOS: *x = k < 2 ? U(-1, 1) : k == 2 ? L(-60, -1, 1) : U(0.99, 1); break;
    case ATAN: *x = k == 0 ? U(-10, 10) : k == 1 ? L(-60, 60, 1) : k == 2 ? L(-1022, 1023, 1) : U(-1, 1); break;
    case SQRT: *x = k == 0 ? L(-1022, 1023, 0) : U(0, 100); break;
    }
}

static double call(int fn, double x, double y) {
    switch (fn) {
    case ATAN2: return atan2(x, y);
    case CBRT: return cbrt(x);
    case COSH: return cosh(x);
    case SINH: return sinh(x);
    case TANH: return tanh(x);
    case LOG2: return log2(x);
    case EXPM1: return expm1(x);
    case LOG1P: return log1p(x);
    case HYPOT: return hypot(x, y);
    case FMOD: return fmod(x, y);
    case REMAINDER: return remainder(x, y);
    case ROUND: return round(x);
    case NEXTAFTER: return nextafter(x, y);
    case EXP: return exp(x);
    case LOG: return log(x);
    case POW: return pow(x, y);
    case SIN: return sin(x);
    case COS: return cos(x);
    case TAN: return tan(x);
    case ASIN: return asin(x);
    case ACOS: return acos(x);
    case ATAN: return atan(x);
    case LOG10: return log10(x);
    case FLOOR: return floor(x);
    case CEIL: return ceil(x);
    case TRUNC: return trunc(x);
    case SQRT: return sqrt(x);
    }
    return 0;
}

int main(int argc, char **argv) {
    int n = argc > 1 ? atoi(argv[1]) : 300000;
    double rec[3];
    for (int fn = 0; fn < NFN; fn++) {
        for (int i = 0; i < n; i++) {
            gen(fn, i, &rec[0], &rec[1]);
            rec[2] = call(fn, rec[0], rec[1]);
            fwrite(rec, 8, 3, stdout);
        }
        for (int a = 0; a < NSPEC; a++)
            for (int b = 0; b < NSPEC; b++) {
                rec[0] = SPEC[a]; rec[1] = SPEC[b];
                rec[2] = call(fn, rec[0], rec[1]);
                fwrite(rec, 8, 3, stdout);
            }
    }
    return 0;
}
