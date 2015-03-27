// ----------------------------------------------------------------------------
// F# async extensions
// (c) Tomas Petricek, David Thomas 2012, Available under Apache 2.0 license.
// ----------------------------------------------------------------------------
namespace FSharp.Control
open System
open System.Threading
open System.Threading.Tasks

// ----------------------------------------------------------------------------

module internal AsyncOps =
      
  let unit : Async<unit> = async.Return()

  let never : Async<unit> = Async.Sleep -1
 

[<AutoOpen>]
module internal AsyncExtensions =             
    type Microsoft.FSharp.Control.Async with       
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


      /// Creates an async computation which maps a function f over the 
      /// value produced by the specified asynchronous computation.
      static member map f a = async.Bind(a, f >> async.Return)

      /// Creates an async computation which binds the result of the specified 
      /// async computation to the specified function. The computation produced 
      /// by the specified function is returned.
      static member bind f a = async.Bind(a, f)

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