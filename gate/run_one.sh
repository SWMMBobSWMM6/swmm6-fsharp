#!/bin/bash
# run_one.sh <engine-cmd> <model.inp> <outdir>   (engine run in the model's own folder, like EPA)
eng="$1"; inp="$2"; out="$3"; mkdir -p "$out"
dir=$(dirname "$inp"); base=$(basename "$inp")
start=$(date +%s.%N)
( cd "$dir" && timeout ${GATE_TIMEOUT:-60} $eng "$base" "$out/r.rpt" "$out/r.out" > "$out/stdout.txt" 2> "$out/stderr.txt" ); echo $? > "$out/rc"
end=$(date +%s.%N); echo "$end - $start" | bc > "$out/sec"
