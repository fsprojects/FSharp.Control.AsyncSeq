#!/usr/bin/env dotnet fsi

// Test script for the new mapAsyncUnorderedParallel function
// Run with: dotnet fsi mapAsyncUnorderedParallel_test.fsx

#r "./src/FSharp.Control.AsyncSeq/bin/Release/netstandard2.1/FSharp.Control.AsyncSeq.dll"

open System
open System.Threading
open FSharp.Control
open System.Diagnostics
open System.Collections.Generic

// Test 1: Basic functionality - ensure results are all present
let testBasicFunctionality() =
    printfn "=== Test 1: Basic Functionality ==="
    
    let input = [1; 2; 3; 4; 5] |> AsyncSeq.ofSeq
    let expected = [2; 4; 6; 8; 10] |> Set.ofList
    
    let actual = 
        input
        |> AsyncSeq.mapAsyncUnorderedParallel (fun x -> async {
            do! Async.Sleep(100)  // Simulate work
            return x * 2
        })
        |> AsyncSeq.toListAsync
        |> Async.RunSynchronously
        |> Set.ofList
    
    if actual = expected then
        printfn "✅ All expected results present: %A" (Set.toList actual)
    else
        printfn "❌ Results mismatch. Expected: %A, Got: %A" (Set.toList expected) (Set.toList actual)

// Test 2: Exception handling
let testExceptionHandling() =
    printfn "\n=== Test 2: Exception Handling ==="
    
    let input = [1; 2; 3; 4; 5] |> AsyncSeq.ofSeq
    
    try
        input
        |> AsyncSeq.mapAsyncUnorderedParallel (fun x -> async {
            if x = 3 then failwith "Test exception"
            return x * 2
        })
        |> AsyncSeq.toListAsync
        |> Async.RunSynchronously
        |> ignore
        printfn "❌ Expected exception but none was thrown"
    with
    | ex -> printfn "✅ Exception correctly propagated: %s" ex.Message

// Test 3: Order independence - results should come in any order
let testOrderIndependence() =
    printfn "\n=== Test 3: Order Independence ==="
    
    let input = [1; 2; 3; 4; 5] |> AsyncSeq.ofSeq
    let results = List<int>()
    
    input
    |> AsyncSeq.mapAsyncUnorderedParallel (fun x -> async {
        // Longer sleep for smaller numbers to test unordered behavior
        do! Async.Sleep(600 - x * 100)  
        results.Add(x)
        return x
    })
    |> AsyncSeq.iter ignore
    |> Async.RunSynchronously
    
    let resultOrder = results |> List.ofSeq
    printfn "Processing order: %A" resultOrder
    
    // In unordered parallel, we expect larger numbers (shorter delays) to complete first
    if resultOrder <> [1; 2; 3; 4; 5] then
        printfn "✅ Results processed in non-sequential order (expected for unordered)"
    else
        printfn "⚠️ Results processed in sequential order (might be coincidental)"

// Test 4: Performance comparison
let performanceComparison() =
    printfn "\n=== Test 4: Performance Comparison ==="
    
    let input = [1..20] |> AsyncSeq.ofSeq
    let workload x = async {
        do! Async.Sleep(50)  // Simulate I/O work
        return x * 2
    }
    
    // Test ordered parallel
    let sw1 = Stopwatch.StartNew()
    let orderedResults = 
        input
        |> AsyncSeq.mapAsyncParallel workload
        |> AsyncSeq.toListAsync
        |> Async.RunSynchronously
    sw1.Stop()
    
    // Test unordered parallel  
    let sw2 = Stopwatch.StartNew()
    let unorderedResults = 
        input
        |> AsyncSeq.mapAsyncUnorderedParallel workload
        |> AsyncSeq.toListAsync
        |> Async.RunSynchronously
        |> List.sort  // Sort for comparison
    sw2.Stop()
    
    printfn "Ordered parallel:   %d ms, results: %A" sw1.ElapsedMilliseconds orderedResults
    printfn "Unordered parallel: %d ms, results: %A" sw2.ElapsedMilliseconds unorderedResults
    
    if List.sort orderedResults = unorderedResults then
        printfn "✅ Both methods produce same results when sorted"
    else
        printfn "❌ Results differ between methods"
    
    let improvement = (float sw1.ElapsedMilliseconds - float sw2.ElapsedMilliseconds) / float sw1.ElapsedMilliseconds * 100.0
    if improvement > 5.0 then
        printfn "✅ Unordered is %.1f%% faster" improvement
    elif improvement < -5.0 then
        printfn "❌ Unordered is %.1f%% slower" (-improvement)
    else
        printfn "➡️ Performance similar (%.1f%% difference)" improvement

// Test 5: Cancellation behavior  
let testCancellation() =
    printfn "\n=== Test 5: Cancellation Behavior ==="
    
    let input = [1..20] |> AsyncSeq.ofSeq
    let cts = new CancellationTokenSource()
    
    // Cancel after 500ms
    Async.Start(async {
        do! Async.Sleep(500)
        cts.Cancel()
    })
    
    let sw = Stopwatch.StartNew()
    try
        let work = input
                   |> AsyncSeq.mapAsyncUnorderedParallel (fun x -> async {
                       do! Async.Sleep(200)  // Each item takes 200ms
                       return x
                   })
                   |> AsyncSeq.iter (fun x -> printfn "Processed: %d" x)
        
        Async.RunSynchronously(work, cancellationToken = cts.Token)
        printfn "❌ Expected cancellation but completed normally in %dms" sw.ElapsedMilliseconds
    with
    | :? OperationCanceledException -> 
        sw.Stop()
        printfn "✅ Cancellation handled correctly after %dms" sw.ElapsedMilliseconds
    | ex -> printfn "❌ Unexpected exception: %s" ex.Message

// Run all tests
printfn "Testing mapAsyncUnorderedParallel Function"
printfn "=========================================="

testBasicFunctionality()
testExceptionHandling() 
testOrderIndependence()
performanceComparison()
testCancellation()

printfn "\n=== All Tests Complete ==="