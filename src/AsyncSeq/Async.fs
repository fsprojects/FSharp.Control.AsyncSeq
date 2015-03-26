// ----------------------------------------------------------------------------
// F# async extensions
// (c) Tomas Petricek, David Thomas 2012, Available under Apache 2.0 license.
// ----------------------------------------------------------------------------
namespace FSharpx.Control
open System
open System.Threading
open System.Threading.Tasks

// ----------------------------------------------------------------------------

module AsyncOps =
      
  let unit : Async<unit> = async.Return()

  let never : Async<unit> = Async.Sleep -1
 

[<AutoOpen>]
module AsyncExtensions =             
    type Microsoft.FSharp.Control.Async with       
      /// Creates an asynchronous workflow that runs the asynchronous workflow
      /// given as an argument at most once. When the returned workflow is 
      /// started for the second time, it reuses the result of the 
      /// previous execution.
      static member Cache (input:Async<'T>) = 
          let agent = Agent<AsyncReplyChannel<_>>.Start(fun agent -> async {
              let! repl = agent.Receive()
              let! res = input
              repl.Reply(res)
              while true do
                  let! repl = agent.Receive()
                  repl.Reply(res) })

          async { return! agent.PostAndAsyncReply(id) }

      /// Starts the specified operation using a new CancellationToken and returns
      /// IDisposable object that cancels the computation. This method can be used
      /// when implementing the Subscribe method of IObservable interface.
      static member StartDisposable(op:Async<unit>) =
          let ct = new System.Threading.CancellationTokenSource()
          Async.Start(op, ct.Token)
          { new IDisposable with 
              member x.Dispose() = ct.Cancel() }

      /// Creates an async computations which runs the specified computations
      /// in parallel and returns their results.
      static member Parallel(a:Async<'a>, b:Async<'b>) : Async<'a * 'b> = async {
        let! a = a |> Async.StartChild
        let! b = b |> Async.StartChild
        let! a = a
        let! b = b
        return a,b }

      /// Creates an async computations which runs the specified computations
      /// in parallel and returns their results.
      static member Parallel(a:Async<'a>, b:Async<'b>, c:Async<'c>) : Async<'a * 'b * 'c> = async {
        let! a = a |> Async.StartChild
        let! b = b |> Async.StartChild
        let! c = c |> Async.StartChild
        let! a = a
        let! b = b
        let! c = c
        return a,b,c }

      /// Creates an async computation which runs the provided sequence of computations and completes
      /// when all computations in the sequence complete. Up to parallelism computations will
      /// be in-flight at any given point in time. Error or cancellation of any computation in
      /// the sequence causes the resulting computation to error or cancel, respectively.
      static member ParallelIgnore (parallelism:int) (xs:seq<Async<_>>) = async {
                
        let sm = new SemaphoreSlim(parallelism)
        let cde = new CountdownEvent(1)
        let tcs = new TaskCompletionSource<unit>()
    
        let inline ok _ =
          sm.Release() |> ignore
          if (cde.Signal()) then
            tcs.SetResult(())
                
        let inline err (ex:exn) =
          tcs.SetException ex
          sm.Release() |> ignore
                
        let inline cnc (ex:OperationCanceledException) =      
          tcs.SetCanceled()
          sm.Release() |> ignore

        try

          for computation in xs do
            sm.Wait()
            cde.AddCount(1)            
            // the following decreases throughput 3x but avoids blocking
            // do! sm.WaitAsync() |> Async.AwaitTask
            Async.StartWithContinuations(computation, ok, err, cnc)                    
      
          if (cde.Signal()) then
            tcs.SetResult(())

          do! tcs.Task |> Async.AwaitTask

        finally
      
          cde.Dispose()    
          sm.Dispose()
                          
        }

      /// An async computation which does nothing and completes immediatly.
      static member inline unit = AsyncOps.unit

      /// An async computation which does nothing and never completes.
      static member inline never = AsyncOps.never

      /// Creates an async computation which maps a function f over the 
      /// value produced by the specified asynchronous computation.
      static member inline map f a = async.Bind(a, f >> async.Return)

      /// Creates an async computation which binds the result of the specified 
      /// async computation to the specified function. The computation produced 
      /// by the specified function is returned.
      static member inline bind f a = async.Bind(a, f)

      /// Maps over an async computation which produces a choice value
      /// using a function which maps over Choice1Of2 and itself returns a choice. 
      /// A value of Choice2Of2 is treated like an error and passed through.
      static member mapChoice (f:'a -> Choice<'b, 'e>) (a:Async<Choice<'a, 'e>>) : Async<Choice<'b, 'e>> = 
        a |> Async.map (function 
          | Choice1Of2 a' -> f a'
          | Choice2Of2 e -> Choice2Of2 e)

      /// Binds an async computation producing a choice value to another async
      /// computation producing a choice such that a Choice2Of2 value is passed through.
      static member bindChoice (f:'a -> Async<Choice<'b, 'e>>) (a:Async<Choice<'a, 'e>>) : Async<Choice<'b, 'e>> = 
        a |> Async.bind (function
          | Choice1Of2 a' -> f a'
          | Choice2Of2 e ->  Choice2Of2 e |> async.Return)

      /// Binds an async computation producing a choice value to another async
      /// computation producing a choice such that a Choice2Of2 value is passed through.
      static member bindChoices (f:'a -> Async<Choice<'b, 'e2>>) (a:Async<Choice<'a, 'e1>>) : Async<Choice<'b, Choice<'e1, 'e2>>> = 
        a |> Async.bind (function
          | Choice1Of2 a' -> f a' |> Async.map (function Choice1Of2 b -> Choice1Of2 b | Choice2Of2 e2 -> Choice2Of2 (Choice2Of2 e2))
          | Choice2Of2 e1 -> Choice2Of2 (Choice1Of2 e1) |> async.Return)

      /// Creates a computation which produces a tuple consiting of the value produces by the first
      /// argument computation to complete and a handle to the other computation. The second computation
      /// to complete is memoized.
      static member internal chooseBoth (a:Async<'a>) (b:Async<'a>) : Async<'a * Async<'a>> =
        Async.FromContinuations <| fun (ok,err,cnc) ->
          let state = ref 0            
          let tcs = TaskCompletionSource<'a>()            
          let inline ok a =
            if (Interlocked.CompareExchange(state, 1, 0) = 0) then
              ok (a, tcs.Task |> Async.AwaitTask)
            else
              tcs.SetResult a
          let inline err (ex:exn) =
            if (Interlocked.CompareExchange(state, 1, 0) = 0) then err ex
            else tcs.SetException ex
          let inline cnc ex =
            if (Interlocked.CompareExchange(state, 1, 0) = 0) then cnc ex
            else tcs.SetCanceled()
          Async.StartWithContinuations(a, ok, err, cnc)
          Async.StartWithContinuations(b, ok, err, cnc)