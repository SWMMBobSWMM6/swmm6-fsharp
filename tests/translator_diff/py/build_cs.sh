#!/bin/bash
set -e
cd "$(dirname "$0")"
FW=$(ls -d /usr/lib/dotnet/shared/Microsoft.NETCore.App/8.* | head -1)
RB=$(ls -d /usr/lib/dotnet/sdk/*/Roslyn/bincore | head -1)
REFS=$(for f in $FW/*.dll; do case $(basename $f) in Microsoft.VisualBasic*|mscorlib.dll|netstandard.dll|System.Private.*) ;; *) echo -n "-r:$f ";; esac; done)
dotnet $RB/csc.dll -nologo -unsafe -nowarn:CS0108,CS0114,CS8632,CS0162,CS0168,CS0219 -langversion:latest -out:drv/dt.dll $REFS -r:$FW/netstandard.dll -r:$FW/System.Private.CoreLib.dll cs/runtime/*.cs cs/gen/*.cs drv/Driver.cs
printf '{"runtimeOptions":{"tfm":"net8.0","framework":{"name":"Microsoft.NETCore.App","version":"%s"}}}' $(basename $FW) > drv/dt.runtimeconfig.json
