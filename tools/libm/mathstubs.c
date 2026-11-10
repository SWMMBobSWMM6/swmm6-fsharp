#include <math.h>
#include <stdint.h>
double __math_oflow (uint32_t s) { return s ? -HUGE_VAL : HUGE_VAL; }
double __math_uflow (uint32_t s) { return s ? -0.0 : 0.0; }
double __math_may_uflow (uint32_t s) { return s ? -0.0 : 0.0; }
double __math_divzero (uint32_t s) { return s ? -HUGE_VAL : HUGE_VAL; }
double __math_invalid (double x) { return (x - x) / (x - x); }
double __math_check_oflow (double y) { return y; }
double __math_check_uflow (double y) { return y; }
