A local that a Groovy lambda captures becomes a `groovy.lang.Reference` typed as its wrapper class, in the lambda and in the method around it, so `<` on a captured double is Groovy's `compareTo` and `*` is `NumberNumberMultiply`, even under `@CompileStatic`.

The Java translator writes C#'s lambdas as lambdas, and Java captures an effectively final local by value. The
first Groovy engine compiled the same text, and passed all 40 models of a development sample. Reading its bytecode
(`javap -c` over every engine class, counting calls into Groovy's dynamic runtime) found this, in a method of
DefaultOutputPlugin:

```groovy
		double step_ms = (1000.0d * (ctx.@options).@report_step);
		...
		FN122 __lam1 = (int report) -> {
			return F_datetime.addSeconds((ctx.@options).@start_date, ((((double) report) * step_ms) + 1.0d) / 1000.0d);
		};
```

compiled inside the lambda (javap -c of `DefaultOutputPlugin$_validateCheckpoint_lambda2.doCall`) to

      27: getfield      #29     // Field step_ms:Lgroovy/lang/Reference;
      30: invokevirtual #42     // Method groovy/lang/Reference.get:()Ljava/lang/Object;
      33: ldc           #62     // class java/lang/Double
      35: invokestatic  #50     // Method org/codehaus/groovy/runtime/ScriptBytecodeAdapter.castToType
      41: invokestatic  #72     // Method org/codehaus/groovy/runtime/dgmimpl/NumberNumberMultiply.multiply:(Ljava/lang/Number;Ljava/lang/Number;)Ljava/lang/Number;
      44: invokestatic  #78     // Method org/codehaus/groovy/runtime/typehandling/DefaultTypeTransformation.doubleUnbox

and a test class showed the same thing in the declaring method after the lambda: `double z = step * 3.0d` became
`NumberNumberMultiply`, and `step < z` became `ScriptBytecodeAdapter.compareLessThan`, which compares two Doubles
with `compareTo`: `-0.0 < 0.0` is true there and `NaN > 1.0` true, where Java and C# say false. The engine had 131
`NumberNumberMultiply`, 76 `compareLessThan` and 21 `NumberNumberDiv` call sites, nearly all in lambdas and the
methods that make them; none had met a NaN or a signed zero in the sample, which is why a gate run alone would
not have shown them.

Cs2Groovy now copies each captured local into a fresh local just before the lambda, and copies that again into a
typed local at the top of the lambda body:

```groovy
		double __cap11 = dt;
		FN74 __lam27 = (int slot_4) -> {
		double __cap12 = __cap11;
```

so only the fresh local becomes a Reference, the method's own local stays a primitive, and the lambda's arithmetic
is on a primitive. A captured local is never assigned after the lambda is made (the Java translator puts any
such local in a cell, and then the cell is what is copied), so the copy holds the same value. After the change the
engine's bytecode has no `NumberNumber*` call and no `compareLessThan`, `compareEqual` or other `compareTo`-based
comparison left (only `compareIdentical`, which is `===`).

Written by Claude (Anthropic) for Robert Dickinson's SWMM6 ports.
