// Program.cs - the SWMM6 command line (src/cli/main.cpp), translated by hand: the same calls into the engine's
// C API in the same order, the same messages on stdout and the same exit code.
// Written by Claude (Anthropic) for the SWMM6 ports; see AI-USE.md.
using System;
using Cpp;
using API = global::_global._F;

public static class Program
{
    const string VERSION = "6.0.0-alpha.4";

    static void Out(string s) { var b = System.Text.Encoding.Latin1.GetBytes(s); R.stdout_.S.Write(b, 0, b.Length); }

    public static int Main(string[] argv)
    {
        int rc = Run(argv);
        R.FlushAll();          // returning from main calls exit(), which flushes every open FILE
        return rc;
    }

    static int Run(string[] argv)
    {
        if (argv.Length < 1) { Out("\nError: not enough arguments. Use --help for usage.\n\n"); return 1; }
        if (argv[0] == "--version" || argv[0] == "-v") { Out("openswmm.engine " + VERSION + "\n"); return 0; }
        if (argv.Length < 2)
        {
            Out("\nError: need at least input and report file paths.\n");
            Out("Usage: openswmm <input.inp> <report.rpt> [output.out]\n\n");
            return 1;
        }
        string inp = argv[0], rpt = argv[1], outf = argv.Length > 2 ? argv[2] : "";
        Out("\n... OpenSWMM Engine " + VERSION + "\n... Input: " + inp + "\n... Report: " + rpt + "\n... Output: " + outf + "\n");
        R.fflush(R.stdout_);
        var t0 = DateTime.UtcNow;
        object engine = API.swmm_engine_create();
        if (engine == null) { Out("Error: failed to create engine instance.\n"); return 1; }
        int err = API.swmm_engine_open(engine, R.Lit(inp), R.Lit(rpt), R.Lit(outf), CPtr.Null);
        if (err != 0)
        {
            Out("Error opening input file: " + R.Str(API.swmm_get_last_error_msg(engine)) + "\n");
            API.swmm_engine_destroy(engine);
            return err;
        }
        err = API.swmm_engine_initialize(engine);
        if (err != 0)
        {
            Out("Error initializing: " + R.Str(API.swmm_get_last_error_msg(engine)) + "\n");
            API.swmm_engine_close(engine);
            API.swmm_engine_destroy(engine);
            return err;
        }
        int save = outf.Length > 0 ? 1 : 0;
        err = API.swmm_engine_start(engine, save);
        if (err != 0)
        {
            Out("Error starting simulation: " + R.Str(API.swmm_get_last_error_msg(engine)) + "\n");
            API.swmm_engine_close(engine);
            API.swmm_engine_destroy(engine);
            return err;
        }
        var elapsed = new double[1];
        long steps = 0;
        while (true)
        {
            err = API.swmm_engine_step(engine, new Ptr<double>(elapsed, 0));
            if (err != 0) { Out("\nError at step " + steps + ": " + R.Str(API.swmm_get_last_error_msg(engine)) + "\n"); break; }
            if (elapsed[0] <= 0.0) break;
            steps++;
        }
        Out("\n... " + steps + " steps completed.\n");
        int result = err;
        void Fin(int status, string phase)
        {
            if (status != 0)
            {
                Out("Error " + phase + ": " + R.Str(API.swmm_get_last_error_msg(engine)) + "\n");
                if (result == 0) result = status;
            }
        }
        Fin(API.swmm_engine_end(engine), "ending simulation");
        Fin(API.swmm_engine_report(engine), "writing report");
        Fin(API.swmm_engine_close(engine), "closing simulation");
        API.swmm_engine_destroy(engine);
        Out(string.Format(System.Globalization.CultureInfo.InvariantCulture, "... OpenSWMM Engine {0} in {1:F2} seconds.\n\n",
            result == 0 ? "completed" : "failed", (DateTime.UtcNow - t0).TotalSeconds));
        return result;
    }
}
