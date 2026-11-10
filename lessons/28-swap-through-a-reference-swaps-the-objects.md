`std::swap(a, b)` exchanges two objects; when the C# holds either through a variable standing for a C++ reference, exchanging the variables exchanges nothing the caller can see.

src/engine/quality/ConservativeMixing.cpp ends its solve with

```cpp
ConservativeMixing::Result ConservativeMixing::solve(const MixingScalar& scalar, MixedScalar& output) {
    ...
    auto& accepted=candidate_;
    ...
    std::swap(output,accepted); // retain both buffers; no partial state on failure
    return result;
```

`output` is the caller's `result[row]` and `accepted` is the member `candidate_`. The translator wrote every std::swap
as a swap of two C# locations:

```csharp
            global::Cpp.Std.swap(ref output, ref accepted);
```

which is right for fields, elements and numbers (the other 16 swap sites in the engine) but here exchanged the
parameter variable and the local variable, so neither result[row] nor candidate_ changed. The FV quality transport
then copied concentrations from an empty result and the three sdm_fv decks died with IndexOutOfRangeException in
FvQualityTransport::execute, once lesson 29 had let the GMRES solve run at all.

The fix (tools/cpp2x/std_calls.py `free`, case swap, and `ref_variable`): when either argument names a variable
declared as an lvalue reference, the contents are exchanged with std::swap's three moves,

```csharp
            ((global::Gen.FN3)(() => { var __t = global::openswmm.quality.MixedScalar.MoveNew(output); output.MoveFrom(accepted); accepted.MoveFrom(__t); }))();
```

written as an immediately invoked lambda, a form every tools/cs2x back end already translates (lesson 09), so the
five derived runtimes need nothing new (a first version called a new runtime helper, Std.swapContents, which each of
them would have had to grow). A swap through a reference to anything but an engine class, or of operands other than
names and member accesses (each is written twice), is a TODO, so a new case cannot pass silently.

The rule: a C++ reference is not a variable; any C# operation on "the variable" (assignment, swap, ref passing) must
be checked to act on the referent.
