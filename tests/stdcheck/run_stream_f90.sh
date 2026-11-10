#!/bin/bash
# The Fortran runtime's streams and file system (f90/runtime/cpp_rt_d.f90, part D, over parts A, C and B) against
# libstdc++, cs/runtime/Streams.cs + Fs.cs and go/cpp/streams.go + fs.go, on the cases of streamcheck.cpp.
# run_stream.sh builds and runs the C++, C# and Go programs; their outputs in build/ are reused unless RERUN_OTHERS=1.
# This script builds the Fortran runtime alone (f90/build.sh ONLY=runtime, each part's real module or its stub, into
# build/f90), compiles streamcheck_f90/streamcheck.f90 against it with the engine's flags, runs the three sections and
# compares. Section a: Fortran must equal C# and Go, and C++ but for the known glibc %#g quirk (see run_stream.sh).
# Section b: Fortran must equal C# and Go. Section c: Fortran must equal C++ and C#.
# Needs the real parts A and C (the stubs' procedures do nothing).
# Written by Claude (Anthropic) for the SWMM6 ports; see AI-USE.md.
set -e
cd "$(dirname "$0")"
ROOT=$(cd ../.. && pwd)
B=build/f90
mkdir -p build "$B"
if [ "${RERUN_OTHERS:-0}" = 1 ] || [ ! -f build/stream_cs_b.txt ]; then ./run_stream.sh || true; fi
BUILD_DIR=$(pwd)/$B ONLY=runtime "$ROOT/f90/build.sh" > "$B/build.log" 2>&1 || { cat "$B/build.log"; echo "runtime build failed"; exit 1; }
FFLAGS="-O2 -fwrapv -ffp-contract=off -fno-fast-math -frecursive -ffree-line-length-none -fimplicit-none -fno-range-check"
nice -n 10 gfortran $FFLAGS -J"$B" -I"$B" -c streamcheck_f90/streamcheck.f90 -o "$B/streamcheck.o"
nice -n 10 gfortran $FFLAGS -o "$B/streamcheck" "$B/streamcheck.o" "$B"/cpp_rt_*.o "$B/cpp_runtime.o" -lm
for s in a b c; do env -u TMPDIR "$B/streamcheck" $s > build/stream_f90_$s.txt; done
fail=0
same() { if cmp -s "$1" "$2"; then echo "  $3: identical"; else echo "  $3: DIFFERENT"; diff "$1" "$2" | head -8; fail=1; fi; }
# section a: the one line where glibc and the C#/Go formatters differ (printf "%#.2g" of 99.995); part C's formatter
# may do either, so Fortran must equal one side exactly and the other but for that line.
quirk() {   # $1 $2 differ only by the quirk line, in either direction
	local d; d=$(diff "$1" "$2" | grep '^[<>]' || true)
	[ "$d" = $'< d 01 2 [1.0e+02]\n> d 01 2 [1.e+02]' ] || [ "$d" = $'< d 01 2 [1.e+02]\n> d 01 2 [1.0e+02]' ]
}
echo "section a: $(wc -l < build/stream_f90_a.txt) lines"
for o in cs:C# go:Go cpp:C++; do
	f=build/stream_${o%%:*}_a.txt; n=${o#*:}
	if cmp -s $f build/stream_f90_a.txt; then echo "  $n and Fortran: identical"
	elif quirk $f build/stream_f90_a.txt; then echo "  $n and Fortran: identical but for the glibc %#g quirk line (printf \"%#.2g\" of 99.995)"
	else echo "  $n and Fortran: DIFFERENT"; diff $f build/stream_f90_a.txt | head -8; fail=1; fi
done
cmp -s build/stream_cs_a.txt build/stream_f90_a.txt || cmp -s build/stream_cpp_a.txt build/stream_f90_a.txt || { echo "  Fortran equals neither C# nor C++ exactly"; fail=1; }
echo "section b: $(wc -l < build/stream_f90_b.txt) lines"
same build/stream_cs_b.txt build/stream_f90_b.txt "C# and Fortran"
same build/stream_go_b.txt build/stream_f90_b.txt "Go and Fortran"
echo "section c: $(cat build/stream_f90_c.txt)"
same build/stream_cpp_c.txt build/stream_f90_c.txt "C++ and Fortran"
same build/stream_cs_c.txt build/stream_f90_c.txt "C# and Fortran"
exit $fail
