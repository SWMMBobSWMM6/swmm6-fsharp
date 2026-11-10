# How AI was used in swmm6-fsharp

This translation of the OpenSWMM 6 1D engine (HydroCouple/openswmm.engine, branch `swmm6_rel`, commit 88329d6)
into F# was made by Claude (Anthropic's AI model, Claude Opus 5.5), in Claude Code sessions, working for
Robert Dickinson.

- **What Claude wrote:** the C++ front end and C# back end in `tools/cpp2x/`, the C# runtime in `cs/runtime/`,
  the C#-to-F# translator in `tools/cs2x/`, the F# runtime, the
  gate in `gate/`, the checks in `tests/`, the notes in `lessons/` and this repository's README. Several parts of the
  runtimes were written by Claude agents working in parallel to a written contract, then integrated and checked
  by the main session.
- **What Claude did not write:** the engine. Every generated engine file is produced mechanically from the C++
  source (C++ to C#, C# to F#); nothing there is edited by hand. The
  numerical methods, the input format and the report text are Caleb Buahin's and the OpenSWMM contributors',
  descended from EPA SWMM 5.
- **How the work is checked:** not by reading it. Each translation is run on 480 models and its binary output file
  and report are compared byte for byte with the C++ reference engine built from the same commit and run on one
  thread (`gate/gate.py`). A model counts only if every byte matches, apart from the report's three clock lines.
  The runtime is also compared with glibc, libstdc++ and the C# runtime case by case (`tests/`).
- **Human direction:** Robert Dickinson chose the task, the reference, the languages and the standard of
  agreement, created the repositories, and reviews the results. Responsibility for publishing the work is his.
