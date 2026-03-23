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

/// Benchmarks for filter, choose, and fold operations (optimised direct-enumerator implementations)
[<MemoryDiagnoser>]
[<SimpleJob(RuntimeMoniker.Net80)>]
type AsyncSeqFilterChooseFoldBenchmarks() =

    [<Params(1000, 10000)>]
    member val ElementCount = 0 with get, set

    /// Benchmark filterAsync — all elements pass the predicate
    [<Benchmark(Baseline = true)>]
    member this.FilterAsyncAllPass() =
        AsyncSeq.replicate this.ElementCount 1
        |> AsyncSeq.filterAsync (fun _ -> async.Return true)
        |> AsyncSeq.iterAsync (fun _ -> async.Return())
        |> Async.RunSynchronously

    /// Benchmark filterAsync — no elements pass the predicate (entire sequence scanned)
    [<Benchmark>]
    member this.FilterAsyncNonePass() =
        AsyncSeq.replicate this.ElementCount 1
        |> AsyncSeq.filterAsync (fun _ -> async.Return false)
        |> AsyncSeq.iterAsync (fun _ -> async.Return())
        |> Async.RunSynchronously

    /// Benchmark chooseAsync — all elements selected
    [<Benchmark>]
    member this.ChooseAsyncAllSelected() =
        AsyncSeq.replicate this.ElementCount 42
        |> AsyncSeq.chooseAsync (fun x -> async.Return (Some x))
        |> AsyncSeq.iterAsync (fun _ -> async.Return())
        |> Async.RunSynchronously

    /// Benchmark foldAsync — sum all elements
    [<Benchmark>]
    member this.FoldAsync() =
        AsyncSeq.replicate this.ElementCount 1
        |> AsyncSeq.foldAsync (fun acc x -> async.Return (acc + x)) 0
        |> Async.RunSynchronously
        |> ignore

/// Benchmarks for multi-step pipeline composition
[<MemoryDiagnoser>]
[<SimpleJob(RuntimeMoniker.Net80)>]
type AsyncSeqPipelineBenchmarks() =

    [<Params(1000, 10000)>]
    member val ElementCount = 0 with get, set

    /// Benchmark map → filter → fold pipeline (exercises the three optimised combinators together)
    [<Benchmark(Baseline = true)>]
    member this.MapFilterFold() =
        AsyncSeq.replicate this.ElementCount 1
        |> AsyncSeq.mapAsync  (fun x -> async.Return (x * 2))
        |> AsyncSeq.filterAsync (fun x -> async.Return (x > 0))
        |> AsyncSeq.foldAsync (fun acc x -> async.Return (acc + x)) 0
        |> Async.RunSynchronously
        |> ignore

    /// Benchmark collecting to an array
    [<Benchmark>]
    member this.ToArray() =
        AsyncSeq.replicate this.ElementCount 1
        |> AsyncSeq.toArrayAsync
        |> Async.RunSynchronously
        |> ignore

/// Benchmarks for map and mapi variants — ensures the direct-enumerator optimisation
/// for mapiAsync is visible and comparable against mapAsync.
[<MemoryDiagnoser>]
[<SimpleJob(RuntimeMoniker.Net80)>]
type AsyncSeqMapiBenchmarks() =

    [<Params(1000, 10000)>]
    member val ElementCount = 0 with get, set

    /// Baseline: mapAsync (already uses direct enumerator)
    [<Benchmark(Baseline = true)>]
    member this.MapAsync() =
        AsyncSeq.replicate this.ElementCount 1
        |> AsyncSeq.mapAsync (fun x -> async.Return (x * 2))
        |> AsyncSeq.iterAsync (fun _ -> async.Return())
        |> Async.RunSynchronously

    /// mapiAsync — now uses direct enumerator; should be close to mapAsync cost
    [<Benchmark>]
    member this.MapiAsync() =
        AsyncSeq.replicate this.ElementCount 1
        |> AsyncSeq.mapiAsync (fun i x -> async.Return (i, x * 2))
        |> AsyncSeq.iterAsync (fun _ -> async.Return())
        |> Async.RunSynchronously

    /// mapi — synchronous projection variant; dispatches through mapiAsync
    [<Benchmark>]
    member this.Mapi() =
        AsyncSeq.replicate this.ElementCount 1
        |> AsyncSeq.mapi (fun i x -> (i, x * 2))
        |> AsyncSeq.iterAsync (fun _ -> async.Return())
        |> Async.RunSynchronously

/// Entry point for running benchmarks.
/// Delegates directly to BenchmarkSwitcher so all BenchmarkDotNet CLI options
/// (--filter, --job short, --exporters, etc.) work out of the box.
/// Examples:
///   dotnet run -c Release                                     # run all
///   dotnet run -c Release -- --filter '*Filter*'              # specific class
///   dotnet run -c Release -- --filter '*' --job short         # quick smoke-run
module AsyncSeqBenchmarkRunner =

    [<EntryPoint>]
    let Main args =
        BenchmarkSwitcher
            .FromAssembly(typeof<AsyncSeqCoreBenchmarks>.Assembly)
            .Run(args)
        |> ignore
        0