#!/bin/bash
# run_libm_tests.sh [N] [N2] - check the F# port's math bit for bit against the system libm (glibc 2.39, the library
# the C++ SWMM6 engine calls), by the method of tools/libm/test/run_tests.sh:
#   set 1: N random arguments per function over the ranges SWMM uses plus special values (tools/libm/test/mkvectors.c,
#     default 1,000,000) and 100,000 edge cases per function (mkedge.c): exp, log, log10, pow, sin, cos, tan, asin,
#     acos, atan;
#   set 2: N2 random arguments per function over wide ranges and every branch threshold, +-0, +-inf, subnormals and NaNs
#     of both signs with several payloads, every pair of special values for atan2 and hypot (mkvectors2.c, default
#     1,000,000): cbrt, hypot, atan2, cosh, sinh, tanh, log2, expm1, log1p;
#   exact: sqrt, floor, ceil, trunc, fabs, copysign (mkexact.c, N arguments each plus special values), which the engine
#     takes from .NET's Math.
# A NaN must match in sign and payload. The F# side is fsharp/runtime/LibM.fs compiled with test/LibmCheck.fs. The
# vectors (about 0.5 GB) go to W (default /dev/shm/fs6_libm_test) and are deleted at the end.
# Written by Claude (Anthropic) for Robert Dickinson's SWMM6 ports; see ../README.md.
set -e
HERE=$(cd "$(dirname "$0")" && pwd); F=$(cd "$HERE/.." && pwd); ROOT=$(cd "$F/.." && pwd)
N=${1:-1000000}; N2=${2:-1000000}; W=${W:-/dev/shm/fs6_libm_test}; rm -rf "$W"; mkdir -p "$W"
T=$ROOT/tools/libm/test
gcc -O2 -o "$W/mkvectors" "$T/mkvectors.c" -lm
gcc -O2 -o "$W/mkedge" "$T/mkedge.c" -lm
gcc -O2 -fno-builtin -o "$W/mkvectors2" "$T/mkvectors2.c" -lm
gcc -O2 -fno-builtin -o "$W/mkexact" "$HERE/mkexact.c" -lm
"$W/mkvectors" "$N" "$W/vectors.bin" > /dev/null 2>&1
"$W/mkedge" 100000 "$W/edge.bin" > /dev/null 2>&1
"$W/mkvectors2" "$N2" "$W/vectors2.bin" > /dev/null 2>&1
"$W/mkexact" "$N" "$W/exact.bin" > /dev/null 2>&1
SDK=$(ls -d /usr/lib/dotnet/sdk/8.* | head -1)
FW=$(ls -d /usr/lib/dotnet/shared/Microsoft.NETCore.App/8.* | head -1)
REFS=$(for f in $FW/*.dll; do case $(basename $f) in Microsoft.VisualBasic*|mscorlib.dll|netstandard.dll|System.Private.*) ;; *) echo -n "-r:$f ";; esac; done)
unzip -o -q -j "$SDK/FSharp/library-packs/FSharp.Core.8.0.102.nupkg" lib/netstandard2.1/FSharp.Core.dll -d "$W"
dotnet "$SDK/FSharp/fsc.dll" --nologo --noframework --optimize+ --target:exe --out:"$W/libmcheck.dll" \
  $REFS -r:$FW/netstandard.dll -r:$FW/System.Private.CoreLib.dll -r:$FW/mscorlib.dll -r:"$W/FSharp.Core.dll" "$F/runtime/LibM.fs" "$HERE/LibmCheck.fs"
printf '{"runtimeOptions":{"tfm":"net8.0","framework":{"name":"Microsoft.NETCore.App","version":"%s"}}}' $(basename $FW) > "$W/libmcheck.runtimeconfig.json"
dotnet "$W/libmcheck.dll" "$W/vectors.bin" "$W/edge.bin" "$W/vectors2.bin" "$W/exact.bin"
rm -rf "$W"
