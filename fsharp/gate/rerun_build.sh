#!/bin/bash
# rerun_build.sh [COMMIT] [DIR] - the first half of the independent rerun: a clean copy of COMMIT (default HEAD) of
# this repository (git archive) in DIR (default /dev/shm/fs6_v), where the translator is rebuilt, the engine is
# regenerated from cs/gen and compared byte for byte with the committed fsharp/gen/Engine.fs (and the translator's
# three reports beside it), the generated part of the runtime (runtime/PrimeList.fs, from cs/runtime/PrimeList.cs) is
# written again and compared, and the runtime, engine and command line are compiled into DIR/fsharp/bin. The rest
# of the runtime is translated by hand and is used as committed. The gate then runs that copy:
#   SWMM_FS_BIN=DIR/fsharp/bin fsharp/gate/run_chunk.sh fs6_v_c1 corpus fsharp/gate/ids_c1.txt DIR/fsharp/runswmm6_saves.sh
# Written by Claude (Anthropic) for Robert Dickinson's SWMM6 ports; see ../README.md.
set -e
K=$(cd "$(dirname "$0")/.." && pwd); ROOT=$(cd "$K/.." && pwd)
C=${1:-HEAD}; R=${2:-/dev/shm/fs6_v}
rm -rf "$R"; mkdir -p "$R"
git -C "$ROOT" archive "$C" | tar -x -C "$R"
echo "== clean copy of $(git -C "$ROOT" rev-parse --short "$C") in $R"
cd "$R"
echo "== the translator: tools/cs2x/build_fs.sh"
nice -n 10 tools/cs2x/build_fs.sh 2>&1 | grep -v 'warning CS' || true
echo "== the engine, regenerated from cs/gen into $R/regen/gen and compared with fsharp/gen/Engine.fs"
mkdir -p "$R/regen/gen"
nice -n 10 dotnet tools/cs2x/bin/cs2fs.dll cs "$R/regen/gen"
if cmp "$R/regen/gen/Engine.fs" fsharp/gen/Engine.fs; then echo "engine: Engine.fs, $(wc -l < fsharp/gen/Engine.fs) lines, byte-identical to the commit"; else echo "engine: DIFFERENT"; exit 1; fi
for f in cs2fs_todo.txt cs2fs_runtime.txt cs2fs_stats.txt; do
	if cmp "$R/regen/$f" "fsharp/$f"; then echo "$f: byte-identical to the commit"; else echo "$f: DIFFERENT"; exit 1; fi
done
echo "== the generated part of the runtime, written again from cs/runtime/PrimeList.cs"
fsharp/make_primelist.sh "$R/regen/PrimeList.fs"
if cmp "$R/regen/PrimeList.fs" fsharp/runtime/PrimeList.fs; then echo "runtime/PrimeList.fs: byte-identical to the commit"; else echo "PrimeList.fs: DIFFERENT"; exit 1; fi
rm -rf "$R/regen" tools/cs2x/bin
echo "== build"
if nice -n 10 fsharp/build.sh > "$R/build.log" 2>&1; then echo "built; compiler warnings: $(grep -c 'warning FS' "$R/build.log")"
else grep -m 20 'error' "$R/build.log"; exit 1; fi
rm -rf fsharp/obj "$R/build.log"
ls -l fsharp/bin/runswmm6.dll
sha256sum fsharp/bin/runswmm6.dll | sed 's/ .*/  runswmm6.dll (fsc --deterministic+, paths mapped to \/_\/fsharp)/'
