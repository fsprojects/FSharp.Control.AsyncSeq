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
