To know whether the translator saw every function, compare against the symbols the reference compiler actually defined, not against your own lists.

The C# build compiled cleanly, and the first model run died with "TODO no body: add_input_plugin". The body of
PluginFactory::add_input_plugin is plainly in PluginFactory.cpp, so the translator had lost it somewhere between
clang's AST dump and the merged declaration file.

The cause was in the small Go filter that keeps only the engine's own declarations from clang's JSON dump. Clang
writes a location's "file" only when it changes from the previous location printed, so the filter has to work out
which file each top-level declaration belongs to. It took the first "file" key it found anywhere inside the
declaration. For a `namespace openswmm { ... }` block in PluginFactory.cpp, the first "file" inside was the spelling
location of a macro defined in a header, deep in one of the functions. The whole block, 35 function bodies, was
filed under the header and then dropped as a duplicate. The fix reads the declaration's own "loc" (the expansion
location for a macro), and when that has no "file", the declaration is in the file named last.

The bug was invisible to every check built from the translator's own output: the census counted what it was given,
the emitter emitted what the census found, and the compiler was happy because the declarations still existed
(the header supplied them, bodiless). What found it was outside the pipeline altogether: `nm --defined-only` over the
object files of the reference build lists every function the real compiler emitted for each source file. Matching
those (demangled, constructors folded, variables and template instantiations set aside) against the declarations
that have bodies gave 37 missing, 35 of them in PluginFactory.cpp. After the fix, re-dumping three files closed it.

The general rule: when a pipeline filters its input, check its output against an independent inventory of the
input, made by a different tool. Your own counts only tell you the pipeline agrees with itself.
