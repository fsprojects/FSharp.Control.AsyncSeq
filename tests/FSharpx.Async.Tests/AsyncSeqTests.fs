module AsyncSeqTests

open NUnit.Framework
open FSharpx.Control

[<Test>]
let ``skipping should return all elements after the first non-match``() =
    let expected = [ 3; 4 ]
    let result = 
        [ 1; 2; 3; 4 ] 
        |> AsyncSeq.ofSeq 
        |> AsyncSeq.skipWhile (fun i -> i <= 2) 
        |> AsyncSeq.toBlockingSeq 
        |> Seq.toList
    Assert.AreEqual(expected, result)


[<Test>]
let ``AsyncSeq.toArray``() =
  
  let s = asyncSeq {
    yield 1
    yield 2
    yield 3
  }

  let a = s |> AsyncSeq.toArray |> Async.RunSynchronously |> Array.toList

  Assert.True(([1;2;3] = a))


[<Test>]
let ``AsyncSeq.toList``() =
  
  let s = asyncSeq {
    yield 1
    yield 2
    yield 3
  }

  let a = s |> AsyncSeq.toList |> Async.RunSynchronously

  Assert.True(([1;2;3] = a))


[<Test>]
let ``AsyncSeq.concatSeq``() =
  
  let s = asyncSeq {
    yield [1;2]
    yield [3;4]
  }
  
  let s = 
    s
    |> AsyncSeq.concatSeq
    |> AsyncSeq.toList
    |> Async.RunSynchronously

  Assert.True(([1;2;3;4] = s))



[<Test>]
let ``AsyncSeq.unfoldAsync``() =
  
  let gen s = 
    if s < 3 then (s,s + 1) |> Some |> async.Return
    else None |> async.Return
  
  let s = 
    AsyncSeq.unfoldAsync gen 0 
    |> AsyncSeq.toList 
    |> Async.RunSynchronously

  Assert.True(([0;1;2] = s))



[<Test>]
let ``AsyncSeq.interleave``() =
  let s1 = AsyncSeq.ofSeq ["a";"b";"c"]
  let s2 = AsyncSeq.ofSeq [1;2;3]
  let merged = AsyncSeq.interleave s1 s2 |> AsyncSeq.toList |> Async.RunSynchronously
  printfn "%A" merged
  Assert.True([Choice1Of2 "a" ; Choice2Of2 1 ; Choice1Of2 "b" ; Choice2Of2 2 ; Choice1Of2 "c" ; Choice2Of2 3] = merged)


[<Test>]
let ``AsyncSeq.bufferByCount``() =
  
  let s = asyncSeq {
    yield 1
    yield 2
    yield 3
    yield 4
  }

  let s' = s |> AsyncSeq.bufferByCount 2 |> AsyncSeq.toList |> Async.RunSynchronously

  Assert.True(([[|1;2|];[|3;4|]] = s'))