### 1.12.2 - 27.03.2015
* Renamed to FSharp.Control.AsyncSeq

### 1.12.1 - 27.03.2015
* Added Async.bindChoice, Async.ParallelIgnore, AsyncSeq.zipWithAsync, AsyncSeq.zappAsync, AsyncSeq.threadStateAsync, AsyncSeq.merge, AsyncSeq.traverseOptionAsync, AsyncSeq.traverseChoiceAsync
* Added AsyncSeq.toList, AsyncSeq.toArray, AsyncSeq.bufferByCount, AsyncSeq.unfoldAsync, AsyncSeq.concatSeq, AsyncSeq.interleave
* Copied the AsyncSeq from FSharpx.Async
* BUGFIX: AsyncSeq.skipWhile skips an extra item - https://github.com/fsprojects/AsyncSeq/pull/2
* BUGFIX: AsyncSeq.skipWhile skips an extra item - https://github.com/fsprojects/AsyncSeq/pull/2
* BUGFIX: AsyncSeq.toBlockingSeq does not hung forever if an exception is thrown and reraise it outside - https://github.com/fsprojects/AsyncSeq/pull/21