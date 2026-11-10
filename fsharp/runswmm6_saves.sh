#!/bin/bash
# runswmm6_saves.sh - the F# SWMM6 engine (runswmm6) for gate/gate.py, which runs
# `ENGINE model.inp report.rpt output.out` in the model's folder. The files the deck SAVEs to ([FILES] SAVE ..., and
# [LID_USAGE] report files; listed by savefiles.py from the swmm6-rust repository, which was written for SWMM6's input
# format and is called read-only) are backed up first and restored after, so a SAVE in the corpus never changes a file
# another deck USEs. The exit status is the engine's.
# The lister turns '\' into '/' in report-file names, as OpenSWMM 6 does; both spellings are backed up and restored
# (SWMM5 lessons/29), so a name the engine opens either way is covered.
# A deck with nothing to back up runs the engine with exec, so that gate.py's timeout, which kills its own child,
# kills the engine and does not leave it running as an orphan.
# SWMM_BIN overrides the engine; SAVEFILES the lister; TMPDIR where the backups go.
# Copied from the Groovy SWMM6 port's groovy/runswmm6_saves.sh with the engine changed.
# Written by Claude (Anthropic) for Robert Dickinson's SWMM6 ports; see README.md.
H=$(cd "$(dirname "$0")" && pwd)
BIN=${SWMM_BIN:-$H/runswmm6}
SAVEFILES=${SAVEFILES:-$H/../gate/savefiles.py}
d="$PWD/$1"
dir=$(dirname "$d")
B=$(mktemp -d "${TMPDIR:-/tmp}/swmmsave.XXXXXX"); python3 "$SAVEFILES" "$d" > "$B/listed.txt" 2>/dev/null
while IFS= read -r f; do
  printf '%s\n' "$f"
  rel=${f#"$dir/"}
  if [ "$rel" != "$f" ] && [ "${rel//\//\\}" != "$rel" ]; then printf '%s\n' "$dir/${rel//\//\\}"; fi
done < "$B/listed.txt" > "$B/saves.txt"
if [ ! -s "$B/saves.txt" ]; then rm -rf "$B"; exec "$BIN" "$@"; fi
k=0; while IFS= read -r f; do k=$((k+1)); [ -f "$f" ] && cp -p "$f" "$B/$k"; done < "$B/saves.txt"
"$BIN" "$@"; rc=$?
k=0; while IFS= read -r f; do k=$((k+1)); if [ -f "$B/$k" ]; then cp -p "$B/$k" "$f"; else rm -f "$f"; fi; done < "$B/saves.txt"
rm -rf "$B"; exit $rc
