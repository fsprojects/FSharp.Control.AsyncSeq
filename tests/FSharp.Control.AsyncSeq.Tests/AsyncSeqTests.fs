#if INTERACTIVE
#if COMPARE_TO_OLD
#r @"../../packages/FSharp.Control.AsyncSeq/lib/net40/Fsharp.Control.AsyncSeq.dll"
#else
#r @"../../bin/FSharp.Control.AsyncSeq.dll"
#endif
#r @"../../packages/NUnit/lib/nunit.framework.dll"
#time "on"
#else
module AsyncSeqTests
#endif

open NUnit.Framework
open FSharp.Control
open System

module AsyncOps = 
    let unit = async.Return()    
    let never = Async.Sleep(-1)    
    let timeoutMs (timeoutMs:int) (a:Async<'a>) = async {
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
  let exp = a |> AsyncSeq.toList 
  let act = b |> AsyncSeq.toList 
  if (exp = act) then true
  else
    printfn "expected=%A" exp
    printfn "actual=%A" act
    false

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
let ``AsyncSeq.toArray``() =  
  let ls = [1;2;3]
  let s = asyncSeq {
    yield 1
    yield 2
    yield 3
  }
  let a = s |> AsyncSeq.toList 
  Assert.True(([1;2;3] = a))


[<Test>]
let ``AsyncSeq.toList``() =  
  let s = asyncSeq {
    yield 1
    yield 2
    yield 3
  }
  let a = s |> AsyncSeq.toList 
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
      |> AsyncSeq.toList
      |> List.unzip
    shouldEqual expected actual1 (Some "cached sequence1 was different")
    shouldEqual expected actual2 (Some "cached sequence2 was different")
    shouldEqual expected.Length !effects (Some "iterating cached sequence resulted in multiple iterations of source")

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
  let merged = AsyncSeq.interleaveChoice s1 s2 |> AsyncSeq.toList 
  Assert.True([Choice1Of2 "a" ; Choice2Of2 1 ; Choice1Of2 "b" ; Choice2Of2 2 ; Choice1Of2 "c" ; Choice2Of2 3] = merged)

[<Test>]
let ``AsyncSeq.interleaveChoice second smaller``() =  
  let s1 = AsyncSeq.ofSeq ["a";"b";"c"]
  let s2 = AsyncSeq.ofSeq [1]
  let merged = AsyncSeq.interleaveChoice s1 s2 |> AsyncSeq.toList 
  Assert.True([Choice1Of2 "a" ; Choice2Of2 1 ; Choice1Of2 "b" ; Choice1Of2 "c" ] = merged)

[<Test>]
let ``AsyncSeq.interleaveChoice second empty``() =  
  let s1 = AsyncSeq.ofSeq ["a";"b";"c"]
  let s2 = AsyncSeq.ofSeq []
  let merged = AsyncSeq.interleaveChoice s1 s2 |> AsyncSeq.toList 
  Assert.True([Choice1Of2 "a" ; Choice1Of2 "b" ; Choice1Of2 "c" ] = merged)

[<Test>]
let ``AsyncSeq.interleaveChoice both empty``() =  
  let s1 = AsyncSeq.ofSeq<int> []
  let s2 = AsyncSeq.ofSeq<int> []
  let merged = AsyncSeq.interleaveChoice s1 s2 |> AsyncSeq.toList 
  Assert.True([ ] = merged)

[<Test>]
let ``AsyncSeq.interleaveChoice first smaller``() =  
  let s1 = AsyncSeq.ofSeq ["a"]
  let s2 = AsyncSeq.ofSeq [1;2;3]
  let merged = AsyncSeq.interleaveChoice s1 s2 |> AsyncSeq.toList 
  Assert.True([Choice1Of2 "a" ; Choice2Of2 1 ; Choice2Of2 2 ; Choice2Of2 3] = merged)



[<Test>]
let ``AsyncSeq.interleaveChoice first empty``() =  
  let s1 = AsyncSeq.ofSeq []
  let s2 = AsyncSeq.ofSeq [1;2;3]
  let merged = AsyncSeq.interleaveChoice s1 s2 |> AsyncSeq.toList 
  Assert.True([Choice2Of2 1 ; Choice2Of2 2 ; Choice2Of2 3] = merged)


[<Test>]
let ``AsyncSeq.interleave``() =  
  let s1 = AsyncSeq.ofSeq ["a";"b";"c"]
  let s2 = AsyncSeq.ofSeq ["1";"2";"3"]
  let merged = AsyncSeq.interleave s1 s2 |> AsyncSeq.toList 
  Assert.True(["a" ; "1" ; "b" ; "2" ; "c" ; "3"] = merged)

[<Test>]
let ``AsyncSeq.interleave second smaller``() =  
  let s1 = AsyncSeq.ofSeq ["a";"b";"c"]
  let s2 = AsyncSeq.ofSeq ["1"]
  let merged = AsyncSeq.interleave s1 s2 |> AsyncSeq.toList 
  Assert.True(["a" ; "1" ; "b" ; "c" ] = merged)

[<Test>]
let ``AsyncSeq.interleave second empty``() =  
  let s1 = AsyncSeq.ofSeq ["a";"b";"c"]
  let s2 = AsyncSeq.ofSeq []
  let merged = AsyncSeq.interleave s1 s2 |> AsyncSeq.toList 
  Assert.True(["a" ; "b" ; "c" ] = merged)

[<Test>]
let ``AsyncSeq.interleave both empty``() =  
  let s1 = AsyncSeq.ofSeq<int> []
  let s2 = AsyncSeq.ofSeq<int> []
  let merged = AsyncSeq.interleave s1 s2 |> AsyncSeq.toList 
  Assert.True([ ] = merged)

[<Test>]
let ``AsyncSeq.interleave first smaller``() =  
  let s1 = AsyncSeq.ofSeq ["a"]
  let s2 = AsyncSeq.ofSeq ["1";"2";"3"]
  let merged = AsyncSeq.interleave s1 s2 |> AsyncSeq.toList 
  Assert.True(["a" ; "1" ; "2" ; "3"] = merged)



[<Test>]
let ``AsyncSeq.interleave first empty``() =  
  let s1 = AsyncSeq.ofSeq []
  let s2 = AsyncSeq.ofSeq [1;2;3]
  let merged = AsyncSeq.interleave s1 s2 |> AsyncSeq.toList 
  Assert.True([1 ; 2 ; 3] = merged)


[<Test>]
let ``AsyncSeq.bufferByCount``() =
  let s = asyncSeq {
    yield 1
    yield 2
    yield 3
    yield 4
    yield 5
  }
  let s' = s |> AsyncSeq.bufferByCount 2 |> AsyncSeq.toList 
  Assert.True(([[|1;2|];[|3;4|];[|5|]] = s'))

[<Test>]
let ``AsyncSeq.bufferByCount various sizes``() =
  for sz in 0 .. 10 do
      let s = asyncSeq {
        for i in 1 .. sz do
           yield i
      }
      let s' = s |> AsyncSeq.bufferByCount 1 |> AsyncSeq.toList 
      Assert.True(([for i in 1 .. sz -> [|i|]] = s'))

[<Test>]
let ``AsyncSeq.bufferByCount empty``() =
  let s = AsyncSeq.empty<int>
  let s' = s |> AsyncSeq.bufferByCount 2 |> AsyncSeq.toList 
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
  let actual = AsyncSeq.bufferByCountAndTime 2 50 s |> AsyncSeq.toList
  Assert.True((actual = [ [|1;2|] ; [|3|] ; [|4;5|] ]))

[<Test>]
let ``AsyncSeq.bufferByCountAndTime various sizes``() =
  for sz in 0 .. 10 do
      let s = asyncSeq {
        for i in 1 .. sz do
           yield i
      }
      let s' = s |> AsyncSeq.bufferByCountAndTime 1 1 |> AsyncSeq.toList 
      Assert.True(([for i in 1 .. sz -> [|i|]] = s'))

[<Test>]
let ``AsyncSeq.bufferByTimeAndCount empty``() =      
  let s = AsyncSeq.empty<int>
  let actual = AsyncSeq.bufferByCountAndTime 2 10 s |> AsyncSeq.toList
  Assert.True((actual = []))


[<Test>]
let ``try finally works no exception``() =
  let x = ref 0
  let s = asyncSeq {
    try yield 1 
    finally x := x.Value + 3
  }
  Assert.True(x.Value = 0)
  let s1 = s |> AsyncSeq.toList 
  Assert.True(x.Value = 3)
  let s2 = s |> AsyncSeq.toList
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
  let s1 = try s |> AsyncSeq.toList with _ -> []
  Assert.True((s1 = []))
  Assert.True(x.Value = 3)
  let s2 = try s |> AsyncSeq.toList with _ -> []
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
  let s1 = try s |> AsyncSeq.toList with _ -> []
  Assert.True((s1 = []))
  Assert.True(x.Value = 3)
  let s2 = try s |> AsyncSeq.toList with _ -> []
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
  let s1 = try s |> AsyncSeq.toList with _ -> []
  Assert.True((s1 = [1]))
  Assert.True(x.Value = 0)
  let s2 = try s |> AsyncSeq.toList with _ -> []
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
  let actual = AsyncSeq.merge (AsyncSeq.ofSeq ls1) (AsyncSeq.ofSeq ls2) |> AsyncSeq.toList |> Set.ofList
  let expected = ls1 @ ls2 |> Set.ofList
  Assert.True((expected = actual))

[<Test>]
let ``AsyncSeq.mergeChoice``() =
  let ls1 = [1;2;3;4;5]
  let ls2 = [6.;7.;8.;9.;10.]
  let actual = AsyncSeq.mergeChoice (AsyncSeq.ofSeq ls1) (AsyncSeq.ofSeq ls2) |> AsyncSeq.toList |> Set.ofList
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
  Assert.True(src |> AsyncSeq.ofObservableBuffered |> AsyncSeq.toList = [])
  Assert.True(discarded())

[<Test>]
let ``AsyncSeq.ofObservableBuffered should work (singleton)``() =  
  let src, discarded = observe [1] false
  Assert.True(src |> AsyncSeq.ofObservableBuffered |> AsyncSeq.toList = [1])
  Assert.True(discarded())

[<Test>]
let ``AsyncSeq.ofObservableBuffered should work (ten)``() =  
  let src, discarded = observe [1..10] false
  Assert.True(src |> AsyncSeq.ofObservableBuffered |> AsyncSeq.toList = [1..10])
  Assert.True(discarded())

[<Test>]
let ``AsyncSeq.ofObservableBuffered should work (empty, fail)``() =  
  let src, discarded = observe [] true
  Assert.True(try (src |> AsyncSeq.ofObservableBuffered |> AsyncSeq.toList |> ignore); false with _ -> true)
  Assert.True(discarded())

[<Test>]
let ``AsyncSeq.ofObservableBuffered should work (one, fail)``() =  
  let src, discarded = observe [1] true
  Assert.True(try (src |> AsyncSeq.ofObservableBuffered |> AsyncSeq.toList |> ignore); false with _ -> true)
  Assert.True(discarded())

[<Test>]
let ``AsyncSeq.ofObservableBuffered should work (one, take)``() =  
  let src, discarded = observe [1] true
  Assert.True(src |> AsyncSeq.ofObservableBuffered |> AsyncSeq.take 1 |> AsyncSeq.toList = [1])
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
            |> AsyncSeq.toList
            |> List.sort
        Assert.True((actual = expected), sprintf "mergeAll test at n = %d" n)


[<Test>]
let ``Async.mergeAll should perform well``() =
    let mergeTest n = 
        [ for i in 1 .. n -> 
            asyncSeq{ do! Async.Sleep 1000; 
                      yield i } ] 
        |> AsyncSeq.mergeAll
        |> AsyncSeq.toList

    Assert.DoesNotThrow(fun _ -> mergeTest 1000 |> ignore)



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
          |> AsyncSeq.toList
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
          |> AsyncSeq.toList
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
          |> AsyncSeq.toList
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
          |> AsyncSeq.toList
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
    |> AsyncSeq.toArray

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
    |> AsyncSeq.toArray


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
    |> AsyncSeq.toArray

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
let ``AsyncSeq.take should work``() =  
  let s = asyncSeq {
    yield ["a",1] |> Map.ofList
  }
  let ss = s |> AsyncSeq.take 1
  let ls = ss |> AsyncSeq.toList
  ()

[<Test>]
let ``AsyncSeq.mapParallelAsync should maintain order`` () =
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