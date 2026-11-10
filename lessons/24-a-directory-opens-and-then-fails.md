On Linux a directory opens for reading like a file; it is the first read that fails. libstdc++'s ifstream and glibc's fopen both succeed, and the ports' runtimes, modelled on .NET's FileStream, refused at the open.

src/engine/input/InputReader.cpp opens the input this way:

```cpp
    std::ifstream ifs(openswmm::io::utf8_path(path));
    if (!ifs.is_open()) {
        ctx.error_code = 2; // public SWMM_ERR_INPFILE (was 1 = NOMEM)
        ctx.error_message = "InputReader: cannot open file '" + path + "'";
```

Given a directory named dir_as_deck.inp, the C++ opens it, its first getline fails (basic_filebuf::underflow
throws ios_base::failure on EISDIR, and getline catches it and sets badbit), the model is empty, and the run ends on
ERROR 145, exit status 5. Every port said "cannot open file" and exited with 2. HourlyMeteorology.cpp tells the two
cases apart in its messages:

```cpp
    std::ifstream stream(path);
    if (!stream)
        return "Cannot open hourly meteorology file: " + path;
    return read(stream);
    ...
    if (stream.bad())
        return "Could not read hourly meteorology.";
```

and ppr_ueb_dir.inp, whose MET_FILE is `../decks`, expects the second.

The refusals, as they were: C# `new FileStream(path, ...)` throws on a directory (cs/runtime/Streams.cs
`IFStream.open`); java/src/cpp/FsNet.java `open` has `if (Files.isDirectory(p)) throw new IOException("Is a
directory");`; go/cpp/streams.go `stOpen` has `if st, err := f.Stat(); err != nil || st.IsDir() { f.Close(); return
nil, os.ErrPermission }`; f90/runtime/cpp_rt_d.f90 `d_open_fd` closes a directory's descriptor; and the Fortran
fopen read a byte to refuse one:

```fortran
    if (crt_ub(a2, 0_8) == 114) then          ! 'r'
      c = crt_c_fgetc(fp)
      if (c < 0 .and. crt_c_ferror(fp) /= 0) then
        c = crt_c_fclose(fp)
        return
```

The fix models the open and the failure. An ifstream on a directory is open and good; its first read sets badbit
and failbit and not eofbit (cs/runtime/Streams.cs `IStream.dir`, `Fill`, `AtEnd`, `IFStream.open`;
java/src/cpp/IStream.java and IFStream.java the same names; go/cpp/streams.go `stDir`, `stFill`, `stAtEnd`,
`Open_Str_i`; f90/runtime/cpp_rt_d.f90 `cpp_istream%dir`, `d_fill`, `d_atend`, `d_dir_path`,
`cpp_ifstream_open_str_i`; py/cpp/streams.py `IStream.dir`, `Fill`, `AtEnd`, `IFStream.open_Str_i`; ts/src/cpp/streams.ts the same names). fopen(dir, "r") returns a FILE whose reads fail with the error flag set and feof clear
(cs/runtime/CRuntime.cs `CFile.Dir`, `ReadFailed`, `fopen`, Fs.cs `ferror`; java/src/cpp/CFile.java, R.java `fopen`,
RFs.java `ferror`; go/cpp/file.go `dir`, `readFailed`, `Fopen`, crt2.go `R_ferror`; py/cpp/crt.py `CFile.Dir`, `ReadFailed`, `R_fopen`,
rfs.py `R_ferror`; ts/src/cpp/CRuntime.ts `CFile.Dir`, `ReadFailed`, `R.fopen`, `CFileStream.nul`, rfs.ts
`ferror`); the Fortran fopen simply keeps
glibc's result now, and its ferror reports glibc's flag.

The rule: emulate the C library's failure where the C library fails, not where the host language's library does;
an open that succeeds and a read that fails are two different messages in the engine.
