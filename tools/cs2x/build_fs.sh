#!/bin/bash
# Build the C#-to-F# translator (bin/cs2fs.dll) against the Roslyn assemblies that ship with the .NET SDK.
# Written by Claude (Anthropic) for Robert Dickinson's SWMM6 ports; see AI-USE.md and fsharp/README.md.
set -e
cd "$(dirname "$0")"
FW=$(ls -d /usr/lib/dotnet/shared/Microsoft.NETCore.App/8.* | head -1)
RB=$(ls -d /usr/lib/dotnet/sdk/*/Roslyn/bincore | head -1)
REFS=$(for f in $FW/*.dll; do case $(basename $f) in Microsoft.VisualBasic*|mscorlib.dll|netstandard.dll|System.Private.*) ;; *) echo -n "-r:$f ";; esac; done)
mkdir -p bin
dotnet $RB/csc.dll -nologo -optimize+ -nullable:enable -nowarn:CS8600,CS8602,CS8603,CS8604,CS8618,CS8625 -langversion:latest -out:bin/cs2fs.dll $REFS -r:$FW/netstandard.dll -r:$FW/System.Private.CoreLib.dll \
  -r:$RB/Microsoft.CodeAnalysis.dll -r:$RB/Microsoft.CodeAnalysis.CSharp.dll Cs2Fs.cs FsTypes.cs FsFn.cs
for f in Microsoft.CodeAnalysis.dll Microsoft.CodeAnalysis.CSharp.dll; do cmp -s $RB/$f bin/$f || cp $RB/$f bin/$f; done   # rewriting a DLL a running translator has mapped kills it (SIGBUS)
printf '{"runtimeOptions":{"tfm":"net8.0","framework":{"name":"Microsoft.NETCore.App","version":"%s"}}}' $(basename $FW) > bin/cs2fs.runtimeconfig.json
