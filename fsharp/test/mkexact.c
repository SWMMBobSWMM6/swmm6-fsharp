/* mkexact.c - test vectors for the exact math.h functions the SWMM6 engine calls (sqrt, floor, ceil, trunc, fabs and
   copysign), computed with the system libm, in the record format of tools/libm/test/mkvectors.c (name\0, n:u32, then
   n x (args..., result) as raw doubles). The F# engine takes these from .NET's Math (Math.Sqrt, Floor, Ceiling,
   Truncate, Abs, CopySign), not from libm; this checks that they give glibc's bits, signed zeros, NaN signs and
   payloads, infinities and huge and subnormal arguments included. After the SWMM5 F# port's fsharp/test/mkexact.c
   (itself from its Ruby port), with trunc and copysign added.
   gcc -O2 -fno-builtin -o mkexact mkexact.c -lm && ./mkexact N out.bin
   Written by Claude (Anthropic) for Robert Dickinson's SWMM6 ports; see ../README.md. */
#include <stdio.h>
#include <stdlib.h>
#include <stdint.h>
#include <string.h>
#include <math.h>
static uint64_t s = 0x2545F4914F6CDD1Dull;
static uint64_t xr(void){ s ^= s<<13; s ^= s>>7; s ^= s<<17; return s; }
static double asd(uint64_t u){ double d; memcpy(&d,&u,8); return d; }
static double uni(double a, double b){ return a + (b-a) * ((double)(xr()>>11) / 9007199254740992.0); }
static double lgu(int e0, int e1){ uint64_t r = xr(); return asd((r & 0x800fffffffffffffull) | ((uint64_t)(1023 + e0 + (int)((r>>52) % (uint64_t)(e1-e0))) << 52)); }
static double anyd(void){ return asd(xr()); }
static double pick(long i) {
  switch (i % 5) {
  case 0: return anyd();                          /* any bit pattern: NaN payloads, subnormals, huge */
  case 1: return uni(-1000, 1000);
  case 2: return lgu(-60, 60);                     /* signed, wide range */
  case 3: return (double)(int64_t)uni(-1e6, 1e6) + ((xr() & 1) ? 0.0 : 0.5);   /* integers and halves */
  default: return uni(-2, 2) * ldexp(1.0, (int)(xr() % 70));
  }
}
static double (*fn[5])(double) = {sqrt, floor, ceil, trunc, fabs};
static const char *names[6] = {"sqrt", "floor", "ceil", "trunc", "fabs", "copysign"};
int main(int argc, char **argv) {
  long n = atol(argv[1]);
  double sp[] = {0.0, -0.0, 1.0, -1.0, INFINITY, -INFINITY, NAN, -NAN, 0.5, -0.5, 0.49999999999999994, -0.49999999999999994,
    1e-310, -1e-310, 4503599627370495.5, -4503599627370495.5, 4503599627370496.0, 9007199254740993.0, 1e300, -1e300,
    asd(0x7ff0000000000123ull), asd(0xfff8000000000456ull), 2.5, -2.5, 1.5, -1.5, 0.9999999999999999, -0.9999999999999999};
  int nsp = sizeof sp / sizeof *sp;
  FILE *o = fopen(argv[2], "wb");
  for (int f = 0; f < 6; f++) {
    uint32_t m = f < 5 ? (uint32_t)(n + nsp) : (uint32_t)(n + nsp * nsp);
    fwrite(names[f], 1, strlen(names[f]) + 1, o); fwrite(&m, 4, 1, o);
    if (f < 5) {
      for (int i = 0; i < nsp; i++) { double r[2] = {sp[i], fn[f](sp[i])}; fwrite(r, 8, 2, o); }
      for (long i = 0; i < n; i++) { double x = pick(i); double r[2] = {x, fn[f](x)}; fwrite(r, 8, 2, o); }
    } else {
      for (int i = 0; i < nsp; i++) for (int j = 0; j < nsp; j++) { double r[3] = {sp[i], sp[j], copysign(sp[i], sp[j])}; fwrite(r, 8, 3, o); }
      for (long i = 0; i < n; i++) { double x = pick(i), y = pick(i + 3); double r[3] = {x, y, copysign(x, y)}; fwrite(r, 8, 3, o); }
    }
    fprintf(stderr, "%s %u\n", names[f], m);
  }
  fclose(o);
  return 0;
}
