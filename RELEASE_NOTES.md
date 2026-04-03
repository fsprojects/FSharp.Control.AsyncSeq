### 4.12.0

* Added `AsyncSeq.tryFindBack` — returns the last element for which the predicate returns true, or `None` if no match. Mirrors `Array.tryFindBack` / `List.tryFindBack`.
* Added `AsyncSeq.tryFindBackAsync` — async-predicate variant of `tryFindBack`.
* Added `AsyncSeq.findBack` — returns the last element for which the predicate returns true; raises `KeyNotFoundException` if no match. Mirrors `Array.findBack` / `List.findBack`.
* Added `AsyncSeq.findBackAsync` — async-predicate variant of `findBack`.
* Added `AsyncSeq.sortAsync` — asynchronous variant of `sort` returning `Async<'T[]>`, avoiding `Async.RunSynchronously` in async workflows.
* Added `AsyncSeq.sortByAsync` — asynchronous variant of `sortBy` returning `Async<'T[]>`.
* Added `AsyncSeq.sortDescendingAsync` — asynchronous variant of `sortDescending` returning `Async<'T[]>`.
* Added `AsyncSeq.sortByDescendingAsync` — asynchronous variant of `sortByDescending` returning `Async<'T[]>`.
* Added `AsyncSeq.sortWithAsync` — asynchronous variant of `sortWith` returning `Async<'T[]>`.

### 4.11.0

