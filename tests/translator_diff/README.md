# Differential tests of the TypeScript and Python translators

Each translator was checked, before its runtime existed, by translating small C# programs written to exercise
every rule (integer widths and signedness, conversions, shifts, evaluation order, closures in loops, ref/out,
goto, exceptions, using, hiding vs overriding, static-initialiser order, FNV and splitmix64) and comparing
their output under .NET with the translated program's output under Node or CPython, with a test-only runtime
written to the translator's contract (ts/RUNTIME_HELPERS.md, py/RUNTIME_HELPERS.md).

- `ts/`: numtest.cs and flowtest.cs with their drivers; 240,069 of 240,079 numeric results and all 130
  control-flow results equal (diff.txt: the 10 others are the NaN sign of a float `x % 0`, which the engine never
  computes). `index.ts` is the test-only `cpp.L`.
- `py/`: Test.cs and Test2.cs with Driver.cs and pydrv.py; cs_out.txt and py_out.txt are the two outputs (24 test
  groups, all equal). `cpp.py` is the test-only runtime.

These are kept as evidence; the scripts expect the scratch layout the agents used and are not wired into a build.
Written by Claude (Anthropic) for the SWMM6 ports; see AI-USE.md.
