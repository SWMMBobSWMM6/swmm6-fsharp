F# calls a property setter only on a mutable value, so the F# translator sets `p[i] = c` on a struct through a mutable copy; that is right only when the struct is a view of shared storage, as CPtr is, and the translator now refuses any other struct.

The C# engine writes characters into C strings through CPtr, the runtime's `char*` (a struct of an array `B` and an
offset `O`, whose indexer setter writes `B[O + i]`), held in fields and parameters:

```csharp
                _ = ((sbyte)(saved_line_[(int)(global::openswmm.climate.ClimateFileReader.MAX_LINE)] = unchecked((byte)(((sbyte)0)))));
```

(cs/gen/hydrology_ClimateFile.cpp.cs)

F# rejects the direct translation, `this.saved_line_.[i] <- 0`, with FS0256 ("A value must be mutable in order to
mutate the contents or take the address of a value type"), because `saved_line_` is read as a value and a setter on a
struct value may change it. The translator (tools/cs2x/FsFn.cs, LValue) writes the set through a mutable copy:

```fsharp
(let mutable o'2675 = this.saved_line_ in let i'2676 = ... in o'2675.[i'2676] <- 0)
```

which compiles anywhere and is correct here only because the copy shares CPtr's array: the write lands in `B`, which
the field's CPtr also points to. For a struct whose setter changes its own fields (a small vector, a pair held by
value) the same code would compile, write into the copy, and drop the write silently. The engine's only settable
non-reference indexer on a struct is CPtr's (fsharp/cs2fs_runtime.txt: `property Cpp.CPtr.this[][int]`, 158 uses;
Ptr, Vec and Str return references, which F# sets in place), so the 22 copies in Engine.fs are all of CPtr; but
nothing in the translator said so. It now checks the receiver's type and stops with a TODO for any struct other than
CPtr; the engine regenerates byte-identical.

The rule: when a target language forces a copy to make a construct compile, write down why the copy is equivalent
and make the translator check that reason, rather than leaving it true by luck of the current input.
