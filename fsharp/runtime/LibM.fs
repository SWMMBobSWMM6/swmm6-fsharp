// LibM.fs - the C math library the engine calls: glibc's own routines in the system's libm.so.6, by P/Invoke, as the
// C++ engine calls them. The C# runtime's LibM class (cs/runtime/Misc.cs) declares hypot, fmod, round, ... the same way;
// the C# engine reaches exp, pow, sin and the rest through .NET's Math, which the F# translator replaces with the
// functions here (tools/cs2x/FsFn.cs, LibmCalls), so every rounded result is glibc's. dlsym finds each name's default
// symbol version, the one the C++ engine links against (exp@GLIBC_2.29, pow@GLIBC_2.29, sin@GLIBC_2.2.5, ...;
// fsharp/test/check_binding.sh). SuppressGCTransition makes each call a plain call: the routines neither block nor
// call back into .NET. sqrt, fabs, floor, ceil, trunc and copysign are exact (IEEE fixes their results) and come
// from .NET's Math.
// Written by Claude (Anthropic) for Robert Dickinson's SWMM6 ports; see AI-USE.md and fsharp/README.md.
namespace Cpp

open System.Runtime.InteropServices

module LibM =
    [<Literal>]
    let private M = "libm.so.6"

    // set 1 (tools/libm/README.md)
    [<DllImport(M, CallingConvention = CallingConvention.Cdecl); SuppressGCTransition>]
    extern double exp(double x)
    [<DllImport(M, CallingConvention = CallingConvention.Cdecl); SuppressGCTransition>]
    extern double log(double x)
    [<DllImport(M, CallingConvention = CallingConvention.Cdecl); SuppressGCTransition>]
    extern double log10(double x)
    [<DllImport(M, CallingConvention = CallingConvention.Cdecl); SuppressGCTransition>]
    extern double pow(double x, double y)
    [<DllImport(M, CallingConvention = CallingConvention.Cdecl); SuppressGCTransition>]
    extern double sin(double x)
    [<DllImport(M, CallingConvention = CallingConvention.Cdecl); SuppressGCTransition>]
    extern double cos(double x)
    [<DllImport(M, CallingConvention = CallingConvention.Cdecl); SuppressGCTransition>]
    extern double tan(double x)
    [<DllImport(M, CallingConvention = CallingConvention.Cdecl); SuppressGCTransition>]
    extern double asin(double x)
    [<DllImport(M, CallingConvention = CallingConvention.Cdecl); SuppressGCTransition>]
    extern double acos(double x)
    [<DllImport(M, CallingConvention = CallingConvention.Cdecl); SuppressGCTransition>]
    extern double atan(double x)
    // set 2
    [<DllImport(M, CallingConvention = CallingConvention.Cdecl); SuppressGCTransition>]
    extern double atan2(double y, double x)
    [<DllImport(M, CallingConvention = CallingConvention.Cdecl); SuppressGCTransition>]
    extern double log2(double x)
    [<DllImport(M, CallingConvention = CallingConvention.Cdecl); SuppressGCTransition>]
    extern double sinh(double x)
    [<DllImport(M, CallingConvention = CallingConvention.Cdecl); SuppressGCTransition>]
    extern double tanh(double x)
    [<DllImport(M, CallingConvention = CallingConvention.Cdecl); SuppressGCTransition>]
    extern double expm1(double x)
    [<DllImport(M, CallingConvention = CallingConvention.Cdecl); SuppressGCTransition>]
    extern double log1p(double x)
    [<DllImport(M, CallingConvention = CallingConvention.Cdecl); SuppressGCTransition>]
    extern double cosh(double x)
    [<DllImport(M, CallingConvention = CallingConvention.Cdecl); SuppressGCTransition>]
    extern double cbrt(double x)
    [<DllImport(M, CallingConvention = CallingConvention.Cdecl); SuppressGCTransition>]
    extern double hypot(double x, double y)
    // the rest of the C# runtime's LibM
    [<DllImport(M, CallingConvention = CallingConvention.Cdecl); SuppressGCTransition>]
    extern double fmod(double x, double y)
    [<DllImport(M, CallingConvention = CallingConvention.Cdecl); SuppressGCTransition>]
    extern double round(double x)
    [<DllImport(M, CallingConvention = CallingConvention.Cdecl); SuppressGCTransition>]
    extern int64 lround(double x)
    [<DllImport(M, CallingConvention = CallingConvention.Cdecl); SuppressGCTransition>]
    extern double rint(double x)
    [<DllImport(M, CallingConvention = CallingConvention.Cdecl); SuppressGCTransition>]
    extern double nearbyint(double x)
    [<DllImport(M, CallingConvention = CallingConvention.Cdecl); SuppressGCTransition>]
    extern double erf(double x)
    [<DllImport(M, CallingConvention = CallingConvention.Cdecl); SuppressGCTransition>]
    extern double erfc(double x)
    [<DllImport(M, CallingConvention = CallingConvention.Cdecl); SuppressGCTransition>]
    extern double tgamma(double x)
    [<DllImport(M, CallingConvention = CallingConvention.Cdecl); SuppressGCTransition>]
    extern double lgamma(double x)
    [<DllImport(M, CallingConvention = CallingConvention.Cdecl); SuppressGCTransition>]
    extern double exp2(double x)
    [<DllImport(M, CallingConvention = CallingConvention.Cdecl); SuppressGCTransition>]
    extern double remainder(double x, double y)
    [<DllImport(M, CallingConvention = CallingConvention.Cdecl); SuppressGCTransition>]
    extern double fdim(double x, double y)
    [<DllImport(M, CallingConvention = CallingConvention.Cdecl); SuppressGCTransition>]
    extern double fmax(double x, double y)
    [<DllImport(M, CallingConvention = CallingConvention.Cdecl); SuppressGCTransition>]
    extern double fmin(double x, double y)

    // nextafter: the C# runtime computes it in C# (cs/runtime/Misc.cs), whose `x + y` for two NaN arguments may give
    // y's NaN where glibc gives x's (fsharp/test/run_runtime_checks.sh, libm); here it is glibc's own, as the rest.
    [<DllImport(M, CallingConvention = CallingConvention.Cdecl); SuppressGCTransition>]
    extern double nextafter(double x, double y)
