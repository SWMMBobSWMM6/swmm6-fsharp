In clang's JSON dump an initializer list with an array filler has no "inner": the filler and then every element are listed under "array_filler", so a translator that reads "inner" sees an empty list and fills it with zeros.

src/engine/hydrology/LidNode.cpp builds the hydraulic layers of a legacy BC/RG/IT/PP LID control:

```cpp
    if (s[0] > 0.0)
        out.push_back({LidNodeLayerKind::Surface, {s[0], s[1]}});
```

`params` is a `std::array<double, 7>`, so `{s[0], s[1]}` is a list of two values and an implicit filler for the other
five. clang's JSON streamer names a node's child array after the label of its first child, and the filler is
visited first, labelled "array_filler":

```json
"kind": "InitListExpr", "type": {"desugaredQualType": "double[7]"},
"array_filler": [ {"kind": "ImplicitValueInitExpr"}, {"kind": "ImplicitCastExpr", ... s[0] ...}, ... s[1] ... ]
```

tools/cpp2x/collect.py copied "array_filler" as it was and read children only from "inner", so the C# came out as

```csharp
                @out.push_back(new global::openswmm.LidNodeLayer() { kind = global::openswmm.LidNodeLayerKind.Surface, @params = new double[] { 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0 }, ...
```

for all four such layers. A surface layer of zero thickness fails `validLayer`, and lid_nodes_rows.inp and
lid_validate_stacks.inp reported "LID node S5: invalid layer parameters at row 1" where the C++ reported nothing. The
same quirk had hidden the `0` of `long tier_cells[8] = {0}` in three records, harmlessly.

The fix (collect.py `unfold_array_filler`, called on each dump element as it is read): the entries after the
filler move to "inner", the filler stays under its label, and pass 1 walks the filler first (document order, for
the file and line tracking). The layer is now `@params = new double[] { s[((ulong)0)], s[((ulong)1)], 0.0, ... }`.

A census of node keys holding child arrays over the whole decls file (`inner`, `fields`, `lookups`, `enumerators`,
`array_filler`) showed this to be the only label of clang's that hides children.

The rule: when a format names a child array by its first child, every labelled child is a place where ordinary
children can be hiding; list the labels once and check each.
