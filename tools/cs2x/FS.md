# C# to F#: how tools/cs2x/Cs2Fs.cs maps the generated engine

The F# engine (fsharp/gen/Engine.fs) is written from the verified C# engine (cs/gen, with the engine's hand-written
partial classes cs/runtime/Hand_*.cs) through Roslyn's semantic model: one IOperation tree per method, built into a
small statement tree, restructured and printed as F#. The translator is Cs2Fs.cs (names, types, the driver),
FsTypes.cs (declarations, statics) and FsFn.cs (method bodies, expressions); build_fs.sh builds it against the
Roslyn in the .NET SDK.

    tools/cs2x/build_fs.sh && dotnet tools/cs2x/bin/cs2fs.dll cs fsharp/gen

It writes `fsharp/gen/Engine.fs` (one file, `namespace rec Engine`, 1,257 types, about 148,000 lines) and, beside
`gen`, `cs2fs_todo.txt` (constructs not translated: one, an empty enum given a placeholder member),
`cs2fs_runtime.txt` (every runtime and System member the engine uses, with counts) and `cs2fs_stats.txt` (how the
jumps were translated). The runtime (`fsharp/runtime`, namespace `Cpp`) is the C# runtime (`cs/runtime`) translated
by hand with the same classes, members, fields and `ref` parameters, so a runtime call is printed exactly as the C#
engine makes it: `Cpp.R.sscanf(...)`, `this.v.[i]` on a `Cpp.Vec<float>`, `Cpp.Ptr<int>(a, 0L)`.

## Types

