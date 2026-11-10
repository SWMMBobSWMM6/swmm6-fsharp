#!/bin/bash
# Compare the Java runtime's C library (package cpp, java/src/cpp) with the system's glibc, as ../run.sh does for
# go/cpp: libm results bit for bit (per-function mismatch counts), printf output, sscanf results, from_chars /
# strtol / strtod / atoi / ctime / ctype / get_nprocs, and unit checks of the rest. The C and C++ programs are
# ../libm.c, ../printf.c, ../scanf.c and ../misc.cpp and use only the system's own libc, libm and libstdc++.
# Usage: run.sh [N] (N libm cases per function, default 300000). Set TZ to test ctime in another time zone.
# Written by Claude (Anthropic) for the SWMM6 ports; see AI-USE.md.
set -e
cd "$(dirname "$0")"
B=../build/java
mkdir -p $B/classes
for p in libm printf scanf; do gcc -O2 -ffp-contract=off -fno-builtin -o $B/$p ../$p.c -lm; done
g++ -O2 -std=c++20 -o $B/misc ../misc.cpp
# -sourcepath: only the cpp classes the checker reaches are compiled
javac --release 21 -nowarn -sourcepath ../../../java/src -d $B/classes CrtCheck.java
J="java -cp $B/classes CrtCheck"
N=${1:-300000}
$B/libm $N | $J libm $N
$B/printf | $J printf
$B/scanf | $J scanf
$B/misc | $J misc
$J units
