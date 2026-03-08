# FSharp.Control.AsyncSeq

FSharp.Control.AsyncSeq is a collection of asynchronous programming utilities for F#.

An asynchronous sequence is a sequence in which individual elements are _awaited_, so the next element of the sequence is not necessarily available immediately. This allows for efficient composition of asynchronous workflows which involve sequences of data.

FSharp.Control.AsyncSeq is an implementation of functional-first programming over asynchronous sequences for F#. The central type of the library, `AsyncSeq<'T>`, is a type alias for the standard type `System.Collections.Generic.IAsyncEnumerable<'T>`.

This was [one of the world's first implementations of langauge integrated asynchronous sequences](http://tomasp.net/blog/async-sequences.aspx) - that is, asynchronous sequences with integrated language support through computation expressions. It is a mature library used in production for many years and is widely used in the F# community.

An `AsyncSeq<'a>` can be generated using computation expression syntax much like `seq<'a>`:

    let oneThenTwo = asyncSeq {
      yield 1
      do! Async.Sleep 1000 // non-blocking sleep
      yield 2
    }

## Learning

* [Tutorial](AsyncSeq.fsx).
* [Generating sequences](AsyncSeqGenerating.fsx)
* [Transforming and reducing sequences](AsyncSeqTransforming.fsx)
* [Combining sequences](AsyncSeqCombining.fsx)
* [Advanced topics](AsyncSeqAdvanced.fsx)

## Related Libraries

### `FSharp.Control.TaskSeq`

[`FSharp.Control.TaskSeq`](https://github.com/fsprojects/FSharp.Control.TaskSeq/) provides a similar API oriented towards `Task<'T>` instead of `Async<'T>`. The choice between them is mostly a matter of preference or performance. The `AsyncSeq` library integrates well with the F# `Async<_>` type, while the `TaskSeq` library is more performant and integrates well with the .NET `Task<_>` type.

Both libraries implement that .NET standard `IAsyncEnumerable<'T>` interface, so they can be used interchangeably in most scenarios.

### seq<'T>

The central difference between `seq<'T>` and `AsyncSeq<'T>` can be illustrated by introducing the notion of time. Suppose that generating subsequent elements of a sequence requires an IO-bound operation. Invoking long  running IO-bound operations from within a `seq<'T>` will _block_ the thread which calls `MoveNext` on the corresponding `IEnumerator`. An `AsyncSeq` on the other hand can use facilities provided by the F# `Async` type to make more efficient use of system resources.

### IObservable<'T>

See [Comparison with IObservable](ComparisonWithObservable.fsx).
