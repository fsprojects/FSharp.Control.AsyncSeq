(**
---
title: Advanced AsyncSeq Operations
category: Documentation
categoryindex: 2
index: 5
description: Advanced F# asynchronous sequence operations including groupBy, distinct-until-changed and time-or-count buffering.
keywords: F#, asynchronous sequences, AsyncSeq, groupBy, distinctUntilChanged, bufferByCountAndTime
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
[![Binder](https://mybinder.org/badge_logo.svg)](https://mybinder.org/v2/gh/fsprojects/FSharp.Control.AsyncSeq/gh-pages?filepath=AsyncSeqAdvanced.ipynb)

# Advanced AsyncSeq Operations

This document covers advanced `AsyncSeq<'T>` operations: partitioning a sequence into keyed
sub-streams with `groupBy`, deduplication with `distinctUntilChanged`, and accumulating
elements into time-or-count-bounded batches with `bufferByCountAndTime`.

*)

open System
open FSharp.Control

(**

## Group By

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

## Distinct Until Changed

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

## Buffer by Count and Time

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

let events : AsyncSeq<Event> = failwith "TODO: connect to event source"

let bufferSize    = 100
let bufferTimeout = 1000 // milliseconds

let bufferedEvents : AsyncSeq<Event[]> =
    events |> AsyncSeq.bufferByCountAndTime bufferSize bufferTimeout
