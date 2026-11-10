// Hand_IOThread.cs - IOThread's start, post, stop, flush and run, written by hand (tools/cpp2x/emit_cs.py HAND).
// The engine hands each reporting-step snapshot to a worker thread that runs the output plugins in posting order.
// The ports are single-threaded, so post() runs the plugins at once, as the engine's own __EMSCRIPTEN__ build of
// src/engine/output/IOThread.cpp does. The snapshot is the engine's copy either way, so the writes, their order and
// the files are the same.
// Written by Claude (Anthropic) for the SWMM6 ports; see AI-USE.md.
namespace openswmm
{
    public partial class IOThread
    {
        public void start()
        {
            stop_flag_.v = false;
            running_.v = true;
        }

        public void post(global::openswmm.SimulationSnapshot snap)
        {
            if (stop_flag_.v)
                return;
            next_sequence_++;
            int rc = factory_.update_all(snap);
            if (rc != 0)
                last_error_.v = rc;
            tasks_completed_.v += 1;
        }

        public void stop()
        {
            stop_flag_.v = true;
            running_.v = false;
        }

        public int flush() => last_error();

        public void run() { }
    }
}
