#!/bin/bash
# build_glibm.sh GLIBC_SRC OUTDIR
# Compile glibc 2.39's double-precision math routines the way glibc's x86-64 multiarch build compiles
# the variants it selects on a processor with FMA and AVX2, and dump GCC's optimized GIMPLE (with vectorization
# off) for gim2x.py. The objects are only compiled, never run: extract_tables.py reads their constant tables,
# and the translations are checked against the system libm (test/run_tests.sh).
#
# Set 1 (glibm.*, copied from swmm5-ports/tools/libm): exp, log, pow, sin, cos, tan, asin, acos, atan with their
# FMA variants (-mfma -mavx2), log10 and branred generic (no FMA variant).
# Set 2 (glibm2.*, added for the SWMM6 ports): sysdeps/x86_64/fpu/multiarch has FMA variants (ifunc-fma.h or
# ifunc-avx-fma4.h, selected when the CPU has FMA and AVX2) of atan2, log2, sinh, tanh, expm1 and log1p; cosh,
# cbrt and hypot are generic. sinh's FMA variant calls __ieee754_exp_fma and __expm1_fma (e_sinh-fma.c);
# tanh's calls __expm1_fma; generic cosh calls the __ieee754_exp and __expm1 ifuncs, which resolve to the same
# FMA variants. glibc builds libm with -fno-math-errno (Makeconfig +extra-math-flags), which set 2 uses
# (set 1 was built and checked without it; it changes none of set 1's arithmetic).
# Written by Claude (Anthropic); see AI-USE.md.
set -e
GL=$(readlink -f "$1"); OUT=$(readlink -f "$2"); HERE=$(cd "$(dirname "$0")" && pwd)
G=$GL/sysdeps/ieee754/dbl-64
mkdir -p "$OUT/gimple"; cd "$OUT"
cp -r "$HERE/stub" . ; cp "$HERE/mathstubs.c" .
CF="-O2 -fno-tree-vectorize -fno-tree-slp-vectorize -frounding-math -std=gnu11 -include $OUT/stub/shim.h -include $OUT/stub/mylog.h -I$OUT/stub -I$G"
FMA="-mfma -mavx2"
build() { # name extra-flags...
  local f=$1; shift
  gcc $CF "$@" -c -o $f.o $G/$f.c
  (cd gimple && gcc $CF "$@" -fdump-tree-optimized -c -o /dev/null $G/$f.c)
}
# ---- set 1
build e_exp $FMA -D__exp=my_exp_fma
build e_log $FMA -D__log=my_log
build e_pow $FMA -D__pow=my_pow
build s_sin $FMA -D__sin=my_sin -D__cos=my_cos
build s_tan $FMA -D__tan=my_tan
build e_asin $FMA -D__ieee754_asin=my_asin -D__ieee754_acos=my_acos
build s_atan $FMA -D__atan=my_atan
build e_log10 -D__ieee754_log10=my_log10 -D__ieee754_log=my_log
for d in e_exp_data e_log_data e_pow_log_data sincostab branred; do gcc $CF -c -o $d.o $G/$d.c; done
# ---- set 2
S2="-fno-math-errno -include $OUT/stub/decls2.h -D__ieee754_exp=my_exp_fma -D__expm1=my_expm1"
build e_atan2 $FMA $S2 -D__ieee754_atan2=my_atan2
build e_log2 $FMA $S2 -D__log2=my_log2
build e_sinh $FMA $S2 -D__ieee754_sinh=my_sinh
build s_tanh $FMA $S2 -D__tanh=my_tanh
build s_expm1 $FMA $S2
build s_log1p $FMA $S2 -D__log1p=my_log1p
build e_cosh $S2 -D__ieee754_cosh=my_cosh
build s_cbrt $S2 -D__cbrt=my_cbrt -D__frexp=my_frexp -D__ldexp=my_ldexp
build e_hypot $S2 -D__hypot=my_hypot
gcc $CF -fno-math-errno -c -o e_log2_data.o $G/e_log2_data.c
gcc -O2 -c -o mathstubs.o mathstubs.c
# (no glibc-derived code is run here: the translated routines are checked against the system libm by test/run_tests.sh)
