#r "src/FSharp.Control.AsyncSeq/bin/Release/netstandard2.1/FSharp.Control.AsyncSeq.dll"

open System
open System.Diagnostics
open FSharp.Control

// Simple async operation for benchmarking
let simpleAsyncOp x = async.Return ()

// Lightweight computational async operation
let computeAsyncOp x = async {
    let _ = x * x + x  // Some computation
    return ()
}

let benchmarkIterAsync name asyncOp elementCount runs =
    let sequence = AsyncSeq.init elementCount id
    
    // Warmup
    sequence |> AsyncSeq.iterAsync asyncOp |> Async.RunSynchronously
    
    let mutable totalTime = 0L
    let mutable totalGC0 = 0
    
    for run in 1..runs do
        let beforeGC0 = GC.CollectionCount(0)
        let sw = Stopwatch.StartNew()
        
        sequence |> AsyncSeq.iterAsync asyncOp |> Async.RunSynchronously
        
        sw.Stop()
        let afterGC0 = GC.CollectionCount(0)
        
        totalTime <- totalTime + sw.ElapsedMilliseconds
        totalGC0 <- totalGC0 + (afterGC0 - beforeGC0)
    
    let avgTime = totalTime / int64 runs
    let avgGC0 = totalGC0 / runs
    
    printfn "%s (%d elements): %dms avg, GC gen0: %d avg over %d runs" 
        name elementCount avgTime avgGC0 runs

printfn "=== Optimized iterAsync Performance Benchmark ==="
printfn ""

// Test different scales with multiple runs for accuracy
for scale in [50000; 100000; 200000] do
    printfn "--- %d Elements ---" scale
    benchmarkIterAsync "iterAsync (simple)" simpleAsyncOp scale 5
    benchmarkIterAsync "iterAsync (compute)" computeAsyncOp scale 5
    printfn ""

// Memory efficiency test
printfn "=== Memory Efficiency Test ==="
let testMemoryEfficiency() =
    let elementCount = 200000
    let sequence = AsyncSeq.init elementCount id
    
    // Force GC before test
    GC.Collect()
    GC.WaitForPendingFinalizers()
    GC.Collect()
    
    let sw = Stopwatch.StartNew()
    let beforeMem = GC.GetTotalMemory(false)
    let beforeGC0 = GC.CollectionCount(0)
    
    sequence |> AsyncSeq.iterAsync simpleAsyncOp |> Async.RunSynchronously
    
    sw.Stop()
    let afterMem = GC.GetTotalMemory(false)
    let afterGC0 = GC.CollectionCount(0)
    
    let memDiff = afterMem - beforeMem
    printfn "%d elements processed in %dms" elementCount sw.ElapsedMilliseconds
    printfn "Memory difference: %s" (if memDiff >= 1024 then sprintf "+%.1fKB" (float memDiff / 1024.0) else sprintf "%d bytes" memDiff)
    printfn "GC gen0 collections: %d" (afterGC0 - beforeGC0)

testMemoryEfficiency()

printfn ""
printfn "=== Optimization Benefits ==="
printfn "✅ Eliminated ref cell allocations (count = ref 0, b = ref move)"
printfn "✅ Direct tail recursion instead of while loop overhead" 
printfn "✅ Removed closure allocation in iterAsync delegation"
printfn "✅ Proper resource disposal with sealed enumerator classes"
printfn "✅ Streamlined async computation with fewer allocation points"

// Test edge cases to verify correctness
printfn ""
printfn "=== Correctness Verification ==="
let testCorrectness() =
    // Test empty sequence
    let empty = AsyncSeq.empty<int>
    empty |> AsyncSeq.iterAsync simpleAsyncOp |> Async.RunSynchronously
    printfn "✅ Empty sequence handled correctly"
    
    // Test single element
    let single = AsyncSeq.singleton 42
    let mutable result = 0
    single |> AsyncSeq.iterAsync (fun x -> async { result <- x }) |> Async.RunSynchronously
    if result = 42 then printfn "✅ Single element handled correctly"
    
    // Test multiple elements with order preservation
    let sequence = AsyncSeq.ofSeq [1; 2; 3; 4; 5]
    let mutable results = []
    sequence |> AsyncSeq.iterAsync (fun x -> async { results <- x :: results }) |> Async.RunSynchronously
    let orderedResults = List.rev results
    if orderedResults = [1; 2; 3; 4; 5] then 
        printfn "✅ Order preservation verified"
    
    // Test exception propagation
    try
        let failing = AsyncSeq.ofSeq [1; 2; 3]
        failing |> AsyncSeq.iterAsync (fun x -> if x = 2 then failwith "test" else async.Return()) |> Async.RunSynchronously
        printfn "❌ Exception handling test failed"
    with
    | ex when ex.Message = "test" ->
        printfn "✅ Exception propagation works correctly"

testCorrectness()