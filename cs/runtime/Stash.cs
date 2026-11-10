// Stash.cs - sscanf targets that are pointer parameters, from the C# port of EPA SWMM 5.2.4 (swmm5-csharp).
// Written by Claude (Anthropic); see AI-USE.md.
using System;
using System.Collections.Generic;

namespace Cpp
{
    public sealed class ScanSlot { }
    public static partial class R
    {
        /// <summary>sscanf target that is a C pointer parameter (ref in C#): the value is stashed, then R.Back copies it.</summary>
        public static readonly ScanSlot Slot = new ScanSlot();
        [ThreadStatic] static List<object> stash;
        internal static void StashReset() { (stash ??= new List<object>()).Clear(); }
        internal static void Stash(object v) { stash.Add(v); }
        static void Take<T>(int i, ref T a) { if (stash != null && i < stash.Count && stash[i] != null) a = (T)Convert.ChangeType(stash[i], typeof(T)); }
        public static int Back<T1>(int n, ref T1 a) { Take(0, ref a); return n; }
        public static int Back<T1, T2>(int n, ref T1 a, ref T2 b) { Take(0, ref a); Take(1, ref b); return n; }
        public static int Back<T1, T2, T3>(int n, ref T1 a, ref T2 b, ref T3 c) { Take(0, ref a); Take(1, ref b); Take(2, ref c); return n; }
        public static int Back<T1, T2, T3, T4>(int n, ref T1 a, ref T2 b, ref T3 c, ref T4 d) { Take(0, ref a); Take(1, ref b); Take(2, ref c); Take(3, ref d); return n; }
        public static int Back<T1, T2, T3, T4, T5>(int n, ref T1 a, ref T2 b, ref T3 c, ref T4 d, ref T5 e) { Take(0, ref a); Take(1, ref b); Take(2, ref c); Take(3, ref d); Take(4, ref e); return n; }
        public static int Back<T1, T2, T3, T4, T5, T6>(int n, ref T1 a, ref T2 b, ref T3 c, ref T4 d, ref T5 e, ref T6 f) { Take(0, ref a); Take(1, ref b); Take(2, ref c); Take(3, ref d); Take(4, ref e); Take(5, ref f); return n; }
    }
}
