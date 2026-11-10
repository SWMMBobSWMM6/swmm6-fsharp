#!/bin/bash
# Build both sides and compare: libstdc++ against cs/runtime/StdAlgo.cs.
set -e
cd "$(dirname "$0")"
mkdir -p build
g++ -O2 -std=c++20 -o build/sortcheck sortcheck.cpp
FW=$(ls -d /usr/lib/dotnet/shared/Microsoft.NETCore.App/8.* | head -1)
CSC=$(ls /usr/lib/dotnet/sdk/*/Roslyn/bincore/csc.dll | head -1)
REFS=$(for f in $FW/*.dll; do case $(basename $f) in Microsoft.VisualBasic*|mscorlib.dll|netstandard.dll|System.Private.*) ;; *) echo -n "-r:$f ";; esac; done)
dotnet $CSC -nologo -optimize+ -langversion:latest -out:build/sortcheck.dll $REFS -r:$FW/netstandard.dll -r:$FW/System.Private.CoreLib.dll sortcheck.cs ../../cs/runtime/StdAlgo.cs
printf '{"runtimeOptions":{"tfm":"net8.0","framework":{"name":"Microsoft.NETCore.App","version":"%s"}}}' $(basename $FW) > build/sortcheck.runtimeconfig.json
build/sortcheck > build/cpp.txt
dotnet build/sortcheck.dll > build/cs.txt
echo "cases: $(wc -l < build/cpp.txt)"
if cmp -s build/cpp.txt build/cs.txt; then echo "sort, stable_sort, partial_sort: identical permutations"; else diff build/cpp.txt build/cs.txt | head -5; exit 1; fi
