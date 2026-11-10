// Runtime.fs - the parts of the C++ standard library and the C library the generated SWMM6 engine calls, in F#: a
// translation by hand of the C# port's runtime (cs/runtime/*.cs, the runtime the C# engine passes 480 of 480 and 452
// of 452 with), class for class and member for member, so that the F# engine calls it exactly as the C# engine calls
// the C# runtime. Each section names the C# file it comes from; where F# needed a different shape (no ref locals in
// lambdas, no yield return, no struct property setters on immutable values), the comment says what changed. The math
// is in LibM.fs (glibc's own routines, by P/Invoke), libstdc++'s sorting in StdAlgo.fs, its prime table in
// PrimeList.fs.
//
// The F# engine does not reference the C# runtime's assembly or any C# project; nothing here calls another port.
// Written by Claude (Anthropic) for Robert Dickinson's SWMM6 ports; see AI-USE.md and fsharp/README.md.
namespace rec Cpp

#nowarn "9" "51" "52" "64" "1182" "3370" "3391" "3536" "3560" "864" "44" "57" "40"

open System
open System.Collections.Generic
open System.Globalization
open System.IO
open System.Numerics
open System.Runtime.CompilerServices
open System.Runtime.InteropServices
open System.Text

// =================================================================================================== Std.cs
/// Copy semantics for the engine's classes: Clone is the copy constructor, Assign the copy assignment. The emitter
/// generates both for every engine class.
[<AllowNullLiteral>]
type ICopy<'T> =
    abstract Clone : unit -> 'T
    abstract Assign : 'T -> unit

