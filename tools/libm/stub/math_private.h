#ifndef STUB_MATH_PRIVATE
#define STUB_MATH_PRIVATE
#include <stdint.h>
#include <string.h>
#define EXTRACT_WORDS(ix0,ix1,d) do { uint64_t __u; double __d=(d); memcpy(&__u,&__d,8); (ix0)=(int32_t)(__u>>32); (ix1)=(uint32_t)__u; } while (0)
#define EXTRACT_WORDS64(i,d) do { double __d=(d); uint64_t __u; memcpy(&__u,&__d,8); (i)=__u; } while (0)
#define GET_HIGH_WORD(i,d) do { double __d=(d); uint64_t __u; memcpy(&__u,&__d,8); (i)=(int32_t)(__u>>32); } while (0)
#define GET_LOW_WORD(i,d) do { double __d=(d); uint64_t __u; memcpy(&__u,&__d,8); (i)=(uint32_t)__u; } while (0)
#define INSERT_WORDS(d,ix0,ix1) do { uint64_t __u=((uint64_t)(uint32_t)(ix0)<<32)|(uint32_t)(ix1); double __d; memcpy(&__d,&__u,8); (d)=__d; } while (0)
#define INSERT_WORDS64(d,i) do { uint64_t __u=(i); double __d; memcpy(&__d,&__u,8); (d)=__d; } while (0)
#define SET_HIGH_WORD(d,v) do { double __d=(d); uint64_t __u; memcpy(&__u,&__d,8); __u=(__u&0xffffffffull)|((uint64_t)(uint32_t)(v)<<32); memcpy(&__d,&__u,8); (d)=__d; } while (0)
#define SET_LOW_WORD(d,v) do { double __d=(d); uint64_t __u; memcpy(&__u,&__d,8); __u=(__u&0xffffffff00000000ull)|(uint32_t)(v); memcpy(&__d,&__u,8); (d)=__d; } while (0)
#endif
