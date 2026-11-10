#!/bin/bash
# run_runtime_checks.sh [N] - check the F# runtime (fsharp/runtime, namespace Cpp) with the checks of tests/ that prove
# the C# runtime it was translated from. The F# runtime is compiled on its own into a library (fsrt.dll) with the
# same public shape as cs/runtime (the same classes, static members, fields and ref parameters), so the C# check
# programs compile against it unchanged; each is also built from cs/runtime's sources as the control.
#   crtcheck  fsharp/test/CrtCheck.cs (tests/crtcheck/javacheck/CrtCheck.java in C#) against the system's glibc
#             (tests/crtcheck: libm.c, printf.c, scanf.c, misc.cpp): libm bit for bit, printf output, sscanf results,
#             from_chars / strtol / strtod / atoi / ctime / ctype, and unit checks; then the F# and C# runtimes'
#             results case by case (the C# runtime differs from glibc on a few kinds of case the engine does not
#             meet, and the F# runtime must differ in the same way)
#   sort/hash tests/stdcheck/sortcheck.cs and hashcheck.cs against libstdc++ (sortcheck.cpp, hashcheck.cpp)
#   streams   tests/stdcheck/streamcheck.cs against libstdc++ (streamcheck.cpp) and the C# runtime
#   core      tests/javacheck/corecheck.cs against the C# runtime
# Everything is built in W (default /dev/shm/fs6_rtcheck) and deleted at the end (KEEP=1 keeps it); tests/ and
# cs/ are only read.
# Written by Claude (Anthropic) for Robert Dickinson's SWMM6 ports; see ../README.md.
set -e
HERE=$(cd "$(dirname "$0")" && pwd); K=$(cd "$HERE/.." && pwd); ROOT=$(cd "$K/.." && pwd); T=$ROOT/tests
N=${1:-300000}; W=${W:-/dev/shm/fs6_rtcheck}; rm -rf "$W"; mkdir -p "$W/fs" "$W/cs" "$W/s/build"
SDK=$(ls -d /usr/lib/dotnet/sdk/8.* | head -1)
FW=$(ls -d /usr/lib/dotnet/shared/Microsoft.NETCore.App/8.* | head -1)
CSC=$SDK/Roslyn/bincore/csc.dll
REFS=$(for f in $FW/*.dll; do case $(basename $f) in Microsoft.VisualBasic*|mscorlib.dll|netstandard.dll|System.Private.*) ;; *) echo -n "-r:$f ";; esac; done)
REFS="$REFS -r:$FW/netstandard.dll -r:$FW/System.Private.CoreLib.dll -r:$FW/mscorlib.dll"
RT=$(ls "$ROOT"/cs/runtime/*.cs | grep -v -e Program.cs -e /Hand_)
NW=CS8600,CS8601,CS8602,CS8603,CS8604,CS8618,CS8625,CS0162,CS0168,CS0219,CS0414,CS0649,CS8632
cfg() { printf '{"runtimeOptions":{"tfm":"net8.0","framework":{"name":"Microsoft.NETCore.App","version":"%s"},"configProperties":{"System.Globalization.Invariant":true}}}' $(basename $FW) > "$1"; }
fail=0
echo "== compiling the F# runtime (fsharp/runtime) as a library"
unzip -q -o -j "$SDK/FSharp/library-packs/FSharp.Core.8.0.102.nupkg" lib/netstandard2.1/FSharp.Core.dll -d "$W/fs"
R=$K/runtime
dotnet "$SDK/FSharp/fsc.dll" --nologo --noframework --optimize+ --target:library --out:"$W/fs/fsrt.dll" \
  --nowarn:9,20,25,26,40,44,51,52,57,64,760,864,1182,3370,3391,3536,3560 $REFS -r:"$W/fs/FSharp.Core.dll" \
  "$R/LibM.fs" "$R/PrimeList.fs" "$R/StdAlgo.fs" "$R/Runtime.fs" | grep -v warning || true
[ -f "$W/fs/fsrt.dll" ] || { echo "runtime build failed"; exit 1; }
# build NAME SOURCES...: the check program against the F# runtime (W/fs) and against cs/runtime's sources (W/cs)
build() {
	local n=$1; shift
	dotnet "$CSC" -nologo -optimize+ -nullable:enable -nowarn:$NW -langversion:latest -define:FSRT -out:"$W/fs/$n.dll" $REFS \
	  -r:"$W/fs/fsrt.dll" -r:"$W/fs/FSharp.Core.dll" "$@" > "$W/fs/$n.log" || { cat "$W/fs/$n.log" | grep error | head; exit 1; }
	dotnet "$CSC" -nologo -optimize+ -nullable:enable -nowarn:$NW -langversion:latest -out:"$W/cs/$n.dll" $REFS "$@" $RT > "$W/cs/$n.log" \
	  || { cat "$W/cs/$n.log" | grep error | head; exit 1; }
	cfg "$W/fs/$n.runtimeconfig.json"; cfg "$W/cs/$n.runtimeconfig.json"
}
same() { if cmp -s "$1" "$2"; then echo "  $3: identical"; else echo "  $3: DIFFERENT"; diff "$1" "$2" | head -8 | cut -c1-300; fail=1; fi; }

echo "== crtcheck: the C library against glibc, and against the C# runtime case by case"
for p in libm printf scanf; do gcc -O2 -ffp-contract=off -fno-builtin -o "$W/$p" "$T/crtcheck/$p.c" -lm; done
g++ -O2 -std=c++20 -o "$W/misc" "$T/crtcheck/misc.cpp"
build crtcheck "$HERE/CrtCheck.cs"
"$W/libm" $N | dotnet "$W/fs/crtcheck.dll" libm $N | tee "$W/libm_fs.txt"
grep -q '^total mismatches: 0$' "$W/libm_fs.txt" || fail=1
for c in printf scanf misc; do
	for s in fs cs; do "$W/$c" | CRT_DUMP="$W/${c}_$s.dump" dotnet "$W/$s/crtcheck.dll" $c > "$W/${c}_$s.txt"; done
	echo "$c, F# runtime against glibc: $(tail -1 "$W/${c}_fs.txt")"
	echo "$c, C# runtime against glibc: $(tail -1 "$W/${c}_cs.txt")"
	same "$W/${c}_cs.dump" "$W/${c}_fs.dump" "$c results ($(wc -l < "$W/${c}_fs.dump") cases), C# and F# runtimes"
done
(cd "$W" && dotnet "$W/fs/crtcheck.dll" units > "$W/units_fs.txt"; dotnet "$W/cs/crtcheck.dll" units > "$W/units_cs.txt") || true
echo "units, F# runtime: $(tail -1 "$W/units_fs.txt")"; grep FAIL "$W/units_fs.txt" | sed 's/^/  /' | cut -c1-200 || true
echo "units, C# runtime: $(tail -1 "$W/units_cs.txt")"
same "$W/units_cs.txt" "$W/units_fs.txt" "units output, C# and F# runtimes"

echo "== sort and hash: StdAlgo, the hash tables and std::hash against libstdc++"
g++ -O2 -std=c++20 -o "$W/sortcheck" "$T/stdcheck/sortcheck.cpp"; g++ -O2 -std=c++20 -o "$W/hashcheck" "$T/stdcheck/hashcheck.cpp"
build sortcheck "$T/stdcheck/sortcheck.cs"; build hashcheck "$T/stdcheck/hashcheck.cs"
"$W/sortcheck" > "$W/sort_cpp.txt"; "$W/hashcheck" > "$W/hash_cpp.txt"
dotnet "$W/fs/sortcheck.dll" > "$W/sort_fs.txt"; dotnet "$W/fs/hashcheck.dll" > "$W/hash_fs.txt"
echo "sort cases: $(wc -l < "$W/sort_cpp.txt")"
same "$W/sort_cpp.txt" "$W/sort_fs.txt" "sort, stable_sort, partial_sort permutations, libstdc++ and F#"
echo "hash lines: $(wc -l < "$W/hash_cpp.txt")"
same "$W/hash_cpp.txt" "$W/hash_fs.txt" "unordered_map / unordered_set order and std::hash, libstdc++ and F#"

echo "== streams and file system against libstdc++ and the C# runtime"
g++ -O2 -std=c++20 -o "$W/streamcheck" "$T/stdcheck/streamcheck.cpp"
build streamcheck "$T/stdcheck/streamcheck.cs"
for s in a c; do (cd "$W/s" && "$W/streamcheck" $s > "$W/stream_cpp_$s.txt"); done
for s in a b c; do
	(cd "$W/s" && dotnet "$W/fs/streamcheck.dll" $s > "$W/stream_fs_$s.txt")
	(cd "$W/s" && dotnet "$W/cs/streamcheck.dll" $s > "$W/stream_cs_$s.txt")
done
quirk() { local d; d=$(diff "$1" "$2" | grep '^[<>]' || true); [ "$d" = $'< d 01 2 [1.0e+02]\n> d 01 2 [1.e+02]' ] || [ "$d" = $'< d 01 2 [1.e+02]\n> d 01 2 [1.0e+02]' ]; }
echo "section a: $(wc -l < "$W/stream_fs_a.txt") lines"
same "$W/stream_cs_a.txt" "$W/stream_fs_a.txt" "C# and F#"
if cmp -s "$W/stream_cpp_a.txt" "$W/stream_fs_a.txt"; then echo "  C++ and F#: identical"
elif quirk "$W/stream_cpp_a.txt" "$W/stream_fs_a.txt"; then echo "  C++ and F#: identical but for the glibc %#g quirk line (printf \"%#.2g\" of 99.995)"
else echo "  C++ and F#: DIFFERENT"; diff "$W/stream_cpp_a.txt" "$W/stream_fs_a.txt" | head -8; fail=1; fi
echo "section b: $(wc -l < "$W/stream_fs_b.txt") lines"
same "$W/stream_cs_b.txt" "$W/stream_fs_b.txt" "C# and F#"
echo "section c: $(cat "$W/stream_fs_c.txt")"
same "$W/stream_cpp_c.txt" "$W/stream_fs_c.txt" "C++ and F#"
same "$W/stream_cs_c.txt" "$W/stream_fs_c.txt" "C# and F#"

echo "== core: Vec, iterators, pointers, Str, CPtr, Pair, Opt, ... against the C# runtime"
build corecheck "$T/javacheck/corecheck.cs"
dotnet "$W/cs/corecheck.dll" > "$W/core_cs.txt"; dotnet "$W/fs/corecheck.dll" > "$W/core_fs.txt"
echo "lines: $(wc -l < "$W/core_cs.txt")"
same "$W/core_cs.txt" "$W/core_fs.txt" "core output, C# and F#"
[ "${KEEP:-0}" = 1 ] || rm -rf "$W"
echo "exit=$fail"
exit $fail
