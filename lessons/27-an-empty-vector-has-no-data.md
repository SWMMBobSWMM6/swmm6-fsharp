libstdc++'s `vector::data()` is the start of the vector's storage, and a vector that has never allocated has none: data() is nullptr. Code that tests the pointer for null depends on it.

In an FV run the dynamic wave solver's node arrays are never sized, and the structure code is handed their data():

```cpp
        double* surf_buf = dw.nodeNewSurfAreaDataMut();           // SWMMEngine.cpp: xnode_.new_surf_area.data()
        ...
void StructureSolver::scatterOrificeSurfArea(SimulationContext& ctx, double* node_new_surf_area, ...) {
    if (node_new_surf_area == nullptr)
        return;
```

Every runtime made data() a pointer to the storage array whatever its length; cs/runtime/Std.cs had

```csharp
        public Ptr<T> data() => new Ptr<T>(a, 0);
```

where `a` is `Array.Empty<T>()` for a new vector. The pointer was not null, the guard did not fire, and
valve_closure.inp (an orifice under FV with structure coupling) died with IndexOutOfRangeException in
scatterOrificeSurfArea. The C++ skips the scatter.

The fix gives data() libstdc++'s rule, null when the capacity is 0: cs/runtime/Std.cs `Vec<T>.data`;
java/tools/gen_core.py (the template of java/src/cpp/Vec*.java, regenerated: only the nine data() methods changed);
go/cpp/core.go `(*Vec[T]).Data` (a Go vector made by NewVec already had a nil slice, one made with a count of 0 did
not); f90/runtime/gen_rt_a.py (the template of cpp_rt_a.f90, regenerated: only the five cpp_vec_*_data functions
changed); py/cpp/core_vec.py `_VecBase.data`; ts/src/cpp/a_std.ts `Vec.data`.

The first 480-model gate after that change found its other half. HotStartManager writes vectors with
`os.write(reinterpret_cast<const char*>(v.data()), n * sizeof(double))`, and for an empty vector that is
`write(nullptr, 0)`, which libstdc++ (like glibc's memcpy with a length of 0) never dereferences. The C# helper did:

```csharp
        static Span<byte> Bytes<T>(Ptr<T> p, int nbytes) where T : struct =>
            MemoryMarshal.AsBytes(new Span<T>(p.a, (int)p.o, p.a.Length - (int)p.o)).Slice(0, nbytes);
```

and extran8a.inp and extran8a_slot.inp died with NullReferenceException in save_legacy_routing. So every runtime
helper that takes a pointer and a length now accepts a null pointer with a length of 0, and the char* view of a null
pointer is null: cs/runtime/Misc.cs `Mem.Bytes`, `memcpy(CPtr, CPtr)`, `fwrite(CPtr)`, `Bytes.View`, Streams.cs
`OStream.write(CPtr)`, CRuntime.cs `fwriteC`, Str.cs `Str(byte[], int, int)`, `assign(CPtr, ulong)`,
`append(CPtr, ulong)`; java/src/cpp/Mem.java `read`, `write`, `memcpy_CPtr_CPtr_ul`, `fwrite_CPtr_ul_ul_CFile`,
PodView.java (every `of` and `into`), Bytes.java (every `View_PtrT__*`), OStream.java `write_CPtr_l`, Str.java the
three matching methods; f90/runtime/cpp_rt_d.f90 the five `cpp_ostream_write_ptrt_l__*` and five
`cpp_istream_read_ptrt_l__*` (they raised the NullReferenceException before looking at the length) and
cpp_rt_c_ptr.f90 the seven `cpp_bytes_view_ptrt__*`; py/cpp/crtmisc.py `_elem_bytes`, `_cp_bytes`, `_cp_write`,
`Mem_memcpy_CPtr_CPtr_ul`, `Mem_fwrite_CPtr_ul_ul_CFile`, `Bytes_View_PtrT__*`, streams.py `pod_of`, `pod_into`,
`OStream.write_CPtr_l`, core_str.py the matching Str methods; ts/src/cpp/Misc.ts `readBytes`, `writeBytes`, `cBytes`,
`cWrite`, `copyFromC`, `memset_CPtr_i_ul`, `viewOf`, `fwrite_CPtr_ul_ul_CFile`, streams.ts `OStream.write_CPtr_l`,
CRuntime.ts `fwriteC`, a_str.ts `append_CPtr_ul`. Go needed nothing: its byte helpers return at once for a length
of 0 and a nil slice is safe to slice.

The ports track capacity close to libstdc++ but not exactly (the C# copy-assignment allocates exactly the source's
size, libstdc++ keeps a larger capacity), so a vector assigned from an empty one has a null data() in the ports and
not in the C++. No code the decks reach compares such a pointer with null.

The rule: a null pointer and a pointer to nothing are different values in C++, and an emulation must keep them
apart wherever the source compares with nullptr; and once it does, every consumer of a (pointer, length) pair must
accept (nullptr, 0), because C++ code passes it all the time.