| C# | F# | notes |
|---|---|---|
| bool, sbyte, byte, short, ushort, char | bool, sbyte, byte, int16, uint16, char | |
| int, uint, long, ulong | int, uint32, int64, uint64 | F#'s operators are unchecked, as C#'s are by default; shifts are `<<<` `>>>`, bit operations `&&&` `\|\|\|` `^^^` `~~~` |
| float, double | float32, float | |
| string, object | string, obj | |
| T[] | 'T[] | `Array.zeroCreate`; a C# array initializer is `[\| a; b \|]` |
| engine class | a class in `namespace rec Engine`, `[<AllowNullLiteral>]` | nested and namespaced names are flattened as in the Kotlin port (`dynwave_DWSolver`, `F_` for a file's static class, `F__global` for the C API) |
| engine interface | an interface, `[<AllowNullLiteral>]` | a class implements it with members that forward to its own methods |
| engine enum | an F# enum with the C# values | compared and switched on through `LanguagePrimitives.EnumToValue`; the empty `PluginType` gets a placeholder member |
| delegate | an F# delegate type of the same signature | a lambda is `D(fun a b -> ...)`; a lambda with a statement body is bound before its statement (`let lam'N = ...`) |
| runtime type | `Cpp.X` | `Cpp.Vec<'T>`, `Cpp.Ptr<'T>`, `Cpp.CPtr`, `Cpp.Str`, `Cpp.UMap<'K,'V>`, ... |

The engine has no structs of its own; the runtime's structs (CPtr, Ptr, Iter, Pair) are F# structs.

## Classes

Fields are `[<DefaultValue(false)>] val mutable`, so they start at zero or null as C#'s do. Every C# constructor
becomes an explicit F# constructor, `new (a, b) as this = { inherit Base(x) } then body`, with the field
initializers first, as C# runs them. Virtual methods are `abstract` with a `default`; an override is `override`.
A method or field that hides a base class member (C#'s `new`) gets its class's name appended
(`reset_dynwave_DWSolver`), since F# does not hide by name. Properties and indexers that return a reference
(`ref T`) are F# byref returns, used as `&v.[i]` or `&(f x)`.

## Statics

F# has no class-level static fields that start in a defined order across one recursive namespace, and a
`namespace rec` may not hold `let mutable`. C# initializes a class's static fields when the class is first used; the
engine's initializers are values with no side effects (literals, tables, new objects). So each engine class with
static fields gets a holder class `X__S` with one instance, `X__S.I`, whose fields are the static fields (873), and
`Init__.run ()` creates every holder and runs the 758 initializers in an order where each runs after the
initializers of the fields it reads, directly or through the engine methods it calls (a depth-first walk over the
initializers' operation trees). `main` (runtime/Program.fs) calls it before anything else. A static field is
`X__S.I.name`.

## Statements

F# has no `return`, `break`, `continue` or `goto`, and an `if`, a loop or a `match` is an expression. Each C# body
is built into a statement tree and restructured (the header of FsFn.cs has the rules; the counts are
cs2fs_stats.txt's):

- Code after a statement that cannot complete normally is dropped, as C#'s compiler drops it (70 statements).
- `if (c) <jump>; rest`, where one branch cannot complete and the other can, becomes `if c then <jump> else rest`;
  a chain of early exits becomes `if ... elif ... else` (4,756).
- A jump in tail position needs nothing: a return whose value is the method's last expression (7,361), a continue
  at the end of a loop body (1,339), the last break of a switch section (427).
- Every other jump sets a flag (`ret'`, with the value in `rv'`; `brk'N`, `cnt'N`, `sw'N`, `jmp'N`) and the
  statements it skips are wrapped in `if not (flags) then`; in a block whose value is the result,
  `if ret' then rv' else rest` (784 returns, 172 breaks, 85 continues by flag).
- A loop tests its flags before C#'s condition (`while not brk'3 && not ret' && cond do`); a `for` loop's step runs
  after a continue but not after a break or return; a do-while evaluates its condition at the end of the body.
- `switch` is `match` on constants, default last; C#'s sections never fall through.
- The gotos of `hydrology/surface/UebReference` (four methods) become a dispatch loop,
  `while st'N >= 0 do match st'N with | 0 -> ... | 1 -> ...`, with every local of the method declared before it.
- `using` declarations become `try ... finally` with `Dispose`.

## Expressions

F# has sequencing, `let` and `if` inside expressions, so C#'s expressions stay expressions: an assignment used as a
value is `(let t = v in x <- t; t)`; `x++` used as a value is a `let` of the old value. C# evaluates a location
before the value assigned to it; where the location is not pure (a call, an index with side effects) its parts are
bound first. A conditional that yields a reference (`ref (c ? ref a : ref b)`) is bound with `let r = ...` before
the call that takes `&r`. In a method argument, `a = b` would be read as a named argument, so comparisons are
parenthesized; so is every prefix application in an argument or operand (`(int x.size())`).

A local that a lambda captures and that is assigned after the capture is a `ref` cell (169); a byref parameter whose
name starts with a capital letter is renamed `p_X` (F# would read it as a pattern). A setter called on a struct
value (C#'s `p[i] = c` on a `CPtr` held in a field or a parameter) goes through a mutable copy, `let mutable o' = p
in o'.[i] <- c`, which is right only because CPtr is a view of its array; any other struct stops the translation
(lessons/42).

## Numbers

A C# conversion is the F# conversion function of the target type (`int`, `int64`, `uint64`, `float32`, `float`,
...), which compiles to the same IL (`conv.i4`, `conv.r8`, ...), so a double out of an integer's range converts as
C# converts it on x86-64. Float arithmetic is IEEE and is never fused. A reference `==` is `obj.ReferenceEquals`;
string `==` is F#'s `=`, ordinal as C#'s.

The math functions the C++ engine takes from glibc go to glibc too: `Math.Exp`, `Pow`, `Sin`, `Cos`, `Tan`, `Asin`,
`Acos`, `Atan`, `Atan2`, `Log`, `Log10`, `Log2`, `Sinh`, `Cosh`, `Tanh`, `Cbrt` and the C# runtime's `LibM.hypot`,
`fmod`, `expm1`, `log1p`, `round`, `remainder` and `nextafter` are printed as `Cpp.LibM.*`, which runtime/LibM.fs
declares with `DllImport("libm.so.6")` (dlsym gives the same symbol versions as the C++ engine's imports:
fsharp/test/check_binding.sh). `Math.Sqrt`, `Floor`, `Ceiling`, `Truncate`, `Abs` and `CopySign`, which IEEE fixes,
stay on .NET's `System.Math`.

## Names

F#'s keywords (`begin`, `end`, `val`, `done`, `open`, `in`, `fixed`, `internal`, `type`, `rec`, ...) are written
in double backquotes (``` ``type`` ```). A local that would shadow another in the same F# scope, where C#'s scopes
differ, gets `_` and a number. Generated names carry an apostrophe (`ret'`, `o'12`, `lam'3`), which no C# name can.

Written by Claude (Anthropic) for Robert Dickinson's SWMM6 ports; see AI-USE.md and fsharp/README.md.
