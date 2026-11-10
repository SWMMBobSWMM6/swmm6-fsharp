#!/bin/bash
# Reproduce the Go linker crash that tools/cs2x avoids (lessons/09): a func literal converted to a named func type
# and called at once, inside a method another package inlines. Then the two variants that link.
cd "$(dirname "$0")"; go version
echo "as written:";               go build -o /dev/null . 2>&1 | grep -m1 panic || echo "links"
cp p/p.go /tmp/p.go.$$
sed -i 's/order = FN(func() \[\]int32 {/order = (func() []int32 {/' p/p.go
echo "without the conversion:";   go build -o /dev/null . 2>&1 | grep -m1 panic || echo "links"
cp /tmp/p.go.$$ p/p.go
echo "with inlining off:";        go build -gcflags=linkbug/p=-l -o /dev/null . 2>&1 | grep -m1 panic || echo "links"
rm -f /tmp/p.go.$$
