// Calls p.(*S).Run so that the linker sees it inlined across packages. Written by Claude (Anthropic); see AI-USE.md.
package main

import (
	"fmt"
	"linkbug/p"
)

func main() { s := &p.S{}; fmt.Println(s.Run(5)) }
