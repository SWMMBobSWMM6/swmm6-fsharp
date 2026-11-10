#!/bin/bash
# Part A of the TypeScript runtime (package cpp, ts/src/cpp/a_*.ts) against the C# runtime it is ported from:
#   lcheck    - every cpp.L integer helper and cpp.Conv conversion over edge values (lcheck.ts, lcheck.cs);
#   corecheck - Vec, Iter, RevIter, Ptr, CSpan, Str, CPtr, Pair, Opt, M, Arr, Mv, Dtor, Defer, Box and the exceptions
#               (corecheck.ts, corecheck.cs; the Java port's check plus TypeScript-specific cases).
#   manifest  - every ts/cs2ts_runtime.txt entry of part A names a member of the compiled runtime (manifest.mjs).
# The outputs must be identical. Part A is compiled with stub_index.ts in place of index.ts (stubs for the few members
# of parts B and C it calls); REAL=1 compiles the whole of ts/src/cpp with its own index.ts instead.
# Written by Claude (Anthropic) for the SWMM6 ports; see AI-USE.md.
set -e
cd "$(dirname "$0")"
B=build
TSC=${TSC:-/home/claude/.npm-global/bin/tsc}
rm -rf $B/ts $B/js; mkdir -p $B/cs $B/ts/cpp $B/js
# C#: the runtime without the engine's hand-written partial classes and entry point
FW=$(ls -d /usr/lib/dotnet/shared/Microsoft.NETCore.App/8.* | head -1)
CSC=$(ls /usr/lib/dotnet/sdk/*/Roslyn/bincore/csc.dll | head -1)
REFS=$(for f in $FW/*.dll; do case $(basename $f) in Microsoft.VisualBasic*|mscorlib.dll|netstandard.dll|System.Private.*) ;; *) echo -n "-r:$f ";; esac; done)
RT=$(ls ../../cs/runtime/*.cs | grep -v -e Program.cs -e /Hand_)
for t in lcheck corecheck; do
  if [ ! -f $B/cs/$t.dll ] || [ -n "$(find $t.cs ../../cs/runtime -newer $B/cs/$t.dll -name '*.cs' | head -1)" ]; then
    dotnet $CSC -nologo -optimize+ -nullable:enable -nowarn:CS8765,CS1718,CS8600,CS8601,CS8602,CS8603,CS8604,CS8618,CS8625,CS0162,CS0168,CS0219,CS0414,CS0649,CS8632 \
      -langversion:latest -out:$B/cs/$t.dll $REFS -r:$FW/netstandard.dll -r:$FW/System.Private.CoreLib.dll $t.cs $RT
    printf '{"runtimeOptions":{"tfm":"net8.0","framework":{"name":"Microsoft.NETCore.App","version":"%s"},"configProperties":{"System.Globalization.Invariant":true}}}' $(basename $FW) > $B/cs/$t.runtimeconfig.json
  fi
done
# TypeScript: part A with the stub index (or every part with the real one), then the checks
if [ -n "$REAL" ]; then cp -r ../../ts/src/cpp/. $B/ts/cpp/; else cp ../../ts/src/cpp/a_*.ts $B/ts/cpp/; cp stub_index.ts $B/ts/cpp/index.ts; fi
cp lcheck.ts corecheck.ts $B/ts/
echo '{ "type": "module" }' > $B/package.json
cat > $B/tsconfig.json <<'JSON'
{ "compilerOptions": { "target": "ES2022", "module": "nodenext", "moduleResolution": "nodenext", "strict": false, "skipLibCheck": true,
  "useDefineForClassFields": true, "types": ["node"], "typeRoots": ["/opt/node-tools/node_modules/@types"],
  "outDir": "js", "rootDir": "ts" }, "include": ["ts/**/*.ts"] }
JSON
$TSC -p $B/tsconfig.json
rc=0
for t in lcheck corecheck; do
  dotnet $B/cs/$t.dll > $B/$t.cs.txt
  node $B/js/$t.js > $B/$t.ts.txt
  if cmp -s $B/$t.cs.txt $B/$t.ts.txt; then echo "$t: $(wc -l < $B/$t.cs.txt) lines, TypeScript and C# outputs identical"
  else echo "$t: outputs differ"; diff $B/$t.cs.txt $B/$t.ts.txt | head -30; rc=1; fi
done
# every manifest entry of part A names a member the compiled runtime has
node manifest.mjs $B/js/cpp/index.js || rc=1
exit $rc
