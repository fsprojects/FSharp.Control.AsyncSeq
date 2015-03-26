module AsyncSeq.Tests.AsyncTest

open System
open System.Threading
open System.Threading.Tasks
open NUnit.Framework
open FSharpx.Control

[<Test>]
let ``Async.ParallelIgnore should run argument computations``() =  
  let bag = System.Collections.Concurrent.ConcurrentBag<_>()  
  let s = Seq.init 10 id |> Set.ofSeq    
  s 
  |> Seq.map (fun i -> bag.Add i ; Async.unit)
  |> Async.ParallelIgnore 1
  |> Async.RunSynchronously
  Assert.True((s = (bag |> Set.ofSeq)))

[<Test>]
let ``Async.ParallelIgnore should fail upon first failure``() =
  let s =
    [
      async { return failwith "catch me if you can" }
    ]
  Assert.Throws<AggregateException>(fun() ->
    s
    |> Async.ParallelIgnore 1
    |> Async.RunSynchronously
  )
  |> ignore

[<Test>]
let ``Async.ParallelIgnore should cancel upon first cancellation``() =
  let tcs = new TaskCompletionSource<unit>()
  let s =
    [
      tcs.Task |> Async.AwaitTask
    ]
  tcs.SetCanceled()
  Assert.Throws<OperationCanceledException>(fun() ->
    s
    |> Async.ParallelIgnore 1
    |> Async.RunSynchronously
  )
  |> ignore
    