TypeScript with its type arguments left in can still be valid JavaScript: `new MIt<cpp.Pair<K, V>>(null, items, i)` parses in JavaScript as two comparisons and a shift, so a type-stripped runtime has to be checked against the compiler's own output, not only parsed.

The JavaScript port's runtime (js/src/cpp) is the TypeScript port's runtime (ts/src/cpp) with the types removed by a
small script, js/tools/ts2js.mjs, which tokenizes the TypeScript and cuts out annotations, casts, type parameters
and type arguments. Its first version tokenized `>>` as one token (JavaScript's shift operator), so a type argument
list that closes two levels at once was not recognized as one. In ts/src/cpp/containers.ts (UMap's insert):

```typescript
		return cpp.Pair.new_A_B(new MIt<cpp.Pair<K, V>>(null, this.items, i), added);
```

came out unchanged, and Node accepted it: `node --check` passed, because JavaScript reads it as

```javascript
		return cpp.Pair.new_A_B((new MIt < cpp.Pair < K), (V >> (null, this.items, i)), added);
```

a call with three arguments, the first a chain of comparisons (with `K` undefined, a ReferenceError only when the line
runs) and the second a shift. The engine would have failed at the first unordered_map insertion, or, had `K` and `V`
been names in scope, gone on with a boolean where an iterator belongs.

What found it was a comparison with the program tsc writes from the same source for the TypeScript port
(ts/dist/src/cpp/containers.js): js/test/compare_ast.mjs parses both with acorn and compares the syntax trees, and it
reported `arguments.length: 3 vs 2` at that call. The same run found `let fa: string, fb: string;` stripped to
`let fa, fb: string;` (a syntax error: the declarator after a declarator without an initializer was not looked at),
and one import tsc drops that the script kept (`import { CFile, R }`, CFile used only as a type). After the fixes
(every `>` a token of its own, so a `>>` can close two lists; a declarator list re-armed at every comma; unused
imported names dropped), all 22 runtime and math files, and the generated engine against tsc's engine.js, have
identical syntax trees.

The rule: when a translation is meant to equal what an existing compiler makes of the same source, compare with that
compiler's output, structurally; a parse or a syntax check proves only that the result is some program.
