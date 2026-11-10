#!/bin/bash
# libstdc++ against the Java runtime (java/src/cpp: StdAlgo.java and the hash tables UMap / USet / HashTable /
# RehashPolicy / StdHash). sortcheck.java and hashcheck.java run the cases of sortcheck.cpp and hashcheck.cpp; each
# output must be identical to the C++ one. sortcheck.java runs four times: the generic sorts over structs (O) and
# the same permutations through the primitive specialisations Sort__D, Sort__I, Sort__L (and their stable_sort and
# partial_sort). javac compiles the tests with java/src on its source path, so it builds only the runtime classes
# they reach.
# Written by Claude (Anthropic) for the SWMM6 ports; see AI-USE.md.
set -e
cd "$(dirname "$0")"
mkdir -p build/java
g++ -O2 -std=c++20 -o build/sortcheck sortcheck.cpp
g++ -O2 -std=c++20 -o build/hashcheck hashcheck.cpp
javac --release 21 -nowarn -encoding UTF-8 -sourcepath ../../java/src -d build/java sortcheck.java hashcheck.java
build/sortcheck > build/cpp.txt
build/hashcheck > build/hash_cpp.txt
JAVA="java -Xss16m -cp build/java"
fail=0
echo "sort cases: $(wc -l < build/cpp.txt)"
for mode in O D I L; do
    $JAVA sortcheck $mode > build/sort_java_$mode.txt
    if cmp -s build/cpp.txt build/sort_java_$mode.txt; then
        echo "sort, stable_sort, partial_sort ($mode): identical permutations"
    else
        echo "sort ($mode): DIFFERENT"; diff build/cpp.txt build/sort_java_$mode.txt | head -5 | cut -c1-300; fail=1
    fi
done
$JAVA hashcheck > build/hash_java.txt
echo "hash lines: $(wc -l < build/hash_cpp.txt)"
if cmp -s build/hash_cpp.txt build/hash_java.txt; then
    echo "unordered_map / unordered_set order and std::hash: identical"
else
    echo "hash: DIFFERENT"; diff build/hash_cpp.txt build/hash_java.txt | head -6 | cut -c1-300; fail=1
fi
(cd build && md5sum cpp.txt sort_java_O.txt sort_java_D.txt sort_java_I.txt sort_java_L.txt hash_cpp.txt hash_java.txt)
exit $fail
