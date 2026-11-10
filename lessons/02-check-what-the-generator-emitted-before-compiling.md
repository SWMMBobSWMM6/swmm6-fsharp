Before reading compiler errors, count what the generator emitted: the first 680 errors hid that no classes existed.

The census (tools/cpp2x/census.py) worked out which records the engine reaches, printed the count (386 of 587), and
then wrote only the reachable *functions* to census.json. The emitter took that list as its filter, so it emitted
no classes at all, only free functions in `_F` classes. The C# compiler stopped at syntax errors in a handful of
files and never got far enough to complain about the 386 missing types.

A ten-second check would have shown it: `grep -c "partial class" gen/*.cs` gave 229, all of them `_F`.

The rule: after a generator runs, count its output by kind (classes, methods, free functions, TODO sites) against the
census before compiling anything.
