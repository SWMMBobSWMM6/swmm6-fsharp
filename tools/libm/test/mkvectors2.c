/* mkvectors2.c - test vectors for set 2 of the ports of glibc's math routines (glibm2.*: cbrt, hypot, atan2,
   cosh, sinh, tanh, log2, expm1, log1p, and the runtime's frexp and ldexp), computed with the system libm.
     mkvectors2 N OUT.bin
   For each function: N random arguments mixing several distributions (uniform over the ranges the SWMM6 engine
   uses, uniform over wider ranges, random bit patterns over every exponent and both signs, subnormals, the
   neighbourhoods of every branch threshold in glibc's source), then an edge set: +-0, +-1, +-inf, quiet and
   signaling NaNs of both signs with several payloads, the smallest and largest subnormal and normal numbers,
   and, for atan2 and hypot, every pair of those. Same record format as mkvectors.c (name\0 n:u32, then n x
   (args..., result) as doubles); frexp records are (x, mantissa, exponent), ldexp records (x, n, result).
   Built with -fno-builtin so that GCC folds no call at compile time. Written by Claude (Anthropic) for the
   SWMM6 ports; see AI-USE.md. */
#include <stdio.h>
#include <stdlib.h>
#include <stdint.h>
#include <string.h>
#include <math.h>
static uint64_t s = 0xD1B54A32D192ED03ull;
static uint64_t xr(void){ s ^= s<<13; s ^= s>>7; s ^= s<<17; return s; }
static double asd(uint64_t u){ double d; memcpy(&d,&u,8); return d; }
static uint64_t asu(double d){ uint64_t u; memcpy(&u,&d,8); return u; }
static double uni(double a, double b){ return a + (b-a) * ((double)(xr()>>11) / 9007199254740992.0); }
/* a random double with exponent in [e0, e1) (unbiased), random mantissa, positive */
static double lgu(int e0, int e1){ uint64_t r = xr(); return asd((r & 0x000fffffffffffffull) | ((uint64_t)(1023 + e0 + (int)((r>>52) % (uint64_t)(e1-e0))) << 52)); }
static double sg(double x){ return (xr() & 1) ? -x : x; }
static double anybits(void){ uint64_t u; do u = xr(); while (((u >> 52) & 0x7ff) == 0x7ff); return asd(u); } /* finite, any exponent */
static double sub(void){ return asd(xr() & 0x000fffffffffffffull); }            /* subnormal (or zero), positive */
static double nb(double x, int k){ return asd(asu(x) + (int64_t)k); }            /* k ulps away (same sign) */
static double near(double t, int w){ return nb(t, (int)(xr() % (uint64_t)(2*w+1)) - w); }

