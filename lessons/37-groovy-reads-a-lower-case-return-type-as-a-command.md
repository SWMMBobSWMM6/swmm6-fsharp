Groovy 3 cannot parse a method declaration whose return type starts with a lower-case letter or an underscore: it reads `io_X m() { }` as the command `io_X(m(...))`.

The engine's C++ namespaces become prefixes of its class names (`openswmm::io::CheckpointOutputPublication::Output`
is `io_CheckpointOutputPublication_Output`), and 473 of the 1,258 classes start with a lower-case letter or `_`.
Java and Kotlin take them as they are. The first Groovy build stopped at the first method that returns one:

    gen/engine/DefaultOutputPlugin.groovy: 91: Unexpected input: '(' @ line 91, column 68.
       	public io_CheckpointOutputPublication_Output checkpointPublication(DefaultOutputPlugin_PreparedCheckpoint prepared) {

Tested one declaration at a time, Groovy 3.0.24 rejects `public io_X m()`, `public static io_X m()`,
`public io_X[] m()`, `public final io_X m()` and even `public engine.io_X m()`, and accepts `public Io_X m()`,
`public foo.Bar m()`, `public java.util.List<io_X> m()`, and `io_X` as the type of a field, parameter or local. The
grammar tells a declaration from a command expression by the case of the type name's first letter.

Cs2Groovy's `TypeIdent` now writes such a class as `L` + its name (`Lio_CheckpointOutputPublication_Output`,
`L_anon_core_...`): all 473, renamed everywhere they are named, with no name clash (the translator reports two
types of one name, and reported none).

Written by Claude (Anthropic) for Robert Dickinson's SWMM6 ports.
