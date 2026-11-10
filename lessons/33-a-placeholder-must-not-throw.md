A hoisted declaration needs a placeholder value; Kotlin's `null!!` type-checks as anything and throws as soon as it runs.

The gotos in hydrology/surface/UebReference make the translator declare a method's locals before its dispatch loop
(the labels jump over declarations). Java and Kotlin both want a value there. For a local that lives in a cell (its
address is taken), the Kotlin translator first wrote the type's "default" as

```kotlin
		var w1: cpp.RefD.Cell = null!!
		var w2: cpp.RefD.Cell = null!!
		...
		__dispatch@ while (true) {
```

`null!!` is a Kotlin expression of type `Nothing`, so it compiles in any position, and it throws
NullPointerException when evaluated, which is at the top of the method, before any statement of the C# has run.
Every deck that reaches the UEB snow model (swmm6-rust's gate decks inithydro/ih_ueb_*.inp among the 452) ended
with `Unhandled exception. java.lang.NullPointerException ... at predicorr__UebReference` and exit code 134; the
corpus has no UEB deck, so the 480 could not have shown it.

KtFn.cs `DefaultForKt` now gives a non-nullable cell type a fresh, empty cell (the C# declaration replaces it when
it runs):

```kotlin
		var w1: cpp.RefD.Cell = cpp.RefD.Cell()
```

The lesson for any back end: a placeholder that only has to satisfy the compiler still runs. Search the generated
code for it (`grep -c 'null!!'`) before the first gate.

Written by Claude (Anthropic) for Robert Dickinson's SWMM6 ports.
