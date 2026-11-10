#!/bin/bash
# build.sh - build the F# SWMM6 engine (bin/runswmm6.dll) with the F# compiler that ships in the .NET 8 SDK (fsc.dll),
# against the shared framework's reference assemblies and FSharp.Core 8.0.102 from the SDK's offline package
# (FSharp/library-packs); no NuGet restore is needed. The runtime (runtime/*.fs, the F# translation of cs/runtime)
# and the generated engine (gen/Engine.fs, written by tools/cs2x/Cs2Fs.cs) are compiled together into one assembly;
# nothing references the C# port.
#   OUT=dir build.sh     build into another folder (default bin)
# Written by Claude (Anthropic) for Robert Dickinson's SWMM6 ports; see ../AI-USE.md and README.md.
set -e
cd "$(dirname "$0")"
SDK=$(ls -d /usr/lib/dotnet/sdk/8.* | head -1)
FW=$(ls -d /usr/lib/dotnet/shared/Microsoft.NETCore.App/8.* | head -1)
OUT=${OUT:-bin}
OBJ=${OBJ:-obj}
mkdir -p "$OUT" "$OBJ"
if [ ! -f "$OBJ/FSharp.Core.dll" ]; then
  unzip -o -q -j "$SDK/FSharp/library-packs/FSharp.Core.8.0.102.nupkg" lib/netstandard2.1/FSharp.Core.dll -d "$OBJ"
fi
REFS=$(for f in $FW/*.dll; do case $(basename $f) in Microsoft.VisualBasic*|mscorlib.dll|netstandard.dll|System.Private.*) ;; *) echo -n "-r:$f ";; esac; done)
dotnet "$SDK/FSharp/fsc.dll" --nologo --noframework --optimize+ --debug:portable --deterministic+ \
  --pathmap:"$PWD"=/_/fsharp --target:exe --out:"$OUT/runswmm6.dll" \
  --nowarn:9,20,25,26,40,44,51,52,57,64,760,864,1182,3370,3391,3536,3560 \
  $REFS -r:$FW/netstandard.dll -r:$FW/System.Private.CoreLib.dll -r:$FW/mscorlib.dll -r:"$OBJ/FSharp.Core.dll" \
  runtime/LibM.fs runtime/PrimeList.fs runtime/StdAlgo.fs runtime/Runtime.fs gen/Engine.fs runtime/Program.fs "$@"
cp "$OBJ/FSharp.Core.dll" "$OUT/"
printf '{"runtimeOptions":{"tfm":"net8.0","framework":{"name":"Microsoft.NETCore.App","version":"%s"},"configProperties":{"System.Globalization.Invariant":true,"System.Runtime.TieredPGO":true}}}' $(basename $FW) > "$OUT/runswmm6.runtimeconfig.json"
