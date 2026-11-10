#!/bin/bash
# Compare the TypeScript runtime's C library (package cpp, ts/src/cpp) with the system's glibc, as ../run.sh does for
# go/cpp and ../javacheck/run.sh for the Java runtime: libm results bit for bit (per-function mismatch counts and
# ulp distances), printf output, sscanf results, from_chars / strtol / strtod / atoi / ctime / ctype / get_nprocs,
# and unit checks of the rest. The C and C++ programs are ../libm.c, ../printf.c, ../scanf.c and ../misc.cpp and use
# only the system's own libc, libm and libstdc++.
# The runtime is compiled as it stands in ts/src/cpp, from a copy in ../build/ts with an index.ts in the order
# ts/gen_index.mjs gives (--check: it writes nothing), so ts/src is not touched.
# Usage: run.sh [N] (N libm cases per function, default 300000). Set TZ to test ctime in another time zone, HIST=1
# to see each libm function's first miss of more than 10 ulps.
# Written by Claude (Anthropic) for the SWMM6 ports; see AI-USE.md.
set -e
cd "$(dirname "$0")"
TS=../../../ts
B=../build/ts
TSC=${TSC:-/home/claude/.npm-global/bin/tsc}
mkdir -p $B
for p in libm printf scanf; do gcc -O2 -ffp-contract=off -fno-builtin -o $B/$p ../$p.c -lm; done
g++ -O2 -std=c++20 -o $B/misc ../misc.cpp
rm -rf $B/src $B/check $B/js
mkdir -p $B/src/cpp $B/check
cp -r $TS/src/cpp/. $B/src/cpp/
rm -f $B/src/cpp/index.ts
{ echo "// index.ts of the checker's copy of the runtime: ts/gen_index.mjs --check's order."; node $TS/gen_index.mjs --check; } > $B/src/cpp/index.ts
cp CrtCheck.ts $B/check/
cat > $B/tsconfig.json <<JSON
{ "compilerOptions": { "target": "ES2022", "module": "nodenext", "moduleResolution": "nodenext", "rootDir": ".",
  "outDir": "js", "strict": false, "noImplicitAny": false, "skipLibCheck": true, "allowUnreachableCode": true,
  "useDefineForClassFields": true, "types": ["node"],
  "typeRoots": ["$(cd $TS && pwd)/node_modules/@types", "/opt/node-tools/node_modules/@types"] },
  "include": ["src/cpp/*.ts", "src/cpp/libm/*.ts", "check/*.ts"] }
JSON
echo '{ "type": "module" }' > $B/package.json         # ES modules (nodenext reads the nearest package.json)
$TSC -p $B/tsconfig.json
J="node $B/js/check/CrtCheck.js"
N=${1:-300000}
$B/libm $N | $J libm $N
$B/printf | $J printf
$B/scanf | $J scanf
$B/misc | $J misc
$J units