/// How to make, copy and assign elements of a container: plain values copy by assignment; classes are created with
/// their default constructor and copied with Clone.
[<Sealed>]
type Elem<'T> private () =
    static let isClass = typeof<ICopy<'T>>.IsAssignableFrom(typeof<'T>)
    static let isArr = typeof<'T>.IsArray
    static let needsCopy = isClass || typeof<'T>.IsArray
    static let mutable make : Func<'T> =
        if typeof<'T>.IsValueType then Func<'T>(fun () -> Unchecked.defaultof<'T>)
        else
            let ctor = typeof<'T>.GetConstructor(Type.EmptyTypes)
            if not (isNull ctor) then Func<'T>(fun () -> ctor.Invoke(null) :?> 'T)
            else Func<'T>(fun () -> Unchecked.defaultof<'T>)
    static member IsClass : bool = isClass
    static member Make with get () : Func<'T> = make and set (v: Func<'T>) = make <- v
    /// Copying a T means more than copying the reference: a class, or an array (std::array, T[n]).
    static member NeedsCopy : bool = needsCopy
    [<MethodImpl(MethodImplOptions.AggressiveInlining)>]
    static member Copy (x: 'T) : 'T =
        if isClass && not (isNull (box x)) then (box x :?> ICopy<'T>).Clone()
        elif isArr && not (isNull (box x)) then ArrCopy.Deep(box x) :?> 'T
        else x

/// A C array member copied with its object (T a[n][m], or an array of classes): every row and element.
[<AbstractClass; Sealed>]
type ArrCopy =
    [<DefaultValue>] static val mutable private elemCopy : System.Collections.Concurrent.ConcurrentDictionary<Type, Func<obj, obj>>
    static member Deep (x: obj) : obj =
        if isNull ArrCopy.elemCopy then ArrCopy.elemCopy <- System.Collections.Concurrent.ConcurrentDictionary<Type, Func<obj, obj>>()
        let a = x :?> Array
        let b = a.Clone() :?> Array
        let et = a.GetType().GetElementType()
        if et.IsArray then
            for i in 0 .. a.Length - 1 do
                let v = a.GetValue(i)
                if not (isNull v) then b.SetValue(ArrCopy.Deep(v), i)
        elif not et.IsValueType || typedefof<ICopy<_>>.MakeGenericType(et).IsAssignableFrom(et) then
            let f = ArrCopy.elemCopy.GetOrAdd(et, fun (t: Type) ->
                        let m = typedefof<Elem<_>>.MakeGenericType(t).GetMethod("Copy")
                        Func<obj, obj>(fun o -> m.Invoke(null, [| o |])))
            for i in 0 .. a.Length - 1 do
                let v = a.GetValue(i)
                if not (isNull v) then b.SetValue(f.Invoke(v), i)
        box b

/// std::vector<T>: contiguous storage with a ref indexer, so v[i] = x and double& r = v[i] work as in C++. Element
/// copies follow C++: copying the vector or inserting a named object copies it.
[<AllowNullLiteral>]
type Vec<'T> =
    [<DefaultValue(false)>] val mutable a : 'T[]
    [<DefaultValue(false)>] val mutable n : int
    /// How to make a new element when T alone cannot say: a std::array or C array element (vector<array<double, 3>>
    /// holds double[], and the 3 is known only to the generated code).
    [<DefaultValue(false)>] val mutable mk : Func<'T>

    new () as this = { } then this.a <- Array.Empty<'T>()
    new (mk: Func<'T>) as this = { } then this.a <- Array.Empty<'T>(); this.mk <- mk
    new (count: uint64, mk: Func<'T>) as this = { } then this.mk <- mk; this.a <- Array.zeroCreate (int count); this.n <- int count; this.Fill(0, this.n)
    new (count: uint64) as this = { } then this.a <- Array.zeroCreate (int count); this.n <- int count; this.Fill(0, this.n)
    new (count: uint64, v: 'T) as this =
        { }
        then
            this.a <- Array.zeroCreate (int count)
            this.n <- int count
            for i in 0 .. this.n - 1 do this.a.[i] <- Elem<'T>.Copy(v)
    new (init: 'T[]) as this =
        { }
        then
            this.a <- Array.zeroCreate init.Length
            this.n <- init.Length
            for i in 0 .. this.n - 1 do this.a.[i] <- Elem<'T>.Copy(init.[i])
    static member Adopt (arr: 'T[]) : Vec<'T> = let v = Vec<'T>() in v.a <- arr; v.n <- arr.Length; v

    /// ~vector: each element in order, first to last (libstdc++ _Destroy).
    member this.DestroyElems () = for i in 0 .. this.n - 1 do Dtor.Run(box this.a.[i])

    member private this.Fill (from: int, upto: int) =
        if not (isNull this.mk) then
            for i in from .. upto - 1 do this.a.[i] <- this.mk.Invoke()
        elif Elem<'T>.IsClass || not typeof<'T>.IsValueType then
            for i in from .. upto - 1 do this.a.[i] <- Elem<'T>.Make.Invoke()

    member this.Item with get (i: uint64) : byref<'T> = &this.a.[int i]
    member this.Item with get (i: int64) : byref<'T> = &this.a.[int i]
    member this.Item with get (i: int) : byref<'T> = &this.a.[i]

    member this.size () : uint64 = uint64 this.n
    member this.empty () : bool = this.n = 0
    member this.capacity () : uint64 = uint64 this.a.Length
    member this.at (i: uint64) : byref<'T> =
        if i >= uint64 this.n then raise (OutOfRange("vector::_M_range_check: __n (which is " + string i + ") >= this->size() (which is " + string this.n + ")"))
        &this.a.[int i]
    member this.front () : byref<'T> = &this.a.[0]
    member this.back () : byref<'T> = &this.a.[this.n - 1]
    /// libstdc++'s data() is the start of the storage, which is nullptr until the vector first allocates (capacity
    /// 0): DynamicWave's nodeNewSurfAreaDataMut() is null in an FV run, and the structure code tests it.
    member this.data () : Ptr<'T> = if this.a.Length = 0 then Unchecked.defaultof<Ptr<'T>> else Ptr<'T>(this.a, 0L)

    member private this.Grow (need: int) =
        if need > this.a.Length then
            // libstdc++: new capacity = size + max(size, 1) when push_back runs out
            let cap = Math.Max(need, this.n + Math.Max(this.n, 1))
            Array.Resize(&this.a, cap)

    member this.reserve (c: uint64) = if int c > this.a.Length then Array.Resize(&this.a, int c)
    member this.push_back (v: 'T) = this.Grow(this.n + 1); this.a.[this.n] <- v; this.n <- this.n + 1      // the emitter copies a named argument
    member this.emplace_back (v: 'T) : byref<'T> = this.Grow(this.n + 1); this.a.[this.n] <- v; this.n <- this.n + 1; &this.a.[this.n - 1]
    member this.pop_back () = this.n <- this.n - 1; this.a.[this.n] <- Unchecked.defaultof<'T>
    member this.clear () = Array.Clear(this.a, 0, this.n); this.n <- 0
    member this.resize (c: uint64) =
        let m = int c
        if m > this.n then this.Grow(m); this.Fill(this.n, m)
        else Array.Clear(this.a, m, this.n - m)
        this.n <- m
    member this.resize (c: uint64, v: 'T) =
        let m = int c
        if m > this.n then
            this.Grow(m)
            for i in this.n .. m - 1 do this.a.[i] <- Elem<'T>.Copy(v)
        else Array.Clear(this.a, m, this.n - m)
        this.n <- m
    member this.assign (c: uint64, v: 'T) =
        let m = int c
        if m > this.a.Length then this.a <- Array.zeroCreate m
        else Array.Clear(this.a, 0, this.a.Length)
        for i in 0 .. m - 1 do this.a.[i] <- Elem<'T>.Copy(v)
        this.n <- m
    member this.assign (b: Iter<'T>, e: Iter<'T>) =
        let m = int (e.i - b.i)
        let src = b.a
        let o = int b.i
        let na : 'T[] = Array.zeroCreate m
        for i in 0 .. m - 1 do na.[i] <- Elem<'T>.Copy(src.[o + i])
        this.a <- na
        this.n <- m
    member this.assign (il: 'T[]) =
        let na : 'T[] = Array.zeroCreate il.Length
        for i in 0 .. il.Length - 1 do na.[i] <- Elem<'T>.Copy(il.[i])
        this.a <- na
        this.n <- il.Length
    member this.shrink_to_fit () = if this.a.Length <> this.n then Array.Resize(&this.a, this.n)
    member this.swap (o: Vec<'T>) =
        let ta = this.a in this.a <- o.a; o.a <- ta
        let tn = this.n in this.n <- o.n; o.n <- tn

    member this.``begin`` () = Iter<'T>(this, 0L)
    member this.``end`` () = Iter<'T>(this, int64 this.n)
    member this.cbegin () = this.``begin`` ()
    member this.cend () = this.``end`` ()
    member this.rbegin () = RevIter<'T>(this, int64 this.n)
    member this.rend () = RevIter<'T>(this, 0L)

    member this.insert (pos: Iter<'T>, v: 'T) : Iter<'T> =
        let p = int pos.i
        this.Grow(this.n + 1)
        Array.Copy(this.a, p, this.a, p + 1, this.n - p)
        this.a.[p] <- v
        this.n <- this.n + 1
        Iter<'T>(this, int64 p)
    member this.insert (pos: Iter<'T>, b: Iter<'T>, e: Iter<'T>) : Iter<'T> =
        let p = int pos.i
        let m = int (e.i - b.i)
        let tmp : 'T[] = Array.zeroCreate m
        for i in 0 .. m - 1 do tmp.[i] <- Elem<'T>.Copy(b.a.[int b.i + i])
        this.Grow(this.n + m)
        Array.Copy(this.a, p, this.a, p + m, this.n - p)
        Array.Copy(tmp, 0, this.a, p, m)
        this.n <- this.n + m
        Iter<'T>(this, int64 p)
    /// insert(pos, {a, b, c}): the initializer list's elements, copied.
    member this.insert (pos: Iter<'T>, il: 'T[]) : Iter<'T> =
        let p = int pos.i
        let m = il.Length
        let tmp : 'T[] = Array.zeroCreate m
        for i in 0 .. m - 1 do tmp.[i] <- Elem<'T>.Copy(il.[i])
        this.Grow(this.n + m)
        Array.Copy(this.a, p, this.a, p + m, this.n - p)
        Array.Copy(tmp, 0, this.a, p, m)
        this.n <- this.n + m
        Iter<'T>(this, int64 p)
    member this.insert (pos: Iter<'T>, cnt: uint64, v: 'T) : Iter<'T> =
        let p = int pos.i
        let m = int cnt
        this.Grow(this.n + m)
        Array.Copy(this.a, p, this.a, p + m, this.n - p)
        for i in 0 .. m - 1 do this.a.[p + i] <- Elem<'T>.Copy(v)
        this.n <- this.n + m
        Iter<'T>(this, int64 p)
    member this.insert (pos: Iter<'T>, b: Ptr<'T>, e: Ptr<'T>) : Iter<'T> =
        let p = int pos.i
        let m = int (e.o - b.o)
        let tmp : 'T[] = Array.zeroCreate m
        for i in 0 .. m - 1 do tmp.[i] <- Elem<'T>.Copy(b.a.[int b.o + i])
        this.Grow(this.n + m)
        Array.Copy(this.a, p, this.a, p + m, this.n - p)
        Array.Copy(tmp, 0, this.a, p, m)
        this.n <- this.n + m
        Iter<'T>(this, int64 p)
    member this.erase (pos: Iter<'T>) : Iter<'T> =
        let p = int pos.i
        Array.Copy(this.a, p + 1, this.a, p, this.n - p - 1)
        this.n <- this.n - 1
        this.a.[this.n] <- Unchecked.defaultof<'T>
        Iter<'T>(this, int64 p)
    member this.erase (b: Iter<'T>, e: Iter<'T>) : Iter<'T> =
        let p = int b.i
        let q = int e.i
        Array.Copy(this.a, q, this.a, p, this.n - q)
        let m = q - p
        Array.Clear(this.a, this.n - m, m)
        this.n <- this.n - m
        Iter<'T>(this, int64 p)

    member this.Clone () : Vec<'T> =
        let v = Vec<'T>()
        v.mk <- this.mk
        v.a <- Array.zeroCreate this.n
        v.n <- this.n
        if Elem<'T>.NeedsCopy then
            for i in 0 .. this.n - 1 do v.a.[i] <- Elem<'T>.Copy(this.a.[i])
        else Array.Copy(this.a, v.a, this.n)
        v
    member this.Assign (o: Vec<'T>) =
        if not (obj.ReferenceEquals(this, o)) then
            let na : 'T[] = Array.zeroCreate o.n
            if Elem<'T>.NeedsCopy then
                for i in 0 .. o.n - 1 do na.[i] <- Elem<'T>.Copy(o.a.[i])
            else Array.Copy(o.a, na, o.n)
            this.a <- na
            this.n <- o.n
            if isNull this.mk then this.mk <- o.mk
    /// Move-assign: take o's storage and leave o empty, as libstdc++ does.
    member this.MoveFrom (o: Vec<'T>) =
        if not (obj.ReferenceEquals(this, o)) then
            this.a <- o.a
            this.n <- o.n
            if isNull this.mk then this.mk <- o.mk
            o.a <- Array.Empty<'T>()
            o.n <- 0
    member this.Assign (il: 'T[]) = this.assign(il)
    member this.MoveFrom (il: 'T[]) = this.assign(il)          // v = {a, b, c}

    static member Eq (x: Vec<'T>, y: Vec<'T>) : bool =
        if x.n <> y.n then false
        else
            let c = EqualityComparer<'T>.Default
            let mutable r = true
            let mutable i = 0
            while r && i < x.n do
                if not (c.Equals(x.a.[i], y.a.[i])) then r <- false
                i <- i + 1
            r

    interface ICopy<Vec<'T>> with
        member this.Clone () = this.Clone()
        member this.Assign (o) = this.Assign(o)
    interface IDestroyElems with
        member this.DestroyElems () = this.DestroyElems()

/// A vector iterator: the container and an index (contiguous, so it is also a pointer).
[<Struct; CustomEquality; NoComparison; IsReadOnly>]
type Iter<'T> =
    val v : Vec<'T>
    val a : 'T[]
    val i : int64
    new (v: Vec<'T>, i: int64) = { v = v; a = v.a; i = i }
    new (a: 'T[], i: int64) = { v = null; a = a; i = i }
    member this.Clone () : Iter<'T> = this      // a value: copying it is the copy
    member this.Deref with get () : byref<'T> = &this.a.[int this.i]
    member this.Item with get (k: int64) : byref<'T> = &this.a.[int (this.i + k)]
    static member op_Addition (x: Iter<'T>, k: int64) : Iter<'T> = if not (isNull x.v) then Iter<'T>(x.v, x.i + k) else Iter<'T>(x.a, x.i + k)
    static member op_Subtraction (x: Iter<'T>, k: int64) : Iter<'T> = Iter<'T>.op_Addition(x, -k)
    static member op_Subtraction (x: Iter<'T>, y: Iter<'T>) : int64 = x.i - y.i
    static member op_Increment (x: Iter<'T>) : Iter<'T> = Iter<'T>.op_Addition(x, 1L)
    static member op_Decrement (x: Iter<'T>) : Iter<'T> = Iter<'T>.op_Addition(x, -1L)
    static member op_Equality (x: Iter<'T>, y: Iter<'T>) = x.i = y.i
    static member op_Inequality (x: Iter<'T>, y: Iter<'T>) = x.i <> y.i
    static member op_LessThan (x: Iter<'T>, y: Iter<'T>) = x.i < y.i
    static member op_GreaterThan (x: Iter<'T>, y: Iter<'T>) = x.i > y.i
    static member op_LessThanOrEqual (x: Iter<'T>, y: Iter<'T>) = x.i <= y.i
    static member op_GreaterThanOrEqual (x: Iter<'T>, y: Iter<'T>) = x.i >= y.i
    override this.Equals (o: obj) = match o with :? Iter<'T> as x -> x.i = this.i | _ -> false
    override this.GetHashCode () = int this.i
    interface IEquatable<Iter<'T>> with
        member this.Equals (o: Iter<'T>) = this.i = o.i

[<Struct; CustomEquality; NoComparison; IsReadOnly>]
type RevIter<'T> =
    val v : Vec<'T>
    val i : int64      // one past the element it designates, as std::reverse_iterator
    new (v: Vec<'T>, i: int64) = { v = v; i = i }
    member this.Clone () : RevIter<'T> = this
    member this.Deref with get () : byref<'T> = &this.v.a.[int (this.i - 1L)]
    static member op_Increment (x: RevIter<'T>) : RevIter<'T> = RevIter<'T>(x.v, x.i - 1L)
    static member op_Equality (x: RevIter<'T>, y: RevIter<'T>) = x.i = y.i
    static member op_Inequality (x: RevIter<'T>, y: RevIter<'T>) = x.i <> y.i
    override this.Equals (o: obj) = match o with :? RevIter<'T> as x -> x.i = this.i | _ -> false
    override this.GetHashCode () = int this.i

/// A reference to one field of an object, for a pointer made by &obj.field.
type RefAt<'T> = delegate of obj -> byref<'T>

/// A raw pointer to T inside an array: the array and an offset. Null has a null array.
[<Struct; CustomEquality; NoComparison; IsReadOnly>]
type Ptr<'T> =
    val a : 'T[]
    val o : int64
    // &obj.field: the object and how to reach the field (a and o unused). .NET cannot point into an object, so the
    // pointer keeps the object and a getter of the field's reference; reads and writes go to the field itself.
    val ob : obj
    val at : RefAt<'T>
    new (a: 'T[], o: int64) = { a = a; o = o; ob = null; at = null }
    new (ob: obj, at: RefAt<'T>) = { a = null; o = 0L; ob = ob; at = at }
    private new (a: 'T[], o: int64, ob: obj, at: RefAt<'T>) = { a = a; o = o; ob = ob; at = at }
    member this.Clone () : Ptr<'T> = this
    member this.IsNull = isNull this.a && isNull this.at
    member this.Item
        with get (i: int64) : byref<'T> = if not (isNull this.at) then &this.at.Invoke(this.ob) else &this.a.[int (this.o + i)]
    member this.Item
        with get (i: uint64) : byref<'T> = if not (isNull this.at) then &this.at.Invoke(this.ob) else &this.a.[int (this.o + int64 i)]
    member this.Item
        with get (i: int) : byref<'T> = if not (isNull this.at) then &this.at.Invoke(this.ob) else &this.a.[int (this.o + int64 i)]
    member this.Deref with get () : byref<'T> = if not (isNull this.at) then &this.at.Invoke(this.ob) else &this.a.[int this.o]
    static member op_Addition (p: Ptr<'T>, k: int64) = Ptr<'T>(p.a, p.o + k, p.ob, p.at)
    static member op_Addition (p: Ptr<'T>, k: uint64) = Ptr<'T>(p.a, p.o + int64 k, p.ob, p.at)
    static member op_Subtraction (p: Ptr<'T>, k: int64) = Ptr<'T>(p.a, p.o - k, p.ob, p.at)
    static member op_Subtraction (p: Ptr<'T>, q: Ptr<'T>) : int64 = p.o - q.o
    static member op_Increment (p: Ptr<'T>) = Ptr<'T>(p.a, p.o + 1L, p.ob, p.at)
    static member op_Decrement (p: Ptr<'T>) = Ptr<'T>(p.a, p.o - 1L, p.ob, p.at)
    static member op_Equality (p: Ptr<'T>, q: Ptr<'T>) : bool =
        if not (isNull p.at) || not (isNull q.at) then
            not (isNull q.at) && not (isNull p.at) && obj.ReferenceEquals(p.ob, q.ob) && p.at.Method = q.at.Method && p.o = q.o
        else obj.ReferenceEquals(p.a, q.a) && (isNull p.a || p.o = q.o)
    static member op_Inequality (p: Ptr<'T>, q: Ptr<'T>) = not (Ptr<'T>.op_Equality(p, q))
    static member op_LessThan (p: Ptr<'T>, q: Ptr<'T>) = p.o < q.o
    static member op_GreaterThan (p: Ptr<'T>, q: Ptr<'T>) = p.o > q.o
    static member op_LessThanOrEqual (p: Ptr<'T>, q: Ptr<'T>) = p.o <= q.o
    static member op_GreaterThanOrEqual (p: Ptr<'T>, q: Ptr<'T>) = p.o >= q.o
    override this.Equals (x: obj) = match x with :? Ptr<'T> as q -> Ptr<'T>.op_Equality(this, q) | _ -> false
    override this.GetHashCode () = int this.o
    static member op_Implicit (it: Iter<'T>) : Ptr<'T> = Ptr<'T>(it.a, it.i)
    interface IEquatable<Ptr<'T>> with
        member this.Equals (q: Ptr<'T>) = Ptr<'T>.op_Equality(this, q)

/// std::pair: a mutable class so p.first = x works through references.
[<AllowNullLiteral>]
type Pair<'A, 'B> =
    [<DefaultValue(false)>] val mutable first : 'A
    [<DefaultValue(false)>] val mutable second : 'B
    new () as this = { } then this.first <- Elem<'A>.Make.Invoke(); this.second <- Elem<'B>.Make.Invoke()
    new (a: 'A, b: 'B) as this = { } then this.first <- a; this.second <- b
    /// std::pair's operator<: first, then second.
    member this.CompareTo (o: Pair<'A, 'B>) : int =
        if Less<'A>.Fn.Invoke(this.first, o.first) then -1
        elif Less<'A>.Fn.Invoke(o.first, this.first) then 1
        elif Less<'B>.Fn.Invoke(this.second, o.second) then -1
        elif Less<'B>.Fn.Invoke(o.second, this.second) then 1
        else 0
    static member op_LessThan (x: Pair<'A, 'B>, y: Pair<'A, 'B>) = x.CompareTo(y) < 0
    static member op_GreaterThan (x: Pair<'A, 'B>, y: Pair<'A, 'B>) = x.CompareTo(y) > 0
    static member op_LessThanOrEqual (x: Pair<'A, 'B>, y: Pair<'A, 'B>) = x.CompareTo(y) <= 0
    static member op_GreaterThanOrEqual (x: Pair<'A, 'B>, y: Pair<'A, 'B>) = x.CompareTo(y) >= 0
    member this.Clone () : Pair<'A, 'B> = Pair<'A, 'B>(Elem<'A>.Copy(this.first), Elem<'B>.Copy(this.second))
    member this.Assign (o: Pair<'A, 'B>) = this.first <- Elem<'A>.Copy(o.first); this.second <- Elem<'B>.Copy(o.second)
    static member Eq (x: Pair<'A, 'B>, y: Pair<'A, 'B>) : bool =
        EqualityComparer<'A>.Default.Equals(x.first, y.first) && EqualityComparer<'B>.Default.Equals(x.second, y.second)
    interface ICopy<Pair<'A, 'B>> with
        member this.Clone () = this.Clone()
        member this.Assign (o) = this.Assign(o)
    interface IComparable<Pair<'A, 'B>> with
        member this.CompareTo (o) = this.CompareTo(o)

/// std::optional<T>.
[<AllowNullLiteral>]
type Opt<'T> =
    [<DefaultValue(false)>] val mutable has : bool
    [<DefaultValue(false)>] val mutable ``val`` : 'T
    new () = { }
    new (v: 'T) as this = { } then this.has <- true; this.``val`` <- v
    member this.DestroyElems () = if this.has then Dtor.Run(box this.``val``)
    member this.has_value () = this.has
    member this.value () : byref<'T> = (if not this.has then raise (BadOptionalAccess())); &this.``val``
    member this.Deref with get () : byref<'T> = &this.``val``
    member this.value_or (d: 'T) : 'T = if this.has then this.``val`` else d
    member this.reset () = this.has <- false; this.``val`` <- Unchecked.defaultof<'T>
    member this.MoveFrom (o: Opt<'T>) = if not (obj.ReferenceEquals(this, o)) then this.has <- o.has; this.``val`` <- o.``val``
    member this.emplace (v: 'T) : byref<'T> = this.has <- true; this.``val`` <- v; &this.``val``
    member this.Clone () : Opt<'T> = if this.has then Opt<'T>(Elem<'T>.Copy(this.``val``)) else Opt<'T>()
    member this.Assign (o: Opt<'T>) = this.has <- o.has; this.``val`` <- (if o.has then Elem<'T>.Copy(o.``val``) else Unchecked.defaultof<'T>)
    member this.Set (v: 'T) = this.has <- true; this.``val`` <- v
    interface ICopy<Opt<'T>> with
        member this.Clone () = this.Clone()
        member this.Assign (o) = this.Assign(o)
    interface IDestroyElems with
        member this.DestroyElems () = this.DestroyElems()

/// std::array, std::fill, std::copy and the small helpers on plain arrays.
[<AbstractClass; Sealed>]
type Arr =
    /// T a[n][m]: n rows of m (each a fresh array).
    static member Make2<'T> (n: int, m: int) : 'T[][] =
        let a : 'T[][] = Array.zeroCreate n
        for i in 0 .. n - 1 do a.[i] <- Arr.Make<'T>(m)
        a
    /// std::array<std::array<T, m>, n> and the like: n elements, each made by mk.
    static member MakeWith<'T> (n: int, mk: Func<'T>) : 'T[] =
        let a : 'T[] = Array.zeroCreate n
        for i in 0 .. n - 1 do a.[i] <- mk.Invoke()
        a
    static member Make<'T> (n: int) : 'T[] =
        let a : 'T[] = Array.zeroCreate n
        if Elem<'T>.IsClass || not typeof<'T>.IsValueType then
            for i in 0 .. n - 1 do a.[i] <- Elem<'T>.Make.Invoke()
        a
    static member Clone<'T> (a: 'T[]) : 'T[] =
        if isNull a then null
        else
            let b : 'T[] = Array.zeroCreate a.Length
            if Elem<'T>.IsClass then
                for i in 0 .. a.Length - 1 do b.[i] <- Elem<'T>.Copy(a.[i])
            else Array.Copy(a, b, a.Length)
            b
    static member Assign<'T> (dst: 'T[], src: 'T[]) =
        if Elem<'T>.IsClass then
            for i in 0 .. src.Length - 1 do (box dst.[i] :?> ICopy<'T>).Assign(src.[i])
        else Array.Copy(src, dst, src.Length)
    static member Fill<'T> (a: 'T[], b: int64, e: int64, v: 'T) =
        let mutable i = b
        while i < e do
            a.[int i] <- Elem<'T>.Copy(v)
            i <- i + 1L
    static member Eq<'T> (x: 'T[], y: 'T[]) : bool =
        if x.Length <> y.Length then false
        else
            let c = EqualityComparer<'T>.Default
            let mutable r = true
            let mutable i = 0
            while r && i < x.Length do
                if not (c.Equals(x.[i], y.[i])) then r <- false
                i <- i + 1
            r

/// std::min / std::max / std::clamp exactly as libstdc++ writes them (the comparisons decide which argument comes
/// back, which matters for -0.0 and NaN).
[<AbstractClass; Sealed>]
type M =
    [<MethodImpl(MethodImplOptions.AggressiveInlining)>] static member min (a: float, b: float) : float = if b < a then b else a
    [<MethodImpl(MethodImplOptions.AggressiveInlining)>] static member max (a: float, b: float) : float = if a < b then b else a
    [<MethodImpl(MethodImplOptions.AggressiveInlining)>] static member min (a: float32, b: float32) : float32 = if b < a then b else a
    [<MethodImpl(MethodImplOptions.AggressiveInlining)>] static member max (a: float32, b: float32) : float32 = if a < b then b else a
    [<MethodImpl(MethodImplOptions.AggressiveInlining)>] static member min (a: int, b: int) : int = if b < a then b else a
    [<MethodImpl(MethodImplOptions.AggressiveInlining)>] static member max (a: int, b: int) : int = if a < b then b else a
    [<MethodImpl(MethodImplOptions.AggressiveInlining)>] static member min (a: int64, b: int64) : int64 = if b < a then b else a
    [<MethodImpl(MethodImplOptions.AggressiveInlining)>] static member max (a: int64, b: int64) : int64 = if a < b then b else a
    [<MethodImpl(MethodImplOptions.AggressiveInlining)>] static member min (a: uint64, b: uint64) : uint64 = if b < a then b else a
    [<MethodImpl(MethodImplOptions.AggressiveInlining)>] static member max (a: uint64, b: uint64) : uint64 = if a < b then b else a
    [<MethodImpl(MethodImplOptions.AggressiveInlining)>] static member min (a: uint32, b: uint32) : uint32 = if b < a then b else a
    [<MethodImpl(MethodImplOptions.AggressiveInlining)>] static member max (a: uint32, b: uint32) : uint32 = if a < b then b else a
    static member min (a: byte, b: byte) : byte = if b < a then b else a
    static member max (a: byte, b: byte) : byte = if a < b then b else a
    static member min (a: sbyte, b: sbyte) : sbyte = if b < a then b else a
    static member max (a: sbyte, b: sbyte) : sbyte = if a < b then b else a
    static member min (a: int16, b: int16) : int16 = if b < a then b else a
    static member max (a: int16, b: int16) : int16 = if a < b then b else a
    static member min (a: uint16, b: uint16) : uint16 = if b < a then b else a
    static member max (a: uint16, b: uint16) : uint16 = if a < b then b else a
    static member min<'T> (a: 'T, b: 'T, lt: Func<'T, 'T, bool>) : 'T = if lt.Invoke(b, a) then b else a
    static member max<'T> (a: 'T, b: 'T, lt: Func<'T, 'T, bool>) : 'T = if lt.Invoke(a, b) then b else a
    // std::clamp(v, lo, hi): (v < lo) ? lo : (hi < v) ? hi : v
    static member clamp (v: float, lo: float, hi: float) : float = if v < lo then lo elif hi < v then hi else v
    static member clamp (v: int, lo: int, hi: int) : int = if v < lo then lo elif hi < v then hi else v
    static member clamp (v: int64, lo: int64, hi: int64) : int64 = if v < lo then lo elif hi < v then hi else v
    // initializer-list forms: the first smallest / first largest
    static member min (il: float[]) : float = let mutable r = il.[0] in (for i in 1 .. il.Length - 1 do if il.[i] < r then r <- il.[i]); r
    static member max (il: float[]) : float = let mutable r = il.[0] in (for i in 1 .. il.Length - 1 do if r < il.[i] then r <- il.[i]); r
    static member min (il: int[]) : int = let mutable r = il.[0] in (for i in 1 .. il.Length - 1 do if il.[i] < r then r <- il.[i]); r
    static member max (il: int[]) : int = let mutable r = il.[0] in (for i in 1 .. il.Length - 1 do if r < il.[i] then r <- il.[i]); r

[<AllowNullLiteral>]
type CppException =
    inherit Exception
    val msg : Str
    new () = { inherit Exception("std::exception"); msg = Str("std::exception") }
    new (m: Str) = { inherit Exception(m.ToString()); msg = m.Clone() }
    abstract what : unit -> CPtr
    default this.what () = this.msg.c_str()
[<AllowNullLiteral>]
type LogicError =
    inherit CppException
    new (m: Str) = { inherit CppException(m) }
    new (m: string) = { inherit CppException(Str(m)) }
[<AllowNullLiteral>]
type RuntimeError =
    inherit CppException
    new (m: Str) = { inherit CppException(m) }
    new (m: string) = { inherit CppException(Str(m)) }
[<AllowNullLiteral>]
type InvalidArgument =
    inherit LogicError
    new (m: Str) = { inherit LogicError(m) }
    new (m: string) = { inherit LogicError(m) }
[<AllowNullLiteral>]
type OutOfRange =
    inherit LogicError
    new (m: Str) = { inherit LogicError(m) }
    new (m: string) = { inherit LogicError(m) }
[<AllowNullLiteral>]
type LengthError =
    inherit LogicError
    new (m: Str) = { inherit LogicError(m) }
    new (m: string) = { inherit LogicError(m) }
[<AllowNullLiteral>]
type DomainError =
    inherit LogicError
    new (m: Str) = { inherit LogicError(m) }
    new (m: string) = { inherit LogicError(m) }
[<AllowNullLiteral>]
type RangeError =
    inherit RuntimeError
    new (m: Str) = { inherit RuntimeError(m) }
    new (m: string) = { inherit RuntimeError(m) }
[<AllowNullLiteral>]
type OverflowError =
    inherit RuntimeError
    new (m: Str) = { inherit RuntimeError(m) }
    new (m: string) = { inherit RuntimeError(m) }
[<AllowNullLiteral>]
type BadAlloc =
    inherit CppException
    new () = { inherit CppException(Str("std::bad_alloc")) }
[<AllowNullLiteral>]
type BadOptionalAccess =
    inherit CppException
    new () = { inherit CppException(Str("bad optional access")) }
[<AllowNullLiteral>]
type BadFunctionCall =
    inherit CppException
    new () = { inherit CppException(Str("bad_function_call")) }

/// std::variant<bool, int, double, std::string> (the engine's user-flag value): the alternative held, as a boxed
/// value, and its index in the declared list.
[<AllowNullLiteral>]
type Variant =
    [<DefaultValue(false)>] val mutable v : obj
    [<DefaultValue(false)>] val mutable idx : int
    new () as this = { } then this.v <- box false
    new (x: bool) as this = { } then this.Set(x)
    new (x: int) as this = { } then this.Set(x)
    new (x: float) as this = { } then this.Set(x)
    new (x: Str) as this = { } then this.Set(x)
    member this.Set (x: bool) = this.v <- box x; this.idx <- 0
    member this.Set (x: int) = this.v <- box x; this.idx <- 1
    member this.Set (x: float) = this.v <- box x; this.idx <- 2
    member this.Set (x: Str) = this.v <- box (x.Clone()); this.idx <- 3
    member this.index () : uint64 = uint64 this.idx
    member this.Holds<'T> () : bool = this.v :? 'T
    member this.Get<'T> () : 'T = match this.v with :? 'T as t -> t | _ -> raise (BadVariantAccess())
    member this.Clone () : Variant = let c = Variant() in c.Assign(this); c
    member this.Assign (o: Variant) = this.idx <- o.idx; this.v <- (match o.v with :? Str as s -> box (s.Clone()) | x -> x)
    member this.MoveFrom (o: Variant) = this.idx <- o.idx; this.v <- o.v
    interface ICopy<Variant> with
        member this.Clone () = this.Clone()
        member this.Assign (o) = this.Assign(o)
[<AllowNullLiteral>]
type BadVariantAccess =
    inherit CppException
    new () = { inherit CppException(Str("std::get: wrong index for variant")) }

/// std::span<T>: a view of n elements of an array starting at o.
[<Struct; NoEquality; NoComparison>]
type CSpan<'T> =
    val mutable a : 'T[]
    val mutable o : int64
    val mutable n : int64
    new (a: 'T[], o: int64, n: int64) = { a = a; o = o; n = n }
    member this.Item with get (i: int64) : byref<'T> = &this.a.[int (this.o + i)]
    member this.size () : uint64 = uint64 this.n
    member this.empty () : bool = this.n = 0L
    member this.data () : Ptr<'T> = Ptr<'T>((if isNull this.a then Array.Empty<'T>() else this.a), this.o)
    member this.front () : byref<'T> = &this.a.[int this.o]
    member this.back () : byref<'T> = &this.a.[int (this.o + this.n - 1L)]
    member this.subspan (off: uint64, cnt: uint64) : CSpan<'T> = CSpan<'T>(this.a, this.o + int64 off, (if cnt = UInt64.MaxValue then this.n - int64 off else int64 cnt))
    member this.subspan (off: uint64) : CSpan<'T> = this.subspan(off, UInt64.MaxValue)
    member this.first (cnt: uint64) : CSpan<'T> = CSpan<'T>(this.a, this.o, int64 cnt)
    member this.last (cnt: uint64) : CSpan<'T> = CSpan<'T>(this.a, this.o + this.n - int64 cnt, int64 cnt)
    member this.Clone () : CSpan<'T> = this

// =================================================================================================== Containers.cs
// std::unordered_map / unordered_set with libstdc++'s exact node order (GCC 13 bits/hashtable.h: one singly linked
// list, a bucket array pointing at the node before each bucket's first node, new nodes at the front of their bucket,
// the prime rehash policy with growth factor 2), std::hash as libstdc++ computes it (identity for integers,
// _Hash_bytes for strings), std::map / std::set (sorted), std::stack, and their iterators. The engine iterates hashed
// containers, so their order reaches its output; tests/stdcheck compares this order with libstdc++'s.
[<AbstractClass; Sealed>]
type StdHash =
    static member private Mul = (0xc6a4a793UL <<< 32) + 0x5bd1e995UL
    static member private ShiftMix (v: uint64) = v ^^^ (v >>> 47)

    /// libstdc++'s std::_Hash_bytes (libsupc++/hash_bytes.cc, the 64-bit branch).
    static member HashBytes (b: byte[], off: int, len: int, seed: uint64) : uint64 =
        let mul = StdHash.Mul
        let aligned = len &&& ~~~7
        let mutable hash = seed ^^^ (uint64 len * mul)
        let mutable p = off
        while p < off + aligned do
            let data = StdHash.ShiftMix(BitConverter.ToUInt64(b, p) * mul) * mul
            hash <- hash ^^^ data
            hash <- hash * mul
            p <- p + 8
        let rest = len &&& 7
        if rest <> 0 then
            let mutable data = 0UL
            let mutable n = rest - 1
            while n >= 0 do
                data <- (data <<< 8) + uint64 b.[p + n]
                n <- n - 1
            hash <- hash ^^^ data
            hash <- hash * mul
        hash <- StdHash.ShiftMix(hash) * mul
        hash <- StdHash.ShiftMix(hash)
        hash

    static member Of (s: Str) : uint64 = StdHash.HashBytes(s.b, 0, s.n, 0xc70f6907UL)
    static member Of (v: int) : uint64 = uint64 (int64 v)
    static member Of (v: int64) : uint64 = uint64 v
    static member Of (v: uint32) : uint64 = uint64 v
    static member Of (v: uint64) : uint64 = v

    static member For<'K> () : Func<'K, uint64> =
        let t = typeof<'K>
        if t = typeof<Str> then Func<'K, uint64>(fun k -> StdHash.Of(box k :?> Str))
        elif t = typeof<int> then Func<'K, uint64>(fun k -> StdHash.Of(unbox<int> (box k)))
        elif t = typeof<int64> then Func<'K, uint64>(fun k -> StdHash.Of(unbox<int64> (box k)))
        elif t = typeof<uint32> then Func<'K, uint64>(fun k -> uint64 (unbox<uint32> (box k)))
        elif t = typeof<uint64> then Func<'K, uint64>(fun k -> unbox<uint64> (box k))
        elif t = typeof<int16> then Func<'K, uint64>(fun k -> uint64 (int64 (unbox<int16> (box k))))
        elif t = typeof<sbyte> then Func<'K, uint64>(fun k -> uint64 (int64 (unbox<sbyte> (box k))))
        elif t.IsEnum then Func<'K, uint64>(fun k -> uint64 (Convert.ToInt64(box k)))
        elif t = typeof<float> then
            Func<'K, uint64>(fun k ->
                let d = unbox<float> (box k)
                if d = 0.0 then 0UL else StdHash.HashBytes(BitConverter.GetBytes(d), 0, 8, 0xc70f6907UL))
        // object identity: libstdc++ hashes the address, which no other program can reproduce
        else Func<'K, uint64>(fun k -> uint64 (RuntimeHelpers.GetHashCode(box k)))

[<Sealed; AllowNullLiteral>]
type Hash<'T>() =
    static member Of (x: 'T) : uint64 = StdHash.For<'T>().Invoke(x)
    member this.op_Call (x: 'T) : uint64 = Hash<'T>.Of(x)
[<Sealed; AllowNullLiteral>]
type EqualTo<'T>() =
    member this.op_Call (a: 'T, b: 'T) : bool = EqualityComparer<'T>.Default.Equals(a, b)
[<Sealed; AllowNullLiteral>]
type Less<'T>() =
    static let fn : Func<'T, 'T, bool> =
        if typeof<'T> = typeof<Str> then Func<'T, 'T, bool>(fun a b -> (box a :?> Str).compare(box b :?> Str) < 0)
        else
            let c = Comparer<'T>.Default
            Func<'T, 'T, bool>(fun a b -> c.Compare(a, b) < 0)
    static member Fn : Func<'T, 'T, bool> = fn
    member this.op_Call (a: 'T, b: 'T) : bool = fn.Invoke(a, b)

/// The prime rehash policy (libstdc++ src/c++11/hashtable_c++0x.cc and hashtable-aux.cc).
[<Sealed; AllowNullLiteral>]
type RehashPolicy() =
    static let fastBkt : byte[] = [| 2uy; 2uy; 2uy; 3uy; 5uy; 5uy; 7uy; 7uy; 11uy; 11uy; 11uy; 11uy; 13uy; 13uy |]
    member val MaxLoad = 1.0 with get, set
    member val NextResize = 0UL with get, set

    member this.NextBkt (n: uint64) : uint64 =
        if n < uint64 fastBkt.Length then
            if n = 0UL then 1UL
            else
                this.NextResize <- uint64 (Math.Floor(float fastBkt.[int n] * this.MaxLoad))
                uint64 fastBkt.[int n]
        else
            let primes = PrimeList.P
            let last = primes.Length - 1          // __prime_list + __n_primes - 1 (the sentinel is excluded)
            let mutable lo = 6
            let mutable hi = last
            while lo < hi do
                let mid = (lo + hi) / 2
                if primes.[mid] < n then lo <- mid + 1 else hi <- mid
            if lo = last then this.NextResize <- UInt64.MaxValue
            else this.NextResize <- uint64 (Math.Floor(float primes.[lo] * this.MaxLoad))
            primes.[lo]

    member this.BktForElements (n: uint64) : uint64 = uint64 (Math.Ceiling(float n / this.MaxLoad))

    member this.NeedRehash (nBkt: uint64, nElt: uint64, nIns: uint64) : struct (bool * uint64) =
        if nElt + nIns > this.NextResize then
            let minBkts = float (Math.Max(nElt + nIns, (if this.NextResize <> 0UL then 0UL else 11UL))) / this.MaxLoad
            if minBkts >= float nBkt then
                struct (true, this.NextBkt(Math.Max(uint64 (Math.Floor(minBkts)) + 1UL, nBkt * 2UL)))
            else
                this.NextResize <- uint64 (Math.Floor(float nBkt * this.MaxLoad))
                struct (false, 0UL)
        else struct (false, 0UL)

/// A hash table node; the before-begin sentinel is a node too.
[<Sealed; AllowNullLiteral>]
type HNode<'E>() =
    [<DefaultValue(false)>] val mutable next : HNode<'E>
    [<DefaultValue(false)>] val mutable ``val`` : 'E
    [<DefaultValue(false)>] val mutable code : uint64

/// A key stored in a map or set is the container's own copy, as in C++, when the key is a library value (a string, a
/// vector, a pair). An engine class as key is a pointer in C++ (std::set<const Column*>), so it is stored as it is:
/// the same object, compared by identity.
[<Sealed>]
type KeyCopy<'T> private () =
    static let copy = Elem<'T>.IsClass && typeof<'T>.Namespace = "Cpp"
    static member Of (x: 'T) : 'T = if copy then Elem<'T>.Copy(x) else x

/// libstdc++'s _Hashtable for unique keys. E is the stored element (Pair<K,V> or K).
[<AllowNullLiteral>]
type HashTable<'K, 'E> =
    val key : Func<'E, 'K>
    val hash : Func<'K, uint64>
    val eq : Func<'K, 'K, bool>
    [<DefaultValue(false)>] val mutable buckets : HNode<'E>[]
    val beforeBegin : HNode<'E>
    [<DefaultValue(false)>] val mutable nelem : uint64
    val pol : RehashPolicy

    new (key: Func<'E, 'K>, hash: Func<'K, uint64>, eq: Func<'K, 'K, bool>) as this =
        { key = key
          hash = (if isNull hash then StdHash.For<'K>() else hash)
          eq = (if isNull eq then Func<'K, 'K, bool>(fun a b -> EqualityComparer<'K>.Default.Equals(a, b)) else eq)
          beforeBegin = HNode<'E>()
          pol = RehashPolicy() }
        then this.buckets <- Array.zeroCreate 1

    member private this.Bkt (code: uint64) : uint64 = code % uint64 this.buckets.Length

    member this.Find (k: 'K) : HNode<'E> =
        let code = this.hash.Invoke(k)
        let b = this.Bkt(code)
        let before = this.buckets.[int b]
        if isNull before then null
        else
            let mutable p = before.next
            let mutable res : HNode<'E> = null
            let mutable go = true
            while go && not (isNull p) do
                if p.code = code && this.eq.Invoke(k, this.key.Invoke(p.``val``)) then res <- p; go <- false
                elif isNull p.next || this.Bkt(p.next.code) <> b then go <- false
                else p <- p.next
            res

    /// Insert e if its key is absent; returns the node holding the key and whether it was added.
    member this.Insert (e: 'E) : struct (HNode<'E> * bool) =
        let k = this.key.Invoke(e)
        let found = this.Find(k)
        if not (isNull found) then struct (found, false)
        else
            let code = this.hash.Invoke(k)
            let struct (re, n) = this.pol.NeedRehash(uint64 this.buckets.Length, this.nelem, 1UL)
            if re then this.Rehash(n)
            let node = HNode<'E>()
            node.``val`` <- e
            node.code <- code
            this.InsertBucketBegin(this.Bkt(code), node)
            this.nelem <- this.nelem + 1UL
            struct (node, true)

    member private this.InsertBucketBegin (b: uint64, node: HNode<'E>) =
        if not (isNull this.buckets.[int b]) then
            node.next <- this.buckets.[int b].next
            this.buckets.[int b].next <- node
        else
            node.next <- this.beforeBegin.next
            this.beforeBegin.next <- node
            if not (isNull node.next) then this.buckets.[int (this.Bkt(node.next.code))] <- node
            this.buckets.[int b] <- this.beforeBegin

    member this.Rehash (n: uint64) =
        let nb : HNode<'E>[] = Array.zeroCreate (int n)
        let mutable p = this.beforeBegin.next
        this.beforeBegin.next <- null
        let mutable bbeginBkt = 0UL
        while not (isNull p) do
            let next = p.next
            let b = p.code % n
            if isNull nb.[int b] then
                p.next <- this.beforeBegin.next
                this.beforeBegin.next <- p
                nb.[int b] <- this.beforeBegin
                if not (isNull p.next) then nb.[int bbeginBkt] <- p
                bbeginBkt <- b
            else
                p.next <- nb.[int b].next
                nb.[int b].next <- p
            p <- next
        this.buckets <- nb

    member this.EraseKey (k: 'K) : bool =
        let code = this.hash.Invoke(k)
        let b = this.Bkt(code)
        let mutable prev = this.buckets.[int b]
        if isNull prev then false
        else
            let mutable p = prev.next
            let mutable miss = false
            while not miss && not (isNull p) && not (p.code = code && this.eq.Invoke(k, this.key.Invoke(p.``val``))) do
                if isNull p.next || this.Bkt(p.next.code) <> b then miss <- true
                else
                    prev <- p
                    p <- p.next
            if miss || isNull p then false
            else
                this.EraseNode(b, prev, p)
                true

    member private this.EraseNode (b: uint64, prev: HNode<'E>, n: HNode<'E>) =
        if obj.ReferenceEquals(prev, this.buckets.[int b]) then
            // removing the bucket's first node (_M_remove_bucket_begin)
            let next = n.next
            let nextBkt = if not (isNull next) then this.Bkt(next.code) else 0UL
            if isNull next || nextBkt <> b then
                if not (isNull next) then this.buckets.[int nextBkt] <- this.buckets.[int b]
                if obj.ReferenceEquals(this.buckets.[int b], this.beforeBegin) then this.beforeBegin.next <- next
                this.buckets.[int b] <- null
        elif not (isNull n.next) then
            let nextBkt = this.Bkt(n.next.code)
            if nextBkt <> b then this.buckets.[int nextBkt] <- prev
        prev.next <- n.next
        this.nelem <- this.nelem - 1UL

    member this.EraseAt (n: HNode<'E>) =
        let b = this.Bkt(n.code)
        let mutable prev = this.buckets.[int b]
        while not (obj.ReferenceEquals(prev.next, n)) do prev <- prev.next
        this.EraseNode(b, prev, n)

    member this.size () : uint64 = this.nelem
    member this.empty () : bool = this.nelem = 0UL
    member this.clear () = this.buckets <- Array.zeroCreate this.buckets.Length; this.beforeBegin.next <- null; this.nelem <- 0UL
    member this.bucket_count () : uint64 = uint64 this.buckets.Length

    member this.reserve (n: uint64) = this.rehash(this.pol.BktForElements(n))
    member this.rehash (n: uint64) =
        // _M_rehash_policy._M_next_bkt(max(n, bkt_for_elements(size + 1))), only if it differs
        let want = this.pol.NextBkt(Math.Max(n, this.pol.BktForElements(this.nelem + 1UL)))
        if want <> uint64 this.buckets.Length then this.Rehash(want)

    member this.Items () : IEnumerable<'E> =
        seq {
            let p = ref this.beforeBegin.next
            while not (isNull p.Value) do
                yield p.Value.``val``
                p.Value <- p.Value.next
        }

    member this.``begin`` () = MIt<'E>(this.beforeBegin.next)
    member this.``end`` () = MIt<'E>(null: HNode<'E>)
    member this.find (k: 'K) = MIt<'E>(this.Find(k))
    member this.count_ (k: 'K) : uint64 = if not (isNull (this.Find(k))) then 1UL else 0UL
    member this.count (k: 'K) : uint64 = this.count_(k)
    member this.contains (k: 'K) : bool = not (isNull (this.Find(k)))
    member this.erase (k: 'K) : uint64 = if this.EraseKey(k) then 1UL else 0UL
    member this.erase (it: MIt<'E>) : MIt<'E> = let nx = it.node.next in this.EraseAt(it.node); MIt<'E>(nx)

    member this.CopyFrom (o: HashTable<'K, 'E>, copy: Func<'E, 'E>) =
        // _M_assign: same bucket count, same node order
        this.buckets <- Array.zeroCreate o.buckets.Length
        this.beforeBegin.next <- null
        this.nelem <- 0UL
        this.pol.NextResize <- o.pol.NextResize
        let mutable prev = this.beforeBegin
        let mutable p = o.beforeBegin.next
        while not (isNull p) do
            let n = HNode<'E>()
            n.``val`` <- copy.Invoke(p.``val``)
            n.code <- p.code
            prev.next <- n
            let b = this.Bkt(n.code)
            if isNull this.buckets.[int b] then this.buckets.[int b] <- prev
            prev <- n
            this.nelem <- this.nelem + 1UL
            p <- p.next

/// An iterator over a hashed or sorted container: a node (null at end) or a position in a list.
[<Struct; CustomEquality; NoComparison; IsReadOnly>]
type MIt<'E> =
    val node : HNode<'E>
    val list : List<'E>
    val pos : int
    new (n: HNode<'E>) = { node = n; list = null; pos = 0 }
    new (l: List<'E>, p: int) = { node = null; list = l; pos = p }
    member this.Clone () : MIt<'E> = this
    member this.Deref : 'E = if not (isNull this.list) then this.list.[this.pos] else this.node.``val``
    static member op_Increment (x: MIt<'E>) : MIt<'E> = if not (isNull x.list) then MIt<'E>(x.list, x.pos + 1) else MIt<'E>(x.node.next)
    static member op_Decrement (x: MIt<'E>) : MIt<'E> = MIt<'E>(x.list, x.pos - 1)
    static member private PosOf (x: MIt<'E>) = if not (isNull x.list) then x.pos else Int32.MaxValue
    static member op_Equality (a: MIt<'E>, b: MIt<'E>) : bool =
        if not (isNull a.list) || not (isNull b.list) then
            not (isNull (if isNull a.list then b.list else a.list)) && MIt<'E>.PosOf(a) = MIt<'E>.PosOf(b)
        else obj.ReferenceEquals(a.node, b.node)
    static member op_Inequality (a: MIt<'E>, b: MIt<'E>) = not (MIt<'E>.op_Equality(a, b))
    override this.Equals (o: obj) = match o with :? MIt<'E> as x -> MIt<'E>.op_Equality(this, x) | _ -> false
    override this.GetHashCode () = this.pos
    interface IEquatable<MIt<'E>> with
        member this.Equals (o: MIt<'E>) = MIt<'E>.op_Equality(this, o)

[<AllowNullLiteral>]
type UMap<'K, 'V> =
    inherit HashTable<'K, Pair<'K, 'V>>
    /// How to make a mapped value that V alone cannot describe (a std::array); see Vec.mk.
    [<DefaultValue(false)>] val mutable mk : Func<'V>
    new (h: Func<'K, uint64>, eq: Func<'K, 'K, bool>, mk: Func<'V>) as this =
        { inherit HashTable<'K, Pair<'K, 'V>>(Func<Pair<'K, 'V>, 'K>(fun p -> p.first), h, eq) } then this.mk <- mk
    new (h: Func<'K, uint64>, eq: Func<'K, 'K, bool>) = UMap<'K, 'V>(h, eq, (null: Func<'V>))
    new (h: Func<'K, uint64>) = UMap<'K, 'V>(h, (null: Func<'K, 'K, bool>), (null: Func<'V>))
    new () = UMap<'K, 'V>((null: Func<'K, uint64>), (null: Func<'K, 'K, bool>), (null: Func<'V>))
    new (il: Pair<'K, 'V>[], h: Func<'K, uint64>, eq: Func<'K, 'K, bool>) as this =
        UMap<'K, 'V>(h, eq) then for p in il do this.Insert(p.Clone()) |> ignore
    new (il: Pair<'K, 'V>[]) = UMap<'K, 'V>(il, (null: Func<'K, uint64>), (null: Func<'K, 'K, bool>))
    member private this.MakeV () : 'V = if not (isNull this.mk) then this.mk.Invoke() else Elem<'V>.Make.Invoke()

    /// operator[]: the value, inserted default-constructed if absent.
    member this.Index (k: 'K) : byref<'V> =
        let mutable f = this.Find(k)
        if isNull f then
            let struct (n, _) = this.Insert(Pair<'K, 'V>(KeyCopy<'K>.Of(k), this.MakeV()))   // the map owns a copy of the key
            f <- n
        &f.``val``.second
    member this.at (k: 'K) : byref<'V> =
        let f = this.Find(k)
        if isNull f then raise (OutOfRange("_Map_base::at"))
        &f.``val``.second
    member this.insert (p: Pair<'K, 'V>) : Pair<MIt<Pair<'K, 'V>>, bool> =
        let struct (n, added) = this.Insert(Pair<'K, 'V>(KeyCopy<'K>.Of(p.first), p.second))
        Pair<MIt<Pair<'K, 'V>>, bool>(MIt<Pair<'K, 'V>>(n), added)
    member this.emplace (k: 'K, v: 'V) = this.insert(Pair<'K, 'V>(k, v))
    member this.emplace (p: Pair<'K, 'V>) = this.insert(p)
    member this.try_emplace (k: 'K, v: 'V) = this.insert(Pair<'K, 'V>(k, v))
    member this.try_emplace (k: 'K) = this.insert(Pair<'K, 'V>(k, this.MakeV()))
    member this.insert_or_assign (k: 'K, v: 'V) : Pair<MIt<Pair<'K, 'V>>, bool> =
        let f = this.Find(k)
        if not (isNull f) then
            f.``val``.second <- v
            Pair<MIt<Pair<'K, 'V>>, bool>(MIt<Pair<'K, 'V>>(f), false)
        else this.insert(Pair<'K, 'V>(k, v))
    member this.Clone () : UMap<'K, 'V> =
        let m = UMap<'K, 'V>(this.hash, this.eq, this.mk)
        m.CopyFrom(this, Func<Pair<'K, 'V>, Pair<'K, 'V>>(fun p -> p.Clone()))
        m
    member this.Assign (o: UMap<'K, 'V>) = if not (obj.ReferenceEquals(this, o)) then this.CopyFrom(o, Func<Pair<'K, 'V>, Pair<'K, 'V>>(fun p -> p.Clone()))
    member this.MoveFrom (o: UMap<'K, 'V>) =
        if not (obj.ReferenceEquals(this, o)) then
            this.CopyFrom(o, Func<Pair<'K, 'V>, Pair<'K, 'V>>(fun p -> p))
            o.clear()
    interface ICopy<UMap<'K, 'V>> with
        member this.Clone () = this.Clone()
        member this.Assign (o) = this.Assign(o)

[<AllowNullLiteral>]
type USet<'T> =
    inherit HashTable<'T, 'T>
    new (h: Func<'T, uint64>, eq: Func<'T, 'T, bool>) = { inherit HashTable<'T, 'T>(Func<'T, 'T>(fun x -> x), h, eq) }
    new (h: Func<'T, uint64>) = USet<'T>(h, (null: Func<'T, 'T, bool>))
    new () = USet<'T>((null: Func<'T, uint64>), (null: Func<'T, 'T, bool>))
    new (il: 'T[], h: Func<'T, uint64>, eq: Func<'T, 'T, bool>) as this =
        USet<'T>(h, eq) then for x in il do this.Insert(x) |> ignore
    new (il: 'T[]) = USet<'T>(il, (null: Func<'T, uint64>), (null: Func<'T, 'T, bool>))
    member this.insert (x: 'T) : Pair<MIt<'T>, bool> =
        let struct (n, added) = this.Insert(KeyCopy<'T>.Of(x))
        Pair<MIt<'T>, bool>(MIt<'T>(n), added)
    member this.emplace (x: 'T) = this.insert(x)
    member this.Clone () : USet<'T> = let m = USet<'T>(this.hash, this.eq) in m.CopyFrom(this, Func<'T, 'T>(fun x -> Elem<'T>.Copy(x))); m
    member this.Assign (o: USet<'T>) = if not (obj.ReferenceEquals(this, o)) then this.CopyFrom(o, Func<'T, 'T>(fun x -> Elem<'T>.Copy(x)))
    member this.MoveFrom (o: USet<'T>) =
        if not (obj.ReferenceEquals(this, o)) then
            this.CopyFrom(o, Func<'T, 'T>(fun x -> x))
            o.clear()
    interface ICopy<USet<'T>> with
        member this.Clone () = this.Clone()
        member this.Assign (o) = this.Assign(o)

/// A hasher of the engine's own (struct CiHash { size_t operator()(...) }): the generated class implements this by
/// calling its operator().
[<AllowNullLiteral>]
type IHashFn<'K> =
    abstract HashOf : 'K -> uint64
/// A two-argument predicate of the engine's own (a key-equal such as CiEqual).
[<AllowNullLiteral>]
type IBinFn<'K> =
    abstract Call2 : 'K * 'K -> bool
/// std::equal_to, for a container whose hasher is the engine's but whose key-equal is the default.
[<Sealed; AllowNullLiteral>]
type StdEq<'K>() =
    member this.Call2 (a: 'K, b: 'K) : bool = EqualityComparer<'K>.Default.Equals(a, b)
    interface IBinFn<'K> with
        member this.Call2 (a, b) = this.Call2(a, b)

/// std::unordered_map<K, V, H, E> with the engine's hasher H and key-equal E: the same table, hashed by H, so lookups
/// and the iteration order are the C++ ones. (C#'s static h0 and e0 are made once per map here: they hold nothing.)
[<Sealed; AllowNullLiteral>]
type UMapC<'K, 'V, 'H, 'E when 'H :> IHashFn<'K> and 'H : (new : unit -> 'H) and 'E :> IBinFn<'K> and 'E : (new : unit -> 'E)> =
    inherit UMap<'K, 'V>
    new (mk: Func<'V>) =
        let h0 = new 'H()
        let e0 = new 'E()
        { inherit UMap<'K, 'V>(Func<'K, uint64>(fun k -> h0.HashOf(k)), Func<'K, 'K, bool>(fun a b -> e0.Call2(a, b)), mk) }
    new () = UMapC<'K, 'V, 'H, 'E>(null: Func<'V>)
    new (il: Pair<'K, 'V>[]) as this = UMapC<'K, 'V, 'H, 'E>() then for p in il do this.Insert(p.Clone()) |> ignore
    member this.Clone () : UMapC<'K, 'V, 'H, 'E> =
        let m = UMapC<'K, 'V, 'H, 'E>(this.mk)
        m.CopyFrom(this, Func<Pair<'K, 'V>, Pair<'K, 'V>>(fun p -> p.Clone()))
        m
    member this.Assign (o: UMapC<'K, 'V, 'H, 'E>) = (this :> UMap<'K, 'V>).Assign(o)
    member this.MoveFrom (o: UMapC<'K, 'V, 'H, 'E>) = (this :> UMap<'K, 'V>).MoveFrom(o)
    interface ICopy<UMapC<'K, 'V, 'H, 'E>> with
        member this.Clone () = this.Clone()
        member this.Assign (o) = this.Assign(o)

/// std::unordered_set<T, H, E> with the engine's hasher and key-equal.
[<Sealed; AllowNullLiteral>]
type USetC<'T, 'H, 'E when 'H :> IHashFn<'T> and 'H : (new : unit -> 'H) and 'E :> IBinFn<'T> and 'E : (new : unit -> 'E)> =
    inherit USet<'T>
    new () =
        let h0 = new 'H()
        let e0 = new 'E()
        { inherit USet<'T>(Func<'T, uint64>(fun k -> h0.HashOf(k)), Func<'T, 'T, bool>(fun a b -> e0.Call2(a, b))) }
    new (il: 'T[]) as this = USetC<'T, 'H, 'E>() then for x in il do this.Insert(x) |> ignore
    member this.Clone () : USetC<'T, 'H, 'E> =
        let m = USetC<'T, 'H, 'E>()
        m.CopyFrom(this, Func<'T, 'T>(fun x -> Elem<'T>.Copy(x)))
        m
    member this.Assign (o: USetC<'T, 'H, 'E>) = (this :> USet<'T>).Assign(o)
    member this.MoveFrom (o: USetC<'T, 'H, 'E>) = (this :> USet<'T>).MoveFrom(o)
    interface ICopy<USetC<'T, 'H, 'E>> with
        member this.Clone () = this.Clone()
        member this.Assign (o) = this.Assign(o)

/// std::map: keys kept sorted by the comparator (a red-black tree's order is the sorted order).
[<Sealed; AllowNullLiteral>]
type Map<'K, 'V> =
    val lt : Func<'K, 'K, bool>
    [<DefaultValue(false)>] val mutable items : List<Pair<'K, 'V>>
    [<DefaultValue(false)>] val mutable mk : Func<'V>
    new (less: Func<'K, 'K, bool>, mk: Func<'V>) as this =
        { lt = (if isNull less then Less<'K>.Fn else less) } then this.items <- List<Pair<'K, 'V>>(); this.mk <- mk
    new (less: Func<'K, 'K, bool>) = Map<'K, 'V>(less, (null: Func<'V>))
    new () = Map<'K, 'V>((null: Func<'K, 'K, bool>), (null: Func<'V>))
    new (il: Pair<'K, 'V>[], less: Func<'K, 'K, bool>) as this = Map<'K, 'V>(less) then for p in il do this.insert(p.Clone()) |> ignore
    new (il: Pair<'K, 'V>[]) = Map<'K, 'V>(il, (null: Func<'K, 'K, bool>))

    member private this.Lower (k: 'K) : int =
        let mutable lo = 0
        let mutable hi = this.items.Count
        while lo < hi do
            let mid = (lo + hi) / 2
            if this.lt.Invoke(this.items.[mid].first, k) then lo <- mid + 1 else hi <- mid
        lo
    member private this.Find (k: 'K) : int =
        let i = this.Lower(k)
        if i < this.items.Count && not (this.lt.Invoke(k, this.items.[i].first)) then i else -1

    member this.Index (k: 'K) : byref<'V> =
        let i = this.Lower(k)
        if i = this.items.Count || this.lt.Invoke(k, this.items.[i].first) then
            this.items.Insert(i, Pair<'K, 'V>(KeyCopy<'K>.Of(k), (if not (isNull this.mk) then this.mk.Invoke() else Elem<'V>.Make.Invoke())))
        &this.items.[i].second
    member this.at (k: 'K) : byref<'V> =
        let i = this.Find(k)
        if i < 0 then raise (OutOfRange("map::at"))
        &this.items.[i].second
    member this.insert (p: Pair<'K, 'V>) : Pair<MIt<Pair<'K, 'V>>, bool> =
        let i = this.Lower(p.first)
        if i < this.items.Count && not (this.lt.Invoke(p.first, this.items.[i].first)) then
            Pair<MIt<Pair<'K, 'V>>, bool>(MIt<Pair<'K, 'V>>(this.items, i), false)
        else
            this.items.Insert(i, Pair<'K, 'V>(KeyCopy<'K>.Of(p.first), p.second))
            Pair<MIt<Pair<'K, 'V>>, bool>(MIt<Pair<'K, 'V>>(this.items, i), true)
    member this.emplace (k: 'K, v: 'V) = this.insert(Pair<'K, 'V>(k, v))
    member this.emplace (p: Pair<'K, 'V>) = this.insert(p)
    member this.try_emplace (k: 'K, v: 'V) = this.insert(Pair<'K, 'V>(k, v))
    member this.insert_or_assign (k: 'K, v: 'V) : Pair<MIt<Pair<'K, 'V>>, bool> =
        let i = this.Find(k)
        if i >= 0 then
            this.items.[i].second <- v
            Pair<MIt<Pair<'K, 'V>>, bool>(MIt<Pair<'K, 'V>>(this.items, i), false)
        else this.insert(Pair<'K, 'V>(k, v))
    member this.find (k: 'K) = let i = this.Find(k) in MIt<Pair<'K, 'V>>(this.items, (if i < 0 then this.items.Count else i))
    member this.lower_bound (k: 'K) = MIt<Pair<'K, 'V>>(this.items, this.Lower(k))
    member this.upper_bound (k: 'K) =
        let mutable i = this.Lower(k)
        while i < this.items.Count && not (this.lt.Invoke(k, this.items.[i].first)) do i <- i + 1
        MIt<Pair<'K, 'V>>(this.items, i)
    member this.count (k: 'K) : uint64 = if this.Find(k) >= 0 then 1UL else 0UL
    member this.contains (k: 'K) : bool = this.Find(k) >= 0
    member this.erase (k: 'K) : uint64 = let i = this.Find(k) in if i < 0 then 0UL else (this.items.RemoveAt(i); 1UL)
    member this.erase (it: MIt<Pair<'K, 'V>>) = this.items.RemoveAt(it.pos); MIt<Pair<'K, 'V>>(this.items, it.pos)
    member this.size () : uint64 = uint64 this.items.Count
    member this.empty () : bool = this.items.Count = 0
    member this.clear () = this.items.Clear()
    member this.``begin`` () = MIt<Pair<'K, 'V>>(this.items, 0)
    member this.``end`` () = MIt<Pair<'K, 'V>>(this.items, this.items.Count)
    member this.Items () : IEnumerable<Pair<'K, 'V>> = this.items :> IEnumerable<Pair<'K, 'V>>
    member this.Clone () : Map<'K, 'V> =
        let m = Map<'K, 'V>(this.lt, this.mk)
        for p in this.items do m.items.Add(p.Clone())
        m
    member this.Assign (o: Map<'K, 'V>) =
        if not (obj.ReferenceEquals(this, o)) then
            this.items <- List<Pair<'K, 'V>>()
            for p in o.items do this.items.Add(p.Clone())
    member this.MoveFrom (o: Map<'K, 'V>) =
        if not (obj.ReferenceEquals(this, o)) then
            this.items <- o.items
            o.items <- List<Pair<'K, 'V>>()
    interface ICopy<Map<'K, 'V>> with
        member this.Clone () = this.Clone()
        member this.Assign (o) = this.Assign(o)

[<Sealed; AllowNullLiteral>]
type Set<'T> =
    val lt : Func<'T, 'T, bool>
    [<DefaultValue(false)>] val mutable items : List<'T>
    new (less: Func<'T, 'T, bool>) as this = { lt = (if isNull less then Less<'T>.Fn else less) } then this.items <- List<'T>()
    new () = Set<'T>(null: Func<'T, 'T, bool>)
    new (il: 'T[], less: Func<'T, 'T, bool>) as this = Set<'T>(less) then for x in il do this.insert(x) |> ignore
    new (il: 'T[]) = Set<'T>(il, (null: Func<'T, 'T, bool>))
    member private this.Lower (k: 'T) : int =
        let mutable lo = 0
        let mutable hi = this.items.Count
        while lo < hi do
            let mid = (lo + hi) / 2
            if this.lt.Invoke(this.items.[mid], k) then lo <- mid + 1 else hi <- mid
        lo
    member this.insert (x: 'T) : Pair<MIt<'T>, bool> =
        let i = this.Lower(x)
        if i < this.items.Count && not (this.lt.Invoke(x, this.items.[i])) then Pair<MIt<'T>, bool>(MIt<'T>(this.items, i), false)
        else
            this.items.Insert(i, KeyCopy<'T>.Of(x))
            Pair<MIt<'T>, bool>(MIt<'T>(this.items, i), true)
    member this.emplace (x: 'T) = this.insert(x)
    member this.find (k: 'T) = let i = this.Lower(k) in MIt<'T>(this.items, (if i < this.items.Count && not (this.lt.Invoke(k, this.items.[i])) then i else this.items.Count))
    member this.count (k: 'T) : uint64 = let i = this.Lower(k) in if i < this.items.Count && not (this.lt.Invoke(k, this.items.[i])) then 1UL else 0UL
    member this.contains (k: 'T) : bool = this.count(k) > 0UL
    member this.erase (k: 'T) : uint64 =
        let i = this.Lower(k)
        if i < this.items.Count && not (this.lt.Invoke(k, this.items.[i])) then (this.items.RemoveAt(i); 1UL) else 0UL
    member this.erase (it: MIt<'T>) = this.items.RemoveAt(it.pos); MIt<'T>(this.items, it.pos)
    member this.size () : uint64 = uint64 this.items.Count
    member this.empty () : bool = this.items.Count = 0
    member this.clear () = this.items.Clear()
    member this.``begin`` () = MIt<'T>(this.items, 0)
    member this.``end`` () = MIt<'T>(this.items, this.items.Count)
    member this.Items () : IEnumerable<'T> = this.items :> IEnumerable<'T>
    member this.Clone () : Set<'T> = let m = Set<'T>(this.lt) in (for x in this.items do m.items.Add(Elem<'T>.Copy(x))); m
    member this.Assign (o: Set<'T>) =
        if not (obj.ReferenceEquals(this, o)) then
            this.items <- List<'T>()
            for x in o.items do this.items.Add(Elem<'T>.Copy(x))
    member this.MoveFrom (o: Set<'T>) =
        if not (obj.ReferenceEquals(this, o)) then
            this.items <- o.items
            o.items <- List<'T>()
    interface ICopy<Set<'T>> with
        member this.Clone () = this.Clone()
        member this.Assign (o) = this.Assign(o)

/// std::stack over std::deque: push, pop, top. (The C# version keeps a List and hands out a ref into it through
/// CollectionsMarshal.AsSpan; F# cannot return a reference out of a local span, so this keeps its own array, which
/// gives the same elements, the same references and the same order.)
[<Sealed; AllowNullLiteral>]
type Stk<'T>() =
    let mutable items : 'T[] = Array.zeroCreate 4
    let mutable count = 0
    member this.push (x: 'T) =
        if count = items.Length then Array.Resize(&items, items.Length * 2)
        items.[count] <- x
        count <- count + 1
    member this.pop () = count <- count - 1; items.[count] <- Unchecked.defaultof<'T>
    member this.top () : byref<'T> = &items.[count - 1]
    member this.empty () : bool = count = 0
    member this.size () : uint64 = uint64 count
    member this.Clone () : Stk<'T> = let s = Stk<'T>() in (for i in 0 .. count - 1 do s.push(Elem<'T>.Copy(items.[i]))); s
    member this.Assign (o: Stk<'T>) =
        if not (obj.ReferenceEquals(this, o)) then
            let src = o.Snapshot()
            this.Reset()
            for x in src do this.push(Elem<'T>.Copy(x))
    member this.MoveFrom (o: Stk<'T>) =
        if not (obj.ReferenceEquals(this, o)) then
            let src = o.Snapshot()
            this.Reset()
            for x in src do this.push(x)
            o.Reset()
    member internal this.Snapshot () : 'T[] = Array.sub items 0 count
    member internal this.Reset () = items <- Array.zeroCreate 4; count <- 0
    interface ICopy<Stk<'T>> with
        member this.Clone () = this.Clone()
        member this.Assign (o) = this.Assign(o)

/// std::queue over std::deque: first in, first out. (Its own array, as Stk.)
[<Sealed; AllowNullLiteral>]
type Que<'T>() =
    let mutable items : 'T[] = Array.zeroCreate 4
    let mutable count = 0
    let mutable head = 0
    member this.push (x: 'T) =
        if count = items.Length then Array.Resize(&items, items.Length * 2)
        items.[count] <- x
        count <- count + 1
    member this.pop () =
        items.[head] <- Unchecked.defaultof<'T>
        head <- head + 1
        if head > 64 && head * 2 > count then
            Array.Copy(items, head, items, 0, count - head)
            Array.Clear(items, count - head, head)
            count <- count - head
            head <- 0
    member this.front () : byref<'T> = &items.[head]
    member this.back () : byref<'T> = &items.[count - 1]
    member this.empty () : bool = count = head
    member this.size () : uint64 = uint64 (count - head)
    member internal this.Snapshot () : 'T[] = Array.sub items head (count - head)
    member internal this.Reset () = items <- Array.zeroCreate 4; count <- 0; head <- 0
    member this.Clone () : Que<'T> = let q = Que<'T>() in (for x in this.Snapshot() do q.push(Elem<'T>.Copy(x))); q
    member this.Assign (o: Que<'T>) =
        if not (obj.ReferenceEquals(this, o)) then
            let src = o.Snapshot()
            this.Reset()
            for x in src do this.push(Elem<'T>.Copy(x))
    member this.MoveFrom (o: Que<'T>) =
        if not (obj.ReferenceEquals(this, o)) then
            let src = o.Snapshot()
            this.Reset()
            for x in src do this.push(x)
            o.Reset()
    interface ICopy<Que<'T>> with
        member this.Clone () = this.Clone()
        member this.Assign (o) = this.Assign(o)

// =================================================================================================== Misc.cs
// The rest of the small runtime the generated engine calls: moves and out-parameter copy-back (Mv), <algorithm>
// (Algo), x86-64 float-to-integer conversion (Conv), raw memory on typed arrays (Mem, Bytes), tuples, atomics and
// locks for one thread, and C library extras. (LibM is in LibM.fs.)

/// The argument of the constructors that leave an object uninitialized for an __init method.
[<Struct; IsReadOnly>]
type Raw =
    static member V : Raw = Unchecked.defaultof<Raw>

/// A C++ pointer to data member (T C::*): given the object, a reference to that field of it.
type MemPtr<'TO, 'TF> = delegate of 'TO -> byref<'TF>

[<AllowNullLiteral>]
type IDestroyElems =
    abstract DestroyElems : unit -> unit

/// Runs a C++ destructor: an engine class's Dispose (its destructor body, members, base), a file stream's close, a
/// container's elements. The emitter calls it only for types whose destruction has an effect.
[<AbstractClass; Sealed>]
type Dtor =
    static member Run (o: obj) : unit =
        match o with
        | null -> ()
        | :? IDisposable as d -> d.Dispose()
        | :? IDestroyElems as c -> c.DestroyElems()
        | :? Array as arr -> for x in arr do Dtor.Run(x)
        | _ -> ()

/// A destructor deferred to the end of the enclosing block (a using declaration), unless the object is returned to
/// the caller.
[<Sealed; AllowNullLiteral>]
type Defer(a0: Action) =
    let mutable a = a0
    member this.Cancel () = a <- null
    member this.Dispose () = let x = a in a <- null; (if not (isNull x) then x.Invoke())
    interface IDisposable with
        member this.Dispose () = this.Dispose()

[<AbstractClass; Sealed>]
type Mv =
    /// unique_ptr::reset(p) and unique_ptr assignment: store the new pointer, then delete the old one.
    static member Reset<'T when 'T : not struct> (p: byref<'T>, q: 'T) =
        let old = p
        p <- q
        if not (isNull (box old)) && not (obj.ReferenceEquals(old, q)) then Dtor.Run(box old)
    /// Copy a boxed out-parameter back into its variable after the call produced value v.
    [<MethodImpl(MethodImplOptions.AggressiveInlining)>]
    static member Back<'R, 'T> (v: 'R, x: byref<'T>, box0: 'T[]) : 'R = x <- box0.[0]; v
    /// std::move of a container or class into a new owner: the source is left empty.
    static member Take<'T> (x: byref<'T>) : 'T = let r = x in x <- Elem<'T>.Make.Invoke(); r
    /// std::move of a unique_ptr or std::function: the source is left null.
    static member TakeRef<'T> (x: byref<'T>) : 'T = let r = x in x <- Unchecked.defaultof<'T>; r
    static member PostStep<'T> (p: byref<Ptr<'T>>, d: int) : Ptr<'T> = let r = p in p <- Ptr<'T>.op_Addition(p, int64 d); r
    static member PostStep<'T> (p: byref<Iter<'T>>, d: int) : Iter<'T> = let r = p in p <- Iter<'T>.op_Addition(p, int64 d); r
    static member PostStep (p: byref<CPtr>, d: int) : CPtr = let r = p in p <- CPtr.op_Addition(p, d); r
    static member PostStep<'E> (p: byref<MIt<'E>>, d: int) : MIt<'E> =
        let r = p
        p <- (if d > 0 then MIt<'E>.op_Increment(p) else MIt<'E>.op_Decrement(p))
        r
    static member PostStep<'T> (p: byref<RevIter<'T>>, d: int) : RevIter<'T> = let r = p in p <- RevIter<'T>.op_Increment(p); r
    static member Delete (o: obj) = match o with :? IDisposable as d -> d.Dispose() | _ -> ()

[<AbstractClass; Sealed>]
type Conv =
    // x86-64 cvttsd2si: NaN and out-of-range give the "integer indefinite" value
    static member D2I (x: float) : int = if x > -2147483649.0 && x < 2147483648.0 then int x else Int32.MinValue
    static member D2L (x: float) : int64 = if x >= -9223372036854775808.0 && x < 9223372036854775808.0 then int64 x else Int64.MinValue
    // GCC's double -> unsigned int: the 64-bit conversion, truncated
    static member D2U (x: float) : uint32 = uint32 (Conv.D2L(x))
    // GCC's double -> unsigned long: below 2^63 the signed conversion, above it the conversion of x - 2^63
    static member D2UL (x: float) : uint64 =
        if x < 9223372036854775808.0 then uint64 (Conv.D2L(x))
        else uint64 (Conv.D2L(x - 9223372036854775808.0)) ^^^ 0x8000000000000000UL
    static member D2S (x: float) : int16 = int16 (Conv.D2I(x))
    static member D2US (x: float) : uint16 = uint16 (Conv.D2I(x))
    static member D2SB (x: float) : sbyte = sbyte (Conv.D2I(x))
    static member D2B (x: float) : byte = byte (Conv.D2I(x))

[<AbstractClass; Sealed>]
type Std =
    /// std::rethrow_exception: throw the stored exception again, its original stack kept.
    static member Rethrow (e: Exception) : unit = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e).Throw()
    /// std::call_once: run f the first time only (single-threaded; an exception leaves the flag unset).
    static member call_once (flag: OnceFlag, f: Action) = if not flag.``done`` then (f.Invoke(); flag.``done`` <- true)
    static member Span<'T> () : CSpan<'T> = Unchecked.defaultof<CSpan<'T>>
    static member Span<'T> (v: Vec<'T>) : CSpan<'T> = CSpan<'T>(v.a, 0L, int64 v.n)
    static member Span<'T> (arr: 'T[]) : CSpan<'T> = CSpan<'T>(arr, 0L, int64 arr.Length)
    static member Span<'T> (p: Ptr<'T>, n: uint64) : CSpan<'T> = CSpan<'T>(p.a, p.o, int64 n)
    static member Span<'T> (s: CSpan<'T>) : CSpan<'T> = s
    /// std::get<i> of a pair or tuple (structured bindings of map entries and emplace results).
    static member get0<'A, 'B> (p: Pair<'A, 'B>) : byref<'A> = &p.first
    static member get1<'A, 'B> (p: Pair<'A, 'B>) : byref<'B> = &p.second
    static member get0<'A, 'B> (t: Tup<'A, 'B>) : byref<'A> = &t.i0
    static member get1<'A, 'B> (t: Tup<'A, 'B>) : byref<'B> = &t.i1
    static member get0<'A, 'B, 'C> (t: Tup<'A, 'B, 'C>) : byref<'A> = &t.i0
    static member get1<'A, 'B, 'C> (t: Tup<'A, 'B, 'C>) : byref<'B> = &t.i1
    static member get2<'A, 'B, 'C> (t: Tup<'A, 'B, 'C>) : byref<'C> = &t.i2
    static member QNaN : float = BitConverter.Int64BitsToDouble(0x7FF8000000000000L)   // C++'s quiet NaN
    static member SNaN : float = BitConverter.Int64BitsToDouble(0x7FF4000000000000L)
    static member QNaNf : float32 = BitConverter.Int32BitsToSingle(0x7FC00000)
    static member swap<'T> (a: byref<'T>, b: byref<'T>) = let t = a in a <- b; b <- t
    static member exchange<'T> (a: byref<'T>, v: 'T) : 'T = let old = a in a <- v; old
    static member release<'T> (a: byref<'T>) : 'T = let r = a in a <- Unchecked.defaultof<'T>; r
    static member iabs (x: int) : int = if x < 0 then -x else x
    static member iabs (x: int64) : int64 = if x < 0L then -x else x
    static member Nop (x: obj) : int = 0
    static member OptCmp<'T> (a: Opt<'T>, b: obj, op: string) : bool =
        let eq =
            match b with
            | null -> not a.has
            | :? Opt<'T> as o -> a.has = o.has && (not a.has || EqualityComparer<'T>.Default.Equals(a.``val``, o.``val``))
            | _ -> a.has && EqualityComparer<'T>.Default.Equals(a.``val``, unbox<'T> b)
        if op = "==" then eq else not eq
    /// std::from_chars for double (chars_format::general), as libstdc++ has it: the value strtod reads from the same
    /// characters (both are correctly rounded), except that a sign other than '-', leading white space and a
    /// hexadecimal prefix are not read ("0x10" is 0 and stops at the x), and a value too large or too small for a
    /// double (an overflow to inf, or a nonzero number read as zero) is result_out_of_range (34) with the value left
    /// unchanged. As the Java, Go and C# runtimes have it.
    static member from_chars (first: CPtr, last: CPtr, v: byref<float>) : FromCharsResult =
        let n = last.O - first.O
        let tmp : byte[] = Array.zeroCreate (n + 2)
        if n > 0 then Array.Copy(first.B, first.O, tmp, 0, n)
        let k = if tmp.[0] = byte '-' then 1 else 0
        let c = int tmp.[k]
        let lc = if c >= int 'A' && c <= int 'Z' then c + 32 else c
        if not ((c >= int '0' && c <= int '9') || c = int '.' || lc = int 'i' || lc = int 'n') then FromCharsResult(first, 22)
        else
            if c = int '0' && (tmp.[k + 1] = byte 'x' || tmp.[k + 1] = byte 'X') then tmp.[k + 1] <- 0uy   // read the 0 alone
            let p = CPtr(tmp, 0)
            let mutable e = p
            let r = R.strtod(p, &e)
            if e.O = 0 then FromCharsResult(first, 22)
            else
                let res = FromCharsResult(CPtr.op_Addition(first, e.O), 0)
                if Double.IsInfinity(r) && lc <> int 'i' then FromCharsResult(res.ptr, 34)   // overflow
                else
                    let mutable under = false
                    if r = 0.0 then
                        // underflow: a nonzero digit before the exponent
                        let mutable j = k
                        let mutable go = true
                        while go && j < e.O do
                            let d = int tmp.[j]
                            if d = int 'e' || d = int 'E' then go <- false
                            elif d >= int '1' && d <= int '9' then under <- true; go <- false
                            else j <- j + 1
                    if under then FromCharsResult(res.ptr, 34)
                    else
                        v <- r
                        res
    /// std::from_chars for integers, base 10: an optional '-' (signed types only), digits; an empty match is
    /// invalid_argument (22) with ptr = first; a value out of the type's range is result_out_of_range (34), all the
    /// digits consumed, v unchanged.
    static member private IntChars (first: CPtr, last: CPtr, signed: bool, mn: BigInteger, mx: BigInteger, r: outref<BigInteger>, ok: outref<bool>) : FromCharsResult =
        let mutable i = first.O
        r <- BigInteger.Zero
        ok <- false
        let mutable neg = false
        if signed && i < last.O && first.B.[i] = byte '-' then neg <- true; i <- i + 1
        let start = i
        let mutable acc = BigInteger.Zero
        while i < last.O && first.B.[i] >= byte '0' && first.B.[i] <= byte '9' do
            acc <- acc * BigInteger(10) + BigInteger(int (first.B.[i] - byte '0'))
            i <- i + 1
        if i = start then FromCharsResult(first, 22)
        else
            if neg then acc <- BigInteger.Negate(acc)
            r <- acc
            if acc < mn || acc > mx then FromCharsResult(CPtr(first.B, i), 34)
            else
                ok <- true
                FromCharsResult(CPtr(first.B, i), 0)
    static member from_chars (first: CPtr, last: CPtr, v: byref<int>) : FromCharsResult =
        let mutable r = BigInteger.Zero
        let mutable ok = false
        let res = Std.IntChars(first, last, true, BigInteger(Int32.MinValue), BigInteger(Int32.MaxValue), &r, &ok)
        if ok then v <- int r
        res
    static member from_chars (first: CPtr, last: CPtr, v: byref<int64>) : FromCharsResult =
        let mutable r = BigInteger.Zero
        let mutable ok = false
        let res = Std.IntChars(first, last, true, BigInteger(Int64.MinValue), BigInteger(Int64.MaxValue), &r, &ok)
        if ok then v <- int64 r
        res
    static member from_chars (first: CPtr, last: CPtr, v: byref<uint32>) : FromCharsResult =
        let mutable r = BigInteger.Zero
        let mutable ok = false
        let res = Std.IntChars(first, last, false, BigInteger(UInt32.MinValue), BigInteger(UInt32.MaxValue), &r, &ok)
        if ok then v <- uint32 r
        res
    static member from_chars (first: CPtr, last: CPtr, v: byref<uint64>) : FromCharsResult =
        let mutable r = BigInteger.Zero
        let mutable ok = false
        let res = Std.IntChars(first, last, false, BigInteger(UInt64.MinValue), BigInteger(UInt64.MaxValue), &r, &ok)
        if ok then v <- uint64 r
        res

[<Struct; NoEquality; NoComparison>]
type FromCharsResult =
    val mutable ptr : CPtr
    val mutable ec : int
    new (p: CPtr, e: int) = { ptr = p; ec = e }

[<Sealed; AllowNullLiteral>]
type ErrorCode() =
    [<DefaultValue(false)>] val mutable v : int
    member this.Bool () : bool = this.v <> 0
    member this.value () : int = this.v
    member this.clear () = this.v <- 0
    /// glibc's strerror text for the errno values the filesystem calls set.
    member this.message () : Str =
        Str(match this.v with
            | 0 -> "Success" | 2 -> "No such file or directory" | 13 -> "Permission denied" | 17 -> "File exists"
            | 20 -> "Not a directory" | 21 -> "Is a directory" | 95 -> "Operation not supported"
            | _ -> "Unknown error " + string this.v)

[<Sealed; AllowNullLiteral>]
type Atomic<'T> =
    [<DefaultValue(false)>] val mutable v : 'T
    new () = { }
    new (x: 'T) as this = { } then this.v <- x
    member this.exchange (x: 'T) : 'T = let o = this.v in this.v <- x; o
    member this.load (order: int) : 'T = this.v
    member this.load () : 'T = this.v
    member this.store (x: 'T, order: int) = this.v <- x
    member this.store (x: 'T) = this.v <- x
    member this.fetch_add (d: 'T, order: int) : 'T = let o = this.v in this.v <- Num.Add(this.v, d); o
    member this.fetch_add (d: 'T) : 'T = this.fetch_add(d, 0)
    member this.fetch_sub (d: 'T, order: int) : 'T = let o = this.v in this.v <- Num.Sub(this.v, d); o
    member this.fetch_sub (d: 'T) : 'T = this.fetch_sub(d, 0)
    // compare_exchange_weak/strong on one thread: never fails spuriously
    member this.compare_exchange (expected: byref<'T>, desired: 'T) : bool =
        if EqualityComparer<'T>.Default.Equals(this.v, expected) then this.v <- desired; true
        else expected <- this.v; false
    member this.Clone () : Atomic<'T> = Atomic<'T>(this.v)
    member this.Assign (o: Atomic<'T>) = this.v <- o.v
    interface ICopy<Atomic<'T>> with
        member this.Clone () = this.Clone()
        member this.Assign (o) = this.Assign(o)

/// Wrapping integer and IEEE arithmetic on a generic value (std::atomic's fetch_add).
[<AbstractClass; Sealed>]
type Num =
    static member Add<'T> (a: 'T, b: 'T) : 'T =
        match box a, box b with
        | (:? int as x), (:? int as y) -> unbox<'T> (box (x + y))
        | (:? int64 as x), (:? int64 as y) -> unbox<'T> (box (x + y))
        | (:? uint64 as x), (:? uint64 as y) -> unbox<'T> (box (x + y))
        | (:? uint32 as x), (:? uint32 as y) -> unbox<'T> (box (x + y))
        | (:? float as x), (:? float as y) -> unbox<'T> (box (x + y))
        | _ -> raise (NotSupportedException("atomic add on " + string typeof<'T>))
    static member Sub<'T> (a: 'T, b: 'T) : 'T =
        match box a, box b with
        | (:? int as x), (:? int as y) -> unbox<'T> (box (x - y))
        | (:? int64 as x), (:? int64 as y) -> unbox<'T> (box (x - y))
        | (:? uint64 as x), (:? uint64 as y) -> unbox<'T> (box (x - y))
        | (:? uint32 as x), (:? uint32 as y) -> unbox<'T> (box (x - y))
        | (:? float as x), (:? float as y) -> unbox<'T> (box (x - y))
        | _ -> raise (NotSupportedException("atomic sub on " + string typeof<'T>))

[<Sealed; AllowNullLiteral>]
type Mutex() = class end
/// The ports run single-threaded: the engine's one worker thread (the output writer, IOThread) is replaced by a
/// synchronous call (Hand_IOThread.cs), so these never block.
[<Sealed; AllowNullLiteral>]
type CondVar() = class end
[<Sealed; AllowNullLiteral>]
type Thread() =
    member this.joinable () : bool = false
    member this.join () = ()
[<Sealed; AllowNullLiteral>]
type Lock() = class end

[<Sealed; AllowNullLiteral>]
type Tup<'A, 'B> =
    [<DefaultValue(false)>] val mutable i0 : 'A
    [<DefaultValue(false)>] val mutable i1 : 'B
    new () as this = { } then this.i0 <- Elem<'A>.Make.Invoke(); this.i1 <- Elem<'B>.Make.Invoke()
    new (a: 'A, b: 'B) as this = { } then this.i0 <- a; this.i1 <- b
    member this.Clone () : Tup<'A, 'B> = Tup<'A, 'B>(Elem<'A>.Copy(this.i0), Elem<'B>.Copy(this.i1))
    member this.Assign (o: Tup<'A, 'B>) = this.i0 <- Elem<'A>.Copy(o.i0); this.i1 <- Elem<'B>.Copy(o.i1)
    interface ICopy<Tup<'A, 'B>> with
        member this.Clone () = this.Clone()
        member this.Assign (o) = this.Assign(o)
[<Sealed; AllowNullLiteral>]
type Tup<'A, 'B, 'C> =
    [<DefaultValue(false)>] val mutable i0 : 'A
    [<DefaultValue(false)>] val mutable i1 : 'B
    [<DefaultValue(false)>] val mutable i2 : 'C
    new () as this = { } then this.i0 <- Elem<'A>.Make.Invoke(); this.i1 <- Elem<'B>.Make.Invoke(); this.i2 <- Elem<'C>.Make.Invoke()
    new (a: 'A, b: 'B, c: 'C) as this = { } then this.i0 <- a; this.i1 <- b; this.i2 <- c
    member this.Clone () : Tup<'A, 'B, 'C> = Tup<'A, 'B, 'C>(Elem<'A>.Copy(this.i0), Elem<'B>.Copy(this.i1), Elem<'C>.Copy(this.i2))
    member this.Assign (o: Tup<'A, 'B, 'C>) = this.i0 <- Elem<'A>.Copy(o.i0); this.i1 <- Elem<'B>.Copy(o.i1); this.i2 <- Elem<'C>.Copy(o.i2)
    interface ICopy<Tup<'A, 'B, 'C>> with
        member this.Clone () = this.Clone()
        member this.Assign (o) = this.Assign(o)

/// Raw memory on typed arrays: memcpy, memset, fread / fwrite and byte views of numbers.
[<AbstractClass; Sealed>]
type Mem =
    /// &obj - p: the index of an object of an array of objects (pointers to objects are references).
    static member Diff<'T when 'T : not struct> (o: 'T, p: Ptr<'T>) : int64 = int64 (Array.IndexOf(p.a, o)) - p.o
    static member Diff<'T when 'T : not struct> (a: Ptr<'T>, o: 'T) : int64 = a.o - int64 (Array.IndexOf(a.a, o))
    /// calloc(n, size) cast to T*: n*size bytes of zeroed T. Zero bytes give an empty array, which, like glibc's unique
    /// pointer for a zero-size request, is never dereferenced. free() is the collector's.
    static member Calloc<'T when 'T : struct and 'T : (new : unit -> 'T) and 'T :> ValueType> (n: uint64, size: uint64) : Ptr<'T> =
        Ptr<'T>(Array.zeroCreate (int (n * size / uint64 (Unsafe.SizeOf<'T>()))), 0L)
    static member Free (p: obj) = ()
    /// The nbytes bytes at p. A null p with no bytes is no bytes: libstdc++ passes the data() of an empty vector,
    /// which is nullptr (Vec.data), with a length of 0 (os.write(v.data(), 0), memcpy(d, s, 0)).
    static member private Bytes<'T when 'T : struct and 'T : (new : unit -> 'T) and 'T :> ValueType> (p: Ptr<'T>, nbytes: int) : Span<byte> =
        if isNull p.a && nbytes = 0 then Span<byte>.Empty
        else MemoryMarshal.AsBytes(Span<'T>(p.a, int p.o, p.a.Length - int p.o)).Slice(0, nbytes)
    static member private Bytes (p: CPtr, nbytes: int) : Span<byte> = Span<byte>(p.B, p.O, nbytes)

    static member memcpy<'T, 'U when 'T : struct and 'T : (new : unit -> 'T) and 'T :> ValueType and 'U : struct and 'U : (new : unit -> 'U) and 'U :> ValueType> (d: Ptr<'T>, s: Ptr<'U>, n: uint64) : Ptr<'T> =
        Mem.Bytes(s, int n).CopyTo(Mem.Bytes(d, int n)); d
    static member memcpy<'T when 'T : struct and 'T : (new : unit -> 'T) and 'T :> ValueType> (d: Ptr<'T>, s: CPtr, n: uint64) : Ptr<'T> =
        Mem.Bytes(s, int n).CopyTo(Mem.Bytes(d, int n)); d
    static member memcpy<'U when 'U : struct and 'U : (new : unit -> 'U) and 'U :> ValueType> (d: CPtr, s: Ptr<'U>, n: uint64) : CPtr =
        Mem.Bytes(s, int n).CopyTo(Mem.Bytes(d, int n)); d
    static member memcpy (d: CPtr, s: CPtr, n: uint64) : CPtr = (if n <> 0UL then Array.Copy(s.B, s.O, d.B, d.O, int n)); d
    static member memset<'T when 'T : struct and 'T : (new : unit -> 'T) and 'T :> ValueType> (d: Ptr<'T>, c: int, n: uint64) : Ptr<'T> =
        Mem.Bytes(d, int n).Fill(byte c); d
    static member memset (d: CPtr, c: int, n: uint64) : CPtr = Span<byte>(d.B, d.O, int n).Fill(byte c); d

    static member fwrite<'T when 'T : struct and 'T : (new : unit -> 'T) and 'T :> ValueType> (p: Ptr<'T>, size: uint64, count: uint64, f: CFile) : uint64 =
        let b = Mem.Bytes(p, int (size * count)).ToArray()
        f.S.Write(b, 0, b.Length)
        count
    static member fwrite (p: CPtr, size: uint64, count: uint64, f: CFile) : uint64 =
        if size * count <> 0UL then f.S.Write(p.B, p.O, int (size * count))
        count
    static member fread<'T when 'T : struct and 'T : (new : unit -> 'T) and 'T :> ValueType> (p: Ptr<'T>, size: uint64, count: uint64, f: CFile) : uint64 =
        let want = int (size * count)
        let b : byte[] = Array.zeroCreate want
        let mutable got = 0
        let mutable go = true
        while go && got < want do
            let k = f.S.Read(b, got, want - got)
            if k <= 0 then go <- false else got <- got + k
        if got < want then f.ReadFailed()
        Span<byte>(b, 0, got).CopyTo(Mem.Bytes(p, got))
        if size = 0UL then 0UL else uint64 got / size
    static member fread (p: CPtr, size: uint64, count: uint64, f: CFile) : uint64 =
        let want = int (size * count)
        let mutable got = 0
        let mutable go = true
        while go && got < want do
            let k = f.S.Read(p.B, p.O + got, want - got)
            if k <= 0 then go <- false else got <- got + k
        if got < want then f.ReadFailed()
        if size = 0UL then 0UL else uint64 got / size
    static member BytesOf<'T when 'T : struct and 'T : (new : unit -> 'T) and 'T :> ValueType> (p: Ptr<'T>, n: int) : byte[] = Mem.Bytes(p, n).ToArray()
    static member BytesInto<'T when 'T : struct and 'T : (new : unit -> 'T) and 'T :> ValueType> (p: Ptr<'T>, src: byte[], off: int, n: int) =
        Span<byte>(src, off, n).CopyTo(Mem.Bytes(p, n))

[<AbstractClass; Sealed>]
type Bytes =
    /// The bytes of the numbers p points at, as a char* for reading (a copy).
    static member View<'T when 'T : struct and 'T : (new : unit -> 'T) and 'T :> ValueType> (p: Ptr<'T>) : CPtr =
        if isNull p.a && isNull p.at then CPtr.Null      // (const char*) of a null pointer is null
        else
            let span = MemoryMarshal.AsBytes(Span<'T>(p.a, int p.o, p.a.Length - int p.o))
            CPtr(span.ToArray(), 0)
    static member View (p: CPtr) : CPtr = p

[<AbstractClass; Sealed>]
type Bits =
    static member Cast<'T when 'T : struct> (x: obj) : 'T =
        let t = typeof<'T>
        match x with
        | :? float as d when t = typeof<uint64> -> unbox<'T> (box (uint64 (BitConverter.DoubleToInt64Bits(d))))
        | :? float as d when t = typeof<int64> -> unbox<'T> (box (BitConverter.DoubleToInt64Bits(d)))
        | :? uint64 as u when t = typeof<float> -> unbox<'T> (box (BitConverter.Int64BitsToDouble(int64 u)))
        | :? int64 as l when t = typeof<float> -> unbox<'T> (box (BitConverter.Int64BitsToDouble(l)))
        | :? float32 as f when t = typeof<uint32> -> unbox<'T> (box (uint32 (BitConverter.SingleToInt32Bits(f))))
        | :? uint32 as u when t = typeof<float32> -> unbox<'T> (box (BitConverter.Int32BitsToSingle(int u)))
        | _ -> raise (NotSupportedException("bit_cast " + string (x.GetType()) + " -> " + string t))

/// A reference member of a class (int& held by an object): the array cell it refers to.
type RefGet<'T> = delegate of unit -> byref<'T>

/// A reference member of a class (bool& held by an object): the place it refers to, an array cell or a field of some
/// object (a lambda returning a ref to it).
[<Sealed; AllowNullLiteral>]
type Box<'T> =
    [<DefaultValue(false)>] val mutable a : 'T[]
    [<DefaultValue(false)>] val mutable o : int64
    val g : RefGet<'T>
    new (a: 'T[], o: int64) as this = { g = null } then this.a <- a; this.o <- o
    new (g: RefGet<'T>) = { g = g }
    static member Of (g: RefGet<'T>) : Box<'T> = Box<'T>(g)
    member this.V with get () : byref<'T> = if not (isNull this.g) then &this.g.Invoke() else &this.a.[int this.o]

/// <algorithm> and <numeric> over vector / array iterators, pointers and map iterators.
[<AbstractClass; Sealed>]
type Algo =
    static member fill<'T> (b: Iter<'T>, e: Iter<'T>, v: 'T) = (let mutable i = b.i in while i < e.i do (b.a.[int i] <- Elem<'T>.Copy(v); i <- i + 1L))
    static member fill<'T> (b: Ptr<'T>, e: Ptr<'T>, v: 'T) = (let mutable i = b.o in while i < e.o do (b.a.[int i] <- Elem<'T>.Copy(v); i <- i + 1L))
    static member fill_n<'T> (b: Iter<'T>, n: uint64, v: 'T) : Iter<'T> =
        let mutable i = 0L
        while i < int64 n do
            b.a.[int (b.i + i)] <- Elem<'T>.Copy(v)
            i <- i + 1L
        Iter<'T>.op_Addition(b, int64 n)
    static member fill_n<'T> (b: Ptr<'T>, n: uint64, v: 'T) : Ptr<'T> =
        let mutable i = 0L
        while i < int64 n do
            b.a.[int (b.o + i)] <- Elem<'T>.Copy(v)
            i <- i + 1L
        Ptr<'T>.op_Addition(b, int64 n)
    static member any_of<'T> (b: Iter<'T>, e: Iter<'T>, f: Func<'T, bool>) : bool =
        let mutable i = b.i
        let mutable r = false
        while not r && i < e.i do
            if f.Invoke(b.a.[int i]) then r <- true
            i <- i + 1L
        r
    static member all_of<'T> (b: Iter<'T>, e: Iter<'T>, f: Func<'T, bool>) : bool =
        let mutable i = b.i
        let mutable r = true
        while r && i < e.i do
            if not (f.Invoke(b.a.[int i])) then r <- false
            i <- i + 1L
        r
    static member none_of<'T> (b: Iter<'T>, e: Iter<'T>, f: Func<'T, bool>) : bool = not (Algo.any_of(b, e, f))
    static member private FindFrom<'T> (b: Iter<'T>, e: Iter<'T>, f: 'T -> bool) : Iter<'T> =
        let mutable i = b.i
        let mutable r = -1L
        while r < 0L && i < e.i do
            if f b.a.[int i] then r <- i
            i <- i + 1L
        if r >= 0L then Iter<'T>.op_Addition(b, r - b.i) else e
    static member find<'T> (b: Iter<'T>, e: Iter<'T>, v: 'T) : Iter<'T> = let c = EqualityComparer<'T>.Default in Algo.FindFrom(b, e, fun x -> c.Equals(x, v))
    static member find_if<'T> (b: Iter<'T>, e: Iter<'T>, f: Func<'T, bool>) : Iter<'T> = Algo.FindFrom(b, e, fun x -> f.Invoke(x))
    static member find_if_not<'T> (b: Iter<'T>, e: Iter<'T>, f: Func<'T, bool>) : Iter<'T> = Algo.FindFrom(b, e, fun x -> not (f.Invoke(x)))
    static member count<'T> (b: Iter<'T>, e: Iter<'T>, v: 'T) : int64 =
        let c = EqualityComparer<'T>.Default
        let mutable n = 0L
        let mutable i = b.i
        while i < e.i do
            if c.Equals(b.a.[int i], v) then n <- n + 1L
            i <- i + 1L
        n
    static member count_if<'T> (b: Iter<'T>, e: Iter<'T>, f: Func<'T, bool>) : int64 =
        let mutable n = 0L
        let mutable i = b.i
        while i < e.i do
            if f.Invoke(b.a.[int i]) then n <- n + 1L
            i <- i + 1L
        n
    static member accumulate (b: Iter<float>, e: Iter<float>, init: float) : float = (let mutable r = init in let mutable i = b.i in (while i < e.i do (r <- r + b.a.[int i]; i <- i + 1L)); r)
    static member accumulate (b: Iter<int>, e: Iter<int>, init: int) : int = (let mutable r = init in let mutable i = b.i in (while i < e.i do (r <- r + b.a.[int i]; i <- i + 1L)); r)
    static member accumulate (b: Iter<int64>, e: Iter<int64>, init: int64) : int64 = (let mutable r = init in let mutable i = b.i in (while i < e.i do (r <- r + b.a.[int i]; i <- i + 1L)); r)
    static member accumulate (b: Iter<int>, e: Iter<int>, init: float) : float = (let mutable r = init in let mutable i = b.i in (while i < e.i do (r <- r + float b.a.[int i]; i <- i + 1L)); r)
    static member accumulate (b: Iter<uint64>, e: Iter<uint64>, init: uint64) : uint64 = (let mutable r = init in let mutable i = b.i in (while i < e.i do (r <- r + b.a.[int i]; i <- i + 1L)); r)
    static member accumulate<'T, 'A> (b: Iter<'T>, e: Iter<'T>, init: 'A, op: Func<'A, 'T, 'A>) : 'A =
        let mutable r = init
        let mutable i = b.i
        while i < e.i do
            r <- op.Invoke(r, b.a.[int i])
            i <- i + 1L
        r
    static member lower_bound<'T> (b: Iter<'T>, e: Iter<'T>, v: 'T) : Iter<'T> = Algo.lower_bound<'T>(b, e, v, Less<'T>.Fn)
    static member lower_bound<'T> (b: Iter<'T>, e: Iter<'T>, v: 'T, lt: Func<'T, 'T, bool>) : Iter<'T> =
        let mutable first = b.i
        let mutable len = e.i - b.i
        while len > 0L do
            let half = len >>> 1
            let mid = first + half
            if lt.Invoke(b.a.[int mid], v) then first <- mid + 1L; len <- len - half - 1L
            else len <- half
        Iter<'T>.op_Addition(b, first - b.i)
    static member lower_bound<'T, 'V> (b: Iter<'T>, e: Iter<'T>, v: 'V, lt: Func<'T, 'V, bool>) : Iter<'T> =
        let mutable first = b.i
        let mutable len = e.i - b.i
        while len > 0L do
            let half = len >>> 1
            let mid = first + half
            if lt.Invoke(b.a.[int mid], v) then first <- mid + 1L; len <- len - half - 1L
            else len <- half
        Iter<'T>.op_Addition(b, first - b.i)
    static member upper_bound<'T> (b: Iter<'T>, e: Iter<'T>, v: 'T) : Iter<'T> = Algo.upper_bound<'T>(b, e, v, Less<'T>.Fn)
    static member upper_bound<'T> (b: Iter<'T>, e: Iter<'T>, v: 'T, lt: Func<'T, 'T, bool>) : Iter<'T> =
        let mutable first = b.i
        let mutable len = e.i - b.i
        while len > 0L do
            let half = len >>> 1
            let mid = first + half
            if lt.Invoke(v, b.a.[int mid]) then len <- half
            else first <- mid + 1L; len <- len - half - 1L
        Iter<'T>.op_Addition(b, first - b.i)
    static member upper_bound<'T, 'V> (b: Iter<'T>, e: Iter<'T>, v: 'V, lt: Func<'V, 'T, bool>) : Iter<'T> =
        let mutable first = b.i
        let mutable len = e.i - b.i
        while len > 0L do
            let half = len >>> 1
            let mid = first + half
            if lt.Invoke(v, b.a.[int mid]) then len <- half
            else first <- mid + 1L; len <- len - half - 1L
        Iter<'T>.op_Addition(b, first - b.i)
    static member binary_search<'T> (b: Iter<'T>, e: Iter<'T>, v: 'T) : bool =
        let it = Algo.lower_bound<'T>(b, e, v)
        it.i <> e.i && not (Less<'T>.Fn.Invoke(v, it.Deref))
    /// std::is_sorted: no element is less than the one before it.
    static member is_sorted<'T> (b: Iter<'T>, e: Iter<'T>) : bool = Algo.is_sorted<'T>(b, e, Less<'T>.Fn)
    static member is_sorted<'T> (b: Iter<'T>, e: Iter<'T>, lt: Func<'T, 'T, bool>) : bool =
        let mutable r = true
        let mutable i = b.i + 1L
        while r && i < e.i do
            if lt.Invoke(b.a.[int i], b.a.[int (i - 1L)]) then r <- false
            i <- i + 1L
        r
    static member adjacent_find<'T> (b: Iter<'T>, e: Iter<'T>) : Iter<'T> = let c = EqualityComparer<'T>.Default in Algo.adjacent_find<'T>(b, e, Func<'T, 'T, bool>(fun x y -> c.Equals(x, y)))
    static member adjacent_find<'T> (b: Iter<'T>, e: Iter<'T>, eq: Func<'T, 'T, bool>) : Iter<'T> =
        let mutable i = b.i
        let mutable r = -1L
        while r < 0L && i + 1L < e.i do
            if eq.Invoke(b.a.[int i], b.a.[int (i + 1L)]) then r <- i
            i <- i + 1L
        if r >= 0L then Iter<'T>.op_Addition(b, r - b.i) else e
    /// std::unique: keeps the first of each run of equal elements (libstdc++'s order of moves).
    static member unique<'T> (b: Iter<'T>, e: Iter<'T>) : Iter<'T> = let c = EqualityComparer<'T>.Default in Algo.unique<'T>(b, e, Func<'T, 'T, bool>(fun x y -> c.Equals(x, y)))
    static member unique<'T> (b: Iter<'T>, e: Iter<'T>, eq: Func<'T, 'T, bool>) : Iter<'T> =
        if b.i = e.i then e
        else
            let last = e.i
            let mutable first = b.i
            // adjacent_find
            let mutable nxt = first
            let mutable go = true
            while go do
                nxt <- nxt + 1L
                if nxt = last then go <- false
                elif eq.Invoke(b.a.[int first], b.a.[int nxt]) then go <- false
                else first <- nxt
            if nxt = last then e
            else
                let mutable dest = first
                first <- nxt
                first <- first + 1L
                while first <> last do
                    if not (eq.Invoke(b.a.[int dest], b.a.[int first])) then
                        dest <- dest + 1L
                        b.a.[int dest] <- b.a.[int first]
                    first <- first + 1L
                Iter<'T>.op_Addition(b, dest + 1L - b.i)
    static member remove_if<'T> (b: Iter<'T>, e: Iter<'T>, f: Func<'T, bool>) : Iter<'T> =
        let mutable first = b.i
        while first <> e.i && not (f.Invoke(b.a.[int first])) do first <- first + 1L
        if first = e.i then e
        else
            let mutable res = first
            let mutable i = first + 1L
            while i <> e.i do
                if not (f.Invoke(b.a.[int i])) then
                    b.a.[int res] <- b.a.[int i]
                    res <- res + 1L
                i <- i + 1L
            Iter<'T>.op_Addition(b, res - b.i)
    static member remove<'T> (b: Iter<'T>, e: Iter<'T>, v: 'T) : Iter<'T> = let c = EqualityComparer<'T>.Default in Algo.remove_if(b, e, Func<'T, bool>(fun x -> c.Equals(x, v)))
    static member max_element<'T> (b: Iter<'T>, e: Iter<'T>) : Iter<'T> = Algo.max_element<'T>(b, e, Less<'T>.Fn)
    static member max_element<'T> (b: Iter<'T>, e: Iter<'T>, lt: Func<'T, 'T, bool>) : Iter<'T> =
        if b.i = e.i then e
        else
            let mutable best = b.i
            let mutable i = b.i + 1L
            while i < e.i do
                if lt.Invoke(b.a.[int best], b.a.[int i]) then best <- i
                i <- i + 1L
            Iter<'T>.op_Addition(b, best - b.i)
    static member min_element<'T> (b: Iter<'T>, e: Iter<'T>) : Iter<'T> = Algo.min_element<'T>(b, e, Less<'T>.Fn)
    static member min_element<'T> (b: Iter<'T>, e: Iter<'T>, lt: Func<'T, 'T, bool>) : Iter<'T> =
        if b.i = e.i then e
        else
            let mutable best = b.i
            let mutable i = b.i + 1L
            while i < e.i do
                if lt.Invoke(b.a.[int i], b.a.[int best]) then best <- i
                i <- i + 1L
            Iter<'T>.op_Addition(b, best - b.i)
    static member copy<'T> (b: Iter<'T>, e: Iter<'T>, o: Iter<'T>) : Iter<'T> =
        let mutable i = b.i
        while i < e.i do
            o.a.[int (o.i + (i - b.i))] <- Elem<'T>.Copy(b.a.[int i])
            i <- i + 1L
        Iter<'T>.op_Addition(o, e.i - b.i)
    static member copy<'T> (b: Iter<'T>, e: Iter<'T>, o: Ptr<'T>) : Ptr<'T> =
        let mutable i = b.i
        while i < e.i do
            o.a.[int (o.o + (i - b.i))] <- Elem<'T>.Copy(b.a.[int i])
            i <- i + 1L
        Ptr<'T>.op_Addition(o, e.i - b.i)
    static member copy<'T> (b: Ptr<'T>, e: Ptr<'T>, o: Ptr<'T>) : Ptr<'T> =
        let mutable i = b.o
        while i < e.o do
            o.a.[int (o.o + (i - b.o))] <- Elem<'T>.Copy(b.a.[int i])
            i <- i + 1L
        Ptr<'T>.op_Addition(o, e.o - b.o)
    static member copy<'T> (b: Ptr<'T>, e: Ptr<'T>, o: Iter<'T>) : Iter<'T> =
        let mutable i = b.o
        while i < e.o do
            o.a.[int (o.i + (i - b.o))] <- Elem<'T>.Copy(b.a.[int i])
            i <- i + 1L
        Iter<'T>.op_Addition(o, e.o - b.o)
    static member copy_n<'T> (b: Iter<'T>, n: uint64, o: Iter<'T>) : Iter<'T> = Algo.copy(b, Iter<'T>.op_Addition(b, int64 n), o)
    static member copy_n<'T> (b: Ptr<'T>, n: uint64, o: Ptr<'T>) : Ptr<'T> = Algo.copy(b, Ptr<'T>.op_Addition(b, int64 n), o)
    /// transform over a string's bytes with a function returning int (::toupper and lambdas around it): each result
    /// is stored as a char, as the C++ assignment converts it.
    static member transform (b: Iter<byte>, e: Iter<byte>, o: Iter<byte>, f: Func<byte, int>) : Iter<byte> =
        let mutable i = b.i
        while i < e.i do
            o.a.[int (o.i + (i - b.i))] <- byte (f.Invoke(b.a.[int i]))
            i <- i + 1L
        Iter<byte>.op_Addition(o, e.i - b.i)
    static member transform (b: Iter<byte>, e: Iter<byte>, o: Iter<byte>, f: Func<byte, sbyte>) : Iter<byte> =
        let mutable i = b.i
        while i < e.i do
            o.a.[int (o.i + (i - b.i))] <- byte (f.Invoke(b.a.[int i]))
            i <- i + 1L
        Iter<byte>.op_Addition(o, e.i - b.i)
    static member transform<'T, 'U> (b: Iter<'T>, e: Iter<'T>, o: Iter<'U>, f: Func<'T, 'U>) : Iter<'U> =
        let mutable i = b.i
        while i < e.i do
            o.a.[int (o.i + (i - b.i))] <- f.Invoke(b.a.[int i])
            i <- i + 1L
        Iter<'U>.op_Addition(o, e.i - b.i)
    static member replace<'T> (b: Iter<'T>, e: Iter<'T>, old: 'T, nw: 'T) =
        let c = EqualityComparer<'T>.Default
        let mutable i = b.i
        while i < e.i do
            if c.Equals(b.a.[int i], old) then b.a.[int i] <- nw
            i <- i + 1L
    static member reverse<'T> (b: Iter<'T>, e: Iter<'T>) = Array.Reverse(b.a, int b.i, int (e.i - b.i))
    static member iota (b: Iter<int>, e: Iter<int>, v0: int) = (let mutable v = v0 in let mutable i = b.i in while i < e.i do (b.a.[int i] <- v; v <- v + 1; i <- i + 1L))
    static member iota (b: Iter<uint64>, e: Iter<uint64>, v0: uint64) = (let mutable v = v0 in let mutable i = b.i in while i < e.i do (b.a.[int i] <- v; v <- v + 1UL; i <- i + 1L))
    static member equal<'T> (b: Iter<'T>, e: Iter<'T>, b2: Iter<'T>) : bool =
        let c = EqualityComparer<'T>.Default
        let mutable r = true
        let mutable i = b.i
        while r && i < e.i do
            if not (c.Equals(b.a.[int i], b2.a.[int (b2.i + (i - b.i))])) then r <- false
            i <- i + 1L
        r
    static member distance<'T> (a: Iter<'T>, b: Iter<'T>) : int64 = b.i - a.i
    static member distance<'T> (a: Ptr<'T>, b: Ptr<'T>) : int64 = b.o - a.o
    static member next<'T> (a: Iter<'T>, n: int64) : Iter<'T> = Iter<'T>.op_Addition(a, n)
    static member next<'T> (a: Iter<'T>) : Iter<'T> = Iter<'T>.op_Addition(a, 1L)
    static member prev<'T> (a: Iter<'T>, n: int64) : Iter<'T> = Iter<'T>.op_Subtraction(a, n)
    static member prev<'T> (a: Iter<'T>) : Iter<'T> = Iter<'T>.op_Subtraction(a, 1L)
    static member VecFrom<'T> (b: Iter<'T>, e: Iter<'T>) : Vec<'T> =
        let v = Vec<'T>()
        v.a <- Array.zeroCreate (int (e.i - b.i))
        v.n <- int (e.i - b.i)
        for i in 0 .. v.n - 1 do v.a.[i] <- Elem<'T>.Copy(b.a.[int b.i + i])
        v
    static member VecFromPtr<'T> (b: Ptr<'T>, e: Ptr<'T>) : Vec<'T> =
        let v = Vec<'T>()
        v.a <- Array.zeroCreate (int (e.o - b.o))
        v.n <- int (e.o - b.o)
        for i in 0 .. v.n - 1 do v.a.[i] <- Elem<'T>.Copy(b.a.[int b.o + i])
        v
    static member ``begin``<'T> (v: Vec<'T>) : Iter<'T> = v.``begin`` ()
    static member ``end``<'T> (v: Vec<'T>) : Iter<'T> = v.``end`` ()
    static member ``begin``<'T> (a: 'T[]) : Iter<'T> = Iter<'T>(a, 0L)
    static member ``end``<'T> (a: 'T[]) : Iter<'T> = Iter<'T>(a, int64 a.Length)
    static member size<'T> (v: Vec<'T>) : uint64 = v.size ()
    static member size<'T> (a: 'T[]) : uint64 = uint64 a.Length

/// std::chrono time points and durations, used only for timing (profiling counters, the run's elapsed time,
/// timestamps in temporary file names); none of them reaches the results.
[<Struct; NoEquality; NoComparison>]
type TimePoint =
    val mutable ns : int64
    new (n: int64) = { ns = n }
    member this.Clone () : TimePoint = this
    static member now () : TimePoint = TimePoint(int64 (float (Diagnostics.Stopwatch.GetTimestamp()) * (1e9 / float Diagnostics.Stopwatch.Frequency)))
    static member op_Subtraction (a: TimePoint, b: TimePoint) : Duration = Duration(a.ns - b.ns)
    member this.time_since_epoch () : Duration = Duration(this.ns)

/// A duration held in nanoseconds; count() gives seconds (the engine counts durations as duration<double>).
[<Struct; NoEquality; NoComparison>]
type Duration =
    val mutable ns : int64
    new (n: int64) = { ns = n }
    new (d: Duration) = { ns = d.ns }
    member this.Clone () : Duration = this
    member this.count () : float = float this.ns * 1e-9
    static member op_Addition (a: Duration, b: Duration) : Duration = Duration(a.ns + b.ns)
    static member op_Subtraction (a: Duration, b: Duration) : Duration = Duration(a.ns - b.ns)

module private SysNative =
    [<DllImport("libc", EntryPoint = "get_nprocs")>]
    extern int get_nprocs()

[<AbstractClass; Sealed>]
type Sys =
    /// std::thread::hardware_concurrency: libstdc++ on Linux asks glibc's get_nprocs (processors online, not the
    /// cgroup or affinity limit .NET's ProcessorCount reports). The engine prints it in THREADS warnings.
    static member hardware_concurrency () : uint32 =
        try
            let n = SysNative.get_nprocs()
            if n > 0 then uint32 n else 0u
        with _ -> uint32 Environment.ProcessorCount

/// A unique number per object for this run: orders pointers the way only their identity matters (a std::map key
/// holding table pointers is looked up, never iterated in address order).
[<AbstractClass; Sealed>]
type Ident =
    [<DefaultValue>] static val mutable private ids : ConditionalWeakTable<obj, Ref<int64>>
    [<DefaultValue>] static val mutable private next : int64
    static member Of (o: obj) : int64 =
        if isNull o then 0L
        else
            if isNull Ident.ids then Ident.ids <- ConditionalWeakTable<obj, Ref<int64>>()
            Ident.ids.GetValue(o, fun _ -> Ident.next <- Ident.next + 1L; ref Ident.next).Value

/// std::tuple of more than three elements (a map key compared element by element, as std::tuple's operator< does).
[<Sealed; AllowNullLiteral>]
type TupN =
    [<DefaultValue(false)>] val mutable v : obj[]
    new () as this = { } then this.v <- Array.Empty<obj>()
    new ([<ParamArray>] items: obj[]) as this = { } then this.v <- items
    member this.Clone () : TupN = TupN(this.v.Clone() :?> obj[])
    member this.Assign (o: TupN) = this.v <- o.v.Clone() :?> obj[]
    member this.MoveFrom (o: TupN) = this.v <- o.v
    static member private Cmp (a: obj, b: obj) : int =
        match a with
        | null -> if isNull b then 0 else -1
        | :? float as x -> let y = unbox<float> b in if x < y then -1 elif y < x then 1 else 0
        | :? int as x -> x.CompareTo(unbox<int> b)
        | :? int64 as x -> x.CompareTo(unbox<int64> b)
        | :? bool as x -> x.CompareTo(unbox<bool> b)
        | :? Ptr<float> as p ->
            let q = unbox<Ptr<float>> b
            let c = Ident.Of(box p.a).CompareTo(Ident.Of(box q.a))
            if c <> 0 then c else p.o.CompareTo(q.o)
        | :? IComparable as c0 -> c0.CompareTo(b)
        | _ -> raise (NotSupportedException("tuple element " + string (a.GetType())))
    member this.CompareTo (o: TupN) : int =
        let mutable r = 0
        let mutable i = 0
        while r = 0 && i < this.v.Length do
            r <- TupN.Cmp(this.v.[i], o.v.[i])
            i <- i + 1
        r
    member this.Equals (o: TupN) : bool = not (isNull o) && this.CompareTo(o) = 0
    override this.Equals (o: obj) = match o with :? TupN as t -> this.Equals(t) | _ -> false
    override this.GetHashCode () = this.v.Length
    interface ICopy<TupN> with
        member this.Clone () = this.Clone()
        member this.Assign (o) = this.Assign(o)
    interface IComparable<TupN> with
        member this.CompareTo (o) = this.CompareTo(o)
    interface IEquatable<TupN> with
        member this.Equals (o: TupN) = this.Equals(o)

/// Debugging aid: with the environment variable SWMM6_CS_TRACE set, each exception a translated catch handler
/// catches is printed to stderr (the engine itself turns most of them into error codes).
[<AbstractClass; Sealed>]
type Dbg =
    static member Caught (e: Exception) =
        if not (isNull (Environment.GetEnvironmentVariable("SWMM6_CS_TRACE"))) then Console.Error.WriteLine("[caught] " + string e)

/// std::once_flag: whether call_once has run its function.
[<Sealed; AllowNullLiteral>]
type OnceFlag() =
    [<DefaultValue(false)>] val mutable ``done`` : bool

// =================================================================================================== Str.cs
/// std::string (and std::string_view, which the engine only reads) as a mutable byte sequence, so lengths,
/// comparisons and printf padding count bytes as libstdc++ does. c_str() hands out a NUL-terminated CPtr over the
/// same buffer; npos is UInt64.MaxValue as in C++.
[<Sealed; AllowNullLiteral>]
type Str =
    [<DefaultValue(false)>] val mutable b : byte[]       // capacity >= n + 1; b[n] = 0 whenever c_str() is taken
    [<DefaultValue(false)>] val mutable n : int

    /// const char* -> std::string: the converting constructor C++ applies implicitly.
    static member op_Implicit (p: CPtr) : Str = Str(p)
    static member npos : uint64 = UInt64.MaxValue

    new () as this = { } then this.b <- Array.zeroCreate 16
    new (s: Str) = Str(s.b, 0, s.n)                       // the copy constructor (std::string(view))
    new (s: string) as this =
        { }
        then
            let e = Encoding.Latin1.GetBytes(s)
            this.b <- Array.zeroCreate (e.Length + 1)
            Array.Copy(e, this.b, e.Length)
            this.n <- e.Length
    new (src: byte[], off: int, len: int) as this =
        { }
        then
            this.b <- Array.zeroCreate (len + 1)
            if len <> 0 then Array.Copy(src, off, this.b, 0, len)
            this.n <- len
    new (p: CPtr) = Str(p.B, p.O, int (R.strlen(p)))
    new (p: CPtr, len: uint64) = Str(p.B, p.O, int len)
    new (count: uint64, ch: int) as this =
        { }
        then
            this.n <- int count
            this.b <- Array.zeroCreate (this.n + 1)
            for i in 0 .. this.n - 1 do this.b.[i] <- byte ch
    new (s: Str, pos: uint64, len: uint64) as this =
        { }
        then
            let p = int pos
            let m = int (Math.Min(len, uint64 (s.n - p)))
            this.b <- Array.zeroCreate (m + 1)
            Array.Copy(s.b, p, this.b, 0, m)
            this.n <- m
    new (s: Str, pos: uint64) = Str(s, pos, UInt64.MaxValue)
    static member FromBytes (x: byte[]) : Str = Str(x, 0, x.Length)

    /// string_view::remove_prefix / remove_suffix (a view here is a Str of its own).
    member this.remove_prefix (k: uint64) = let m = int k in Array.Copy(this.b, m, this.b, 0, this.n - m); this.n <- this.n - m; this.b.[this.n] <- 0uy
    member this.remove_suffix (k: uint64) = this.n <- this.n - int k; this.b.[this.n] <- 0uy

    member private this.Reserve (need: int) =
        if need + 1 > this.b.Length then
            let cap = Math.Max(need + 1, this.b.Length * 2)
            Array.Resize(&this.b, cap)

    member this.size () : uint64 = uint64 this.n
    member this.length () : uint64 = uint64 this.n
    member this.empty () : bool = this.n = 0
    member this.clear () = this.n <- 0
    member this.c_str () : CPtr = this.Reserve(this.n); this.b.[this.n] <- 0uy; CPtr(this.b, 0)
    member this.data () : CPtr = this.c_str ()
    member this.Item with get (i: uint64) : byref<byte> = &this.b.[int i]
    member this.Item with get (i: int64) : byref<byte> = &this.b.[int i]
    member this.Item with get (i: int) : byref<byte> = &this.b.[i]
    member this.at (i: uint64) : byte =
        if i >= uint64 this.n then raise (OutOfRange("basic_string::at: __n (which is " + string i + ") >= this->size() (which is " + string this.n + ")"))
        this.b.[int i]
    member this.front () : byref<byte> = &this.b.[0]
    member this.back () : byref<byte> = &this.b.[this.n - 1]
    member this.Char (i: uint64) : sbyte = sbyte this.b.[int i]

    member this.Clone () : Str = Str(this.b, 0, this.n)
    member this.Assign (o: Str) =
        if not (obj.ReferenceEquals(this, o)) then
            this.Reserve(o.n)
            Array.Copy(o.b, this.b, o.n)
            this.n <- o.n
    member this.Assign (p: CPtr) =
        let m = int (R.strlen(p))
        let t : byte[] = Array.zeroCreate (m + 1)
        Array.Copy(p.B, p.O, t, 0, m)
        this.b <- t
        this.n <- m
    member this.Assign (ch: int) = this.Reserve(1); this.b.[0] <- byte ch; this.n <- 1
    member this.MoveFrom (o: Str) =
        if not (obj.ReferenceEquals(this, o)) then
            this.b <- o.b
            this.n <- o.n
            o.b <- Array.zeroCreate 16
            o.n <- 0
    member this.assign (o: Str) : Str = this.Assign(o); this
    member this.assign (p: CPtr) : Str = this.Assign(p); this
    member this.assign (b0: CPtr, e0: CPtr) : Str = this.assign(b0, uint64 (e0.O - b0.O))     // assign(first, last)
    member this.assign (b0: Ptr<sbyte>, e0: Ptr<sbyte>) : Str = this.assign(CPtr.op_Implicit(b0), uint64 (e0.o - b0.o))
    member this.assign (p: CPtr, len: uint64) : Str =
        let t : byte[] = Array.zeroCreate (int len + 1)
        if len <> 0UL then Array.Copy(p.B, p.O, t, 0, int len)
        this.b <- t
        this.n <- int len
        this
    member this.assign (count: uint64, ch: int) : Str =
        this.Reserve(int count)
        for i in 0 .. int count - 1 do this.b.[i] <- byte ch
        this.n <- int count
        this

    member this.append (o: Str) : Str = this.Reserve(this.n + o.n); Array.Copy(o.b, 0, this.b, this.n, o.n); this.n <- this.n + o.n; this
    member this.append (o: Str, pos: uint64, len: uint64) : Str =
        let p = int pos
        let m = int (Math.Min(len, uint64 (o.n - p)))
        this.Reserve(this.n + m)
        Array.Copy(o.b, p, this.b, this.n, m)
        this.n <- this.n + m
        this
    member this.append (p: CPtr) : Str =
        let m = int (R.strlen(p))
        this.Reserve(this.n + m)
        Array.Copy(p.B, p.O, this.b, this.n, m)
        this.n <- this.n + m
        this
    member this.append (p: CPtr, len: uint64) : Str =
        let m = int len
        if m <> 0 then
            this.Reserve(this.n + m)
            Array.Copy(p.B, p.O, this.b, this.n, m)
            this.n <- this.n + m
        this
    member this.append (count: uint64, ch: int) : Str =
        let m = int count
        this.Reserve(this.n + m)
        for i in 0 .. m - 1 do this.b.[this.n + i] <- byte ch
        this.n <- this.n + m
        this
    member this.append (ch: int) : Str = this.append(1UL, ch)
    member this.push_back (ch: int) = this.Reserve(this.n + 1); this.b.[this.n] <- byte ch; this.n <- this.n + 1
    member this.pop_back () = this.n <- this.n - 1
    member this.resize (m: uint64, ch: int) =
        let k = int m
        this.Reserve(k)
        for i in this.n .. k - 1 do this.b.[i] <- byte ch
        this.n <- k
    member this.resize (m: uint64) = this.resize(m, 0)
    member this.reserve (m: uint64) = this.Reserve(int m)
    member this.shrink_to_fit () = ()

    member this.substr (pos: uint64, len: uint64) : Str =
        if pos > uint64 this.n then raise (OutOfRange(String.Format("basic_string::substr: __pos (which is {0}) > this->size() (which is {1})", pos, this.n)))
        let m = int (Math.Min(len, uint64 this.n - pos))
        Str(this.b, int pos, m)
    member this.substr (pos: uint64) : Str = this.substr(pos, UInt64.MaxValue)
    member this.substr () : Str = this.substr(0UL, UInt64.MaxValue)

    member this.erase (pos: uint64, len: uint64) : Str =
        let p = int pos
        let m = int (Math.Min(len, uint64 (this.n - p)))
        Array.Copy(this.b, p + m, this.b, p, this.n - p - m)
        this.n <- this.n - m
        this
    member this.erase (pos: uint64) : Str = this.erase(pos, UInt64.MaxValue)
    member this.erase () : Str = this.erase(0UL, UInt64.MaxValue)
    member this.insert (pos: uint64, s: Str) : Str =
        let p = int pos
        this.Reserve(this.n + s.n)
        Array.Copy(this.b, p, this.b, p + s.n, this.n - p)
        Array.Copy(s.b, 0, this.b, p, s.n)
        this.n <- this.n + s.n
        this
    member this.insert (pos: uint64, s: CPtr) : Str = this.insert(pos, Str(s))
    member this.insert (pos: uint64, count: uint64, ch: int) : Str = this.insert(pos, Str(count, ch))
    member this.replace (pos: uint64, len: uint64, s: Str) : Str =
        let p = int pos
        let m = int (Math.Min(len, uint64 (this.n - p)))
        let t : byte[] = Array.zeroCreate (this.n - m + s.n + 1)
        Array.Copy(this.b, 0, t, 0, p)
        Array.Copy(s.b, 0, t, p, s.n)
        Array.Copy(this.b, p + m, t, p + s.n, this.n - p - m)
        this.b <- t
        this.n <- this.n - m + s.n
        this
    member this.replace (pos: uint64, len: uint64, s: CPtr) : Str = this.replace(pos, len, Str(s))

    static member private In (c: byte, set: Str) : bool =
        let mutable r = false
        let mutable i = 0
        while not r && i < set.n do
            if set.b.[i] = c then r <- true
            i <- i + 1
        r

    member this.find (s: Str, pos: uint64) : uint64 =
        if s.n = 0 then (if pos <= uint64 this.n then pos else Str.npos)
        else
            let mutable i = int64 pos
            let mutable r = Str.npos
            while r = Str.npos && i + int64 s.n <= int64 this.n do
                let mutable j = 0
                while j < s.n && this.b.[int i + j] = s.b.[j] do j <- j + 1
                if j = s.n then r <- uint64 i
                i <- i + 1L
            r
    member this.find (s: Str) : uint64 = this.find(s, 0UL)
    member this.find (s: CPtr, pos: uint64) : uint64 = this.find(Str(s), pos)
    member this.find (s: CPtr) : uint64 = this.find(Str(s), 0UL)
    member this.find (ch: int, pos: uint64) : uint64 =
        let mutable i = int64 pos
        let mutable r = Str.npos
        while r = Str.npos && i < int64 this.n do
            if this.b.[int i] = byte ch then r <- uint64 i
            i <- i + 1L
        r
    member this.find (ch: int) : uint64 = this.find(ch, 0UL)
    member this.rfind (s: Str, pos: uint64) : uint64 =
        if s.n > this.n then Str.npos
        else
            let mutable i = int64 (Math.Min(pos, uint64 (this.n - s.n)))
            let mutable r = Str.npos
            while r = Str.npos && i >= 0L do
                let mutable j = 0
                while j < s.n && this.b.[int i + j] = s.b.[j] do j <- j + 1
                if j = s.n then r <- uint64 i
                i <- i - 1L
            r
    member this.rfind (s: Str) : uint64 = this.rfind(s, Str.npos)
    member this.rfind (s: CPtr, pos: uint64) : uint64 = this.rfind(Str(s), pos)
    member this.rfind (s: CPtr) : uint64 = this.rfind(Str(s), Str.npos)
    member this.rfind (ch: int, pos: uint64) : uint64 =
        if this.n = 0 then Str.npos
        else
            let mutable i = int64 (Math.Min(pos, uint64 (this.n - 1)))
            let mutable r = Str.npos
            while r = Str.npos && i >= 0L do
                if this.b.[int i] = byte ch then r <- uint64 i
                i <- i - 1L
            r
    member this.rfind (ch: int) : uint64 = this.rfind(ch, Str.npos)
    member private this.Scan (pos: uint64, test: byte -> bool) : uint64 =
        let mutable i = int64 pos
        let mutable r = Str.npos
        while r = Str.npos && i < int64 this.n do
            if test this.b.[int i] then r <- uint64 i
            i <- i + 1L
        r
    member private this.ScanBack (pos: uint64, test: byte -> bool) : uint64 =
        if this.n = 0 then Str.npos
        else
            let mutable i = int64 (Math.Min(pos, uint64 (this.n - 1)))
            let mutable r = Str.npos
            while r = Str.npos && i >= 0L do
                if test this.b.[int i] then r <- uint64 i
                i <- i - 1L
            r
    member this.find_first_of (set: Str, pos: uint64) : uint64 = this.Scan(pos, fun c -> Str.In(c, set))
    member this.find_first_of (set: Str) : uint64 = this.find_first_of(set, 0UL)
    member this.find_first_of (set: CPtr, pos: uint64) : uint64 = this.find_first_of(Str(set), pos)
    member this.find_first_of (set: CPtr) : uint64 = this.find_first_of(Str(set), 0UL)
    member this.find_first_of (ch: int, pos: uint64) : uint64 = this.find(ch, pos)
    member this.find_first_of (ch: int) : uint64 = this.find(ch, 0UL)
    member this.find_first_not_of (set: Str, pos: uint64) : uint64 = this.Scan(pos, fun c -> not (Str.In(c, set)))
    member this.find_first_not_of (set: Str) : uint64 = this.find_first_not_of(set, 0UL)
    member this.find_first_not_of (set: CPtr, pos: uint64) : uint64 = this.find_first_not_of(Str(set), pos)
    member this.find_first_not_of (set: CPtr) : uint64 = this.find_first_not_of(Str(set), 0UL)
    member this.find_first_not_of (ch: int, pos: uint64) : uint64 = this.Scan(pos, fun c -> c <> byte ch)
    member this.find_first_not_of (ch: int) : uint64 = this.find_first_not_of(ch, 0UL)
    member this.find_last_of (set: Str, pos: uint64) : uint64 = this.ScanBack(pos, fun c -> Str.In(c, set))
    member this.find_last_of (set: Str) : uint64 = this.find_last_of(set, Str.npos)
    member this.find_last_of (set: CPtr, pos: uint64) : uint64 = this.find_last_of(Str(set), pos)
    member this.find_last_of (set: CPtr) : uint64 = this.find_last_of(Str(set), Str.npos)
    member this.find_last_of (ch: int, pos: uint64) : uint64 = this.rfind(ch, pos)
    member this.find_last_of (ch: int) : uint64 = this.rfind(ch, Str.npos)
    member this.find_last_not_of (set: Str, pos: uint64) : uint64 = this.ScanBack(pos, fun c -> not (Str.In(c, set)))
    member this.find_last_not_of (set: Str) : uint64 = this.find_last_not_of(set, Str.npos)
    member this.find_last_not_of (set: CPtr, pos: uint64) : uint64 = this.find_last_not_of(Str(set), pos)
    member this.find_last_not_of (set: CPtr) : uint64 = this.find_last_not_of(Str(set), Str.npos)
    member this.find_last_not_of (ch: int, pos: uint64) : uint64 = this.ScanBack(pos, fun c -> c <> byte ch)
    member this.find_last_not_of (ch: int) : uint64 = this.find_last_not_of(ch, Str.npos)

    member this.starts_with (s: Str) : bool =
        if s.n > this.n then false
        else
            let mutable r = true
            let mutable i = 0
            while r && i < s.n do
                if this.b.[i] <> s.b.[i] then r <- false
                i <- i + 1
            r
    member this.starts_with (s: CPtr) : bool = this.starts_with(Str(s))
    member this.starts_with (ch: int) : bool = this.n > 0 && this.b.[0] = byte ch
    member this.ends_with (s: Str) : bool =
        if s.n > this.n then false
        else
            let mutable r = true
            let mutable i = 0
            while r && i < s.n do
                if this.b.[this.n - s.n + i] <> s.b.[i] then r <- false
                i <- i + 1
            r
    member this.ends_with (s: CPtr) : bool = this.ends_with(Str(s))
    member this.ends_with (ch: int) : bool = this.n > 0 && this.b.[this.n - 1] = byte ch
    member this.contains (s: Str) : bool = this.find(s) <> Str.npos
    member this.contains (ch: int) : bool = this.find(ch) <> Str.npos

    /// char_traits<char>::compare is memcmp: unsigned bytes, then length.
    member this.compare (o: Str) : int =
        let m = Math.Min(this.n, o.n)
        let mutable r = 0
        let mutable i = 0
        while r = 0 && i < m do
            if this.b.[i] <> o.b.[i] then r <- (if this.b.[i] < o.b.[i] then -1 else 1)
            i <- i + 1
        if r <> 0 then r
        else
            let d = int64 this.n - int64 o.n
            if d > int64 Int32.MaxValue then Int32.MaxValue elif d < int64 Int32.MinValue then Int32.MinValue else int d
    member this.compare (o: CPtr) : int = this.compare(Str(o))
    member this.compare (pos: uint64, len: uint64, o: Str) : int = this.substr(pos, len).compare(o)
    member this.CompareTo (o: Str) : int = this.compare(o)
    member this.Equals (o: Str) : bool =
        if isNull o || o.n <> this.n then false
        else
            let mutable r = true
            let mutable i = 0
            while r && i < this.n do
                if this.b.[i] <> o.b.[i] then r <- false
                i <- i + 1
            r
    override this.Equals (o: obj) = match o with :? Str as s -> this.Equals(s) | _ -> false
    override this.GetHashCode () = (let mutable h = this.n in (for i in 0 .. this.n - 1 do h <- h * 31 + int this.b.[i]); h)
    member this.EqLit (p: CPtr) : bool =
        let m = int (R.strlen(p))
        if m <> this.n then false
        else
            let mutable r = true
            let mutable i = 0
            while r && i < this.n do
                if this.b.[i] <> p.B.[p.O + i] then r <- false
                i <- i + 1
            r

    static member Cat (x: Str, y: Str) : Str = let r = Str(x.b, 0, x.n) in r.append(y) |> ignore; r
    static member Cat (x: Str, y: CPtr) : Str = let r = Str(x.b, 0, x.n) in r.append(y) |> ignore; r
    static member Cat (x: CPtr, y: Str) : Str = let r = Str(x) in r.append(y) |> ignore; r
    static member Cat (x: Str, ch: int) : Str = let r = Str(x.b, 0, x.n) in r.push_back(ch); r
    static member Cat (ch: int, y: Str) : Str = let r = Str(1UL, ch) in r.append(y) |> ignore; r

    member this.Bytes () : byte[] = let r : byte[] = Array.zeroCreate this.n in Array.Copy(this.b, r, this.n); r
    override this.ToString () = Encoding.Latin1.GetString(this.b, 0, this.n)

    member this.``begin`` () = Iter<byte>(this.b, 0L)
    member this.``end`` () = Iter<byte>(this.b, int64 this.n)

    // ---- std::to_string / std::stod and friends (libstdc++: to_string(double) is "%f") ----
    static member to_string (v: int) : Str = Str(v.ToString(CultureInfo.InvariantCulture))
    static member to_string (v: int64) : Str = Str(v.ToString(CultureInfo.InvariantCulture))
    static member to_string (v: uint64) : Str = Str(v.ToString(CultureInfo.InvariantCulture))
    static member to_string (v: uint32) : Str = Str(v.ToString(CultureInfo.InvariantCulture))
    static member to_string (v: float) : Str = Str.FromBytes(R.Format(R.Lit("%f"), [| box v |]))
    static member to_string (v: float32) : Str = Str.FromBytes(R.Format(R.Lit("%f"), [| box (float v) |]))

    /// std::stod (libstdc++'s __stoa over strtod): invalid_argument("stod") when nothing converts, out_of_range("stod")
    /// when strtod sets ERANGE (overflow, or an underflow: "1e999", "1e-400", and a subnormal that is not exact, such
    /// as 2.2250738585072011e-308).
    static member stod (s: Str, idx: Ptr<uint64>) : float =
        let p = s.c_str ()
        let mutable e = p
        let v = R.strtod(p, &e)
        if e.O = p.O then raise (InvalidArgument("stod"))
        if R.ERange then raise (OutOfRange("stod"))
        if not idx.IsNull then idx.Deref <- uint64 (e.O - p.O)
        v
    static member stod (s: Str) : float = Str.stod(s, Unchecked.defaultof<Ptr<uint64>>)
    static member stoi (s: Str, idx: Ptr<uint64>, bas: int) : int =
        let p = s.c_str ()
        let mutable e = p
        let v = R.strtol(p, &e, bas)
        if e.O = p.O then raise (InvalidArgument("stoi"))
        if v < int64 Int32.MinValue || v > int64 Int32.MaxValue then raise (OutOfRange("stoi"))
        if not idx.IsNull then idx.Deref <- uint64 (e.O - p.O)
        int v
    static member stoi (s: Str, idx: Ptr<uint64>) : int = Str.stoi(s, idx, 10)
    static member stoi (s: Str) : int = Str.stoi(s, Unchecked.defaultof<Ptr<uint64>>, 10)
    static member stol (s: Str, idx: Ptr<uint64>, bas: int) : int64 =
        let p = s.c_str ()
        let mutable e = p
        let v = R.strtol(p, &e, bas)
        if e.O = p.O then raise (InvalidArgument("stol"))
        if not idx.IsNull then idx.Deref <- uint64 (e.O - p.O)
        v
    static member stol (s: Str, idx: Ptr<uint64>) : int64 = Str.stol(s, idx, 10)
    static member stol (s: Str) : int64 = Str.stol(s, Unchecked.defaultof<Ptr<uint64>>, 10)

    interface ICopy<Str> with
        member this.Clone () = this.Clone()
        member this.Assign (o: Str) = this.Assign(o)
    interface IEquatable<Str> with
        member this.Equals (o: Str) = this.Equals(o)
    interface IComparable<Str> with
        member this.CompareTo (o: Str) = this.compare(o)

// =================================================================================================== Streams.cs
// The iostreams the engine uses, with libstdc++'s formatting: a double is written with the printf format libstdc++'s
// num_put builds from the stream flags (%.*g by default, %.*f for fixed, %.*e for scientific, '#' for showpoint),
// through the glibc-exact formatter; width and fill are applied afterwards and width resets. Reading numbers goes
// through strtod / strtol as libstdc++'s num_get does for ordinary input.
[<AbstractClass; Sealed>]
type Ios =
    // the libstdc++ values of ios_base::openmode, seekdir, fmtflags and iostate
    static member ``in`` = 8
    static member out = 16
    static member binary = 4
    static member app = 1
    static member trunc = 32
    static member ate = 2
    static member beg = 0
    static member cur = 1
    static member ``end`` = 2
    static member boolalpha = 1
    static member dec = 2
    static member ``fixed`` = 4
    static member hex = 8
    static member ``internal`` = 16
    static member left = 32
    static member oct = 64
    static member right = 128
    static member scientific = 256
    static member showbase = 512
    static member showpoint = 1024
    static member showpos = 2048
    static member skipws = 4096
    static member unitbuf = 8192
    static member uppercase = 16384
    static member adjustfield = 176
    static member basefield = 74
    static member floatfield = 260
    static member goodbit = 0
    static member badbit = 1
    static member eofbit = 2
    static member failbit = 4

[<Sealed; AllowNullLiteral>]
type SetPrec(n0: int) = member this.n = n0
[<Sealed; AllowNullLiteral>]
type SetW(n0: int) = member this.n = n0
[<Sealed; AllowNullLiteral>]
type SetFill(c0: int) = member this.c = c0

[<AllowNullLiteral>]
type IosBase =
    [<DefaultValue(false)>] val mutable failbit : bool
    [<DefaultValue(false)>] val mutable eofbit : bool
    [<DefaultValue(false)>] val mutable badbit : bool
    [<DefaultValue(false)>] val mutable prec : int64
    [<DefaultValue(false)>] val mutable flagsv : int
    [<DefaultValue(false)>] val mutable widthv : int64
    [<DefaultValue(false)>] val mutable fillc : int
    new () as this = { } then this.prec <- 6L; this.fillc <- int ' '
    member this.good () = not this.failbit && not this.eofbit && not this.badbit
    member this.fail () = this.failbit || this.badbit
    member this.bad () = this.badbit
    member this.eof () = this.eofbit
    member this.ok () = not (this.fail ())
    member this.notok () = this.fail ()
    member this.clear (s: int) = this.failbit <- false; this.eofbit <- false; this.badbit <- false
    member this.clear () = this.clear(0)
    member this.precision () : int64 = this.prec
    member this.precision (p: int64) : int64 = let o = this.prec in this.prec <- p; o
    member this.width () : int64 = this.widthv
    member this.width (w: int64) : int64 = let o = this.widthv in this.widthv <- w; o
    member this.fill () : int = this.fillc
    member this.fill (c: int) : int = let o = this.fillc in this.fillc <- c; o
    member this.setf (f: int) : int = let o = this.flagsv in this.flagsv <- this.flagsv ||| f; o
    member this.setf (f: int, mask: int) : int = let o = this.flagsv in this.flagsv <- (this.flagsv &&& ~~~mask) ||| (f &&& mask); o
    member this.unsetf (f: int) = this.flagsv <- this.flagsv &&& ~~~f
    member this.rdbuf () : obj = null
    member this.imbue (l: obj) = ()
    member this.exceptions (m: int) = ()

[<AllowNullLiteral>]
type OStream =
    inherit IosBase
    [<DefaultValue(false)>] val mutable s : Stream
    [<DefaultValue(false)>] val mutable mem : MemoryStream
    new () = { inherit IosBase() }
    new (s: Stream) as this = { inherit IosBase() } then this.s <- s

    abstract Raw : byte[] * int * int -> unit
    default this.Raw (b: byte[], off: int, n: int) =
        let st : Stream = if not (isNull this.s) then this.s else (this.mem :> Stream)
        if not (isNull st) then st.Write(b, off, n)
    member private this.Pad (body: byte[]) =
        let w = this.widthv
        this.widthv <- 0L
        if int64 body.Length >= w then this.Raw(body, 0, body.Length)
        else
            let pad : byte[] = Array.zeroCreate (int (w - int64 body.Length))
            for i in 0 .. pad.Length - 1 do pad.[i] <- byte this.fillc
            if (this.flagsv &&& Ios.left) <> 0 then this.Raw(body, 0, body.Length); this.Raw(pad, 0, pad.Length)
            else this.Raw(pad, 0, pad.Length); this.Raw(body, 0, body.Length)
    static member private B (x: string) : byte[] = Encoding.Latin1.GetBytes(x)

    member this.Put (x: Str) : OStream = this.Pad(x.Bytes ()); this
    member this.Put (x: CPtr) : OStream = this.Pad(R.Bytes(x)); this
    member this.Put (c: sbyte) : OStream = this.Pad([| byte c |]); this
    member this.Put (c: byte) : OStream = this.Pad([| c |]); this
    member this.Put (v: int) : OStream = this.Pad(OStream.B(v.ToString(CultureInfo.InvariantCulture))); this
    member this.Put (v: int64) : OStream = this.Pad(OStream.B(v.ToString(CultureInfo.InvariantCulture))); this
    member this.Put (v: uint32) : OStream = this.Pad(OStream.B(v.ToString(CultureInfo.InvariantCulture))); this
    member this.Put (v: uint64) : OStream = this.Pad(OStream.B(v.ToString(CultureInfo.InvariantCulture))); this
    member this.Put (v: int16) : OStream = this.Put(int v)
    member this.Put (v: bool) : OStream = this.Pad(OStream.B(if (this.flagsv &&& Ios.boolalpha) <> 0 then (if v then "true" else "false") else (if v then "1" else "0"))); this
    member this.Put (v: float32) : OStream = this.Put(float v)
    member this.Put (v: float) : OStream =
        // libstdc++ __num_base::_S_format_float: the conversion letter from the floatfield, '#' for showpoint
        let fx = (this.flagsv &&& Ios.``fixed``) <> 0
        let sci = (this.flagsv &&& Ios.scientific) <> 0
        let spec = if (this.flagsv &&& Ios.showpoint) <> 0 then "%#" else "%"
        let conv = if fx && sci then 'a' elif fx then 'f' elif sci then 'e' else 'g'
        let body =
            if fx && sci then R.Format(R.Lit(spec + "a"), [| box v |])
            else R.Format(R.Lit(spec + ".*" + string conv), [| box (int this.prec); box v |])
        this.Pad(body)
        this
    member this.Put (p: SetPrec) : OStream = this.prec <- int64 p.n; this
    member this.Put (w: SetW) : OStream = this.widthv <- int64 w.n; this
    member this.Put (f: SetFill) : OStream = this.fillc <- f.c; this
    member this.Put (o: OStream) : OStream = this
    member this.Manip_endl () : OStream = this.Raw([| 10uy |], 0, 1); this.flush () |> ignore; this
    member this.Manip_flush () : OStream = this.flush () |> ignore; this
    member this.Manip_setprecision (n: int64) : OStream = this.prec <- n; this
    member this.Manip_setw (n: int64) : OStream = this.widthv <- n; this
    member this.Manip_setfill (c: int) : OStream = this.fillc <- c; this
    // std::quoted(s): s between double quotes, with every '"' and '\\' escaped by a backslash; the width applies to
    // the whole quoted string, as in libstdc++ (__ostream_insert of the built string)
    member this.Manip_quoted (x: Str) : OStream =
        let b = List<byte>()
        b.Add(byte '"')
        for c in x.Bytes () do
            if c = byte '"' || c = byte '\\' then b.Add(byte '\\')
            b.Add(c)
        b.Add(byte '"')
        this.Pad(b.ToArray())
        this
    member this.Manip_quoted (x: CPtr) : OStream = this.Manip_quoted(Str(x))
    member this.Manip_fixed () : OStream = this.flagsv <- (this.flagsv &&& ~~~(Ios.``fixed`` ||| Ios.scientific)) ||| Ios.``fixed``; this
    member this.Manip_scientific () : OStream = this.flagsv <- (this.flagsv &&& ~~~(Ios.``fixed`` ||| Ios.scientific)) ||| Ios.scientific; this
    member this.Manip_defaultfloat () : OStream = this.flagsv <- this.flagsv &&& ~~~(Ios.``fixed`` ||| Ios.scientific); this
    member this.Manip_left () : OStream = this.flagsv <- (this.flagsv &&& ~~~(Ios.left ||| Ios.right)) ||| Ios.left; this
    member this.Manip_right () : OStream = this.flagsv <- (this.flagsv &&& ~~~(Ios.left ||| Ios.right)) ||| Ios.right; this
    member this.Manip_boolalpha () : OStream = this.flagsv <- this.flagsv ||| Ios.boolalpha; this
    member this.Manip_noboolalpha () : OStream = this.flagsv <- this.flagsv &&& ~~~Ios.boolalpha; this
    member this.Manip_showpoint () : OStream = this.flagsv <- this.flagsv ||| Ios.showpoint; this
    member this.Manip_noshowpoint () : OStream = this.flagsv <- this.flagsv &&& ~~~Ios.showpoint; this
    member this.write (p: CPtr, n: int64) : OStream = (if n <> 0L then this.Raw(p.B, p.O, int n)); this
    member this.write<'T when 'T : struct and 'T : (new : unit -> 'T) and 'T :> ValueType> (p: Ptr<'T>, n: int64) : OStream =
        let b = Mem.BytesOf(p, int n)
        this.Raw(b, 0, b.Length)
        this
    abstract flush : unit -> OStream
    default this.flush () = (if not (isNull this.s) then this.s.Flush()); this
    abstract tellp : unit -> int64
    default this.tellp () = if not (isNull this.s) then this.s.Position elif not (isNull this.mem) then this.mem.Position else 0L
    member this.seekp (pos: int64, dir: int) : OStream =
        let st : Stream = if not (isNull this.s) then this.s else (this.mem :> Stream)
        if not (isNull st) then st.Seek(pos, (if dir = Ios.beg then SeekOrigin.Begin elif dir = Ios.cur then SeekOrigin.Current else SeekOrigin.End)) |> ignore
        this
    member this.seekp (pos: int64) : OStream = this.seekp(pos, Ios.beg)

[<AllowNullLiteral>]
type OStringStream =
    inherit OStream
    new () as this = { inherit OStream() } then this.mem <- new MemoryStream()
    new (init: Str) as this =
        { inherit OStream() }
        then
            this.mem <- new MemoryStream()
            let b = init.Bytes ()
            this.mem.Write(b, 0, b.Length)
            this.mem.Position <- 0L
    new (mode: int) = OStringStream()
    member this.str () : Str = Str.FromBytes(this.mem.ToArray())
    member this.str (x: Str) = this.mem <- new MemoryStream(); let b = x.Bytes () in this.mem.Write(b, 0, b.Length)
    member this.MoveFrom (o: OStringStream) = this.mem <- o.mem; this.prec <- o.prec; this.flagsv <- o.flagsv; o.mem <- new MemoryStream()

[<AllowNullLiteral>]
type OFStream =
    inherit OStream
    new () = { inherit OStream() }
    new (path: Str, mode: int) as this = { inherit OStream() } then this.``open``(path, mode)
    new (path: Str) = new OFStream(path, Ios.out)
    new (path: CPtr, mode: int) as this = { inherit OStream() } then this.``open``(Str(path), mode)
    new (path: CPtr) = new OFStream(path, Ios.out)
    new (path: FsPath, mode: int) as this = { inherit OStream() } then this.``open``(path.p, mode)
    new (path: FsPath) = new OFStream(path, Ios.out)
    member this.``open`` (path: Str, mode: int) =
        try
            let fm = if (mode &&& Ios.app) <> 0 then FileMode.Append else FileMode.Create
            this.s <- new FileStream(path.ToString(), fm, FileAccess.Write, FileShare.ReadWrite, 1 <<< 16)
            this.failbit <- false
        with _ -> this.s <- null; this.failbit <- true
    member this.``open`` (path: Str) = this.``open``(path, Ios.out)
    member this.``open`` (path: FsPath, mode: int) = this.``open``(path.p, mode)
    member this.``open`` (path: FsPath) = this.``open``(path.p, Ios.out)
    member this.is_open () : bool = not (isNull this.s)
    member this.close () =
        if not (isNull this.s) then
            this.s.Flush()
            this.s.Dispose()
        this.s <- null
    member this.Dispose () = this.close ()      // ~basic_ofstream closes the file
    member this.MoveFrom (o: OFStream) = this.s <- o.s; o.s <- null
    interface IDisposable with
        member this.Dispose () = this.close ()

[<AllowNullLiteral>]
type IStream =
    inherit IosBase
    [<DefaultValue(false)>] val mutable buf : byte[]
    [<DefaultValue(false)>] val mutable pos : int
    [<DefaultValue(false)>] val mutable len : int
    [<DefaultValue(false)>] val mutable src : Stream
    [<DefaultValue(false)>] val mutable gcountv : int64
    /// An ifstream opened on a directory: libstdc++ opens it (open(2) of a directory for reading succeeds) and the
    /// first read fails with EISDIR, which basic_filebuf::underflow throws as ios_base::failure and the extraction
    /// catches: badbit and failbit, not eofbit.
    [<DefaultValue(false)>] val mutable dir : bool
    new () as this = { inherit IosBase() } then this.buf <- Array.Empty<byte>()

    member this.Fill () : bool =
        if this.pos < this.len then true
        elif this.dir then this.badbit <- true; this.failbit <- true; false
        elif isNull this.src then false
        else
            if this.buf.Length = 0 then this.buf <- Array.zeroCreate (1 <<< 16)
            this.len <- this.src.Read(this.buf, 0, this.buf.Length)
            this.pos <- 0
            this.len > 0
    /// A read found no more input: the end of the input sets eofbit; a failed read (dir) set badbit instead.
    member this.AtEnd () = if not this.dir then this.eofbit <- true
    member this.peek () : int = if not (this.Fill ()) then (this.AtEnd (); -1) else int this.buf.[this.pos]
    member this.get () : int =
        if not (this.Fill ()) then
            this.AtEnd ()
            this.failbit <- true
            -1
        else
            this.gcountv <- 1L
            let c = int this.buf.[this.pos]
            this.pos <- this.pos + 1
            c
    member this.get (c: byref<sbyte>) : IStream = (let x = this.get () in if x >= 0 then c <- sbyte x); this
    member this.ignore (n: int64, delim: int) : IStream =
        let mutable i = 0L
        let mutable go = true
        while go && i < n do
            let c = this.get ()
            if c < 0 || c = delim then go <- false
            i <- i + 1L
        this.failbit <- this.failbit && not this.eofbit
        this
    member this.ignore (n: int64) : IStream = this.ignore(n, -1)
    member this.ignore () : IStream = this.ignore(1L, -1)
    member this.gcount () : int64 = this.gcountv

    member this.read (p: CPtr, n: int64) : IStream =
        let mutable got = 0L
        let mutable go = true
        while go && got < n do
            if not (this.Fill ()) then go <- false
            else
                let k = int (Math.Min(n - got, int64 (this.len - this.pos)))
                Array.Copy(this.buf, int64 this.pos, p.B, int64 p.O + got, int64 k)
                this.pos <- this.pos + k
                got <- got + int64 k
        this.gcountv <- got
        if got < n then
            this.AtEnd ()
            this.failbit <- true
        this
    member this.read<'T when 'T : struct and 'T : (new : unit -> 'T) and 'T :> ValueType> (p: Ptr<'T>, n: int64) : IStream =
        let tmp : byte[] = Array.zeroCreate (int n)
        this.read(CPtr(tmp, 0), n) |> ignore
        Mem.BytesInto(p, tmp, 0, int this.gcountv)
        this

    member this.ReadLine (s: Str, delim: int) : bool =
        s.clear ()
        let mutable any = false
        let mutable result = None
        while result.IsNone do
            if not (this.Fill ()) then
                this.AtEnd ()
                if not any then this.failbit <- true
                result <- Some any
            else
                let c = int this.buf.[this.pos]
                this.pos <- this.pos + 1
                any <- true
                if c = delim then result <- Some true
                else s.push_back(c)
        result.Value

    static member private IsWs (c: byte) = c = byte ' ' || c = byte '\t' || c = byte '\n' || c = byte '\r' || c = 11uy || c = 12uy
    member private this.SkipWs () = while this.Fill () && IStream.IsWs(this.buf.[this.pos]) do this.pos <- this.pos + 1
    member private this.Token () : Str =
        this.SkipWs ()
        let t = Str()
        while this.Fill () && not (IStream.IsWs(this.buf.[this.pos])) do
            t.push_back(int this.buf.[this.pos])
            this.pos <- this.pos + 1
        if t.n = 0 then
            this.failbit <- true
            if not (this.Fill ()) then this.AtEnd ()
        elif not (this.Fill ()) then this.AtEnd ()
        t
    member this.Get (s: Str) : IStream = (let t = this.Token () in if t.n > 0 then s.Assign(t)); this
    /// is >> std::ws: skip whitespace (eof is set if the input ends there, failbit is not).
    member this.Manip_ws () : IStream =
        let mutable go = true
        while go do
            if not (this.Fill ()) then this.AtEnd (); go <- false
            else
                let c = int this.buf.[this.pos]
                if c = int ' ' || (c >= 9 && c <= 13) then this.pos <- this.pos + 1 else go <- false
        this
    /// is >> v for a double, as libstdc++ does it: the sentry skips white space (at the end of the input: eof and
    /// fail, v unchanged); num_get's _M_extract_float takes the characters a C-locale number can have (a sign, one of
    /// any leading zeros, digits, one '.', and an 'e' after a digit with its sign), stopping at the first other one,
    /// which stays in the stream ("0x10" is read as 0, then x10 is left); and the gnu __convert_to_v converts them with
    /// strtod: not all of them converted (nothing, "1e", "inf") is failbit with v = 0, and an overflow (strtod's +-inf)
    /// is failbit with v = +-DBL_MAX. An underflow is no error. eofbit when the number ends the input. (C#'s local
    /// function Next, which assigns the locals more and c, is written out at each use.)
    member this.Get (v: byref<float>) : IStream =
        if not (this.good ()) then this.failbit <- true     // the sentry: a stream not good extracts nothing
        else
            this.SkipWs ()
            if not (this.Fill ()) then
                this.AtEnd ()
                this.failbit <- true
            else
                let acc = List<byte>(32)
                let mutable more = true
                let mutable c = int this.buf.[this.pos]
                if c = int '+' || c = int '-' then
                    acc.Add(byte c)
                    this.pos <- this.pos + 1; more <- this.Fill (); c <- (if more then int this.buf.[this.pos] else -1)
                let mutable mantissa = false
                let mutable dec = false
                let mutable sci = false
                while more && c = int '0' do
                    if not mantissa then acc.Add(byte '0'); mantissa <- true
                    this.pos <- this.pos + 1; more <- this.Fill (); c <- (if more then int this.buf.[this.pos] else -1)
                let mutable go = true
                while go && more do
                    let mutable advance = true
                    if c >= int '0' && c <= int '9' then acc.Add(byte c); mantissa <- true
                    elif c = int '.' && not dec && not sci then acc.Add(byte '.'); dec <- true
                    elif (c = int 'e' || c = int 'E') && not sci && mantissa then
                        acc.Add(byte 'e')
                        sci <- true
                        this.pos <- this.pos + 1; more <- this.Fill (); c <- (if more then int this.buf.[this.pos] else -1)
                        if not more then go <- false; advance <- false
                        elif c = int '+' || c = int '-' then acc.Add(byte c)
                        else advance <- false              // look at this character again
                    else go <- false; advance <- false
                    if advance then
                        this.pos <- this.pos + 1; more <- this.Fill (); c <- (if more then int this.buf.[this.pos] else -1)
                if not more then this.AtEnd ()
                acc.Add(0uy)
                let a = acc.ToArray()
                let mutable r = 0.0
                let n = R.ScanDouble(a, 0, Int32.MaxValue, &r, false)
                if n = 0 || a.[n] <> 0uy then v <- 0.0; this.failbit <- true
                elif Double.IsPositiveInfinity(r) then v <- Double.MaxValue; this.failbit <- true
                elif Double.IsNegativeInfinity(r) then v <- -Double.MaxValue; this.failbit <- true
                else v <- r
        this
    member this.Get (v: byref<float32>) : IStream = (let mutable d = 0.0 in this.Get(&d) |> ignore; v <- float32 d); this
    member this.Get (v: byref<int>) : IStream =
        let t = this.Token ()
        if t.n = 0 then v <- 0
        else
            let p = t.c_str ()
            let mutable e = p
            let r = R.strtol(p, &e, 10)
            if e.O <> t.n || r < int64 Int32.MinValue || r > int64 Int32.MaxValue then
                this.failbit <- true
                v <- (if r < int64 Int32.MinValue then Int32.MinValue elif r > int64 Int32.MaxValue then Int32.MaxValue else 0)
            else v <- int r
        this
    member this.Get (v: byref<int64>) : IStream =
        let t = this.Token ()
        if t.n = 0 then v <- 0L
        else
            let p = t.c_str ()
            let mutable e = p
            let r = R.strtol(p, &e, 10)
            if e.O <> t.n then this.failbit <- true; v <- 0L
            else v <- r
        this
    member this.Get (v: byref<uint64>) : IStream = (let mutable x = 0L in this.Get(&x) |> ignore; v <- uint64 x); this
    member this.Get (v: byref<uint32>) : IStream = (let mutable x = 0L in this.Get(&x) |> ignore; v <- uint32 x); this
    member this.Get (c: byref<sbyte>) : IStream = (this.SkipWs (); let x = this.get () in if x >= 0 then c <- sbyte x); this
    member this.tellg () : int64 = if not (isNull this.src) then this.src.Position - int64 (this.len - this.pos) else int64 this.pos
    member this.seekg (off: int64, dir: int) : IStream =
        this.eofbit <- false
        if not (isNull this.src) then
            // ios::cur is relative to the read position, not to the stream's, which is past the buffered block
            let np = if dir = Ios.beg then off elif dir = Ios.cur then this.tellg () + off else this.src.Length + off
            if np < 0L then this.failbit <- true
            else
                this.src.Seek(np, SeekOrigin.Begin) |> ignore
                this.pos <- 0
                this.len <- 0
        else this.pos <- int (if dir = Ios.beg then off elif dir = Ios.cur then int64 this.pos + off else int64 this.len + off)
        this
    member this.seekg (off: int64) : IStream = this.seekg(off, Ios.beg)

[<AllowNullLiteral>]
type IStringStream =
    inherit IStream
    new () = { inherit IStream() }
    new (s: Str) as this = { inherit IStream() } then this.str(s)
    new (s: Str, mode: int) as this = { inherit IStream() } then this.str(s)
    member this.str (s: Str) = this.buf <- s.Bytes (); this.pos <- 0; this.len <- this.buf.Length; this.failbit <- false; this.eofbit <- false
    member this.str () : Str = Str(this.buf, 0, this.len)
    member this.MoveFrom (o: IStringStream) = this.buf <- o.buf; this.pos <- o.pos; this.len <- o.len

[<AllowNullLiteral>]
type IFStream =
    inherit IStream
    new () = { inherit IStream() }
    new (path: Str, mode: int) as this = { inherit IStream() } then this.``open``(path, mode)
    new (path: Str) = new IFStream(path, Ios.``in``)
    new (path: CPtr, mode: int) as this = { inherit IStream() } then this.``open``(Str(path), mode)
    new (path: CPtr) = new IFStream(path, Ios.``in``)
    new (path: FsPath, mode: int) as this = { inherit IStream() } then this.``open``(path.p, mode)
    new (path: FsPath) = new IFStream(path, Ios.``in``)
    member this.``open`` (path: Str, mode: int) =
        this.dir <- false
        try
            if Directory.Exists(path.ToString()) then
                // libstdc++ opens a directory for reading; every read then fails (see IStream.dir)
                this.src <- Stream.Null
                this.dir <- true
                this.failbit <- false
                this.pos <- 0
                this.len <- 0
            else
                this.src <- new FileStream(path.ToString(), FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 <<< 16)
                this.failbit <- false
                this.pos <- 0
                this.len <- 0
        with _ -> this.src <- null; this.failbit <- true
    member this.``open`` (path: Str) = this.``open``(path, Ios.``in``)
    member this.``open`` (path: FsPath, mode: int) = this.``open``(path.p, mode)
    member this.``open`` (path: FsPath) = this.``open``(path.p, Ios.``in``)
    member this.is_open () : bool = not (isNull this.src)
    member this.close () = (if not (isNull this.src) then this.src.Dispose()); this.src <- null; this.dir <- false
    member this.Dispose () = this.close ()
    member this.MoveFrom (o: IFStream) =
        this.src <- o.src; this.buf <- o.buf; this.pos <- o.pos; this.len <- o.len; this.dir <- o.dir
        o.src <- null; o.dir <- false
    interface IDisposable with
        member this.Dispose () = this.close ()

[<AllowNullLiteral>]
type FStream =
    inherit OStream
    new (path: Str, mode: int) as this =
        { inherit OStream() }
        then
            try this.s <- new FileStream(path.ToString(), FileMode.OpenOrCreate, FileAccess.ReadWrite)
            with _ -> this.failbit <- true
    member this.is_open () : bool = not (isNull this.s)
    member this.close () = (if not (isNull this.s) then this.s.Dispose()); this.s <- null
    member this.Dispose () = this.close ()
    interface IDisposable with
        member this.Dispose () = this.close ()

[<AbstractClass; Sealed>]
type Streams =
    [<DefaultValue>] static val mutable private cout0 : OStream
    [<DefaultValue>] static val mutable private cerr0 : OStream
    static member cout with get () : OStream = (if isNull Streams.cout0 then Streams.cout0 <- OStream(Console.OpenStandardOutput())); Streams.cout0
    static member cerr with get () : OStream = (if isNull Streams.cerr0 then Streams.cerr0 <- OStream(Console.OpenStandardError())); Streams.cerr0
    static member clog with get () : OStream = Streams.cerr
    static member getline (s: IStream, line: Str) : IStream = s.ReadLine(line, int '\n') |> ignore; s
    static member getline (s: IStream, line: Str, delim: sbyte) : IStream = s.ReadLine(line, int (byte delim)) |> ignore; s
    static member getline (s: IStream, line: Str, delim: int) : IStream = s.ReadLine(line, int (byte delim)) |> ignore; s
    static member setprecision (n: int) = SetPrec(n)
    static member setw (n: int) = SetW(n)
    static member setfill (c: int) = SetFill(c)

// =================================================================================================== Fs.cs
// std::filesystem::path and the filesystem functions the engine calls (POSIX paths, as libstdc++ on Linux). (The C
// library functions of Fs.cs are in R below.)
[<Sealed; AllowNullLiteral>]
type FsPath =
    [<DefaultValue(false)>] val mutable p : Str
    new () as this = { } then this.p <- Str()
    new (s: Str) as this = { } then this.p <- s.Clone()
    new (s: CPtr) as this = { } then this.p <- Str(s)
    new (s: string) as this = { } then this.p <- Str(s)
    member this.Clone () : FsPath = FsPath(this.p)
    member this.clear () = this.p <- Str()
    member this.Assign (o: FsPath) = this.p <- o.p.Clone()
    member this.Assign (s: Str) = this.p <- s.Clone()
    member this.MoveFrom (o: FsPath) = this.p <- o.p; o.p <- Str()
    member this.CompareTo (o: FsPath) : int = this.p.compare(o.p)
    member this.compare (o: FsPath) : int = this.p.compare(o.p)
    member private this.S : string = this.p.ToString()

    member this.``string`` () : Str = this.p.Clone()
    member this.generic_string () : Str = this.p.Clone()
    member this.native () : Str = this.p.Clone()
    member this.c_str () : CPtr = this.p.c_str ()
    member this.u8string () : Str = this.p.Clone()
    member this.empty () : bool = this.p.n = 0
    member this.is_absolute () : bool = this.p.n > 0 && this.p.b.[0] = byte '/'
    member this.is_relative () : bool = not (this.is_absolute ())
    member this.has_filename () : bool = (this.filename ()).p.n > 0
    member this.has_parent_path () : bool = (this.parent_path ()).p.n > 0
    member this.has_extension () : bool = (this.extension ()).p.n > 0

    /// The last element (libstdc++: the part after the last '/'; "" when the path ends in '/').
    member this.filename () : FsPath =
        let s = this.S
        let i = s.LastIndexOf('/')
        FsPath(if i < 0 then s else s.Substring(i + 1))
    member this.parent_path () : FsPath =
        let s = this.S
        let i = s.LastIndexOf('/')
        if i < 0 then FsPath("")
        elif i = 0 then FsPath("/")
        else
            let mutable j = i
            while j > 0 && s.[j - 1] = '/' do j <- j - 1
            FsPath(s.Substring(0, (if j = 0 then 1 else j)))
    member this.stem () : FsPath =
        let f = (this.filename ()).S
        if f = "." || f = ".." then FsPath(f)
        else
            let i = f.LastIndexOf('.')
            FsPath(if i <= 0 then f else f.Substring(0, i))
    member this.extension () : FsPath =
        let f = (this.filename ()).S
        if f = "." || f = ".." then FsPath("")
        else
            let i = f.LastIndexOf('.')
            FsPath(if i <= 0 then "" else f.Substring(i))
    member this.replace_extension (ext: FsPath) : FsPath =
        let e = (this.extension ()).S
        let mutable s = this.S.Substring(0, this.S.Length - e.Length)
        if not (isNull ext) && ext.p.n > 0 then s <- s + (if ext.p.b.[0] = byte '.' then "" else ".") + ext.S
        this.p <- Str(s)
        this
    member this.replace_extension () : FsPath = this.replace_extension(null: FsPath)
    member this.replace_extension (ext: Str) : FsPath = this.replace_extension(FsPath(ext))
    member this.replace_filename (f: FsPath) : FsPath = let par = this.parent_path () in this.p <- par.Join(f).p; this
    member this.remove_filename () : FsPath =
        let s = this.S
        let i = s.LastIndexOf('/')
        this.p <- Str(if i < 0 then "" else s.Substring(0, i + 1))
        this

    /// operator/ : an absolute right side replaces the path; otherwise a separator is added.
    member this.Join (o: FsPath) : FsPath =
        if o.is_absolute () then o.Clone()
        else
            let mutable s = this.S
            if s.Length = 0 then o.Clone()
            else
                if not (s.EndsWith("/")) && o.p.n > 0 then s <- s + "/"
                FsPath(s + o.S)
    member this.Join (o: Str) : FsPath = this.Join(FsPath(o))
    member this.Join (o: CPtr) : FsPath = this.Join(FsPath(o))

    member this.lexically_normal () : FsPath =
        let s = this.S
        if s.Length = 0 then FsPath("")
        else
            let abs = s.StartsWith("/")
            let parts = List<string>()
            for x in s.Split('/') do
                if x = "" || x = "." then ()
                elif x = ".." && parts.Count > 0 && parts.[parts.Count - 1] <> ".." then parts.RemoveAt(parts.Count - 1)
                elif x = ".." && abs then ()
                else parts.Add(x)
            let mutable r = (if abs then "/" else "") + String.Join("/", parts)
            if s.EndsWith("/") && parts.Count > 0 then r <- r + "/"
            if r = "" then r <- "."
            FsPath(r)
    override this.ToString () = this.S
    interface ICopy<FsPath> with
        member this.Clone () = this.Clone()
        member this.Assign (o: FsPath) = this.Assign(o)
    interface IComparable<FsPath> with
        member this.CompareTo (o) = this.CompareTo(o)

[<AbstractClass; Sealed>]
type Fs =
    static member exists (p: FsPath) : bool = File.Exists(p.ToString()) || Directory.Exists(p.ToString())
    static member exists (p: FsPath, ec: ErrorCode) : bool = Fs.exists(p)
    static member is_regular_file (p: FsPath) : bool = File.Exists(p.ToString())
    static member is_directory (p: FsPath) : bool = Directory.Exists(p.ToString())
    static member file_size (p: FsPath) : uint64 = uint64 (FileInfo(p.ToString()).Length)
    /// file_size(p, ec): libstdc++ sets ENOENT for a missing file, EISDIR for a directory and returns
    /// static_cast<uintmax_t>(-1) on error.
    static member file_size (p: FsPath, ec: ErrorCode) : uint64 =
        let s = p.ToString()
        if File.Exists(s) then ec.v <- 0; uint64 (FileInfo(s).Length)
        else
            ec.v <- (if Directory.Exists(s) then 21 else 2)
            UInt64.MaxValue
    static member remove (p: FsPath) : bool =
        try
            if File.Exists(p.ToString()) then File.Delete(p.ToString()); true else false
        with _ -> false
    static member create_directories (p: FsPath) : bool = try Directory.CreateDirectory(p.ToString()) |> ignore; true with _ -> false
    static member current_path () : FsPath = FsPath(Directory.GetCurrentDirectory())
    static member absolute (p: FsPath) : FsPath = if p.is_absolute () then p.Clone() else (Fs.current_path ()).Join(p)
    static member canonical (p: FsPath) : FsPath = FsPath(Path.GetFullPath(p.ToString()))
    static member weakly_canonical (p: FsPath) : FsPath = FsPath(Path.GetFullPath(p.ToString()))
    static member temp_directory_path () : FsPath = FsPath(match Environment.GetEnvironmentVariable("TMPDIR") with null -> "/tmp" | t -> t)
    static member rename (a: FsPath, b: FsPath) = File.Move(a.ToString(), b.ToString(), true)
    static member equivalent (a: FsPath, b: FsPath) : bool = Path.GetFullPath(a.ToString()) = Path.GetFullPath(b.ToString())
    static member equivalent (a: FsPath, b: FsPath, ec: ErrorCode) : bool =
        if not (Fs.exists(a)) || not (Fs.exists(b)) then ec.v <- 2; false
        else ec.v <- 0; Fs.equivalent(a, b)
    // the error-code overloads of the checkpoint and atomic-write helpers (ENOENT = 2, EEXIST = 17)
    static member remove (p: FsPath, ec: ErrorCode) : bool =
        ec.v <- 0
        let s = p.ToString()
        try
            if File.Exists(s) then File.Delete(s); true
            elif Directory.Exists(s) then Directory.Delete(s); true
            else false
        with _ -> ec.v <- 13; false
    static member rename (a: FsPath, b: FsPath, ec: ErrorCode) =
        try
            File.Move(a.ToString(), b.ToString(), true)
            ec.v <- 0
        with
        | :? FileNotFoundException -> ec.v <- 2
        | _ -> ec.v <- 13
    static member create_hard_link (target: FsPath, link: FsPath, ec: ErrorCode) =
        if File.Exists(link.ToString()) then ec.v <- 17
        else
            try
                File.Copy(target.ToString(), link.ToString())
                ec.v <- 0
            with _ -> ec.v <- 2
    static member weakly_canonical (p: FsPath, ec: ErrorCode) : FsPath = ec.v <- 0; Fs.weakly_canonical(p)
    static member canonical (p: FsPath, ec: ErrorCode) : FsPath = if not (Fs.exists(p)) then ec.v <- 2; FsPath() else ec.v <- 0; Fs.canonical(p)
    static member status (p: FsPath) : FileStatus = FileStatus.Of(p.ToString())
    static member status (p: FsPath, ec: ErrorCode) : FileStatus = ec.v <- 0; FileStatus.Of(p.ToString())
    static member symlink_status (p: FsPath) : FileStatus = FileStatus.Of(p.ToString())
    static member symlink_status (p: FsPath, ec: ErrorCode) : FileStatus = ec.v <- 0; FileStatus.Of(p.ToString())
    static member exists (s: FileStatus) : bool = s.kind <> FileStatus.NotFound
    static member is_regular_file (s: FileStatus) : bool = s.kind = FileStatus.Regular
    static member is_directory (s: FileStatus) : bool = s.kind = FileStatus.Directory
    static member is_symlink (s: FileStatus) : bool = false
    static member create_directories (p: FsPath, ec: ErrorCode) : bool = ec.v <- 0; Fs.create_directories(p)

/// std::filesystem::file_status, as far as the engine asks: not found, a regular file or a directory.
[<Sealed; AllowNullLiteral>]
type FileStatus() =
    [<DefaultValue(false)>] val mutable kind : int
    static member NotFound = -1
    static member Regular = 1
    static member Directory = 2
    static member Other = 3
    static member Of (s: string) : FileStatus =
        let f = FileStatus()
        f.kind <- (if File.Exists(s) then FileStatus.Regular elif System.IO.Directory.Exists(s) then FileStatus.Directory else FileStatus.NotFound)
        f
    member this.``type`` () : int = this.kind
    member this.Clone () : FileStatus = let f = FileStatus() in f.kind <- this.kind; f

// =================================================================================================== CRuntime.cs
// C runtime layer, taken from the C# port of EPA SWMM 5.2.4 (swmm5-csharp) for the SWMM6 ports. Reproduces glibc
// behaviour byte for byte where SWMM's output depends on it: printf-family formatting (exact decimal expansion,
// round-half-even like glibc), strtod/sscanf parsing, NUL-terminated byte strings, stdio files, and x86-64
// float->int conversion.

/// C char* : a byte buffer plus an offset. Indexing reads/writes signed chars like x86 C.
[<Struct; CustomEquality; NoComparison; IsReadOnly>]
type CPtr =
    val B : byte[]
    val O : int
    new (b: byte[], o: int) = { B = b; O = o }
    member this.Clone () : CPtr = this
    /// A char* made from a std::vector<char> or std::array<char, N> (sbyte storage): the same bytes, since the runtime
    /// lets an sbyte[] be read as a byte[].
    static member op_Implicit (p: Ptr<sbyte>) : CPtr = CPtr(box p.a :?> byte[], int p.o)
    static member op_Implicit (p: Ptr<byte>) : CPtr = CPtr(p.a, int p.o)
    static member Null : CPtr = Unchecked.defaultof<CPtr>
    member this.IsNull : bool = isNull this.B
    member this.Item
        with get (i: int) : int = int (sbyte this.B.[this.O + i])
        and set (i: int) (value: int) = this.B.[this.O + i] <- byte value
    static member op_Addition (p: CPtr, n: int) : CPtr = CPtr(p.B, p.O + n)
    static member op_Addition (p: CPtr, n: int64) : CPtr = CPtr(p.B, p.O + int n)
    static member op_Addition (p: CPtr, n: uint64) : CPtr = CPtr(p.B, p.O + int n)
    static member op_Subtraction (p: CPtr, n: int) : CPtr = CPtr(p.B, p.O - n)
    static member op_Increment (p: CPtr) : CPtr = CPtr(p.B, p.O + 1)
    static member op_Decrement (p: CPtr) : CPtr = CPtr(p.B, p.O - 1)
    static member op_Equality (a: CPtr, b: CPtr) : bool = obj.ReferenceEquals(a.B, b.B) && (isNull a.B || a.O = b.O)
    static member op_Inequality (a: CPtr, b: CPtr) : bool = not (CPtr.op_Equality(a, b))
    static member op_LessThan (a: CPtr, b: CPtr) : bool = a.O < b.O
    static member op_GreaterThan (a: CPtr, b: CPtr) : bool = a.O > b.O
    static member op_LessThanOrEqual (a: CPtr, b: CPtr) : bool = a.O <= b.O
    static member op_GreaterThanOrEqual (a: CPtr, b: CPtr) : bool = a.O >= b.O
    static member Diff (a: CPtr, b: CPtr) : int64 = int64 (a.O - b.O)
    member this.Equals (o: CPtr) : bool = CPtr.op_Equality(this, o)
    override this.Equals (o: obj) = match o with :? CPtr as p -> CPtr.op_Equality(this, p) | _ -> false
    override this.GetHashCode () = (if isNull this.B then 0 else this.B.GetHashCode()) ^^^ this.O
    override this.ToString () = if this.IsNull then "(null)" else R.Str(this)
    interface IEquatable<CPtr> with
        member this.Equals (o: CPtr) = CPtr.op_Equality(this, o)

[<Sealed; AllowNullLiteral>]
type RefI() = [<DefaultValue(false)>] val mutable Set : Action<int>
[<Sealed; AllowNullLiteral>]
type RefL() = [<DefaultValue(false)>] val mutable Set : Action<int64>
[<Sealed; AllowNullLiteral>]
type RefD() = [<DefaultValue(false)>] val mutable Set : Action<float>
[<Sealed; AllowNullLiteral>]
type RefF() = [<DefaultValue(false)>] val mutable Set : Action<float32>

/// C FILE*
[<Sealed; AllowNullLiteral>]
type CFile =
    val mutable S : Stream
    [<DefaultValue(false)>] val mutable Eof : bool
    val IsStd : bool
    /// A directory opened with fopen(dir, "r"): glibc opens it, and every read then fails with EISDIR, which sets the
    /// error indicator, not end-of-file.
    [<DefaultValue(false)>] val mutable Dir : bool
    [<DefaultValue(false)>] val mutable Err : bool
    new (s: Stream, std: bool) as this = { S = s; IsStd = std } then if not std then lock CFile.Open (fun () -> CFile.Open.Add(this) |> ignore)
    new (s: Stream) = CFile(s, false)
    /// A read found no more data: end-of-file, or for a directory an error.
    member this.ReadFailed () = if this.Dir then this.Err <- true else this.Eof <- true
    [<DefaultValue>] static val mutable private open0 : HashSet<CFile>
    /// The files fopen opened and fclose has not closed: exit() flushes them (R.FlushAll).
    static member Open with get () : HashSet<CFile> = (if isNull CFile.open0 then CFile.open0 <- HashSet<CFile>()); CFile.open0

/// The C library: strings, numbers from text (strtod, strtol, sscanf), the printf family, stdio, time and ctype, from
/// cs/runtime/CRuntime.cs, Fs.cs and Stash.cs (C#'s partial class R).
[<AbstractClass; Sealed>]
type R =
    [<DefaultValue>] static val mutable private dummyDouble : float
    [<DefaultValue>] static val mutable private dummyInt : int
    [<DefaultValue>] static val mutable private tokNext : CPtr
    [<DefaultValue>] static val mutable private eRange : bool
    [<DefaultValue>] static val mutable private stdout0 : CFile
    [<DefaultValue>] static val mutable private stderr0 : CFile
    [<DefaultValue>] static val mutable private stdin0 : CFile
    [<DefaultValue>] static val mutable private errno0 : int
    [<DefaultValue>] static val mutable private tmpCounter : int
    [<DefaultValue>] static val mutable private tmp8_ : byte[]
    [<DefaultValue>] static val mutable private slot0 : ScanSlot
    [<DefaultValue>] static val mutable private stash : List<obj>

    static member DummyDouble with get () : float = R.dummyDouble and set (v: float) = R.dummyDouble <- v
    static member DummyInt with get () : int = R.dummyInt and set (v: int) = R.dummyInt <- v

    // ---------------------------------------------------------------- conversions (x86-64 cvttsd2si)
    static member D2I (x: float) : int = if Double.IsNaN(x) || x >= 2147483648.0 || x <= -2147483649.0 then Int32.MinValue else int x
    static member D2L (x: float) : int64 = if Double.IsNaN(x) || x >= 9223372036854775808.0 || x < -9223372036854775808.0 then Int64.MinValue else int64 x
    static member Discard (o: obj) = ()
    static member Comma<'T> (a: obj, b: 'T) : 'T = b

    // ---------------------------------------------------------------- memory
    static member Lit (s: string) : CPtr =
        let b : byte[] = Array.zeroCreate (s.Length + 1)
        for i in 0 .. s.Length - 1 do b.[i] <- byte s.[i]
        CPtr(b, 0)
    /// glibc's %a of a non-negative finite double: 0x1.hhhp+e with trailing zero digits dropped (0x0p+0 for zero,
    /// 0x0.hhhp-1022 for a subnormal); with a precision, rounded to that many digits (round half to even, as glibc does
    /// in the default rounding mode).
    static member private HexFloat (a: float, prec: int, alt: bool) : string =
        let bits = BitConverter.DoubleToInt64Bits(a)
        let ex = int ((bits >>> 52) &&& 0x7FFL)
        let man = uint64 bits &&& 0xFFFFFFFFFFFFFUL
        let mutable lead = 1
        let e = if ex = 0 then (lead <- 0; (if man = 0UL then 0 else -1022)) else ex - 1023
        let digits =
            if prec < 0 then man.ToString("x13").TrimEnd('0')
            elif prec < 13 then
                let drop = (13 - prec) * 4
                let mutable keep = man >>> drop
                let rem = man &&& ((1UL <<< drop) - 1UL)
                let half = 1UL <<< (drop - 1)
                if rem > half || (rem = half && (keep &&& 1UL) = 1UL) then keep <- keep + 1UL
                if keep >>> (prec * 4) <> 0UL then
                    keep <- keep &&& ((1UL <<< (prec * 4)) - 1UL)
                    lead <- lead + 1
                if prec = 0 then "" else keep.ToString("x" + string prec)
            else man.ToString("x13") + String('0', prec - 13)
        let mant = lead.ToString(CultureInfo.InvariantCulture) + (if digits.Length > 0 || alt then "." else "") + digits
        "0x" + mant + "p" + (if e >= 0 then "+" else "-") + Math.Abs(e).ToString(CultureInfo.InvariantCulture)

    /// char a[n] = "literal": n bytes, the literal's first, the rest zero.
    static member LitN (s: string, n: int) : CPtr =
        let b : byte[] = Array.zeroCreate (Math.Max(n, s.Length + 1))
        for i in 0 .. s.Length - 1 do b.[i] <- byte s.[i]
        CPtr(b, 0)
    static member Buf (n: int) : CPtr = CPtr(Array.zeroCreate (Math.Max(n, 1)), 0)
    static member Buf (n: int64) : CPtr = R.Buf(int n)
    static member Buf (n: uint64) : CPtr = R.Buf(int n)
    static member BufFrom (lit: CPtr, n: int) : CPtr =
        let p = R.Buf(n)
        let mutable i = 0
        while i < n && lit.B.[lit.O + i] <> 0uy do
            p.B.[i] <- lit.B.[lit.O + i]
            i <- i + 1
        p
    static member CopyBuf (p: CPtr) : CPtr = if p.IsNull then p else CPtr(p.B.Clone() :?> byte[], p.O)
    static member NewBufs (n: int, m: int) : CPtr[] = let a : CPtr[] = Array.zeroCreate n in (for i in 0 .. n - 1 do a.[i] <- R.Buf(m)); a
    static member NewArray<'T when 'T : (new : unit -> 'T)> (n0: int) : 'T[] =
        let n = if n0 < 0 then 0 else n0
        let a : 'T[] = Array.zeroCreate n
        for i in 0 .. n - 1 do a.[i] <- new 'T()
        a
    static member NewArray<'T when 'T : (new : unit -> 'T)> (n: int64) : 'T[] = R.NewArray<'T>(int n)
    static member Jagged<'T> (n: int, m: int) : 'T[][] = let a : 'T[][] = Array.zeroCreate n in (for i in 0 .. n - 1 do a.[i] <- Array.zeroCreate m); a
    static member CloneArray<'T> (a: 'T[]) : 'T[] =
        if isNull a then null
        else
            let c : 'T[] = Array.zeroCreate a.Length
            let mi = typeof<'T>.GetMethod("Clone")
            for i in 0 .. a.Length - 1 do c.[i] <- (if isNull (box a.[i]) then Unchecked.defaultof<'T> else mi.Invoke(a.[i], null) :?> 'T)
            c
    static member CloneJagged<'T> (a: 'T[][]) : 'T[][] = let c : 'T[][] = Array.zeroCreate a.Length in (for i in 0 .. a.Length - 1 do c.[i] <- a.[i].Clone() :?> 'T[]); c
    static member Realloc<'T> (a: 'T[], n: int) : 'T[] =
        let c : 'T[] = Array.zeroCreate n
        if not (isNull a) then Array.Copy(a, c, Math.Min(a.Length, n))
        if typeof<'T>.IsClass && not (isNull (typeof<'T>.GetConstructor(Type.EmptyTypes))) then
            for i in (if isNull a then 0 else a.Length) .. n - 1 do c.[i] <- Activator.CreateInstance(typeof<'T>) :?> 'T
        c
    static member free (o: obj) = ()
    static member memset (a: float[], v: int, n: uint64) =
        let cnt = int (n / 8UL)
        let mutable i = 0
        while i < cnt && i < a.Length do
            a.[i] <- 0.0
            i <- i + 1
    static member memset (a: float[], v: int, n: int64) = R.memset(a, v, uint64 n)
    static member memset (a: float[], v: int, n: int) = R.memset(a, v, uint64 n)

    // ---------------------------------------------------------------- strings
    static member Str (p: CPtr) : string =
        let sb = StringBuilder()
        let mutable i = p.O
        while i < p.B.Length && p.B.[i] <> 0uy do
            sb.Append(char p.B.[i]) |> ignore
            i <- i + 1
        sb.ToString()
    static member Bytes (p: CPtr) : byte[] =
        let n = int (R.strlen(p))
        let b : byte[] = Array.zeroCreate n
        Array.Copy(p.B, p.O, b, 0, n)
        b
    static member strlen (p: CPtr) : uint64 =
        let mutable i = p.O
        while p.B.[i] <> 0uy do i <- i + 1
        uint64 (i - p.O)
    static member strcpy (d: CPtr, s: CPtr) : CPtr =
        if obj.ReferenceEquals(d.B, s.B) && d.O > s.O then
            let tmp = R.Bytes(s)
            for i in 0 .. tmp.Length - 1 do d.B.[d.O + i] <- tmp.[i]
            d.B.[d.O + tmp.Length] <- 0uy
            d
        else
            let mutable i = 0
            let mutable c = 1uy
            while c <> 0uy do
                c <- s.B.[s.O + i]
                d.B.[d.O + i] <- c
                i <- i + 1
            d
    static member strcat (d: CPtr, s: CPtr) : CPtr = R.strcpy(CPtr.op_Addition(d, int (R.strlen(d))), s) |> ignore; d
    static member strcmp (a: CPtr, b: CPtr) : int =
        let mutable i = 0
        let mutable r = 0
        let mutable go = true
        while go do
            let x = int a.B.[a.O + i]
            let y = int b.B.[b.O + i]
            if x <> y then r <- x - y; go <- false
            elif x = 0 then go <- false
            else i <- i + 1
        r
    static member strchr (s: CPtr, c: int) : CPtr =
        let ch = byte c
        let mutable i = s.O
        let mutable r = CPtr.Null
        let mutable go = true
        while go do
            if s.B.[i] = ch then r <- CPtr(s.B, i); go <- false
            elif s.B.[i] = 0uy then go <- false
            else i <- i + 1
        r
    static member strrchr (s: CPtr, c: int) : CPtr =
        let ch = byte c
        let mutable r = CPtr.Null
        let mutable i = s.O
        let mutable go = true
        while go do
            if s.B.[i] = ch then r <- CPtr(s.B, i)
            if s.B.[i] = 0uy then go <- false else i <- i + 1
        r
    static member strstr (h: CPtr, n: CPtr) : CPtr =
        let nl = int (R.strlen(n))
        if nl = 0 then h
        else
            let mutable i = h.O
            let mutable r = CPtr.Null
            let mutable found = false
            while not found && h.B.[i] <> 0uy do
                let mutable j = 0
                while j < nl && h.B.[i + j] = n.B.[n.O + j] do j <- j + 1
                if j = nl then r <- CPtr(h.B, i); found <- true
                else i <- i + 1
            r
    static member strcspn (s: CPtr, reject: CPtr) : uint64 =
        let mutable i = 0
        let mutable stop = false
        while not stop && s.B.[s.O + i] <> 0uy do
            let c = s.B.[s.O + i]
            let mutable j = reject.O
            while not stop && reject.B.[j] <> 0uy do
                if reject.B.[j] = c then stop <- true
                j <- j + 1
            if not stop then i <- i + 1
        uint64 i
    static member strtok (s: CPtr, delim: CPtr) : CPtr =
        if not s.IsNull then R.tokNext <- s
        if R.tokNext.IsNull then CPtr.Null
        else
            let mutable p = R.tokNext
            // skip leading delimiters
            while p.B.[p.O] <> 0uy && R.IsIn(p.B.[p.O], delim) do p <- CPtr.op_Addition(p, 1)
            if p.B.[p.O] = 0uy then
                R.tokNext <- CPtr.Null
                CPtr.Null
            else
                let start = p
                while p.B.[p.O] <> 0uy && not (R.IsIn(p.B.[p.O], delim)) do p <- CPtr.op_Addition(p, 1)
                if p.B.[p.O] <> 0uy then
                    p.B.[p.O] <- 0uy
                    R.tokNext <- CPtr.op_Addition(p, 1)
                else R.tokNext <- CPtr.Null
                start
    static member private IsIn (c: byte, set: CPtr) : bool =
        let mutable j = set.O
        let mutable r = false
        while not r && set.B.[j] <> 0uy do
            if set.B.[j] = c then r <- true
            j <- j + 1
        r
    static member toupper (c: int) : int = if c >= int 'a' && c <= int 'z' then c - 32 else c
    static member private IsSpace (c: int) : bool = c = int ' ' || c = int '\t' || c = int '\n' || c = 11 || c = 12 || c = int '\r'
    static member private IsDigit (c: int) : bool = c >= int '0' && c <= int '9'

    // ---------------------------------------------------------------- numbers from text (glibc strtod/strtol)
    static member strtod (s: CPtr) : float = let mutable e = CPtr.Null in R.strtod(s, &e)
    static member strtod (s: CPtr, ``end``: byref<CPtr>) : float =
        let mutable v = 0.0
        let n = R.ScanDouble(s.B, s.O, Int32.MaxValue, &v, true)
        ``end`` <- (if n = 0 then s else CPtr(s.B, s.O + n))
        v
    static member atof (s: CPtr) : float = R.strtod(s)
    /// Whether the last ScanDouble (strtod, atof, sscanf's conversions) would have set errno to ERANGE in glibc: an
    /// overflow to +-inf, a nonzero number that became zero, or a result that is tiny (below DBL_MIN after rounding to
    /// 53 bits, the x86 rule) and inexact. libstdc++'s std::stod throws out_of_range on it.
    static member ERange with get () : bool = R.eRange and set (v: bool) = R.eRange <- v
    /// Parse a C floating number at b[o..] using at most maxw chars (after leading space). Returns chars consumed (0 =
    /// none).
    static member ScanDouble (b: byte[], o: int, maxw: int, v: outref<float>, skipSpace: bool) : int =
        v <- 0.0
        R.eRange <- false
        let mutable i = o
        if skipSpace then
            while R.IsSpace(int b.[i]) do i <- i + 1
        let lim = if maxw = Int32.MaxValue then Int32.MaxValue else i + maxw
        let get (k: int) = if k < lim then int b.[k] else 0
        let mutable neg = false
        if get i = int '+' || get i = int '-' then
            neg <- get i = int '-'
            i <- i + 1
        // inf / nan
        if R.Lower(get i) = int 'i' && R.Lower(get (i + 1)) = int 'n' && R.Lower(get (i + 2)) = int 'f' then
            i <- i + 3
            if R.Lower(get i) = int 'i' && R.Lower(get (i + 1)) = int 'n' && R.Lower(get (i + 2)) = int 'i' && R.Lower(get (i + 3)) = int 't' && R.Lower(get (i + 4)) = int 'y' then i <- i + 5
            v <- (if neg then Double.NegativeInfinity else Double.PositiveInfinity)
            i - o
        elif R.Lower(get i) = int 'n' && R.Lower(get (i + 1)) = int 'a' && R.Lower(get (i + 2)) = int 'n' then
            i <- i + 3
            if get i = int '(' then
                let mutable k = i + 1
                while R.IsDigit(get k) || (R.Lower(get k) >= int 'a' && R.Lower(get k) <= int 'z') || get k = int '_' do k <- k + 1
                if get k = int ')' then i <- k + 1
            // C#'s `neg ? -double.NaN : double.NaN`: Roslyn folds -double.NaN to a constant with double.NaN's own bits
            // (0xFFF8...), so both arms give the same NaN; F#'s unary minus would flip the sign bit at run time
            // (fsharp/test/CrtCheck.cs, misc "-nan"; lessons/41)
            v <- (if neg then Double.NaN else Double.NaN)
            i - o
        elif get i = int '0' && R.Lower(get (i + 1)) = int 'x' && (R.IsHex(get (i + 2)) || (get (i + 2) = int '.' && R.IsHex(get (i + 3)))) then
            let mutable hv = 0.0
            let r = R.HexFloat(b, o, i, lim, neg, &hv)
            v <- hv
            r
        else
            let ms = i
            let mutable nd = 0
            while R.IsDigit(get i) do
                i <- i + 1
                nd <- nd + 1
            if get i = int '.' then
                i <- i + 1
                while R.IsDigit(get i) do
                    i <- i + 1
                    nd <- nd + 1
            if nd = 0 then
                v <- 0.0
                0
            else
                if R.Lower(get i) = int 'e' then
                    let mutable k = i + 1
                    if get k = int '+' || get k = int '-' then k <- k + 1
                    if R.IsDigit(get k) then
                        while R.IsDigit(get k) do k <- k + 1
                        i <- k
                let txt = Encoding.ASCII.GetString(b, ms, i - ms)
                let mutable d = Double.Parse(txt, NumberStyles.Float, CultureInfo.InvariantCulture)
                R.eRange <- R.DecRange(txt, d)
                if neg then d <- -d
                v <- d
                i - o
    static member ScanDouble (b: byte[], o: int, maxw: int, v: outref<float>) : int = R.ScanDouble(b, o, maxw, &v, true)
    /// glibc strtod's ERANGE for the decimal text txt (digits, '.', exponent; no sign) whose correctly rounded value
    /// is av: overflow, or underflow (tiny after rounding and inexact, decided exactly).
    static member private DecRange (txt: string, av: float) : bool =
        if Double.IsInfinity(av) then true
        elif av > 2.2250738585072014e-308 then false          // above DBL_MIN: normal
        else
            let mutable d = BigInteger.Zero
            let mutable e10 = 0L
            let mutable k = 0
            while k < txt.Length && R.IsDigit(int txt.[k]) do
                d <- d * BigInteger(10) + BigInteger(int txt.[k] - int '0')
                k <- k + 1
            if k < txt.Length && txt.[k] = '.' then
                k <- k + 1
                while k < txt.Length && R.IsDigit(int txt.[k]) do
                    d <- d * BigInteger(10) + BigInteger(int txt.[k] - int '0')
                    e10 <- e10 - 1L
                    k <- k + 1
            if d.IsZero then false                              // a zero is exact
            else
                if k < txt.Length && (txt.[k] = 'e' || txt.[k] = 'E') then
                    k <- k + 1
                    let mutable en = false
                    if k < txt.Length && (txt.[k] = '+' || txt.[k] = '-') then
                        en <- txt.[k] = '-'
                        k <- k + 1
                    let mutable ex = 0L
                    while k < txt.Length && R.IsDigit(int txt.[k]) do
                        ex <- Math.Min(ex * 10L + int64 (int txt.[k] - int '0'), 1000000000L)
                        k <- k + 1
                    e10 <- e10 + (if en then -ex else ex)
                let nd = int64 (d.ToString(CultureInfo.InvariantCulture).Length)
                if nd + e10 < -340L then true                    // below 1e-340: zero, and not exact
                elif e10 >= 0L then false
                else
                    let p10 = BigInteger.Pow(BigInteger(10), int -e10)   // the value is d / 10^-e10
                    // tiny after rounding: below 2^-1022 - 2^-1076, the midpoint under DBL_MIN of the 53-bit grid
                    if d * BigInteger.Pow(BigInteger(2), 1076) >= ((BigInteger.One <<< 54) - BigInteger.One) * p10 then false
                    else
                        // inexact: not av itself, which is m * 2^-1074 for a subnormal (or DBL_MIN, or zero)
                        let m = BitConverter.DoubleToInt64Bits(av)
                        d * (BigInteger.One <<< 1074) <> BigInteger(m) * p10
    static member private IsHex (c: int) : bool = R.IsDigit(c) || (R.Lower(c) >= int 'a' && R.Lower(c) <= int 'f')
    static member private Lower (c: int) : int = if c >= int 'A' && c <= int 'Z' then c + 32 else c
    static member private HexFloat (b: byte[], o: int, i0: int, lim: int, neg: bool, v: outref<float>) : int =
        let get (k: int) = if k < lim then int b.[k] else 0
        let mutable i = i0 + 2
        let mutable m = BigInteger.Zero
        let mutable exp = 0
        let mutable any = false
        while R.IsHex(get i) do
            m <- m * BigInteger(16) + BigInteger(R.HexVal(get i))
            i <- i + 1
            any <- true
        if get i = int '.' then
            i <- i + 1
            while R.IsHex(get i) do
                m <- m * BigInteger(16) + BigInteger(R.HexVal(get i))
                exp <- exp - 4
                i <- i + 1
                any <- true
        if not any then
            v <- 0.0
            0
        else
            if R.Lower(get i) = int 'p' then
                let mutable k = i + 1
                let mutable en = false
                if get k = int '+' || get k = int '-' then
                    en <- get k = int '-'
                    k <- k + 1
                if R.IsDigit(get k) then
                    let mutable e = 0
                    while R.IsDigit(get k) do
                        e <- Math.Min(e * 10 + get k - int '0', 100000)
                        k <- k + 1
                    exp <- exp + (if en then -e else e)
                    i <- k
            let mutable d = R.BigRatioToDouble(m, exp)
            R.eRange <- not m.IsZero && (d = 0.0 || Double.IsInfinity(d))    // (a hexadecimal subnormal is not checked)
            if neg then d <- -d
            v <- d
            i - o
    static member private HexVal (c: int) : int = if R.IsDigit(c) then c - int '0' else R.Lower(c) - int 'a' + 10
    static member private BigRatioToDouble (m: BigInteger, exp2: int) : float =
        if m.IsZero then 0.0
        else
            // m may lose bits: hex floats are not used by SWMM inputs in practice (as the C# runtime has it)
            let d = float m
            d * Math.Pow(2.0, float exp2)
    static member strtol (s: CPtr, ``end``: byref<CPtr>, b: int) : int64 =
        let mutable i = s.O
        while R.IsSpace(int s.B.[i]) do i <- i + 1
        let mutable neg = false
        if s.B.[i] = byte '+' || s.B.[i] = byte '-' then
            neg <- s.B.[i] = byte '-'
            i <- i + 1
        let st = i
        let mutable v = BigInteger.Zero
        while R.IsDigit(int s.B.[i]) do
            v <- v * BigInteger(10) + BigInteger(int s.B.[i] - int '0')
            i <- i + 1
        if i = st then
            ``end`` <- s
            0L
        else
            ``end`` <- CPtr(s.B, i)
            if neg then v <- BigInteger.Negate(v)
            if v > BigInteger(Int64.MaxValue) then Int64.MaxValue
            elif v < BigInteger(Int64.MinValue) then Int64.MinValue
            else int64 v
    static member atoi (s: CPtr) : int = let mutable e = CPtr.Null in int (R.strtol(s, &e, 10))

    // ---------------------------------------------------------------- sscanf (the directives SWMM uses)
    static member RefI (a: Action<int>) : RefI = let r = Cpp.RefI() in r.Set <- a; r
    static member RefL (a: Action<int64>) : RefL = let r = Cpp.RefL() in r.Set <- a; r
    static member RefD (a: Action<float>) : RefD = let r = Cpp.RefD() in r.Set <- a; r
    static member RefF (a: Action<float32>) : RefF = let r = Cpp.RefF() in r.Set <- a; r

    /// glibc's sscanf for the directives the engine uses: %d %i %u (decimal), %f %e %g %a (and capitals), %s, %c,
    /// %[...], %n and %%, with * and widths and the h/l/L/z/j/t/q modifiers. It returns the number of assignments, or
    /// EOF (-1) when the input ends before the first assignment (a suppressed conversion that consumed input does not
    /// count). %u is strtoul's value (a negative number wraps), cut to 32 bits without l; a float target gets strtof's
    /// rounding of the characters, not a double rounded again. As in the Java, Go, Fortran and C# runtimes.
    static member sscanf (s: CPtr, fmt: CPtr, [<ParamArray>] args: obj[]) : int =
        R.StashReset()
        let b = s.B
        let mutable i = s.O
        let f = fmt.B
        let mutable k = fmt.O
        let mutable ai = 0
        let mutable count = 0
        let mutable result = Int32.MinValue        // set when the scan stops early
        while result = Int32.MinValue && f.[k] <> 0uy do
            let fc = int f.[k]
            if R.IsSpace(fc) then
                while R.IsSpace(int f.[k]) do k <- k + 1
                while R.IsSpace(R.At(b, i)) do i <- i + 1
            elif fc <> int '%' then
                if R.At(b, i) = 0 then result <- (if count = 0 then -1 else count)
                elif R.At(b, i) <> fc then result <- count
                else
                    i <- i + 1
                    k <- k + 1
            else
                k <- k + 1
                let mutable suppress = false
                if f.[k] = byte '*' then
                    suppress <- true
                    k <- k + 1
                let mutable width = 0
                while R.IsDigit(int f.[k]) do
                    width <- width * 10 + int f.[k] - int '0'
                    k <- k + 1
                let mutable lmod = 0
                while f.[k] = byte 'l' || f.[k] = byte 'h' || f.[k] = byte 'L' || f.[k] = byte 'z' || f.[k] = byte 'j' || f.[k] = byte 't' || f.[k] = byte 'q' do
                    if f.[k] <> byte 'h' then lmod <- lmod + 1
                    k <- k + 1
                let conv = int f.[k]
                k <- k + 1
                let mutable set : bool[] = null
                if conv = int '[' then
                    set <- Array.zeroCreate 256
                    let mutable neg = false
                    if f.[k] = byte '^' then
                        neg <- true
                        k <- k + 1
                    let mutable first = true
                    while f.[k] <> 0uy && (first || f.[k] <> byte ']') do
                        let c = int f.[k]
                        if c = int '-' && not first && f.[k + 1] <> byte ']' && f.[k + 1] <> 0uy && f.[k - 1] <= f.[k + 1] then
                            for x in int f.[k - 1] .. int f.[k + 1] do set.[x] <- true
                            k <- k + 2
                            first <- false
                        else
                            set.[c] <- true
                            k <- k + 1
                            first <- false
                    if f.[k] = byte ']' then k <- k + 1
                    if neg then
                        for x in 0 .. 255 do set.[x] <- not set.[x]
                        set.[0] <- false
                if conv <> int 'c' && conv <> int '[' && conv <> int 'n' then
                    while R.IsSpace(R.At(b, i)) do i <- i + 1
                if conv = int 'n' then
                    // the characters read so far; not an assignment, and not stopped by the end of the input
                    if not suppress then
                        R.StoreInt(args.[ai], int64 (i - s.O), lmod)
                        ai <- ai + 1
                elif R.At(b, i) = 0 then result <- (if count = 0 then -1 else count)
                else
                    let w = if width > 0 then width else 1 <<< 30
                    match conv with
                    | 37 (* '%' *) ->
                        if R.At(b, i) <> int '%' then result <- count
                        else i <- i + 1
                    | 100 | 105 | 117 (* 'd' 'i' 'u' *) ->
                        let mutable j = i
                        let lim = i + w
                        let mutable neg = false
                        if j < lim && (R.At(b, j) = int '+' || R.At(b, j) = int '-') then
                            neg <- R.At(b, j) = int '-'
                            j <- j + 1
                        let ds = j
                        let mutable u = BigInteger.Zero
                        while j < lim && R.IsDigit(R.At(b, j)) do
                            u <- u * BigInteger(10) + BigInteger(R.At(b, j) - int '0')
                            j <- j + 1
                        if j = ds then result <- count
                        else
                            i <- j
                            let v =
                                if conv = int 'u' then
                                    // strtoul: the magnitude (ULONG_MAX when it does not fit), negated as an unsigned number
                                    let mutable m = if u > BigInteger(UInt64.MaxValue) then UInt64.MaxValue else uint64 u
                                    if neg && u <= BigInteger(UInt64.MaxValue) then m <- 0UL - m
                                    int64 (if lmod = 0 then (m &&& 0xFFFFFFFFUL) else m)
                                else
                                    if neg then u <- BigInteger.Negate(u)             // strtol: clamped to long
                                    if u > BigInteger(Int64.MaxValue) then Int64.MaxValue elif u < BigInteger(Int64.MinValue) then Int64.MinValue else int64 u
                            if not suppress then
                                R.StoreInt(args.[ai], v, lmod)
                                ai <- ai + 1
                                count <- count + 1
                    | 102 | 101 | 103 | 69 | 71 | 97 | 65 | 70 (* 'f' 'e' 'g' 'E' 'G' 'a' 'A' 'F' *) ->
                        let mutable num : byte[] = null
                        let e = R.ScanFloat(b, i, width, &num)
                        if e < 0 then result <- count
                        else
                            i <- e
                            // the characters taken are converted as strtod (or strtof, for a float) converts them
                            let mutable v = 0.0
                            let n = R.ScanDouble(num, 0, Int32.MaxValue, &v, false)
                            if n <= 0 then result <- count
                            elif not suppress then
                                let a = args.[ai]
                                ai <- ai + 1
                                let fv = if lmod > 0 then 0.0f else R.StrToF(num, n, v)
                                match a with
                                | :? RefD as rd -> rd.Set.Invoke(v)
                                | :? RefF as rf -> rf.Set.Invoke(fv)
                                | :? ScanSlot -> R.Stash(if lmod > 0 then box v else box fv)
                                | _ -> raise (NotSupportedException("sscanf %" + string (char conv) + " target " + string a))
                                count <- count + 1
                    | 115 | 91 (* 's' '[' *) ->
                        let mutable j = i
                        let mutable m = 0
                        let mutable go = true
                        while go && R.At(b, j) <> 0 && m < w do
                            let c = R.At(b, j)
                            if (if conv = int 's' then R.IsSpace(c) else not set.[c]) then go <- false
                            else
                                j <- j + 1
                                m <- m + 1
                        if m = 0 then result <- count       // only %[ can match nothing: a matching failure
                        else
                            if not suppress then
                                let d = unbox<CPtr> args.[ai]
                                ai <- ai + 1
                                Array.Copy(b, i, d.B, d.O, m)
                                d.B.[d.O + m] <- 0uy
                                count <- count + 1
                            i <- j
                    | 99 (* 'c' *) ->
                        let n = if width > 0 then width else 1
                        let a = if suppress then null else (let x = args.[ai] in ai <- ai + 1; x)
                        let mutable t = 0
                        while t < n && R.At(b, i) <> 0 do
                            if not suppress then
                                match a with
                                | :? CPtr as cp -> cp.B.[cp.O + t] <- b.[i]
                                | :? RefI as ri -> ri.Set.Invoke(int (sbyte b.[i]))
                                | :? ScanSlot when t = 0 -> R.Stash(box (sbyte b.[i]))
                                | :? ScanSlot -> R.Stash(box (int (sbyte b.[i])))
                                | _ -> ()
                            i <- i + 1
                            t <- t + 1
                        if not suppress then count <- count + 1
                    | _ -> raise (NotSupportedException("sscanf %" + string (char conv)))
        if result <> Int32.MinValue then result else count
    static member private At (b: byte[], j: int) : int = if j < b.Length then int b.[j] else 0
    /// An integer conversion's value into its target (an int unless the l modifier was given).
    static member private StoreInt (a: obj, v: int64, lmod: int) =
        match a with
        | :? RefI as ri -> ri.Set.Invoke(int v)
        | :? RefL as rl -> rl.Set.Invoke(v)
        | :? ScanSlot -> R.Stash(if lmod > 0 then box v else box (int v))
        | _ -> raise (NotSupportedException("sscanf integer target " + string a))
    /// strtof of the first n characters of a NUL-terminated number text, of which v is strtod's value: .NET's float
    /// parser rounds the decimal text once, to float, as strtof does (a hexadecimal float, which the engine's inputs
    /// do not contain, is the double's value rounded to float).
    static member private StrToF (num: byte[], n: int, v: float) : float32 =
        if Double.IsNaN(v) || Double.IsInfinity(v) then float32 v
        else
            let txt = Encoding.ASCII.GetString(num, 0, n)
            if txt.IndexOf('x') >= 0 || txt.IndexOf('X') >= 0 then float32 v
            else Single.Parse(txt, NumberStyles.Float, CultureInfo.InvariantCulture)
    /// The scanner of glibc's vfscanf for a floating conversion at b[i] (not a space, not the end): it collects the
    /// characters it takes (at most width, 0 for no limit) into num (NUL-terminated) and returns the index after them,
    /// -1 for a matching failure. Like glibc, it reads one character ahead and pushes back only that one, so an exponent
    /// marker or sign with no digits after it ("1e", "1e+") is taken (and ignored by the conversion), and a partial
    /// "infinity" or a bare "0x" fails. "nan" never takes an n-char-sequence. (C#'s local functions, which assign the
    /// scanner's state, become members of a small scanner object here.)
    static member private ScanFloat (b: byte[], i: int, width: int, num: outref<byte[]>) : int =
        let sc = FloatScan(b, i, width)
        let r = sc.Run()
        num <- sc.Num
        r

    // ---------------------------------------------------------------- printf family
    static member fprintf (f: CFile, fmt: CPtr, [<ParamArray>] args: obj[]) : int =
        let bytes = R.Format(fmt, args)
        if isNull f then -1
        else
            f.S.Write(bytes, 0, bytes.Length)
            bytes.Length
    static member snprintf (buf: CPtr, n: uint64, fmt: CPtr, [<ParamArray>] args: obj[]) : int =
        let bytes = R.Format(fmt, args)
        if n > 0UL then
            let m = int (Math.Min(uint64 bytes.Length, n - 1UL))
            Array.Copy(bytes, 0, buf.B, buf.O, m)
            buf.B.[buf.O + m] <- 0uy
        bytes.Length
    static member snprintf (buf: CPtr, n: int64, fmt: CPtr, [<ParamArray>] args: obj[]) : int = R.snprintf(buf, uint64 n, fmt, args)
    static member snprintf (buf: CPtr, n: int, fmt: CPtr, [<ParamArray>] args: obj[]) : int = R.snprintf(buf, uint64 n, fmt, args)

    static member Format (fmt: CPtr, args: obj[]) : byte[] =
        let o = List<byte>(128)
        let f = fmt.B
        let mutable k = fmt.O
        let mutable ai = 0
        let nextArg () = let a = args.[ai] in ai <- ai + 1; a
        while f.[k] <> 0uy do
            if f.[k] <> byte '%' then
                o.Add(f.[k])
                k <- k + 1
            else
                k <- k + 1
                if f.[k] = byte '%' then
                    o.Add(byte '%')
                    k <- k + 1
                else
                    let mutable left = false
                    let mutable plus = false
                    let mutable space = false
                    let mutable alt = false
                    let mutable zero = false
                    let mutable flags = true
                    while flags do
                        if f.[k] = byte '-' then left <- true; k <- k + 1
                        elif f.[k] = byte '+' then plus <- true; k <- k + 1
                        elif f.[k] = byte ' ' then space <- true; k <- k + 1
                        elif f.[k] = byte '#' then alt <- true; k <- k + 1
                        elif f.[k] = byte '0' then zero <- true; k <- k + 1
                        else flags <- false
                    let mutable width = 0
                    let mutable prec = -1
                    if f.[k] = byte '*' then
                        width <- Convert.ToInt32(nextArg ())
                        if width < 0 then
                            left <- true
                            width <- -width
                        k <- k + 1
                    else
                        while R.IsDigit(int f.[k]) do
                            width <- width * 10 + int f.[k] - int '0'
                            k <- k + 1
                    if f.[k] = byte '.' then
                        k <- k + 1
                        prec <- 0
                        if f.[k] = byte '*' then
                            prec <- Convert.ToInt32(nextArg ())
                            if prec < 0 then prec <- -1
                            k <- k + 1
                        else
                            while R.IsDigit(int f.[k]) do
                                prec <- prec * 10 + int f.[k] - int '0'
                                k <- k + 1
                    while f.[k] = byte 'l' || f.[k] = byte 'h' || f.[k] = byte 'L' || f.[k] = byte 'z' || f.[k] = byte 'j' || f.[k] = byte 't' || f.[k] = byte 'q' do k <- k + 1
                    let conv = int f.[k]
                    k <- k + 1
                    let mutable body = ""
                    let mutable sign = ""
                    let mutable numeric = true
                    match char conv with
                    | 'd' | 'i' ->
                        let v = R.ToLong(nextArg ())
                        let mutable bv = BigInteger(v)
                        if v < 0L then
                            sign <- "-"
                            bv <- BigInteger.Negate(bv)
                        elif plus then sign <- "+"
                        elif space then sign <- " "
                        body <- bv.ToString(CultureInfo.InvariantCulture)
                        if prec >= 0 then
                            zero <- false
                            if prec = 0 && v = 0L then body <- ""
                            body <- body.PadLeft(prec, '0')
                    | 'u' | 'x' | 'X' | 'o' ->
                        let v = R.ToULong(nextArg ())
                        body <- (if conv = int 'u' then v.ToString(CultureInfo.InvariantCulture)
                                 elif conv = int 'o' then Convert.ToString(int64 v, 8)
                                 else v.ToString(if conv = int 'x' then "x" else "X"))
                        if prec >= 0 then
                            zero <- false
                            if prec = 0 && v = 0UL then body <- ""
                            body <- body.PadLeft(prec, '0')
                    | 'c' ->
                        numeric <- false
                        let c = Convert.ToInt32(nextArg ())
                        body <- (char (byte c)).ToString()
                    | 's' ->
                        numeric <- false
                        let p = unbox<CPtr> (nextArg ())
                        let mutable s = if p.IsNull then "(null)" else R.Str(p)
                        if prec >= 0 && s.Length > prec then s <- s.Substring(0, prec)
                        body <- s
                    | 'f' | 'F' | 'e' | 'E' | 'g' | 'G' ->
                        let v = R.ToDouble(nextArg ())
                        if prec < 0 then prec <- 6
                        let negv = Double.IsNegative(v)
                        if negv then sign <- "-" elif plus then sign <- "+" elif space then sign <- " "
                        let a = Math.Abs(v)
                        if Double.IsNaN(v) || Double.IsInfinity(v) then
                            body <- (if Double.IsNaN(v) then "nan" else "inf")
                            if conv = int 'F' || conv = int 'E' || conv = int 'G' then body <- body.ToUpperInvariant()
                            zero <- false
                        else body <- R.FormatFloat(a, conv, prec, alt)
                    | 'a' | 'A' ->
                        let v = R.ToDouble(nextArg ())
                        let negv = Double.IsNegative(v)
                        if negv then sign <- "-" elif plus then sign <- "+" elif space then sign <- " "
                        if Double.IsNaN(v) || Double.IsInfinity(v) then
                            body <- (if Double.IsNaN(v) then "nan" else "inf")
                            zero <- false
                        else body <- R.HexFloat(Math.Abs(v), prec, alt)
                        if conv = int 'A' then body <- body.ToUpperInvariant()
                    | _ -> raise (NotSupportedException("printf %" + string (char conv)))
                    let len = sign.Length + body.Length
                    let full =
                        if len >= width then sign + body
                        elif left then sign + body + String(' ', width - len)
                        elif zero && numeric then sign + String('0', width - len) + body
                        else String(' ', width - len) + sign + body
                    for ch in full do o.Add(byte ch)
        o.ToArray()
    static member private ToLong (a: obj) : int64 =
        match a with
        | :? int as i -> int64 i
        | :? int64 as l -> l
        | :? uint32 as u -> int64 u
        | :? uint64 as ul -> int64 ul
        | :? float as d -> int64 d
        | _ -> Convert.ToInt64(a)
    static member private ToULong (a: obj) : uint64 =
        match a with
        | :? int as i -> uint64 (int64 i)
        | :? int64 as l -> uint64 l
        | :? uint32 as u -> uint64 u
        | :? uint64 as ul -> ul
        | _ -> Convert.ToUInt64(a)
    static member private ToDouble (a: obj) : float =
        match a with
        | :? float as d -> d
        | :? float32 as f -> float f
        | :? int as i -> float i
        | :? int64 as l -> float l
        | _ -> Convert.ToDouble(a)

    /// exact decimal formatting of a finite non-negative double, glibc rounding (half-even on the exact value)
    static member FormatFloat (a: float, conv: int, prec: int, alt: bool) : string =
        let lc = char (conv ||| 0x20)
        if lc = 'f' then R.FixedDigits(a, prec, alt)
        elif lc = 'e' then R.ExpDigits(a, prec, alt, (conv = int 'E'))
        else
            // %g
            let P = if prec = 0 then 1 else prec
            let mutable X = 0
            if a <> 0.0 then R.ExpDigitsRaw(a, P - 1, &X) |> ignore
            let mutable s = if P > X && X >= -4 then R.FixedDigits(a, P - 1 - X, alt) else R.ExpDigits(a, P - 1, alt, (conv = int 'G'))
            if not alt then
                let epos = s.IndexOfAny([| 'e'; 'E' |])
                let mutable mant = if epos >= 0 then s.Substring(0, epos) else s
                let ex = if epos >= 0 then s.Substring(epos) else ""
                if mant.Contains('.') then
                    mant <- mant.TrimEnd('0')
                    if mant.EndsWith(".") then mant <- mant.Substring(0, mant.Length - 1)
                s <- mant + ex
            s
    static member private Decompose (a: float, m: outref<BigInteger>, e: outref<int>) =
        let bits = BitConverter.DoubleToInt64Bits(a)
        let ex = int ((bits >>> 52) &&& 0x7FFL)
        let frac = bits &&& 0xFFFFFFFFFFFFFL
        if ex = 0 then
            m <- BigInteger(frac)
            e <- -1074
        else
            m <- BigInteger(frac ||| (1L <<< 52))
            e <- ex - 1075
    /// round_half_even(a * 10^p) for p possibly negative
    static member private ScaledRound (a: float, p: int) : BigInteger =
        let mutable m = BigInteger.Zero
        let mutable e = 0
        R.Decompose(a, &m, &e)
        let mutable num = m
        let mutable den = BigInteger.One
        if e >= 0 then num <- num <<< e else den <- den <<< -e
        if p >= 0 then num <- num * BigInteger.Pow(BigInteger(10), p) else den <- den * BigInteger.Pow(BigInteger(10), -p)
        let mutable r = BigInteger.Zero
        let mutable q = BigInteger.DivRem(num, den, &r)
        let twice = r * BigInteger(2)
        let c = twice.CompareTo(den)
        if c > 0 || (c = 0 && not q.IsEven) then q <- q + BigInteger.One
        q
    static member private FixedDigits (a: float, prec: int, alt: bool) : string =
        let q = R.ScaledRound(a, prec)
        let mutable d = q.ToString(CultureInfo.InvariantCulture)
        if prec = 0 then (if alt then d + "." else d)
        else
            if d.Length <= prec then d <- String('0', prec - d.Length + 1) + d
            d.Substring(0, d.Length - prec) + "." + d.Substring(d.Length - prec)
    static member private ExpDigitsRaw (a: float, prec: int, xe: outref<int>) : string =
        if a = 0.0 then
            xe <- 0
            String('0', prec + 1)
        else
            // estimate exponent
            let mutable E = int (Math.Floor(Math.Log10(a)))
            let mutable res : string = null
            let mutable iter = 0
            while isNull res && iter < 4 do
                let q = R.ScaledRound(a, prec - E)
                let d = q.ToString(CultureInfo.InvariantCulture)
                if d.Length = prec + 1 then
                    xe <- E
                    res <- d
                elif d.Length > prec + 1 then E <- E + 1
                else E <- E - 1
                iter <- iter + 1
            if not (isNull res) then res
            else
                // fallback (should not happen)
                let qq = R.ScaledRound(a, prec - E)
                xe <- E
                qq.ToString(CultureInfo.InvariantCulture)
    static member private ExpDigits (a: float, prec: int, alt: bool, upper: bool) : string =
        let mutable X = 0
        let d = R.ExpDigitsRaw(a, prec, &X)
        let mant = d.Substring(0, 1) + (if prec > 0 then "." + d.Substring(1) else (if alt then "." else ""))
        let ex = (if X < 0 then "-" else "+") + Math.Abs(X).ToString("00", CultureInfo.InvariantCulture)
        mant + (if upper then "E" else "e") + ex

    // ---------------------------------------------------------------- files
    static member stdout_ with get () : CFile = (if isNull R.stdout0 then R.stdout0 <- CFile(Console.OpenStandardOutput(), true)); R.stdout0
    static member fopen (name: CPtr, mode: CPtr) : CFile =
        let fn = R.Str(name)
        let m = R.Str(mode)
        try
            let plus = m.Contains('+')
            if m.[0] = 'r' && not plus && Directory.Exists(fn) then
                let c = CFile(Stream.Null)
                c.Dir <- true
                c
            else
                let fs =
                    if m.[0] = 'r' then new FileStream(fn, FileMode.Open, (if plus then FileAccess.ReadWrite else FileAccess.Read), FileShare.ReadWrite)
                    elif m.[0] = 'w' then new FileStream(fn, FileMode.Create, (if plus then FileAccess.ReadWrite else FileAccess.Write), FileShare.ReadWrite)
                    elif m.[0] = 'a' then new FileStream(fn, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)
                    else null
                if isNull fs then null else CFile(new BufferedStream(fs, 1 <<< 16))
        with _ -> null
    static member fclose (f: CFile) : int =
        if isNull f then -1
        else
            if not f.IsStd then
                f.S.Dispose()
                lock CFile.Open (fun () -> CFile.Open.Remove(f) |> ignore)
            else f.S.Flush()
            0
    /// What C's exit() does to open streams: flush every one (stdout and stderr too).
    static member FlushAll () =
        let opened = lock CFile.Open (fun () -> List<CFile>(CFile.Open).ToArray())
        for f in opened do
            try f.S.Flush() with _ -> ()
        (try R.stdout_.S.Flush() with _ -> ())
        (try R.stderr_.S.Flush() with _ -> ())
    static member fflush (f: CFile) : int = (if not (isNull f) then f.S.Flush()); 0
    static member feof (f: CFile) : int = if f.Eof then 1 else 0
    static member rewind (f: CFile) = f.S.Seek(0L, SeekOrigin.Begin) |> ignore; f.Eof <- false
    static member fseek (f: CFile, off: int64, whence: int) : int =
        try
            f.S.Seek(off, (if whence = 0 then SeekOrigin.Begin elif whence = 1 then SeekOrigin.Current else SeekOrigin.End)) |> ignore
            f.Eof <- false
            0
        with _ -> -1
    static member fseek (f: CFile, off: int, whence: int) : int = R.fseek(f, int64 off, whence)
    static member ftell (f: CFile) : int64 = f.S.Position
    static member fgets (buf: CPtr, n: int, f: CFile) : CPtr =
        let mutable i = 0
        let mutable go = true
        while go && i < n - 1 do
            let c = f.S.ReadByte()
            if c < 0 then
                f.ReadFailed()
                go <- false
            else
                buf.B.[buf.O + i] <- byte c
                i <- i + 1
                if c = int '\n' then go <- false
        if i = 0 then CPtr.Null
        else
            buf.B.[buf.O + i] <- 0uy
            buf
    static member remove (name: CPtr) : int = try File.Delete(R.Str(name)); 0 with _ -> -1
    static member mkstemp (template: CPtr) : int =
        let n = int (R.strlen(template))
        let rnd = Random(Environment.ProcessId ^^^ Environment.TickCount ^^^ (R.tmpCounter * 7919))
        R.tmpCounter <- R.tmpCounter + 1
        let chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789"
        let mutable result = -1
        let mutable attempt = 0
        while result < 0 && attempt < 1000 do
            for i in n - 6 .. n - 1 do template.B.[template.O + i] <- byte chars.[rnd.Next(chars.Length)]
            let fn = R.Str(template)
            try
                (new FileStream(fn, FileMode.CreateNew)).Dispose()
                result <- 3
            with :? IOException -> ()
            attempt <- attempt + 1
        result

    static member private tmp8 with get () : byte[] = (if isNull R.tmp8_ then R.tmp8_ <- Array.zeroCreate 8); R.tmp8_
    static member private ReadExact (f: CFile, b: byte[], n: int) : bool =
        let mutable got = 0
        let mutable ok = true
        while ok && got < n do
            let r = f.S.Read(b, got, n - got)
            if r <= 0 then
                f.ReadFailed()
                ok <- false
            else got <- got + r
        ok
    // fwrite/fread of typed values (little-endian like x86)
    static member fwriteF1 (v: float32, f: CFile) : uint64 = f.S.Write(BitConverter.GetBytes(v), 0, 4); 1UL
    static member fwriteD1 (v: float, f: CFile) : uint64 = f.S.Write(BitConverter.GetBytes(v), 0, 8); 1UL
    static member fwriteI1 (v: int, f: CFile) : uint64 = f.S.Write(BitConverter.GetBytes(v), 0, 4); 1UL
    static member fwriteL1 (v: int64, f: CFile) : uint64 = f.S.Write(BitConverter.GetBytes(v), 0, 8); 1UL
    static member fwriteF (a: float32[], off: int, n: int, f: CFile) : uint64 = (for i in 0 .. n - 1 do f.S.Write(BitConverter.GetBytes(a.[off + i]), 0, 4)); uint64 n
    static member fwriteD (a: float[], off: int, n: int, f: CFile) : uint64 = (for i in 0 .. n - 1 do f.S.Write(BitConverter.GetBytes(a.[off + i]), 0, 8)); uint64 n
    static member fwriteI (a: int[], off: int, n: int, f: CFile) : uint64 = (for i in 0 .. n - 1 do f.S.Write(BitConverter.GetBytes(a.[off + i]), 0, 4)); uint64 n
    static member fwriteC (p: CPtr, off: int, n: int, f: CFile) : uint64 = (if n <> 0 then f.S.Write(p.B, p.O + off, n)); uint64 n
    static member freadF1 (v: byref<float32>, f: CFile) : uint64 = if not (R.ReadExact(f, R.tmp8, 4)) then 0UL else (v <- BitConverter.ToSingle(R.tmp8, 0); 1UL)
    static member freadD1 (v: byref<float>, f: CFile) : uint64 = if not (R.ReadExact(f, R.tmp8, 8)) then 0UL else (v <- BitConverter.ToDouble(R.tmp8, 0); 1UL)
    static member freadI1 (v: byref<int>, f: CFile) : uint64 = if not (R.ReadExact(f, R.tmp8, 4)) then 0UL else (v <- BitConverter.ToInt32(R.tmp8, 0); 1UL)
    static member freadL1 (v: byref<int64>, f: CFile) : uint64 = if not (R.ReadExact(f, R.tmp8, 8)) then 0UL else (v <- BitConverter.ToInt64(R.tmp8, 0); 1UL)
    static member freadF (a: float32[], off: int, n: int, f: CFile) : uint64 =
        let mutable i = 0
        let mutable r = uint64 n
        while r = uint64 n && i < n do
            if not (R.ReadExact(f, R.tmp8, 4)) then r <- uint64 i
            else
                a.[off + i] <- BitConverter.ToSingle(R.tmp8, 0)
                i <- i + 1
        r
    static member freadD (a: float[], off: int, n: int, f: CFile) : uint64 =
        let mutable i = 0
        let mutable r = uint64 n
        while r = uint64 n && i < n do
            if not (R.ReadExact(f, R.tmp8, 8)) then r <- uint64 i
            else
                a.[off + i] <- BitConverter.ToDouble(R.tmp8, 0)
                i <- i + 1
        r
    static member freadI (a: int[], off: int, n: int, f: CFile) : uint64 =
        let mutable i = 0
        let mutable r = uint64 n
        while r = uint64 n && i < n do
            if not (R.ReadExact(f, R.tmp8, 4)) then r <- uint64 i
            else
                a.[off + i] <- BitConverter.ToInt32(R.tmp8, 0)
                i <- i + 1
        r
    static member freadC (p: CPtr, off: int, n: int, f: CFile) : uint64 =
        let mutable got = 0
        let mutable go = true
        while go && got < n do
            let r = f.S.Read(p.B, p.O + off + got, n - got)
            if r <= 0 then
                f.ReadFailed()
                go <- false
            else got <- got + r
        uint64 got

    // ---------------------------------------------------------------- time
    static member time () : int64 = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
    static member time (p: Ptr<int64>) : int64 = let t = R.time () in (if not p.IsNull then p.[0] <- t); t
    static member ctime (p: Ptr<int64>) : CPtr = R.ctime(p.[0])
    static member difftime (e: int64, start: int64) : float = float (e - start)
    /// char a[n][m]: n zeroed rows of m bytes.
    static member Bufs (n: int, m: int) : CPtr[] = let r : CPtr[] = Array.zeroCreate n in (for i in 0 .. n - 1 do r.[i] <- R.Buf(m)); r
    static member ctime (t: int64) : CPtr =
        let d = DateTimeOffset.FromUnixTimeSeconds(t).ToLocalTime()
        let s = d.ToString("ddd MMM ", CultureInfo.InvariantCulture) + d.Day.ToString().PadLeft(2) + d.ToString(" HH:mm:ss yyyy", CultureInfo.InvariantCulture) + "\n"
        R.Lit(s)

    // ---------------------------------------------------------------- Fs.cs: the C library functions the SWMM5 runtime did not need
    static member errno_ with get () : int = R.errno0 and set (v: int) = R.errno0 <- v
    static member stderr_ with get () : CFile = (if isNull R.stderr0 then R.stderr0 <- CFile(Console.OpenStandardError(), true)); R.stderr0
    static member stdin_ with get () : CFile = (if isNull R.stdin0 then R.stdin0 <- CFile(Console.OpenStandardInput(), true)); R.stdin0
    static member getenv (name: CPtr) : CPtr =
        let v = Environment.GetEnvironmentVariable(R.Str(name))
        if isNull v then CPtr.Null else R.Lit(v)
    static member isdigit (c: int) : int = if c >= int '0' && c <= int '9' then 1 else 0
    static member isalpha (c: int) : int = if (c >= int 'a' && c <= int 'z') || (c >= int 'A' && c <= int 'Z') then 1 else 0
    static member isalnum (c: int) : int = if R.isdigit(c) <> 0 || R.isalpha(c) <> 0 then 1 else 0
    static member isspace (c: int) : int = if c = int ' ' || (c >= 9 && c <= 13) then 1 else 0
    static member isupper (c: int) : int = if c >= int 'A' && c <= int 'Z' then 1 else 0
    static member islower (c: int) : int = if c >= int 'a' && c <= int 'z' then 1 else 0
    static member ispunct (c: int) : int = if c > 32 && c < 127 && R.isalnum(c) = 0 then 1 else 0
    static member isprint (c: int) : int = if c >= 32 && c < 127 then 1 else 0
    static member isxdigit (c: int) : int = if R.isdigit(c) <> 0 || (c >= int 'a' && c <= int 'f') || (c >= int 'A' && c <= int 'F') then 1 else 0
    static member tolower (c: int) : int = if c >= int 'A' && c <= int 'Z' then c + 32 else c
    static member atol (s: CPtr) : int64 = let mutable e = s in R.strtol(s, &e, 10)
    static member strtol0 (s: CPtr, b: int) : int64 = let mutable e = s in R.strtol(s, &e, b)
    static member strtol0 (s: CPtr) : int64 = R.strtol0(s, 10)
    static member ferror (f: CFile) : int = if not (isNull f) && f.Err then 1 else 0
    static member fileno (f: CFile) : int = 3
    static member setvbuf (f: CFile, b: CPtr, mode: int, size: uint64) : int = 0
    static member rename (a: CPtr, b: CPtr) : int = try File.Move(R.Str(a), R.Str(b), true); 0 with _ -> -1
    static member fgetc (f: CFile) : int = let c = f.S.ReadByte() in (if c < 0 then f.ReadFailed()); c
    static member time (p: obj) : int64 = DateTimeOffset.UtcNow.ToUnixTimeSeconds()

    static member strncpy (d: CPtr, s: CPtr, n: uint64) : CPtr =
        let mutable i = 0
        while i < int n && s.B.[s.O + i] <> 0uy do
            d.B.[d.O + i] <- s.B.[s.O + i]
            i <- i + 1
        while i < int n do
            d.B.[d.O + i] <- 0uy
            i <- i + 1
        d
    static member strncmp (a: CPtr, b: CPtr, n: uint64) : int =
        let mutable i = 0
        let mutable r = 0
        let mutable go = true
        while go && i < int n do
            let x = int a.B.[a.O + i]
            let y = int b.B.[b.O + i]
            if x <> y then r <- x - y; go <- false
            elif x = 0 then go <- false
            else i <- i + 1
        r
    static member memcmp (a: CPtr, b: CPtr, n: uint64) : int =
        let mutable i = 0
        let mutable r = 0
        while r = 0 && i < int n do
            let x = int a.B.[a.O + i]
            let y = int b.B.[b.O + i]
            if x <> y then r <- x - y
            i <- i + 1
        r
    /// char buf[n] = { ... }: n bytes, the given ones first, the rest zero.
    static member BufOf (n: int, vals: int[]) : CPtr =
        let b : byte[] = Array.zeroCreate (Math.Max(n, 1))
        let mutable i = 0
        while i < vals.Length && i < b.Length do
            b.[i] <- byte vals.[i]
            i <- i + 1
        CPtr(b, 0)
    /// Copy-assigning a char array member: all its bytes.
    static member memcpyBuf (d: CPtr, s: CPtr) = Array.Copy(s.B, s.O, d.B, d.O, Math.Min(d.B.Length - d.O, s.B.Length - s.O))
    static member sprintf (buf: CPtr, fmt: CPtr, [<ParamArray>] args: obj[]) : int =
        let b = R.Format(fmt, args)
        Array.Copy(b, 0, buf.B, buf.O, b.Length)
        buf.B.[buf.O + b.Length] <- 0uy
        b.Length

    // ---------------------------------------------------------------- Stash.cs: sscanf targets that are pointer parameters
    /// sscanf target that is a C pointer parameter (ref in C#): the value is stashed, then R.Back copies it.
    static member Slot with get () : ScanSlot = (if isNull R.slot0 then R.slot0 <- ScanSlot()); R.slot0
    static member StashReset () = (if isNull R.stash then R.stash <- List<obj>()); R.stash.Clear()
    static member Stash (v: obj) = R.stash.Add(v)
    static member private Take<'T> (i: int, a: byref<'T>) =
        if not (isNull R.stash) && i < R.stash.Count && not (isNull R.stash.[i]) then a <- Convert.ChangeType(R.stash.[i], typeof<'T>) :?> 'T
    static member Back<'T1> (n: int, a: byref<'T1>) : int = R.Take(0, &a); n
    static member Back<'T1, 'T2> (n: int, a: byref<'T1>, b: byref<'T2>) : int = R.Take(0, &a); R.Take(1, &b); n
    static member Back<'T1, 'T2, 'T3> (n: int, a: byref<'T1>, b: byref<'T2>, c: byref<'T3>) : int = R.Take(0, &a); R.Take(1, &b); R.Take(2, &c); n
    static member Back<'T1, 'T2, 'T3, 'T4> (n: int, a: byref<'T1>, b: byref<'T2>, c: byref<'T3>, d: byref<'T4>) : int =
        R.Take(0, &a); R.Take(1, &b); R.Take(2, &c); R.Take(3, &d); n
    static member Back<'T1, 'T2, 'T3, 'T4, 'T5> (n: int, a: byref<'T1>, b: byref<'T2>, c: byref<'T3>, d: byref<'T4>, e: byref<'T5>) : int =
        R.Take(0, &a); R.Take(1, &b); R.Take(2, &c); R.Take(3, &d); R.Take(4, &e); n
    static member Back<'T1, 'T2, 'T3, 'T4, 'T5, 'T6> (n: int, a: byref<'T1>, b: byref<'T2>, c: byref<'T3>, d: byref<'T4>, e: byref<'T5>, f: byref<'T6>) : int =
        R.Take(0, &a); R.Take(1, &b); R.Take(2, &c); R.Take(3, &d); R.Take(4, &e); R.Take(5, &f); n

[<Sealed; AllowNullLiteral>]
type ScanSlot() = class end

/// The state of R.ScanFloat (glibc vfscanf's floating-point scanner), whose C# local functions assign it.
[<Sealed; AllowNullLiteral>]
type FloatScan(b: byte[], i: int, width: int) =
    let mutable left = if width = 0 then -1 else width        // characters still allowed (-1: no limit)
    let mutable p = i                                         // next index
    let buf = List<byte>(32)
    member val Num : byte[] = null with get, set
    member private this.Inchar () : int = if p < b.Length && b.[p] <> 0uy then (p <- p + 1; int b.[p - 1]) else -1
    member private this.Dec () = if left > 0 then left <- left - 1
    member private this.Lower (c: int) : int = if c >= int 'A' && c <= int 'Z' then c + 32 else c
    member private this.IsHex (c: int) : bool = (c >= int '0' && c <= int '9') || (this.Lower(c) >= int 'a' && this.Lower(c) <= int 'f')
    member private this.Expect (ch: int) : bool =
        if left = 0 then false
        else
            let c2 = this.Inchar ()
            if c2 < 0 || this.Lower(c2) <> ch then false
            else
                this.Dec ()
                buf.Add(byte c2)
                true
    member private this.Done () : int = buf.Add(0uy); this.Num <- buf.ToArray(); p
    member this.Run () : int =
        let mutable c = this.Inchar ()
        this.Dec ()
        let mutable gotSign = 0
        let mutable fail = false
        if c = int '-' || c = int '+' then
            gotSign <- 1
            buf.Add(byte c)
            if left = 0 then fail <- true
            else
                c <- this.Inchar ()
                if c < 0 then fail <- true
                else this.Dec ()
        if fail then -1
        elif this.Lower(c) = int 'n' then
            buf.Add(byte c)
            if not (this.Expect(int 'a')) || not (this.Expect(int 'n')) then -1
            else this.Done ()
        elif this.Lower(c) = int 'i' then
            buf.Add(byte c)
            if not (this.Expect(int 'n')) || not (this.Expect(int 'f')) then -1
            else
                let mutable bad = false
                if left <> 0 then
                    c <- this.Inchar ()
                    if c >= 0 then
                        if this.Lower(c) = int 'i' then
                            this.Dec ()
                            buf.Add(byte c)
                            if not (this.Expect(int 'n')) || not (this.Expect(int 'i')) || not (this.Expect(int 't')) || not (this.Expect(int 'y')) then bad <- true
                        else p <- p - 1                               // ungetc
                if bad then -1 else this.Done ()
        else
            let mutable expChar = int 'e'
            let mutable hexa = false
            let mutable gotDigit = false
            let mutable gotDot = false
            let mutable gotE = false
            if left <> 0 && c = int '0' then
                buf.Add(byte c)
                c <- this.Inchar ()
                this.Dec ()
                if left <> 0 && this.Lower(c) = int 'x' then
                    buf.Add(byte c)
                    hexa <- true
                    expChar <- int 'p'
                    c <- this.Inchar ()
                    this.Dec ()
                else gotDigit <- true
            let mutable go = true
            while go do
                let mutable took = true
                if c >= int '0' && c <= int '9' then
                    buf.Add(byte c)
                    gotDigit <- true
                elif not gotE && hexa && this.IsHex(c) then
                    buf.Add(byte c)
                    gotDigit <- true
                elif gotE && int buf.[buf.Count - 1] = expChar && (c = int '-' || c = int '+') then buf.Add(byte c)
                elif gotDigit && not gotE && this.Lower(c) = expChar then
                    buf.Add(byte expChar)
                    gotE <- true
                    gotDot <- true
                elif not gotDot && c = int '.' then
                    buf.Add(byte c)
                    gotDot <- true
                else
                    if c >= 0 then p <- p - 1                         // not part of the number: pushed back
                    took <- false
                    go <- false
                if took then
                    if left = 0 then go <- false
                    else
                        c <- this.Inchar ()
                        if c < 0 then go <- false
                        else this.Dec ()
            if buf.Count = gotSign || (hexa && buf.Count = 2 + gotSign) then -1
            else this.Done ()
