`stream >> x` for a double reads the characters a number can have and stops at the first other one; it does not read a whitespace-delimited token, and an overflow is a failure, not infinity.

src/engine/hydrology/LidNodeTreatment.cpp reads [LID_LAYER_TREATMENT] rows with

```cpp
        if (!(stream >> control >> layer >> pollutant >> removal >> decay)) {
            ctx.errors.push_back("Invalid [LID_LAYER_TREATMENT] row: " + line);
```

Every runtime read a double as the C# one did (cs/runtime/Streams.cs):

```csharp
        public IStream Get(ref double v)
        {
            var t = Token(); if (t.n == 0) { v = 0; return this; }
            var p = t.c_str(); var end = p;
            double r = R.strtod(p, ref end);
            if (end.O != t.n) { failbit = true; v = 0; return this; }
            v = r; return this;
        }
```

libstdc++ does something else. num_get's `_M_extract_float` collects a sign, one of any leading zeros, digits, one
point and an `e` after a digit with its sign, and leaves the first other character in the stream; the gnu
`__convert_to_v` then runs strtod on what it collected, fails (value 0) when strtod does not use all of it, and fails
with the value DBL_MAX when strtod returns +-inf. So, in lid_layer_treatment_rows.inp:

- `Train 2 TSS 1e999 0`: removal overflows: failbit, "Invalid row". The token reader stored +inf and the row went on
  to the treatment validator ("Removal must be between 0 and 100 percent").
- `Train 2 TSS 0x10 0`: removal is 0 and `x10` stays in the stream, so decay fails: "Invalid row". The token reader
  read the token as the hexadecimal 16.
- `Train 2 TSS inf 0`: no character of "inf" is collected: failbit. The token reader stored +inf.

The fix replaces the double extraction in each runtime with num_get's rules (and the sentry's: a stream that is not
good extracts nothing; at the end of the input eof and fail, the value unchanged; eof when the number ends the
input): cs/runtime/Streams.cs `IStream.Get(ref double)`, java/src/cpp/IStream.java `Get_dr`, go/cpp/streams.go
`(*IStream).Get_dr`, f90/runtime/cpp_rt_d.f90 `cpp_istream_get_dr`, py/cpp/streams.py `IStream.Get_dr`,
ts/src/cpp/streams.ts `IStream.Get_dr`. The integer and string extractions still read a
token; no deck has shown a difference there yet, but an integer followed by letters ("12abc") is read by
libstdc++ as 12 with "abc" left, and by the ports as a failure.

The rule: an emulation of a parser is only as good as its stopping rule; check what is left in the stream, not just
the value read.
