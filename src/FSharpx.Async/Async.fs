// ----------------------------------------------------------------------------
// F# async extensions
// (c) Tomas Petricek, David Thomas 2012, Available under Apache 2.0 license.
// ----------------------------------------------------------------------------
namespace FSharpx.Control
open System
open System.Threading

// ----------------------------------------------------------------------------

module AsyncOps =
      
  let unit : Async<unit> = async.Return()
 

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

      /// An async computation which does nothing.
      static member inline unit = AsyncOps.unit

      /// Creates an async computation which maps a function f over the 
      /// value produced by the specified asynchronous computation.
      static member inline map f a = async.Bind(a, f >> async.Return)

      /// Creates an async computation which binds the result of the specified 
      /// async computation to the specified function. The computation produced 
      /// by the specified function is returned.
      static member inline bind f a = async.Bind(a, f)