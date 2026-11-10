#!/bin/bash
# Build both sides and compare: libstdc++ against the Go runtime (go/cpp/stdalgo.go and go/cpp/containers.go).
# The Go programs live in the Go module, at go/internal/stdcheck/{sortcheck,hashcheck}.
# Written by Claude (Anthropic); see AI-USE.md.
set -e
cd "$(dirname "$0")"
mkdir -p build
g++ -O2 -std=c++20 -o build/sortcheck sortcheck.cpp
g++ -O2 -std=c++20 -o build/hashcheck hashcheck.cpp
(cd ../../go && go build -o ../tests/stdcheck/build/sortcheck_go ./internal/stdcheck/sortcheck \
             && go build -o ../tests/stdcheck/build/hashcheck_go ./internal/stdcheck/hashcheck)
build/sortcheck > build/cpp.txt
build/sortcheck_go > build/go.txt
echo "sort cases: $(wc -l < build/cpp.txt)"
if cmp -s build/cpp.txt build/go.txt; then echo "sort, stable_sort, partial_sort: identical permutations"; else diff build/cpp.txt build/go.txt | head -5 | cut -c1-300; exit 1; fi
build/hashcheck > build/hash_cpp.txt
build/hashcheck_go > build/hash_go.txt
echo "hash lines: $(wc -l < build/hash_cpp.txt)"
if cmp -s build/hash_cpp.txt build/hash_go.txt; then echo "unordered_map / unordered_set order and std::hash: identical"; else diff build/hash_cpp.txt build/hash_go.txt | head -6 | cut -c1-300; exit 1; fi
