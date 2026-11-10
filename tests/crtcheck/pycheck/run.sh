#!/bin/bash
# Compare the Python runtime's C library (package cpp, py/cpp: part C's crt / crtmath / crtmisc) with the system's
# glibc, as ../run.sh does for go/cpp and ../javacheck/run.sh for Java: libm results bit for bit (per-function
# mismatch counts, and the special-value table), printf output, sscanf results, from_chars / strtol / strtod / atoi /
# ctime / ctype / get_nprocs, and unit checks of the rest; specs.c adds glibc's output for every printf conversion
# spec the engine's format strings use. The C and C++ programs are ../libm.c, ../printf.c, ../scanf.c, ../misc.cpp
# and specs.c and use only the system's own libc, libm and libstdc++.
# Usage: run.sh [N] (N libm cases per function, default 300000). Set TZ to test ctime in another time zone.
# Written by Claude (Anthropic) for the SWMM6 ports; see AI-USE.md.
set -e
cd "$(dirname "$0")"
B=../build/py
mkdir -p $B
for p in libm printf scanf; do gcc -O2 -ffp-contract=off -fno-builtin -o $B/$p ../$p.c -lm; done
gcc -O2 -o $B/specs specs.c
g++ -O2 -std=c++20 -o $B/misc ../misc.cpp
P="python3 -I crtcheck.py"
N=${1:-300000}
$B/libm $N | $P libm $N
$B/printf | $P printf
$B/specs | $P printf
$B/scanf | $P scanf
$B/misc | $P misc
$P units
$P speed
