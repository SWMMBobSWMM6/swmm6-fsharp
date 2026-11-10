#!/bin/bash
# Part A of the Python runtime (package cpp: py/cpp/core_num.py, core_ptr.py, core_str.py, core_vec.py) against the
# C# runtime it is ported from: the numeric helpers (f32, fdiv0, idiv, irem, fmod, the double -> integer
# conversions, wrap-around, bit casts) over edge values, and the same operations on Vec and its specialisations,
# iterators, pointers, Str, CPtr, Pair, Opt, Box, M, Arr, Mv, Dtor, Defer and the exceptions (corecheck.py,
# corecheck.cs). The outputs must be identical.
# Written by Claude (Anthropic) for the SWMM6 ports; see AI-USE.md.
set -e
cd "$(dirname "$0")"
mkdir -p build/cs
# C#: the runtime without the engine's hand-written partial classes and entry point (as tests/javacheck/run.sh)
FW=$(ls -d /usr/lib/dotnet/shared/Microsoft.NETCore.App/8.* | head -1)
CSC=$(ls /usr/lib/dotnet/sdk/*/Roslyn/bincore/csc.dll | head -1)
REFS=$(for f in $FW/*.dll; do case $(basename $f) in Microsoft.VisualBasic*|mscorlib.dll|netstandard.dll|System.Private.*) ;; *) echo -n "-r:$f ";; esac; done)
RT=$(ls ../../cs/runtime/*.cs | grep -v -e Program.cs -e /Hand_)
if [ ! -f build/cs/corecheck.dll ] || [ corecheck.cs -nt build/cs/corecheck.dll ]; then
  dotnet $CSC -nologo -optimize+ -nullable:enable -nowarn:CS8600,CS8601,CS8602,CS8603,CS8604,CS8618,CS8625,CS0162,CS0168,CS0219,CS0414,CS0649,CS8632,CS0652 \
    -langversion:latest -out:build/cs/corecheck.dll $REFS -r:$FW/netstandard.dll -r:$FW/System.Private.CoreLib.dll corecheck.cs $RT
  printf '{"runtimeOptions":{"tfm":"net8.0","framework":{"name":"Microsoft.NETCore.App","version":"%s"},"configProperties":{"System.Globalization.Invariant":true}}}' $(basename $FW) > build/cs/corecheck.runtimeconfig.json
fi
dotnet build/cs/corecheck.dll > build/cs.txt
python3 -I -B corecheck.py > build/py.txt
echo "lines: $(wc -l < build/cs.txt)"
if cmp -s build/cs.txt build/py.txt; then echo "part A core: Python and C# outputs identical"; else diff build/cs.txt build/py.txt | head -40; exit 1; fi
