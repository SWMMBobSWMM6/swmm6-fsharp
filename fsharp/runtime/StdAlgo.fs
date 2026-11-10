// StdAlgo.fs - libstdc++'s sorting algorithms, step for step (GCC 13 bits/stl_algo.h and bits/stl_heap.h), translated
// by hand from cs/runtime/StdAlgo.cs. std::sort is an introsort and is not stable: with equal keys the order it leaves
// depends on every swap it makes, so the port has to make the same ones. std::stable_sort's result is fixed by
// stability alone. Checked against libstdc++ by tests/stdcheck (fsharp/test/run_runtime_checks.sh).
// Written by Claude (Anthropic) for Robert Dickinson's SWMM6 ports; see AI-USE.md and fsharp/README.md.
namespace Cpp

open System

[<AbstractClass; Sealed>]
type StdAlgo =
    static member private Threshold = 16L

    static member private Lg (n: int64) : int = 63 - System.Numerics.BitOperations.LeadingZeroCount(uint64 n)

    static member private Swap<'T> (a: 'T[], i: int64, j: int64) =
        let t = a.[int i]
        a.[int i] <- a.[int j]
        a.[int j] <- t

    /// std::sort(first, last, comp) over a[first..last).
    static member Sort<'T> (a: 'T[], first: int64, last: int64, comp: Func<'T, 'T, bool>) : unit =
        if first <> last then
            StdAlgo.IntrosortLoop(a, first, last, int64 (StdAlgo.Lg(last - first)) * 2L, comp)
            StdAlgo.FinalInsertionSort(a, first, last, comp)

    static member private IntrosortLoop<'T> (a: 'T[], first: int64, last0: int64, depth0: int64, comp: Func<'T, 'T, bool>) : unit =
        let mutable last = last0
        let mutable depth = depth0
        let mutable go = true
        while go && last - first > StdAlgo.Threshold do
            if depth = 0L then
                StdAlgo.PartialSort(a, first, last, last, comp)
                go <- false
            else
                depth <- depth - 1L
                let cut = StdAlgo.UnguardedPartitionPivot(a, first, last, comp)
                StdAlgo.IntrosortLoop(a, cut, last, depth, comp)
                last <- cut

    static member private UnguardedPartitionPivot<'T> (a: 'T[], first: int64, last: int64, comp: Func<'T, 'T, bool>) : int64 =
        let mid = first + (last - first) / 2L
        StdAlgo.MoveMedianToFirst(a, first, first + 1L, mid, last - 1L, comp)
        StdAlgo.UnguardedPartition(a, first + 1L, last, first, comp)

    static member private MoveMedianToFirst<'T> (x: 'T[], result: int64, a: int64, b: int64, c: int64, comp: Func<'T, 'T, bool>) : unit =
        if comp.Invoke(x.[int a], x.[int b]) then
            if comp.Invoke(x.[int b], x.[int c]) then StdAlgo.Swap(x, result, b)
            elif comp.Invoke(x.[int a], x.[int c]) then StdAlgo.Swap(x, result, c)
            else StdAlgo.Swap(x, result, a)
        elif comp.Invoke(x.[int a], x.[int c]) then StdAlgo.Swap(x, result, a)
        elif comp.Invoke(x.[int b], x.[int c]) then StdAlgo.Swap(x, result, c)
        else StdAlgo.Swap(x, result, b)

    static member private UnguardedPartition<'T> (a: 'T[], first0: int64, last0: int64, pivot: int64, comp: Func<'T, 'T, bool>) : int64 =
        let mutable first = first0
        let mutable last = last0
        let mutable result = -1L
        while result < 0L do
            while comp.Invoke(a.[int first], a.[int pivot]) do first <- first + 1L
            last <- last - 1L
            while comp.Invoke(a.[int pivot], a.[int last]) do last <- last - 1L
            if not (first < last) then result <- first
            else
                StdAlgo.Swap(a, first, last)
                first <- first + 1L
        result

    static member private FinalInsertionSort<'T> (a: 'T[], first: int64, last: int64, comp: Func<'T, 'T, bool>) : unit =
        if last - first > StdAlgo.Threshold then
            StdAlgo.InsertionSort(a, first, first + StdAlgo.Threshold, comp)
            let mutable i = first + StdAlgo.Threshold
            while i <> last do
                StdAlgo.UnguardedLinearInsert(a, i, comp)
                i <- i + 1L
        else StdAlgo.InsertionSort(a, first, last, comp)

    static member private InsertionSort<'T> (a: 'T[], first: int64, last: int64, comp: Func<'T, 'T, bool>) : unit =
        if first <> last then
            let mutable i = first + 1L
            while i <> last do
                if comp.Invoke(a.[int i], a.[int first]) then
                    let v = a.[int i]
                    Array.Copy(a, first, a, first + 1L, i - first)     // move_backward(first, i, i + 1)
                    a.[int first] <- v
                else StdAlgo.UnguardedLinearInsert(a, i, comp)
                i <- i + 1L

    static member private UnguardedLinearInsert<'T> (a: 'T[], last0: int64, comp: Func<'T, 'T, bool>) : unit =
        let mutable last = last0
        let v = a.[int last]
        let mutable next = last - 1L
        while comp.Invoke(v, a.[int next]) do
            a.[int last] <- a.[int next]
            last <- next
            next <- next - 1L
        a.[int last] <- v

    static member PartialSort<'T> (a: 'T[], first: int64, middle: int64, last: int64, comp: Func<'T, 'T, bool>) : unit =
        StdAlgo.HeapSelect(a, first, middle, last, comp)
        StdAlgo.SortHeap(a, first, middle, comp)

    static member private HeapSelect<'T> (a: 'T[], first: int64, middle: int64, last: int64, comp: Func<'T, 'T, bool>) : unit =
        StdAlgo.MakeHeap(a, first, middle, comp)
        let mutable i = middle
        while i < last do
            if comp.Invoke(a.[int i], a.[int first]) then StdAlgo.PopHeap(a, first, middle, i, comp)
            i <- i + 1L

    static member private MakeHeap<'T> (a: 'T[], first: int64, last: int64, comp: Func<'T, 'T, bool>) : unit =
        if last - first >= 2L then
            let len = last - first
            let mutable parent = (len - 2L) / 2L
            let mutable go = true
            while go do
                let v = a.[int (first + parent)]
                StdAlgo.AdjustHeap(a, first, parent, len, v, comp)
                if parent = 0L then go <- false
                else parent <- parent - 1L

    static member private PopHeap<'T> (a: 'T[], first: int64, last: int64, result: int64, comp: Func<'T, 'T, bool>) : unit =
        let v = a.[int result]
        a.[int result] <- a.[int first]
        StdAlgo.AdjustHeap(a, first, 0L, last - first, v, comp)

    static member private SortHeap<'T> (a: 'T[], first: int64, last0: int64, comp: Func<'T, 'T, bool>) : unit =
        let mutable last = last0
        while last - first > 1L do
            last <- last - 1L
            StdAlgo.PopHeap(a, first, last, last, comp)

    static member private AdjustHeap<'T> (a: 'T[], first: int64, hole0: int64, len: int64, v: 'T, comp: Func<'T, 'T, bool>) : unit =
        let mutable hole = hole0
        let top = hole
        let mutable second = hole
        while second < (len - 1L) / 2L do
            second <- 2L * (second + 1L)
            if comp.Invoke(a.[int (first + second)], a.[int (first + (second - 1L))]) then second <- second - 1L
            a.[int (first + hole)] <- a.[int (first + second)]
            hole <- second
        if (len &&& 1L) = 0L && second = (len - 2L) / 2L then
            second <- 2L * (second + 1L)
            a.[int (first + hole)] <- a.[int (first + (second - 1L))]
            hole <- second - 1L
        let mutable parent = (hole - 1L) / 2L
        while hole > top && comp.Invoke(a.[int (first + parent)], v) do
            a.[int (first + hole)] <- a.[int (first + parent)]
            hole <- parent
            parent <- (hole - 1L) / 2L
        a.[int (first + hole)] <- v

    /// std::stable_sort: any stable sort gives libstdc++'s result.
    static member StableSort<'T> (a: 'T[], first: int64, last: int64, comp: Func<'T, 'T, bool>) : unit =
        let n = last - first
        if n >= 2L then
            let buf : 'T[] = Array.zeroCreate (int n)
            StdAlgo.MergeSort(a, first, last, buf, comp)

    static member private MergeSort<'T> (a: 'T[], lo: int64, hi: int64, buf: 'T[], comp: Func<'T, 'T, bool>) : unit =
        if hi - lo >= 2L then
            let mid = lo + (hi - lo) / 2L
            StdAlgo.MergeSort(a, lo, mid, buf, comp)
            StdAlgo.MergeSort(a, mid, hi, buf, comp)
            let mutable i = lo
            let mutable j = mid
            let mutable k = 0L
            while i < mid && j < hi do
                // take the right one only if strictly less
                if comp.Invoke(a.[int j], a.[int i]) then
                    buf.[int k] <- a.[int j]
                    j <- j + 1L
                else
                    buf.[int k] <- a.[int i]
                    i <- i + 1L
                k <- k + 1L
            while i < mid do
                buf.[int k] <- a.[int i]
                k <- k + 1L
                i <- i + 1L
            while j < hi do
                buf.[int k] <- a.[int j]
                k <- k + 1L
                j <- j + 1L
            Array.Copy(buf, 0L, a, lo, k)
