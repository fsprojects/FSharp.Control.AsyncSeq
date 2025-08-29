#r @"src/FSharp.Control.AsyncSeq/bin/Release/netstandard2.1/FSharp.Control.AsyncSeq.dll"

open System
open System.Diagnostics
open FSharp.Control

let benchmark name f =
    printfn "Running %s..." name
    let sw = Stopwatch.StartNew()
    let startGC0 = GC.CollectionCount(0)
    let startGC1 = GC.CollectionCount(1)
    let startGC2 = GC.CollectionCount(2)
    
    let result = f()
    
    sw.Stop()
    let endGC0 = GC.CollectionCount(0)
    let endGC1 = GC.CollectionCount(1) 
    let endGC2 = GC.CollectionCount(2)
    
    printfn "%s: %A, GC gen0: %d, gen1: %d, gen2: %d" name sw.Elapsed (endGC0-startGC0) (endGC1-startGC1) (endGC2-startGC2)
    result

// Test 1: Simple collect with small inner sequences
let collectSmallInner n =
    let input = AsyncSeq.replicate n ()
    input
    |> AsyncSeq.collect (fun () -> AsyncSeq.replicate 3 1) // Each element produces 3 sub-elements
    |> AsyncSeq.fold (+) 0
    |> Async.RunSynchronously

// Test 2: Collect with larger inner sequences
let collectLargeInner n =
    let input = AsyncSeq.replicate n ()
    input
    |> AsyncSeq.collect (fun () -> AsyncSeq.replicate 100 1) // Each element produces 100 sub-elements
    |> AsyncSeq.fold (+) 0
    |> Async.RunSynchronously

// Test 3: Collect with varying inner sequence sizes (worst case for state management)
let collectVaryingSizes n =
    let input = AsyncSeq.init (int64 n) (fun i -> int i)
    input
    |> AsyncSeq.collect (fun i -> AsyncSeq.replicate (i % 10 + 1) i) // Varying sizes 1-10
    |> AsyncSeq.fold (+) 0
    |> Async.RunSynchronously

// Test 4: Deep nesting with collect
let collectNested n =
    let input = AsyncSeq.replicate n ()
    input
    |> AsyncSeq.collect (fun () -> 
        AsyncSeq.replicate 5 ()
        |> AsyncSeq.collect (fun () -> AsyncSeq.replicate 2 1))
    |> AsyncSeq.fold (+) 0
    |> Async.RunSynchronously

// Test 5: Collect with async inner sequences
let collectAsync n =
    let input = AsyncSeq.replicate n ()
    input
    |> AsyncSeq.collect (fun () -> 
        asyncSeq {
            yield! AsyncSeq.replicate 3 1
            do! Async.Sleep 1 // Small async delay
        })
    |> AsyncSeq.fold (+) 0
    |> Async.RunSynchronously

let testSizes = [1000; 5000; 10000]

printfn "=== Collect Performance Baseline ==="
printfn ""

for size in testSizes do
    printfn "--- Testing with %d elements ---" size
    benchmark (sprintf "collectSmallInner_%d" size) (fun () -> collectSmallInner size) |> ignore
    benchmark (sprintf "collectLargeInner_%d" size) (fun () -> collectLargeInner size) |> ignore  
    benchmark (sprintf "collectVaryingSizes_%d" size) (fun () -> collectVaryingSizes size) |> ignore
    benchmark (sprintf "collectNested_%d" size) (fun () -> collectNested size) |> ignore
    benchmark (sprintf "collectAsync_%d" size) (fun () -> collectAsync size) |> ignore
    printfn ""