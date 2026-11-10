A rule that recognizes a pattern should be checked for firing at all; one that never matches fails silently, and everything downstream quietly takes the other branch.

The C# back end had a test, `is_std_move`, for calls to `std::move`, so that `rules_.push_back(std::move(rule))`
moves the rule into the vector rather than sharing it. Since clang 15, `std::move` and `std::forward` are compiler
builtins. clang 18's AST reaches them through an implicit `BuiltinFnToFnPtr` cast, and the test looked one level
too shallow for the name. It never matched anything, anywhere in the engine.

Nothing failed to compile. Most moves happened to be handled by a second rule that looks at the value category
(xvalue), and 378 of 480 models were already byte-identical. The rest showed up as models whose control rules did
nothing. SWMM6 parses each rule into a local `Rule`, pushes it with `std::move`, then resets the local:

```cpp
auto finishRule = [&]() {
    if (!current_rule.name.empty()) {
        rules_.push_back(std::move(current_rule));
        rules_parsed++;
    }
    current_rule = Rule{};
```

In C# the vector received the local object itself, and the reset then emptied the object the vector held. Every
rule ended up with no name, no premises and no actions, so no control action was ever taken. A harness that
opened one model and printed the parsed rules found it in a minute: six rules, all blank.

The rule for next time: when a back end has a special case for a library call, count its matches over the whole
program once. Zero matches for something as common as `std::move` is a bug in the matcher, not a fact about the
program.
