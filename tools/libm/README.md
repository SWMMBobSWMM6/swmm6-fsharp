# tools/libm: glibc's math routines for the TypeScript, Java and Go ports of the SWMM6 engine

The C++ SWMM6 engine calls glibc 2.39's libm. C#, Fortran and Python call the system libm too and inherit its
results; JavaScript, Java and Go bring their own math functions, which differ from glibc's in the last bits for
some arguments, and the SWMM6 gate compares reports and .out files byte for byte. So the TypeScript, Java and Go
ports carry translations of glibc's routines, made here. On x86-64, glibc picks at program start, for most of
these routines, a version compiled with `-mfma -mavx2` when the processor has fused multiply-add and AVX2; the
translations reproduce those versions.

This is the SWMM5 ports' pipeline (`swmm5-ports/tools/libm`), copied and extended. It produces two sets:

| Set | Functions | Variant glibc 2.39 runs on x86-64 with FMA and AVX2 | Output |
|---|---|---|---|
| 1 | exp, log, pow, sin, cos, tan, asin, acos, atan; log10 | FMA variants; log10 generic | `go/glibm/glibm.go`, `java/src/cpp/Glibm.java`, `ts/src/cpp/libm/glibm.ts` |
| 2 | atan2, log2, sinh, tanh, expm1, log1p; cosh, cbrt, hypot | FMA variants (`sysdeps/x86_64/fpu/multiarch`: `e_atan2-fma.c`, `e_log2-fma.c`, `e_sinh-fma.c`, `s_tanh-fma.c`, `s_expm1-fma.c`, `s_log1p-fma.c`); cosh, cbrt and hypot generic (no multiarch variant) | `go/glibm/glibm2.go`, `java/src/cpp/Glibm2.java`, `ts/src/cpp/libm/glibm2.ts` |

Set 1's committed files are the SWMM5 ports' with their package and import lines changed; this pipeline
regenerates the same code (`make_libm.sh --all`; checked identical apart from those lines). Set 2 was made here
(November 2026) for the SWMM6 finite-volume solver, which calls cbrt, hypot and expm1.

