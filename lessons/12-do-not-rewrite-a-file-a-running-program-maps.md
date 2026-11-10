Rewriting a shared library in place kills every running program that has it mapped; copy only when it changed.

Each translator's build script copied the Roslyn assemblies into the shared tools/cs2x/bin with a plain
`cp $RB/Microsoft.CodeAnalysis.dll $RB/Microsoft.CodeAnalysis.CSharp.dll bin/`. With several translators being
built and run at the same time (Go, Java, TypeScript, Python, Fortran, by separate agents), a rebuild of one
rewrote the DLLs while another translator was running from them, and that one died with SIGBUS (exit 135):
the .NET runtime maps assemblies into memory, and `cp` truncates and rewrites the same inode instead of
replacing it. The TypeScript agent hit it twice and reported it.

Every build script now copies a DLL only when it differs (`cmp -s $RB/$f bin/$f || cp $RB/$f bin/$f`). Writing
to a new file and renaming it over the old one would also be safe, since a running program keeps the old inode.
