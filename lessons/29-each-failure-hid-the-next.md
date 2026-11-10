The 16 FV structure-coupling decks failed at the first TODO they met, and each fix uncovered the next one down the same path; none of them passed until at least three causes were fixed.

The order, as the decks met them (agree_pass.inp needed 1, 2 and 5; sdm_fv_o1.inp met 1, 3 and 4; valve_closure.inp
ended on the nullptr test of 5):

1. Routing.cpp's refresh callback, a lambda converted to a function pointer (lesson 17).
2. ExplicitFvSolver::updateNodes calling refreshDummyFlows with its default argument (lesson 21).
3. NativeGmres::solve, reached through FV water-quality mixing, sizing its workspace with
   `std::min(controls.workspace_limit_bytes / sizeof(double), std::vector<double>().max_size())`, which the back end
   had no rule for:

   ```csharp
            throw new global::System.NotImplementedException("TODO vector::max_size"); // line 96
   ```

   tools/cpp2x/std_calls.py `vec_method` now writes libstdc++'s value on x86-64, PTRDIFF_MAX / sizeof(T)
   (1152921504606846975 for a double).
4. ConservativeMixing::solve's std::swap through references (lesson 28).
5. The orifice scatter's nullptr test on an empty vector's data() (lesson 27), in valve_closure.inp, and the missing
   run_stats override (lesson 26), which only changed the report.

The 480 corpus reaches none of these: no model in it uses the explicit FV solver with structure coupling or FV water
quality. The TODO list had all of 1 to 3 on it from the start (cs/gen/_todo.txt, one occurrence each).

The rule: with a pipeline of failures like this, gate the subset after each fix and read the new first failure; and
treat the TODO list as a list of known crashes, ranking each site by what reaches it rather than by its count.
