#r @"../../bin/FSharp.Control.AsyncSeq.dll"
#nowarn "40"
#time "on"

open System
open System.Diagnostics
open FSharp.Control

let N = 1000000

let run (test:int -> Async<_>) =
  let sw = Stopwatch.StartNew()
  test N |> Async.RunSynchronously |> ignore
  sw.Stop()

(*

------------------------------------------------------------------------------------------------------------------------
-- append generator
N=5000
unfoldIter
Real: 00:00:03.587, CPU: 00:00:03.578, GC gen0: 346, gen1: 2, gen2: 0
Real: 00:00:00.095, CPU: 00:00:00.093, GC gen0: 4, gen1: 2, gen2: 0
------------------------------------------------------------------------------------------------------------------------
N=1000000
unfoldChooseIter
Real: 00:00:10.818, CPU: 00:00:10.828, GC gen0: 1114, gen1: 3, gen2: 0
------------------------------------------------------------------------------------------------------------------------
N=1000000
unfoldIter
Real: 00:00:08.565, CPU: 00:00:08.562, GC gen0: 889, gen1: 2, gen2: 0
------------------------------------------------------------------------------------------------------------------------

*)
let unfoldIter (N:int) =
  let generator state = async {
    if state < N then
      return Some (state, state + 1)
    else
      return None }
  AsyncSeq.unfoldAsync generator 0
  |> AsyncSeq.iterAsync (ignore >> async.Return)

let unfoldChooseIter (N:int) =
  let generator state = async {
    if state < N then
      return Some (state, state + 1)
    else
      return None }
  AsyncSeq.unfoldAsync generator 0
  |> AsyncSeq.chooseAsync (Some >> async.Return)
  |> AsyncSeq.iterAsync (ignore >> async.Return)

run unfoldIter
//run unfoldChooseIter
