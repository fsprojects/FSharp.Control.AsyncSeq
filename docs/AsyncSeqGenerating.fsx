(**
---
title: Generating Asynchronous Sequences
category: Documentation
categoryindex: 2
index: 2
description: How to generate F# asynchronous sequences using computation expressions and AsyncSeq.unfoldAsync.
keywords: F#, asynchronous sequences, AsyncSeq, computation expressions, unfoldAsync, asyncSeq
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
[![Binder](https://mybinder.org/badge_logo.svg)](https://mybinder.org/v2/gh/fsprojects/FSharp.Control.AsyncSeq/gh-pages?filepath=AsyncSeqGenerating.ipynb)

# Generating Asynchronous Sequences

This document covers the two main ways to create `AsyncSeq<'T>` values: the `asyncSeq`
computation expression and the `AsyncSeq.unfoldAsync` factory function.

*)

open System
open FSharp.Control

(**

## Computation Expression Syntax

`asyncSeq { ... }` is a computation expression that lets you write asynchronous sequences
using familiar F# constructs. The following sections show each supported form.

### Conditionals

Use `if`/`then`/`else` to emit elements conditionally:

*)

let evenNumbers = asyncSeq {
    for i in 1 .. 10 do
        if i % 2 = 0 then
            yield i
}

(**

You can also yield different values in each branch:

*)

let labelledNumbers = asyncSeq {
    for i in 1 .. 5 do
        if i % 2 = 0 then
            yield sprintf "%d is even" i
        else
            yield sprintf "%d is odd" i
}

(**

### Match Expressions

`match` works exactly as it does in ordinary F# code:

*)

type Shape =
    | Circle of radius: float
    | Rectangle of width: float * height: float

let areas = asyncSeq {
    for shape in [ Circle 3.0; Rectangle (4.0, 5.0); Circle 1.5 ] do
        match shape with
        | Circle r       -> yield Math.PI * r * r
        | Rectangle(w,h) -> yield w * h
}

(**

### For Loops

`for` iterates over any `seq<'T>` or `IEnumerable<'T>` synchronously, or over an `AsyncSeq<'T>` asynchronously:

*)

// Iterating a plain sequence
let squaresOfList = asyncSeq {
    for n in [ 1; 2; 3; 4; 5 ] do
        yield n * n
}

// Iterating another AsyncSeq
let doubled = asyncSeq {
    for n in squaresOfList do
        yield n * 2
}

(**

### While Loops

`while` lets you emit elements until a condition becomes false.  Async operations can appear
inside the loop body:

*)

let countdown = asyncSeq {
    let mutable i = 5
    while i > 0 do
        yield i
        i <- i - 1
}

// Polling a resource until it is ready
let pollUntilReady (checkReady: unit -> Async<bool>) = asyncSeq {
    let mutable ready = false
    while not ready do
        do! Async.Sleep 500
        let! r = checkReady ()
        ready <- r
        if r then yield "ready"
}

(**

### Use Bindings

`use` disposes the resource when the sequence is no longer consumed, making it easy to work with
`IDisposable` objects such as file streams or database connections:

*)

open System.IO

let readLines (path: string) = asyncSeq {
    use reader = new StreamReader(path)
    let mutable line = reader.ReadLine()
    while line <> null do
        yield line
        line <- reader.ReadLine()
}

(**

### Try / With

`try`/`with` lets you catch exceptions that occur while producing elements and decide how to proceed:

*)

let safeParseInts (inputs: string list) = asyncSeq {
    for s in inputs do
        try
            yield int s
        with
        | :? FormatException -> () // skip values that can't be parsed
}

(**

`try`/`finally` is also supported and guarantees clean-up even if the consumer cancels early:

*)

let withCleanup = asyncSeq {
    try
        for i in 1 .. 5 do
            yield i
    finally
        printfn "sequence finished or cancelled"
}

(**

---

## Generating with `unfoldAsync`

`AsyncSeq.unfoldAsync` builds a sequence from a seed state. The supplied function receives
the current state and returns an `Async` of `Some (element, nextState)` to emit an element
and continue, or `None` to end the sequence.

As a concrete example, suppose you are writing a program that reads batches of tweets from
an API and stores those that pass a filter into a database. Each step is asynchronous:
fetching a batch, deciding whether a tweet is relevant, and writing to the database.

Given a `getTweetBatch` function of type `int -> Async<(Tweet[] * int) option>` — where the
`int` is a stream offset — the sequence of all batches is:

*)

type Tweet = {
    user    : string
    message : string
}

let getTweetBatch (offset: int) : Async<(Tweet[] * int) option> =
    async { return failwith "TODO: call Twitter API" }

let tweetBatches : AsyncSeq<Tweet[]> =
    AsyncSeq.unfoldAsync getTweetBatch 0

(**

When iterated, `tweetBatches` incrementally pages through the entire tweet stream.

Next, a filtering function that calls a web service has type `Tweet -> Async<bool>`. We
flatten the batches and filter the individual tweets:

*)

let filterTweet (t: Tweet) : Async<bool> =
    failwith "TODO: call web service"

let filteredTweets : AsyncSeq<Tweet> =
    tweetBatches
    |> AsyncSeq.concatSeq       // flatten batches to individual tweets
    |> AsyncSeq.filterAsync filterTweet

(**

Finally, a `storeTweet` function of type `Tweet -> Async<unit>` writes each tweet to the
database. We can compose the entire pipeline and run it:

*)

let storeTweet (t: Tweet) : Async<unit> =
    failwith "TODO: call database"

AsyncSeq.unfoldAsync getTweetBatch 0
|> AsyncSeq.concatSeq
|> AsyncSeq.filterAsync filterTweet
|> AsyncSeq.iterAsync storeTweet
|> Async.RunSynchronously

(**

This pipeline is a *representation* of the workflow — nothing executes until `Async.RunSynchronously`
is called. When it runs, it pages through tweets, filters them asynchronously, and stores
matches, all without blocking any threads.

*)
