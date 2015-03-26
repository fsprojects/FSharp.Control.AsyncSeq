### 1.11.1 - 26.03.2015
* BUGFIX: AsyncSeq.toBlockingSeq does not hung forever if an exception is thrown and reraise it outside - https://github.com/fsprojects/FSharpx.Async/pull/21

### 1.11.0 - 27.02.2015
* Added Async.map, Async.bind, Async.unit
* Added AsyncSeq.toList, AsyncSeq.toArray, AsyncSeq.bufferByCount, AsyncSeq.unfoldAsync, AsyncSeq.concatSeq, AsyncSeq.interleave

### 1.10.0 - 25.02.2015
* Use Paket instead of NuGet

### 1.9.9 - 23.02.2015
* BUGFIX: AsyncSeq.skipWhile skips an extra item - https://github.com/fsprojects/FSharpx.Async/pull/2
 
### 1.9.9 - 23.02.2015
* Copied the async helpers from FSharpx
* BUGFIX: AsyncSeq.skipWhile skips an extra item - https://github.com/fsprojects/FSharpx.Async/pull/2
