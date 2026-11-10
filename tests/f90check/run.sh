#!/bin/bash
# Part A (core) of the Fortran runtime (f90/runtime/cpp_rt_a.f90 and its submodule cpp_rt_a_crt.f90) against the C#
# runtime it is ported from: the same operations on Vec and its specialisations, iterators, pointers, Str, CPtr, Pair,
# Opt, Variant, M, Arr, the exceptions and the numeric helpers (corecheck.f90, corecheck.cs). The outputs must be
# identical. Str.stod calls part C's strtod: with part C's real module (f90/runtime/cpp_rt_c.f90 and its submodules
# cpp_rt_c_*.f90, when they build and link with part A alone) the stod lines are compared too; with part C's stub
# they are dropped from both outputs.
#   tests/f90check/run.sh            (needs dotnet 8 and gfortran 13; builds into tests/f90check/build)
# Written by Claude (Anthropic) for the SWMM6 ports; see AI-USE.md.
set -e
cd "$(dirname "$0")"
R=../../f90/runtime
mkdir -p build/cs build/f90
# C#: the runtime without the engine's hand-written partial classes and entry point (as tests/javacheck/run.sh)
FW=$(ls -d /usr/lib/dotnet/shared/Microsoft.NETCore.App/8.* | head -1)
CSC=$(ls /usr/lib/dotnet/sdk/*/Roslyn/bincore/csc.dll | head -1)
REFS=$(for f in $FW/*.dll; do case $(basename $f) in Microsoft.VisualBasic*|mscorlib.dll|netstandard.dll|System.Private.*) ;; *) echo -n "-r:$f ";; esac; done)
RT=$(ls ../../cs/runtime/*.cs | grep -av -e Program.cs -e /Hand_)
if [ ! -f build/cs/corecheck.dll ] || [ corecheck.cs -nt build/cs/corecheck.dll ]; then
  nice -n 10 dotnet $CSC -nologo -optimize+ -nullable:enable -nowarn:CS8600,CS8601,CS8602,CS8603,CS8604,CS8618,CS8625,CS0162,CS0168,CS0219,CS0414,CS0649,CS8632,CS0652,CS8765,CS1718 \
    -langversion:latest -out:build/cs/corecheck.dll $REFS -r:$FW/netstandard.dll -r:$FW/System.Private.CoreLib.dll corecheck.cs $RT
  printf '{"runtimeOptions":{"tfm":"net8.0","framework":{"name":"Microsoft.NETCore.App","version":"%s"},"configProperties":{"System.Globalization.Invariant":true}}}' $(basename $FW) > build/cs/corecheck.runtimeconfig.json
fi
dotnet build/cs/corecheck.dll > build/cs.txt
# Fortran: the engine's flags (f90/RUNTIME_PORTING.md)
FFLAGS="-O2 -fwrapv -ffp-contract=off -fno-fast-math -frecursive -ffree-line-length-none -fimplicit-none -fno-range-check"
cd build/f90
rm -f *.o *.mod *.smod corecheck
nice -n 10 gfortran $FFLAGS -c ../../$R/cpp_rt_a.f90
CPART=stub
build_with() {   # $1: the part C sources
  rm -f cpp_rt_c*.o cpp_rt_c*.mod cpp_rt_c*.smod cpp_rt_a_crt.o corecheck.o corecheck
  for f in $1; do nice -n 10 gfortran $FFLAGS -c $f || return 1; done
  nice -n 10 gfortran $FFLAGS -c ../../$R/cpp_rt_a_crt.f90 || return 1
  nice -n 10 gfortran $FFLAGS -c ../../corecheck.f90 || return 1
  nice -n 10 gfortran $FFLAGS -o corecheck corecheck.o cpp_rt_a_crt.o cpp_rt_c*.o cpp_rt_a.o
}
# part C's module and its submodules (it uses only part A), else its stub
if [ -f ../../$R/cpp_rt_c.f90 ] && build_with "../../$R/cpp_rt_c.f90 $(ls ../../$R/cpp_rt_c_*.f90 2>/dev/null)" > cpart.log 2>&1; then
  CPART=real
else
  build_with ../../$R/stub/cpp_rt_c.f90
fi
./corecheck > ../f90.txt
rm -f *.o *.mod *.smod
cd ../..
if [ $CPART = stub ]; then
  grep -av '^stod' build/cs.txt > build/cs.cmp; grep -av '^stod' build/f90.txt > build/f90.cmp
  echo "part C: stub (the stod lines are not compared)"
else
  cp build/cs.txt build/cs.cmp; cp build/f90.txt build/f90.cmp
  echo "part C: f90/runtime/cpp_rt_c.f90"
fi
echo "lines: $(wc -l < build/cs.cmp)"
if cmp -s build/cs.cmp build/f90.cmp; then echo "part A core: Fortran and C# outputs identical"; else diff build/cs.cmp build/f90.cmp | head -40; exit 1; fi
