#include <stdbool.h>
#define hidden_def(x)
#define hidden_proto(x)
#define strong_alias(a,b)
#define weak_alias(a,b)
#define versioned_symbol(a,b,c,d)
#define attribute_hidden
#define __glibc_unlikely(x) __builtin_expect((x),0)
#define __glibc_likely(x) __builtin_expect((x),1)
#define attribute_tls_model_ie
#define libc_hidden_proto(x)
#define libm_hidden_proto(x)
#define __set_errno(e) ((void)0)
#include <math.h>
#include <stdint.h>
double __math_oflow (uint32_t);
double __math_uflow (uint32_t);
double __math_may_uflow (uint32_t);
double __math_divzero (uint32_t);
double __math_invalid (double);
double __math_check_oflow (double);
double __math_check_uflow (double);
