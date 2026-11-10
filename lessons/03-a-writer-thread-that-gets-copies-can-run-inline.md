A worker thread that receives its own copy of the data, and is the only one consuming it, can run inline in a port.

SWMM6 writes the binary .out file on a thread of its own (src/engine/output/IOThread.cpp). At each reporting step the
engine builds a SimulationSnapshot, a copy of the results, and posts it to a queue; the thread takes snapshots off
the queue one at a time, in order, and hands each to the output plugins. Nothing else reads the queue and the
plugins write only from the snapshot (plus the name tables, which do not change after the input is read).

So the file is the same whether the plugins run on the thread a moment later or on the main thread at once, and
the ports do the second (cs/runtime/Hand_IOThread.cs). The engine itself does exactly this in its WebAssembly
build, where there are no threads: the `__EMSCRIPTEN__` branch of IOThread.cpp calls the plugins inside post().

Before making such a change, check three things in the source: the worker gets copies, not pointers into live
state; there is one consumer and it keeps posting order; and nothing waits on the worker's timing (flush() and
stop() only wait for the queue to drain).
