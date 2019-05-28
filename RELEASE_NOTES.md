### 2.0.22 - 28.05.2019
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
* Improve performance of internal Async.chooseTasks function which improves performance of AsyncSeq.bufferByCountAndTime, etc (https://github.com/fsprojects/FSharp.Control.AsyncSeq/pull/73)

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
* AsyncSeq got extracted as separate project and is now a dependency - https://github.com/fsprojects/FSharpx.Async/pull/24 

### 1.13 - 27.03.2015
* Renamed to FSharp.Control.AsyncSeq
* Remove surface area
* Hide Nil/Cons from representation of AsyncSeq 

### 1.12.1 - 27.03.2015
* Added Async.bindChoice, Async.ParallelIgnore, AsyncSeq.zipWithAsync, AsyncSeq.zappAsync, AsyncSeq.threadStateAsync, AsyncSeq.merge, AsyncSeq.traverseOptionAsync, AsyncSeq.traverseChoiceAsync
* Added AsyncSeq.toList, AsyncSeq.toArray, AsyncSeq.bufferByCount, AsyncSeq.unfoldAsync, AsyncSeq.concatSeq, AsyncSeq.interleave
* Copied the AsyncSeq from FSharpx.Async
* BUGFIX: AsyncSeq.skipWhile skips an extra item - https://github.com/fsprojects/AsyncSeq/pull/2
* BUGFIX: AsyncSeq.skipWhile skips an extra item - https://github.com/fsprojects/AsyncSeq/pull/2
* BUGFIX: AsyncSeq.toBlockingSeq does not hung forever if an exception is thrown and reraise it outside - https://github.com/fsprojects/AsyncSeq/pull/21
