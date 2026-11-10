# The OpenSWMM 6 1D engine in F#, byte for byte

A translation of the 1D engine of OpenSWMM 6 (HydroCouple/openswmm.engine, branch `swmm6_rel`, commit 88329d6) into
F#, held to the same output as the C++ engine: the same binary `.out` file and the same `.rpt` report, apart from
the three clock lines. It is not SWMM6 and not a HydroCouple or EPA release; it is a translation checked against
SWMM6.

## Result

**480 of 480 models byte-identical** against the C++ reference engine built from the same commit and run on one
thread (`OMP_NUM_THREADS=1`), on the 480-model gate set the SWMM5 ports were checked on, drawn from
1729-SWMM5-Models-2030. Gate runs `fs6_c1` (25 of 25), `fs6_c2` (99 of 99), `fs6_c3` (68 of 68), `fs6_c4` (69 of 69)
and `fs6_c5` (219 of 219), the corpus cut into chunks by the C# port's times (`gate/ids_c1.txt` ... `ids_c5.txt`);
23.1 minutes of running in all, a median of 0.95 s per model, the slowest `72_H&H_Elements(2).inp` (212 s).

**452 of 452 extra decks byte-identical**: SWMM6's own tests (the input files in the upstream repository's `tests`
folder at 88329d6) and the synthetic decks of swmm6-rust's gate (the finite-volume solver, its structure coupling,
the newer hydrology and the input checks). Gate runs `fs6_x1` (218 of 218), `fs6_x4` (232 of 232), and the two
slowest decks alone, `fs6_x2` (`legacy_small.inp`, 407 s) and `fs6_x3` (`refactored_small.inp`, 382 s); 20.1 minutes
in all, a median of 0.70 s per deck.

**The independent rerun: 480 of 480.** A clean copy of commit 3a71d3b (`git archive`) in memory, where the translator
was rebuilt, the engine regenerated from `cs/gen` (`Engine.fs` and the translator's three reports byte-identical to
the committed ones), the one generated runtime file written again (`runtime/PrimeList.fs`, byte-identical) and
everything compiled again (`gate/fs6_v_build.txt`), then gated: runs `fs6_v_a` (192 of 192) and `fs6_v_b` (288 of
288); 22.8 minutes, a median of 0.94 s per model. Every `.out` and `.rpt` hash equals the first run's. The rest of
the runtime is written by hand and was compiled as committed. The engine of the first runs was built from the
sources of commit 6ca72c1 (`gate/fs6_snapshot.txt`), which 3a71d3b leaves unchanged.

**The main session's check: 48 of 48 and 12 of 12.** A clean `git clone` of commit b970d3c (in memory), where the
translator was rebuilt, the engine and `PrimeList.fs` regenerated (no file changed: `git status` empty) and the engine
built with `build.sh` (3 minutes), then gated: run `fs6_main` on 45 corpus models drawn at random
(`random.Random(20261110)`) plus `Hydrology/save_rtk_long.inp`, `one_sub_one_rdii_node.inp` and
`Use_SeasonalRDIIn.inp`, the decks that SAVE and USE `cho.txt` (48 of 48; 85 s), and run `fs6_main_x` on 12 extra
decks drawn the same way (12 of 12; 8 s); the same samples as the Kotlin, Groovy and JavaScript ports' checks. Ids in
`gate/ids_main_check.txt` and `gate/ids_main_check_extra.txt`.

The F# engine took about 1.4 times the C# engine's time on the same models (1,381 s against 1,002 s for the 480;
1,206 s against 833 s for the 452), measured while other gate runs shared the machine.

Every result, with the SHA-256 of each `.out` and `.rpt`, is in `results/NAME.json`, with the run's log beside it
(`results/NAME_run.txt`); the start and end of each run are in `gate/NAME_times.txt`.

A model passes when its `.out` is byte-identical to the reference's (when the reference writes one), its `.rpt` is
identical after removing the lines "Analysis begun", "Analysis ended" and "Total elapsed time", and its exit code is
the same. The gate runs every model through `runswmm6_saves.sh`, which backs up the files a deck SAVEs and restores
them afterwards, so that no deck changes a file another deck reads.

