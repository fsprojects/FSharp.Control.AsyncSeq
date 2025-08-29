// Benchmark script to test append operation performance and memory usage
#r @"src/FSharp.Control.AsyncSeq/bin/Release/netstandard2.1/FSharp.Control.AsyncSeq.dll"
#time "on"

open System
open System.Diagnostics
open FSharp.Control

let measureGC () =
    GC.Collect()
    GC.WaitForPendingFinalizers() 
    GC.Collect()
    let gen0 = GC.CollectionCount(0)
    let gen1 = GC.CollectionCount(1) 
    let gen2 = GC.CollectionCount(2)
    let memory = GC.GetTotalMemory(false)
    (gen0, gen1, gen2, memory)

let printGC label (startGC: int * int * int * int64) (endGC: int * int * int * int64) =
    let (sg0, sg1, sg2, smem) = startGC
    let (eg0, eg1, eg2, emem) = endGC
    printfn "%s - GC gen0: %d, gen1: %d, gen2: %d, Memory: %d bytes" 
        label (eg0 - sg0) (eg1 - sg1) (eg2 - sg2) (emem - smem)

let timeOperation name operation = 
    let startGC = measureGC()
    let sw = Stopwatch.StartNew()
    let result = operation()
    sw.Stop()
    let endGC = measureGC()
    printfn "%s - Time: %A" name sw.Elapsed
    printGC name startGC endGC
    result

// Test 1: Chain many small sequences using append (this triggers the memory leak issue)
let testAppendChain n =
    let singleSeq = AsyncSeq.singleton 1
    let rec buildChain remaining acc =
        if remaining <= 0 then acc
        else buildChain (remaining - 1) (AsyncSeq.append acc singleSeq)
    
    let chain = buildChain n (AsyncSeq.empty)
    AsyncSeq.length chain |> Async.RunSynchronously

// Test 2: Append large sequences (stress test memory usage)  
let testAppendLarge n =
    let seq1 = AsyncSeq.replicate n 1
    let seq2 = AsyncSeq.replicate n 2
    let appended = AsyncSeq.append seq1 seq2
    AsyncSeq.length appended |> Async.RunSynchronously

// Test 3: Multiple appends in sequence (simulation of typical usage)
let testMultipleAppends n =
    let sequences = [ for i in 1..n -> AsyncSeq.replicate 100 i ]
    let result = sequences |> List.reduce AsyncSeq.append
    AsyncSeq.take 10 result |> AsyncSeq.toListAsync |> Async.RunSynchronously

printfn "=== Append Performance Benchmark ==="
printfn "Testing append operations for memory leaks and performance"
printfn ""

// Small chain test - this would cause OutOfMemoryException with old implementation
printfn "Test 1: Chained appends (memory leak test)"
let result1 = timeOperation "Append chain (n=1000)" (fun () -> testAppendChain 1000)
printfn "Result: %A elements" result1
printfn ""

// Large sequence test
printfn "Test 2: Large sequence append"  
let result2 = timeOperation "Append large (n=100000)" (fun () -> testAppendLarge 100000)
printfn "Result: %A elements" result2
printfn ""

// Multiple appends test
printfn "Test 3: Multiple sequence appends"
let result3 = timeOperation "Multiple appends (n=50)" (fun () -> testMultipleAppends 50)
printfn "Result: %A" result3
printfn ""

printfn "=== Benchmark Complete ==="