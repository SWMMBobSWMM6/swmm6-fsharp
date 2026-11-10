Read the earlier project's lessons before writing a new translator: the SWMM5 lesson on lambda shadowing would have saved a gate run here.

The third C# gate run failed two models of 480, both reading the same NCDC rain file. Every rain value came back
missing. The cause was one line of generated code:

```csharp
n = R.sscanf(line + k, R.Lit("%2d%2d%6ld%c%c"), R.RefI(v => hour_2 = v), R.RefI(v => minute_2 = v),
             R.RefL(v => v = v), ...);
```

The C++ function has a local `long v = 99999;` and passes `&v` to sscanf. The translator writes each scanf target
as a small lambda that stores the scanned value, and it named the lambda's parameter `v`. C# lets a lambda
parameter shadow a local, so `v => v = v` stored the value into the parameter, and the local stayed 99999,
which the engine reads as "missing".

This is exactly lesson 01 of the SWMM5 ports (swmm5-ports/lessons/01-lambda-shadowing.md): same function
(sscanf targets), same name, same symptom, same rain files. That lesson's rule, "any name the translator invents
must live in a namespace C identifiers cannot reach (leading double underscore)", was not carried into the new
C++ back end. The parameter is now `__sv`.

The rule: a new translator for the same engine family starts from the old project's lessons as a checklist,
item by item, before the first gate run.
