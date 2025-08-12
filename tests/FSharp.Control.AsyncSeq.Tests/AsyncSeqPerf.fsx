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
Real: 00:00:00.095, CPU: 00:00:00.093, GC gen0: 4, gen1: 2, gen2: 0 (gain due to AsyncGenerator)
------------------------------------------------------------------------------------------------------------------------
N=1000000
unfoldChooseIter
Real: 00:00:10.818, CPU: 00:00:10.828, GC gen0: 1114, gen1: 3, gen2: 0
------------------------------------------------------------------------------------------------------------------------
N=1000000
unfoldIter
Real: 00:00:08.565, CPU: 00:00:08.562, GC gen0: 889, gen1: 2, gen2: 0
------------------------------------------------------------------------------------------------------------------------
-- handcoded unfold
N=1000000

unfoldIter
Real: 00:00:08.514, CPU: 00:00:08.562, GC gen0: 890, gen1: 3, gen2: 1
Real: 00:00:00.878, CPU: 00:00:00.875, GC gen0: 96, gen1: 2, gen2: 0 (gain due to hand-coded unfold)

replicate
Real: 00:00:01.530, CPU: 00:00:01.531, GC gen0: 156, gen1: 1, gen2: 0
Real: 00:00:00.926, CPU: 00:00:00.937, GC gen0: 97, gen1: 2, gen2: 0

------------------------------------------------------------------------------------------------------------------------
-- fused unfold (AsyncSeqOp)

unfoldChooseIter
Real: 00:00:02.979, CPU: 00:00:02.968, GC gen0: 321, gen1: 2, gen2: 0
Real: 00:00:00.913, CPU: 00:00:00.906, GC gen0: 115, gen1: 2, gen2: 0 (gain due to fused unfold and choose via AsyncSeqOp)
------------------------------------------------------------------------------------------------------------------------


------------------------------------------------------------------------------------------------------------------------
-- bind via AsyncGenerator (N=10000)

Real: 00:01:09.394, CPU: 00:01:09.109, GC gen0: 4994, gen1: 2661, gen2: 0

Real: 00:00:00.097, CPU: 00:00:00.109, GC gen0: 5, gen1: 2, gen2: 0
Real: 00:00:04.667, CPU: 00:00:04.671, GC gen0: 478, gen1: 2, gen2: 0 (N=1000000)

Real: 00:00:00.658, CPU: 00:00:00.656, GC gen0: 80, gen1: 1, gen2: 0 (N=1000000) via unfoldAsync

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

let replicate (N:int) =
  AsyncSeq.replicate N ()
  |> AsyncSeq.iterAsync (ignore >> async.Return)


let bind cnt =
  let rec generate cnt = asyncSeq {
    if cnt = 0 then () else
    let! v = async.Return 1
    yield v
    yield! generate (cnt-1) }
  generate cnt |> AsyncSeq.iter ignore

let bindUnfold =
  AsyncSeq.unfoldAsync (fun cnt -> async {
    if cnt = 0 then return None
    else
      let! v = async.Return 1
      return Some (v, cnt - 1) })
  >> AsyncSeq.iter ignore



let collect n = 
  AsyncSeq.replicate n ()
  |> AsyncSeq.collect (fun () -> AsyncSeq.singleton ())
  |> AsyncSeq.iter ignore


//run unfoldIter
//run unfoldChooseIter
//run replicate
//run bind
//run bindUnfold
//run collect

let Y = Choice1Of2
let S = Choice2Of2
  
let timeMs = 500

let inp0 = [ ]
let exp0 = [ ]

let inp1 = [ Y 1 ; Y 2 ; S timeMs ; Y 3 ; Y 4 ; S timeMs ; Y 5 ; Y 6 ]
let exp1 = [ [1;2] ; [3;4] ; [5;6] ]

//  let inp2 : Choice<int, int> list = [ S 500 ]
//  let exp2 : int list list = [ [] ; [] ; [] ; []  ]

let toSeq (xs:Choice<int, int> list) = asyncSeq {
  for x in xs do
    match x with
    | Choice1Of2 v -> yield v
    | Choice2Of2 s -> do! Async.Sleep s }    

for (inp,exp) in [ (inp0,exp0) ; (inp1,exp1) ] do

  let actual = 
    toSeq inp
    |> AsyncSeq.bufferByTime (timeMs - 5)
    |> AsyncSeq.map List.ofArray
    |> AsyncSeq.toListSynchronously
  
  printfn "actual=%A expected=%A" actual exp

  //let ls = toSeq inp |> AsyncSeq.toListSynchronously
  //let actualLs = actual |> List.concat

  