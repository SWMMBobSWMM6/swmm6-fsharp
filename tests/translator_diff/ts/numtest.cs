// differential test of the C# -> TypeScript numeric translation (scratch, not part of the ports)
namespace openswmm
{
    public enum E : sbyte { A = 1, B = 100, C = -5 }
    public static class NumTest
    {
        public static long IAdd(long x, long y) { unchecked { int a = (int)x; int b = (int)y; return a + b; } }
        public static long UAdd(long x, long y) { unchecked { uint a = (uint)x; uint b = (uint)y; return a + b; } }
        public static long LAdd(long x, long y) { unchecked { long a = x; long b = y; return a + b; } }
        public static ulong ULAdd(long x, long y) { unchecked { ulong a = (ulong)x; ulong b = (ulong)y; return a + b; } }
        public static long SAdd(long x, long y) { unchecked { short a = (short)x; short b = (short)y; short c = (short)(a + b); return c; } }
        public static long BAdd(long x, long y) { unchecked { byte a = (byte)x; byte b = (byte)y; byte c = (byte)(a + b); return c; } }
        public static long SBAdd(long x, long y) { unchecked { sbyte a = (sbyte)x; sbyte b = (sbyte)y; a += b; return a; } }
        public static long USAdd(long x, long y) { unchecked { ushort a = (ushort)x; ushort b = (ushort)y; a += b; return a; } }
        public static long CIAdd(long x, long y) { unchecked { int a = (int)x; int b = (int)y; a += b; return a; } }
        public static long CUAdd(long x, long y) { unchecked { uint a = (uint)x; uint b = (uint)y; a += b; return a; } }
        public static long CLAdd(long x, long y) { unchecked { long a = x; a += y; return a; } }
        public static ulong CULAdd(long x, long y) { unchecked { ulong a = (ulong)x; a += (ulong)y; return a; } }
        public static long ISub(long x, long y) { unchecked { int a = (int)x; int b = (int)y; return a - b; } }
        public static long USub(long x, long y) { unchecked { uint a = (uint)x; uint b = (uint)y; return a - b; } }
        public static long LSub(long x, long y) { unchecked { long a = x; long b = y; return a - b; } }
        public static ulong ULSub(long x, long y) { unchecked { ulong a = (ulong)x; ulong b = (ulong)y; return a - b; } }
        public static long SSub(long x, long y) { unchecked { short a = (short)x; short b = (short)y; short c = (short)(a - b); return c; } }
        public static long BSub(long x, long y) { unchecked { byte a = (byte)x; byte b = (byte)y; byte c = (byte)(a - b); return c; } }
        public static long SBSub(long x, long y) { unchecked { sbyte a = (sbyte)x; sbyte b = (sbyte)y; a -= b; return a; } }
        public static long USSub(long x, long y) { unchecked { ushort a = (ushort)x; ushort b = (ushort)y; a -= b; return a; } }
        public static long CISub(long x, long y) { unchecked { int a = (int)x; int b = (int)y; a -= b; return a; } }
        public static long CUSub(long x, long y) { unchecked { uint a = (uint)x; uint b = (uint)y; a -= b; return a; } }
        public static long CLSub(long x, long y) { unchecked { long a = x; a -= y; return a; } }
        public static ulong CULSub(long x, long y) { unchecked { ulong a = (ulong)x; a -= (ulong)y; return a; } }
        public static long IMul(long x, long y) { unchecked { int a = (int)x; int b = (int)y; return a * b; } }
        public static long UMul(long x, long y) { unchecked { uint a = (uint)x; uint b = (uint)y; return a * b; } }
        public static long LMul(long x, long y) { unchecked { long a = x; long b = y; return a * b; } }
        public static ulong ULMul(long x, long y) { unchecked { ulong a = (ulong)x; ulong b = (ulong)y; return a * b; } }
        public static long SMul(long x, long y) { unchecked { short a = (short)x; short b = (short)y; short c = (short)(a * b); return c; } }
        public static long BMul(long x, long y) { unchecked { byte a = (byte)x; byte b = (byte)y; byte c = (byte)(a * b); return c; } }
        public static long SBMul(long x, long y) { unchecked { sbyte a = (sbyte)x; sbyte b = (sbyte)y; a *= b; return a; } }
        public static long USMul(long x, long y) { unchecked { ushort a = (ushort)x; ushort b = (ushort)y; a *= b; return a; } }
        public static long CIMul(long x, long y) { unchecked { int a = (int)x; int b = (int)y; a *= b; return a; } }
        public static long CUMul(long x, long y) { unchecked { uint a = (uint)x; uint b = (uint)y; a *= b; return a; } }
        public static long CLMul(long x, long y) { unchecked { long a = x; a *= y; return a; } }
        public static ulong CULMul(long x, long y) { unchecked { ulong a = (ulong)x; a *= (ulong)y; return a; } }
        public static long IDiv(long x, long y) { unchecked { int a = (int)x; int b = (int)y; return a / b; } }
        public static long UDiv(long x, long y) { unchecked { uint a = (uint)x; uint b = (uint)y; return a / b; } }
        public static long LDiv(long x, long y) { unchecked { long a = x; long b = y; return a / b; } }
        public static ulong ULDiv(long x, long y) { unchecked { ulong a = (ulong)x; ulong b = (ulong)y; return a / b; } }
        public static long SDiv(long x, long y) { unchecked { short a = (short)x; short b = (short)y; short c = (short)(a / b); return c; } }
        public static long BDiv(long x, long y) { unchecked { byte a = (byte)x; byte b = (byte)y; byte c = (byte)(a / b); return c; } }
        public static long SBDiv(long x, long y) { unchecked { sbyte a = (sbyte)x; sbyte b = (sbyte)y; a /= b; return a; } }
        public static long USDiv(long x, long y) { unchecked { ushort a = (ushort)x; ushort b = (ushort)y; a /= b; return a; } }
        public static long CIDiv(long x, long y) { unchecked { int a = (int)x; int b = (int)y; a /= b; return a; } }
        public static long CUDiv(long x, long y) { unchecked { uint a = (uint)x; uint b = (uint)y; a /= b; return a; } }
        public static long CLDiv(long x, long y) { unchecked { long a = x; a /= y; return a; } }
        public static ulong CULDiv(long x, long y) { unchecked { ulong a = (ulong)x; a /= (ulong)y; return a; } }
        public static long IRem(long x, long y) { unchecked { int a = (int)x; int b = (int)y; return a % b; } }
        public static long URem(long x, long y) { unchecked { uint a = (uint)x; uint b = (uint)y; return a % b; } }
        public static long LRem(long x, long y) { unchecked { long a = x; long b = y; return a % b; } }
        public static ulong ULRem(long x, long y) { unchecked { ulong a = (ulong)x; ulong b = (ulong)y; return a % b; } }
        public static long SRem(long x, long y) { unchecked { short a = (short)x; short b = (short)y; short c = (short)(a % b); return c; } }
        public static long BRem(long x, long y) { unchecked { byte a = (byte)x; byte b = (byte)y; byte c = (byte)(a % b); return c; } }
        public static long SBRem(long x, long y) { unchecked { sbyte a = (sbyte)x; sbyte b = (sbyte)y; a %= b; return a; } }
        public static long USRem(long x, long y) { unchecked { ushort a = (ushort)x; ushort b = (ushort)y; a %= b; return a; } }
        public static long CIRem(long x, long y) { unchecked { int a = (int)x; int b = (int)y; a %= b; return a; } }
        public static long CURem(long x, long y) { unchecked { uint a = (uint)x; uint b = (uint)y; a %= b; return a; } }
        public static long CLRem(long x, long y) { unchecked { long a = x; a %= y; return a; } }
        public static ulong CULRem(long x, long y) { unchecked { ulong a = (ulong)x; a %= (ulong)y; return a; } }
        public static long IAnd(long x, long y) { unchecked { int a = (int)x; int b = (int)y; return a & b; } }
        public static long UAnd(long x, long y) { unchecked { uint a = (uint)x; uint b = (uint)y; return a & b; } }
        public static long LAnd(long x, long y) { unchecked { long a = x; long b = y; return a & b; } }
        public static ulong ULAnd(long x, long y) { unchecked { ulong a = (ulong)x; ulong b = (ulong)y; return a & b; } }
        public static long SAnd(long x, long y) { unchecked { short a = (short)x; short b = (short)y; short c = (short)(a & b); return c; } }
        public static long BAnd(long x, long y) { unchecked { byte a = (byte)x; byte b = (byte)y; byte c = (byte)(a & b); return c; } }
        public static long SBAnd(long x, long y) { unchecked { sbyte a = (sbyte)x; sbyte b = (sbyte)y; a &= b; return a; } }
        public static long USAnd(long x, long y) { unchecked { ushort a = (ushort)x; ushort b = (ushort)y; a &= b; return a; } }
        public static long CIAnd(long x, long y) { unchecked { int a = (int)x; int b = (int)y; a &= b; return a; } }
        public static long CUAnd(long x, long y) { unchecked { uint a = (uint)x; uint b = (uint)y; a &= b; return a; } }
        public static long CLAnd(long x, long y) { unchecked { long a = x; a &= y; return a; } }
        public static ulong CULAnd(long x, long y) { unchecked { ulong a = (ulong)x; a &= (ulong)y; return a; } }
        public static long IOr(long x, long y) { unchecked { int a = (int)x; int b = (int)y; return a | b; } }
        public static long UOr(long x, long y) { unchecked { uint a = (uint)x; uint b = (uint)y; return a | b; } }
        public static long LOr(long x, long y) { unchecked { long a = x; long b = y; return a | b; } }
        public static ulong ULOr(long x, long y) { unchecked { ulong a = (ulong)x; ulong b = (ulong)y; return a | b; } }
        public static long SOr(long x, long y) { unchecked { short a = (short)x; short b = (short)y; short c = (short)(a | b); return c; } }
        public static long BOr(long x, long y) { unchecked { byte a = (byte)x; byte b = (byte)y; byte c = (byte)(a | b); return c; } }
        public static long SBOr(long x, long y) { unchecked { sbyte a = (sbyte)x; sbyte b = (sbyte)y; a |= b; return a; } }
        public static long USOr(long x, long y) { unchecked { ushort a = (ushort)x; ushort b = (ushort)y; a |= b; return a; } }
        public static long CIOr(long x, long y) { unchecked { int a = (int)x; int b = (int)y; a |= b; return a; } }
        public static long CUOr(long x, long y) { unchecked { uint a = (uint)x; uint b = (uint)y; a |= b; return a; } }
        public static long CLOr(long x, long y) { unchecked { long a = x; a |= y; return a; } }
        public static ulong CULOr(long x, long y) { unchecked { ulong a = (ulong)x; a |= (ulong)y; return a; } }
        public static long IXor(long x, long y) { unchecked { int a = (int)x; int b = (int)y; return a ^ b; } }
        public static long UXor(long x, long y) { unchecked { uint a = (uint)x; uint b = (uint)y; return a ^ b; } }
        public static long LXor(long x, long y) { unchecked { long a = x; long b = y; return a ^ b; } }
        public static ulong ULXor(long x, long y) { unchecked { ulong a = (ulong)x; ulong b = (ulong)y; return a ^ b; } }
        public static long SXor(long x, long y) { unchecked { short a = (short)x; short b = (short)y; short c = (short)(a ^ b); return c; } }
        public static long BXor(long x, long y) { unchecked { byte a = (byte)x; byte b = (byte)y; byte c = (byte)(a ^ b); return c; } }
        public static long SBXor(long x, long y) { unchecked { sbyte a = (sbyte)x; sbyte b = (sbyte)y; a ^= b; return a; } }
        public static long USXor(long x, long y) { unchecked { ushort a = (ushort)x; ushort b = (ushort)y; a ^= b; return a; } }
        public static long CIXor(long x, long y) { unchecked { int a = (int)x; int b = (int)y; a ^= b; return a; } }
        public static long CUXor(long x, long y) { unchecked { uint a = (uint)x; uint b = (uint)y; a ^= b; return a; } }
        public static long CLXor(long x, long y) { unchecked { long a = x; a ^= y; return a; } }
        public static ulong CULXor(long x, long y) { unchecked { ulong a = (ulong)x; a ^= (ulong)y; return a; } }
        public static long IShl(long x, long y) { unchecked { int a = (int)x; int n = (int)y; return a << n; } }
        public static long UShl(long x, long y) { unchecked { uint a = (uint)x; int n = (int)y; return a << n; } }
        public static long LShl(long x, long y) { unchecked { long a = x; int n = (int)y; return a << n; } }
        public static ulong ULShl(long x, long y) { unchecked { ulong a = (ulong)x; int n = (int)y; return a << n; } }
        public static long CIShl(long x, long y) { unchecked { int a = (int)x; a <<= (int)y; return a; } }
        public static ulong CULShl(long x, long y) { unchecked { ulong a = (ulong)x; a <<= (int)y; return a; } }
        public static long BShl(long x, long y) { unchecked { byte a = (byte)x; a <<= (int)y; return a; } }
        public static long IShr(long x, long y) { unchecked { int a = (int)x; int n = (int)y; return a >> n; } }
        public static long UShr(long x, long y) { unchecked { uint a = (uint)x; int n = (int)y; return a >> n; } }
        public static long LShr(long x, long y) { unchecked { long a = x; int n = (int)y; return a >> n; } }
        public static ulong ULShr(long x, long y) { unchecked { ulong a = (ulong)x; int n = (int)y; return a >> n; } }
        public static long CIShr(long x, long y) { unchecked { int a = (int)x; a >>= (int)y; return a; } }
        public static ulong CULShr(long x, long y) { unchecked { ulong a = (ulong)x; a >>= (int)y; return a; } }
        public static long BShr(long x, long y) { unchecked { byte a = (byte)x; a >>= (int)y; return a; } }
        public static long ILt(long x, long y) { unchecked { int a = (int)x; int b = (int)y; return (a < b) ? 1 : 0; } }
        public static long ULt(long x, long y) { unchecked { uint a = (uint)x; uint b = (uint)y; return (a < b) ? 1 : 0; } }
        public static long LLt(long x, long y) { unchecked { return (x < y) ? 1 : 0; } }
        public static long ULLt(long x, long y) { unchecked { ulong a = (ulong)x; ulong b = (ulong)y; return (a < b) ? 1 : 0; } }
        public static long DLt(double x, double y) { unchecked { return (x < y) ? 1 : 0; } }
        public static long FLt(double x, double y) { unchecked { float a = (float)x; float b = (float)y; return (a < b) ? 1 : 0; } }
        public static long ILe(long x, long y) { unchecked { int a = (int)x; int b = (int)y; return (a <= b) ? 1 : 0; } }
        public static long ULe(long x, long y) { unchecked { uint a = (uint)x; uint b = (uint)y; return (a <= b) ? 1 : 0; } }
        public static long LLe(long x, long y) { unchecked { return (x <= y) ? 1 : 0; } }
        public static long ULLe(long x, long y) { unchecked { ulong a = (ulong)x; ulong b = (ulong)y; return (a <= b) ? 1 : 0; } }
        public static long DLe(double x, double y) { unchecked { return (x <= y) ? 1 : 0; } }
        public static long FLe(double x, double y) { unchecked { float a = (float)x; float b = (float)y; return (a <= b) ? 1 : 0; } }
        public static long IGt(long x, long y) { unchecked { int a = (int)x; int b = (int)y; return (a > b) ? 1 : 0; } }
        public static long UGt(long x, long y) { unchecked { uint a = (uint)x; uint b = (uint)y; return (a > b) ? 1 : 0; } }
        public static long LGt(long x, long y) { unchecked { return (x > y) ? 1 : 0; } }
        public static long ULGt(long x, long y) { unchecked { ulong a = (ulong)x; ulong b = (ulong)y; return (a > b) ? 1 : 0; } }
        public static long DGt(double x, double y) { unchecked { return (x > y) ? 1 : 0; } }
        public static long FGt(double x, double y) { unchecked { float a = (float)x; float b = (float)y; return (a > b) ? 1 : 0; } }
        public static long IGe(long x, long y) { unchecked { int a = (int)x; int b = (int)y; return (a >= b) ? 1 : 0; } }
        public static long UGe(long x, long y) { unchecked { uint a = (uint)x; uint b = (uint)y; return (a >= b) ? 1 : 0; } }
        public static long LGe(long x, long y) { unchecked { return (x >= y) ? 1 : 0; } }
        public static long ULGe(long x, long y) { unchecked { ulong a = (ulong)x; ulong b = (ulong)y; return (a >= b) ? 1 : 0; } }
        public static long DGe(double x, double y) { unchecked { return (x >= y) ? 1 : 0; } }
        public static long FGe(double x, double y) { unchecked { float a = (float)x; float b = (float)y; return (a >= b) ? 1 : 0; } }
        public static long IEq(long x, long y) { unchecked { int a = (int)x; int b = (int)y; return (a == b) ? 1 : 0; } }
        public static long UEq(long x, long y) { unchecked { uint a = (uint)x; uint b = (uint)y; return (a == b) ? 1 : 0; } }
        public static long LEq(long x, long y) { unchecked { return (x == y) ? 1 : 0; } }
        public static long ULEq(long x, long y) { unchecked { ulong a = (ulong)x; ulong b = (ulong)y; return (a == b) ? 1 : 0; } }
        public static long DEq(double x, double y) { unchecked { return (x == y) ? 1 : 0; } }
        public static long FEq(double x, double y) { unchecked { float a = (float)x; float b = (float)y; return (a == b) ? 1 : 0; } }
        public static long INe(long x, long y) { unchecked { int a = (int)x; int b = (int)y; return (a != b) ? 1 : 0; } }
        public static long UNe(long x, long y) { unchecked { uint a = (uint)x; uint b = (uint)y; return (a != b) ? 1 : 0; } }
        public static long LNe(long x, long y) { unchecked { return (x != y) ? 1 : 0; } }
        public static long ULNe(long x, long y) { unchecked { ulong a = (ulong)x; ulong b = (ulong)y; return (a != b) ? 1 : 0; } }
        public static long DNe(double x, double y) { unchecked { return (x != y) ? 1 : 0; } }
        public static long FNe(double x, double y) { unchecked { float a = (float)x; float b = (float)y; return (a != b) ? 1 : 0; } }
        public static long INeg(long x, long y) { unchecked { int a = (int)x; return -a; } }
        public static long INot(long x, long y) { unchecked { int a = (int)x; return ~a; } }
        public static long UNot(long x, long y) { unchecked { uint a = (uint)x; return ~a; } }
        public static long UNeg(long x, long y) { unchecked { uint a = (uint)x; return -a; } }
        public static long LNeg(long x, long y) { unchecked { return -x; } }
        public static long LNot(long x, long y) { unchecked { return ~x; } }
        public static ulong ULNot(long x, long y) { unchecked { ulong a = (ulong)x; return ~a; } }
        public static long IPost(long x, long y) { unchecked { int a = (int)x; int b = a++; return (long)b * 4294967296L + (uint)a; } }
        public static long IPre(long x, long y) { unchecked { int a = (int)x; int b = --a; return (long)b * 3 + a; } }
        public static long UPost(long x, long y) { unchecked { uint a = (uint)x; uint b = a--; return (long)b - (long)a; } }
        public static long LPost(long x, long y) { unchecked { long a = x; long b = a++; return b ^ a; } }
        public static ulong ULPost(long x, long y) { unchecked { ulong a = (ulong)x; ulong b = a--; return b ^ a; } }
        public static ulong ULPre(long x, long y) { unchecked { ulong a = (ulong)x; ulong b = ++a; return b + a; } }
        public static long BInc(long x, long y) { unchecked { byte a = (byte)x; a++; a++; return a; } }
        public static long SBDec(long x, long y) { unchecked { sbyte a = (sbyte)x; a--; return a; } }
        public static long SInc(long x, long y) { unchecked { short a = (short)x; ++a; return a; } }
        public static long CharInc(long x, long y) { unchecked { char a = (char)x; a++; return a; } }
        public static double FInc(double x, double y) { unchecked { float a = (float)x; a++; return a; } }
        public static double DInc(double x, double y) { unchecked { double a = x; a++; return a; } }
        public static long ConvL_int(long x, long y) { unchecked { int a = (int)x; return a; } }
        public static long ConvL_uint(long x, long y) { unchecked { uint a = (uint)x; return a; } }
        public static long ConvL_short(long x, long y) { unchecked { short a = (short)x; return a; } }
        public static long ConvL_ushort(long x, long y) { unchecked { ushort a = (ushort)x; return a; } }
        public static long ConvL_sbyte(long x, long y) { unchecked { sbyte a = (sbyte)x; return a; } }
        public static long ConvL_byte(long x, long y) { unchecked { byte a = (byte)x; return a; } }
        public static long ConvL_char(long x, long y) { unchecked { char a = (char)x; return a; } }
        public static ulong ConvL_ulong(long x, long y) { unchecked { ulong a = (ulong)x; return a; } }
        public static double ConvL_d(long x, long y) { unchecked { return (double)x; } }
        public static double ConvL_f(long x, long y) { unchecked { return (float)x; } }
        public static double ConvUL_d(long x, long y) { unchecked { ulong a = (ulong)x; return (double)a; } }
        public static double ConvUL_f(long x, long y) { unchecked { ulong a = (ulong)x; return (float)a; } }
        public static long ConvUL_l(long x, long y) { unchecked { ulong a = (ulong)x; return (long)a; } }
        public static double ConvI_f(long x, long y) { unchecked { int a = (int)x; return (float)a; } }
        public static double ConvU_f(long x, long y) { unchecked { uint a = (uint)x; return (float)a; } }
        public static double ConvU_d(long x, long y) { unchecked { uint a = (uint)x; return a; } }
        public static ulong ConvI_UL(long x, long y) { unchecked { int a = (int)x; return (ulong)a; } }
        public static long ConvS_U(long x, long y) { unchecked { short a = (short)x; return (uint)a; } }
        public static long ConvSB_US(long x, long y) { unchecked { sbyte a = (sbyte)x; return (ushort)a; } }
        public static long ConvB_SB(long x, long y) { unchecked { byte a = (byte)x; return (sbyte)a; } }
        public static long ConvIULL(long x, long y) { unchecked { int a = (int)x; return (long)(ulong)a; } }
        public static long ConvIULI(long x, long y) { unchecked { int a = (int)x; return (int)(ulong)a; } }
        public static long ConvUULL(long x, long y) { unchecked { uint a = (uint)x; return (long)(ulong)a; } }
        public static double ConvIULD(long x, long y) { unchecked { int a = (int)x; return (double)(ulong)a; } }
        public static double ConvD_f(double x, double y) { unchecked { return (float)x; } }
        public static double FAdd(double x, double y) { unchecked { float a = (float)x; float b = (float)y; return a + b; } }
        public static double CFAdd(double x, double y) { unchecked { float a = (float)x; a += (float)y; return a; } }
        public static double DAdd(double x, double y) { unchecked { return x + y; } }
        public static double FSub(double x, double y) { unchecked { float a = (float)x; float b = (float)y; return a - b; } }
        public static double CFSub(double x, double y) { unchecked { float a = (float)x; a -= (float)y; return a; } }
        public static double DSub(double x, double y) { unchecked { return x - y; } }
        public static double FMul(double x, double y) { unchecked { float a = (float)x; float b = (float)y; return a * b; } }
        public static double CFMul(double x, double y) { unchecked { float a = (float)x; a *= (float)y; return a; } }
        public static double DMul(double x, double y) { unchecked { return x * y; } }
        public static double FDiv(double x, double y) { unchecked { float a = (float)x; float b = (float)y; return a / b; } }
        public static double CFDiv(double x, double y) { unchecked { float a = (float)x; a /= (float)y; return a; } }
        public static double DDiv(double x, double y) { unchecked { return x / y; } }
        public static double FRem(double x, double y) { unchecked { float a = (float)x; float b = (float)y; return a % b; } }
        public static double CFRem(double x, double y) { unchecked { float a = (float)x; a %= (float)y; return a; } }
        public static double DRem(double x, double y) { unchecked { return x % y; } }
        public static double FNeg(double x, double y) { unchecked { float a = (float)x; return -a; } }
        public static double DNeg(double x, double y) { unchecked { return -x; } }
        public static long MixUI(long x, long y) { unchecked { uint a = (uint)x; int b = (int)y; return a * b; } }
        public static long MixIU(long x, long y) { unchecked { int a = (int)x; uint b = (uint)y; return a + b; } }
        public static long MixIL(long x, long y) { unchecked { int a = (int)x; return a * y; } }
        public static ulong MixUL(long x, long y) { unchecked { uint a = (uint)x; ulong b = (ulong)y; return a * b; } }
        public static double MixID(long x, long y) { unchecked { int a = (int)x; double d = 0.5; return a * d; } }
        public static double MixLD(long x, long y) { unchecked { double d = 1.5; return x * d; } }
        public static double MixFI(long x, long y) { unchecked { float f = 0.1f; int a = (int)x; return f * a; } }
        public static long EnumAdd(long x, long y) { unchecked { E e = (E)(sbyte)x; e = e + 1; return (sbyte)e; } }
        public static long EnumSub(long x, long y) { unchecked { E e = (E)(sbyte)x; E f = (E)(sbyte)y; return f - e; } }
        public static long EnumCmp(long x, long y) { unchecked { E e = (E)(sbyte)x; return e == E.B ? 1 : (e < E.A ? 2 : 3); } }
        public static long EnumOr(long x, long y) { unchecked { E e = (E)(sbyte)x; E f = (E)(sbyte)y; return (sbyte)(e | f); } }
        public static long Tern(long x, long y) { unchecked { bool p = x > 0; bool q = y > 0; return ((p ? q : !q) ? 1 : 0) * 10 + (p ? (q ? 1 : 2) : 3); } }
        public static long ArrSide(long x, long y) { unchecked { int[] arr = new int[4]; int i = (int)(x & 3); arr[i++] += (int)y; arr[i & 3] = arr[(i - 1) & 3]++; return arr[0] + arr[1] * 10 + arr[2] * 100 + arr[3] * 1000 + i * 10000; } }
        public static long ArrSideL(long x, long y) { unchecked { long[] arr = new long[3]; int i = 0; arr[i++] = x; arr[i++] = arr[0] * y; arr[i] = arr[i - 1]--; return arr[0] ^ arr[1] ^ arr[2]; } }
        public static long Chain(long x, long y) { unchecked { int a = (int)x, b = (int)y; int c = a = b = a + b; return (long)a * 3 + b * 5 + c; } }
        public static long Shift33(long x, long y) { unchecked { int a = (int)x; long b = x; return (a << 33) + (b << 65) + (a >> 34); } }
        public static long CharStr(long x, long y) { unchecked { char c = (char)(65 + (int)(x & 15)); string s = "a" + c + true + (int)y + (long)x; return s.Length; } }
        public static ulong Const(long x, long y) { unchecked { ulong a = 0xFFFFFFFFFFFFFFFFUL; long b = long.MinValue; uint c = 0x80000000u; return a ^ (ulong)b ^ c ^ (ulong)x; } }
        public static ulong Fnv(long x, long y) { unchecked { ulong h = 14695981039346656037UL; for (int k = 0; k < 8; k++) { h ^= (ulong)((x >> (8 * k)) & 0xFF); h *= 1099511628211UL; } return h; } }
        public static ulong SplitMix(long x, long y) { unchecked { ulong z = (ulong)x + 0x9E3779B97F4A7C15UL; z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL; z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL; return z ^ (z >> 31); } }
        public static long LongLoop(long x, long y) { unchecked { long s = 0; for (long k = x & 7; k < 20; k++) s += k * y; return s; } }
        public static long IDivC(long x, long y) { unchecked { int a = (int)x; return (long)(a / 7) * 1000000 + (a % -3) * 1000 + a / -2 + a % 5; } }
        public static long IDivC2(long x, long y) { unchecked { int a = (int)x; int b = a; b /= 3; b %= 1000; return b; } }
    }
}
