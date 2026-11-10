The census linked an override to the virtual method it overrides by comparing the two function types as strings, and an out-of-line definition spells its return type with the class it is written outside of.

src/engine/hydraulics/fv/INetworkSolver.hpp and ExplicitFvSolver.cpp:

```cpp
    virtual RunStats run_stats() const noexcept { return {}; }                // in class INetworkSolver
...
INetworkSolver::RunStats ExplicitFvSolver::run_stats() const noexcept {      // the override, out of line
```

clang types them `RunStats () const noexcept` and `INetworkSolver::RunStats () const noexcept`, and
tools/cpp2x/census.py matched overrides on name and type:

```python
            by_rec_name[(par, nm, sig)].append(key)
...
                for o in by_rec_name.get((d2, nm, sig), []):
```

so ExplicitFvSolver::run_stats was never reached, never emitted, and every C# run of the FV solver called the base
method, which returns zeroed counters. SWMMEngine then left `fv_nsteps` at its -1 sentinel and the report lost its
"FV Solver Statistics" block, the one difference in agree_pass.inp and transmission_main.inp once lessons 17 and 21
let them run (their .out files were already identical).

The fix (census.py `params_sig`): an override is matched by name and parameter list with qualifiers, `() const
noexcept`, which is what C++ requires of it; the return type may be written any way. The reachable set grew by
exactly this one method. A search over the whole decls file for methods marked `override` (or virtual) that were not
reached while a base method of the same name was found no others.

The rule: compare declarations by what the language says identifies them, not by how a printer spelled them; and
when a reachability pass decides what gets translated, check its misses against the language's own marks (here the
OverrideAttr clang records on every `override`).
