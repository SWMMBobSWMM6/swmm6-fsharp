A captureless lambda assigned to a plain function pointer is a call to the closure's conversion operator in clang's AST, and a back end that knows lambdas only as delegates must still translate that call.

The FV solver's structure coupling (FV_STRUCTURE_COUPLING SUBSTEP) hands the solver a C callback and a user pointer.
src/engine/hydraulics/Routing.cpp sets it up with a lambda:

```cpp
        forcing.refresh_user = &rctx;
        forcing.refresh = [](void* user, double t_elapsed) {
            auto* r = static_cast<RefreshCtx*>(user);
            r->self->refreshFvBoundaryFlows(*r->ctx, t_elapsed, *r->eval);
        };
```

`forcing.refresh` is a `void (*)(void*, double)`, so clang wraps the lambda in an implicit user-defined conversion: a
CXXMemberCallExpr of the closure's `operator void (*)(void *, double)`. The C# back end already turns every lambda
into a delegate, and a function pointer into the delegate of its signature, but that member call fell through to the
last line of tools/cpp2x/std_calls.py `method`,

```python
    return todo(fn, 'method %s::%s' % (sn or repr(ot), nm), n)
```

and the generated C# was

```csharp
                throw new global::System.NotImplementedException("TODO method const (lambda at /tmp/.../Routing.cpp:1714:27)::operator void (*)(void *, double)"); // line 1717
```

Every deck with SUBSTEP coupling (16 of the 452 extra decks: agree_pass, sdm_fv_o1, the slot_program decks, ...)
stopped there in all six ports.

The fix (std_calls.py `method`): a conversion operator of a closure type whose result is a function pointer is the
lambda itself, which the back end has already cast to the delegate of the same signature. The line is now

```csharp
                forcing.refresh = (global::Gen.FN20)((object user, double t_elapsed) =>
```

A lambda with captures has no such conversion in C++, so the case is always a lambda the delegate can stand for.
The rule: the TODO list (cs/gen/_todo.txt) is a list of failures waiting for an input that reaches them; a site with
one occurrence is worth reading before the corpus finds it.
