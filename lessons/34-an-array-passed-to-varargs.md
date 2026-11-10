Java passes an `Object[]` to a varargs parameter as the varargs themselves; Kotlin passes it as one element unless it is spread with `*`.

C#'s `params object[] items` lets a call pass the elements or an array. The Java translator writes the array:

```java
		return new cpp.TupN(new Object[] {(Object) x.type, (Object) x.culvert_code, (Object) x.transect, ...});
```

and javac binds it to `public TupN(Object... items)` as the whole argument list. J2Kt made that constructor
`constructor(vararg items: Any?)`, and the Kotlin translator's first form of the same call,

```kotlin
		return cpp.TupN(arrayOf<Any?>(x!!.type, x!!.culvert_code, x!!.transect, ...))
```

compiles, but makes a tuple of one element, the array. Comparing two of them (F_fv groups cross-sections by this
tuple) reached TupN's element comparison with an `Object[]`, and the upstream deck
tests/parity/slot_program/decks/transmission_main.inp ended with

    Unhandled exception. System.NotSupportedException: tuple element class [Ljava.lang.Object;
    	at cpp.TupN$Companion.Cmp(TupN.kt:76)
 KtFn.cs `ArgOne` now writes a params array's elements as separate arguments, and an array that is
already an array value as `*arr!!`. Kotlin will not warn here: an array is a perfectly good `Any?`.

Written by Claude (Anthropic) for Robert Dickinson's SWMM6 ports.
