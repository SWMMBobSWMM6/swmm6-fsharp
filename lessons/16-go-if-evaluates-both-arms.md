The Go translation wrote a C# conditional `c ? p.x : y` as a call, cpp.If(c, p.x, y), which evaluates both arms before choosing, so `p.x` was read even when the condition had just found p nil.

Found by the extra decks (the upstream test decks and the swmm6-rust synthetic decks): 31 of them failed in Go
alone, 23 with a nil-pointer panic and 8 with a report missing its transport table. None of the 480 corpus models
has groundwater transport, and the report's preamble only takes this path when it does. The C# in
src/transport/TransportPolicy.cpp's translation:

```
bool two_zone = (draft != null) ? draft.groundwater : (authored || ...);
```

became, in go/engine/g_transport_TransportPolicy_cpp.go,

```
var two_zone bool = cpp.If((cpp.Obj(draft) != nil), draft.groundwater, (authored || ...))
```

Go evaluates every argument of a call, so `draft.groundwater` dereferenced the nil the condition was testing. The
translator writes the short cpp.If form only for arms it judges free of side effects and faults (GoFn.cs Pure), and
that test counted a field read through any local or parameter as safe. The fix: a field read through a local or
parameter that holds a reference is not safe, so the conditional becomes `func() bool { if c { return p.x }; return
y }()`. All 31 decks then passed. (lessons/10 was the same trap for `c ? ref a : ref b`, fixed then only for refs.)
