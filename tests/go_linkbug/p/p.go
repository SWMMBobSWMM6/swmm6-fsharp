// Minimal reproduction of a Go 1.24 linker crash (see run.sh, lessons/09). Written by Claude (Anthropic); see AI-USE.md.
package p

type FN func() []int32

var cache []int32
var done bool

type S struct{ sorted []int32 }

func (s *S) Run(n int32) int32 {
	var order []int32
	if len(s.sorted) == 0 {
		order = FN(func() []int32 {
			if !done {
				cache = nil
				done = true
			}
			cache = make([]int32, n)
			for j := int32(0); j < n; j++ {
				cache[j] = j
			}
			return cache
			panic("unreachable")
		})()
	} else {
		order = s.sorted
	}
	return int32(len(order))
}
