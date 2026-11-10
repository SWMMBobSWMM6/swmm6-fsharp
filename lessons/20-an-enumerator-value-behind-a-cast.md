An unscoped enum without a fixed type whose values are all nonnegative has `unsigned int` as its underlying type, and clang wraps every initializer in a cast to it; the C# enum emitter looked for the value only one level down, so 486 enumerators lost their values.

include/openswmm/engine/openswmm_output.h numbers the UEB snow fields of the binary output from 65536:

```c
    SWMM_OUT_SUBCATCH_POLLUT_BASE = 8, /**< First pollutant concentration slot. */
    ...
    SWMM_OUT_SUBCATCH_SNOW_SWE = 65536,          /**< m, including retained liquid. */
```

In the dump the initializer is `ImplicitCastExpr <IntegralCast> unsigned int` around `ConstantExpr value "65536"`.
tools/cpp2x/emit_cs.py `enum_lines` took a value only from a ConstantExpr child of the enumerator:

```python
            for x in kids(c):
                if x.get('kind') == 'ConstantExpr' and 'value' in x:
                    v = x['value']
```

so the C# enum declared `SWMM_OUT_SUBCATCH_SNOW_SWE,`, which C# numbers 9. The output plugin writes the codes of the
UEB fields into the .out header (`writeInt4(SWMM_OUT_SUBCATCH_SNOW_SWE + v)`), and the six UEB decks (ih_ueb_*,
ih_met_file) came out with codes 9..18 where the C++ wrote 65536..65545: ten words of the header, every result
after it identical.

The same cast hid the values of every enumerator of such enums: 486 of them, of which 45 had a different value in C#
(comparing the old and new cs/gen enums): `SWMM_ERR_INTERNAL = 99` (C# 18), the routing totals declared out of order
(`SWMM_ROUTING_LINK_GW_INFLOW` 13 and `SWMM_ROUTING_COUPLING_OUT` 12, swapped), every `SWMM_FilePathRole` (numbered
from 1, C# from 0), the deprecated alias `SWMM_OUT_SYS_LAT_INFLOW = SWMM_OUT_SYS_TOTAL_INFLOW` (C# 15 for 9) and the
trace bit flags (1, 2, 4, 8, 16, 32, 64 as 0, 1, ..., 6). None of the others is read by number in the code the gate
runs, which is why 480 models passed.

The fix (`enum_lines`): look through implicit casts for the ConstantExpr. The only change in cs/gen besides the
three other fixes of this round is those 486 lines gaining `= unchecked((int)N)`.

The rule: an AST pattern match on "the child" should strip the implicit casts first; clang inserts them wherever a
type changes, including places where the source has no conversion at all.
