In clang's JSON AST, a member defined outside its class remembers the class only in `parentDeclContextId`.

`void SnowSoA::resize(int n) { ... }` written in a .cpp file appears at namespace level in the dump. Its name, its
mangled name and its body are all there, but nothing says it is a method except the `parentDeclContextId` field,
which holds the id of the class declaration earlier in the same translation unit. The same holds for a nested class
defined outside its enclosing class (`struct Router::RestartState { ... };`).

The first merge of the 204 dumps kept the definition with the body and threw away the in-class declaration, so every
out-of-line method lost its class, its `virtual` and `static` flags and its default arguments. The C# came out with
`this.global::openswmm.snow._F.resize(n)`: a member call to a free function. Two different `RestartState` structs
also landed on one key, `openswmm::RestartState`.

The fix, in tools/cpp2x/collect.py: follow `parentDeclContextId` to the class, and when a definition replaces a
declaration, carry over what only the declaration says (merge_decl).

The general rule: when a later copy of a declaration replaces an earlier one, check which facts only the earlier
copy holds.
