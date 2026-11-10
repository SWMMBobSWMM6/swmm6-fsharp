libstdc++'s std::stod throws out_of_range whenever strtod sets errno to ERANGE, and glibc sets it not only for an overflow but for an underflow: a nonzero number read as zero, and a result that is tiny and inexact.

The ports' runtimes did half of std::stod. cs/runtime/Str.cs had

```csharp
        /// <summary>std::stod: strtod, throwing invalid_argument when nothing converts (out_of_range on ERANGE is
        /// not reproduced: strtod's ERANGE cases do not occur in the engine's inputs).</summary>
        public static double stod(Str s, Ptr<ulong> idx = default)
        {
            var p = s.c_str(); var end = p;
            double v = R.strtod(p, ref end);
            if (end.O == p.O) throw new InvalidArgument("stod");
```

and the other five runtimes copied the code (Java, Fortran, TypeScript and Python the comment too). The engine's inputs do
contain such numbers once someone writes a deck to test them. The [TREATMENT] expression scanner in
src/engine/quality/Treatment.cpp reports a number stod rejects:

```cpp
            try {
                t.value = std::stod(rhs.substr(start, i - start));
            } catch (...) {
                return fail("malformed number '" + rhs.substr(start, i - start) + "'",
```

and lid_layer_treatment_rows.inp has rows with `C = 1e999`, `C = 1e-400`, `C = 2.2250738585072011e-308` and
`C = 2.22507385850720138e-308`. The C++ calls the first three malformed and accepts the fourth, which is the
x86 rule exactly: the third is below DBL_MIN and not exact (ERANGE), the fourth is also below DBL_MIN as written but
rounds to DBL_MIN at 53 bits, so it is not "tiny after rounding". In initq_treat_huge.inp the same `1e999` reaches the
other stod call in Treatment.cpp (`tokenize`) inside the noexcept initQuality, and the C++ ends with "terminate
called after throwing an instance of 'std::out_of_range'", exit status 134 and no report; the ports read +inf,
went on, and stopped later on ERROR 161 with exit status 5.

The fix, the same in each runtime: strtod records glibc's ERANGE (overflow to +-inf; nonzero digits read as zero;
a magnitude at most DBL_MIN that is below 2^-1022 - 2^-1076 and differs from the exact decimal value, both decided
with exact integer arithmetic) and stod throws out_of_range("stod") on it:
cs/runtime/CRuntime.cs `R.ERange`, `ScanDouble`, `DecRange` and Str.cs `stod`; java/src/cpp/R.java `ERange`,
`scanDouble`, `decRange` and Str.java `stod`; go/cpp/format.go `ERange`, `scanDouble`, `decRange` and str.go
`Str_stod`; f90/runtime/cpp_rt_c.f90 `crt_erange`, cpp_rt_c_fmt.f90 `crt_strtod`, `dec_erange` (with the converter's
own exact comparator `cmp_dec_bin`) and cpp_rt_a_crt.f90 `cpp_str_stod`; py/cpp/crt.py `ERange`, `R_ScanDouble`,
`_dec_range` (with `_big`, because Python's int() refuses more than 4,300 digits) and core_str.py `Str_stod`;
ts/src/cpp/CRuntime.ts `scanRange` (read as `R.ERange`), `scanDouble`, `decRange` (in BigInt) and a_str.ts `Str.stod`.

The C# std::from_chars had the same gap and one more (it read "0x10" as hexadecimal); the Java and Go runtimes
already followed libstdc++ there, so ppr_refs.inp (`1e999` in an [INITIAL_QUALITY] side file) failed in C# alone.
cs/runtime/Misc.cs `Std.from_chars(..., ref double)` now follows java/src/cpp/Std.java.

The rule: a comment saying a library behaviour "does not occur in the inputs" is a claim about the corpus, not about
the engine; a test deck written against the engine will find it, so state the behaviour in full instead.
