namespace AsyncSeqBenchmarks

open System
open BenchmarkDotNet.Attributes
open BenchmarkDotNet.Configs
open BenchmarkDotNet.Running
open BenchmarkDotNet.Jobs
open BenchmarkDotNet.Engines
open BenchmarkDotNet.Toolchains.InProcess.Emit
open FSharp.Control

/// Core AsyncSeq performance benchmarks focused on foundational operations
[<MemoryDiagnoser>]
[<SimpleJob(RuntimeMoniker.Net80)>]
type AsyncSeqCoreBenchmarks() =
    
    [<Params(1000, 10000)>]
    member val ElementCount = 0 with get, set
    
    /// Benchmark unfoldAsync - core sequence generation
    [<Benchmark(Baseline = true)>]
    member this.UnfoldAsync() =
        let generator state = async {
            if state < this.ElementCount then
                return Some (state, state + 1)
            else
                return None
        }
        AsyncSeq.unfoldAsync generator 0
        |> AsyncSeq.iterAsync (fun _ -> async.Return())
        |> Async.RunSynchronously
    
    /// Benchmark replicate - simple constant generation
    [<Benchmark>]
    member this.Replicate() =
        AsyncSeq.replicate this.ElementCount 42
        |> AsyncSeq.iterAsync (fun _ -> async.Return())
        |> Async.RunSynchronously
    
    /// Benchmark mapAsync - common transformation
    [<Benchmark>]
    member this.MapAsync() =
        AsyncSeq.replicate this.ElementCount 1
        |> AsyncSeq.mapAsync (fun x -> async.Return (x * 2))
        |> AsyncSeq.iterAsync (fun _ -> async.Return())
        |> Async.RunSynchronously
        
    /// Benchmark chooseAsync with high selectivity
    [<Benchmark>]
    member this.ChooseAsync() =
        AsyncSeq.replicate this.ElementCount 1
        |> AsyncSeq.chooseAsync (fun x -> async.Return (Some (x * 2)))
        |> AsyncSeq.iterAsync (fun _ -> async.Return())
        |> Async.RunSynchronously

/// Benchmarks for append operations (previously had memory leaks)
[<MemoryDiagnoser>]
[<SimpleJob(RuntimeMoniker.Net80)>]
type AsyncSeqAppendBenchmarks() =
    
    [<Params(10, 50, 100)>]
    member val ChainCount = 0 with get, set
    
    /// Benchmark chained appends - tests for memory leaks and O(n²) behavior
    [<Benchmark>]
    member this.ChainedAppends() =
        let mutable result = AsyncSeq.singleton 1
        for i in 2 .. this.ChainCount do
            result <- AsyncSeq.append result (AsyncSeq.singleton i)
        result
        |> AsyncSeq.iterAsync (fun _ -> async.Return())
        |> Async.RunSynchronously
    
    /// Benchmark multiple sequence appends
    [<Benchmark>]
    member this.MultipleAppends() =
        let sequences = [1 .. this.ChainCount] |> List.map (fun i -> AsyncSeq.singleton i)
        sequences
        |> List.reduce AsyncSeq.append
        |> AsyncSeq.iterAsync (fun _ -> async.Return())
        |> Async.RunSynchronously

/// Benchmarks for computation builder recursive patterns (previously O(n²))
[<MemoryDiagnoser>]
[<SimpleJob(RuntimeMoniker.Net80)>]
type AsyncSeqBuilderBenchmarks() =
    
    [<Params(50, 100, 200)>]
    member val RecursionDepth = 0 with get, set
    
    /// Benchmark recursive asyncSeq computation - tests for O(n²) regression
    [<Benchmark>]
    member this.RecursiveAsyncSeq() =
        let rec generate cnt = asyncSeq {
            if cnt = 0 then () else
            let! v = async.Return 1
            yield v
            yield! generate (cnt-1)
        }
        generate this.RecursionDepth
        |> AsyncSeq.iterAsync (fun _ -> async.Return())
        |> Async.RunSynchronously
    
    /// Benchmark unfoldAsync equivalent for comparison
    [<Benchmark>]
    member this.UnfoldAsyncEquivalent() =
        AsyncSeq.unfoldAsync (fun cnt -> async {
            if cnt = 0 then return None
            else
                let! v = async.Return 1
                return Some (v, cnt - 1)
        }) this.RecursionDepth
        |> AsyncSeq.iterAsync (fun _ -> async.Return())
        |> Async.RunSynchronously

/// Entry point for running benchmarks
module AsyncSeqBenchmarkRunner =
    
    [<EntryPoint>]
    let Main args =
        printfn "AsyncSeq Performance Benchmarks"
        printfn "================================"
        printfn "Running comprehensive performance benchmarks to establish baseline metrics"
        printfn "and verify fixes for known performance issues (memory leaks, O(n²) patterns)."
        printfn ""
        
        let result = 
            match args |> Array.tryHead with
            | Some "core" ->
                printfn "Running Core Operations Benchmarks..."
                BenchmarkRunner.Run<AsyncSeqCoreBenchmarks>() |> ignore
                0
            | Some "append" ->
                printfn "Running Append Operations Benchmarks..."
                BenchmarkRunner.Run<AsyncSeqAppendBenchmarks>() |> ignore
                0
            | Some "builder" ->
                printfn "Running Builder Pattern Benchmarks..."
                BenchmarkRunner.Run<AsyncSeqBuilderBenchmarks>() |> ignore
                0
            | Some "all" | None ->
                printfn "Running All Benchmarks..."
                BenchmarkRunner.Run<AsyncSeqCoreBenchmarks>() |> ignore
                BenchmarkRunner.Run<AsyncSeqAppendBenchmarks>() |> ignore
                BenchmarkRunner.Run<AsyncSeqBuilderBenchmarks>() |> ignore
                0
            | Some suite ->
                printfn "Unknown benchmark suite: %s" suite
                printfn "Available suites: core, append, builder, all"
                1
        
        printfn ""
        printfn "Benchmarks completed. Results provide baseline performance metrics"
        printfn "for future performance improvements and regression detection."
        result