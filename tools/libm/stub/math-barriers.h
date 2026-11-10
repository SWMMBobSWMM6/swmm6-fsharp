#define math_opt_barrier(x) ({ __typeof (x) __x = (x); __asm ("" : "+m" (__x)); __x; })
#define math_force_eval(x) ({ __typeof (x) __x = (x); __asm __volatile__ ("" : : "m" (__x)); })
