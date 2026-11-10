Which build of a glibc routine runs is a fact in sysdeps/x86_64/fpu/multiarch, not a guess; and which NaN a sum of two NaNs returns is decided by operand order, which a compiler may change.

Translating cbrt, hypot, atan2, cosh, sinh, tanh, log2, expm1 and log1p for the Go, Java and TypeScript runtimes
(tools/libm, set 2), the working assumption was that atan2 and log2 might have FMA variants and the other seven
were probably generic. glibc 2.39's multiarch directory says otherwise: it has `e_sinh-fma.c`, `s_tanh-fma.c`,
`s_expm1-fma.c` and `s_log1p-fma.c` beside `e_atan2-fma.c` and `e_log2-fma.c` (all `-mfma -mavx2`, chosen by
ifunc-fma.h when the processor has FMA and AVX2). Only cosh, cbrt and hypot are generic, and cosh still calls the
FMA exp and expm1 through the `__ieee754_exp` and `__expm1` ifuncs. With -mfma, GCC fuses operations the source
writes separately; in sinh's GIMPLE, `h * (2.0 * t - t * t / (t + one))` becomes

```
  _14 = .FMS (t_48, 2.0e+0, _13);
```

Translated from the generic builds instead, atan2, sinh, tanh, expm1 and log1p differ from the system libm on 150,
688, 201, 672 and 207 of a million arguments; from the FMA builds, on none.

The second trap was in a routine that was right. hypot's GIMPLE returns `x + y` when an argument is not finite:

```
  _45 = x_33(D) + y_34(D);
```

translated to Go as

```go
	l__45 = float64(p_x + p_y)
```

On x86, a sum of two NaNs is the first operand's NaN (quieted); IEEE leaves it open, so a compiler may swap the
operands of a floating-point add, and Go's did: Go's Hypot(NaN1, NaN2) returned NaN2 where glibc returns NaN1 (57 of
the 1,681 special-value pairs, all with both arguments NaN). Every finite result matched. Java's interpreter and
JIT may also differ there (SysMath.Pow_d already guards pow's two-NaN case for that reason). The fix: the public
functions at the end of glibm2.go/.java/.ts decide the NaN cases themselves (a NaN argument comes back quieted;
hypot and atan2 give x's when both are NaNs) and call the translation otherwise.

The rules: before translating a libm routine, list `sysdeps/x86_64/fpu/multiarch/*-fma.c` and the ifuncs its callers
reach; and wherever two NaNs can meet in one operation, pin the result outside the translated arithmetic, and test
NaN payloads, not only NaN signs.
