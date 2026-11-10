When two copies of a function land on one key and both have bodies, the collector kept the first and dropped what only the second said; for the specializations of a member template that included a default argument, which clang instantiates only where a call uses it.

ExplicitFvSolver declares a member template with a default:

```cpp
    template <class ArrivedFn>
    void refreshDummyFlows(double dt, const FvStepForcing& forcing, ArrivedFn&& arrived, bool book_receipts = true);
```

and calls it four times with four lambdas, two passing `false` and two relying on the default, e.g. in updateNodes:

```cpp
    refreshDummyFlows(dt, forcing, [&](std::size_t un) {
        double sum = 0.0;
        ...
        return sum * dt;
    });
```

The four specializations have distinct mangled names, but the out-of-line definition in ExplicitFvSolver.cpp lists
them again under its own FunctionTemplateDecl, with the same ids and without mangled names, and pass 1 of
tools/cpp2x/collect.py re-keyed them by scope, name and line: all four became `openswmm::fv::refreshDummyFlows@2985`.
That is harmless for the code (the closure parameter is erased to `object` in C#, so one method serves all four) but
`Store.put` handled a second copy only when one of the two lacked a body:

```python
        if d['kind'] in FUNC_KINDS and has_body(d) and not has_body(old):
            merge_decl(d, old)
            self.decls[key] = d
        elif d['kind'] in FUNC_KINDS and has_body(old) and not has_body(d):
            merge_decl(old, d)
```

The copy kept was limitPositivity's specialization, which passes `false` and so has no instantiated default, and the
two calls that use the default became

```csharp
            throw new global::System.NotImplementedException("TODO default argument with no expression"); // line 3050
```

Every FV deck that reached ExplicitFvSolver::updateNodes stopped there; the 480 corpus has none, and the SUBSTEP decks
had stopped one step earlier, on lesson 17.

The fix (collect.py `merge_defaults`, called from `Store.put` when both copies have bodies): the copy kept takes any
default argument only the other has. The calls are now `refreshDummyFlows(dt, forcing, (FN119)(...), true)`.

The rule (lesson 01 again, one level deeper): wherever two copies of a declaration are reduced to one, list what each
copy can say that the other cannot, and merge all of it, not only for the declaration/definition pair.
