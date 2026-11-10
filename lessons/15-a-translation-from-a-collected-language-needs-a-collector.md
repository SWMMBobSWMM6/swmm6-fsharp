The Fortran engine passed 454 of 480 models on its first full gate run and was killed by the kernel on the other 26, all long runs: C# frees memory by garbage collection, and a translation that never frees needs a collector of its own.

Every one of the 26 ended with exit status -9 (SIGKILL, the out-of-memory killer) after 13 to 60 seconds; the machine
has 8 GB and no swap. On Hydrology/rtk_long.inp the engine held 1.4 GB two seconds into the run. A heap profile
(valgrind --tool=massif, stopped at 40 s) showed no single culprit: new vectors from `cpp_vec_d_new_0` in 1,974
places, string copies (`cpp_str_clone`) in 204, cross-section parameter objects built for every outfall at every
step (`p_outfall__f__buildxsectparams_outfall`), boxes of `Ptr<double>` made per step, a new buffer for every string
literal evaluated (`cpp_r_lit`). The C++ frees each of these at the end of a statement or scope; the C# leaves them to
the collector; the translator, which sees only the C#, cannot tell which reference is the last.

The fix is the one the C# relies on: a collector. Ubuntu ships the Boehm-Demers-Weiser conservative collector as a
system library (libgc1). f90/gc/gc_wrap.c routes malloc, calloc, realloc and free to it, and f90/build.sh links with
`-Wl,--wrap=malloc,--wrap=calloc,--wrap=realloc,--wrap=free` and `-static-libgfortran`, so that gfortran's
ALLOCATE, the runtime's and libgfortran's own allocations all come from the collected heap; a pointer that libc
itself allocated (realpath, getline) still goes to the real free. A collector frees only memory nothing can reach,
so it cannot change a result. rtk_long then ran in 10 s at 92 MB (C# 9.4 s) with the .out and .rpt byte-identical.
