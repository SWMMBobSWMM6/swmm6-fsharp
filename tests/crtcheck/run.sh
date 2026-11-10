#!/bin/bash
# Compare go/cpp's C runtime with the system's glibc: libm results bit for bit (per-function mismatch counts),
# printf output, sscanf results, from_chars / strtol / ctime / ctype / get_nprocs, and unit checks of the
# rest. The C and C++ programs use only the system's own libc, libm and libstdc++.
set -e
cd "$(dirname "$0")"
mkdir -p build
for p in libm printf scanf; do gcc -O2 -ffp-contract=off -fno-builtin -o build/$p $p.c -lm; done
g++ -O2 -std=c++20 -o build/misc misc.cpp
(cd gocheck && go build -o ../build/gocheck .)
N=${1:-300000}
build/libm $N | build/gocheck libm $N
build/printf | build/gocheck printf
build/scanf | build/gocheck scanf
build/misc | build/gocheck misc
build/gocheck units
