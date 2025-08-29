#r @"src/FSharp.Control.AsyncSeq/bin/Release/netstandard2.1/FSharp.Control.AsyncSeq.dll"

open System
open System.Diagnostics
open FSharp.Control

// Restore the old implementation for comparison
module OldCollect =
    [<RequireQualifiedAccess>]
    type CollectState<'T,'U> =
        | NotStarted of AsyncSeq<'T>
        | HaveInputEnumerator of IAsyncEnumerator<'T>
        | HaveInnerEnumerator of IAsyncEnumerator<'T> * IAsyncEnumerator<'U>
        | Finished

    let dispose (x: IDisposable) = x.Dispose()

    let collectOld (f: 'T -> AsyncSeq<'U>) (inp: AsyncSeq<'T>) : AsyncSeq<'U> =
        { new IAsyncEnumerable<'U> with
            member x.GetEnumerator() =
                let state = ref (CollectState.NotStarted inp)
                { new IAsyncEnumerator<'U> with
                    member x.MoveNext() =
                        async { 
                            match !state with
                            | CollectState.NotStarted inp ->
                                return!
                                   (let e1 = inp.GetEnumerator()
                                    state := CollectState.HaveInputEnumerator e1
                                    x.MoveNext())
                            | CollectState.HaveInputEnumerator e1 ->
                                let! res1 = e1.MoveNext()
                                return!
                                   (match res1 with
                                    | Some v1 ->
                                        let e2 = (f v1).GetEnumerator()
                                        state := CollectState.HaveInnerEnumerator (e1, e2)
                                    | None ->
                                        x.Dispose()
                                    x.MoveNext())
                            | CollectState.HaveInnerEnumerator (e1, e2) ->
                                let! res2 = e2.MoveNext()
                                match res2 with
                                | None ->
                                    state := CollectState.HaveInputEnumerator e1
                                    dispose e2
                                    return! x.MoveNext()
                                | Some _ ->
                                    return res2
                            | _ ->
                                return None 
                        }
                    member x.Dispose() =
                        match !state with
                        | CollectState.HaveInputEnumerator e1 ->
                            state := CollectState.Finished
                            dispose e1
                        | CollectState.HaveInnerEnumerator (e1, e2) ->
                            state := CollectState.Finished
                            dispose e2
                            dispose e1
                        | _ -> () 
                } 
        }

let benchmark name f =
    let sw = Stopwatch.StartNew()
    let startGC0 = GC.CollectionCount(0)
    
    let result = f()
    
    sw.Stop()
    let endGC0 = GC.CollectionCount(0)
    
    printfn "%s: %A, GC gen0: %d" name sw.Elapsed (endGC0-startGC0)
    result

// Stress test with many small inner sequences
let stressTestManySmall n collectImpl =
    let input = AsyncSeq.replicate n ()
    input
    |> collectImpl (fun () -> AsyncSeq.replicate 10 1)
    |> AsyncSeq.fold (+) 0
    |> Async.RunSynchronously

// Stress test with fewer large inner sequences  
let stressTestLarge n collectImpl =
    let input = AsyncSeq.replicate n ()
    input
    |> collectImpl (fun () -> AsyncSeq.replicate 1000 1)
    |> AsyncSeq.fold (+) 0
    |> Async.RunSynchronously

// Memory allocation test
let allocationTest n collectImpl =
    let input = AsyncSeq.init (int64 n) (fun i -> int i)
    input
    |> collectImpl (fun i -> AsyncSeq.singleton (i * 2))
    |> AsyncSeq.fold (+) 0
    |> Async.RunSynchronously

let sizes = [5000; 10000; 20000]

printfn "=== Collect Implementation Comparison ==="
printfn ""

for size in sizes do
    printfn "--- %d elements ---" size
    
    // Test many small inner sequences
    benchmark (sprintf "OLD_ManySmall_%d" size) (fun () -> stressTestManySmall size OldCollect.collectOld) |> ignore
    benchmark (sprintf "NEW_ManySmall_%d" size) (fun () -> stressTestManySmall size AsyncSeq.collect) |> ignore
    
    // Test fewer large inner sequences
    let smallerSize = size / 10 // Adjust size to avoid timeout
    benchmark (sprintf "OLD_Large_%d" smallerSize) (fun () -> stressTestLarge smallerSize OldCollect.collectOld) |> ignore
    benchmark (sprintf "NEW_Large_%d" smallerSize) (fun () -> stressTestLarge smallerSize AsyncSeq.collect) |> ignore
    
    // Test allocation patterns
    benchmark (sprintf "OLD_Allocation_%d" size) (fun () -> allocationTest size OldCollect.collectOld) |> ignore
    benchmark (sprintf "NEW_Allocation_%d" size) (fun () -> allocationTest size AsyncSeq.collect) |> ignore
    
    printfn ""