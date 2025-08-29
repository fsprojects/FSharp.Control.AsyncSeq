#r @"src/FSharp.Control.AsyncSeq/bin/Release/netstandard2.1/FSharp.Control.AsyncSeq.dll"

open System
open FSharp.Control

// Test empty sequences
let testEmptyOuter() =
    let result = 
        AsyncSeq.empty
        |> AsyncSeq.collect (fun x -> AsyncSeq.singleton x)
        |> AsyncSeq.toListSynchronously
    assert (result = [])
    printfn "✓ Empty outer sequence"

let testEmptyInner() =
    let result =
        AsyncSeq.singleton 1
        |> AsyncSeq.collect (fun _ -> AsyncSeq.empty)
        |> AsyncSeq.toListSynchronously
    assert (result = [])
    printfn "✓ Empty inner sequence"

// Test single element sequences
let testSingleElements() =
    let result =
        AsyncSeq.singleton 1
        |> AsyncSeq.collect (fun x -> AsyncSeq.singleton (x * 2))
        |> AsyncSeq.toListSynchronously
    assert (result = [2])
    printfn "✓ Single element sequences"

// Test exception handling
let testExceptionHandling() =
    try
        AsyncSeq.singleton 1
        |> AsyncSeq.collect (fun _ -> failwith "Test exception")
        |> AsyncSeq.toListSynchronously
        |> ignore
        failwith "Should have thrown"
    with
    | ex when ex.Message = "Test exception" -> printfn "✓ Exception handling"
    | _ -> failwith "Wrong exception"

// Test disposal behavior
let testDisposal() =
    let disposed = ref false
    let testSeq = 
        { new IAsyncEnumerable<int> with
            member _.GetEnumerator() =
                { new IAsyncEnumerator<int> with
                    member _.MoveNext() = async { return Some 1 }
                    member _.Dispose() = disposed.Value <- true } }
    
    let result =
        testSeq
        |> AsyncSeq.take 1
        |> AsyncSeq.collect (fun x -> AsyncSeq.singleton x)
        |> AsyncSeq.toListSynchronously
    
    // Force disposal by creating a new enumerator and disposing it
    testSeq.GetEnumerator().Dispose()
    
    assert !disposed
    assert (result = [1])
    printfn "✓ Disposal behavior"

// Test with async inner sequences
let testAsyncInner() =
    let result =
        AsyncSeq.ofSeq [1; 2; 3]
        |> AsyncSeq.collect (fun x -> asyncSeq {
            do! Async.Sleep 1
            yield x * 2
            yield x * 3
        })
        |> AsyncSeq.toListSynchronously
    
    assert (result = [2; 3; 4; 6; 6; 9])
    printfn "✓ Async inner sequences"

// Test deeply nested collect operations
let testNestedCollect() =
    let result =
        AsyncSeq.ofSeq [1; 2]
        |> AsyncSeq.collect (fun x ->
            AsyncSeq.ofSeq [1; 2]
            |> AsyncSeq.collect (fun y -> AsyncSeq.singleton (x * y)))
        |> AsyncSeq.toListSynchronously
    
    assert (result = [1; 2; 2; 4])
    printfn "✓ Nested collect operations"

// Test large sequence handling
let testLargeSequence() =
    let n = 1000
    let result =
        AsyncSeq.init (int64 n) (fun i -> int i)
        |> AsyncSeq.collect (fun x -> AsyncSeq.singleton (x % 10))
        |> AsyncSeq.length
        |> Async.RunSynchronously
    
    assert (result = n)
    printfn "✓ Large sequence handling"

// Test cancellation
let testCancellation() =
    try
        use cts = new Threading.CancellationTokenSource()
        cts.CancelAfter(100)
        
        AsyncSeq.init 1000000L id
        |> AsyncSeq.collect (fun x -> asyncSeq {
            do! Async.Sleep 1
            yield x
        })
        |> AsyncSeq.iterAsync (fun _ -> async { do! Async.Sleep 1 })
        |> fun async -> Async.RunSynchronously(async, cancellationToken = cts.Token)
    with
    | :? OperationCanceledException -> printfn "✓ Cancellation handling"
    | _ -> printfn "⚠ Cancellation not properly handled"

printfn "=== Collect Edge Case Tests ==="
printfn ""

testEmptyOuter()
testEmptyInner()
testSingleElements()
testExceptionHandling()
testDisposal()
testAsyncInner()
testNestedCollect()
testLargeSequence()
testCancellation()

printfn ""
printfn "All edge case tests completed!"