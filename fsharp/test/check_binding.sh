#!/bin/bash
# check_binding.sh [LIBOPENSWMM] - which glibc symbol version each libm function of the F# engine reaches, against the
# versions the C++ engine (libopenswmm.engine.so.6, built from swmm6_rel 88329d6) imports.
# The F# runtime declares the functions with DllImport("libm.so.6") (fsharp/runtime/LibM.fs). .NET resolves a
# P/Invoke with dlsym, which takes the symbol's default version: on glibc 2.39 exp, log, pow and log2 @GLIBC_2.29,
# hypot @GLIBC_2.35, fmod @GLIBC_2.38, the rest @GLIBC_2.2.5. For every function in LibM.fs this script compares the
# address dlsym gives with the address dlvsym gives for the version the C++ engine names (objdump -T); a function the
# C++ engine does not import is listed with the version dlsym's address belongs to.
# Written by Claude (Anthropic) for Robert Dickinson's SWMM6 ports; see ../README.md.
set -e
HERE=$(cd "$(dirname "$0")" && pwd)
LIB=${1:-/tmp/claude-0/-home-claude/9f3dde4c-f353-52c4-b476-e410dd8d4e21/scratchpad/swmm6/build1d/src/engine/libopenswmm.engine.so.6}
W=$(mktemp -d /dev/shm/fs6_bind.XXXXXX)
trap 'rm -rf "$W"' EXIT
cat > "$W/b.c" <<'EOF'
#define _GNU_SOURCE
#include <dlfcn.h>
#include <stdio.h>
#include <string.h>
/* b NAME [VERSION]: dlsym's address for NAME, and whether dlvsym(NAME, VERSION) is the same address; without
   VERSION, the first of glibc's x86-64 libm versions whose dlvsym address is dlsym's */
int main(int argc, char **argv) {
    void *h = dlopen("libm.so.6", RTLD_NOW);
    void *d = dlsym(h, argv[1]);
    if (!d) { printf("%-10s not found\n", argv[1]); return 1; }
    if (argc > 2) {
        void *v = dlvsym(h, argv[1], argv[2]);
        printf("%-10s %-12s %s\n", argv[1], argv[2], v == d ? "same" : "DIFFERENT");
        return v == d ? 0 : 1;
    }
    const char *vs[] = { "GLIBC_2.2.5", "GLIBC_2.15", "GLIBC_2.18", "GLIBC_2.23", "GLIBC_2.24", "GLIBC_2.25", "GLIBC_2.26",
                         "GLIBC_2.27", "GLIBC_2.28", "GLIBC_2.29", "GLIBC_2.31", "GLIBC_2.32", "GLIBC_2.35", "GLIBC_2.38", "GLIBC_2.39" };
    for (unsigned i = 0; i < sizeof vs / sizeof vs[0]; i++)
        if (dlvsym(h, argv[1], vs[i]) == d) { printf("%-10s %-12s (not imported by the C++ engine)\n", argv[1], vs[i]); return 0; }
    printf("%-10s ?\n", argv[1]);
    return 0;
}
EOF
gcc -O1 -o "$W/b" "$W/b.c" -ldl
objdump -T "$LIB" | awk '$0 ~ /\*UND\*/ && $0 ~ /GLIBC/ { v = $(NF-1); gsub(/[()]/, "", v); print $NF, v }' | sort > "$W/imports.txt"
names=$(sed -n 's/^ *extern [a-z0-9]* \([a-z0-9]*\)(.*/\1/p' "$HERE/../runtime/LibM.fs")
echo "function   C++ engine   dlsym (what .NET's DllImport binds)"
fail=0
for n in $names; do
	v=$(awk -v n=$n '$1 == n { print $2 }' "$W/imports.txt")
	if [ -n "$v" ]; then "$W/b" $n $v || fail=1; else "$W/b" $n; fi
done
echo "functions: $(echo $names | wc -w); imported by the C++ engine: $(for n in $names; do awk -v n=$n '$1 == n' "$W/imports.txt"; done | wc -l)"
exit $fail
