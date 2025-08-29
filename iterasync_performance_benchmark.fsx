#r "src/FSharp.Control.AsyncSeq/bin/Release/netstandard2.1/FSharp.Control.AsyncSeq.dll"

open System
open System.Diagnostics
open FSharp.Control

// Simple async operation for benchmarking
let simpleAsyncOp x = async {
    return ()
}

// More realistic async operation (with some work)
let realisticAsyncOp x = async {
    do! Async.Sleep 1  // Simulate very light I/O
    return ()
}

let benchmarkIterAsync name asyncOp elementCount =
    let sequence = AsyncSeq.init elementCount id
    
    // Warmup
    sequence |> AsyncSeq.iterAsync asyncOp |> Async.RunSynchronously
    
    // Benchmark
    let sw = Stopwatch.StartNew()
    let beforeGC0 = GC.CollectionCount(0)
    
    sequence |> AsyncSeq.iterAsync asyncOp |> Async.RunSynchronously
    
    sw.Stop()
    let afterGC0 = GC.CollectionCount(0)
    
    printfn "%s (%d elements): %dms, GC gen0: %d" 
        name elementCount sw.ElapsedMilliseconds (afterGC0 - beforeGC0)

let benchmarkIteriAsync name asyncOp elementCount =
    let sequence = AsyncSeq.init elementCount id
    
    // Warmup
    sequence |> AsyncSeq.iteriAsync (fun i x -> asyncOp x) |> Async.RunSynchronously
    
    // Benchmark
    let sw = Stopwatch.StartNew()
    let beforeGC0 = GC.CollectionCount(0)
    
    sequence |> AsyncSeq.iteriAsync (fun i x -> asyncOp x) |> Async.RunSynchronously
    
    sw.Stop()
    let afterGC0 = GC.CollectionCount(0)
    
    printfn "%s (%d elements): %dms, GC gen0: %d" 
        name elementCount sw.ElapsedMilliseconds (afterGC0 - beforeGC0)

printfn "=== iterAsync Performance Benchmark ==="
printfn ""

// Test different scales
for scale in [10000; 50000; 100000] do
    printfn "--- %d Elements ---" scale
    benchmarkIterAsync "iterAsync (simple)" simpleAsyncOp scale
    benchmarkIterAsync "iterAsync (realistic)" realisticAsyncOp scale
    benchmarkIteriAsync "iteriAsync (simple)" simpleAsyncOp scale
    benchmarkIteriAsync "iteriAsync (realistic)" realisticAsyncOp scale
    printfn ""

// Memory pressure test
printfn "=== Memory Allocation Test ==="
let testMemoryAllocations() =
    let elementCount = 100000
    let sequence = AsyncSeq.init elementCount id
    
    // Force GC before test
    GC.Collect()
    GC.WaitForPendingFinalizers()
    GC.Collect()
    
    let beforeMem = GC.GetTotalMemory(false)
    let beforeGC0 = GC.CollectionCount(0)
    let beforeGC1 = GC.CollectionCount(1)
    let beforeGC2 = GC.CollectionCount(2)
    
    sequence |> AsyncSeq.iterAsync simpleAsyncOp |> Async.RunSynchronously
    
    let afterMem = GC.GetTotalMemory(false)
    let afterGC0 = GC.CollectionCount(0)
    let afterGC1 = GC.CollectionCount(1)
    let afterGC2 = GC.CollectionCount(2)
    
    let memDiff = afterMem - beforeMem
    printfn "Memory difference: %s" (if memDiff >= 0 then sprintf "+%d bytes" memDiff else sprintf "%d bytes" memDiff)
    printfn "GC collections - gen0: %d, gen1: %d, gen2: %d" 
        (afterGC0 - beforeGC0) (afterGC1 - beforeGC1) (afterGC2 - beforeGC2)

testMemoryAllocations()

printfn ""
printfn "=== Performance Summary ==="
printfn "✅ Optimized iterAsync implementation uses:"
printfn "   - Direct tail recursion instead of while loop with refs"
printfn "   - Single enumerator instance with proper disposal"
printfn "   - Eliminated ref allocations (count = ref 0, b = ref move)"
printfn "   - Eliminated closure allocation in iterAsync -> iteriAsync delegation"
printfn "   - Streamlined memory layout with sealed classes"