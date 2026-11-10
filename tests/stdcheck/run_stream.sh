#!/bin/bash
# libstdc++'s iostreams against cs/runtime/Streams.cs and go/cpp/streams.go (and Fs.cs against go/cpp/fs.go).
# Section a: the three must agree, except one glibc quirk neither port reproduces (printf "%#.2g" of 99.995 is
# "1.e+02" in glibc: with '#', a rounding carry that turns %g's fixed form into the exponent form loses the
# trailing zero digits; the engine never sets showpoint). Section b: the C# port's own behaviour (getline after
# the end, token-wise extraction, .NET file system calls), which Go must reproduce. Section c: ios::cur on a file,
# where the C# port used to seek from the end of its read buffer (fixed in Streams.cs; all three must agree).
set -e
cd "$(dirname "$0")"
mkdir -p build
g++ -O2 -std=c++20 -o build/streamcheck streamcheck.cpp
FW=$(ls -d /usr/lib/dotnet/shared/Microsoft.NETCore.App/8.* | head -1)
CSC=$(ls /usr/lib/dotnet/sdk/*/Roslyn/bincore/csc.dll | head -1)
REFS=$(for f in $FW/*.dll; do case $(basename $f) in Microsoft.VisualBasic*|mscorlib.dll|netstandard.dll|System.Private.*) ;; *) echo -n "-r:$f ";; esac; done)
RT=$(ls ../../cs/runtime/*.cs | grep -v -e Program.cs -e Hand_)
dotnet $CSC -nologo -optimize+ -nowarn:CS8632,CS0162,CS0168 -langversion:latest -out:build/streamcheck.dll $REFS -r:$FW/netstandard.dll -r:$FW/System.Private.CoreLib.dll streamcheck.cs $RT
printf '{"runtimeOptions":{"tfm":"net8.0","framework":{"name":"Microsoft.NETCore.App","version":"%s"}}}' $(basename $FW) > build/streamcheck.runtimeconfig.json
(cd streamcheck_go && go build -o ../build/streamcheck_go .)
for s in a c; do build/streamcheck $s > build/stream_cpp_$s.txt; done
for s in a b c; do dotnet build/streamcheck.dll $s > build/stream_cs_$s.txt; build/streamcheck_go $s > build/stream_go_$s.txt; done
fail=0
echo "section a: $(wc -l < build/stream_cpp_a.txt) lines"
if cmp -s build/stream_cs_a.txt build/stream_go_a.txt; then echo "  C# and Go: identical"; else diff build/stream_cs_a.txt build/stream_go_a.txt | head -6; fail=1; fi
d=$(diff build/stream_cpp_a.txt build/stream_go_a.txt | grep '^[<>]' || true)
if [ "$d" = $'< d 01 2 [1.e+02]\n> d 01 2 [1.0e+02]' ]; then echo "  C++ and Go: identical but for the known glibc %#g quirk"; else echo "$d" | head -6; fail=1; fi
echo "section b: $(wc -l < build/stream_cs_b.txt) lines"
if cmp -s build/stream_cs_b.txt build/stream_go_b.txt; then echo "  C# and Go: identical"; else diff build/stream_cs_b.txt build/stream_go_b.txt | head -6; fail=1; fi
echo "section c:"
if cmp -s build/stream_cpp_c.txt build/stream_go_c.txt; then echo "  C++ and Go: identical ($(cat build/stream_go_c.txt))"; else diff build/stream_cpp_c.txt build/stream_go_c.txt; fail=1; fi
if cmp -s build/stream_cpp_c.txt build/stream_cs_c.txt; then echo "  C++ and C#: identical"; else echo "  C# port: $(cat build/stream_cs_c.txt)"; fail=1; fi
exit $fail
