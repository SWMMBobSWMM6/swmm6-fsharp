#!/bin/bash
# Part A of the Java runtime (package cpp) against the C# runtime it is ported from: the same operations on Vec and
# its specialisations, iterators, pointers, Str, CPtr, Pair, Opt, M, Arr, Mv, Dtor, Defer and the exceptions
# (CoreCheck.java, corecheck.cs). The outputs must be identical.
# Written by Claude (Anthropic) for the SWMM6 ports; see AI-USE.md.
set -e
cd "$(dirname "$0")"
mkdir -p build/java build/cs
# C#: the runtime without the engine's hand-written partial classes and entry point
FW=$(ls -d /usr/lib/dotnet/shared/Microsoft.NETCore.App/8.* | head -1)
CSC=$(ls /usr/lib/dotnet/sdk/*/Roslyn/bincore/csc.dll | head -1)
REFS=$(for f in $FW/*.dll; do case $(basename $f) in Microsoft.VisualBasic*|mscorlib.dll|netstandard.dll|System.Private.*) ;; *) echo -n "-r:$f ";; esac; done)
RT=$(ls ../../cs/runtime/*.cs | grep -v -e Program.cs -e /Hand_)
dotnet $CSC -nologo -optimize+ -nullable:enable -nowarn:CS8600,CS8601,CS8602,CS8603,CS8604,CS8618,CS8625,CS0162,CS0168,CS0219,CS0414,CS0649,CS8632 \
  -langversion:latest -out:build/cs/corecheck.dll $REFS -r:$FW/netstandard.dll -r:$FW/System.Private.CoreLib.dll corecheck.cs $RT
printf '{"runtimeOptions":{"tfm":"net8.0","framework":{"name":"Microsoft.NETCore.App","version":"%s"},"configProperties":{"System.Globalization.Invariant":true}}}' $(basename $FW) > build/cs/corecheck.runtimeconfig.json
# Java: package cpp from java/src/cpp, then the check
javac --release 21 -nowarn -Xlint:none -d build/java ../../java/src/cpp/*.java
javac --release 21 -nowarn -Xlint:none -cp build/java -d build/java CoreCheck.java
dotnet build/cs/corecheck.dll > build/cs.txt
java -cp build/java CoreCheck > build/java.txt
echo "lines: $(wc -l < build/cs.txt)"
if cmp -s build/cs.txt build/java.txt; then echo "part A core: Java and C# outputs identical"; else diff build/cs.txt build/java.txt | head -20; exit 1; fi