## How it was made

The C++ engine was translated to C# first (`cs/gen`, 480 of 480), and this engine is translated from that C#, not
from the C++, by a translator written for it: `tools/cs2x/Cs2Fs.cs`, `FsTypes.cs` and `FsFn.cs`, which read the C#
(`cs/gen`, 382 files, and the engine's hand-written partial classes `cs/runtime/Hand_*.cs`) through Roslyn's
semantic model and print one F# file, `gen/Engine.fs` (1,257 types, 148,048 lines). F# has no `return`, `break`,
`continue` or `goto`, so the translator restructures every method: an early exit becomes an `else` branch where it
can (4,756 times), a jump in tail position is left out, and the rest set flags that guard the statements they skip;
the four methods with gotos become dispatch loops. C#'s static fields, which F# cannot hold mutable in a recursive
namespace, become fields of one holder object per class, set by `Init__.run` in dependency order before `main`
starts. The mapping is in `tools/cs2x/FS.md`; how often each kind of jump was translated each way is in
`cs2fs_stats.txt`, and the one construct left as a note (an empty enum) in `cs2fs_todo.txt`.

The runtime (`runtime/`, namespace `Cpp`, 4,791 lines) is the part of the C++ standard library and of the C library
that the engine calls (vectors, maps and sets with libstdc++'s hash-table order, strings, iterators, pointers,
printf and sscanf, streams, the file system, introsort). It is the C# port's runtime (`cs/runtime`) translated into
F# by hand, with the same classes, members, fields and `ref` parameters, so that the translator writes a runtime call
as the C# engine makes it; only `runtime/PrimeList.fs` (libstdc++'s bucket counts) is written by a script
(`make_primelist.sh`). `runtime/Program.fs` is the command line (`src/cli/main.cpp`, by way of the C# port's
`Program.cs`). Nothing in the engine or the runtime refers to the C# port's assembly or projects: `build.sh` compiles
the F# sources alone.

The math is glibc's own. `runtime/LibM.fs` declares the C library's math functions with `DllImport("libm.so.6")`,
and the translator sends `Math.Exp`, `Pow`, `Sin` and the rest to them, so every rounded result comes from the
system's glibc, through the same symbol versions the C++ engine imports (`exp`, `log`, `pow` and `log2` at
GLIBC_2.29, `hypot` at GLIBC_2.35, `fmod` at GLIBC_2.38, the rest at GLIBC_2.2.5; `test/check_binding.sh`,
`results/check_binding.txt`). `sqrt`, `floor`, `ceil`, `trunc`, `fabs` and `copysign`, whose results IEEE fixes,
come from .NET's `Math`.

Nothing in the generated engine is edited by hand: every fix went into the translator or the runtime.

## How it was checked

- **The math**: `test/run_libm_tests.sh` compares the functions as the F# engine calls them with the system's glibc
  2.39, bit for bit, by the method of `tools/libm/test`: a million arguments per function over the ranges SWMM uses
  with special values, and 200,000 to 700,000 edge cases each, for exp, log, log10, pow, sin, cos, tan, asin, acos and
  atan; a million per function over wide ranges and every branch threshold, with signed zeros, infinities,
  subnormals and NaNs of both signs with several payloads, for cbrt, hypot, atan2, cosh, sinh, tanh, log2, expm1 and
  log1p; a million each for the six exact functions. 28,364,587 results, **0 differences**, NaN signs and payloads
  included (`results/libm_tests.txt`). Calling glibc itself, this checks the binding rather than an algorithm.
