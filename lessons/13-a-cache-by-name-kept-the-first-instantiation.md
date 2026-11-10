The Fortran translator cached each runtime member's signature by its Fortran name, and the cache kept the first instantiation's type arguments, so every `new Map<K, V>()` in the engine got the element makers of whichever Map the translator met first.

Found by the agent writing part B of the Fortran runtime (containers), reading the engine's calls before anything
could run. In tools/cs2x/F90Rt.cs the cache key had no type arguments, which is right for the signature itself (one
Fortran procedure serves every `Map<K, V>`, its keys and values erased to `class(*)`):

```
var key = "rt " + raw + (nVarArgs > 0 ? "" : "");
if (rtSigCache.TryGetValue(key, out var cached)) return cached;
```

but the erased container's extra arguments, a maker and a copy mode per type argument, were stored in the cached
signature with the first instantiation's type:

```
s.Params.Add(new RtParam { Kind = PK.Maker, Cs = ta, Decl = "procedure(cpp_maker)" });
```

and every call site read them back from there (`tail.Add(HMk(rp.Cs!))`). So C#'s
`this.ext_options = new global::Cpp.UMap<global::Cpp.Str, global::Cpp.Str>();` became

```
zz5 => cpp_umap_new_funckul_funckkb_funcv(null(), null(), null(), h_mk_i, 0, h_mk_cpp_umap, 2)
```

(the makers of some other UMap), and a `Map<int, MeshSnowRow>`'s `operator[]` would have inserted an object of an
unrelated class. 39 UMap, 29 Map, 16 UMapC, 5 Set, 4 Stk and 3 Que constructions, and Pair, Vec and the generic
Algo calls, all shared one set.

The fix: a maker or copy mode records the index of its type argument (in the class's or the generic method's type
arguments), and each call site takes the type from its own symbol (`TaAt(rp, ct, m)`). The same mistake one level
down: the helper that makes a default element (`h_mk_<type>`, `h_new_<type>`) was named by the Fortran type, which
erases the type arguments, so `Pair<Str, double>` and `Pair<int, int>` shared one; such helpers are now one per
instantiation (`h_mk_cpp_pair_cpp_str_d`). Helpers grew from 58 to over 400.

The rule: whatever a cache keyed by an erased name keeps must not depend on what was erased.
