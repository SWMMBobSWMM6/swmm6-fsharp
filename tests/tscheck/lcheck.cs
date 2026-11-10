// lcheck.cs - the C# side of lcheck.ts: every 64-bit and 32-bit integer operation cpp.L implements (C# unchecked
// arithmetic, bitwise operations, shifts, conversions) and the x86 double-to-integer conversions of Cpp.Conv, over
// edge values, every pair. run.sh requires lcheck.ts to print the same lines. Doubles print as their bit patterns
// (any NaN as "nan").
// Written by Claude (Anthropic) for the SWMM6 ports; see AI-USE.md.
using System;
using System.Text;
using Cpp;

public static class LCheck
{
    static readonly StringBuilder o = new StringBuilder();
    static void P(string s) { o.Append(s).Append('\n'); }
    static string T(Func<string> f)
    {
        try { return f(); }
        catch (DivideByZeroException) { return "throw:DivideByZeroException"; }
        catch (OverflowException) { return "throw:OverflowException"; }
    }
    static string D(double x) => double.IsNaN(x) ? "nan" : BitConverter.DoubleToInt64Bits(x).ToString("x16");     // a NaN's sign does not matter here

    static readonly long[] Ls = {
        0, 1, -1, 2, -2, 3, 7, -7, 100, -100, 2147483647, -2147483648, 2147483648, -2147483649, 4294967295, 4294967296,
        4503599627370496, 9007199254740991, -9007199254740991, 9007199254740992, -9007199254740992, 9007199254740993,
        -9007199254740993, 4611686018427387904, long.MaxValue, long.MinValue, long.MinValue + 1, 123456789012345678,
        -987654321098765432, 0x5555555555555555, 0x7FFFFFFFFFFFFFFE, 94906265, 3037000499, -3037000500,
    };
    static readonly ulong[] Us = {
        0, 1, 2, 3, 7, 100, 2147483647, 2147483648, 4294967295, 4294967296, 4503599627370496, 9007199254740991,
        9007199254740992, 9007199254740993, 4611686018427387904, 9223372036854775807, 9223372036854775808,
        9223372036854775809, ulong.MaxValue, ulong.MaxValue - 1, 0xAAAAAAAAAAAAAAAA, 12345678901234567890,
        0xcbf29ce484222325, 0x100000001b3, 4294967311, 0x9E3779B97F4A7C15,
    };
    static readonly int[] Is = { 0, 1, -1, 2, -2, 7, -7, 3, int.MaxValue, int.MinValue, int.MinValue + 1, 46341, 65536, 100000, -100000 };
    static readonly uint[] Ws = { 0, 1, 2, 7, 3, 2147483647, 2147483648, 4294967295, 4000000000, 65535, 65536 };
    static readonly int[] Sh = { 0, 1, 2, 7, 31, 32, 33, 52, 53, 62, 63, 64, 65, -1, 127 };
    // values whose float conversion rounds near a tie (a second rounding through double would go wrong)
    static readonly long[] F32L = {
        16777217, 33554435, (1L << 60) + (1L << 36) + 1, (1L << 60) + (1L << 36), (1L << 60) + (3L << 36), (1L << 60) + (3L << 36) - 1,
        -((1L << 60) + (1L << 36) + 1), 0x7FFFFFC000000000, 0x7FFFFFBFFFFFFFFF, 0x7FFFFFE000000000, (1L << 55) + (1L << 31) + 1,
        (1L << 54) + 1, -((1L << 54) + 3),
    };
    static readonly ulong[] F32U = {
        0xFFFFFF8000000000, 0xFFFFFF7FFFFFFFFF, 0xFFFFFF8000000001, 0xFFFFFFFFFFFFFFFF, 0x8000008000000000, 0x8000008000000001,
        0x8000018000000000, (1UL << 63) + (1UL << 39) + 1,
    };
    static readonly double[] Ds = {
        0.0, -0.0, 0.5, -0.5, 1.5, -1.5, 2.5, 1e10, -1e10, 2147483647.0, 2147483647.9, 2147483648.0, -2147483648.0,
        -2147483648.9, -2147483649.0, 4294967295.0, 4294967296.0, 9223372036854775808.0, -9223372036854775808.0,
        9223372036854774784.0, 18446744073709551616.0, 18446744073709549568.0, 1e19, 1e20, -1e19, double.NaN, -double.NaN,
        double.PositiveInfinity, double.NegativeInfinity, 5e-324, 123.456, -123.456, 127.9, 128.0, -128.5, 255.5, 256.0,
        65535.5, 32767.9, -32768.9, 4503599627370495.5, 9007199254740994.0, 1.7976931348623157e308, -1.0, -3e9, 3e9,
        -4294967296.5, 9007199254740991.0, -9007199254740991.0, 9007199254740992.0, -9007199254740992.0,
    };

