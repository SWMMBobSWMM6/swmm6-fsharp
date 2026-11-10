// LibmCheck.fs - compare the F# runtime's math (fsharp/runtime/LibM.fs: glibc's routines by P/Invoke; and the exact
// functions the engine takes from .NET's Math) bit for bit with vectors computed by C programs linked against the
// system libm: tools/libm/test/mkvectors.c and mkedge.c (exp, log, log10, pow, sin, cos, tan, asin, acos, atan),
// mkvectors2.c (cbrt, hypot, atan2, cosh, sinh, tanh, log2, expm1, log1p) and fsharp/test/mkexact.c (sqrt, floor,
// ceil, trunc, fabs, copysign). Every bit must match, NaN sign and payload included. fma, frexp and ldexp, which the
// vector files also hold for the ports that translate glibc, are skipped: the F# port calls none of them.
//   dotnet libmcheck.dll FILE...
// Written by Claude (Anthropic) for Robert Dickinson's SWMM6 ports; see ../README.md.
module LibmCheck

open System
open System.IO

let fns : Map<string, int * (float[] -> float)> =
    Map.ofList [
        "exp", (1, fun a -> Cpp.LibM.exp(a.[0])); "log", (1, fun a -> Cpp.LibM.log(a.[0])); "log10", (1, fun a -> Cpp.LibM.log10(a.[0]))
        "pow", (2, fun a -> Cpp.LibM.pow(a.[0], a.[1])); "sin", (1, fun a -> Cpp.LibM.sin(a.[0])); "cos", (1, fun a -> Cpp.LibM.cos(a.[0]))
        "tan", (1, fun a -> Cpp.LibM.tan(a.[0])); "asin", (1, fun a -> Cpp.LibM.asin(a.[0])); "acos", (1, fun a -> Cpp.LibM.acos(a.[0]))
        "atan", (1, fun a -> Cpp.LibM.atan(a.[0])); "cbrt", (1, fun a -> Cpp.LibM.cbrt(a.[0])); "hypot", (2, fun a -> Cpp.LibM.hypot(a.[0], a.[1]))
        "atan2", (2, fun a -> Cpp.LibM.atan2(a.[0], a.[1])); "cosh", (1, fun a -> Cpp.LibM.cosh(a.[0])); "sinh", (1, fun a -> Cpp.LibM.sinh(a.[0]))
        "tanh", (1, fun a -> Cpp.LibM.tanh(a.[0])); "log2", (1, fun a -> Cpp.LibM.log2(a.[0])); "expm1", (1, fun a -> Cpp.LibM.expm1(a.[0]))
        "log1p", (1, fun a -> Cpp.LibM.log1p(a.[0]))
        "sqrt", (1, fun a -> Math.Sqrt(a.[0])); "floor", (1, fun a -> Math.Floor(a.[0])); "ceil", (1, fun a -> Math.Ceiling(a.[0]))
        "trunc", (1, fun a -> Math.Truncate(a.[0])); "fabs", (1, fun a -> Math.Abs(a.[0])); "copysign", (2, fun a -> Math.CopySign(a.[0], a.[1]))
    ]
// what the vector files hold besides the arguments and the result (fma: 3 arguments; frexp: 1 argument, 2 results;
// ldexp: 2 arguments)
let skipped = Map.ofList [ "fma", 4; "frexp", 3; "ldexp", 3 ]

[<EntryPoint>]
let main argv =
    let mutable total = 0L
    for path in argv do
        printfn "== %s" (Path.GetFileName path)
        use r = new BinaryReader(File.OpenRead(path))
        while r.BaseStream.Position < r.BaseStream.Length do
            let sb = Text.StringBuilder()
            let mutable c = r.ReadByte()
            while c <> 0uy do
                sb.Append(char c) |> ignore
                c <- r.ReadByte()
            let name = sb.ToString()
            let n = int (r.ReadUInt32())
            match fns.TryFind name with
            | None ->
                let per = match skipped.TryFind name with Some k -> k | None -> failwithf "unknown function %s" name
                r.BaseStream.Seek(int64 n * int64 per * 8L, SeekOrigin.Current) |> ignore
                printfn "%-9s skipped (%d records; the F# port does not call it)" name n
            | Some (na, f) ->
                let args = Array.zeroCreate<float> na
                let mutable bad = 0L
                let mutable nanSign = 0L
                let mutable nanPay = 0L
                let mutable shown = 0
                let sw = Diagnostics.Stopwatch.StartNew()
                for _ in 1 .. n do
                    for k in 0 .. na - 1 do args.[k] <- r.ReadDouble()
                    let want = r.ReadDouble()
                    let got = f args
                    let gb = BitConverter.DoubleToInt64Bits(got)
                    let wb = BitConverter.DoubleToInt64Bits(want)
                    if gb <> wb then
                        if Double.IsNaN(got) && Double.IsNaN(want) then
                            if (gb >>> 63) <> (wb >>> 63) then nanSign <- nanSign + 1L else nanPay <- nanPay + 1L
                        else
                            bad <- bad + 1L
                            if shown < 4 then
                                shown <- shown + 1
                                printfn "  %s(%s) = %016x, libm %016x" name (String.Join(", ", args |> Array.map (fun x -> x.ToString("R")))) gb wb
                total <- total + int64 n
                printfn "%-9s %d of %d differ, %d NaN sign, %d NaN payload   (%.3f us/call)" name bad n nanSign nanPay (sw.Elapsed.TotalMilliseconds * 1000.0 / float n)
    printfn "records checked: %d" total
    0
