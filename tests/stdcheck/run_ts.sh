#!/bin/bash
# libstdc++ against the TypeScript runtime (ts/src/cpp: stdalgo.ts, containers.ts, algo.ts, primelist.ts).
# sortcheck.mts, hashcheck.mts and containercheck.mts run the cases of sortcheck.cpp, hashcheck.cpp and
# containercheck.cpp; each output must be identical to the C++ one. sortcheck.mts runs twice: Sort / StableSort /
# PartialSort over structs (O) and over numbers with bigint bounds (N). The runtime is compiled with the tests by
# tsc into build/ts (the runtime's index.ts is made by ts/gen_index.mjs, as ts/build.sh makes it, when it is
# missing), run with Node. The prime table (primelist.ts) is checked against the system libstdc++'s too.
# Written by Claude (Anthropic) for the SWMM6 ports; see AI-USE.md.
set -e
cd "$(dirname "$0")"
TSC=${TSC:-$(command -v tsc || echo /home/claude/.npm-global/bin/tsc)}
NODE=${NODE:-node}
ROOT=$(cd ../.. && pwd)
mkdir -p build/ts
g++ -O2 -std=c++20 -o build/sortcheck sortcheck.cpp
g++ -O2 -std=c++20 -o build/hashcheck hashcheck.cpp
g++ -O2 -std=c++20 -o build/containercheck containercheck.cpp
g++ -O2 -std=c++20 -o build/primes primes.cpp
[ -f "$ROOT/ts/src/cpp/index.ts" ] || $NODE "$ROOT/ts/gen_index.mjs"
cat > build/ts/tsconfig.json <<EOF
{ "compilerOptions": { "target": "ES2022", "module": "nodenext", "moduleResolution": "nodenext",
    "rootDir": "$ROOT", "outDir": "out", "strict": false, "noImplicitAny": false, "skipLibCheck": true,
    "allowUnreachableCode": true, "useDefineForClassFields": true, "sourceMap": false,
    "types": ["node"], "typeRoots": ["$ROOT/ts/node_modules/@types", "/opt/node-tools/node_modules/@types"] },
  "include": ["$ROOT/ts/src/cpp/**/*.ts", "$ROOT/tests/stdcheck/sortcheck.mts", "$ROOT/tests/stdcheck/hashcheck.mts",
    "$ROOT/tests/stdcheck/containercheck.mts"] }
EOF
rm -rf build/ts/out
# errors in the runtime's other files do not stop the check (tsc still emits); they are listed
$TSC -p build/ts/tsconfig.json || echo "(tsc reported the errors above; running what it emitted)"
echo '{"type":"module"}' > build/ts/out/package.json
T=build/ts/out/tests/stdcheck
build/sortcheck > build/cpp.txt
build/hashcheck > build/hash_cpp.txt
build/containercheck > build/cont_cpp.txt
fail=0
check() {   # check <name> <C++ output> <TypeScript output>
    if cmp -s "$2" "$3"; then echo "$1: identical"; else echo "$1: DIFFERENT"; diff "$2" "$3" | head -5 | cut -c1-300; fail=1; fi
}
echo "sort cases: $(wc -l < build/cpp.txt)"
for mode in O N; do
    $NODE $T/sortcheck.mjs $mode > build/sort_ts_$mode.txt
    check "sort, stable_sort, partial_sort ($mode)" build/cpp.txt build/sort_ts_$mode.txt
done
$NODE $T/hashcheck.mjs > build/hash_ts.txt
echo "hash lines: $(wc -l < build/hash_cpp.txt)"
check "unordered_map / unordered_set order and std::hash" build/hash_cpp.txt build/hash_ts.txt
$NODE $T/containercheck.mjs > build/cont_ts.txt
echo "container lines: $(wc -l < build/cont_cpp.txt)"
check "64-bit keys, rehash, map / set, stack / queue, partial sorts, algorithms" build/cont_cpp.txt build/cont_ts.txt
# the prime table against the system libstdc++'s
build/primes | grep -o '[0-9]*UL' | tr -d UL > build/primes_cpp.txt
$NODE --input-type=module -e "import { PrimeList } from '$PWD/build/ts/out/ts/src/cpp/primelist.js'; console.log(PrimeList.P.join('\\n'));" > build/primes_ts.txt
check "prime list ($(wc -l < build/primes_cpp.txt) entries)" build/primes_cpp.txt build/primes_ts.txt
(cd build && md5sum cpp.txt sort_ts_O.txt sort_ts_N.txt hash_cpp.txt hash_ts.txt cont_cpp.txt cont_ts.txt primes_cpp.txt primes_ts.txt)
exit $fail
