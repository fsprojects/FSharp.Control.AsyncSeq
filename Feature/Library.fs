[<AutoOpen>]
module ChannelExtention
    open System.Threading
    open System.Threading.Channels
    open FSharp.Control

    open LanguagePrimitives

    let inline (|AbsPosRangeByLength|_|) (start,length) = if start >= GenericZero && length > GenericZero then Some (start,length) else None
    let inline private AsyncSequenceProduce (dataChannel:Channel<'a>) producer source =
        async{
            let Mutex = new Mutex();
            let mutable Completed = GenericZero;
            let mutable Schedueled = GenericZero;
            let mutable HasElement = false;
            source
            |> AsyncSeq.iter (fun x->
                HasElement <- true
                Schedueled <- Checked.(+) Schedueled GenericOne
                let lc_x = x
                async{
                    try
                        let! value = producer(lc_x)
                        let! _ = dataChannel.Writer.WriteAsync(value).AsTask() |> Async.AwaitTask
                        ()
                    finally
                        Mutex.WaitOne() |> ignore
                        Completed <- Completed + GenericOne
                        Mutex.ReleaseMutex()
                        if Completed = Schedueled then dataChannel.Writer.Complete()
                } |> Async.Start)
            |> Async.RunSynchronously
            if not HasElement then dataChannel.Writer.Complete()
        } |> Async.Start
        dataChannel.Reader.ReadAllAsync() |> toAsyncSeq

    let mapAsyncUnorderedParallel transformer sequence = (AsyncSequenceProduce (Helper.CreateChannel.CreateUnbounded())) transformer sequence
