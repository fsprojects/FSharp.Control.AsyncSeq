### 4.1.0

* Added `AsyncSeq.zip3`, `AsyncSeq.zipWith3`, and `AsyncSeq.zipWithAsync3` — combinators for zipping three async sequences, mirroring `Seq.zip3` ([PR #254](https://github.com/fsprojects/FSharp.Control.AsyncSeq/pull/254)).
* Added `AsyncSeq.windowed` — produces a sliding window of a given size over an async sequence ([PR #241](https://github.com/fsprojects/FSharp.Control.AsyncSeq/pull/241)).
* Added `AsyncSeq.reduce` and `AsyncSeq.reduceAsync` — folds without a seed value, mirroring `Seq.reduce` ([PR #242](https://github.com/fsprojects/FSharp.Control.AsyncSeq/pull/242)).
* Added `AsyncSeq.sumBy`, `AsyncSeq.sumByAsync`, `AsyncSeq.average`, `AsyncSeq.averageBy`, and `AsyncSeq.averageByAsync` — numeric aggregation combinators mirroring the corresponding `Seq` module functions ([PR #245](https://github.com/fsprojects/FSharp.Control.AsyncSeq/pull/245)).
* Added `AsyncSeq.min`, `AsyncSeq.max`, `AsyncSeq.minBy`, `AsyncSeq.maxBy`, `AsyncSeq.minByAsync`, and `AsyncSeq.maxByAsync` — min/max aggregation combinators mirroring `Seq.min`/`Seq.max`/`Seq.minBy`/`Seq.maxBy` ([PR #243](https://github.com/fsprojects/FSharp.Control.AsyncSeq/pull/243)).
* Added `AsyncSeq.distinct`, `AsyncSeq.distinctBy`, `AsyncSeq.distinctByAsync`, `AsyncSeq.countBy`, `AsyncSeq.countByAsync`, `AsyncSeq.exactlyOne`, and `AsyncSeq.tryExactlyOne` — set-membership and cardinality combinators mirroring the corresponding `Seq` module functions ([PR #249](https://github.com/fsprojects/FSharp.Control.AsyncSeq/pull/249)).

### 4.0.0

* **Breaking:** `AsyncSeq<'T>` is now `System.Collections.Generic.IAsyncEnumerable<'T>` (the BCL type). `ofAsyncEnum` and `toAsyncEnum` are now identity functions and marked `[<Obsolete>]`. Code that directly calls `.GetEnumerator()`/`.MoveNext()` must switch to `.GetAsyncEnumerator(ct)`/`.MoveNextAsync()` ([#230](https://github.com/fsprojects/FSharp.Control.AsyncSeq/issues/230), [PR #231](https://github.com/fsprojects/FSharp.Control.AsyncSeq/pull/231)).
* Added `YieldFrom` overload for `seq<'T>` in `asyncSeq` computation expression — `yield! items` now works when `items` is a `seq<'T>` ([#123](https://github.com/fsprojects/FSharp.Control.AsyncSeq/issues/123), [PR #236](https://github.com/fsprojects/FSharp.Control.AsyncSeq/pull/236)).
* Added `[<CompilerMessage>]` warning to `groupBy` and `groupByAsync` to alert users that results must be consumed with a parallel combinator to avoid deadlock ([#125](https://github.com/fsprojects/FSharp.Control.AsyncSeq/issues/125), [PR #235](https://github.com/fsprojects/FSharp.Control.AsyncSeq/pull/235)).
* Added `AsyncSeq.mapAsyncUnorderedParallelThrottled` for throttled unordered parallel mapping ([#31](https://github.com/fsprojects/FSharp.Control.AsyncSeq/issues/31), [PR #225](https://github.com/fsprojects/FSharp.Control.AsyncSeq/pull/225)).
* Fixed `ofAsyncEnum` deadlock on single-threaded runtimes such as Blazor WASM ([#152](https://github.com/fsprojects/FSharp.Control.AsyncSeq/issues/152), [PR #229](https://github.com/fsprojects/FSharp.Control.AsyncSeq/pull/229)).
* Updated `PackageLicenseExpression` (removed deprecated `PackageLicenseUrl`) and updated `Microsoft.Bcl.AsyncInterfaces` to 10.0.3 ([#168](https://github.com/fsprojects/FSharp.Control.AsyncSeq/issues/168), [PR #228](https://github.com/fsprojects/FSharp.Control.AsyncSeq/pull/228)).

### 3.3.1

* Quick summary of changes:
  * Performance improvements: optimized `iterAsync` and `iteriAsync`; optimized `collect`, `mapAsync` and `unfoldAsync`; fixed append memory leak (Issue #35).
  * Added `mapAsyncUnorderedParallel` for improved parallel performance.
  * Added `AsyncSeq.chunkBy` and `AsyncSeq.chunkByAsync` for grouping consecutive elements by key ([#156](https://github.com/fsprojects/FSharp.Control.AsyncSeq/issues/156), [PR #222](https://github.com/fsprojects/FSharp.Control.AsyncSeq/pull/222)).
  * `AsyncSeq.mergeAll` now accepts `seq<AsyncSeq<'T>>` instead of `list<AsyncSeq<'T>>` for broader compatibility ([#165](https://github.com/fsprojects/FSharp.Control.AsyncSeq/issues/165), [PR #221](https://github.com/fsprojects/FSharp.Control.AsyncSeq/pull/221)).
  * Set up BenchmarkDotNet for systematic benchmarking; performance benchmarks show measurable improvements (addresses Round 1 & 2 of #190).
  * Build/CI updates: configuration and build-step updates.

### 3.2.1

* Release latest

### 3.2.0

* [Update to Fable 3.0](https://github.com/fsprojects/FSharp.Control.AsyncSeq/pull/148)

### 3.1.0

* Sorting functions <https://github.com/fsprojects/FSharp.Control.AsyncSeq/issues/126>

### 3.0.5

* Update publishing

### 3.0.4

* Restore netstandard2.0 (and thereby .NET Framework 4.6.1+) compatibility
* Update build env versions to current recommendations
* Adjust to ensure all tests pass and eliminate warnings in vscode (ionide) and visual studio

### 3.0.2

* Include .fsi files into Fable package path [#118](https://github.com/fsprojects/FSharp.Control.AsyncSeq/pull/118)

### 3.0.1

* Move to only netstandard 2.1

### 2.0.24 - 27.05.2020

* Adding ofIQueryable [#112](https://github.com/fsprojects/FSharp.Control.AsyncSeq/pull/112)

### 2.0.23 - 29.01.2019

* Adding .NET IAsyncEnumerable conversion functions (ofAsyncEnum and toAsyncEnum) [#96](https://github.com/fsprojects/FSharp.Control.AsyncSeq/pull/96)

### 2.0.22 - 29.09.2019

* Rename toList and toArray to toListSynchronously and toArraySynchronously
* Add ofSeqAsync and concat
* Improve parallelism of AsyncSeq.cache

### 2.0.21 - 28.12.2017

* Fix packaging issues
* Reference FSharp.Core 4.3 for nestandard builds

### 2.0.21-alpha02 - 28.12.2017

* Fix packaging issues
* Reference FSharp.Core 4.3 for nestandard builds

### 2.0.21-alpha01 - 28.12.2017

* Fix packaging issues
* Reference FSharp.Core 4.3 for nestandard builds

### 2.0.20 - 16.12.2017

* Target .NET Standard

### 2.0.19-alpha01 - 22.12.2017

* Target .NET Standard

### 2.0.18 - 14.12.2017

* AsyncSeq.mergeAll min-max fairness

### 2.0.17 - 21.11.2017

* Improve performance of internal Async.chooseTasks function which improves performance of AsyncSeq.bufferByCountAndTime, etc (<https://github.com/fsprojects/FSharp.Control.AsyncSeq/pull/73>)

### 2.0.16 - 29.09.2017

* Fix previous package deployment

### 2.0.15 - 27.09.2017

* NEW: AsyncSeq.bufferByTime

### 2.0.14 - 27.09.2017

* BUG: Fixed head of line blocking in AsyncSeq.mapAsyncParallel

### 2.0.13 - 26.09.2017

* NEW: AsyncSeq.takeWhileInclusive
* NEW: AsyncSeq.replicateUntilNoneAsync
* NEW: AsyncSeq.iterAsyncParallel
* NEW: AsyncSeq.iterAsyncParallelThrottled
* BUG: Fixed exception propagation in AsyncSeq.mapAsyncParallel

### 2.0.12 - 06.04.2017

* Fix bug #63 in AsyncSeq.unfold >> AsyncSeq.choose

### 2.0.11 - 04.03.2017

* Fixed bug in AsyncSeq.cache when used by interleaved consumers.
* AsyncSeq.zipWithAsyncParallel (and variants)

### 2.0.10 - 24.11.2016

* Improved asyncSeq workflow performance via bindAsync generator (@pragmatrix)

### 2.0.9 - 27.07.2016

* Much improved append performance.
* Direct implementation of unfoldAsync as IAsyncEnumerable, with chooseAsync, mapAsync and foldAsync overrides

### 2.0.8 - 29.03.2016

* Add portable7 profile

### 2.0.3 - 03.12.2015

* Fix bug in Async.cache [#33](https://github.com/fsprojects/FSharp.Control.AsyncSeq/issues/33)

### 2.0.2 - 15.10.2015

* Fix leak in AsyncSeq.append and other derived generators

### 2.0.1 - 01.06.2015

* Add AsyncSeq.sum, length, contains, exists, forall, tryPick, tryFind

### 2.0.0 - 28.05.2015

* Simplify ofObservableBuffered and toBlockingSeq
* Move to IAsyncEnumerable model to support try/finally and try/with
* Rename replicate to replicateInfinite
* Rename toList to toListAsync
* Rename toArray to toArrayAsync
* Rename zipWithIndexAsync to mapiAsync
* Rename interleave to interleaveChoice
* Add interleave
* Add mergeChoice
* Fix performance of mergeAll
* Add init, initInfinite
* Add initAsync, initInfiniteAsync, replicateInfinite
* Add RequireQualifiedAccess to AsyncSeq

### 1.15.0 - 30.03.2015

* Add AsyncSeq.getIterator (unblocks use of AsyncSeq in FSharpx.Async)

### 1.14 - 30.03.2015

* Cancellable AsyncSeq.toBlockingSeq
* Fix AsyncSeq.scanAsync to also return first state
* Add a signature file
* AsyncSeq got extracted as separate project and is now a dependency - <https://github.com/fsprojects/FSharpx.Async/pull/24>

### 1.13 - 27.03.2015

* Renamed to FSharp.Control.AsyncSeq
* Remove surface area
* Hide Nil/Cons from representation of AsyncSeq

### 1.12.1 - 27.03.2015

* Added Async.bindChoice, Async.ParallelIgnore, AsyncSeq.zipWithAsync, AsyncSeq.zappAsync, AsyncSeq.threadStateAsync, AsyncSeq.merge, AsyncSeq.traverseOptionAsync, AsyncSeq.traverseChoiceAsync
* Added AsyncSeq.toList, AsyncSeq.toArray, AsyncSeq.bufferByCount, AsyncSeq.unfoldAsync, AsyncSeq.concatSeq, AsyncSeq.interleave
* Copied the AsyncSeq from FSharpx.Async
* BUGFIX: AsyncSeq.skipWhile skips an extra item - <https://github.com/fsprojects/AsyncSeq/pull/2>
* BUGFIX: AsyncSeq.skipWhile skips an extra item - <https://github.com/fsprojects/AsyncSeq/pull/2>
* BUGFIX: AsyncSeq.toBlockingSeq does not hung forever if an exception is thrown and reraise it outside - <https://github.com/fsprojects/AsyncSeq/pull/21>
