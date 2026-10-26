[![Binder](https://mybinder.org/badge_logo.svg)](https://mybinder.org/v2/gh/fsprojects/FSharp.Control.AsyncSeq/gh-pages?filepath=AsyncSeqCombining.ipynb)

# Combining Asynchronous Sequences

This document covers the operations that combine two or more `AsyncSeq<'T>` values into one.
The operations differ in *when* they wait for input and *how* they interleave elements:

Operation | Timing | Result
--- | --- | ---
`append` | sequential | all of seq1 then all of seq2
`zip` / `zipWith` | lock-step | waits for one element from each source
`interleave` | lock-step | alternates strictly between sources
`merge` | non-deterministic | emits whenever *any* source produces
`combineLatestWith` | non-deterministic | emits the latest pair whenever *any* source advances


```fsharp
open System
open FSharp.Control
```

## Append

`AsyncSeq.append` produces all elements of the first sequence followed by all elements of the
second. The second sequence does not start until the first is exhausted — it is strictly
sequential:

```fsharp
let first  = asyncSeq { yield! [ 1; 2; 3 ] }
let second = asyncSeq { yield! [ 4; 5; 6 ] }

let appended : AsyncSeq<int> = AsyncSeq.append first second
// emits: 1, 2, 3, 4, 5, 6
```

`yield!` inside `asyncSeq { ... }` is equivalent to `append` and is the idiomatic choice when
concatenating inline:

```fsharp
let combined = asyncSeq {
    yield! first
    yield! second
}
```

-----------------------

## Zip

`AsyncSeq.zip` combines two sequences element-wise into a sequence of pairs, consuming one
element from each source before producing output. The result terminates as soon as either source
is exhausted.

```fsharp
---------------------------------------------
| source1  |    a1    |    a2    |           |
| source2  |    b1    |    b2    |    b3     |
| result   |  a1 * b1 |  a2 * b2 |           |
---------------------------------------------

```

```fsharp
let letters = asyncSeq { yield! [ 'a'; 'b'; 'c' ] }
let numbers = asyncSeq { yield! [ 1; 2; 3; 4 ] }

let pairs : AsyncSeq<char * int> = AsyncSeq.zip letters numbers
// emits: ('a',1), ('b',2), ('c',3)  — stops when letters runs out
```

### zipWith and zipWithAsync

`AsyncSeq.zipWith` applies a combining function instead of producing tuples:

```fsharp
let sums : AsyncSeq<int> =
    AsyncSeq.zipWith (fun a b -> a + b) numbers numbers
// emits: 2, 4, 6, 8
```

`AsyncSeq.zipWithAsync` is the same but the combining function returns `Async<'U>`, useful when
the combination itself requires IO:

```fsharp
let combine (a: int) (b: int) : Async<string> =
    async { return sprintf "%d+%d=%d" a b (a+b) }

let asyncSums : AsyncSeq<string> =
    AsyncSeq.zipWithAsync combine numbers numbers
```

### zipParallel

`AsyncSeq.zipParallel` is identical to `zip` but fetches the next element from both sources
*simultaneously* rather than sequentially. Use this when the two sources are independent and
fetching each element involves latency:

```fsharp
let parallelPairs : AsyncSeq<char * int> = AsyncSeq.zipParallel letters numbers
```

### zip3 and zipWith3

For three sources at once:

```fsharp
let triples =
    AsyncSeq.zip3
        (asyncSeq { yield! [ 1; 2; 3 ] })
        (asyncSeq { yield! [ 'a'; 'b'; 'c' ] })
        (asyncSeq { yield! [ true; false; true ] })
// emits: (1,'a',true), (2,'b',false), (3,'c',true)
```

### Rate-limiting with zipWith

A practical pattern for `zipWith` is rate-limiting: pair each element of a fast source with a
minimum delay so that the consumer never runs faster than one element per interval:

```fsharp
type Event = { entityId: int64; data: string }

let events : AsyncSeq<Event> = failwith "TODO: connect to event source"

/// Emit at most one event per second.
let eventsAtLeastOneSec : AsyncSeq<Event> =
    AsyncSeq.zipWith
        (fun evt _ -> evt)
        events
        (AsyncSeq.replicateInfiniteAsync (Async.Sleep 1000))
```

The delay sequence is infinite so it never causes `zipWith` to terminate early; the resulting
sequence ends only when `events` runs out.

-----------------------

## Interleave

`AsyncSeq.interleave` alternates *strictly* between two sources in lock-step: one element from
source1, one from source2, one from source1, and so on. It terminates when either source is
exhausted. Unlike `merge`, the ordering is deterministic.

```fsharp
----------------------------------------------
| source1 | a1 |    | a2 |    | a3 |         |
| source2 |    | b1 |    | b2 |    |         |
| result  | a1 | b1 | a2 | b2 | a3 |         |
----------------------------------------------

```

```fsharp
let odds  = asyncSeq { yield! [ 1; 3; 5 ] }
let evens = asyncSeq { yield! [ 2; 4; 6 ] }

let interleaved : AsyncSeq<int> = AsyncSeq.interleave odds evens
// emits: 1, 2, 3, 4, 5, 6
```

### interleaveMany

`AsyncSeq.interleaveMany` generalises this to an arbitrary number of sequences, cycling through
them in order:

```fsharp
let a = asyncSeq { yield! [ 1; 4; 7 ] }
let b = asyncSeq { yield! [ 2; 5; 8 ] }
let c = asyncSeq { yield! [ 3; 6; 9 ] }

let roundRobin : AsyncSeq<int> = AsyncSeq.interleaveMany [ a; b; c ]
// emits: 1, 2, 3, 4, 5, 6, 7, 8, 9
```

### interleaveChoice

`AsyncSeq.interleaveChoice` interleaves two sequences of *different* types, wrapping each
element in `Choice<'T1,'T2>` so you can tell which source it came from:

```fsharp
let intSeq  = asyncSeq { yield! [ 1; 2; 3 ] }
let strSeq  = asyncSeq { yield! [ "a"; "b"; "c" ] }

let tagged : AsyncSeq<Choice<int,string>> =
    AsyncSeq.interleaveChoice intSeq strSeq
// Choice1Of2 1, Choice2Of2 "a", Choice1Of2 2, Choice2Of2 "b", ...
```

-----------------------

## Merge

`AsyncSeq.merge` interleaves two sequences *non-deterministically*, emitting values as soon as
either source produces one. There is no lock-step coordination — whichever source is ready first
wins. The result continues until both sources are exhausted.

```fsharp
-----------------------------------------
| source1 | t0 |    | t1 |    |    | t2 |
| source2 |    | u0 |    |    | u1 |    |
| result  | t0 | u0 | t1 |    | u1 | t2 |
-----------------------------------------

```

This is different from `append` (sequential) and `interleave` (strict lock-step).

A handy building block is an infinite ticker:

```fsharp
/// Emits `DateTime.UtcNow` immediately, then every `periodMs` milliseconds.
let intervalMs (periodMs: int) = asyncSeq {
    yield DateTime.UtcNow
    while true do
        do! Async.Sleep periodMs
        yield DateTime.UtcNow
}

// React to whichever of the two timers fires first.
let either : AsyncSeq<DateTime> =
    AsyncSeq.merge (intervalMs 20) (intervalMs 30)
```

### mergeAll

`AsyncSeq.mergeAll` merges an arbitrary number of sequences non-deterministically:

```fsharp
let sources : AsyncSeq<DateTime> =
    AsyncSeq.mergeAll [ intervalMs 100; intervalMs 250; intervalMs 500 ]
```

### mergeChoice

`AsyncSeq.mergeChoice` merges two sequences of *different* types non-deterministically, tagging
each element with `Choice<'T1,'T2>` so you can distinguish the source:

```fsharp
type Heartbeat = Heartbeat
type ConfigChange = ConfigChange of string

let heartbeats : AsyncSeq<Heartbeat> = AsyncSeq.replicateInfiniteAsync (async { 
    do! Async.Sleep 5000
    return Heartbeat 
})
let configChanges : AsyncSeq<ConfigChange>  = failwith "TODO: long-poll config store"

let merged : AsyncSeq<Choice<Heartbeat, ConfigChange>> =
    AsyncSeq.mergeChoice heartbeats configChanges
```

-----------------------

## Combine Latest

`AsyncSeq.combineLatestWith` merges two sequences non-deterministically and applies a combining
function to the *latest* value from each side whenever either source advances. The output only
starts once both sources have each produced at least one element.

```fsharp
----------------------------------------
| source1 | a0 |    |    | a1 |   | a2 |
| source2 |    | b0 | b1 |    |   |    |
| result  |    | c0 | c1 | c2 |   | c3 |
----------------------------------------

where  c0 = f a0 b0 | c1 = f a0 b1 | c2 = f a1 b1 | c3 = f a2 b1

```

Unlike `zip`, the two sources do not have to produce elements at the same rate. A fast source
will simply keep updating the "latest" value used in future combinations.

Consider a service that watches a configuration key in [Consul](https://www.consul.io/) using
HTTP long-polling. Each response carries the new value and a modify-index used in the next
request. We can model the stream of changes as an `AsyncSeq`:

```fsharp
type Key         = string
type Value       = string
type ModifyIndex = int64

let longPollKey (key: Key, mi: ModifyIndex) : Async<Value * ModifyIndex> =
    failwith "TODO: call Consul HTTP API"

/// Emits a new value every time the Consul key changes.
let changes (key: Key, mi: ModifyIndex) : AsyncSeq<Value> =
    AsyncSeq.unfoldAsync
        (fun (mi: ModifyIndex) -> async {
            let! value, mi' = longPollKey (key, mi)
            return Some (value, mi') })
        mi
```

We combine the change stream with a periodic heartbeat so that downstream logic is re-triggered
at least once per minute even when the key is stable:

```fsharp
let changesOrInterval : AsyncSeq<Value> =
    AsyncSeq.combineLatestWith
        (fun v _ -> v)
        (changes ("myKey", 0L))
        (intervalMs (1000 * 60))
```

Consumers of `changesOrInterval` see the latest config value whenever the key changes *or* at
least once per minute.
