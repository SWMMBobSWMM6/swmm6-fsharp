#!/bin/bash
# Compare part C of the Fortran runtime (module cpp_rt_c: f90/runtime/cpp_rt_c.f90, cpp_rt_c_fmt.f90,
# cpp_rt_c_ptr.f90) with the system's glibc, as ../run.sh does for go/cpp and ../pycheck/run.sh for Python: libm
# results bit for bit (per-function mismatch counts), printf output (../printf.c, and pycheck/specs.c: glibc's
# output for every conversion spec the engine's format strings use), sscanf results, from_chars / strtol / strtod /
# atoi / atol / ctime / ctype / get_nprocs, and unit checks of the rest. The C and C++ programs use only the
# system's own libc, libm and libstdc++.
# Part A: the real f90/runtime/cpp_rt_a.f90 when it exists, else a stand-in made from its stub by mkstandin.py
# (Ptr fields as C#'s Ptr<T> has them, and the few bodies part C calls).
# Usage: run.sh [N] (N libm cases per function, default 300000). Set TZ to test ctime in another time zone.
# Written by Claude (Anthropic) for the SWMM6 ports; see AI-USE.md.
set -e
cd "$(dirname "$0")"
B=../build/f90
O=$B/obj
R=../../../f90/runtime
mkdir -p $O
FF="-O2 -fwrapv -ffp-contract=off -fno-fast-math -frecursive -ffree-line-length-none -fimplicit-none -fno-range-check"
for p in libm printf scanf; do nice -n 10 gcc -O2 -ffp-contract=off -fno-builtin -o $B/$p ../$p.c -lm; done
nice -n 10 gcc -O2 -o $B/specs ../pycheck/specs.c
nice -n 10 g++ -O2 -std=c++20 -o $B/misc ../misc.cpp
if [ -f $R/cpp_rt_a.f90 ]; then
  A=$R/cpp_rt_a.f90
  echo "part A: $A"
else
  python3 -I mkstandin.py $O/cpp_rt_a_standin.f90
  A=$O/cpp_rt_a_standin.f90
  echo "part A: stand-in (mkstandin.py from f90/runtime/stub/cpp_rt_a.f90)"
fi
G="nice -n 10 gfortran $FF -J$O"
$G -c $A -o $O/a.o
$G -c $R/cpp_rt_c.f90 -o $O/c.o
$G -c $R/cpp_rt_c_fmt.f90 -o $O/c_fmt.o
$G -c $R/cpp_rt_c_ptr.f90 -o $O/c_ptr.o
AS=""
if [ "$A" = "$R/cpp_rt_a.f90" ]; then
  # part A's submodules (those that need part C, e.g. Str.stod); one that needs part B or D is left out
  for f in $R/cpp_rt_a_*.f90; do
    [ -f "$f" ] || continue
    o=$O/$(basename $f .f90).o
    if $G -c $f -o $o 2>/dev/null; then AS="$AS $o"; else echo "part A submodule left out: $f"; fi
  done
fi
$G -c crtcheck.f90 -o $O/ck.o
$G -c crtunits.f90 -o $O/cku.o
$G -o $B/crtcheck $O/cku.o $O/ck.o $O/c_ptr.o $O/c_fmt.o $O/c.o $AS $O/a.o -lm
P=$B/crtcheck
N=${1:-300000}
$B/libm $N | $P libm $N
$B/printf | $P printf
$B/specs | $P printf
$B/scanf | $P scanf
$B/misc | $P misc
$P units
$P speed
