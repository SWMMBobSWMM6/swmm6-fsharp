The Fortran engine compiled without a warning and failed every smoke model, from three translator mistakes that each dropped a statement or a binding silently; a translator that throws on what it does not handle would have shown all three at translation time.

Found by the agent writing part D of the Fortran runtime (the build and the runner), on the first runs.

**1. IDisposable.Dispose was never called.** The runtime calls the `dispose` binding of `cpp_object` (Dtor.Run,
Mv.Delete), and the translator bound an engine class's Dispose to it only when the method was not virtual:

```
if (... && SEq.Equals(dm.ContainingType.OriginalDefinition, t) && !IsVirtualish(dm))
    ti.Bindings.Add("procedure :: dispose => " + Wrapper("dispose", t));
```

The 20 classes with a virtual Dispose got only their own `v_dispose` binding, which nothing calls. So the engine's
StepGuard never cleared `step_active_`, and step 2 failed with "Error at step 1"; the report plugin never wrote the
end of the report. Fix (F90Types.cs): bind `dispose` for a virtual Dispose too; the wrapper already calls it through
`self%v_dispose`, so an override is reached.

**2. Stores to a byte of a C string were dropped.** A CPtr element is a location of kind 'C'
(`buf_%b%a(buf_%o + i)`), and both store routines handled only the other three kinds:

```
case 'V': lines.Add(p + loc.D + " = " + v); break;
case 'P': lines.Add(p + loc.D + " => " + v); break;
case 'S': lines.Add(p + loc.D + "%p => " + v); break;
```

so `buf[(int)(i)] = (byte)toupper(...)` in Tokenizer::parse_boolean computed the value and threw it away, and every
YES/NO option in an .inp file read as NO; `nl[0] = 0` in the report plugin became nothing. Fix (F90Fn.cs Store,
StorePre): 'C' stores like 'V', and any other kind throws a Todo instead of falling through.

**3. 74 of 968 lambdas read a temporary that was never assigned.** A lambda whose parameter is erased (`class(*)`
in the delegate's interface) converts it to its real type in the prologue:

```
Prologue.Add(ln + " => " + Cast("*", dn, p.Type));
```

but Cast writes its `select type` into Pre, the statements before the current statement, and returns the
temporary; the prologue got `c_2_ => zz1` and the `zz1 => h_uas_lidnodeconfig(a1)` went to a Pre that the body never
emitted. The two models that called such lambdas (LID validation's any_of, the report's stable_sort comparator)
crashed. Fix (F90Fn.cs ToPrologue): an expression made for the prologue takes its Pre statements with it.
