// Program.fs - the SWMM6 command line (src/cli/main.cpp) in F#, translated by hand from the C# port's Program.cs: the
// same calls into the engine's C API in the same order, the same messages on stdout and the same exit code.
// Written by Claude (Anthropic) for Robert Dickinson's SWMM6 ports; see AI-USE.md and fsharp/README.md.
module Program

open Cpp

type private API = Engine.F__global

let private VERSION = "6.0.0-alpha.4"

let private out (s: string) =
    let b = System.Text.Encoding.Latin1.GetBytes(s)
    R.stdout_.S.Write(b, 0, b.Length)

let private run (argv: string[]) : int =
    if argv.Length < 1 then
        out "\nError: not enough arguments. Use --help for usage.\n\n"
        1
    elif argv.[0] = "--version" || argv.[0] = "-v" then
        out ("openswmm.engine " + VERSION + "\n")
        0
    elif argv.Length < 2 then
        out "\nError: need at least input and report file paths.\n"
        out "Usage: openswmm <input.inp> <report.rpt> [output.out]\n\n"
        1
    else
        let inp = argv.[0]
        let rpt = argv.[1]
        let outf = if argv.Length > 2 then argv.[2] else ""
        out ("\n... OpenSWMM Engine " + VERSION + "\n... Input: " + inp + "\n... Report: " + rpt + "\n... Output: " + outf + "\n")
        R.fflush(R.stdout_) |> ignore
        let t0 = System.DateTime.UtcNow
        let engine = API.swmm_engine_create()
        if isNull engine then
            out "Error: failed to create engine instance.\n"
            1
        else
        let mutable err = API.swmm_engine_open(engine, R.Lit(inp), R.Lit(rpt), R.Lit(outf), CPtr.Null)
        if err <> 0 then
            out ("Error opening input file: " + R.Str(API.swmm_get_last_error_msg(engine)) + "\n")
            API.swmm_engine_destroy(engine)
            err
        else
        err <- API.swmm_engine_initialize(engine)
        if err <> 0 then
            out ("Error initializing: " + R.Str(API.swmm_get_last_error_msg(engine)) + "\n")
            API.swmm_engine_close(engine) |> ignore
            API.swmm_engine_destroy(engine)
            err
        else
        let save = if outf.Length > 0 then 1 else 0
        err <- API.swmm_engine_start(engine, save)
        if err <> 0 then
            out ("Error starting simulation: " + R.Str(API.swmm_get_last_error_msg(engine)) + "\n")
            API.swmm_engine_close(engine) |> ignore
            API.swmm_engine_destroy(engine)
            err
        else
        let elapsed = Array.zeroCreate<float> 1
        let mutable steps = 0L
        let mutable go = true
        while go do
            err <- API.swmm_engine_step(engine, Ptr<float>(elapsed, 0L))
            if err <> 0 then
                out ("\nError at step " + string steps + ": " + R.Str(API.swmm_get_last_error_msg(engine)) + "\n")
                go <- false
            elif elapsed.[0] <= 0.0 then go <- false
            else steps <- steps + 1L
        out ("\n... " + string steps + " steps completed.\n")
        let result = ref err
        let fin (status: int) (phase: string) =
            if status <> 0 then
                out ("Error " + phase + ": " + R.Str(API.swmm_get_last_error_msg(engine)) + "\n")
                if result.Value = 0 then result.Value <- status
        fin (API.swmm_engine_end(engine)) "ending simulation"
        fin (API.swmm_engine_report(engine)) "writing report"
        fin (API.swmm_engine_close(engine)) "closing simulation"
        API.swmm_engine_destroy(engine)
        out (System.String.Format(System.Globalization.CultureInfo.InvariantCulture, "... OpenSWMM Engine {0} in {1:F2} seconds.\n\n",
                (if result.Value = 0 then "completed" else "failed"), (System.DateTime.UtcNow - t0).TotalSeconds))
        result.Value

[<EntryPoint>]
let main argv =
    Engine.Init__.run ()
    let rc = run argv
    R.FlushAll()          // returning from main calls exit(), which flushes every open FILE
    rc
