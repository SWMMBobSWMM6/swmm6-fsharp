The C# runtime came first and the other five were written from it, each by an agent that improved on it where glibc demanded; those improvements never came back to the C#, and the C# failed decks the other ports passed.

Nine of the 38 C# failures on the extra decks were of this kind. cs/runtime/CRuntime.cs `sscanf` ended its directive
switch with

```csharp
                    default:
                        throw new NotSupportedException("sscanf %" + (char)conv);
```

while java/src/cpp/R.java, whose comment says so ("The C# runtime handles fewer directives (no %n, scan sets or %a)
and differs from glibc on the EOF rule and on %u of negative or huge numbers; glibc is followed"), and the Go,
TypeScript, Python and Fortran runtimes all handled `%n` and `%[`. The engine uses both: HourlyMeteorology.cpp checks
that a date token was read whole with `"%d/%d/%d%n"`, and the external time-series file reader in
PostParseResolver.cpp (`parse_file_date`) falls back to a month name with `"%3[A-Za-z]%c%u%c%u"`.
Seven decks died in C# on the NotSupportedException. The same happened with std::from_chars (lesson 22): Java and Go
followed libstdc++, C# did not, and ppr_refs.inp failed in C# alone.

It ran the other way once too. When an exception escapes the engine, the C++ terminates with exit status 134
(SIGABRT); .NET's unhandled-exception handler also aborts, java/src/main/Main.java halts with 134 on purpose, and the
Fortran runner calls quick_exit(134); the Go runner let the panic end the process with Go's status 2, and
initq_treat_dot.inp (`stod(".")` inside a noexcept function) failed in Go alone.

The fixes: the C# sscanf is now a port of the Java one (glibc's EOF rule, %u wrapping, glibc's float scanner, strtof
rounding for float targets, `%n` and `%[`), the C# from_chars follows Std.java, and go/cmd/openswmm/main.go
`runGuarded` recovers an escaped panic, prints it and exits with 134 without flushing the C files.

The rule: when a port's runtime departs from the one it was written from because the reference demanded it, the
departure is a bug report against the original. Note it where the original's maintainer will see it, or fix both.
