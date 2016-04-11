#r @"../../bin/FSharp.Control.AsyncSeq.dll"
#r @"../../packages/NUnit/lib/nunit.framework.dll"
#time "on"
//#load "AsyncSeqTests.fs"

open FSharp.Control

let generator state =
    async {
        if state < 10000 then
            return Some ((), state + 1)
        else
            return None
    }

AsyncSeq.unfoldAsync generator 0
|> AsyncSeq.iter ignore
|> Async.RunSynchronously


//AsyncSeqTests.``AsyncSeq.unfoldAsync should be iterable in finite resources``()