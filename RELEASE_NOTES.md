### 1.16.0 - 13.05.2015
* Simplify ofObservableBuffered and toBlockingSeq

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