A compiler bug in the target language is one more rule the translator must obey; find the smallest program that triggers it, then write output that never contains it.

The first link of the Go engine crashed the Go 1.24.7 linker itself:

```
panic: inlined function openswmm/engine.(*kinwave_KWSolver).execute.func1 missing func info
```

The generated code was correct Go. The C++ source uses an immediately invoked lambda to fill a static fallback
vector, which the C# port writes as `((FN101)(() => { ... }))()` and the Go back end first wrote as

```go
order = FN101(func() *cpp.Vec[int32] { ... return kinwave_KWSolver_S_fallback_17 })()
```

A 30-line program reproduces the crash (tests/go_linkbug/run.sh): a func literal, converted to a named func
type, called at once, inside a method that another package inlines. Calling the same literal without the
conversion links, and so does turning inlining off for the package.

Two fixes were possible. Turning off inlining for the whole engine (`-gcflags=openswmm/engine=-l`, which a
subagent found) would have worked, but it slows every small accessor in the engine. The other fix is narrow:
tools/cs2x/GoFn.cs (Invoke, ImmediateLambda) now calls an immediately invoked lambda as a plain func literal,
`(func() *cpp.Vec[int32] { ... })()`. The named type adds nothing there, because the value is used once.

The first guess about the cause was wrong. The crash named `func1.func3`, a closure inside a closure, so the
translator was changed to write a conditional operator as if/else instead of a closure called on the spot. That
change was worth keeping (it removed 758 of 1,274 such closures), but the linker still crashed, now on
`func1`. Only the minimal program separated the real trigger from the pattern around it.
