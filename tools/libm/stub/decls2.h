/* internal glibc functions the second set of routines calls (renamed to their translations by -D on the command
   line: __ieee754_exp to the FMA exp, __expm1 to the FMA expm1, __frexp/__ldexp to the runtime's) */
double __ieee754_exp (double);
double __expm1 (double);
