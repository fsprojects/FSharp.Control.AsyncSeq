FSharp.Control.AsyncSeq
=============

FSharp.Control.AsyncSeq is a collection of asynchronous programming utilities for F#.

An AsyncSeq is a sequence in which individual elements are retrieved using an `Async` computation.
The power of `AsyncSeq` lies in that many of these operations also have analogs based on `Async` 
allowing composition of complex asynchronous workflows, including compositional cancellation.

An `AsyncSeq<'a>` can be generated using computation expression syntax much like `seq<'a>`:

    let oneThenTwo = asyncSeq {
      yield 1
      do! Async.Sleep 1000 // non-blocking sleep
      yield 2
    }

Learning
--------------------------

[AsyncSeq](AsyncSeq.html) contains narrative and code samples explaining asynchronous sequences.

[AsyncSeq Examples](AsyncSeqExamples.html) contains examples.

[Terminology](terminology.html) a reference for some of the terminology around F# async.
 
[API Reference](reference/index.html) contains automatically generated documentation for all types, modules and functions in the library. 
This includes additional brief samples on using most of the functions.

Contributing and copyright
--------------------------

The project is hosted on [GitHub][gh] where you can [report issues][issues], fork 
the project and submit pull requests. If you're adding a new public API, please also 
consider adding [samples][content] that can be turned into a documentation. You might
also want to read the [library design notes][readme] to understand how it works.

The library is available under Apache 2.0 license, which allows modification and 
redistribution for both commercial and non-commercial purposes. For more information see the 
[License file][license] in the GitHub repository. 

  [content]: https://github.com/fsprojects/FSharp.Control.AsyncSeq/tree/master/docs/content
  [gh]: https://github.com/fsprojects/FSharp.Control.AsyncSeq
  [issues]: https://github.com/fsprojects/FSharp.Control.AsyncSeq/issues
  [readme]: https://github.com/fsprojects/FSharp.Control.AsyncSeq/blob/master/README.md
  [license]: https://github.com/fsprojects/FSharp.Control.AsyncSeq/blob/master/LICENSE.txt