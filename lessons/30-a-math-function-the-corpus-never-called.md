The FV solver calls cbrt and hypot, which no model of the 480 corpus reached through code whose output shows them, and the TypeScript runtime computed both with JavaScript's Math, which rounds differently from glibc.

Once lessons 17, 21, 26, 27, 28 and 29 let the FV decks of the 452 extra decks run, seven of them still failed in
TypeScript (sdm_fv_o1, sdm_fv_o2, storage_bucket_fv, vj_fv_invert_collision, surcharged_tee, transmission_main,
valve_closure), most by the sign of a zero in the Node Inflow Summary's flow balance error, whose .out was identical:

```
  J11                  JUNCTION      9.05    71.81     0  12:11       0.145        1.51      -0.000     (C++)
  J11                  JUNCTION      9.05    71.81     0  12:11       0.145        1.51       0.000     (TypeScript)
```

The volumes behind that column are sums over the run, so they keep the last bits a 4-byte .out drops. The C++ calls
glibc for `std::cbrt` (the friction term `r * std::cbrt(r)` in src/engine/hydraulics/fv/FvKernels.hpp and
PressurizedHeadSolver.cpp) and `std::hypot` (the overflow-resistant norms of NativeGmres.cpp and
NonlinearPressureStorage.cpp); the C# runtime's Math.Cbrt (.NET calls the C library's cbrt) and its LibM.hypot (a
DllImport of libm, cs/runtime/Misc.cs) call glibc too. ts/src/cpp had

```typescript
	static Cbrt_d(x: number): number { return libmNaN1(x, Math.cbrt(x)); }          // SysMath.ts
...
		return Math.hypot(x, y);                                                       // Misc.ts, LibM.hypot
```

and recording every call on sdm_fv_o1 (2.37 million distinct cbrt arguments, the first 3 million distinct hypot
arguments of 24.6 million calls) and comparing each result with the system's glibc found 1,399,167 cbrt and 1,057,883
hypot results that differed, by 1 to 3 ulps. SysMath.ts's header said so ("for now; they do not always round as
glibc's do") and tests/crtcheck/tscheck measured it (cbrt: 153,242 of 300,576 cases), but the 480 corpus gave no
reason to fix it.

The fix came in two steps. First, hand translations of glibc 2.39's sysdeps/ieee754/dbl-64/s_cbrt.c and e_hypot.c:
neither has an FMA variant in sysdeps/x86_64/fpu/multiarch, so x86-64 glibc runs the generic objects, compiled
without -mfma, and every C double operation is one JavaScript double operation in the C order (hypot's kernel is its
branch without __FP_FAST_FMA). They gave 0 differences on the recorded arguments and on crtcheck's 300,576 cases each,
and the seven decks passed. log2 (V8's Math.log2: 12,140 of 1,215,431 recorded arguments differed by 1 ulp) did not
matter there: the engine uses it only as floor(log2(dt / dt_min)) and floor(log2(remaining / dt0)), and no recorded
argument's floor differed. Then all nine went to ts/src/cpp/libm/glibm2.ts and glibm2_rt.ts, made by tools/libm's
GIMPLE translator as glibm.ts is (atan2, log2, sinh, tanh, expm1 and log1p from the FMA variants, cosh, cbrt and hypot
generic; 0 differences on about a million arguments each, NaNs included, where the hand hypot had returned Infinity
for an infinity paired with a signalling NaN and glibc returns the NaN). SysMath.Atan2_d_d, Cbrt_d, Cosh_d, Sinh_d,
Tanh_d, Log2_d and LibM.hypot, expm1, log1p now call glibm2, and no math function of the TypeScript runtime uses
JavaScript's Math for a rounded result any more (Abs, Floor, Ceiling, Truncate and Sqrt are exact).

The Go runtime has the same gap (go/cpp/libm.go: atan2, cbrt, cosh, sinh, tanh, log2, expm1, log1p and hypot "use Go's
math package for their values"), and go_x2 fails the same FV decks; the Java runtime uses StrictMath (fdlibm) for
them (java/src/cpp/SysMath.java, LibM.java).

The rule: a measured difference from the reference library is a failure waiting for an input, like a TODO (lesson 17);
when a new set of inputs reaches new code, list the library functions that code calls and check each against the
measurements before reading the diffs.
