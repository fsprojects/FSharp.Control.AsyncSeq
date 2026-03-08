(**
---
title: Transforming and Reducing Sequences
category: Documentation
categoryindex: 2
index: 3
description: How to transform, filter and reduce F# asynchronous sequences using map, filter, reduce, fold and sum operations.
keywords: F#, asynchronous sequences, AsyncSeq, map, filter, reduce, fold, sum, mapFoldAsync
---
*)
(*** condition: prepare ***)
#nowarn "211"
#I "../src/FSharp.Control.AsyncSeq/bin/Release/netstandard2.1"
#r "FSharp.Control.AsyncSeq.dll"
(*** condition: fsx ***)
#if FSX
#r "nuget: FSharp.Control.AsyncSeq,{{fsdocs-package-version}}"
#endif // FSX
(*** condition: ipynb ***)
#if IPYNB
#r "nuget: FSharp.Control.AsyncSeq,{{fsdocs-package-version}}"
#endif // IPYNB

(**
[![Binder](https://mybinder.org/badge_logo.svg)](https://mybinder.org/v2/gh/fsprojects/FSharp.Control.AsyncSeq/gh-pages?filepath=AsyncSeqTransforming.ipynb)

# Transforming and Reducing Sequences

This document covers some of the core operations for transforming and aggregating `AsyncSeq<'T>` values:
`map`, `mapAsync`, `filter`, `filterAsync`, `reduceAsync`, `mapFoldAsync`, `sumBy` and `sumByAsync`.

*)

open FSharp.Control

(**

## Transforming Sequences

### Using computation expressions

The most general and simplest way to transform asynchronous sequences is to write a function that accepts an `AsyncSeq<_>` and returns an `AsyncSeq<_>` and is implemented using an `asyncSeq { ... }` computation expression. For example, the following function transforms a sequence of integers into a sequence of strings that labels each integer as even or odd:

*)

let transform (input: AsyncSeq<int>) : AsyncSeq<string> =
    asyncSeq {
        for n in input do
            if n % 2 = 0 then
                do! Async.Sleep 100 // simulate some async work
                yield sprintf "Even: %d" n
            else
                yield sprintf "Odd: %d" n
    }

(**

Here the `for` loop is an asynchronous loop that iterates over the input sequence, awaiting each element. On even numbers, it simulates some asynchronous work before yielding a result. On odd numbers, it yields immediately.

Inside `asyncSeq { ... }`, you can use any F# constructs such as loops, conditionals. You can also use `let!` or `do!` to await individual `Async<_>` values:

*)

let transformWithAsync (input: AsyncSeq<int>) : AsyncSeq<string> =
    asyncSeq {
        for n in input do
            let! isEven = async { return n % 2 = 0 } // simulate async check
            if isEven then
                yield sprintf "Even: %d" n
            else
                yield sprintf "Odd: %d" n
    }

(**

### map and mapAsync

Instead of writing a full computation expression, you can use `AsyncSeq.map` to transform each element of a sequence synchronously:

*)

let strings = asyncSeq { yield! [ "hello"; "world"; "asyncseq" ] }

let upperCased : AsyncSeq<string> =
    strings |> AsyncSeq.map (fun s -> s.ToUpperInvariant())

(**

`AsyncSeq.mapAsync` is the same but the projection returns `Async<'U>`, so it can perform asynchronous work per element — for example, fetching metadata for each item:

*)

let fetchLength (url: string) : Async<int> =
    async { return url.Length } // placeholder for a real HTTP call

let lengths : AsyncSeq<int> =
    strings |> AsyncSeq.mapAsync fetchLength

(**

### filter and filterAsync

`AsyncSeq.filter` keeps only elements satisfying a synchronous predicate:

*)

let longStrings : AsyncSeq<string> =
    strings |> AsyncSeq.filter (fun s -> s.Length > 4)

(**

`AsyncSeq.filterAsync` does the same with an asynchronous predicate — useful when the
keep/discard decision requires an async lookup:

*)

let isInteresting (s: string) : Async<bool> =
    async { return s.Contains('o') } // placeholder for a real async check

let interesting : AsyncSeq<string> =
    strings |> AsyncSeq.filterAsync isInteresting

(**

### reduceAsync

`AsyncSeq.reduceAsync` reduces a sequence to a single value using an asynchronous binary
operation. It raises `InvalidOperationException` on an empty sequence (use `foldAsync` with an
explicit initial state if you need to handle that case):

*)

let words = asyncSeq { yield! [ "F#"; "is"; "great" ] }

let sentence : Async<string> =
    words |> AsyncSeq.reduceAsync (fun acc w -> async { return acc + " " + w })

(**

### mapFoldAsync

`AsyncSeq.mapFoldAsync` combines a map and a fold in a single pass. The folder function receives
the current accumulator state and an element, and returns an `Async` of a *(result, newState)* pair.
The call returns the array of mapped results together with the final state:

*)

// Number each element with a running index, and count total characters as state.
let numberAndCount : Async<string array * int> =
    words
    |> AsyncSeq.mapFoldAsync
        (fun totalChars word -> async {
            let numbered = sprintf "%d: %s" totalChars word
            return numbered, totalChars + word.Length })
        0

(**

---

## Aggregating Sequences

### sumBy and sumByAsync

`AsyncSeq.sumBy` projects each element to a numeric value and sums the results. It returns an
`Async` because consuming the sequence is asynchronous, even though the projection itself is
synchronous:

*)

let numbers = asyncSeq { yield! [ 1 .. 10 ] }

let sumOfSquares : Async<int> =
    numbers |> AsyncSeq.sumBy (fun n -> n * n)

(**

`AsyncSeq.sumByAsync` is the same when the projection needs to perform async work:

*)

let fetchScore (n: int) : Async<float> =
    async { return float n * 0.5 } // placeholder

let totalScore : Async<float> =
    numbers |> AsyncSeq.sumByAsync fetchScore

(**
Other aggregation functions such as `averageBy` and `averageByAsync` are also available and work similarly.
*)