#!/bin/bash
# make_primelist.sh [OUT] - write runtime/PrimeList.fs (libstdc++'s __prime_list, the bucket counts of
# std::unordered_map) from the C# runtime's table, cs/runtime/PrimeList.cs, which tests/stdcheck/primes.cpp generated
# from the system libstdc++. The only part of the F# runtime that is generated; the rest is translated by hand.
# Written by Claude (Anthropic) for Robert Dickinson's SWMM6 ports; see README.md.
set -e
K=$(cd "$(dirname "$0")" && pwd)
python3 - "$K/../cs/runtime/PrimeList.cs" "${1:-$K/runtime/PrimeList.fs}" <<'PY'
import re, sys
rows = [l.strip().rstrip(',') for l in open(sys.argv[1]) if re.match(r'^\d+UL', l)]
out = ["// PrimeList.fs - libstdc++'s __prime_list (the bucket counts of std::unordered_map), the table cs/runtime/PrimeList.cs",
       "// holds (generated there by tests/stdcheck/primes.cpp from the system libstdc++), written out as F# by a script.",
       "// Written by Claude (Anthropic) for Robert Dickinson's SWMM6 ports; see AI-USE.md and fsharp/README.md.",
       "namespace Cpp", "", "",
       "type PrimeList private () =",
       "    static let p : uint64[] = [|"]
out += ["        " + r.replace(", ", "; ") for r in rows]
out += ["    |]", "    static member P = p"]
open(sys.argv[2], "w").write("\n".join(out) + "\n")
PY
