[<AutoOpen>]
module IAsyncEnumerableInterOp
    open System
    open FSharp.Control
    let toAsyncSeq<'t> (xs:System.Collections.Generic.IAsyncEnumerable<'t>) =
        {new FSharp.Control.IAsyncEnumerable<'t> with
            member _.GetEnumerator() =        
                let aeEnum = xs.GetAsyncEnumerator()
                {
                    new FSharp.Control.IAsyncEnumerator<'t> with 
                        member _.MoveNext() = 
                            async {
                                let! haveData = aeEnum.MoveNextAsync().AsTask() |> Async.AwaitTask
                                if haveData then return Some aeEnum.Current else return None
                            }

                    interface IDisposable with
                        member _.Dispose() = 
                            aeEnum.DisposeAsync().AsTask() 
                            |> Async.AwaitTask                            
                            |> Async.RunSynchronously
                }
        }

    let AsAsyncSeq (source:seq<'a>) = asyncSeq { for i in source do i}
