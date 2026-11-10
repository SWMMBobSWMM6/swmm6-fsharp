// Hand_Simd.cs - simd::multiply, written by hand (tools/cpp2x/emit_cs.py HAND).
// src/engine/math/SIMD.hpp multiplies two arrays four lanes at a time with AVX2 (_mm256_mul_pd) and finishes with a
// scalar loop. Each lane is one IEEE-754 double multiplication, rounded the same way as a scalar one (the build has
// -ffp-contract=off and there is no add to fuse), so one scalar loop gives the same bits.
// Written by Claude (Anthropic) for the SWMM6 ports; see AI-USE.md.
namespace openswmm.simd
{
    public static partial class _F
    {
        public static void multiply(global::Cpp.Ptr<double> a, global::Cpp.Ptr<double> b, global::Cpp.Ptr<double> dst, ulong n)
        {
            for (ulong i = 0; i < n; ++i)
                dst[i] = a[i] * b[i];
        }
    }
}
