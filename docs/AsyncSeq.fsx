(**
---
title: F# Asynchronous Sequences
category: Documentation
categoryindex: 2
index: 1
description: An introduction to F# asynchronous sequences and how to use them.
keywords: F#, asynchronous sequences, AsyncSeq, IAsyncEnumerable, async workflows
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
[![Binder](https://mybinder.org/badge_logo.svg)](https://mybinder.org/v2/gh/fsprojects/FSharp.Control.AsyncSeq/gh-pages?filepath=AsyncSeq.ipynb)

# F# Asynchronous Sequences

An asynchronous sequence is a sequence in which individual elements are _awaited_, so the next element of the sequence is not necessarily available immediately. This allows for efficient composition of asynchronous workflows which involve sequences of data.

FSharp.Control.AsyncSeq is an implementation of functional-first programming over asynchronous sequences for F#. The central type of the library, `AsyncSeq<'T>`, is a type alias for the standard type `System.Collections.Generic.IAsyncEnumerable<'T>`.

This library was also [one of the world's first implementations of langauge integrated asynchronous sequences](http://tomasp.net/blog/async-sequences.aspx) - that is, asynchronous sequences with integrated language support through computation expressions. It is a mature library used in production for many years and is widely used in the F# community.

### Generating asynchronous sequences

To use the library, referrence the NuGet package `FSharp.Control.AsyncSeq` in your project and open the `FSharp.Control` namespace:
*)

open FSharp.Control

(**
An asynchronous sequence can be generated using a computation expression:
*)

let async12 = asyncSeq {
  yield 1
  yield 2
}

(**
or more succinctly:
*)

let async12b = asyncSeq { 1; 2 }

(**
### Comparison with `FSharp.Control.TaskSeq`

A related library is [`FSharp.Control.TaskSeq`](https://github.com/fsprojects/FSharp.Control.TaskSeq/) which provides a similar API for sequences of `Task<'T>` instead of `Async<'T>`. The two libraries are very similar and the choice between them is mostly a matter of preference or performance. The `AsyncSeq` library integrates well with the F# `Async` type, while the `TaskSeq` library is more performant and integrates well with the .NET `Task` type.

Both libraries implement that .NET standard `IAsyncEnumerable<'T>` interface, so they can be used interchangeably in most scenarios.

### Comparison with seq<'T>

The central difference between `seq<'T>` and `AsyncSeq<'T>` can be illustrated by introducing the notion of time.
Suppose that generating subsequent elements of a sequence requires an IO-bound operation. Invoking long 
running IO-bound operations from within a `seq<'T>` will *block* the thread which calls `MoveNext` on the 
corresponding `IEnumerator`. An `AsyncSeq` on the other hand can use facilities provided by the F# `Async` type to make 
more efficient use of system resources.
*)

open System.Threading

let withTime = seq {
  Thread.Sleep(1000) // calling thread will block
  yield 1
  Thread.Sleep(1000) // calling thread will block
  yield 1
}

let withTime2 = asyncSeq {
  do! Async.Sleep 1000 // non-blocking sleep
  yield 1
  do! Async.Sleep 1000 // non-blocking sleep
  yield 2
}

(**
When the asynchronous sequence `withTime'` is iterated, the calls to `Async.Sleep` won't block threads. Instead,
the *continuation* of the sequence will be scheduled by `Async` while the calling thread will be free to perform other work. 
Overall, a `seq<'a>` can be viewed as a special case of an `AsyncSeq<'a>` where subsequent elements are retrieved
in a blocking manner.

### Performance Considerations

While an asynchronous computation obviates the need to block an OS thread for the duration of an operation, it isn't always the case
that this will improve the overall performance of an application. Note however that an async computation does not *require* a
non-blocking operation, it simply allows for it. Also of note is that unlike calling `IEnumerable.MoveNext()`, consuming
an item from an asynchronous sequence requires several allocations. Usually this is greatly outweighed by the
benefits, it can make a difference in some scenarios.

## Related Articles

 * [Programming with F# asynchronous sequences](http://tomasp.net/blog/async-sequences.aspx/)

*)
