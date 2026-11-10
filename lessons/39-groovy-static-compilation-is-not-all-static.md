`@CompileStatic` still compiles some plain-looking Groovy to reflective calls: a store to another object's field, a static field read with `.@`, a cast lambda, and a method reference.

Counted with `javap -c` over the engine classes of the first Groovy build that ran (calls into
`ScriptBytecodeAdapter` outside the methods Groovy generates for every class; groovy/test/dynamic_calls.sh):

| Groovy | compiles to | sites | written instead |
|---|---|---|---|
| `o.f = v`, `o.f += v`, `cell.v = x` | `ScriptBytecodeAdapter.setGroovyObjectProperty` / `setProperty` | 674, all `cell.v` (fields were `o.@f` from the start, after a test class) | `o.@f = v`, `o.@f = o.@f + v`, `cell.@v = x` |
| `C.@f` as a value (static field) | `ScriptBytecodeAdapter.getField` (then `castToType`) | 5,434 | `C.f` to read, `C.@f = v` to write |
| `((F) (a) -> ...)` or a lambda in a field initializer | a `Closure` subclass and `castToType` to F | | `F __lam = (a) -> ...` (a typed local) |
| `X::new`, `o::m` | dynamic method calls | | a lambda |
| `a[i] /= v` | `getAt` / `div` / `putAt` by name | | `a[i] = a[i] / v` |

(The SWMM5 Groovy port's notes describe the field store, the method reference and `/=`; the static-field read and
the cast lambda were found here: SWMM6's engine reads statics such as `cpp.Raw.V` in many constructors and hands its
containers their element makers as lambdas.) All of these give the right answer, only slowly, so the gate cannot see them. After the rewrite the
engine's classes call Groovy's runtime only for `castToType` after a conditional expression or a generic return
(a null check and an `isInstance`), `compareIdentical` for `===`, and the unboxing helpers
(groovy/results/dynamic_calls.txt). The runtime, which J2Gv edits rather than rewrites, keeps 241 reflective stores
to other objects' fields.

Written by Claude (Anthropic) for Robert Dickinson's SWMM6 ports.
