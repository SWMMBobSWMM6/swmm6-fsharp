clang 18's JSON dump prints a `bool` template argument as a one-bit signed integer, so `Batch<true>` has the argument -1; a key built from the dump must be spelled the way the type names that refer to it are.

src/engine/hydrology/RichardsColumn.cpp packs Richards columns in a class template used twice:

```cpp
template<bool Packed> struct Batch {
    ...
        if (packed && c.size() >= 8)
            Batch<true>(std::move(c), std::move(r)).run(dt);
        else
            Batch<false>(std::move(c), std::move(r)).run(dt);
```

The two specializations in the dump carry

```json
{"kind": "TemplateArgument", "value": -1}
{"kind": "TemplateArgument", "value": 0}
```

and tools/cpp2x/collect.py `targ` spelled the argument from that value:

```python
    if 'value' in c:
        return str(c['value'])
```

so the records were keyed `openswmm::richards::(anon:...)::Batch<-1>` and `...::Batch<0>`, while every type that names
them says `Batch<true>`, `Batch<false>` or `Batch<true>::Coefficient`. Nothing matched. The emitter wrote both classes
(as `Batch__1` and `Batch_0`) but every use of them became

```csharp
        throw new global::System.NotImplementedException("TODO type openswmm::richards::(anonymous namespace)::Batch"); // line 587
```

and soa_richards_lid.inp stopped in all six ports. A second gap sat behind the first: the name lookup
(tools/cpp2x/resolve.py `known`) knew records and enums but not a class template's own name, which is all an
unqualified `Batch<true>` gives it, because the template exists only as its specializations.

The fixes: `targ` takes the template parameter's type from the ClassTemplateDecl that lists the specialization and
spells a bool argument `true` or `false` (collect.py `index`, `TU.spec_params`); `Resolver.known` accepts the name of
an engine class template. The classes are now `Batch_true` and `Batch_false`, each with only its own `if constexpr`
branch (clang instantiates one per specialization).

The rule: where a dump spells a thing two ways (a value in one place, source text in another), find which spelling
the lookups use and key everything by that one.