typedef struct { const char *name; int nargs; double *a; long n, cap; } V;
static V vs[] = {{"cbrt",1},{"hypot",2},{"atan2",2},{"cosh",1},{"sinh",1},{"tanh",1},{"log2",1},{"expm1",1},{"log1p",1},{"frexp",1},{"ldexp",2}};
enum {CBRT, HYPOT, ATAN2, COSH, SINH, TANH, LOG2, EXPM1, LOG1P, FREXP, LDEXP, NV};
static void add(int i, double x, double y) {
  V *v = &vs[i];
  if (v->n == v->cap) { v->cap = v->cap ? 2*v->cap : 1<<16; v->a = realloc(v->a, sizeof(double)*2*v->cap); }
  double *r = v->a + 2*v->n++; r[0] = x; r[1] = y;
}
static void call(int i, const double *r, double *out) {
  int e;
  switch (i) {
  case CBRT: out[0] = cbrt(r[0]); break;
  case HYPOT: out[0] = hypot(r[0], r[1]); break;
  case ATAN2: out[0] = atan2(r[0], r[1]); break;
  case COSH: out[0] = cosh(r[0]); break;
  case SINH: out[0] = sinh(r[0]); break;
  case TANH: out[0] = tanh(r[0]); break;
  case LOG2: out[0] = log2(r[0]); break;
  case EXPM1: out[0] = expm1(r[0]); break;
  case LOG1P: out[0] = log1p(r[0]); break;
  case FREXP: out[0] = frexp(r[0], &e); out[1] = e; break;
  default: out[0] = ldexp(r[0], (int)r[1]); break;
  }
}
/* thresholds in glibc's sources, as doubles (each tested with its neighbours) */
static double hw(uint32_t hi){ return asd((uint64_t)hi << 32); }
int main(int argc, char **argv) {
  long n = atol(argv[1]);
  const double LN2 = 0.69314718055994530942;
  double th1[] = { /* cosh, sinh, tanh */ hw(0x40360000), hw(0x3e300000), hw(0x3ff00000), hw(0x40862e42), asd(0x408633ce8fb9f87dull),
                   hw(0x3fd62e43), hw(0x3c800000), 22.0, 710.4758600739439, 709.782712893384, 0.5*LN2 };
  double thx[] = { /* expm1 */ hw(0x4043687A), hw(0x40862E42), 7.09782712893383973096e+02, hw(0x3fd62e42), hw(0x3FF0A2B2), hw(0x3c900000),
                   0.5*LN2, 1.5*LN2, 56*LN2, 20*LN2, 19.5*LN2, 20.5*LN2, 55.5*LN2, 56.5*LN2 };
  double thl[] = { /* log1p */ hw(0x3FDA827A), -1.0, hw(0x3e200000), hw(0x3c900000), -hw(0x3fd2bec3), hw(0x43400000), 0.41421356237309503, -0.2928932188134524 };
  for (long i = 0; i < n; i++) {
    int m = (int)(i % 8);
    double x, y;
    /* cbrt */
    x = m == 0 ? uni(0, 10) : m == 1 ? uni(-1e4, 1e4) : m == 2 ? sg(lgu(-60, 60)) : m == 3 ? sg(sub()) : m == 4 ? (double)(int64_t)uni(-1e6, 1e6) : anybits();
    if (m == 4 && (i & 8)) x = x * x * x;                                           /* exact cubes */
    add(CBRT, x, 0);
    /* hypot */
    switch (m) {
    case 0: x = uni(-100, 100); y = uni(-100, 100); break;
    case 1: x = sg(lgu(-30, 30)); y = sg(lgu(-30, 30)); break;
    case 2: x = sg(lgu(-1, 1)); y = x * uni(0.4, 1.0); break;                    /* both kernel branches */
    case 3: x = sg(lgu(500, 1024)); y = sg(lgu(440, 1024)); break;               /* huge: scaled down */
    case 4: x = sg(lgu(-1074, -440)); y = sg(lgu(-1074, -380)); if (i & 8) x = sg(sub()); break; /* tiny: scaled up */
    case 5: x = sg(lgu(-20, 20)); y = x * ldexp(uni(0.5, 1), -(int)(xr() % 70)); break; /* ay near ax*2^-54 */
    case 6: x = uni(0, 2); y = uni(0, 0.1); break;                                 /* flow-geometry sizes */
    default: x = anybits(); y = anybits(); break;
    }
    add(HYPOT, x, y);
    /* atan2 (y, x) */
    switch (m) {
    case 0: y = uni(-10, 10); x = uni(-10, 10); break;
    case 1: y = sg(lgu(-40, 40)); x = sg(lgu(-40, 40)); break;
    case 2: x = sg(lgu(-5, 5)); y = x * sg(uni(1.0/32, 1.0/8)); if (i & 8) { double t = x; x = y; y = t; } break; /* around u = 1/16 */
    case 3: x = sg(lgu(-5, 5)); y = x * sg(near(1.0, 100)); break;               /* |y| near |x| */
    case 4: y = sg(lgu(-1074, -480)); x = sg(lgu(-1074, -480)); break;            /* scaled up */
    case 5: y = sg(lgu(480, 1024)); x = sg(lgu(480, 1024)); break;                /* scaled down */
    case 6: y = sg(lgu(-60, 60)); x = sg(lgu(-60, 60)); if (i & 8) x = y * ldexp(1.0, (int)(xr() % 140) - 70); break; /* exponent gap near 57*16^5 >> 20 */
    default: y = anybits(); x = anybits(); break;
    }
    add(ATAN2, y, x);
    /* cosh, sinh, tanh */
    for (int f = COSH; f <= TANH; f++) {
      switch (m) {
      case 0: x = uni(-3, 3); break;
      case 1: x = uni(-25, 25); break;
      case 2: x = uni(-720, 720); break;
      case 3: x = sg(lgu(-60, 6)); break;
      case 4: x = sg(near(th1[xr() % 11], 2000)); break;
      case 5: x = sg(lgu(-1074, -1022)); if (i & 8) x = sg(sub()); break;
      case 6: x = uni(-1, 1); break;
      default: x = anybits(); break;
      }
      add(f, x, 0);
    }
    /* log2 */
    switch (m) {
    case 0: x = uni(0, 10); break;
    case 1: x = near(1.0, 1 << 20); break;
    case 2: x = uni(1.0 - 0x1.5b51p-5, 1.0 + 0x1.6ab2p-5); break;                 /* close to 1: its own branch */
    case 3: x = sub(); break;
    case 4: x = lgu(-1022, 1024); break;
    case 5: x = ldexp(1.0, (int)(xr() % 2098) - 1074); if (i & 8) x = nb(x, (int)(xr() % 5) - 2); break; /* powers of two */
    case 6: x = -lgu(-30, 30); break;                                              /* domain error */
    default: x = anybits(); break;
    }
    add(LOG2, x, 0);
    /* expm1 */
    switch (m) {
    case 0: x = uni(-1, 1); break;
    case 1: x = uni(-50, 50); break;
    case 2: x = uni(-800, 800); break;
    case 3: x = sg(lgu(-70, 0)); break;
    case 4: x = sg(near(thx[xr() % 14], 4000)); break;
    case 5: x = sg(lgu(-1074, -1022)); if (i & 8) x = sg(sub()); break;
    case 6: x = (double)(int)uni(-60, 60) * LN2 + uni(-0.5, 0.5) * LN2; break;   /* every k */
    default: x = anybits(); break;
    }
    add(EXPM1, x, 0);
    /* log1p */
    switch (m) {
    case 0: x = uni(-0.999, 1); break;
    case 1: x = uni(-1, 100); break;
    case 2: x = sg(lgu(-70, 0)); break;
    case 3: x = sg(near(thl[xr() % 8], 4000)); break;
    case 4: x = lgu(0, 1024); break;
    case 5: x = sg(lgu(-1074, -1022)); if (i & 8) x = sg(sub()); break;
    case 6: x = -1.0 - lgu(-60, 10); break;                                        /* domain error */
    default: x = anybits(); break;
    }
    add(LOG1P, x, 0);
    /* frexp, ldexp */
    add(FREXP, (m & 1) ? sg(sub()) : anybits(), 0);
    { int e = (m == 0) ? (int)(xr() % 4400) - 2200 : (m == 1) ? (int)(xr() % 200001) - 100000 : (int)(xr() % 160) - 80;
      x = (m & 2) ? sg(sub()) : (m & 4) ? sg(lgu(-1022, -900)) : anybits();
      if (m == 7) { x = sg(lgu(-1022, 1024)); e = -(int)((asu(x) >> 52) & 0x7ff) - (int)(xr() % 60); } /* subnormal results */
      add(LDEXP, x, (double)e); }
  }
  /* edge set */
  double sp[] = {0.0, -0.0, 1.0, -1.0, 2.0, -2.0, 0.5, INFINITY, -INFINITY,
                 asd(0x7ff8000000000000ull), asd(0xfff8000000000000ull), asd(0x7ff8000000000123ull), asd(0xfff80000deadbeefull),
                 asd(0x7ff0000000000001ull), asd(0xfff0000000000001ull), asd(0x7ff4000000000abcull), asd(0xfff4000000000777ull),
                 asd(1), -asd(1), asd(0x000fffffffffffffull), -asd(0x000fffffffffffffull), 2.2250738585072014e-308, -2.2250738585072014e-308,
                 1.7976931348623157e308, -1.7976931348623157e308, 1e-300, -1e-300, 1e300, -1e300, 3.0, -3.0, 0.1, -0.1, 22.0, -22.0,
                 710.0, -710.0, 711.0, -711.0, 1e-20, -1e-20};
  enum { NSP = sizeof sp / sizeof sp[0] };
  for (int i = 0; i < NSP; i++) {
    for (int f = 0; f < NV; f++) {
      if (vs[f].nargs == 1) add(f, sp[i], 0);
      else if (f == LDEXP) { int es[] = {0, 1, -1, 1023, -1074, 2000, -2000, 60000, -60000}; for (int k = 0; k < 9; k++) add(f, sp[i], es[k]); }
      else for (int j = 0; j < NSP; j++) add(f, sp[i], sp[j]);
    }
  }
  /* every threshold, its 64 neighbours on each side, both signs */
  double *ths[] = {th1, thx, thl}; int nth[] = {11, 14, 8};
  for (int g = 0; g < 3; g++) for (int t = 0; t < nth[g]; t++) for (int k = -64; k <= 64; k++) for (int sgn = 0; sgn < 2; sgn++) {
    double x = nb(ths[g][t], k); if (sgn) x = -x;
    for (int f = COSH; f <= LOG1P; f++) add(f, x, 0);
    add(CBRT, x, 0);
  }
  FILE *o = fopen(argv[2], "wb");
  for (int i = 0; i < NV; i++) {
    V *v = &vs[i]; uint32_t m = v->n;
    fwrite(v->name, 1, strlen(v->name) + 1, o); fwrite(&m, 4, 1, o);
    for (long j = 0; j < v->n; j++) {
      double *r = v->a + 2*j, out[2], rec[3];
      call(i, r, out);
      int k = 0; rec[k++] = r[0]; if (v->nargs > 1) rec[k++] = r[1]; rec[k++] = out[0]; if (i == FREXP) rec[k++] = out[1];
      fwrite(rec, 8, k, o);
    }
    fprintf(stderr, "%s %ld\n", v->name, v->n);
  }
  fclose(o);
  return 0;
}