* Code/Performance: Modernised ~30 API functions to use `mutable` local variables instead of `ref` cells (`!`/`:=` operators). Affected: `tryLast`, `tryFirst`, `tryItem`, `compareWithAsync`, `reduceAsync`, `scanAsync`, `pairwise`, `windowed`, `pickAsync`, `tryPickAsync`, `tryFindIndex`, `tryFindIndexAsync`, `threadStateAsync`, `zipWithAsync`, `zipWithAsyncParallel`, `zipWithAsync3`, `allPairs`, `takeWhileAsync`, `takeUntilSignal`, `skipWhileAsync`, `skipWhileInclusiveAsync`, `skipUntilSignal`, `tryTail`, `splitAt`, `toArrayAsync`, `concatSeq`, `interleaveChoice`, `chunkBySize`, `chunkByAsync`, `mergeChoiceEnum`, `distinctUntilChangedWithAsync`, `emitEnumerator`, `removeAt`, `updateAt`, `insertAt`. This eliminates heap-allocated `ref`-cell objects for these variables, reducing GC pressure in hot paths, and modernises the code style to idiomatic F#.
* Performance: `mapiAsync` — replaced `asyncSeq`-builder + `collect` implementation with a direct optimised enumerator (`OptimizedMapiAsyncEnumerator`), eliminating `collect` overhead and bringing per-element cost in line with `mapAsync`. Benchmarks added in `AsyncSeqMapiBenchmarks`.
* Design parity with FSharp.Control.TaskSeq (#277, batch 2):
  * Added `AsyncSeq.tryTail` — returns `None` if the sequence is empty; otherwise returns `Some` of the tail. Safe counterpart to `tail`. Mirrors `TaskSeq.tryTail`.
  * Added `AsyncSeq.where` / `AsyncSeq.whereAsync` — aliases for `filter` / `filterAsync`, mirroring the naming convention in `TaskSeq` and F# 8 collection expressions.
  * Added `AsyncSeq.lengthBy` / `AsyncSeq.lengthByAsync` — counts elements satisfying a predicate. Mirrors `TaskSeq.lengthBy` / `TaskSeq.lengthByAsync`.
  * Added `AsyncSeq.compareWith` / `AsyncSeq.compareWithAsync` — lexicographically compares two async sequences using a comparison function. Mirrors `TaskSeq.compareWith` / `TaskSeq.compareWithAsync`.
  * Added `AsyncSeq.takeWhileInclusiveAsync` — async variant of the existing `takeWhileInclusive`. Mirrors `TaskSeq.takeWhileInclusiveAsync`.
  * Added `AsyncSeq.skipWhileInclusive` / `AsyncSeq.skipWhileInclusiveAsync` — skips elements while predicate holds and also skips the first non-matching boundary element. Mirrors `TaskSeq.skipWhileInclusive` / `TaskSeq.skipWhileInclusiveAsync`.
  * Added `AsyncSeq.appendSeq` — appends a synchronous `seq<'T>` after an async sequence. Mirrors `TaskSeq.appendSeq`.
  * Added `AsyncSeq.prependSeq` — prepends a synchronous `seq<'T>` before an async sequence. Mirrors `TaskSeq.prependSeq`.
  * Added `AsyncSeq.delay` — defers sequence creation to enumeration time by calling a factory function each time `GetAsyncEnumerator` is called. Mirrors `TaskSeq.delay`.
  * Added `AsyncSeq.collectAsync` — like `collect` but the mapping function is asynchronous (`'T -> Async<AsyncSeq<'U>>`). Mirrors `TaskSeq.collectAsync`.
  * Added `AsyncSeq.partition` / `AsyncSeq.partitionAsync` — splits a sequence into two arrays using a (optionally async) predicate; the first array contains matching elements, the second non-matching. Mirrors `TaskSeq.partition` / `TaskSeq.partitionAsync`.
* Tests: added 14 new unit tests covering previously untested functions — `AsyncSeq.indexed`, `AsyncSeq.iteriAsync`, `AsyncSeq.tryLast`, `AsyncSeq.replicateUntilNoneAsync`, and `AsyncSeq.reduceAsync` (empty-sequence edge case).

### 4.10.0

* Added `AsyncSeq.withCancellation` — returns a new `AsyncSeq` that passes the given `CancellationToken` to `GetAsyncEnumerator`, overriding whatever token would otherwise be supplied. Mirrors `TaskSeq.withCancellation` and is useful when consuming sequences from libraries (e.g. Entity Framework) that accept a cancellation token through `GetAsyncEnumerator`. Part of ongoing design-parity work with FSharp.Control.TaskSeq (see #277).

### 4.9.0

* Performance: `filterAsync` — replaced `asyncSeq`-builder implementation with a direct optimised enumerator, reducing allocation and generator overhead.
* Performance: `chooseAsync` — fallback (non-`AsyncSeqOp`) path now uses a direct optimised enumerator instead of the `asyncSeq` builder.
* Performance: `foldAsync` — fallback (non-`AsyncSeqOp`) path now uses a direct loop instead of composing `scanAsync` + `lastOrDefault`, avoiding intermediate sequence allocations.
* Performance: `take` — replaced `asyncSeq`-builder implementation with a direct optimised enumerator (`OptimizedTakeEnumerator`), eliminating generator-machinery overhead for this common slicing operation.
* Performance: `skip` — replaced `asyncSeq`-builder implementation with a direct optimised enumerator (`OptimizedSkipEnumerator`), eliminating generator-machinery overhead for this common slicing operation.
* Benchmarks: added `AsyncSeqFilterChooseFoldBenchmarks`, `AsyncSeqPipelineBenchmarks`, and `AsyncSeqSliceBenchmarks` benchmark classes.

### 4.8.0

* Added `AsyncSeq.mapFoldAsync` — maps each element using an asynchronous folder that also threads an accumulator state, returning both the array of results and the final state; mirrors `Seq.mapFold`.
* Added `AsyncSeq.mapFold` — synchronous variant of `AsyncSeq.mapFoldAsync`, mirroring `Seq.mapFold`.
* Added `AsyncSeq.allPairs` — returns an async sequence of all pairs from two input sequences (cartesian product); the second source is fully buffered before iteration, mirroring `Seq.allPairs`.
* Added `AsyncSeq.rev` — returns a new async sequence with all elements in reverse order; the entire source sequence is buffered before yielding, mirroring `Seq.rev`.

### 4.7.0

* Added `AsyncSeq.splitAt` — splits a sequence at the given index, returning the first `count` elements as an array and the remaining elements as a new `AsyncSeq`. Mirrors `Seq.splitAt`. The source is enumerated once.
* Added `AsyncSeq.removeAt` — returns a new sequence with the element at the specified index removed, mirroring `Seq.removeAt`.
* Added `AsyncSeq.updateAt` — returns a new sequence with the element at the specified index replaced by a given value, mirroring `Seq.updateAt`.
* Added `AsyncSeq.insertAt` — returns a new sequence with a value inserted before the element at the specified index (or appended if the index equals the sequence length), mirroring `Seq.insertAt`.

### 4.6.0

* Added `AsyncSeq.isEmpty` — returns `true` if the sequence contains no elements; short-circuits after the first element, mirroring `Seq.isEmpty`.
* Added `AsyncSeq.tryHead` — returns the first element as `option`, or `None` if the sequence is empty, mirroring `Seq.tryHead` (equivalent to the existing `AsyncSeq.tryFirst`).
* Added `AsyncSeq.except` — returns a new sequence excluding all elements present in a given collection, mirroring `Seq.except`.
* Added `AsyncSeq.findIndex` — returns the index of the first element satisfying a predicate; raises `KeyNotFoundException` if no match, mirroring `Seq.findIndex`.
* Added `AsyncSeq.tryFindIndex` — returns the index of the first element satisfying a predicate as `option`, or `None` if not found, mirroring `Seq.tryFindIndex`.
* Added `AsyncSeq.findIndexAsync` — async-predicate variant of `AsyncSeq.findIndex`; raises `KeyNotFoundException` if no match.
* Added `AsyncSeq.tryFindIndexAsync` — async-predicate variant of `AsyncSeq.tryFindIndex`; returns `option`.
* Added `AsyncSeq.sortWith` — sorts the sequence using a custom comparison function, returning an array, mirroring `Seq.sortWith`.

### 4.5.0

* Added `AsyncSeq.last` — returns the last element of the sequence; raises `InvalidOperationException` if empty, mirroring `Seq.last`.
* Added `AsyncSeq.item` — returns the element at the specified index; raises `ArgumentException` if out of bounds, mirroring `Seq.item`.
* Added `AsyncSeq.tryItem` — returns the element at the specified index as `option`, or `None` if the index is out of bounds, mirroring `Seq.tryItem`.

### 4.4.0

* Added `AsyncSeq.findAsync` — async-predicate variant of `AsyncSeq.find`; raises `KeyNotFoundException` if no match, mirroring `Seq.find`.
* Added `AsyncSeq.existsAsync` — async-predicate variant of `AsyncSeq.exists`, mirroring `Seq.exists`.
* Added `AsyncSeq.forallAsync` — async-predicate variant of `AsyncSeq.forall`, mirroring `Seq.forall`.

### 4.3.0

* Added `AsyncSeq.head` — returns the first element of the sequence; raises `InvalidOperationException` if empty, mirroring `Seq.head`.
* Added `AsyncSeq.iteri` — iterates the sequence calling a synchronous action with each element's index, mirroring `Seq.iteri`.
* Added `AsyncSeq.find` — returns the first element satisfying a predicate; raises `KeyNotFoundException` if no match, mirroring `Seq.find`.
* Added `AsyncSeq.tryFindAsync` — async-predicate variant of `AsyncSeq.tryFind`, returning the first matching element as `option`.
* Added `AsyncSeq.tail` — returns the sequence without its first element, mirroring `Seq.tail`.

### 4.2.0

* Added `AsyncSeq.zip3`, `AsyncSeq.zipWith3`, and `AsyncSeq.zipWithAsync3` — combinators for zipping three async sequences, mirroring `Seq.zip3` ([PR #254](https://github.com/fsprojects/FSharp.Control.AsyncSeq/pull/254)).

### 4.1.0

async sequence ([PR #241](https://github.com/fsprojects/FSharp.Control.AsyncSeq/pull/241)).
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
