#!/usr/bin/env dotnet fsi

// Test script to reproduce the iterAsyncParallel cancellation bug from Issue #122
// Run with: dotnet fsi iterAsyncParallel_cancellation_test.fsx

#r "./src/FSharp.Control.AsyncSeq/bin/Release/netstandard2.1/FSharp.Control.AsyncSeq.dll"

open System
open System.Threading
open FSharp.Control

// Reproduce the exact bug from Issue #122
let testCancellationBug() = 
    printfn "Testing iterAsyncParallel cancellation bug..."

    let r = Random()

    let handle x = async {
        do! Async.Sleep (r.Next(200))
        printfn "%A" x
    }

    let fakeAsync = async {    
        do! Async.Sleep 500
        return "hello"
    }

    let makeAsyncSeqBatch () =
        let rec loop() = asyncSeq {            
            let! batch = fakeAsync |> Async.Catch
            match batch with
            | Choice1Of2 batch ->
                if (Seq.isEmpty batch) then
                    do! Async.Sleep 500
                    yield! loop()
                else
                    yield batch
                    yield! loop() 
            | Choice2Of2 err ->
                 printfn "Problem getting batch: %A" err
        }
        loop()

    let x = makeAsyncSeqBatch () |> AsyncSeq.concatSeq |> AsyncSeq.iterAsyncParallel handle
    let exAsync = async {
        do! Async.Sleep 2000
        failwith "error"
    }

    // This should fail after 2 seconds when exAsync throws, but iterAsyncParallel may continue running
    let start = DateTime.Now
    try
        [x; exAsync] |> Async.Parallel |> Async.Ignore |> Async.RunSynchronously
        printfn "ERROR: Expected exception but completed normally"
    with 
    | ex -> 
        let elapsed = DateTime.Now - start
        printfn "Exception after %.1fs: %s" elapsed.TotalSeconds ex.Message
        if elapsed.TotalSeconds > 5.0 then
            printfn "ISSUE CONFIRMED: iterAsyncParallel failed to cancel properly (took %.1fs)" elapsed.TotalSeconds
        else
            printfn "OK: Cancellation worked correctly (took %.1fs)" elapsed.TotalSeconds

// Test with iterAsyncParallelThrottled as well
let testCancellationBugThrottled() = 
    printfn "\nTesting iterAsyncParallelThrottled cancellation bug..."
    
    let handle x = async {
        do! Async.Sleep 100
        printfn "Processing: %A" x
    }

    let longRunningSequence = asyncSeq {
        for i in 1..1000 do
            do! Async.Sleep 50
            yield i
    }

    let x = longRunningSequence |> AsyncSeq.iterAsyncParallelThrottled 5 handle
    let exAsync = async {
        do! Async.Sleep 2000
        failwith "error"
    }

    let start = DateTime.Now
    try
        [x; exAsync] |> Async.Parallel |> Async.Ignore |> Async.RunSynchronously
        printfn "ERROR: Expected exception but completed normally"
    with 
    | ex -> 
        let elapsed = DateTime.Now - start
        printfn "Exception after %.1fs: %s" elapsed.TotalSeconds ex.Message
        if elapsed.TotalSeconds > 5.0 then
            printfn "ISSUE CONFIRMED: iterAsyncParallelThrottled failed to cancel properly (took %.1fs)" elapsed.TotalSeconds
        else
            printfn "OK: Cancellation worked correctly (took %.1fs)" elapsed.TotalSeconds

printfn "=== AsyncSeq iterAsyncParallel Cancellation Test ==="
testCancellationBug()
testCancellationBugThrottled()
printfn "=== Test Complete ==="