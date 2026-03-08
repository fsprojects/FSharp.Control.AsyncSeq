(**
---
title: F# AsyncSeq Examples
category: Documentation
categoryindex: 2
index: 2
description: Examples demonstrating the use of F# asynchronous sequences.
keywords: F#, asynchronous sequences, AsyncSeq, examples
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
[![Binder](https://mybinder.org/badge_logo.svg)](https://mybinder.org/v2/gh/fsprojects/FSharp.Control.AsyncSeq/gh-pages?filepath=AsyncSeqExamples.ipynb)

# F# AsyncSeq Examples

This document illustrates common patterns for working with `AsyncSeq<'T>` — from building sequences
with computation expressions to transforming, filtering and aggregating them with the `AsyncSeq` module.

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

## Transforming Sequences

### map and mapAsync

`AsyncSeq.map` transforms every element synchronously:

*)

let strings = asyncSeq { yield! [ "hello"; "world"; "asyncseq" ] }

let upperCased : AsyncSeq<string> =
    strings |> AsyncSeq.map (fun s -> s.ToUpperInvariant())

(**

`AsyncSeq.mapAsync` is the same but the projection returns `Async<'U>`, so it can perform
asynchronous work per element — for example, fetching metadata for each item:

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

---

## Practical Patterns

The sections below walk through common real-world patterns using the rest of the `AsyncSeq` API.

### Group By

`AsyncSeq.groupBy` partitions a sequence into sub-sequences based on a key, analogous to
`Seq.groupBy`. Each key appears at most once in the output, paired with an `AsyncSeq` of the
elements that share that key.

> **Important:** the sub-sequences *must* be consumed in parallel. Sequential consumption will
> deadlock because no sub-sequence can complete until all others are also being consumed.

```
--------------------------------------------------
| source  | e1 | e2 | e3 | e4 |                  |
| key     | k1 | k2 | k1 | k2 |                  |
| result  | k1 → [e1, e3]   | k2 → [e2, e4]      |
--------------------------------------------------
```

A common use case is processing a stream of domain events where events for the same entity must
be handled in order, but events for different entities are independent and can be handled in
parallel:

*)

type Event = {
    entityId : int64
    data     : string
}

let stream : AsyncSeq<Event> = failwith "TODO: connect to message bus"

let action (e: Event) : Async<unit> = failwith "TODO: process event"

// Process each entity's events sequentially, but different entities in parallel.
stream
|> AsyncSeq.groupBy (fun e -> int e.entityId % 4)  // hash into 4 buckets
|> AsyncSeq.mapAsyncParallel (snd >> AsyncSeq.iterAsync action)
|> AsyncSeq.iter ignore

(**

We can combine this with batching for higher throughput. For example, when writing events to a
full-text search index, batching writes improves performance while the `groupBy` ensures ordering
within each entity:

*)

let batchStream : AsyncSeq<Event[]> = failwith "TODO: connect to batched source"

let batchAction (es: Event[]) : Async<unit> = failwith "TODO: bulk index"

batchStream
|> AsyncSeq.concatSeq                              // flatten batches to individual events
|> AsyncSeq.groupBy (fun e -> int e.entityId % 4) // partition into 4 groups
|> AsyncSeq.mapAsyncParallel (snd
    >> AsyncSeq.bufferByCountAndTime 500 1000      // re-batch per sub-sequence
    >> AsyncSeq.iterAsync batchAction)             // bulk index each batch
|> AsyncSeq.iter ignore

(**

The above workflow: (1) reads events in batches, (2) flattens them, (3) partitions by entity into
mutually-exclusive sub-sequences, (4) re-batches each sub-sequence by size/time, and (5) processes
the batches in parallel while preserving per-entity ordering.

### Merge

`AsyncSeq.merge` interleaves two sequences non-deterministically, emitting values as soon as
either source produces one. This is different from `AsyncSeq.append` (which exhausts one sequence
before starting the other) and `AsyncSeq.zip` (which waits for *both* to produce a value before
emitting a pair).

```
-----------------------------------------
| source1 | t0 |    | t1 |    |    | t2 |
| source2 |    | u0 |    |    | u1 |    |
| result  | t0 | u0 | t1 |    | u1 | t2 |
-----------------------------------------
```

A handy building block is an infinite sequence that ticks on a fixed interval:

*)

/// Emits `DateTime.UtcNow` immediately, then at every `periodMs` milliseconds.
let intervalMs (periodMs: int) = asyncSeq {
    yield DateTime.UtcNow
    while true do
        do! Async.Sleep periodMs
        yield DateTime.UtcNow
}

// React to whichever of the two timers fires first.
let either : AsyncSeq<DateTime> =
    AsyncSeq.merge (intervalMs 20) (intervalMs 30)

(**

### Combine Latest

`AsyncSeq.combineLatestWith` merges two sequences like `merge`, but instead of emitting raw
values it calls a combining function with the latest value from each side. The output sequence
only starts once *both* sources have produced at least one element, and thereafter re-emits
whenever either source advances.

```
----------------------------------------
| source1 | a0 |    |    | a1 |   | a2 |
| source2 |    | b0 | b1 |    |   |    |
| result  |    | c0 | c1 | c2 |   | c3 |
----------------------------------------

where  c0 = f a0 b0 | c1 = f a0 b1 | c2 = f a1 b1 | c3 = f a2 b1
```

Consider a service that watches a configuration key in [Consul](https://www.consul.io/) using HTTP
long-polling. Each response carries the new value and a modify-index used in the next request.
We can model the stream of changes as an `AsyncSeq`:

*)

type Key         = string
type Value       = string
type ModifyIndex = int64

let longPollKey (key: Key, mi: ModifyIndex) : Async<Value * ModifyIndex> =
    failwith "TODO: call Consul HTTP API"

/// Returns an AsyncSeq that emits a new value every time the key changes.
let changes (key: Key, mi: ModifyIndex) : AsyncSeq<Value> =
    AsyncSeq.unfoldAsync
        (fun (mi: ModifyIndex) -> async {
            let! value, mi' = longPollKey (key, mi)
            return Some (value, mi') })
        mi

(**

`changes` emits a new string every time the Consul key is updated. We can now combine it with
a periodic timer so that downstream logic is also triggered on a heartbeat even when the key
hasn't changed, keeping the resulting sequence current regardless:

*)

let changesOrInterval : AsyncSeq<Value> =
    AsyncSeq.combineLatestWith
        (fun v _ -> v)
        (changes ("myKey", 0L))
        (intervalMs (1000 * 60))   // tick every minute as a heartbeat

(**

Consumers of `changesOrInterval` will see the latest config value whenever it changes *or* at
least once per minute.

### Distinct Until Changed

`AsyncSeq.distinctUntilChanged` passes through every element of the source sequence but drops
consecutive duplicates, so downstream consumers only see values that are genuinely new.

```
-----------------------------------
| source  | a | a | b | b | b | a |
| result  | a |   | b |   |   | a |
-----------------------------------
```

A natural use case is polling a resource on a fixed schedule and reacting only when its state
actually changes. Consider a background job whose progress is exposed via a `getStatus` call:

*)

type Status = {
    completed : int
    finished  : bool
    result    : string
}

let getStatus : Async<Status> = failwith "TODO: call job API"

/// Poll every second and emit each status reading.
let statuses : AsyncSeq<Status> = asyncSeq {
    while true do
        let! s = getStatus
        yield s
        do! Async.Sleep 1000
}

/// Only emit when the status has actually changed.
let distinctStatuses : AsyncSeq<Status> =
    statuses |> AsyncSeq.distinctUntilChanged

(**

We can now build a workflow that logs every status change and stops as soon as the job finishes:

*)

let jobResult : Async<string> =
    distinctStatuses
    |> AsyncSeq.pick (fun st ->
        printfn "status=%A" st
        if st.finished then Some st.result else None)

(**

### Zip

`AsyncSeq.zip` combines two sequences element-wise into a sequence of pairs. The resulting
sequence terminates as soon as either source is exhausted.

```
---------------------------------------------
| source1  |    a1    |    a2    |           |
| source2  |    b1    |    b2    |    b3     |
| result   |  a1 * b1 |  a2 * b2 |           |
---------------------------------------------
```

A practical use of `AsyncSeq.zipWith` is rate-limiting: pair each incoming event with a minimum
delay so that the consumer never runs faster than one event per interval:

*)

let events : AsyncSeq<Event> = failwith "TODO: connect to event source"

/// Emit at most one event per second.
let eventsAtLeastOneSec : AsyncSeq<Event> =
    AsyncSeq.zipWith
        (fun evt _ -> evt)
        events
        (AsyncSeq.replicateInfiniteAsync (Async.Sleep 1000))

(**

The delay sequence is infinite so it never causes `zipWith` to terminate early; the overall
sequence ends only when `events` runs out.

### Buffer by Count and Time

`AsyncSeq.bufferByCountAndTime` accumulates incoming elements and emits a batch whenever
*either* the buffer reaches a given size *or* a timeout elapses — whichever comes first. If the
buffer is empty when the timeout fires, nothing is emitted.

```
-------------------------------------------------------
| source   |  a1 | a2 | a3         | a4      |        |
| result   |     |    | [a1,a2,a3] |         |  [a4]  |
-------------------------------------------------------
           ← batch size 3 reached →  ← timeout fires →
```

This is useful for services that write events to a bulk API (e.g. a search index). Fixed-size
batching with `AsyncSeq.bufferByCount` can stall when the source slows down and a partial buffer
never fills. `bufferByCountAndTime` avoids that by guaranteeing forward progress:

*)

let bufferSize    = 100
let bufferTimeout = 1000 // milliseconds

let bufferedEvents : AsyncSeq<Event[]> =
    events |> AsyncSeq.bufferByCountAndTime bufferSize bufferTimeout

