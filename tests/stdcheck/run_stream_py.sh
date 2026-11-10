#!/bin/bash
# The Python runtime's streams and file system (py/cpp: streams.py, fs.py, rfs.py, fsnet.py of part D, over parts A
# and C) against libstdc++, cs/runtime/Streams.cs + Fs.cs and go/cpp/streams.go + fs.go, on the cases of
# streamcheck.cpp. run_stream.sh builds and runs the C++, C# and Go programs (set SKIP_OTHERS=1 to reuse their outputs
# in build/); this script then runs streamcheck_py/streamcheck.py (python3 -I) and compares. Section a: Python must
# equal C# and Go, and C++ but for the known glibc %#g quirk (see run_stream.sh). Section b: Python must equal C# and
# Go. Section c: Python must equal C++ and C#.
# Written by Claude (Anthropic) for the SWMM6 ports; see AI-USE.md.
set -e
cd "$(dirname "$0")"
mkdir -p build
if [ "${SKIP_OTHERS:-0}" != 1 ] || [ ! -f build/stream_cs_b.txt ]; then ./run_stream.sh || true; fi
P="python3 -I streamcheck_py/streamcheck.py"
for s in a b c; do $P $s > build/stream_py_$s.txt; done
fail=0
same() { if cmp -s "$1" "$2"; then echo "  $3: identical"; else echo "  $3: DIFFERENT"; diff "$1" "$2" | head -8; fail=1; fi; }
# section a: the one line where glibc and the C#/Go formatters differ (printf "%#.2g" of 99.995); part C's R.Format
# may do either, so Python must equal one side exactly and the other but for that line.
quirk() {   # $1 $2 differ only by the quirk line, in either direction
	local d; d=$(diff "$1" "$2" | grep '^[<>]' || true)
	[ "$d" = $'< d 01 2 [1.0e+02]\n> d 01 2 [1.e+02]' ] || [ "$d" = $'< d 01 2 [1.e+02]\n> d 01 2 [1.0e+02]' ]
}
echo "section a: $(wc -l < build/stream_py_a.txt) lines"
for o in cs:C# go:Go cpp:C++; do
	f=build/stream_${o%%:*}_a.txt; n=${o#*:}
	if cmp -s $f build/stream_py_a.txt; then echo "  $n and Python: identical"
	elif quirk $f build/stream_py_a.txt; then echo "  $n and Python: identical but for the glibc %#g quirk line (printf \"%#.2g\" of 99.995)"
	else echo "  $n and Python: DIFFERENT"; diff $f build/stream_py_a.txt | head -8; fail=1; fi
done
cmp -s build/stream_cs_a.txt build/stream_py_a.txt || cmp -s build/stream_cpp_a.txt build/stream_py_a.txt || { echo "  Python equals neither C# nor C++ exactly"; fail=1; }
echo "section b: $(wc -l < build/stream_py_b.txt) lines"
same build/stream_cs_b.txt build/stream_py_b.txt "C# and Python"
same build/stream_go_b.txt build/stream_py_b.txt "Go and Python"
echo "section c: $(cat build/stream_py_c.txt)"
same build/stream_cpp_c.txt build/stream_py_c.txt "C++ and Python"
same build/stream_cs_c.txt build/stream_py_c.txt "C# and Python"
exit $fail
