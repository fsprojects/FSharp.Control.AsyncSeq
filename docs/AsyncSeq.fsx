(**
---
title: Tutorial
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

# Tutorial

## Generating asynchronous sequences

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
When the asynchronous sequence `withTime'` is iterated, the calls to `Async.Sleep` won't block threads. Instead, the *continuation* of the sequence will be scheduled by `Async` while the calling thread will be free to perform other work.  Overall, a `seq<'a>` can be viewed as a special case of an `AsyncSeq<'a>` where subsequent elements are retrieved in a blocking manner.

*)

(**
## Performance Considerations

While an asynchronous computation obviates the need to block an OS thread for the duration of an operation, it isn't always the case that this will improve the overall performance of an application. Note however that an async computation does not *require* a non-blocking operation, it simply allows for it. Also of note is that unlike calling `IEnumerable.MoveNext()`, consuming an item from an asynchronous sequence requires several allocations. Usually this is greatly outweighed by the benefits, it can make a difference in some scenarios.

## Related Articles

 * [Programming with F# asynchronous sequences](http://tomasp.net/blog/async-sequences.aspx/)

*)
