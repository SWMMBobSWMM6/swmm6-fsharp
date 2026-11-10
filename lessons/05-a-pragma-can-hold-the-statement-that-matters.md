An OpenMP pragma is not decoration: `#pragma omp atomic` and `#pragma omp master` own the statement under them, and dropping the pragma must not drop the statement.

The ports run SWMM6 on one thread, so the translator turns every OpenMP directive into the plain statement it
governs. In clang's AST most directives (`parallel`, `for`, `single`) wrap their statement in a CapturedStmt, and
the translator looked only there. Three kinds do not: `atomic`, `master` and `critical` hold their statement
directly. For those the translator found no CapturedStmt and emitted nothing.

The cost showed up in the first model to run to the end. The dynamic wave solver counts unconverged nodes per
thread and adds the counts together under `#pragma omp atomic`:

```cpp
#pragma omp atomic
        unconv_shared += local_unconv;
```

With that line gone the count stayed zero, every step "converged" after two Picard iterations, and the
results drifted from step 4 on. The report said 0.00 iterations per step and 100 percent of steps not converging,
because the iteration count was also set under `#pragma omp master`:

```cpp
#pragma omp master
        {
            steps_final = t_steps;
            converged_final = t_converged;
        }
```

SWMM6's own per-step trace (`SWMM_TRACE_RSTEP`, a CSV of hexadecimal state sums) found it in one run: the first
three steps matched the reference to the bit except the iteration column, which read 0 against 2.

The rule: when a construct is translated by removing it, check every form the construct takes in the AST, and
count them. Here a census of directive shapes over the whole engine took one short script and listed all
fifteen forms, three of which had been silently emptied.
