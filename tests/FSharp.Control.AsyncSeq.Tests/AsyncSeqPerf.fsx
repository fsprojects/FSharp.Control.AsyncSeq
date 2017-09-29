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

let rec prependToAll (a:'a) (ls:'a list) : 'a list =
  match ls with
  | [] -> []
  | hd::tl -> a::hd::prependToAll a tl

let rec intersperse (a:'a) (ls:'a list) : 'a list =
  match ls with
  | [] -> []
  | hd::tl -> hd::prependToAll a tl

let intercalate (l:'a list) (xs:'a list list) : 'a list =
  intersperse l xs |> List.concat

let batch (size:int) (ls:'a list) : 'a list list =
  let rec go batch ls =
    match ls with
    | [] -> [List.rev batch]
    | _ when List.length batch = size -> (List.rev batch)::go [] ls
    | hd::tl -> go (hd::batch) tl
  go [] ls 


let Y = Choice1Of2
let S = Choice2Of2  
let sleepMs = 100

let toSeq (xs:Choice<int, int> list) = asyncSeq {
  for x in xs do
    match x with
    | Choice1Of2 v -> yield v
    | Choice2Of2 s -> do! Async.Sleep s }

for (size,batchSize) in [ (0,0) ; (10,2) ; (30,2) ] do

  let expected = 
    List.init size id
    |> batch batchSize
   
  let actual = 
    expected
    |> List.map (List.map Y)
    |> intercalate [S sleepMs] 
    |> toSeq
    |> AsyncSeq.bufferByTime sleepMs
    |> AsyncSeq.map List.ofArray
    |> AsyncSeq.toList

  //Assert.True ((actual = expected))
  printfn "actual=%A expected=%A" actual expected