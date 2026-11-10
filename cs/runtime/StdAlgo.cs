// StdAlgo.cs - libstdc++'s sorting algorithms, step for step (GCC 13 bits/stl_algo.h and bits/stl_heap.h).
// std::sort is an introsort and is not stable: with equal keys the order it leaves depends on every swap it
// makes, so the C# port has to make the same ones. std::stable_sort's result is fixed by stability alone.
// Checked against libstdc++ by tests/stdcheck (random keys with many ties, all lengths 0..300 and larger).
// Written by Claude (Anthropic) for the SWMM6 ports; see AI-USE.md.
using System;

namespace Cpp
{
    public static class StdAlgo
    {
        const int Threshold = 16;

        static int Lg(long n) => 63 - System.Numerics.BitOperations.LeadingZeroCount((ulong)n);

        static void Swap<T>(T[] a, long i, long j) { T t = a[i]; a[i] = a[j]; a[j] = t; }

        /// <summary>std::sort(first, last, comp) over a[first..last).</summary>
        public static void Sort<T>(T[] a, long first, long last, Func<T, T, bool> comp)
        {
            if (first != last)
            {
                IntrosortLoop(a, first, last, (long)Lg(last - first) * 2, comp);
                FinalInsertionSort(a, first, last, comp);
            }
        }

        static void IntrosortLoop<T>(T[] a, long first, long last, long depth, Func<T, T, bool> comp)
        {
            while (last - first > Threshold)
            {
                if (depth == 0)
                {
                    PartialSort(a, first, last, last, comp);
                    return;
                }
                --depth;
                long cut = UnguardedPartitionPivot(a, first, last, comp);
                IntrosortLoop(a, cut, last, depth, comp);
                last = cut;
            }
        }

        static long UnguardedPartitionPivot<T>(T[] a, long first, long last, Func<T, T, bool> comp)
        {
            long mid = first + (last - first) / 2;
            MoveMedianToFirst(a, first, first + 1, mid, last - 1, comp);
            return UnguardedPartition(a, first + 1, last, first, comp);
        }

        static void MoveMedianToFirst<T>(T[] x, long result, long a, long b, long c, Func<T, T, bool> comp)
        {
            if (comp(x[a], x[b]))
            {
                if (comp(x[b], x[c])) Swap(x, result, b);
                else if (comp(x[a], x[c])) Swap(x, result, c);
                else Swap(x, result, a);
            }
            else if (comp(x[a], x[c])) Swap(x, result, a);
            else if (comp(x[b], x[c])) Swap(x, result, c);
            else Swap(x, result, b);
        }

        static long UnguardedPartition<T>(T[] a, long first, long last, long pivot, Func<T, T, bool> comp)
        {
            while (true)
            {
                while (comp(a[first], a[pivot])) ++first;
                --last;
                while (comp(a[pivot], a[last])) --last;
                if (!(first < last)) return first;
                Swap(a, first, last);
                ++first;
            }
        }

        static void FinalInsertionSort<T>(T[] a, long first, long last, Func<T, T, bool> comp)
        {
            if (last - first > Threshold)
            {
                InsertionSort(a, first, first + Threshold, comp);
                for (long i = first + Threshold; i != last; ++i) UnguardedLinearInsert(a, i, comp);
            }
            else InsertionSort(a, first, last, comp);
        }

        static void InsertionSort<T>(T[] a, long first, long last, Func<T, T, bool> comp)
        {
            if (first == last) return;
            for (long i = first + 1; i != last; ++i)
            {
                if (comp(a[i], a[first]))
                {
                    T val = a[i];
                    Array.Copy(a, first, a, first + 1, i - first);   // move_backward(first, i, i + 1)
                    a[first] = val;
                }
                else UnguardedLinearInsert(a, i, comp);
            }
        }

        static void UnguardedLinearInsert<T>(T[] a, long last, Func<T, T, bool> comp)
        {
            T val = a[last];
            long next = last - 1;
            while (comp(val, a[next]))
            {
                a[last] = a[next];
                last = next;
                --next;
            }
            a[last] = val;
        }

        public static void PartialSort<T>(T[] a, long first, long middle, long last, Func<T, T, bool> comp)
        {
            HeapSelect(a, first, middle, last, comp);
            SortHeap(a, first, middle, comp);
        }

        static void HeapSelect<T>(T[] a, long first, long middle, long last, Func<T, T, bool> comp)
        {
            MakeHeap(a, first, middle, comp);
            for (long i = middle; i < last; ++i)
                if (comp(a[i], a[first])) PopHeap(a, first, middle, i, comp);
        }

        static void MakeHeap<T>(T[] a, long first, long last, Func<T, T, bool> comp)
        {
            if (last - first < 2) return;
            long len = last - first, parent = (len - 2) / 2;
            while (true)
            {
                T value = a[first + parent];
                AdjustHeap(a, first, parent, len, value, comp);
                if (parent == 0) return;
                parent--;
            }
        }

        static void PopHeap<T>(T[] a, long first, long last, long result, Func<T, T, bool> comp)
        {
            T value = a[result];
            a[result] = a[first];
            AdjustHeap(a, first, 0, last - first, value, comp);
        }

        static void SortHeap<T>(T[] a, long first, long last, Func<T, T, bool> comp)
        {
            while (last - first > 1)
            {
                --last;
                PopHeap(a, first, last, last, comp);
            }
        }

        static void AdjustHeap<T>(T[] a, long first, long hole, long len, T value, Func<T, T, bool> comp)
        {
            long top = hole, second = hole;
            while (second < (len - 1) / 2)
            {
                second = 2 * (second + 1);
                if (comp(a[first + second], a[first + (second - 1)])) second--;
                a[first + hole] = a[first + second];
                hole = second;
            }
            if ((len & 1) == 0 && second == (len - 2) / 2)
            {
                second = 2 * (second + 1);
                a[first + hole] = a[first + (second - 1)];
                hole = second - 1;
            }
            long parent = (hole - 1) / 2;
            while (hole > top && comp(a[first + parent], value))
            {
                a[first + hole] = a[first + parent];
                hole = parent;
                parent = (hole - 1) / 2;
            }
            a[first + hole] = value;
        }

        /// <summary>std::stable_sort: any stable sort gives libstdc++'s result.</summary>
        public static void StableSort<T>(T[] a, long first, long last, Func<T, T, bool> comp)
        {
            long n = last - first;
            if (n < 2) return;
            T[] buf = new T[n];
            MergeSort(a, first, last, buf, comp);
        }

        static void MergeSort<T>(T[] a, long lo, long hi, T[] buf, Func<T, T, bool> comp)
        {
            if (hi - lo < 2) return;
            long mid = lo + (hi - lo) / 2;
            MergeSort(a, lo, mid, buf, comp);
            MergeSort(a, mid, hi, buf, comp);
            long i = lo, j = mid, k = 0;
            while (i < mid && j < hi)
                buf[k++] = comp(a[j], a[i]) ? a[j++] : a[i++];      // take the right one only if strictly less
            while (i < mid) buf[k++] = a[i++];
            while (j < hi) buf[k++] = a[j++];
            Array.Copy(buf, 0, a, lo, k);
        }
    }
}
