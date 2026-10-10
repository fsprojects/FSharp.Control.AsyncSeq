# FSharp.Control.AsyncSeq

FSharp.Control.AsyncSeq is an implementation of functional-first programming over asynchronous sequences for F#.

An asynchronous sequence is a sequence in which individual elements are *awaited*, so the next element of the sequence is not necessarily available immediately. This allows for efficient composition of asynchronous workflows which involve sequences of data.

## Quick Start

To use, reference [the NuGet package `FSharp.Control.AsyncSeq`](https://www.nuget.org/packages/FSharp.Control.AsyncSeq) in your project and open the `FSharp.Control` namespace:

```fsharp
open FSharp.Control
```

An `AsyncSeq<_>` can be generated using computation expression syntax:

```fsharp
let oneThenTwo = asyncSeq {
    yield 1
    do! Async.Sleep 1000 // non-blocking sleep
    yield 2
}
```

Asynchronous sequences must be started, usually by some consuming operation. When started, the above asynchronous sequence `oneThenTwo` will yield the value `1` immediately, but the value `2` will only be available after a delay of 1 second and any consumer must *await* the second value.

There are many ways to consume an `AsyncSeq<'a>`, for example using `AsyncSeq.iter`:

```fsharp
oneThenTwo |> AsyncSeq.iter (printfn "Got %d") |> Async.RunSynchronously
```

or an async computation expression:

```fsharp
async {
    for x in oneThenTwo do
        printfn "Got %d" x
} |> Async.RunSynchronously
```

## Learning

* [Generating sequences](AsyncSeqGenerating.md)

* [Transforming sequences](AsyncSeqTransforming.md)

* [Combining sequences](AsyncSeqCombining.md)

* [Consuming sequences](AsyncSeqConsuming.md)

* [Advanced topics](AsyncSeqAdvanced.md)

## History

This was [one of the world's first implementations of langauge integrated asynchronous sequences](http://tomasp.net/blog/async-sequences.aspx) - that is, asynchronous sequences with integrated language support through computation expressions. It is a mature library used in production for many years and is widely used in the F# community.

## Related Libraries

### FSharp.Control.TaskSeq

[FSharp.Control.TaskSeq](https://github.com/fsprojects/FSharp.Control.TaskSeq/) provides a similar API, oriented towards `Task<'T>` instead of `Async<'T>`. The choice between them is mostly a matter of preference or performance. The `AsyncSeq` library integrates well with the F# `Async<_>` type, while the `TaskSeq` library is more performant and integrates well with the .NET `Task<_>` type.

Both libraries implement that .NET standard `IAsyncEnumerable<'T>` interface, so they can be used interchangeably in most scenarios.

### FSharp.Collections.Seq

The central difference between `seq<'T>` and `AsyncSeq<'T>` is the notion of time. Suppose that generating subsequent elements of a sequence requires an IO-bound operation. Invoking long  running IO-bound operations from within a `seq<'T>` will *block* the thread which calls `MoveNext` on the corresponding `IEnumerator`. An `AsyncSeq` on the other hand can use facilities provided by the F# `Async` type to make more efficient use of system resources.

## Related Articles

* [Programming with F# asynchronous sequences](http://tomasp.net/blog/async-sequences.aspx/)
