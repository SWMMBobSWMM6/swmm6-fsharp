#!/bin/bash
# The TypeScript runtime's streams and file system (ts/src/cpp: streams.ts, fs.ts, fsnet.ts, rfs.ts, over the other
# parts' Str, CPtr, R.Format, ...) against libstdc++, cs/runtime/Streams.cs + Fs.cs and go/cpp/streams.go + fs.go,
# on the cases of streamcheck.cpp. run_stream.sh builds and runs the C++, C# and Go programs (set SKIP_OTHERS=1 to
# reuse their outputs in build/); this script then writes ts/src/cpp/index.ts (ts/gen_index.mjs), compiles
# streamcheck_ts/streamcheck.ts with the runtime sources (tsc, ts/tsconfig.json's options, into build/ts_out) and
# compares. Section a: TypeScript must equal C# and Go, and C++ but for the known glibc %#g quirk (see
# run_stream.sh). Section b: TypeScript must equal C# and Go. Section c: TypeScript must equal C++ and C#.
# Written by Claude (Anthropic) for the SWMM6 ports; see AI-USE.md.
set -e
cd "$(dirname "$0")"
HERE=$(pwd)
ROOT=$(cd ../.. && pwd)
TSC=${TSC:-/home/claude/.npm-global/bin/tsc}
mkdir -p build
if [ "${SKIP_OTHERS:-0}" != 1 ] || [ ! -f build/stream_cs_b.txt ]; then ./run_stream.sh || true; fi
node "$ROOT/ts/gen_index.mjs"
rm -rf build/ts_out
cat > build/tsconfig.streamcheck.json <<EOT
{ "extends": "$ROOT/ts/tsconfig.json",
  "compilerOptions": { "rootDir": "$ROOT", "outDir": "$HERE/build/ts_out" },
  "include": [], "files": ["$HERE/streamcheck_ts/streamcheck.ts"] }
EOT
if ! $TSC -p build/tsconfig.streamcheck.json > build/ts_tsc.log 2>&1; then cat build/ts_tsc.log | head -30; echo "tsc failed"; exit 1; fi
echo '{ "type": "module" }' > build/ts_out/package.json
J="node build/ts_out/tests/stdcheck/streamcheck_ts/streamcheck.js"
for s in a b c; do $J $s > build/stream_ts_$s.txt; done
fail=0
same() { if cmp -s "$1" "$2"; then echo "  $3: identical"; else echo "  $3: DIFFERENT"; diff "$1" "$2" | head -8; fail=1; fi; }
# section a: the one line where glibc and the C#/Go formatters differ (printf "%#.2g" of 99.995). The TypeScript
# R.Format may do either (part C's choice); the streams must make TypeScript equal to one side exactly and to the
# other but for that line.
q=$'< d 01 2 [1.0e+02]\n> d 01 2 [1.e+02]'
quirk() {   # $1 $2 differ only by the quirk line, in either direction
	local d; d=$(diff "$1" "$2" | grep '^[<>]' || true)
	[ "$d" = "$q" ] || [ "$d" = $'< d 01 2 [1.e+02]\n> d 01 2 [1.0e+02]' ]
}
echo "section a: $(wc -l < build/stream_ts_a.txt) lines"
for o in cs:C# go:Go cpp:C++; do
	f=build/stream_${o%%:*}_a.txt; n=${o#*:}
	if cmp -s $f build/stream_ts_a.txt; then echo "  $n and TypeScript: identical"
	elif quirk $f build/stream_ts_a.txt; then echo "  $n and TypeScript: identical but for the glibc %#g quirk line (printf \"%#.2g\" of 99.995)"
	else echo "  $n and TypeScript: DIFFERENT"; diff $f build/stream_ts_a.txt | head -8; fail=1; fi
done
cmp -s build/stream_cs_a.txt build/stream_ts_a.txt || cmp -s build/stream_cpp_a.txt build/stream_ts_a.txt || { echo "  TypeScript equals neither C# nor C++ exactly"; fail=1; }
echo "section b: $(wc -l < build/stream_ts_b.txt) lines"
same build/stream_cs_b.txt build/stream_ts_b.txt "C# and TypeScript"
same build/stream_go_b.txt build/stream_ts_b.txt "Go and TypeScript"
echo "section c: $(cat build/stream_ts_c.txt)"
same build/stream_cpp_c.txt build/stream_ts_c.txt "C++ and TypeScript"
same build/stream_cs_c.txt build/stream_ts_c.txt "C# and TypeScript"
exit $fail
