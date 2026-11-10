#!/bin/bash
# libstdc++'s hash tables against cs/runtime/Containers.cs.
set -e
cd "$(dirname "$0")"
mkdir -p build
g++ -O2 -std=c++20 -o build/hashcheck hashcheck.cpp
FW=$(ls -d /usr/lib/dotnet/shared/Microsoft.NETCore.App/8.* | head -1)
CSC=$(ls /usr/lib/dotnet/sdk/*/Roslyn/bincore/csc.dll | head -1)
REFS=$(for f in $FW/*.dll; do case $(basename $f) in Microsoft.VisualBasic*|mscorlib.dll|netstandard.dll|System.Private.*) ;; *) echo -n "-r:$f ";; esac; done)
R=../../cs/runtime
dotnet $CSC -nologo -optimize+ -nowarn:CS8632,CS0162,CS0168 -langversion:latest -out:build/hashcheck.dll $REFS -r:$FW/netstandard.dll -r:$FW/System.Private.CoreLib.dll hashcheck.cs $R/Containers.cs $R/PrimeList.cs $R/Std.cs $R/Str.cs $R/CRuntime.cs $R/StdAlgo.cs $R/Stash.cs
printf '{"runtimeOptions":{"tfm":"net8.0","framework":{"name":"Microsoft.NETCore.App","version":"%s"}}}' $(basename $FW) > build/hashcheck.runtimeconfig.json
build/hashcheck > build/hash_cpp.txt
dotnet build/hashcheck.dll > build/hash_cs.txt
echo "lines: $(wc -l < build/hash_cpp.txt)"
if cmp -s build/hash_cpp.txt build/hash_cs.txt; then echo "unordered_map / unordered_set order and std::hash: identical"; else diff build/hash_cpp.txt build/hash_cs.txt | head -6 | cut -c1-300; exit 1; fi
