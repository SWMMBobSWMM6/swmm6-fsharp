#!/bin/bash
# The Java runtime's streams and file system (java/src/cpp: Streams, OStream, IStream, ..., FsPath, Fs) against
# libstdc++, cs/runtime/Streams.cs + Fs.cs and go/cpp/streams.go + fs.go, on the cases of streamcheck.cpp.
# run_stream.sh builds and runs the C++, C# and Go programs (set SKIP_OTHERS=1 to reuse their outputs in build/);
# this script then builds streamcheck_java/StreamCheck.java against the runtime sources (javac -sourcepath, so only
# the classes it uses are compiled) and compares. Section a: Java must equal C# and Go, and C++ but for the known
# glibc %#g quirk (see run_stream.sh). Section b: Java must equal C# and Go. Section c: Java must equal C++ and C#.
# Written by Claude (Anthropic) for the SWMM6 ports; see AI-USE.md.
set -e
cd "$(dirname "$0")"
mkdir -p build
if [ "${SKIP_OTHERS:-0}" != 1 ] || [ ! -f build/stream_cs_b.txt ]; then ./run_stream.sh || true; fi
rm -rf build/java_classes
javac -J-Xmx1g --release 21 -nowarn -encoding UTF-8 -d build/java_classes -sourcepath ../../java/src streamcheck_java/StreamCheck.java 2>&1 | grep -v '^Picked up' || true
J="java -Xss16m -XX:+UseSerialGC -cp build/java_classes StreamCheck"
for s in a b c; do $J $s > build/stream_java_$s.txt 2> >(grep -v '^Picked up' >&2); done
fail=0
same() { if cmp -s "$1" "$2"; then echo "  $3: identical"; else echo "  $3: DIFFERENT"; diff "$1" "$2" | head -8; fail=1; fi; }
# section a: the one line where glibc and the C#/Go formatters differ (printf "%#.2g" of 99.995). Java's
# R.Format may do either (part C's choice); the streams must make Java equal to one side exactly and to the other
# but for that line.
q=$'< d 01 2 [1.0e+02]\n> d 01 2 [1.e+02]'
quirk() {   # $1 $2 differ only by the quirk line, in either direction
	local d; d=$(diff "$1" "$2" | grep '^[<>]' || true)
	[ "$d" = "$q" ] || [ "$d" = $'< d 01 2 [1.e+02]\n> d 01 2 [1.0e+02]' ]
}
echo "section a: $(wc -l < build/stream_java_a.txt) lines"
for o in cs:C# go:Go cpp:C++; do
	f=build/stream_${o%%:*}_a.txt; n=${o#*:}
	if cmp -s $f build/stream_java_a.txt; then echo "  $n and Java: identical"
	elif quirk $f build/stream_java_a.txt; then echo "  $n and Java: identical but for the glibc %#g quirk line (printf \"%#.2g\" of 99.995)"
	else echo "  $n and Java: DIFFERENT"; diff $f build/stream_java_a.txt | head -8; fail=1; fi
done
cmp -s build/stream_cs_a.txt build/stream_java_a.txt || cmp -s build/stream_cpp_a.txt build/stream_java_a.txt || { echo "  Java equals neither C# nor C++ exactly"; fail=1; }
echo "section b: $(wc -l < build/stream_java_b.txt) lines"
same build/stream_cs_b.txt build/stream_java_b.txt "C# and Java"
same build/stream_go_b.txt build/stream_java_b.txt "Go and Java"
echo "section c: $(cat build/stream_java_c.txt)"
same build/stream_cpp_c.txt build/stream_java_c.txt "C++ and Java"
same build/stream_cs_c.txt build/stream_java_c.txt "C# and Java"
exit $fail
