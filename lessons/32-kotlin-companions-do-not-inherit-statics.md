A Java class inherits its superclass's static members by name; a Kotlin companion object does not, so `R.stderr_` stopped resolving when the Java runtime became Kotlin.

The Java runtime splits the C library into two classes, and the engine reaches both through the one name `R`:

```java
// java/src/cpp/RFs.java
public class RFs {
	public static CFile stderr_ = new CFile(new CFile.Stream(new FileOutputStream(FileDescriptor.err)), true);
	public static CPtr getenv(CPtr name) { ... }
// java/src/cpp/R.java
public final class R extends RFs {
```

so the generated Java says `cpp.R.stderr_` and `cpp.R.getenv(...)`, and javac finds them in RFs. kotlin/tools/J2Kt.java
put each class's statics in its own `companion object`, and Kotlin looks a name up in that companion only: the
first build of the Kotlin engine stopped with 718 errors, most of them `Unresolved reference: stderr_`, `getenv`,
`BufOf` on `cpp.R`.

The fix is in J2Kt, not in the engine: for every public static a Java class inherits, the subclass's companion gets
a forwarder,

```kotlin
		var stderr_: cpp.CFile?
			get() = cpp.RFs.stderr_
			set(v) { cpp.RFs.stderr_ = v }
		@JvmStatic fun getenv(p0: cpp.CPtr?): cpp.CPtr? { return cpp.RFs.getenv(p0) }
```

(a getter and setter on lines of their own: Kotlin does not accept them on the property's line). The same build
showed the other two Java-to-Kotlin shape changes that cost errors by the hundred: a Java `Object[]` parameter is
`Array<*>` in Kotlin, because Kotlin's arrays are invariant and the engine passes `Array<Str?>`, and a field that
hides a base class's field of the same name (legal in C# and Java) needs its class's name appended.

Written by Claude (Anthropic) for Robert Dickinson's SWMM6 ports.