- **The runtime**: `test/run_runtime_checks.sh` compiles the runtime alone into a library and builds the check
  programs of `tests/` against it, unchanged (`tests/stdcheck/sortcheck.cs`, `hashcheck.cs`, `streamcheck.cs`,
  `tests/javacheck/corecheck.cs`), and `test/CrtCheck.cs`, a C# copy of `tests/crtcheck/javacheck/CrtCheck.java`;
  each is also built from the C# runtime's sources as the control (`results/runtime_checks.txt`):
  - printf (179,756 cases), sscanf (1,728) and from_chars / strtol / strtod / atoi / ctime / ctype (752): the F#
    runtime's results are identical to the C# runtime's, case by case. Both differ from glibc on the same cases (4,350
    printf cases: `%u`, `%x` and `%o` of a negative int, `%hd`, the `#` flag, `%.0a`; 27 sscanf and 2 misc cases: the
    sign of a NaN read from "nan"); no engine format meets them, as the gates show;
  - libm through the runtime: 0 differences in 300,576 cases for each of 27 functions;
  - introsort, stable_sort and partial_sort permutations (6,447 cases) and the hash tables' node order and std::hash
    (167 lines): identical to libstdc++;
  - streams and the file system: identical to the C# runtime, and to libstdc++ apart from one printf case C# formats
    differently from glibc (`%#.2g` of 99.995);
  - the core containers, iterators and pointers (128 lines): identical to the C# runtime.
- **The engine**: the gate (above), run on the corpus and on the extra decks, and the independent rerun.

## Build and run

Needs the .NET 8 SDK (8.0.132 was used, with the runtime 8.0.32): the F# compiler (`fsc`, F# 8.0, 12.8.102) and
FSharp.Core 8.0.102 ship inside it, and `build.sh` uses them directly, without NuGet. Build (3 to 5 minutes and
about 1.1 GB of memory on two cores): `fsharp/build.sh`, which writes `fsharp/bin/runswmm6.dll`. Then run a model the way
the C++ command line runs it:

    fsharp/runswmm6 model.inp model.rpt model.out

To regenerate the engine from `cs/gen`: `tools/cs2x/build_fs.sh && dotnet tools/cs2x/bin/cs2fs.dll cs fsharp/gen`
(about 40 seconds); `fsharp/make_primelist.sh` writes `runtime/PrimeList.fs` again.

The gate: `fsharp/gate/run_chunk.sh NAME corpus|extra [IDS_FILE] [ENGINE]` runs `gate/gate.py` on this port with the
SAVE-file wrapper, inside the shared lock; `fsharp/gate/rerun_build.sh` makes the clean copy of a commit that the
independent rerun gates.

## What this does not show

- Agreement on models outside the 480 and the 452. A feature no gate model or deck uses is untested.
- The reference's parallel runs: every parallel loop runs on one thread, in order, as the C++ engine does when it
  is held to one thread.
- The 2D and other modules of OpenSWMM 6 beyond the 1D engine the command line runs.
- The runtime where it differs from glibc: it copies the C# runtime, including the printf and sscanf cases above,
  which the engine does not reach on these models; another model that printed a negative number with `%u` would
  show it.
- Every input to the math functions: they are glibc's own, called through P/Invoke, and the tests check that each
  call reaches the right symbol with the right arguments; another glibc would give that glibc's results.
- Speed. The times above were measured on a shared two-core machine while other gate runs were going.

## License and attribution

The engine is a translation of the OpenSWMM Engine, Copyright 2026 HydroCouple Developers, licensed under the
Apache License, Version 2.0 (https://github.com/HydroCouple/openswmm.engine, branch `swmm6_rel`, commit 88329d6).
Portions derive from the USEPA Storm Water Management Model, which is in the public domain; the USEPA does not
endorse this translation, and neither does HydroCouple. The math is the system's glibc, called at run time and not
copied; glibc and glibc-derived math are under the GNU Lesser General Public License 2.1 or later.

## AI use

The translator (`tools/cs2x/Cs2Fs.cs`, `FsTypes.cs`, `FsFn.cs`), the runtime (the C# port's runtime translated into
F# by hand), the port's scripts and checks and these notes were written by Claude, Anthropic's AI model, for Robert
Dickinson, who directed the work. The engine is a mechanical translation of OpenSWMM 6 (Apache-2.0, HydroCouple
Developers), itself derived in part from the public-domain EPA SWMM; it was checked by running it against the C++
engine, not by reading it. glibc-derived math is LGPL-2.1-or-later. See `../AI-USE.md`.
