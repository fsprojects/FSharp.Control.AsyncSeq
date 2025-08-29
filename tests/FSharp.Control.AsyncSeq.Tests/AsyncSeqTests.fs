#if INTERACTIVE
#r "nuget: NUnit, 3.9.0"
#time "on"
#else

#endif
module AsyncSeqTests

open NUnit.Framework
open FSharp.Control
open System
open System.Threading

type AsyncOps = AsyncOps with
  static member unit : Async<unit> = async { return () }
  static member never = async { do! Async.Sleep(-1) }
  static member timeoutMs (timeoutMs:int) (a:Async<'a>) = async {
    let! a = Async.StartChild(a, timeoutMs)
    return! a }

module AsyncSeq =
  [<GeneralizableValue>]
  let never<'a> : AsyncSeq<'a> = asyncSeq {
    do! AsyncOps.never
    yield invalidOp "" }


let DEFAULT_TIMEOUT_MS = 2000

let randomDelayMs (minMs:int) (maxMs:int) (s:AsyncSeq<'a>) =
  let rand = new Random(int DateTime.Now.Ticks)
  let randSleep = async { do! Async.Sleep(rand.Next(minMs, maxMs)) }
  AsyncSeq.zipWith (fun _ a -> a) (AsyncSeq.replicateInfiniteAsync randSleep) s

let randomDelayDefault (s:AsyncSeq<'a>) =
  randomDelayMs 0 50 s

let randomDelayMax m (s:AsyncSeq<'a>) =
  randomDelayMs 0 m s

let catch (f:'a -> 'b) : 'a -> Choice<'b, exn> =
  fun a ->
    try f a |> Choice1Of2
    with ex -> ex |> Choice2Of2

let rec IsCancellationExn (e:exn) =
  match e with
  | :? OperationCanceledException -> true
  | :? TimeoutException -> true
  | :? AggregateException as x -> x.InnerExceptions |> Seq.filter (IsCancellationExn) |> Seq.isEmpty |> not
  | _ -> false

let AreCancellationExns (e1:exn) (e2:exn) =
  IsCancellationExn e1 && IsCancellationExn e2

/// Determines equality of two async sequences by convering them to lists, ignoring side-effects.
let EQ (a:AsyncSeq<'a>) (b:AsyncSeq<'a>) =
  let exp = a |> AsyncSeq.toListSynchronously
  let act = b |> AsyncSeq.toListSynchronously
  if (exp = act) then true
  else
    printfn "expected=%A" exp
    printfn "actual=%A" act
    false

let runTimeout (timeoutMs:int) (a:Async<'a>) : 'a =
  Async.RunSynchronously (a, timeoutMs)

let runTest a = runTimeout 5000 a

type Assert with

  /// Determines equality of two async sequences by convering them to lists, ignoring side-effects.
  static member AreEqual (expected:AsyncSeq<'a>, actual:AsyncSeq<'a>) =
    Assert.AreEqual (expected, actual, DEFAULT_TIMEOUT_MS, exnEq=(fun _ _ -> true), message=null)

  /// Determines equality of two async sequences by convering them to lists, ignoring side-effects.
  static member AreEqual (expected:AsyncSeq<'a>, actual:AsyncSeq<'a>, message:string) =
    Assert.AreEqual (expected, actual, DEFAULT_TIMEOUT_MS, exnEq=(fun _ _ -> true), message=message)

  /// Determines equality of two async sequences by convering them to lists, ignoring side-effects.
  static member AreEqual (expected:AsyncSeq<'a>, actual:AsyncSeq<'a>, exnEq) =
    Assert.AreEqual (expected, actual, timeout=DEFAULT_TIMEOUT_MS, exnEq=exnEq, message=null)

  /// Determines equality of two async sequences by convering them to lists, ignoring side-effects.
  static member AreEqual (expected:AsyncSeq<'a>, actual:AsyncSeq<'a>, timeout) =
    Assert.AreEqual (expected, actual, timeout=timeout, exnEq=(fun _ _ -> true), message=null)

  /// Determines equality of two async sequences by convering them to lists, ignoring side-effects.
  static member AreEqual (expected:AsyncSeq<'a>, actual:AsyncSeq<'a>, timeout, exnEq) =
    Assert.AreEqual (expected, actual, timeout=timeout, exnEq=exnEq, message=null)

  /// Determines equality of two async sequences by convering them to lists, ignoring side-effects.
  /// Exceptions are caught and compared for equality.
  /// Timeouts ensure liveness.
  static member AreEqual (expected:AsyncSeq<'a>, actual:AsyncSeq<'a>, timeout, exnEq:exn -> exn -> bool, message:string) =
    let expected = expected |> AsyncSeq.toListAsync |> AsyncOps.timeoutMs timeout |> Async.Catch
    let expected = Async.RunSynchronously (expected)
    let actual = actual |> AsyncSeq.toListAsync |> AsyncOps.timeoutMs timeout |> Async.Catch
    let actual = Async.RunSynchronously (actual)
    let message =
      if message = null then sprintf "expected=%A actual=%A" expected actual
      else sprintf "message=%s expected=%A actual=%A" message expected actual
    match expected,actual with
    | Choice1Of2 exp, Choice1Of2 act ->
      Assert.True((exp = act), message)
    | Choice2Of2 exp, Choice2Of2 act ->
      Assert.True((exnEq exp act), message)
    | _ ->
      Assert.Fail(message)

  static member AreEqual (expected:unit -> 'a, actual:unit -> 'a) =
    let expected = (catch expected) ()
    let actual = (catch actual) ()
    let message = sprintf "expected=%A actual=%A" expected actual
    match expected,actual with
    | Choice1Of2 exp, Choice1Of2 act ->
      Assert.True((exp = act), message)
    | Choice2Of2 exp, Choice2Of2 act ->
      ()
    | _ ->
      Assert.Fail(message)

[<Test>]
let ``AsyncSeq.never should equal itself`` () =
  Assert.AreEqual(AsyncSeq.never<int>, AsyncSeq.never<int>, timeout=100, exnEq=AreCancellationExns)

[<Test>]
let ``AsyncSeq.toArraySynchronously``() =
  let s = asyncSeq {
    yield 1
    yield 2
    yield 3
  }
  let a = s |> AsyncSeq.toArraySynchronously
  Assert.True(([|1;2;3|] = a))


[<Test>]
let ``AsyncSeq.toListSynchronously``() =
  let s = asyncSeq {
    yield 1
    yield 2
    yield 3
  }
  let a = s |> AsyncSeq.toListSynchronously
  Assert.True(([1;2;3] = a))


[<Test>]
let ``AsyncSeq.concatSeq works``() =
  let ls = [ [1;2] ; [3;4] ]
  let actual = AsyncSeq.ofSeq ls |> AsyncSeq.concatSeq
  let expected = ls |> List.concat |> AsyncSeq.ofSeq
  Assert.AreEqual(expected, actual)

[<Test>]
let ``AsyncSeq.sum works``() =
  for i in 0 .. 10 do
      let ls = [ 1 .. i ]
      let actual = AsyncSeq.ofSeq ls |> AsyncSeq.sum |> Async.RunSynchronously
      let expected = ls |> List.sum
      Assert.True((expected = actual))


[<Test>]
let ``AsyncSeq.length works``() =
  for i in 0 .. 10 do
      let ls = [ 1 .. i ]
      let actual = AsyncSeq.ofSeq ls |> AsyncSeq.length |> Async.RunSynchronously |> int32
      let expected = ls |> List.length
      Assert.True((expected = actual))

[<Test>]
let ``AsyncSeq.contains works``() =
  for i in 0 .. 10 do
      let ls = [ 1 .. i ]
      for j in [0;i;i+1] do
          let actual = AsyncSeq.ofSeq ls |> AsyncSeq.contains j |> Async.RunSynchronously
          let expected = ls |> List.exists (fun x -> x = j)
          Assert.True((expected = actual))

[<Test>]
let ``AsyncSeq.tryPick works``() =
  for i in 0 .. 10 do
      let ls = [ 1 .. i ]
      for j in [0;i;i+1] do
          let actual = AsyncSeq.ofSeq ls |> AsyncSeq.tryPick (fun x -> if x = j then Some (string (x+1)) else None) |> Async.RunSynchronously
          let expected = ls |> Seq.tryPick (fun x -> if x = j then Some (string (x+1)) else None)
          Assert.True((expected = actual))

[<Test>]
let ``AsyncSeq.pick works``() =
  for i in 0 .. 10 do
      let ls = [ 1 .. i ]
      for j in [0;i;i+1] do
          let chooser x = if x = j then Some (string (x+1)) else None
          let actual () = AsyncSeq.ofSeq ls |> AsyncSeq.pick chooser |> Async.RunSynchronously
          let expected () = ls |> Seq.pick chooser
          Assert.AreEqual(actual, expected)

[<Test>]
let ``AsyncSeq.tryFind works``() =
  for i in 0 .. 10 do
      let ls = [ 1 .. i ]
      for j in [0;i;i+1] do
          let actual = AsyncSeq.ofSeq ls |> AsyncSeq.tryFind (fun x -> x = j) |> Async.RunSynchronously
          let expected = ls |> Seq.tryFind (fun x -> x = j)
          Assert.True((expected = actual))

[<Test>]
let ``AsyncSeq.exists works``() =
  for i in 0 .. 10 do
      let ls = [ 1 .. i ]
      for j in [0;i;i+1] do
          let actual = AsyncSeq.ofSeq ls |> AsyncSeq.exists (fun x -> x = j) |> Async.RunSynchronously
          let expected = ls |> Seq.exists (fun x -> x = j)
          Assert.True((expected = actual))

[<Test>]
let ``AsyncSeq.forall works``() =
  for i in 0 .. 10 do
      let ls = [ 1 .. i ]
      for j in [0;i;i+1] do
          let actual = AsyncSeq.ofSeq ls |> AsyncSeq.forall (fun x -> x = j) |> Async.RunSynchronously
          let expected = ls |> Seq.forall (fun x -> x = j)
          Assert.True((expected = actual))
//[<Test>]
//let ``AsyncSeq.cache works``() =
//  for n in 0 .. 10 do
//          let ls = [ for i in 1 .. n do for j in 1 .. i do yield i ]
//          let actual = ls |> AsyncSeq.ofSeq |> AsyncSeq.cache
//          let expected = ls |> AsyncSeq.ofSeq
//          Assert.True(EQ expected actual)

let shouldEqual expected actual msg =
  if expected <> actual then
    printfn "EXPECTED=%A" expected
    printfn "ACTUAL=%A" actual
    match msg with
    | Some msg -> Assert.Fail msg
    | None -> Assert.Fail ()

[<Test>]
let ``AsyncSeq.cache should work``() =
  for N in [0;1;2;3;100] do
    let expected = List.init N id
    let effects = ref 0
    let s = asyncSeq {
      for item in expected do
        yield item
        do! Async.Sleep 1
        incr effects }
    let cached = s |> AsyncSeq.cache
    let actual1,actual2 =
      (cached, cached)
      ||> AsyncSeq.zipParallel
      |> AsyncSeq.toListSynchronously
      |> List.unzip
    shouldEqual expected actual1 (Some "cached sequence1 was different")
    shouldEqual expected actual2 (Some "cached sequence2 was different")
    shouldEqual expected.Length !effects (Some "iterating cached sequence resulted in multiple iterations of source")

[<Test>]
let ``AsyncSeq.cache does not slow down late consumers``() =
    let src =
        AsyncSeq.initInfiniteAsync (fun _ -> Async.Sleep 1000)
        |> AsyncSeq.cache
    let consume initialDelay amount =
        async {
            do! Async.Sleep (initialDelay:int)
            let timing = System.Diagnostics.Stopwatch.StartNew()
            let! _ =
                src
                |> AsyncSeq.truncate amount
                |> AsyncSeq.length
            return timing.Elapsed.TotalSeconds
        }
    let times =
        Async.Parallel [
            // The first to start will take 10s to consume 10 items
            consume 0 10
            // The second should take no time to consume 5 items, starting 5s later, as the first five items have already been cached.
            consume 5000 5
        ]
        |> Async.RunSynchronously
    Assert.LessOrEqual(abs(times.[0] - 10.0), 2.0f, "Sanity check: lead consumer should take 10s")
    Assert.LessOrEqual(times.[1], 2.0, "Test purpose: follower should only read cached items")

[<Test>]
let ``AsyncSeq.unfoldAsync``() =
  let gen s =
    if s < 3 then (s,s + 1) |> Some
    else None
  let expected = Seq.unfold gen 0 |> AsyncSeq.ofSeq
  let actual = AsyncSeq.unfoldAsync (gen >> async.Return) 0
  Assert.True(EQ expected actual)

[<Test>]
let ``AsyncSeq.unfold``() =
  let n = 3
  let chooser x = if x % 2 = 0 then Some x else None
  let gen s =
    if s < n then (s,s + 1) |> Some
    else None
  let expected = Seq.unfold gen 0 |> Seq.choose chooser |> AsyncSeq.ofSeq
  let actual = AsyncSeq.unfold gen 0 |> AsyncSeq.choose chooser
  Assert.True(EQ expected actual)

[<Test>]
let ``AsyncSeq.unfold choose``() =
  let gen s =
    if s < 3 then (s,s + 1) |> Some
    else None
  let expected = Seq.unfold gen 0 |> AsyncSeq.ofSeq
  let actual = AsyncSeq.unfold gen 0
  Assert.True(EQ expected actual)


[<Test>]
let ``AsyncSeq.interleaveChoice``() =
  let s1 = AsyncSeq.ofSeq ["a";"b";"c"]
  let s2 = AsyncSeq.ofSeq [1;2;3]
  let merged = AsyncSeq.interleaveChoice s1 s2 |> AsyncSeq.toListSynchronously
  Assert.True([Choice1Of2 "a" ; Choice2Of2 1 ; Choice1Of2 "b" ; Choice2Of2 2 ; Choice1Of2 "c" ; Choice2Of2 3] = merged)

[<Test>]
let ``AsyncSeq.interleaveChoice second smaller``() =
  let s1 = AsyncSeq.ofSeq ["a";"b";"c"]
  let s2 = AsyncSeq.ofSeq [1]
  let merged = AsyncSeq.interleaveChoice s1 s2 |> AsyncSeq.toListSynchronously
  Assert.True([Choice1Of2 "a" ; Choice2Of2 1 ; Choice1Of2 "b" ; Choice1Of2 "c" ] = merged)

[<Test>]
let ``AsyncSeq.interleaveChoice second empty``() =
  let s1 = AsyncSeq.ofSeq ["a";"b";"c"]
  let s2 = AsyncSeq.ofSeq []
  let merged = AsyncSeq.interleaveChoice s1 s2 |> AsyncSeq.toListSynchronously
  Assert.True([Choice1Of2 "a" ; Choice1Of2 "b" ; Choice1Of2 "c" ] = merged)

[<Test>]
let ``AsyncSeq.interleaveChoice both empty``() =
  let s1 = AsyncSeq.ofSeq<int> []
  let s2 = AsyncSeq.ofSeq<int> []
  let merged = AsyncSeq.interleaveChoice s1 s2 |> AsyncSeq.toListSynchronously
  Assert.True([ ] = merged)

[<Test>]
let ``AsyncSeq.interleaveChoice first smaller``() =
  let s1 = AsyncSeq.ofSeq ["a"]
  let s2 = AsyncSeq.ofSeq [1;2;3]
  let merged = AsyncSeq.interleaveChoice s1 s2 |> AsyncSeq.toListSynchronously
  Assert.True([Choice1Of2 "a" ; Choice2Of2 1 ; Choice2Of2 2 ; Choice2Of2 3] = merged)



[<Test>]
let ``AsyncSeq.interleaveChoice first empty``() =
  let s1 = AsyncSeq.ofSeq []
  let s2 = AsyncSeq.ofSeq [1;2;3]
  let merged = AsyncSeq.interleaveChoice s1 s2 |> AsyncSeq.toListSynchronously
  Assert.True([Choice2Of2 1 ; Choice2Of2 2 ; Choice2Of2 3] = merged)


[<Test>]
let ``AsyncSeq.interleave``() =
  let s1 = AsyncSeq.ofSeq ["a";"b";"c"]
  let s2 = AsyncSeq.ofSeq ["1";"2";"3"]
  let merged = AsyncSeq.interleave s1 s2 |> AsyncSeq.toListSynchronously
  Assert.True(["a" ; "1" ; "b" ; "2" ; "c" ; "3"] = merged)

[<Test>]
let ``AsyncSeq.interleave second smaller``() =
  let s1 = AsyncSeq.ofSeq ["a";"b";"c"]
  let s2 = AsyncSeq.ofSeq ["1"]
  let merged = AsyncSeq.interleave s1 s2 |> AsyncSeq.toListSynchronously
  Assert.True(["a" ; "1" ; "b" ; "c" ] = merged)

[<Test>]
let ``AsyncSeq.interleave second empty``() =
  let s1 = AsyncSeq.ofSeq ["a";"b";"c"]
  let s2 = AsyncSeq.ofSeq []
  let merged = AsyncSeq.interleave s1 s2 |> AsyncSeq.toListSynchronously
  Assert.True(["a" ; "b" ; "c" ] = merged)

[<Test>]
let ``AsyncSeq.interleave both empty``() =
  let s1 = AsyncSeq.ofSeq<int> []
  let s2 = AsyncSeq.ofSeq<int> []
  let merged = AsyncSeq.interleave s1 s2 |> AsyncSeq.toListSynchronously
  Assert.True([ ] = merged)

[<Test>]
let ``AsyncSeq.interleave first smaller``() =
  let s1 = AsyncSeq.ofSeq ["a"]
  let s2 = AsyncSeq.ofSeq ["1";"2";"3"]
  let merged = AsyncSeq.interleave s1 s2 |> AsyncSeq.toListSynchronously
  Assert.True(["a" ; "1" ; "2" ; "3"] = merged)



[<Test>]
let ``AsyncSeq.interleave first empty``() =
  let s1 = AsyncSeq.ofSeq []
  let s2 = AsyncSeq.ofSeq [1;2;3]
  let merged = AsyncSeq.interleave s1 s2 |> AsyncSeq.toListSynchronously
  Assert.True([1 ; 2 ; 3] = merged)

[<Test>]
let ``AsyncSeq.interleaveMany empty``() =
  let merged = AsyncSeq.interleaveMany [] |> AsyncSeq.toListSynchronously
  Assert.True(List.isEmpty merged)

[<Test>]
let ``AsyncSeq.interleaveMany 1``() =
  let s1 = AsyncSeq.ofSeq ["a";"b";"c"]
  let merged = AsyncSeq.interleaveMany [s1] |> AsyncSeq.toListSynchronously
  Assert.True(["a" ; "b" ; "c" ] = merged)

[<Test>]
let ``AsyncSeq.interleaveMany 3``() =
  let s1 = AsyncSeq.ofSeq ["a";"b"]
  let s2 = AsyncSeq.ofSeq ["i";"j";"k";"l"]
  let s3 = AsyncSeq.ofSeq ["x";"y";"z"]
  let merged = AsyncSeq.interleaveMany [s1;s2;s3] |> AsyncSeq.toListSynchronously
  Assert.True(["a"; "x"; "i"; "y"; "b"; "z"; "j"; "k"; "l"] = merged)


[<Test>]
let ``AsyncSeq.bufferByCount``() =
  let s = asyncSeq {
    yield 1
    yield 2
    yield 3
    yield 4
    yield 5
  }
  let s' = s |> AsyncSeq.bufferByCount 2 |> AsyncSeq.toListSynchronously
  Assert.True(([[|1;2|];[|3;4|];[|5|]] = s'))

[<Test>]
let ``AsyncSeq.bufferByCount various sizes``() =
  for sz in 0 .. 10 do
      let s = asyncSeq {
        for i in 1 .. sz do
           yield i
      }
      let s' = s |> AsyncSeq.bufferByCount 1 |> AsyncSeq.toListSynchronously
      Assert.True(([for i in 1 .. sz -> [|i|]] = s'))

[<Test>]
let ``AsyncSeq.bufferByCount empty``() =
  let s = AsyncSeq.empty<int>
  let s' = s |> AsyncSeq.bufferByCount 2 |> AsyncSeq.toListSynchronously
  Assert.True(([] = s'))


[<Test>]
let ``AsyncSeq.bufferByTimeAndCount``() =
  let s = asyncSeq {
    yield 1
    yield 2
    yield 3
    do! Async.Sleep 250
    yield 4
    yield 5
  }
  let actual = AsyncSeq.bufferByCountAndTime 2 50 s |> AsyncSeq.toListSynchronously
  Assert.True((actual = [ [|1;2|] ; [|3|] ; [|4;5|] ]))

[<Test>]
let ``AsyncSeq.bufferByCountAndTime various sizes``() =
  for sz in 0 .. 10 do
      let s = asyncSeq {
        for i in 1 .. sz do
           yield i
      }
      let s' = s |> AsyncSeq.bufferByCountAndTime 1 1 |> AsyncSeq.toListSynchronously
      Assert.True(([for i in 1 .. sz -> [|i|]] = s'))

[<Test>]
let ``AsyncSeq.bufferByTimeAndCount empty``() =
  let s = AsyncSeq.empty<int>
  let actual = AsyncSeq.bufferByCountAndTime 2 10 s |> AsyncSeq.toListSynchronously
  Assert.True((actual = []))

//[<Test>]
//let ``AsyncSeq.bufferByTime`` () =
//
//  let Y = Choice1Of2
//  let S = Choice2Of2
//
//  let timeMs = 500
//
//  let inp0 = [ ]
//  let exp0 = [ ]
//
//  let inp1 = [ Y 1 ; Y 2 ; S timeMs ; Y 3 ; Y 4 ; S timeMs ; Y 5 ; Y 6 ]
//  let exp1 = [ [1;2] ; [3;4] ; [5;6] ]
//
////  let inp2 : Choice<int, int> list = [ S 500 ]
////  let exp2 : int list list = [ [] ; [] ; [] ; []  ]
//
//  let toSeq (xs:Choice<int, int> list) = asyncSeq {
//    for x in xs do
//      match x with
//      | Choice1Of2 v -> yield v
//      | Choice2Of2 s -> do! Async.Sleep s }
//
//  for (inp,exp) in [ (inp0,exp0) ; (inp1,exp1) ] do
//
//    let actual =
//      toSeq inp
//      |> AsyncSeq.bufferByTime (timeMs - 5)
//      |> AsyncSeq.map List.ofArray
//      |> AsyncSeq.toListSynchronously
//
//    //let ls = toSeq inp |> AsyncSeq.toListSynchronously
//    //let actualLs = actual |> List.concat
//
//    Assert.True ((actual = exp))

// WARNING: Too timing sensitive
//let rec prependToAll (a:'a) (ls:'a list) : 'a list =
//  match ls with
//  | [] -> []
//  | hd::tl -> a::hd::prependToAll a tl
//
//let rec intersperse (a:'a) (ls:'a list) : 'a list =
//  match ls with
//  | [] -> []
//  | hd::tl -> hd::prependToAll a tl
//
//let intercalate (l:'a list) (xs:'a list list) : 'a list =
//  intersperse l xs |> List.concat
//
//let batch (size:int) (ls:'a list) : 'a list list =
//  let rec go batch ls =
//    match ls with
//    | [] -> [List.rev batch]
//    | _ when List.length batch = size -> (List.rev batch)::go [] ls
//    | hd::tl -> go (hd::batch) tl
//  go [] ls
//
//[<Test>]
//let ``AsyncSeq.bufferByTime2`` () =
//
//  let Y = Choice1Of2
//  let S = Choice2Of2
//  let sleepMs = 100
//
//  let toSeq (xs:Choice<int, int> list) = asyncSeq {
//    for x in xs do
//      match x with
//      | Choice1Of2 v -> yield v
//      | Choice2Of2 s -> do! Async.Sleep s }
//
//  for (size,batchSize) in [ (0,0) ; (10,2) ; (100,2) ] do
//
//    let expected =
//      List.init size id
//      |> batch batchSize
//
//    let actual =
//      expected
//      |> List.map (List.map Y)
//      |> intercalate [S sleepMs]
//      |> toSeq
//      |> AsyncSeq.bufferByTime sleepMs
//      |> AsyncSeq.map List.ofArray
//      |> AsyncSeq.toListSynchronously
//
//    Assert.True ((actual = expected))

[<Test>]
let ``AsyncSeq.bufferByCountAndTime should not block`` () =
  let op =
    asyncSeq {
      while true do
      do! Async.Sleep 1000
      yield 0
    }
    |> AsyncSeq.bufferByCountAndTime 10 1000
    |> AsyncSeq.take 3
    |> AsyncSeq.iter (ignore)

  // should return immediately
  // while a blocking call would take > 3sec
  let watch = System.Diagnostics.Stopwatch.StartNew()
  let cts = new CancellationTokenSource()
  Async.StartWithContinuations(op, ignore, ignore, ignore, cts.Token)
  watch.Stop()
  cts.Cancel(false)
  Assert.Less (watch.ElapsedMilliseconds, 1000L)

[<Test>]
let ``AsyncSeq.bufferByTime should not block`` () =
  let op =
    asyncSeq {
      while true do
      do! Async.Sleep 1000
      yield 0
    }
    |> AsyncSeq.bufferByTime 1000
    |> AsyncSeq.take 3
    |> AsyncSeq.iter (ignore)

  // should return immediately
  // while a blocking call would take > 3sec
  let watch = System.Diagnostics.Stopwatch.StartNew()
  let cts = new CancellationTokenSource()
  Async.StartWithContinuations(op, ignore, ignore, ignore, cts.Token)
  watch.Stop()
  cts.Cancel(false)
  Assert.Less (watch.ElapsedMilliseconds, 1000L)

//  let s = asyncSeq {
//    yield 1
//    yield 2
//    do! Async.Sleep 100
//    yield 3
//    yield 4
//    do! Async.Sleep 100
//    yield 5
//    yield 6
//  }

  //let actual =
  //  s
  //  |> AsyncSeq.bufferByTime 100
  //  |> AsyncSeq.map (List.ofArray)
  //  |> AsyncSeq.toListSynchronously

  //let expected = [ [1;2] ; [3;4] ; [5;6] ]

  //Assert.True ((actual = expected))

[<Test>]
let ``try finally works no exception``() =
  let x = ref 0
  let s = asyncSeq {
    try yield 1
    finally x := x.Value + 3
  }
  Assert.True(x.Value = 0)
  let s1 = s |> AsyncSeq.toListSynchronously
  Assert.True(x.Value = 3)
  let s2 = s |> AsyncSeq.toListSynchronously
  Assert.True(x.Value = 6)

[<Test>]
let ``try finally works exception``() =
  let x = ref 0
  let s = asyncSeq {
    try
      try yield 1
          failwith "fffail"
      finally x := x.Value + 1
    finally x := x.Value + 2
  }
  Assert.True(x.Value = 0)
  let s1 = try s |> AsyncSeq.toListSynchronously with _ -> []
  Assert.True((s1 = []))
  Assert.True(x.Value = 3)
  let s2 = try s |> AsyncSeq.toListSynchronously with _ -> []
  Assert.True((s2 = []))
  Assert.True(x.Value = 6)

[<Test>]
let ``try with works exception``() =
  let x = ref 0
  let s = asyncSeq {
    try failwith "ffail"
    with e -> x := x.Value + 3
  }
  Assert.True(x.Value = 0)
  let s1 = try s |> AsyncSeq.toListSynchronously with _ -> []
  Assert.True((s1 = []))
  Assert.True(x.Value = 3)
  let s2 = try s |> AsyncSeq.toListSynchronously with _ -> []
  Assert.True((s2 = []))
  Assert.True(x.Value = 6)

[<Test>]
let ``try with works no exception``() =
  let x = ref 0
  let s = asyncSeq {
    try yield 1
    with e -> x := x.Value + 3
  }
  Assert.True(x.Value = 0)
  let s1 = try s |> AsyncSeq.toListSynchronously with _ -> []
  Assert.True((s1 = [1]))
  Assert.True(x.Value = 0)
  let s2 = try s |> AsyncSeq.toListSynchronously with _ -> []
  Assert.True((s2 = [1]))
  Assert.True(x.Value = 0)

[<Test>]
let ``AsyncSeq.zip``() =
  for la in [ []; [1]; [1;2;3;4;5] ] do
     for lb in [ []; [1]; [1;2;3;4;5] ] do
          let a = la |> AsyncSeq.ofSeq
          let b = lb |> AsyncSeq.ofSeq
          let actual = AsyncSeq.zip a b
          let expected = Seq.zip la lb |> AsyncSeq.ofSeq
          Assert.True(EQ expected actual)


[<Test>]
let ``AsyncSeq.zipWithAsync``() =
  for la in [ []; [1]; [1;2;3;4;5] ] do
     for lb in [ []; [1]; [1;2;3;4;5] ] do
          let a = la |> AsyncSeq.ofSeq
          let b = lb |> AsyncSeq.ofSeq
          let actual = AsyncSeq.zipWithAsync (fun a b -> a + b |> async.Return) a b
          let expected = Seq.zip la lb |> Seq.map ((<||) (+)) |> AsyncSeq.ofSeq
          Assert.True(EQ expected actual)

[<Test>]
let ``AsyncSeq.zipWithAsyncParallel``() =
  for la in [ []; [1]; [1;2;3;4;5] ] do
     for lb in [ []; [1]; [1;2;3;4;5] ] do
          let a = la |> AsyncSeq.ofSeq
          let b = lb |> AsyncSeq.ofSeq
          let actual = AsyncSeq.zipWithAsyncParallel (fun a b -> a + b |> async.Return) a b
          let expected = Seq.zip la lb |> Seq.map ((<||) (+)) |> AsyncSeq.ofSeq
          Assert.True(EQ expected actual)

[<Test>]
let ``AsyncSeq.append works``() =
  for la in [ []; [1]; [1;2;3;4;5] ] do
     for lb in [ []; [1]; [1;2;3;4;5] ] do
          let a = la |> AsyncSeq.ofSeq
          let b = lb |> AsyncSeq.ofSeq
          let actual = AsyncSeq.append a b
          let expected = List.append la lb |> AsyncSeq.ofSeq
          Assert.True(EQ expected actual)


[<Test>]
let ``AsyncSeq.skipWhileAsync``() =
  for ls in [ []; [1]; [3]; [1;2;3;4;5] ] do
      let p i = i <= 2
      let actual = ls |> AsyncSeq.ofSeq |> AsyncSeq.skipWhileAsync (p >> async.Return)
      let expected = ls |> Seq.skipWhile p |> AsyncSeq.ofSeq
      Assert.True(EQ expected actual)


[<Test>]
let ``AsyncSeq.takeWhileAsync``() =
  for ls in [ []; [1]; [1;2;3;4;5] ] do
      let p i = i < 4
      let actual = ls |> AsyncSeq.ofSeq |> AsyncSeq.takeWhileAsync (p >> async.Return)
      let expected = ls |> Seq.takeWhile p |> AsyncSeq.ofSeq
      Assert.True(EQ expected actual)

[<Test>]
let ``AsyncSeq.takeWhileInclusive``() =
  for ls in [ []; [1]; [4]; [4;5]; [1;2;3;4;5] ] do
      let p i = i < 4
      let pInclusive i = i <= 4
      let actual = ls |> AsyncSeq.ofSeq |> AsyncSeq.takeWhileInclusive p
      let expected = ls |> Seq.filter(pInclusive) |> AsyncSeq.ofSeq
      Assert.True(EQ expected actual)


[<Test>]
let ``AsyncSeq.take 3 of 5``() =
  let ls = [1;2;3;4;5]
  let c = 3
  let actual = ls |> AsyncSeq.ofSeq |> AsyncSeq.take c
  let expected = ls |> Seq.take c |> AsyncSeq.ofSeq
  Assert.True(EQ expected actual)

[<Test>]
let ``AsyncSeq.take 0 of 5``() =
  let ls = [1;2;3;4;5]
  let c = 0
  let actual = ls |> AsyncSeq.ofSeq |> AsyncSeq.take c
  let expected = ls |> Seq.take c |> AsyncSeq.ofSeq
  Assert.True(EQ expected actual)


[<Test>]
let ``AsyncSeq.take 5 of 5``() =
  let ls = [1;2;3;4;5]
  let c = 5
  let actual = ls |> AsyncSeq.ofSeq |> AsyncSeq.take c
  let expected = ls |> Seq.take c |> AsyncSeq.ofSeq
  Assert.True(EQ expected actual)


[<Test>]
let ``AsyncSeq.skip 3 of 5``() =
  let ls = [1;2;3;4;5]
  let c = 3
  let actual = ls |> AsyncSeq.ofSeq |> AsyncSeq.skip c
  let expected = ls |> Seq.skip c |> AsyncSeq.ofSeq
  Assert.True(EQ expected actual)


[<Test>]
let ``AsyncSeq.skip 0 of 5``() =
  let ls = [1;2;3;4;5]
  let c = 0
  let actual = ls |> AsyncSeq.ofSeq |> AsyncSeq.skip c
  let expected = ls |> Seq.skip c |> AsyncSeq.ofSeq
  Assert.True(EQ expected actual)


[<Test>]
let ``AsyncSeq.skip 5 of 5``() =
  let ls = [1;2;3;4;5]
  let c = 5
  let actual = ls |> AsyncSeq.ofSeq |> AsyncSeq.skip c
  let expected = ls |> Seq.skip c |> AsyncSeq.ofSeq
  Assert.True(EQ expected actual)

[<Test>]
let ``AsyncSeq.skip 1 of 5``() =
  let ls = [1;2;3;4;5]
  let c = 1
  let actual = ls |> AsyncSeq.ofSeq |> AsyncSeq.skip c
  let expected = ls |> Seq.skip c |> AsyncSeq.ofSeq
  Assert.True(EQ expected actual)



[<Test>]
let ``AsyncSeq.threadStateAsync``() =
  let ls = [1;2;3;4;5]
  let actual = ls |> AsyncSeq.ofSeq |> AsyncSeq.threadStateAsync (fun i a -> async.Return(i + a, i + 1)) 0
  let expected = [1;3;5;7;9] |> AsyncSeq.ofSeq
  Assert.True(EQ expected actual)


[<Test>]
let ``AsyncSeq.scanAsync``() =
  for ls in [ []; [1]; [3]; [1;2;3;4;5] ] do
      let f i a = i + a
      let z = 0
      let actual = ls |> AsyncSeq.ofSeq |> AsyncSeq.scanAsync (fun i a -> f i a |> async.Return) z
      let expected = ls |> List.scan f z |> AsyncSeq.ofSeq
      Assert.True(EQ expected actual)

[<Test>]
let ``AsyncSeq.scan``() =
  for ls in [ []; [1]; [3]; [1;2;3;4;5] ] do
      let f i a = i + a
      let z = 0
      let actual = ls |> AsyncSeq.ofSeq |> AsyncSeq.scan (fun i a -> f i a) z
      let expected = ls |> List.scan f z |> AsyncSeq.ofSeq
      Assert.True(EQ expected actual)


[<Test>]
let ``AsyncSeq.foldAsync``() =
  for ls in [ []; [1]; [3]; [1;2;3;4;5] ] do
      let f i a = i + a
      let z = 0
      let actual = ls |> AsyncSeq.ofSeq |> AsyncSeq.foldAsync (fun i a -> f i a |> async.Return) z |> Async.RunSynchronously
      let expected = ls |> Seq.fold f z
      Assert.True((expected = actual))


[<Test>]
let ``AsyncSeq.filterAsync``() =
  for ls in [ []; [1]; [4]; [1;2;3;4;5] ] do
      let p i = i > 3
      let actual = ls |> AsyncSeq.ofSeq |> AsyncSeq.filterAsync (p >> async.Return)
      let expected = ls |> Seq.filter p |> AsyncSeq.ofSeq
      Assert.True(EQ expected actual)

[<Test>]
let ``AsyncSeq.filter``() =
  for ls in [ []; [1]; [4]; [1;2;3;4;5] ] do
      let p i = i > 3
      let actual = ls |> AsyncSeq.ofSeq |> AsyncSeq.filter p
      let expected = ls |> Seq.filter p |> AsyncSeq.ofSeq
      Assert.True(EQ expected actual)

[<Test>]
let ``AsyncSeq.merge``() =
  let ls1 = [1;2;3;4;5]
  let ls2 = [6;7;8;9;10]
  let actual = AsyncSeq.merge (AsyncSeq.ofSeq ls1) (AsyncSeq.ofSeq ls2) |> AsyncSeq.toListSynchronously |> Set.ofList
  let expected = ls1 @ ls2 |> Set.ofList
  Assert.True((expected = actual))

[<Test>]
let ``AsyncSeq.mergeChoice``() =
  let ls1 = [1;2;3;4;5]
  let ls2 = [6.;7.;8.;9.;10.]
  let actual = AsyncSeq.mergeChoice (AsyncSeq.ofSeq ls1) (AsyncSeq.ofSeq ls2) |> AsyncSeq.toListSynchronously |> Set.ofList
  let expected = (List.map Choice1Of2 ls1) @ (List.map Choice2Of2 ls2) |> Set.ofList
  Assert.True((expected = actual))


[<Test>]
let ``AsyncSeq.merge should be fair``() =
  let s1 = asyncSeq {
    do! Async.Sleep 10
    yield 1
  }
  let s2 = asyncSeq {
    yield 2
  }
  let actual = AsyncSeq.merge s1 s2
  let expected = [2;1] |> AsyncSeq.ofSeq
  Assert.True(EQ expected actual)

[<Test>]
let ``AsyncSeq.merge should be fair 2``() =
  let s1 = asyncSeq {
    yield 1
  }
  let s2 = asyncSeq {
    do! Async.Sleep 10
    yield 2
  }
  let actual = AsyncSeq.merge s1 s2
  let expected = [1;2] |> AsyncSeq.ofSeq
  Assert.True(EQ expected actual)

[<Test>]
let ``AsyncSeq.replicate``() =
  let c = 10
  let x = "hello"
  let actual = AsyncSeq.replicate 100 x |> AsyncSeq.take c
  let expected = List.replicate c x |> AsyncSeq.ofSeq
  Assert.True(EQ expected actual)

[<Test>]
let ``AsyncSeq.replicateInfinite``() =
  let c = 10
  let x = "hello"
  let actual = AsyncSeq.replicateInfinite x |> AsyncSeq.take c
  let expected = List.replicate c x |> AsyncSeq.ofSeq
  Assert.True(EQ expected actual)

[<Test>]
let ``AsyncSeq.init``() =
  for c in [0; 1; 100] do
      let actual = AsyncSeq.init (int64 c) string
      let expected = List.init c string |> AsyncSeq.ofSeq
      Assert.True(EQ expected actual)

[<Test>]
let ``AsyncSeq.initInfinite``() =
  for c in [0; 1; 100] do
      let actual = AsyncSeq.initInfinite string  |> AsyncSeq.take c
      let expected = List.init c string |> AsyncSeq.ofSeq
      Assert.True(EQ expected actual)

[<Test>]
let ``AsyncSeq.collect works``() =
  for c in [0; 1; 10] do
      let actual = AsyncSeq.collect (fun i -> AsyncSeq.ofSeq [ 0 .. i]) (AsyncSeq.ofSeq [ 0 .. c ])
      let expected = [ for i in 0 .. c do yield! [ 0 .. i ] ] |> AsyncSeq.ofSeq
      Assert.AreEqual(expected, actual)


[<Test>]
let ``AsyncSeq.initInfinite scales``() =
    AsyncSeq.initInfinite string  |> AsyncSeq.take 1000 |> AsyncSeq.iter ignore |> Async.RunSynchronously

[<Test>]
let ``AsyncSeq.initAsync``() =
  for c in [0; 1; 100] do
      let actual = AsyncSeq.initAsync (int64 c) (string >> async.Return)
      let expected = List.init c string |> AsyncSeq.ofSeq
      Assert.True(EQ expected actual)

[<Test>]
let ``AsyncSeq.initInfiniteAsync``() =
  for c in [0; 1; 100] do
      let actual = AsyncSeq.initInfiniteAsync (string >> async.Return) |> AsyncSeq.take c
      let expected = List.init c string |> AsyncSeq.ofSeq
      Assert.True(EQ expected actual)

[<Test>]
let ``AsyncSeq.traverseOptionAsync``() =
  let seen = ResizeArray<_>()
  let s = [1;2;3;4;5] |> AsyncSeq.ofSeq
  let f i =
    seen.Add i
    if i < 2 then Some i |> async.Return
    else None |> async.Return
  let r = AsyncSeq.traverseOptionAsync f s |> Async.RunSynchronously
  match r with
  | Some _ -> Assert.Fail()
  | None -> Assert.True(([1;2] = (seen |> List.ofSeq)))

[<Test>]
let ``AsyncSeq.traverseChoiceAsync``() =
  let seen = ResizeArray<_>()
  let s = [1;2;3;4;5] |> AsyncSeq.ofSeq
  let f i =
    seen.Add i
    if i < 2 then Choice1Of2 i |> async.Return
    else Choice2Of2 "oh no" |> async.Return
  let r = AsyncSeq.traverseChoiceAsync f s |> Async.RunSynchronously
  match r with
  | Choice1Of2 _ -> Assert.Fail()
  | Choice2Of2 e ->
    Assert.AreEqual("oh no", e)
    Assert.True(([1;2] = (seen |> List.ofSeq)))


[<Test>]
let ``AsyncSeq.toBlockingSeq does not hung forever and rethrows exception``() =
  let s = asyncSeq {
      yield 1
      failwith "error"
  }
  Assert.Throws<Exception>(fun _ -> s |> AsyncSeq.toBlockingSeq |> Seq.toList |> ignore) |> ignore
  try
      let _ = s |> AsyncSeq.toBlockingSeq |> Seq.toList
      ()
  with e ->
      Assert.AreEqual(e.Message, "error")


[<Test>]
let ``AsyncSeq.distinctUntilChangedWithAsync``() =
  let ls = [1;1;2;2;3;4;5;1]
  let s = ls |> AsyncSeq.ofSeq
  let c a b =
    if a = b then true |> async.Return
    else false |> async.Return
  let actual = s |> AsyncSeq.distinctUntilChangedWithAsync c
  let expected = [1;2;3;4;5;1] |> AsyncSeq.ofSeq
  Assert.True(EQ expected actual)


[<Test>]
let ``AsyncSeq.takeUntil should complete immediately with completed signal``() =
  let s = asyncSeq {
    do! Async.Sleep 10
    yield 1
    yield 2
  }
  let actual = AsyncSeq.takeUntilSignal AsyncOps.unit s
  Assert.True(EQ AsyncSeq.empty actual)


[<Test>]
let ``AsyncSeq.takeUntil should take entire sequence with never signal``() =
  let expected = [1;2;3;4] |> AsyncSeq.ofSeq
  let actual = expected |> AsyncSeq.takeUntilSignal AsyncOps.never
  Assert.True(EQ expected actual)

[<Test>]
let ``AsyncSeq.singleton works``() =
  let expected = [1] |> AsyncSeq.ofSeq
  let actual = AsyncSeq.singleton 1
  Assert.True(EQ expected actual)


[<Test>]
let ``AsyncSeq.skipUntil should not skip with completed signal``() =
  let expected = [1;2;3;4] |> AsyncSeq.ofSeq
  let actual =
      asyncSeq {
          do! Async.Sleep 100
          yield! expected
      }
      |> AsyncSeq.skipUntilSignal AsyncOps.unit

  Assert.True(EQ expected actual)


[<Test>]
let ``AsyncSeq.skipUntil should skip everything with never signal``() =
  let actual = [1;2;3;4] |> AsyncSeq.ofSeq |> AsyncSeq.skipUntilSignal AsyncOps.never
  Assert.True(EQ AsyncSeq.empty actual)

[<Test>]
let ``AsyncSeq.toBlockingSeq should work length 1``() =
  let s = asyncSeq { yield 1 } |> AsyncSeq.toBlockingSeq  |> Seq.toList
  Assert.True((s = [1]))

[<Test>]
let ``AsyncSeq.toBlockingSeq should work length 0``() =
  let s = asyncSeq { () } |> AsyncSeq.toBlockingSeq  |> Seq.toList
  Assert.True((s = []))

[<Test>]
let ``AsyncSeq.toBlockingSeq should work length 2 with sleep``() =
  let s = asyncSeq { yield 1
                     do! Async.Sleep 10
                     yield 2  } |> AsyncSeq.toBlockingSeq  |> Seq.toList
  Assert.True((s = [1; 2]))

[<Test>]
let ``AsyncSeq.toBlockingSeq should work length 1 with fail``() =
  let s =
      asyncSeq { yield 1
                 failwith "fail"  }
      |> AsyncSeq.toBlockingSeq
      |> Seq.truncate 1
      |> Seq.toList
  Assert.True((s = [1]))

[<Test>]
let ``AsyncSeq.toBlockingSeq should work length 0 with fail``() =
  let s =
      asyncSeq { failwith "fail"  }
      |> AsyncSeq.toBlockingSeq
      |> Seq.truncate 0
      |> Seq.toList
  Assert.True((s = []))

[<Test>]
let ``AsyncSeq.toBlockingSeq should be cancellable``() =
  let cancelCount = ref 0
  let aseq =
      asyncSeq {
          use! a = Async.OnCancel(fun x -> incr cancelCount)
          while true do
              yield 1
              do! Async.Sleep 10
    }

  let asSeq = aseq |> AsyncSeq.toBlockingSeq
  let enum = asSeq.GetEnumerator()
  Assert.AreEqual(cancelCount.Value, 0)
  let canMoveNext = enum.MoveNext()
  Assert.AreEqual(canMoveNext, true)
  Assert.AreEqual(cancelCount.Value, 0)
  enum.Dispose()
  System.Threading.Thread.Sleep(1000) // wait for task cancellation to be effective
  Assert.AreEqual(cancelCount.Value, 1)

[<Test>]
let ``AsyncSeq.while should allow do at end``() =
  let s1 = asyncSeq {
    while false do
        yield 1
        do! Async.Sleep 10
  }
  Assert.True(true)

let observe vs err =
    let discarded = ref false
    { new IObservable<'U> with
            member x.Subscribe(observer) =
                for v in vs do
                   observer.OnNext v
                if err then
                   observer.OnError (Failure "fail")
                observer.OnCompleted()
                { new IDisposable with member __.Dispose() = discarded := true }  },
    (fun _ -> discarded.Value)

[<Test>]
let ``AsyncSeq.ofObservableBuffered should work (empty)``() =
  let src, discarded = observe [] false
  Assert.True(src |> AsyncSeq.ofObservableBuffered |> AsyncSeq.toListSynchronously = [])
  Assert.True(discarded())

[<Test>]
let ``AsyncSeq.ofObservableBuffered should work (singleton)``() =
  let src, discarded = observe [1] false
  Assert.True(src |> AsyncSeq.ofObservableBuffered |> AsyncSeq.toListSynchronously = [1])
  Assert.True(discarded())

[<Test>]
let ``AsyncSeq.ofObservableBuffered should work (ten)``() =
  let src, discarded = observe [1..10] false
  Assert.True(src |> AsyncSeq.ofObservableBuffered |> AsyncSeq.toListSynchronously = [1..10])
  Assert.True(discarded())

[<Test>]
let ``AsyncSeq.ofObservableBuffered should work (empty, fail)``() =
  let src, discarded = observe [] true
  Assert.True(try (src |> AsyncSeq.ofObservableBuffered |> AsyncSeq.toListSynchronously |> ignore); false with _ -> true)
  Assert.True(discarded())

[<Test>]
let ``AsyncSeq.ofObservableBuffered should work (one, fail)``() =
  let src, discarded = observe [1] true
  Assert.True(try (src |> AsyncSeq.ofObservableBuffered |> AsyncSeq.toListSynchronously |> ignore); false with _ -> true)
  Assert.True(discarded())

[<Test>]
let ``AsyncSeq.ofObservableBuffered should work (one, take)``() =
  let src, discarded = observe [1] true
  Assert.True(src |> AsyncSeq.ofObservableBuffered |> AsyncSeq.take 1 |> AsyncSeq.toListSynchronously = [1])
  Assert.True(discarded())

[<Test>]
let ``AsyncSeq.getIterator should work``() =
  let s1 = [1..2] |> AsyncSeq.ofSeq
  use i = s1.GetEnumerator()
  match i.MoveNext() |> Async.RunSynchronously with
  | None -> Assert.Fail("expected Some")
  | Some v ->
    Assert.AreEqual(v,1)
    match i.MoveNext() |> Async.RunSynchronously with
    | None -> Assert.Fail("expected Some")
    | Some v ->
        Assert.AreEqual(v,2)
        match i.MoveNext() |> Async.RunSynchronously with
        | None -> ()
        | Some _ -> Assert.Fail("expected None")



[<Test>]
let ``asyncSeq.For should delay``() =
  let (s:seq<int>) =
     { new System.Collections.Generic.IEnumerable<int> with
           member x.GetEnumerator() = failwith "fail"
       interface System.Collections.IEnumerable with
           member x.GetEnumerator() = failwith "fail"  }
  Assert.DoesNotThrow(fun _ -> asyncSeq.For(s, (fun v -> AsyncSeq.empty)) |> ignore)

[<Test>]
let ``Async.mergeAll should work``() =
    for n in 0 .. 10 do
        let expected =
            [ for i in 1 .. n do
                    for j in 0 .. i do
                      yield i
                 ]
            |> List.sort
        let actual =
            [ for i in 1 .. n ->
                asyncSeq {
                    for j in 0 .. i do
                      do! Async.Sleep 1;
                      yield i
                } ]
            |> AsyncSeq.mergeAll
            |> AsyncSeq.toListSynchronously
            |> List.sort
        Assert.True((actual = expected), sprintf "mergeAll test at n = %d" n)


[<Test>]
let ``Async.mergeAll should perform well``() =
    let mergeTest n =
        [ for i in 1 .. n ->
            asyncSeq{ do! Async.Sleep 1000;
                      yield i } ]
        |> AsyncSeq.mergeAll
        |> AsyncSeq.toListSynchronously

    Assert.DoesNotThrow(fun _ -> mergeTest 1000 |> ignore)

[<Test>]
let ``Async.mergeAll should be fair``() =
  let s1 = asyncSeq {
    do! Async.Sleep 1000
    yield 1
  }
  let s2 = asyncSeq {
    do! Async.Sleep 100
    yield 2
  }
  let s3 = asyncSeq {
    yield 3
  }
  let actual = AsyncSeq.mergeAll [s1; s2; s3]
  let expected = [3;2;1] |> AsyncSeq.ofSeq
  Assert.True(EQ expected actual)

[<Test>]
let ``AsyncSeq.mergeAll should fail with AggregateException if a task fails``() =
    for n in 0 .. 10 do
      Assert.Throws<AggregateException>(fun _ ->
          [ for i in 0 .. n ->
                    asyncSeq { yield 1
                               if (i % 4) = 0 then
                                    failwith "fail"
                               yield 2 } ]
          |> AsyncSeq.mergeAll
          |> AsyncSeq.toListSynchronously
          |> ignore) |> ignore

[<Test>]
let ``AsyncSeq.merge should fail with AggregateException if a task fails``() =
      for i in 0 .. 1 do
        Assert.Throws<AggregateException>(fun _ ->
          (asyncSeq { yield 1
                      if i % 2 = 0 then failwith "fail"
                      yield 2 },
           asyncSeq { yield 1
                      if i % 2 = 1 then failwith "fail"
                      yield 2 })
          ||> AsyncSeq.merge
          |> AsyncSeq.toListSynchronously
          |> ignore) |> ignore


[<Test>]
let ``AsyncSeq.mergeChoice should fail with AggregateException if a task fails``() =
      for i in 0 .. 1 do
        Assert.Throws<AggregateException>(fun _ ->
          (asyncSeq { yield 1
                      if i % 2 = 0 then failwith "fail"
                      yield 2 },
           asyncSeq { yield 1
                      if i % 2 = 1 then failwith "fail"
                      yield 2 })
          ||> AsyncSeq.mergeChoice
          |> AsyncSeq.toListSynchronously
          |> ignore) |> ignore

[<Test>]
let ``AsyncSeq.interleave should fail with Exception if a task fails``() =
      for i in 0 .. 1 do
        Assert.Throws<Exception>(fun _ ->
          (asyncSeq { yield 1
                      if i % 2 = 0 then failwith "fail"
                      yield 2 },
           asyncSeq { yield 1
                      if i % 2 = 1 then failwith "fail"
                      yield 2 })
          ||> AsyncSeq.interleave
          |> AsyncSeq.toListSynchronously
          |> ignore) |> ignore


let perfTest1 n =
    let empty = async { return () }
    Seq.init n id
    |> AsyncSeq.ofSeq
    |> AsyncSeq.iterAsync (fun _ -> empty )
    |> Async.RunSynchronously

// n                            NEW        1.15.0
//perfTest1 1000 -             0.001        0.004
//perfTest1 2000 -
//perfTest1 3000 -
//perfTest1 4000 -
//perfTest1 5000 -
//perfTest1 6000 -             0.006        0.020
//perfTest1 10000 -            0.012
//perfTest1 100000 -           0.129        0.260
//perfTest1 1000000 -          0.708        2.345


let perfTest2 n =
    Seq.init n id
    |> AsyncSeq.ofSeq
    |> AsyncSeq.toArraySynchronously

// n                        NEW
//perfTest2 1000            0.038
//perfTest2 2000            0.001
//perfTest2 3000            0.004
//perfTest2 4000
//perfTest2 5000
//perfTest2 10000           0.007
//perfTest2 100000          0.076
//perfTest2 1000000         0.663


// This was the original ofSeq implementation.  It is now faster than before this perf testing
// took place, but is still slower than the bespoke ofSeq implementation (which effectively "knows"
// that a single yield happens for each iteration of the loop).
let perfTest3 n =
    let ofSeq2 (source : seq<'T>) = asyncSeq {
        for el in source do
          yield el }

    Seq.init n id
    |> ofSeq2
    |> AsyncSeq.toArraySynchronously


// n                        NEW         1.15.0
//perfTest3 1000            0.003
//perfTest3 2000
//perfTest3 3000
//perfTest3 4000
//perfTest3 5000           0.009
//perfTest3 10000           0.015
//perfTest3 100000          0.155
//perfTest3 1000000         1.500      3.480

let perfTest4 n =
    Seq.init n id
    |> AsyncSeq.ofSeq
    |> AsyncSeq.map id
    |> AsyncSeq.filter (fun x -> x % 2 = 0)
    |> AsyncSeq.toArraySynchronously

// n                        NEW         1.15.0
//perfTest4 1000
//perfTest4 2000
//perfTest4 3000
//perfTest4 4000
//perfTest4 5000
//perfTest4 10000
//perfTest4 100000          0.362       0.442
//perfTest4 1000000         3.533       4.656


[<Test>]
let ``AsyncSeq.unfoldAsync should be iterable in finite resources``() =
    let generator state =
        async {
            if state < 10000 then
                return Some ((), state + 1)
            else
                return None
        }

    AsyncSeq.unfoldAsync generator 0
    |> AsyncSeq.iter ignore
    |> Async.RunSynchronously



[<Test>]
let ``AsyncSeq.mapi should work`` () =
  for i in 0..100 do
    let ls = List.init i (fun x -> x + 100)
    let expected =
      ls
      |> List.mapi (fun i x -> sprintf "%i_%i" i x)
    let actual =
      ls
      |> AsyncSeq.ofSeq
      |> AsyncSeq.mapi (fun i x -> sprintf "%i_%i" i x)
      |> AsyncSeq.toListSynchronously
    Assert.AreEqual(expected, actual)


[<Test>]
let ``AsyncSeq.take should work``() =
  let s = asyncSeq {
    yield ["a",1] |> Map.ofList
  }
  let ss = s |> AsyncSeq.take 1
  let ls = ss |> AsyncSeq.toListSynchronously
  ()

[<Test>]
let ``AsyncSeq.truncate should work like take``() =
  let s = asyncSeq {
    yield ["a",1] |> Map.ofList
  }
  let expected = s |> AsyncSeq.take 1
  let actual =  s |> AsyncSeq.truncate 1
  Assert.AreEqual(expected, actual)


[<Test>]
let ``AsyncSeq.mapAsyncParallel should maintain order`` () =
  for i in 0..100 do
    let ls = List.init i id
    let expected =
      ls
      |> AsyncSeq.ofSeq
      |> AsyncSeq.mapAsync (async.Return)
    let actual =
      ls
      |> AsyncSeq.ofSeq
      |> AsyncSeq.mapAsyncParallel (async.Return)
    Assert.AreEqual(expected, actual)

[<Test>]
let ``AsyncSeq.mapAsyncParallel should propagate exception`` () =

  for N in [100] do

    let fail = N / 2

    let res =
      Seq.init N id
      |> AsyncSeq.ofSeq
      |> FSharp.Control.AsyncSeq.mapAsyncParallel (fun i -> async {
        if i = fail then
          return failwith  "error"
        return i })
      |> FSharp.Control.AsyncSeq.mapAsyncParallel (ignore >> async.Return)
      |> AsyncSeq.iter ignore
      |> Async.Catch
      |> runTest

    match res with
    | Choice2Of2 _ -> ()
    | Choice1Of2 _ -> Assert.Fail ("error expected")


//[<Test>]
let ``AsyncSeq.mapParallelAsync should be parallel`` () =
  let parallelism = 3
  let barrier = new Threading.Barrier(parallelism)
  let s = AsyncSeq.init (int64 parallelism) int
  let expected =
    s |> AsyncSeq.map id
  let actual =
    s
    |> AsyncSeq.mapAsyncParallel (fun i -> async { barrier.SignalAndWait () ; return i }) // can deadlock
  Assert.AreEqual(expected, actual, timeout=200)


[<Test>]
let ``AsyncSeq.iterAsyncParallel should propagate exception`` () =

  for N in [100] do

    let fail = N / 2

    let res =
      Seq.init N id
      |> AsyncSeq.ofSeq
      |> FSharp.Control.AsyncSeq.mapAsyncParallel (fun i -> async {
        if i = fail then
          return failwith  "error"
        return i })
//      |> AsyncSeq.iterAsyncParallel (fun i -> async {
//        if i = fail then
//          return failwith "error"
//        else () })
      //|> AsyncSeq.iter ignore
      |> AsyncSeq.iterAsyncParallel (async.Return >> Async.Ignore)
      |> Async.Catch
      |> runTest

    match res with
    | Choice2Of2 _ -> ()
    | Choice1Of2 _ -> Assert.Fail ("error expected")

[<Test>]
let ``AsyncSeq.iterAsyncParallel should cancel and not block forever when run in parallel with another exception-throwing Async`` () =

    let handle x = async {
           do! Async.Sleep 50
    }

    let fakeAsync = async {
           do! Async.Sleep 500
           return "fakeAsync"
    }

    let makeAsyncSeqBatch () =
           let rec loop() = asyncSeq {
               let! batch =  fakeAsync |> Async.Catch
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

    let t = [x; exAsync] |> Async.Parallel |> Async.Ignore |> Async.StartAsTask

    // should fail after 2 seconds
    Assert.Throws<AggregateException>(fun _ -> t.Wait(4000) |> ignore) |> ignore

[<Test>]
let ``AsyncSeq.iterAsyncParallelThrottled should propagate handler exception`` () =

  let res =
    AsyncSeq.init 100L id
    |> AsyncSeq.iterAsyncParallelThrottled 10 (fun i -> async { if i = 50L then return failwith "oh no" else return () })
    |> Async.Catch
    |> (fun x -> Async.RunSynchronously (x, timeout = 10000))

  match res with
  | Choice2Of2 _ -> ()
  | Choice1Of2 _ -> Assert.Fail ("error expected")

[<Test>]
let ``AsyncSeq.iterAsyncParallelThrottled should propagate sequence exception`` () =

  let res =
    asyncSeq {
      yield 1
      yield 2
      yield 3
      failwith "oh no"
    }
    |> AsyncSeq.iterAsyncParallelThrottled 10 (async.Return >> Async.Ignore)
    |> Async.Catch
    |> (fun x -> Async.RunSynchronously (x, timeout = 10000))

  match res with
  | Choice2Of2 _ -> ()
  | Choice1Of2 _ -> Assert.Fail ("error expected")


[<Test>]
let ``AsyncSeq.iterAsyncParallelThrottled should throttle`` () =

  let count = ref 0
  let parallelism = 10

  let res =
    AsyncSeq.init 100L id
    |> AsyncSeq.iterAsyncParallelThrottled parallelism (fun i -> async {
      let c = Interlocked.Increment count
      if c > parallelism then
        return failwith "oh no"
      do! Async.Sleep 10
      Interlocked.Decrement count |> ignore
      return () })
    |> Async.RunSynchronously
  ()


//[<Test>]
//let ``AsyncSeq.mapParallelAsyncBounded should maintain order`` () =
//  let ls = List.init 500 id
//  let expected =
//    ls
//    |> AsyncSeq.ofSeq
//    |> AsyncSeq.mapAsync (async.Return)
//  let actual =
//    ls
//    |> AsyncSeq.ofSeq
//    |> AsyncSeq.mapAsyncParallelBounded 10 (async.Return)
//  Assert.AreEqual(expected, actual, timeout=200)



[<Test>]
let ``AsyncSeqSrc.should work`` () =
  for n in 0..100 do
    let items = List.init n id
    let src = AsyncSeqSrc.create ()
    let actual = src |> AsyncSeqSrc.toAsyncSeq
    for item in items do
      src |> AsyncSeqSrc.put item
    src |> AsyncSeqSrc.close
    let expected = items |> AsyncSeq.ofSeq
    Assert.AreEqual (expected, actual)

[<Test>]
let ``AsyncSeqSrc.put should yield when tapped after put`` () =
  let item = 1
  let src = AsyncSeqSrc.create ()
  src |> AsyncSeqSrc.put item
  let actual = src |> AsyncSeqSrc.toAsyncSeq
  src |> AsyncSeqSrc.close
  let expected = AsyncSeq.empty
  Assert.AreEqual (expected, actual)

[<Test>]
let ``AsyncSeqSrc.fail should throw`` () =
  let item = 1
  let src = AsyncSeqSrc.create ()
  let actual = src |> AsyncSeqSrc.toAsyncSeq
  src |> AsyncSeqSrc.error (exn("test"))
  let expected = asyncSeq { raise (exn("test")) }
  Assert.AreEqual (expected, actual)


[<Test>]
let ``AsyncSeq.groupBy should work``() =
  for i in 0..100 do
    for j in 1..3 do
      let ls = List.init i id
      let p x = x % j
      let expected =
        ls
        |> Seq.groupBy p
        |> Seq.map (snd >> Seq.toList)
        |> Seq.toList
        |> AsyncSeq.ofSeq
      let actual =
        ls
        |> AsyncSeq.ofSeq
        |> AsyncSeq.groupBy p
        |> AsyncSeq.mapAsyncParallel (snd >> AsyncSeq.toListAsync)
      Assert.AreEqual(expected, actual)

[<Test>]
let ``AsyncSeq.groupBy should propagate exception and terminate all groups``() =
  let expected = asyncSeq { raise (exn("test")) }
  let actual =
    asyncSeq { raise (exn("test")) }
    |> AsyncSeq.groupBy (fun i -> i % 3)
    |> AsyncSeq.mapAsyncParallel (snd >> AsyncSeq.toListAsync)
  Assert.AreEqual(expected, actual)

[<Test>]
let ``AsyncSeq.combineLatest should behave like merge after initial``() =
  for n in 0..10 do
    for m in 0..10 do
      let ls1 = List.init n id
      let ls2 = List.init m id
      // expect each element to increase combined sum by 1
      // expected count is sum of source counts minus 1 for first result
      let expectedCount =
        if n = 0 || m = 0 then 0
        else (n + m - 1)
      let expected = List.init expectedCount id |> AsyncSeq.ofSeq
      let actual = AsyncSeq.combineLatestWith (+) (AsyncSeq.ofSeq ls1 |> randomDelayMax 2) (AsyncSeq.ofSeq ls2 |> randomDelayMax 2)
      Assert.AreEqual(expected, actual, (sprintf "n=%i m=%i" n m))

[<Test>]
let ``AsyncSeq.combineLatest should be never when either argument is never``() =
  let expected = AsyncSeq.never
  let actual1 = AsyncSeq.combineLatestWith (fun _ _ -> 0) (AsyncSeq.never) (AsyncSeq.singleton 1)
  let actual2 = AsyncSeq.combineLatestWith (fun _ _ -> 0) (AsyncSeq.singleton 1) (AsyncSeq.never)
  Assert.AreEqual(expected, actual1, timeout=100, exnEq=AreCancellationExns)
  Assert.AreEqual(expected, actual2, timeout=100, exnEq=AreCancellationExns)

[<Test>]
let ``Async.ofSeqAsync should work``() =
  let asyncSequential (s:seq<Async<'t>>) : Async<seq<'t>> =
    Seq.foldBack
      (fun asyncHead asyncQueue ->
        async.Bind(asyncHead,
                   fun head ->
                    async.Bind(asyncQueue,
                                fun queue -> async.Return(seq{ yield head; yield! queue}))))
      s
      (async.Return Seq.empty)

  for n in 0..10 do
    let s = Seq.init n (id >> async.Return)
    let actual = AsyncSeq.ofSeqAsync s
    let expected = asyncSequential s |> Async.RunSynchronously |> AsyncSeq.ofSeq
    Assert.True(EQ expected actual)

[<Test>]
let ``Async.concat should work``() =
  for n in 0..10 do
    for m in 0..10 do
      let actual =
        Seq.init m (fun _ -> Seq.init n (id >> async.Return) |> AsyncSeq.ofSeqAsync)
        |> AsyncSeq.ofSeq
        |> AsyncSeq.concat

      let expected =
        Seq.init m (fun _ -> Seq.init n id)
        |> AsyncSeq.ofSeq
        |> AsyncSeq.concatSeq

      Assert.True(EQ expected actual)

[<Test>]
let ``AsyncSeq.sort should work for``() =
  let input = [1; 3; 2; 5; 7; 4; 6] |> AsyncSeq.ofSeq
  let expected = [|1..7|]
  let actual = input |> AsyncSeq.sort
  Assert.AreEqual(expected, actual)

[<Test>]
let ``AsyncSeq.sortDescending should work``() =
  let input = [1; 3; 2; Int32.MaxValue; 4; 6; Int32.MinValue; 5; 7; 0] |> AsyncSeq.ofSeq
  let expected = seq { yield Int32.MaxValue; yield! seq{ 7..-1..0 }; yield Int32.MinValue } |> Array.ofSeq
  let actual = input |> AsyncSeq.sortDescending
  Assert.AreEqual(expected, actual)

[<Test>]
let ``AsyncSeq.sortBy should work``() =
  let fn x = Math.Abs(x-5)
  let input = [1; 2; 4; 5; 7] |> AsyncSeq.ofSeq
  let expected = [|5; 4; 7; 2; 1|]
  let actual = input |> AsyncSeq.sortBy fn
  Assert.AreEqual(expected, actual)

[<Test>]
let ``AsyncSeq.sortByDescending should work``() =
  let fn x = Math.Abs(x-5)
  let input = [1; 2; 4; 5; 6; 7;] |> AsyncSeq.ofSeq
  let expected = [|1; 2; 7; 4; 6; 5;|]
  let actual = input |> AsyncSeq.sortByDescending fn
  Assert.AreEqual(expected, actual)

[<Test>]
let ``async.For with AsyncSeq should work``() =
  async {
    let mutable results = []
    let source = asyncSeq { 
      yield 1
      yield 2
      yield 3
    }
    
    do! async {
      for item in source do
        results <- item :: results
    }
    
    Assert.AreEqual([3; 2; 1], results)
  }
  |> Async.RunSynchronously

[<Test>]
let ``async.For with empty AsyncSeq should work``() =
  async {
    let mutable count = 0
    let source = AsyncSeq.empty
    
    do! async {
      for item in source do
        count <- count + 1
    }
    
    Assert.AreEqual(0, count)
  }
  |> Async.RunSynchronously

[<Test>]
let ``async.For with exception in AsyncSeq should propagate``() =
  async {
    let source = asyncSeq {
      yield 1
      failwith "test exception"
      yield 2
    }
    
    try
      do! async {
        for item in source do
          ()
      }
      Assert.Fail("Expected exception to be thrown")
    with
    | ex when ex.Message = "test exception" -> 
      () // Expected
    | ex -> 
      Assert.Fail($"Unexpected exception: {ex.Message}")
  }
  |> Async.RunSynchronously

// ----------------------------------------------------------------------------
// Tests for previously uncovered modules to improve coverage

[<Test>]
let ``AsyncSeqExtensions - async.For with AsyncSeq`` () =
  let mutable result = []
  let computation = async {
    for item in asyncSeq { yield 1; yield 2; yield 3 } do
      result <- item :: result
  }
  computation |> Async.RunSynchronously
  Assert.AreEqual([3; 2; 1], result)

[<Test>]
let ``AsyncSeqExtensions - async.For with empty AsyncSeq`` () =
  let mutable result = []
  let computation = async {
    for item in AsyncSeq.empty do
      result <- item :: result
  }
  computation |> Async.RunSynchronously
  Assert.AreEqual([], result)

[<Test>]
let ``AsyncSeqExtensions - async.For with exception in AsyncSeq`` () =
  let mutable exceptionCaught = false
  let computation = async {
    try
      for item in asyncSeq { yield 1; failwith "test error"; yield 2 } do
        ()
    with
    | ex when ex.Message = "test error" -> 
        exceptionCaught <- true
  }
  computation |> Async.RunSynchronously
  Assert.IsTrue(exceptionCaught)

[<Test>]
let ``Seq.ofAsyncSeq should work`` () =
  let asyncSeqData = asyncSeq {
    yield 1
    yield 2
    yield 3
  }
  let seqResult = Seq.ofAsyncSeq asyncSeqData |> Seq.toList
  Assert.AreEqual([1; 2; 3], seqResult)

[<Test>]
let ``Seq.ofAsyncSeq with empty AsyncSeq`` () =
  let seqResult = Seq.ofAsyncSeq AsyncSeq.empty |> Seq.toList
  Assert.AreEqual([], seqResult)

[<Test>]
let ``Seq.ofAsyncSeq with exception`` () =
  let asyncSeqWithError = asyncSeq {
    yield 1
    failwith "test error"
    yield 2
  }
  Assert.Throws<System.Exception>(fun () -> 
    Seq.ofAsyncSeq asyncSeqWithError |> Seq.toList |> ignore
  ) |> ignore

[<Test>]
let ``AsyncSeq.intervalMs should generate sequence with timestamps``() = 
  let result = 
    AsyncSeq.intervalMs 50
    |> AsyncSeq.take 3
    |> AsyncSeq.toListAsync
    |> AsyncOps.timeoutMs 1000
    |> Async.RunSynchronously
  
  Assert.AreEqual(3, result.Length)
  // Verify timestamps are increasing
  Assert.IsTrue(result.[1] > result.[0])
  Assert.IsTrue(result.[2] > result.[1])

[<Test>]
let ``AsyncSeq.intervalMs with zero period should work``() = 
  let result = 
    AsyncSeq.intervalMs 0
    |> AsyncSeq.take 2
    |> AsyncSeq.toListAsync
    |> AsyncOps.timeoutMs 500
    |> Async.RunSynchronously
  
  Assert.AreEqual(2, result.Length)

[<Test>]
let ``AsyncSeq.take with negative count should throw ArgumentException``() = 
  Assert.Throws<System.ArgumentException>(fun () ->
    AsyncSeq.ofSeq [1;2;3]
    |> AsyncSeq.take -1
    |> AsyncSeq.toListAsync
    |> Async.RunSynchronously
    |> ignore
  ) |> ignore

[<Test>]
let ``AsyncSeq.skip with negative count should throw ArgumentException``() = 
  Assert.Throws<System.ArgumentException>(fun () ->
    AsyncSeq.ofSeq [1;2;3]
    |> AsyncSeq.skip -1
    |> AsyncSeq.toListAsync
    |> Async.RunSynchronously
    |> ignore
  ) |> ignore

[<Test>]
let ``AsyncSeq.take zero should return empty sequence``() = 
  let expected = []
  let actual = 
    AsyncSeq.ofSeq [1;2;3]
    |> AsyncSeq.take 0
    |> AsyncSeq.toListAsync
    |> Async.RunSynchronously
  
  Assert.AreEqual(expected, actual)

[<Test>]  
let ``AsyncSeq.skip zero should return original sequence``() = 
  let expected = [1;2;3]
  let actual = 
    AsyncSeq.ofSeq [1;2;3]
    |> AsyncSeq.skip 0
    |> AsyncSeq.toListAsync
    |> Async.RunSynchronously
  
  Assert.AreEqual(expected, actual)

[<Test>]
let ``AsyncSeq.replicateInfinite with exception should propagate exception``() =
  let exceptionMsg = "test exception"
  let expected = System.ArgumentException(exceptionMsg)
  
  Assert.Throws<System.ArgumentException>(fun () ->
    AsyncSeq.replicateInfinite (raise expected)
    |> AsyncSeq.take 2
    |> AsyncSeq.toListAsync
    |> Async.RunSynchronously
    |> ignore
  ) |> ignore

#if (NETSTANDARD2_1 || NETCOREAPP3_0)
[<Test>]
let ``AsyncSeq.ofAsyncEnum should roundtrip successfully``() =
  let data = [ 1 .. 10 ] |> AsyncSeq.ofSeq
  let actual = data |> AsyncSeq.toAsyncEnum |> AsyncSeq.ofAsyncEnum
  Assert.True(EQ data actual)

[<Test>]
let ``AsyncSeq.toAsyncEnum raises exception``() : unit =
  async {
    let exceptionMessage = "Raised inside AsyncSeq"
    let exceptionSequence =
      asyncSeq { yield failwith exceptionMessage; yield 1 }
      |> AsyncSeq.toAsyncEnum
    let mutable exceptionRaised = false
    try
      let enumerator = exceptionSequence.GetAsyncEnumerator()
      let! item = enumerator.MoveNextAsync().AsTask() |> Async.AwaitTask
      enumerator.Current |> ignore
    with
    | ex ->
      printfn "Exception message"
      if exceptionMessage <> ex.Message
      then Assert.Fail("Message")
      else exceptionRaised <- true
    Assert.IsTrue(exceptionRaised)
  }
  |> Async.RunSynchronously

let ``AsyncSeq.ofAsyncEnum raises exception``() : unit =
  async {
    let exceptionMessage = "Raised inside AsyncSeq"
    let exceptionSequence =
      asyncSeq { yield failwith exceptionMessage; yield 1 }
      |> AsyncSeq.toAsyncEnum
      |> AsyncSeq.ofAsyncEnum
    let mutable exceptionRaised = false
    try
      let enumerator = exceptionSequence.GetEnumerator()
      let! __ = enumerator.MoveNext()
      return Assert.Fail()
    with
    | ex ->
      if exceptionMessage <> ex.Message
      then Assert.Fail()
      else exceptionRaised <- true
    Assert.IsTrue(exceptionRaised)
  }
  |> Async.RunSynchronously

[<Test>]
let ``AsyncSeq.ofAsyncEnum can be cancelled``() : unit =
  use cts = new CancellationTokenSource()
  let mutable cancelledInvoked = false
  let mutable results = ResizeArray<_>()

  let sourceWorkflow =
    asyncSeq {
      use! __ = Async.OnCancel(fun x -> cancelledInvoked <- true)
      yield 1
      yield 2
    } |> AsyncSeq.toAsyncEnum |> AsyncSeq.ofAsyncEnum

  let innerAsync =
    async {
      let enumerator = sourceWorkflow.GetEnumerator()
      let! resultOpt = enumerator.MoveNext()
      results.Add(resultOpt)
      cts.Cancel()
      try
        let! __ = enumerator.MoveNext()
        Assert.Fail("Task should have been cancelled")
      with
      | :? TaskCanceledException -> ()
      | _ -> return Assert.Fail()
    }

  try
    Async.RunSynchronously(innerAsync, cancellationToken = cts.Token)
    Assert.Fail()
  with
  | :? OperationCanceledException ->
    Assert.IsTrue(cancelledInvoked)
    Assert.IsTrue([ Some 1 ] = (results |> Seq.toList))
  | _ -> Assert.Fail()

[<Test>]
let ``AsyncSeq.toAsyncEnum can be cancelled``() : unit =
  async {
    use cts = new CancellationTokenSource()
    let mutable cancelledInvoked = false

    let sourceWorkflow =
      asyncSeq {
        use! __ = Async.OnCancel(fun x -> cancelledInvoked <- true)
        yield 1
        yield 2
      }
      |> AsyncSeq.toAsyncEnum

    let enumerator = sourceWorkflow.GetAsyncEnumerator(cts.Token)
    let! result1 = enumerator.MoveNextAsync().AsTask() |> Async.AwaitTask
    Assert.IsTrue(result1)
    Assert.IsTrue(enumerator.Current = 1)
    cts.Cancel()
    try
      let! _ = enumerator.MoveNextAsync().AsTask() |> Async.AwaitTask
      Assert.Fail()
    with
    | :? Tasks.TaskCanceledException -> return ()
    | _ -> Assert.Fail()
    Assert.IsTrue(cancelledInvoked)
  }
  |> Async.RunSynchronously

[<Test>]
let ``Seq.ofAsyncSeq should work``() =
  let source = asyncSeq {
    yield 1
    yield 2
    yield 3
  }
  
  let result = Seq.ofAsyncSeq source |> Seq.toList
  Assert.AreEqual([1; 2; 3], result)

[<Test>]
let ``Seq.ofAsyncSeq with empty AsyncSeq should work``() =
  let source = AsyncSeq.empty
  let result = Seq.ofAsyncSeq source |> Seq.toList
  Assert.AreEqual([], result)

[<Test>]
let ``Seq.ofAsyncSeq with exception should propagate``() =
  let source = asyncSeq {
    yield 1
    failwith "test exception"
    yield 2
  }
  
  try
    let _ = Seq.ofAsyncSeq source |> Seq.toList
    Assert.Fail("Expected exception to be thrown")
  with
  | ex when ex.Message = "test exception" -> 
    () // Expected
  | ex -> 
    Assert.Fail($"Unexpected exception: {ex.Message}")

#endif

[<Test>]
let ``AsyncSeq.fold with empty sequence should return seed``() =
  let result = AsyncSeq.empty 
               |> AsyncSeq.fold (+) 10 
               |> Async.RunSynchronously
  Assert.AreEqual(10, result)

[<Test>]
let ``AsyncSeq.ofSeq should work with large sequence``() =
  let largeSeq = seq { 1 .. 1000 }
  let asyncSeq = AsyncSeq.ofSeq largeSeq
  let result = asyncSeq |> AsyncSeq.toListAsync |> Async.RunSynchronously
  Assert.AreEqual(1000, result.Length)
  Assert.AreEqual(1, result.[0])
  Assert.AreEqual(1000, result.[999])

[<Test>]
let ``AsyncSeq.mapAsync should preserve order with async transformations``() =
  let data = [1; 2; 3; 4; 5] |> AsyncSeq.ofSeq
  let asyncTransform x = async {
    do! Async.Sleep(50 - x * 10) // Shorter sleep for larger numbers
    return x * 2
  }
  
  let result = data 
               |> AsyncSeq.mapAsync asyncTransform
               |> AsyncSeq.toListAsync 
               |> Async.RunSynchronously
  Assert.AreEqual([2; 4; 6; 8; 10], result)

[<Test>]
let ``AsyncSeq.mapAsync should propagate exceptions``() =
  let data = [1; 2; 3] |> AsyncSeq.ofSeq  
  let asyncTransform x = async {
    if x = 2 then failwith "test error"
    return x * 2
  }
  
  try
    data 
    |> AsyncSeq.mapAsync asyncTransform
    |> AsyncSeq.toListAsync 
    |> Async.RunSynchronously
    |> ignore
    Assert.Fail("Expected exception to be thrown")
  with
  | ex when ex.Message = "test error" -> () // Expected
  | ex -> Assert.Fail($"Unexpected exception: {ex.Message}")

[<Test>]
let ``AsyncSeq.chooseAsync should filter and transform``() =
  let data = [1; 2; 3; 4; 5] |> AsyncSeq.ofSeq
  let asyncChoose x = async {
    if x % 2 = 0 then return Some (x * 10)
    else return None
  }
  
  let result = data 
               |> AsyncSeq.chooseAsync asyncChoose
               |> AsyncSeq.toListAsync 
               |> Async.RunSynchronously
  Assert.AreEqual([20; 40], result)

[<Test>]
let ``AsyncSeq.filterAsync should work with async predicates``() =
  let data = [1; 2; 3; 4; 5] |> AsyncSeq.ofSeq
  let asyncPredicate x = async {
    do! Async.Sleep(1)
    return x % 2 = 1
  }
  
  let result = data 
               |> AsyncSeq.filterAsync asyncPredicate
               |> AsyncSeq.toListAsync 
               |> Async.RunSynchronously
  Assert.AreEqual([1; 3; 5], result)

[<Test>]
let ``AsyncSeq.scan should work with accumulator``() =
  let data = [1; 2; 3; 4] |> AsyncSeq.ofSeq
  let result = data 
               |> AsyncSeq.scan (+) 0
               |> AsyncSeq.toListAsync 
               |> Async.RunSynchronously
  Assert.AreEqual([0; 1; 3; 6; 10], result)

[<Test>]
let ``AsyncSeq.scanAsync should work with async accumulator``() =
  let data = [1; 2; 3] |> AsyncSeq.ofSeq
  let asyncFolder acc x = async {
    do! Async.Sleep(1)
    return acc + x
  }
  let result = data 
               |> AsyncSeq.scanAsync asyncFolder 0
               |> AsyncSeq.toListAsync 
               |> Async.RunSynchronously
  Assert.AreEqual([0; 1; 3; 6], result)

[<Test>]
let ``AsyncSeq.threadStateAsync should maintain state correctly``() =
  let data = [1; 2; 3; 4] |> AsyncSeq.ofSeq
  let statefulFolder state x = async {
    let newState = state + 1
    let output = x * newState
    return (output, newState)
  }
  
  let result = data 
               |> AsyncSeq.threadStateAsync statefulFolder 0
               |> AsyncSeq.toListAsync 
               |> Async.RunSynchronously
  Assert.AreEqual([1; 4; 9; 16], result)

[<Test>]
let ``AsyncSeq.lastOrDefault should return default for empty sequence``() =
  let result = AsyncSeq.empty 
               |> AsyncSeq.lastOrDefault 999
               |> Async.RunSynchronously
  Assert.AreEqual(999, result)

[<Test>]
let ``AsyncSeq.lastOrDefault should return last element``() =
  let data = [1; 2; 3; 4; 5] |> AsyncSeq.ofSeq
  let result = data 
               |> AsyncSeq.lastOrDefault 999
               |> Async.RunSynchronously
  Assert.AreEqual(5, result)

// ----------------------------------------------------------------------------
// Additional Coverage Tests targeting uncovered edge cases and branches

[<Test>]
let ``AsyncSeq.bufferByCount with size 1 should work`` () =
  let source = asyncSeq { yield 1; yield 2; yield 3 }
  let result = AsyncSeq.bufferByCount 1 source |> AsyncSeq.toListSynchronously
  Assert.AreEqual([[|1|]; [|2|]; [|3|]], result)

[<Test>]
let ``AsyncSeq.bufferByCount with empty sequence should return empty`` () =
  let result = AsyncSeq.bufferByCount 2 AsyncSeq.empty |> AsyncSeq.toListSynchronously
  Assert.AreEqual([], result)

[<Test>]
let ``AsyncSeq.bufferByCount with size larger than sequence should return partial`` () =
  let source = asyncSeq { yield 1; yield 2 }
  let result = AsyncSeq.bufferByCount 5 source |> AsyncSeq.toListSynchronously
  Assert.AreEqual([[|1; 2|]], result)

[<Test>]
let ``AsyncSeq.pairwise with empty sequence should return empty`` () =
  let result = AsyncSeq.pairwise AsyncSeq.empty |> AsyncSeq.toListSynchronously
  Assert.AreEqual([], result)

[<Test>]
let ``AsyncSeq.pairwise with single element should return empty`` () =
  let source = asyncSeq { yield 42 }
  let result = AsyncSeq.pairwise source |> AsyncSeq.toListSynchronously
  Assert.AreEqual([], result)

[<Test>]
let ``AsyncSeq.pairwise with three elements should produce two pairs`` () =
  let source = asyncSeq { yield 1; yield 2; yield 3 }
  let result = AsyncSeq.pairwise source |> AsyncSeq.toListSynchronously
  Assert.AreEqual([(1, 2); (2, 3)], result)

[<Test>]
let ``AsyncSeq.distinctUntilChangedWith should work with custom equality`` () =
  let source = asyncSeq { yield "a"; yield "A"; yield "B"; yield "b"; yield "c" }
  let customEq (x: string) (y: string) = x.ToLower() = y.ToLower()
  let result = AsyncSeq.distinctUntilChangedWith customEq source |> AsyncSeq.toListSynchronously
  Assert.AreEqual(["a"; "B"; "c"], result)

[<Test>]
let ``AsyncSeq.distinctUntilChangedWith with all same elements should return single`` () =
  let source = asyncSeq { yield 1; yield 1; yield 1 }
  let result = AsyncSeq.distinctUntilChangedWith (=) source |> AsyncSeq.toListSynchronously
  Assert.AreEqual([1], result)

[<Test>]
let ``AsyncSeq.append with both sequences having exceptions should propagate first`` () =
  async {
    let seq1 = asyncSeq { yield 1; failwith "error1" }
    let seq2 = asyncSeq { yield 2; failwith "error2" }
    let combined = AsyncSeq.append seq1 seq2
    
    try
      let! _ = AsyncSeq.toListAsync combined
      Assert.Fail("Expected exception to be thrown")
    with
    | ex when ex.Message = "error1" -> 
      () // Expected - first sequence's error should be thrown
    | ex -> 
      Assert.Fail($"Unexpected exception: {ex.Message}")
  } |> Async.RunSynchronously

[<Test>]
let ``AsyncSeq.concat with nested exceptions should propagate properly`` () =
  async {
    let nested = asyncSeq {
      yield asyncSeq { yield 1; yield 2 }
      yield asyncSeq { failwith "nested error" }
      yield asyncSeq { yield 3 }
    }
    let flattened = AsyncSeq.concat nested
    
    try
      let! result = AsyncSeq.toListAsync flattened
      Assert.Fail("Expected exception to be thrown")
    with
    | ex when ex.Message = "nested error" -> 
      () // Expected
    | ex -> 
      Assert.Fail($"Unexpected exception: {ex.Message}")
  } |> Async.RunSynchronously

[<Test>]
let ``AsyncSeq.choose with all None should return empty`` () =
  let source = asyncSeq { yield 1; yield 2; yield 3 }
  let result = AsyncSeq.choose (fun _ -> None) source |> AsyncSeq.toListSynchronously
  Assert.AreEqual([], result)

[<Test>]
let ``AsyncSeq.choose with mixed Some and None should filter correctly`` () =
  let source = asyncSeq { yield 1; yield 2; yield 3; yield 4 }
  let chooser x = if x % 2 = 0 then Some (x * 2) else None
  let result = AsyncSeq.choose chooser source |> AsyncSeq.toListSynchronously
  Assert.AreEqual([4; 8], result)

[<Test>]
let ``AsyncSeq.chooseAsync with async transformation should work`` () =
  async {
    let source = asyncSeq { yield 1; yield 2; yield 3; yield 4 }
    let chooserAsync x = async {
      if x % 2 = 0 then return Some (x * 3) else return None
    }
    let! result = AsyncSeq.chooseAsync chooserAsync source |> AsyncSeq.toListAsync
    Assert.AreEqual([6; 12], result)
  } |> Async.RunSynchronously


