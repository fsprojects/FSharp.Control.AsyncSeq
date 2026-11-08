[![Binder](https://mybinder.org/badge_logo.svg)](https://mybinder.org/v2/gh/fsprojects/FSharp.Control.AsyncSeq/gh-pages?filepath=AsyncSeqConsuming.ipynb)

# Consuming Asynchronous Sequences

All `AsyncSeq<'T>` values are *lazy* — they only produce elements when actively consumed. This
document covers the full range of consumption patterns, from iterating with a side effect to
collecting into an array, searching for an element and computing aggregate values.

```fsharp
open FSharp.Control

let oneThenTwo = asyncSeq {
    yield 1
    do! Async.Sleep 1000
    yield 2
}
```

## Iterating with a For Loop

Inside any `async { ... }` computation, you can iterate an `AsyncSeq` with a plain `for` loop.
The loop body may contain `let!` and `do!` bindings just like the rest of the async block:

```fsharp
async {
    for x in oneThenTwo do
        printfn "Got %d" x
} |> Async.RunSynchronously
```

This is the most natural way to consume a sequence when you already have an `async` context,
such as an application entry point or an async test.

-----------------------

## iter and iterAsync

`AsyncSeq.iter` applies a synchronous action to every element and returns `Async<unit>`:

```fsharp
let numbers = asyncSeq { yield! [ 1 .. 5 ] }

let printAll : Async<unit> =
    numbers |> AsyncSeq.iter (printfn "item: %d")
```

`AsyncSeq.iterAsync` does the same but the action returns `Async<unit>`, which is awaited before
the next element is consumed. This makes it ideal for actions that themselves do IO — such as
writing to a database or calling an API:

```fsharp
let processItem (n: int) : Async<unit> =
    async { printfn "processing %d" n }

let processAll : Async<unit> =
    numbers |> AsyncSeq.iterAsync processItem
```

### iteri and iteriAsync

`AsyncSeq.iteri` and `AsyncSeq.iteriAsync` are the same but also pass a zero-based integer
index to the action, useful for logging progress or tagging elements:

```fsharp
let printIndexed : Async<unit> =
    numbers |> AsyncSeq.iteri (fun i n -> printfn "[%d] %d" i n)
```

### iterAsyncParallel

`AsyncSeq.iterAsyncParallel` processes elements concurrently — the action for each element is
started as soon as the element is available, without waiting for the previous action to finish.
Use this when the actions are independent and you want maximum throughput:

```fsharp
let processAllParallel : Async<unit> =
    numbers |> AsyncSeq.iterAsyncParallel processItem
```

`AsyncSeq.iterAsyncParallelThrottled` is the same but limits the number of concurrent actions:

```fsharp
let processThrottled : Async<unit> =
    numbers |> AsyncSeq.iterAsyncParallelThrottled 4 processItem
```

-----------------------

## Folding

`AsyncSeq.fold` accumulates a state over all elements using a synchronous folder function. It
is the most general consumption primitive — all other aggregations can be implemented with it:

```fsharp
let sum : Async<int> =
    numbers |> AsyncSeq.fold (fun acc n -> acc + n) 0
```

`AsyncSeq.foldAsync` is the same but the folder returns `Async<'State>`, for cases where
accumulating a value requires async work:

```fsharp
let asyncSum : Async<int> =
    numbers |> AsyncSeq.foldAsync (fun acc n -> async { return acc + n }) 0
```

### reduceAsync

`AsyncSeq.reduceAsync` is a fold without an explicit seed — it uses the first element as the
initial state. It raises `InvalidOperationException` on an empty sequence:

```fsharp
let words = asyncSeq { yield! [ "F#"; "is"; "great" ] }

let sentence : Async<string> =
    words |> AsyncSeq.reduceAsync (fun acc w -> async { return acc + " " + w })
```

-----------------------

## Searching

### pick and tryPick

`AsyncSeq.pick` applies a chooser function to each element and returns the first `Some` result,
raising `KeyNotFoundException` if the sequence is exhausted without a match:

```fsharp
let firstEven : Async<int> =
    numbers |> AsyncSeq.pick (fun n -> if n % 2 = 0 then Some n else None)
```

`AsyncSeq.tryPick` is the safe variant — it returns `Async<'T option>` and returns `None`
instead of raising when there is no match:

```fsharp
let maybeFirstOver100 : Async<int option> =
    numbers |> AsyncSeq.tryPick (fun n -> if n > 100 then Some n else None)
```

`AsyncSeq.pickAsync` and `AsyncSeq.tryPickAsync` accept choosers that return `Async<_ option>`,
for cases where the matching decision requires async IO.

### exists and forall

`AsyncSeq.exists` returns `true` as soon as it finds an element satisfying the predicate, and
short-circuits consumption at that point. `AsyncSeq.forall` returns `false` as soon as it finds
an element that does *not* satisfy the predicate:

```fsharp
let hasEven  : Async<bool> = numbers |> AsyncSeq.exists (fun n -> n % 2 = 0)
let allSmall : Async<bool> = numbers |> AsyncSeq.forall (fun n -> n < 100)
```

`AsyncSeq.existsAsync` and `AsyncSeq.forallAsync` accept async predicates.

### head, last and firstOrDefault

`AsyncSeq.head` returns the first element, raising if the sequence is empty.
`AsyncSeq.firstOrDefault` returns a default value for empty sequences.
`AsyncSeq.last` and `AsyncSeq.lastOrDefault` do the same for the final element:

```fsharp
let strings = asyncSeq { yield! [ "hello"; "world" ] }

let firstWord : Async<string> = AsyncSeq.head strings
let lastWord  : Async<string> = AsyncSeq.last strings
let safeFirst : Async<string> = AsyncSeq.firstOrDefault "none" strings
```

-----------------------

## Collecting to a Collection

The `toArrayAsync` and `toListAsync` functions consume the entire sequence and materialise it
into an F# array or list. These are useful when you need random access or must pass the results
to code that expects a concrete collection:

```fsharp
let asArray : Async<int[]>   = numbers |> AsyncSeq.toArrayAsync
let asList  : Async<int list> = numbers |> AsyncSeq.toListAsync
```

`toArraySynchronously` and `toListSynchronously` do the same without wrapping in `Async` — they
block the calling thread until the sequence is exhausted. Only use these outside of async
contexts, e.g. in test code or scripts:

```fsharp
let syncArray : int[] = numbers |> AsyncSeq.toArraySynchronously
```

-----------------------

## Aggregation

### length

`AsyncSeq.length` counts the elements, returning `Async<int64>`:

```fsharp
let count : Async<int64> = numbers |> AsyncSeq.length
```

### sumBy and sumByAsync

`AsyncSeq.sumBy` projects each element to a numeric type and sums the results:

```fsharp
let sumOfSquares : Async<int> =
    numbers |> AsyncSeq.sumBy (fun n -> n * n)
```

`AsyncSeq.sumByAsync` is the same when the projection needs to perform async work:

```fsharp
let fetchScore (n: int) : Async<float> =
    async { return float n * 0.5 } // placeholder

let totalScore : Async<float> =
    numbers |> AsyncSeq.sumByAsync fetchScore
```

### averageBy and averageByAsync

`AsyncSeq.averageBy` computes the arithmetic mean of a projected value:

```fsharp
let meanSquare : Async<float> =
    numbers |> AsyncSeq.averageBy (fun n -> float (n * n))
```

`AsyncSeq.averageByAsync` accepts an async projection:

```fsharp
let meanScore : Async<float> =
    numbers |> AsyncSeq.averageByAsync fetchScore
```

### countBy

`AsyncSeq.countBy` counts how many elements share each key, returning an array of
*(key, count)* pairs:

```fsharp
let digitParity : Async<(string * int) array> =
    numbers |> AsyncSeq.countBy (fun n -> if n % 2 = 0 then "even" else "odd")
```

`AsyncSeq.countByAsync` accepts an async projection when computing the key requires IO.
