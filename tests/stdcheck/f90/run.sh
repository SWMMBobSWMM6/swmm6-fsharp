#!/bin/bash
# libstdc++ against part B of the Fortran runtime (f90/runtime/cpp_rt_b.f90 and its submodule cpp_rt_b_ext.f90:
# containers, std::hash, StdAlgo, Algo). sortcheck.f90, hashcheck.f90 and containercheck.f90 run the cases of
# sortcheck.cpp, hashcheck.cpp and containercheck.cpp; each output must be identical to the C++ one stored in
# tests/stdcheck/build (cpp.txt, hash_cpp.txt, cont_cpp.txt, made by run_ts.sh). sortcheck runs four times: Sort /
# StableSort / PartialSort over boxed structs (O) and through the double, int and long specialisations (D, I, L).
# bcheck.f90 runs what those leave out against bcheck.cpp (built here with the system g++). coverage.py checks that
# every part B entry of the manifest is there with the stub's signature.
#
# Parts A and C: the real f90/runtime/cpp_rt_a*.f90 and cpp_rt_c*.f90 when they exist and part B builds against them;
# otherwise the test stand-ins of make_standins.py (the stubs with the few bodies part B calls). Part D is its stub
# (the checks do not use it). The run says which it used; STANDIN=1 ./run.sh forces the stand-ins. Build products go
# to tests/stdcheck/build/f90b.
# Written by Claude (Anthropic) for the SWMM6 ports; see AI-USE.md.
set -e
cd "$(dirname "$0")"
HERE=$PWD
ROOT=$(cd ../../.. && pwd)
R=$ROOT/f90/runtime
B=$HERE/../build/f90b
STD=$HERE/../build
F="-O2 -fwrapv -ffp-contract=off -fno-fast-math -frecursive -ffree-line-length-none -fimplicit-none -fno-range-check"
FC="nice -n 10 gfortran"
mkdir -p $B
cd $B
rm -f *.o *.mod *.smod

build_runtime() {   # build_runtime <A module> <C module> [submodules...]: compiles A, C, their submodules, B, stub D
    rm -f *.o *.mod *.smod
    for f in "$@" $R/cpp_rt_b.f90 $R/cpp_rt_b_ext.f90 $R/stub/cpp_rt_d.f90 $R/cpp_runtime.f90; do
        $FC $F -c "$f" > build.log 2>&1 || { echo "  ($f did not compile: $(grep -m1 -i error build.log))"; return 1; }
    done
    return 0
}

used=""
if [ "$STANDIN" != 1 ] && [ -f $R/cpp_rt_a.f90 ] && [ -f $R/cpp_rt_c.f90 ]; then
    asub=$(ls $R/cpp_rt_a_*.f90 2>/dev/null || true)
    csub=$(ls $R/cpp_rt_c_*.f90 2>/dev/null || true)
    if build_runtime $R/cpp_rt_a.f90 $R/cpp_rt_c.f90 $asub $csub; then used="real parts A and C"; fi
fi
if [ -z "$used" ]; then
    python3 -I $HERE/make_standins.py $B/standin > /dev/null
    if [ "$STANDIN" != 1 ] && [ -f $R/cpp_rt_a.f90 ] && build_runtime $R/cpp_rt_a.f90 $B/standin/cpp_rt_c.f90 $(ls $R/cpp_rt_a_*.f90 2>/dev/null || true); then
        used="the real part A and the test stand-in for part C (make_standins.py)"
    else
        build_runtime $B/standin/cpp_rt_a.f90 $B/standin/cpp_rt_c.f90
        used="test stand-ins for parts A and C (make_standins.py)"
    fi
fi
echo "runtime: part B with $used, part D's stub"
python3 -I $HERE/coverage.py || fail_cov=1
RT=$(ls *.o)
$FC $F -c $HERE/chk_util.f90
for t in sortcheck hashcheck containercheck bcheck; do
    $FC $F -c $HERE/$t.f90
    $FC $F -o $t $t.o chk_util.o $RT
done
nice -n 10 g++ -O2 -std=c++20 -o bcheck_cpp $HERE/bcheck.cpp
fail=${fail_cov:-0}
check() {   # check <name> <C++ output> <Fortran output>
    if cmp -s "$2" "$3"; then echo "$1: identical"; else echo "$1: DIFFERENT"; diff "$2" "$3" | head -5 | cut -c1-300; fail=1; fi
}
echo "sort cases: $(wc -l < $STD/cpp.txt)"
for mode in O D I L; do
    ./sortcheck $mode > sort_f90_$mode.txt
    check "sort, stable_sort, partial_sort ($mode)" $STD/cpp.txt sort_f90_$mode.txt
done
./hashcheck > hash_f90.txt
echo "hash lines: $(wc -l < $STD/hash_cpp.txt)"
check "unordered_map / unordered_set order and std::hash" $STD/hash_cpp.txt hash_f90.txt
./containercheck > cont_f90.txt
echo "container lines: $(wc -l < $STD/cont_cpp.txt)"
check "64-bit keys, rehash, map / set, stack / queue, partial sorts, algorithms" $STD/cont_cpp.txt cont_f90.txt
./bcheck_cpp > bcheck_cpp.txt
./bcheck > bcheck_f90.txt
echo "bcheck lines: $(wc -l < bcheck_cpp.txt)"
check "UMapC, copies and assignment, key copies, identity sets, Algo over Str / objects / bytes / bools / Ptr" bcheck_cpp.txt bcheck_f90.txt
cp $STD/cpp.txt $STD/hash_cpp.txt $STD/cont_cpp.txt .
md5sum cpp.txt sort_f90_O.txt sort_f90_D.txt sort_f90_I.txt sort_f90_L.txt hash_cpp.txt hash_f90.txt cont_cpp.txt cont_f90.txt \
    bcheck_cpp.txt bcheck_f90.txt
rm -f cpp.txt hash_cpp.txt cont_cpp.txt *.o *.mod *.smod build.log
exit $fail