What the set-2 routines call, translated with them: sinh's FMA variant calls `__ieee754_exp_fma` and `__expm1_fma`
(`e_sinh-fma.c`), tanh's calls `__expm1_fma`; the generic cosh calls the `__ieee754_exp` and `__expm1` ifuncs,
which select the same FMA variants. The exp is set 1's. cbrt calls `__frexp` and `__ldexp`, hand-ported into the
set-2 runtimes (exact operations). glibc's public symbols for these nine are the routines themselves or SVID
wrappers that change no value (sinh and cosh overflow return HUGE_VAL, which the routines already give; atan2's
sets errno only; log2 and hypot have versioned symbols without wrappers since 2.29 and 2.35). What IEEE leaves
open, x86 decides by operand order: which NaN an operation on two NaNs returns. Go swapped the operands of
hypot's `x + y`, and Java's interpreter and JIT may differ, so the public functions at the end of each glibm2 file
fix the NaN results (a NaN argument comes back quieted; hypot and atan2 give x's when both are NaNs).

| Step | File | What it does |
|---|---|---|
| compile | `build_glibm.sh` | compiles glibc 2.39's `sysdeps/ieee754/dbl-64` sources with glibc's flags for the variants above (`-O2 -frounding-math`, `-mfma -mavx2` for the FMA ones, `-fno-math-errno` for set 2 as glibc builds libm) and dumps GCC's optimized GIMPLE, in which every fused multiply-add GCC chose is explicit (`.FMA`, `.FMS`, `.FNMA`). The objects are only compiled, never run. `stub/` stands in for glibc's internal headers. |
| tables | `extract_tables.py` | copies the constant tables (`__exp_data`, `__log2_data`, `cij`, cbrt's `factor`, ...) out of the object files with objdump/objcopy, checking each symbol's size against glibc's struct layout. |
| translate | `gim2x.py` | GIMPLE to TypeScript, Java or Go. In TypeScript and Java each function is a loop over its basic blocks with PHI nodes as parallel copies; in Go each block is a label and each PHI a parallel assignment on its edge. 64-bit integers are BigInt in TypeScript, long in Java, int64/uint64 in Go; every Go floating-point operation is wrapped in `float64(...)` so that no Go compiler may fuse it. For set 2 it also reads GCC's unordered comparisons (`u<=`, `ord`, ...), locals read through their default definition, frexp's out-parameter, and operations on two constants. |
| runtime | `glibm_rt.*`, `GlibmRt.java`; `glibm2_rt.*`, `Glibm2Rt.java` | exactly rounded fma, bit casts, x86's float-to-int rule, glibc's error helpers, branred.c; for set 2, frexp, scalbn/ldexp and quiet. |
| check | `test/run_tests.sh` | compares the three ports bit for bit with the system libm (below). |

`make_libm.sh GLIBC_SRC [WORKDIR] [--all]` runs compile, tables and translate. GLIBC_SRC is a glibc checkout;
the one used is the 2.39 stable branch (`release/2.39/master`) at commit ce65d944e38a (20 January 2026). The
system libm the tests compare with is Ubuntu's glibc 2.39-0ubuntu8.9.

## Result of `test/run_tests.sh` (4 November 2026)

Set 2, `mkvectors2.c` with N2 = 1,000,000: per function a million random arguments mixing uniform ranges, random
bit patterns over every exponent and both signs, subnormals and the neighbourhoods of every branch threshold in
glibc's source, plus +-0, +-1, +-inf, quiet and signaling NaNs of both signs with several payloads, the extreme
subnormal and normal numbers and, for atan2 and hypot, every pair of those 41 values; frexp and ldexp likewise.
A NaN result must match glibc's in sign and payload.

| Function | Arguments | Go | Java (JIT) | Java (-Xint) | TypeScript |
|---|---|---|---|---|---|
| cbrt | 1,008,555 | 0 | 0 | 0 | 0 |
| hypot | 1,001,681 | 0 | 0 | 0 | 0 |
| atan2 | 1,001,681 | 0 | 0 | 0 | 0 |
| cosh | 1,008,555 | 0 | 0 | 0 | 0 |
| sinh | 1,008,555 | 0 | 0 | 0 | 0 |
| tanh | 1,008,555 | 0 | 0 | 0 | 0 |
| log2 | 1,008,555 | 0 | 0 | 0 | 0 |
| expm1 | 1,008,555 | 0 | 0 | 0 | 0 |
| log1p | 1,008,555 | 0 | 0 | 0 | 0 |
| frexp | 1,000,041 | 0 | 0 | 0 | 0 |
| ldexp | 1,000,369 | 0 | 0 | 0 | 0 |

(0 = no value, NaN-sign or NaN-payload difference.) Set 1, in the SWMM6 layout, gives 0 differences and 0 NaN-sign
differences in all three languages too: 400,000 random arguments per function (`mkvectors.c`, without the census
samples, which are not kept) and 3.3 million edge cases (`mkedge.c`), and TypeScript's pow fast path agrees with
pow_exact on 20 million arguments.

The variant matters: translated from the generic (non-FMA) builds instead, atan2, sinh, tanh, expm1 and log1p
differ from the system libm on 150, 688, 201, 672 and 207 of the same million arguments.

What this does not cover: it reproduces the variants glibc selects on x86-64 processors with both FMA and AVX2
(Intel since Haswell, AMD since Excavator). The ports give those results on any machine, so they match the C++
engine run on such a processor and not on one without (where glibc picks its SSE2, AVX or FMA4 builds). errno is
not modelled. Java's `atan2_t` is 9.5 kB of bytecode, over HotSpot's 8 kB limit for compiling a method, so it runs
interpreted (about 5 us a call; the engine calls atan2 only in its solar geometry). The translations are derived
from glibc and so are covered by glibc's licence, LGPL-2.1-or-later.

Written by Claude (Anthropic); see AI-USE.md.