    public static void Main()
    {
        foreach (var a in Ls) foreach (var b in Ls)
        {
            string p = a + " " + b + " ";
            P("addL " + p + unchecked(a + b));
            P("subL " + p + unchecked(a - b));
            P("mulL " + p + unchecked(a * b));
            P("divL " + p + T(() => (a / b).ToString()));
            P("remL " + p + T(() => (a % b).ToString()));
            P("andL " + p + (a & b));
            P("orL " + p + (a | b));
            P("xorL " + p + (a ^ b));
        }
        foreach (var a in Ls) foreach (var n in Sh)
        {
            string p = a + " " + n + " ";
            P("shlL " + p + (a << n));
            P("shrL " + p + (a >> n));
            P("ushrL " + p + (a >>> n));
        }
        foreach (var a in Ls)
        {
            P("negL " + a + " " + unchecked(-a));
            P("notL " + a + " " + ~a);
            P("incL " + a + " " + unchecked(a + 1));
            P("decL " + a + " " + unchecked(a - 1));
            P("toI32 " + a + " " + unchecked((int)a));
            P("toU32 " + a + " " + unchecked((uint)a));
            P("toI16 " + a + " " + unchecked((short)a));
            P("toU16 " + a + " " + unchecked((ushort)a));
            P("toI8 " + a + " " + unchecked((sbyte)a));
            P("toU8 " + a + " " + unchecked((byte)a));
            P("toU64 " + a + " " + unchecked((ulong)a));
            P("toF64 " + a + " " + D((double)a));
            P("toF32 " + a + " " + D((float)a));
        }
        foreach (var a in F32L) P("toF32 " + a + " " + D((float)a));
        foreach (var a in Us) foreach (var b in Us)
        {
            string p = a + " " + b + " ";
            P("addUL " + p + unchecked(a + b));
            P("subUL " + p + unchecked(a - b));
            P("mulUL " + p + unchecked(a * b));
            P("divUL " + p + T(() => (a / b).ToString()));
            P("remUL " + p + T(() => (a % b).ToString()));
            P("andUL " + p + (a & b));
            P("orUL " + p + (a | b));
            P("xorUL " + p + (a ^ b));
        }
        foreach (var a in Us) foreach (var n in Sh)
        {
            string p = a + " " + n + " ";
            P("shlUL " + p + (a << n));
            P("shrUL " + p + (a >> n));
        }
        foreach (var a in Us)
        {
            P("negUL " + a + " " + unchecked(0UL - a));
            P("notUL " + a + " " + ~a);
            P("incUL " + a + " " + unchecked(a + 1));
            P("decUL " + a + " " + unchecked(a - 1));
            P("toI32 " + a + " " + unchecked((int)a));
            P("toU32 " + a + " " + unchecked((uint)a));
            P("toI16 " + a + " " + unchecked((short)a));
            P("toU16 " + a + " " + unchecked((ushort)a));
            P("toI8 " + a + " " + unchecked((sbyte)a));
            P("toU8 " + a + " " + unchecked((byte)a));
            P("toI64 " + a + " " + unchecked((long)a));
            P("toF64 " + a + " " + D((double)a));
            P("toF32 " + a + " " + D((float)a));
        }
        foreach (var a in F32U) P("toF32 " + a + " " + D((float)a));
        foreach (var a in Is)
        {
            P("toU64 " + a + " " + unchecked((ulong)a));
            foreach (var b in Is)
            {
                string p = a + " " + b + " ";
                P("divI " + p + T(() => (a / b).ToString()));
                P("remI " + p + T(() => (a % b).ToString()));
            }
        }
        foreach (var a in Ws) foreach (var b in Ws)
        {
            string p = a + " " + b + " ";
            P("divU " + p + T(() => (a / b).ToString()));
            P("remU " + p + T(() => (a % b).ToString()));
        }
        foreach (var x in Ds)
        {
            string p = D(x) + " ";
            P("D2I " + p + Conv.D2I(x));
            P("D2L " + p + Conv.D2L(x));
            P("D2U " + p + Conv.D2U(x));
            P("D2UL " + p + Conv.D2UL(x));
            P("D2S " + p + Conv.D2S(x));
            P("D2US " + p + Conv.D2US(x));
            P("D2SB " + p + Conv.D2SB(x));
            P("D2B " + p + Conv.D2B(x));
            P("RD2I " + p + R.D2I(x));
            P("RD2L " + p + R.D2L(x));
        }
        Console.Out.Write(o.ToString());
    }
}
