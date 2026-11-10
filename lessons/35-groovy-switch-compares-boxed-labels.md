Java converts a `char` case label to an `int` selector's type; Groovy's switch compares them as boxed objects with `isCase`, and an `Integer` is never equal to a `Character`.

The Java runtime's printf reads the conversion letter as an int and switches on it with char labels:

```java
			int conv = f[k] & 255;
			...
			switch (conv) {
				case 'd': case 'i': {
				...
				case 'f': case 'F': case 'e': case 'E': case 'g': case 'G': case 'a': case 'A': {
```

Groovy 3 parses this unchanged (J2Gv had already written each `'f'` as `((char) 102)`, since `'f'` is a String in
Groovy), and compiles it, with `@CompileStatic`, to a chain of `ScriptBytecodeAdapter.isCase(label, value)` calls
on boxed values: `Character('f').equals(Integer(102))` is false, no case matched, and the first printf of a double
in the runtime checks fell through to the default:

    Exception in thread "main" java.lang.IllegalArgumentException: printf %f
    	at cpp.R.Format(R.groovy:1143)

groovy/tools/J2Gv.java now writes every constant label in the selector's type (`case 102: case 70: ...`), which is
what javac does to it. The engine's switches do not reach `isCase` at all: Cs2Groovy writes a switch as an if/else
chain on a temporary of the selector's type (tools/cs2x/GROOVY.md). After the fix the runtime checks
(groovy/test/run_runtime_checks.sh) agree with glibc and libstdc++ on every case: 0 libm mismatches, 179,756
printf and 1,728 scanf cases identical.

Written by Claude (Anthropic) for Robert Dickinson's SWMM6 ports.
