#!/bin/bash
# run_tests.sh [N] [N2]
# Check the TypeScript, Java and Go ports of glibc's math routines bit for bit against the system libm (glibc
# 2.39, the library the C++ engine calls):
#   set 1 (glibm.*: exp, log, log10, pow, sin, cos, tan, asin, acos, atan, and fma): N random arguments per
#     function over the ranges SWMM uses (mkvectors.c; plus the census samples if LIBM_CENSUS_DIR is set) and an
#     edge-case set (mkedge.c), as in swmm5-ports/tools/libm; also TypeScript pow's fast path against pow_exact;
#   set 2 (glibm2.*: cbrt, hypot, atan2, cosh, sinh, tanh, log2, expm1, log1p, and the runtime's frexp and
#     ldexp): N2 random arguments per function over wide ranges and every branch threshold, plus +-0, +-inf,
#     subnormals and NaNs of both signs and several payloads, and every pair of special values for atan2 and
#     hypot (mkvectors2.c); a NaN must match in sign and payload. Java runs set 2 twice, JIT and -Xint.
# Every line must say 0 differ (and 0 NaN sign, 0 NaN payload). W (default build/libm_test) holds the vectors,
# about 0.3 GB for the defaults, removed at the end unless KEEP=1. Needs gcc, node and tsc (TSC), javac, go.
# The glibc-derived code runs only as the translations; the reference values come from the system's libm.
# Written by Claude (Anthropic); see AI-USE.md.
set -e
HERE=$(cd "$(dirname "$0")" && pwd); ROOT=$(cd "$HERE/../../.." && pwd)
N=${1:-400000}; N2=${2:-1000000}; W=${W:-$ROOT/build/libm_test}; mkdir -p "$W/ts" "$W/java/cpp"
TSC=${TSC:-/home/claude/.npm-global/bin/tsc}
gcc -O2 -o "$W/mkvectors" "$HERE/mkvectors.c" -lm
gcc -O2 -o "$W/mkedge" "$HERE/mkedge.c" -lm
gcc -O2 -fno-builtin -o "$W/mkvectors2" "$HERE/mkvectors2.c" -lm
CENSUS=${LIBM_CENSUS_DIR:-}   # optional: per-model samples written by swmm5-ports/tools/libm/run_census.sh
"$W/mkvectors" "$N" "$W/vectors.bin" $CENSUS
"$W/mkedge" 100000 "$W/edge.bin"
"$W/mkvectors2" "$N2" "$W/vectors2.bin"

echo "== TypeScript"
L=$ROOT/ts/src/cpp/libm
cp "$L/glibm.ts" "$L/glibm_rt.ts" "$L/glibm2.ts" "$L/glibm2_rt.ts" "$HERE/check.ts" "$HERE/powdiff.ts" "$HERE/check2.ts" "$W/ts/"
cat > "$W/ts/tsconfig.json" <<'J'
{ "compilerOptions": { "target": "ES2022", "module": "nodenext", "moduleResolution": "nodenext", "outDir": "dist", "strict": false,
    "skipLibCheck": true, "types": ["node"], "typeRoots": ["/opt/node-tools/node_modules/@types"] }, "include": ["*.ts"] }
J
mkdir -p "$W/ts/dist"; echo '{"type":"module"}' > "$W/ts/package.json"; cp "$W/ts/package.json" "$W/ts/dist/"
(cd "$W/ts" && "$TSC" -p . && cd dist && node check.js "$W/vectors.bin" && node check.js "$W/edge.bin" && node powdiff.js 20 \
  && node check2.js "$W/vectors2.bin")

echo "== Java"
J=$ROOT/java/src/cpp
cp "$J/Glibm.java" "$J/GlibmRt.java" "$J/Glibm2.java" "$J/Glibm2Rt.java" "$HERE/Check.java" "$HERE/Check2.java" "$W/java/cpp/"
(cd "$W/java" && javac -nowarn -d classes cpp/*.java && java -cp classes cpp.Check "$W/vectors.bin" && java -cp classes cpp.Check "$W/edge.bin" \
  && java -cp classes cpp.Check2 "$W/vectors2.bin" && echo "-- Java -Xint (interpreter)" && java -Xint -cp classes cpp.Check2 "$W/vectors2.bin") 2>&1 | grep -v '^Picked up'

echo "== Go"
(cd "$ROOT/go" && go run "$HERE/check.go" "$W/vectors.bin" && go run "$HERE/check.go" "$W/edge.bin" && go run "$HERE/check2.go" "$W/vectors2.bin")
[ "${KEEP:-0}" = 1 ] || rm -f "$W/vectors.bin" "$W/edge.bin" "$W/vectors2.bin"
