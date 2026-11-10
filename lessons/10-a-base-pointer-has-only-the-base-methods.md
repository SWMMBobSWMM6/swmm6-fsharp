In Go, an object held through a pointer to its embedded base struct answers only to the base's methods; ask the object itself.

Go gate run go1 passed 479 of 480 models. The odd one, Hydraulics/Allow_Ponding_Low_CE.inp, stops on a routing
error, and the SWMM6 report then ends with a line the Go report lacked:

```
  [Report interrupted — simulation did not complete normally]
```

The C++ destructor of DefaultReportPlugin writes that line. The plugin factory holds its report plugins as
`std::vector<IReportPlugin*>` and deletes each one, which the C# port writes as

```csharp
var p_3 = __r5.a[__i6];
global::Cpp.Mv.Delete(p_3);                                   // Delete(object? o) { (o as IDisposable)?.Dispose(); }
```

C# looks up Dispose on the object's real class. The Go engine holds the same object as `*IReportPlugin`, a
pointer to the base struct embedded in `DefaultReportPlugin`, and the runtime asked that pointer:

```go
func Mv_Delete(o any) {
	if d, ok := o.(interface{ Dispose() }); ok && !isNil(o) {    // *IReportPlugin has no Dispose
```

Go's method set belongs to the static struct type behind the pointer, so the derived class's Dispose never
ran, the file was never finished, and the report lost its last lines.

The fix is `cpp.Dynamic`, which follows the `self_` field every object in an engine class hierarchy carries
back to the most-derived object before any interface test (go/cpp/core.go: Mv_Delete, Dtor_Run, Dispose; the
translator's catch helpers do the same). Gate run go2 then passed 480 of 480.

The general point: any runtime helper that discovers behaviour by asking "does this object implement X?"
must ask the most-derived object. The translator already did this for virtual calls and downcasts; the
runtime's destructor helpers were written as if every pointer pointed at a whole object.
