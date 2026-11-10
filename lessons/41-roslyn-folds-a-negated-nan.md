C# folds `-double.NaN` at compile time into the same NaN (sign bit set, 0xFFF8...), while F#'s unary minus on `Double.NaN` runs and flips the sign; a hand translation of the C# runtime has to copy what C#'s compiler produced, not what its source says.

The C# runtime's strtod (cs/runtime/CRuntime.cs) reads "nan" with an optional sign:

```csharp
                v = neg ? -double.NaN : double.NaN;
```

The F# runtime (fsharp/runtime/Runtime.fs) first said the same thing in F#:

```fsharp
            v <- (if neg then -Double.NaN else Double.NaN)
```

and fsharp/test/CrtCheck.cs (tests/crtcheck's Java checker in C#, built once against each runtime) found the two
runtimes apart on two of the 752 misc cases, `strtod("-nan")` and `from_chars("-nan")`:

```
C# runtime:  fff8000000000000 4
F# runtime:  7ff8000000000000 4  (strtod "-nan")
```

.NET's `double.NaN` is 0xFFF8000000000000 (negative). Roslyn evaluates `-double.NaN` as a constant and emits
0xFFF8000000000000 again (checked with a two-line program: `-double.NaN` gives fff8..., while `-n` for a local
`n = double.NaN` gives 7ff8...); F# emits `ldsfld`/`neg` and x86's negation flips the sign bit. So the C# runtime gives
the same NaN for "nan" and "-nan" (glibc gives 7ff8... and fff8...; the C# runtime is right for "-nan" and wrong for
"nan", by accident). The fix copies the C# runtime's behaviour, with a comment, because the F# runtime is held to the C#
one case by case:

```fsharp
            v <- (if neg then Double.NaN else Double.NaN)
```

after which the printf (179,756), scanf (1,728) and misc (752) results of the two runtimes are identical
(fsharp/test/run_runtime_checks.sh). No gate model reads a NaN from its input, so the gate could not have found it.

The rule: when the source of a hand translation is a program in a language whose compiler folds constants, the
behaviour to copy is the compiled one; check corners like NaN signs, `-0.0` and integer overflow in constants by
running both builds on the same cases, not by reading the source.
