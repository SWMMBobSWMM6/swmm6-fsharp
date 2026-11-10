#!/bin/bash
# libstdc++ against the Python runtime (py/cpp: stdalgo.py and the hash tables of containers.py). sortcheck.py and
# hashcheck.py run the cases of sortcheck.cpp and hashcheck.cpp; each output must be identical to the C++ one.
# sortcheck.py runs four times: the generic sorts over structs (O) and the same permutations through the
# specialisations StdAlgo_Sort__D, __I, __L (and their stable_sort and partial_sort). Outputs go to build/py, so
# this script does not race the other languages' scripts over build/cpp.txt.
# Written by Claude (Anthropic) for the SWMM6 ports; see AI-USE.md.
set -e
cd "$(dirname "$0")"
mkdir -p build/py
g++ -O2 -std=c++20 -o build/py/sortcheck sortcheck.cpp
g++ -O2 -std=c++20 -o build/py/hashcheck hashcheck.cpp
build/py/sortcheck > build/py/cpp.txt
build/py/hashcheck > build/py/hash_cpp.txt
PY=${PYTHON:-python3}
fail=0
echo "sort cases: $(wc -l < build/py/cpp.txt)"
for mode in O D I L; do
    $PY sortcheck.py $mode > build/py/sort_py_$mode.txt
    if cmp -s build/py/cpp.txt build/py/sort_py_$mode.txt; then
        echo "sort, stable_sort, partial_sort ($mode): identical permutations"
    else
        echo "sort ($mode): DIFFERENT"; diff build/py/cpp.txt build/py/sort_py_$mode.txt | head -5 | cut -c1-300; fail=1
    fi
done
$PY hashcheck.py > build/py/hash_py.txt
echo "hash lines: $(wc -l < build/py/hash_cpp.txt)"
if cmp -s build/py/hash_cpp.txt build/py/hash_py.txt; then
    echo "unordered_map / unordered_set order and std::hash: identical"
else
    echo "hash: DIFFERENT"; diff build/py/hash_cpp.txt build/py/hash_py.txt | head -6 | cut -c1-300; fail=1
fi
(cd build/py && md5sum cpp.txt sort_py_O.txt sort_py_D.txt sort_py_I.txt sort_py_L.txt hash_cpp.txt hash_py.txt)
exit $fail
