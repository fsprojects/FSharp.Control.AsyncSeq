// ----------------------------------------------------------------------------
// F# async extensions (AsyncSeq.fs)
// (c) Tomas Petricek, 2011, Available under Apache 2.0 license.
// ----------------------------------------------------------------------------
namespace FSharp.Control

open System
open System.Diagnostics
open System.Collections.Generic
open System.Threading
open System.Threading.Tasks
open System.Runtime.ExceptionServices
open System.Linq

#nowarn "40" "3218"

// ----------------------------------------------------------------------------

type AsyncSeq<'T> = IAsyncEnumerable<'T>

//#if !FABLE_COMPILER
//type AsyncSeqSrc<'T> = private { tail : AsyncSeqSrcNode<'T> ref }

//and private AsyncSeqSrcNode<'T> =
//  val tcs : TaskCompletionSource<('T * AsyncSeqSrcNode<'T>) option>
//  new (tcs) = { tcs = tcs }
//#endif

[<AutoOpen>]
module internal Utils =

    [<RequireQualifiedAccess>]
    module Choice =

        /// Maps over the left result type.
        let mapl (f:'T -> 'U) input =
            match input with
            | Choice1Of2 a -> f a |> Choice1Of2
            | Choice2Of2 e -> Choice2Of2 e

    type IAsyncDisposable with
        member d.DisposeSynchronously() =
            { new IDisposable with member _.Dispose() = d.DisposeAsync().AsTask().Wait() }

    type AsyncEnumeratorSynchronousDisposable<'T>(d: IAsyncEnumerator<'T>) =
        member _.MoveNextAsync() = d.MoveNextAsync()
        member _.Current = d.Current
        interface IAsyncEnumerator<'T> with
            member _.MoveNextAsync() = d.MoveNextAsync()
            member _.Current = d.Current

        interface IAsyncDisposable with
            member _.DisposeAsync() = d.DisposeAsync()

        interface IDisposable with
            member _.Dispose() = d.DisposeAsync().AsTask().Wait() 

    type IAsyncEnumerator<'T> with
        member d.DisposeSynchronously() = new AsyncEnumeratorSynchronousDisposable<'T>(d)

    module Disposable =

        let empty : IDisposable =
            { new IDisposable with member _.Dispose () = () }

    // ----------------------------------------------------------------------------

    #if FABLE_COMPILER
    type ExceptionDispatchInfo private (err: exn) =
        member _.SourceException = err
        member _.Throw () = raise err
        static member Capture (err: exn) = ExceptionDispatchInfo(err)
        static member Throw (err: exn) = raise err
    #endif

    [<RequireQualifiedAccess>]
    module Observable =

        /// Union type that represents different messages that can be sent to the
        /// IObserver interface. The IObserver type is equivalent to a type that has
        /// just OnNext method that gets 'ObservableUpdate' as an argument.
        type internal ObservableUpdate<'T> =
            | Next of 'T
            | Error of ExceptionDispatchInfo
            | Completed

        /// Turns observable into an observable that only calls OnNext method of the
        /// observer, but gives it a discriminated union that represents different
        /// kinds of events (error, next, Complered2)
        let asUpdates (source:IObservable<'T>) =
            { new IObservable<_> with
                member x.Subscribe(observer) =
                  source.Subscribe
                    ({ new IObserver<_> with
                        member x.OnNext(v) = observer.OnNext(Next v)
                        member x.OnCompleted() = observer.OnNext(Completed)
                        member x.OnError(e) = observer.OnNext(Error (ExceptionDispatchInfo.Capture e)) }) }

    
    type Async with

        static member bind (f:'T -> Async<'b>) (a:Async<'T>) : Async<'b> = async.Bind(a, f)

        #if !FABLE_COMPILER
        static member awaitTaskUnitCancellationAsError (t:Task) : Async<unit> =
            Async.FromContinuations <| fun (ok,err,_) ->
                t.ContinueWith (fun (t:Task) ->
                    if t.IsFaulted then err t.Exception
                    elif t.IsCanceled then err (OperationCanceledException("Task wrapped with Async has been cancelled."))
                    elif t.IsCompleted then ok ()
                    else failwith "invalid Task state!") |> ignore

        static member awaitTaskCancellationAsError (t:Task<'T>) : Async<'T> =
            Async.FromContinuations <| fun (ok,err,_) ->
                t.ContinueWith (fun (t:Task<'T>) ->
                    if t.IsFaulted then err t.Exception
                    elif t.IsCanceled then err (OperationCanceledException("Task wrapped with Async has been cancelled."))
                    elif t.IsCompleted then ok t.Result
                    else failwith "invalid Task state!") |> ignore
      #endif

        static member map f a = async.Bind(a, f >> async.Return)

    type Task with

        /// Starts the specified operation using a new CancellationToken and returns
        /// IDisposable object that cancels the computation. This method can be used
        /// when implementing the Subscribe method of IObservable interface.
        static member StartDisposable(op:CancellationToken -> Task<unit>) =
            let ct = new System.Threading.CancellationTokenSource()
            Async.Start(async { return! Async.AwaitTask(op ct.Token) })
            { new IDisposable with
                member x.Dispose() = ct.Cancel() }

    #if !FABLE_COMPILER
        static member internal chooseTasks (a:Task<'T>) (b:Task<'U>) : Task<Choice<'T * Task<'U>, 'U * Task<'T>>> =
            task {
                let ta, tb = a :> Task, b :> Task
                let! i = Task.WhenAny( ta, tb ) |> Async.AwaitTask
                if i = ta then return (Choice1Of2 (a.Result, b))
                elif i = tb then return (Choice2Of2 (b.Result, a))
               else return! failwith "unreachable"
            }

        static member internal chooseTasks2 (a:Task<'T>) (b:Task) : Task<Choice<'T * Task, Task<'T>>> =
            task {
                let ta = a :> Task
                let! i = Task.WhenAny( ta, b ) |> Async.AwaitTask
                if i = ta then return (Choice1Of2 (a.Result, b))
                elif i = b then return (Choice2Of2 (a))
                else return! failwith "unreachable"
            }

    type MailboxProcessor<'Msg> with
        member this.PostAndAsyncReplyTask (f:TaskCompletionSource<'T> -> 'Msg) : Task<'T> =
            let tcs = new TaskCompletionSource<'T>()
            this.Post (f tcs)
            tcs.Task

    [<RequireQualifiedAccess>]
    module Task =

      let map f (t: Task<_>) = task { let! v = t in return f v }

      let inline join (t:Task<Task<'T>>) : Task<'T> =
          t.Unwrap()

      let inline extend (f:Task<'T> -> 'b) (t:Task<'T>) : Task<'b> =
          t.ContinueWith f

      let chooseTask (t:Task<'T>) (a:Task<'T>) = 
          Task.WhenAny(t, a).Unwrap() 

      let toUnit (t:Task) : Task<unit> =
          t.ContinueWith (Func<_, _>(fun (_:Task) -> ()))

      let taskFault (t:Task<'T>) : Task<'TFault> =
        t
        |> extend (fun t ->
          let ivar = TaskCompletionSource<_>()
          if t.IsFaulted then
            ivar.SetException t.Exception
          else if t.IsCanceled then
            ivar.SetCanceled()
          ivar.Task)
        |> join
    #endif

// via: https://github.com/fsharp/fsharp/blob/master/src/fsharp/FSharp.Core/seq.fs
module AsyncGenerator =

    type Step<'T> =
        | Stop
        | Yield of 'T
        /// Jump to another generator.
        | Goto of AsyncGenerator<'T>

    and AsyncGenerator<'T> =
        abstract Apply : unit -> Task<Step<'T>>
        abstract Disposer : (unit -> Task) option

    let disposeG (g:AsyncGenerator<'T>) =
        match g.Disposer with
        | None -> ()
        | Some f -> f().Wait() // TODO: this runs synchronously

    let appG (g:AsyncGenerator<'T>) =
        task {
            let! res = g.Apply ()
            match res with
            | Goto next -> return Goto next
            | Yield _ -> return res
            | Stop ->
                disposeG g
                return res
          }

    type GenerateCont<'T> (generator: AsyncGenerator<'T>, cont: unit -> AsyncGenerator<'T>) =
        member _.Generator = generator
        member _.Cont = cont
        interface AsyncGenerator<'T> with
            member _.Apply () =
                task {
                    let! step = appG generator
                    match step with
                    | Stop -> return Goto (cont ())
                    | Yield _ as res -> return res
                    | Goto next -> return Goto (GenerateCont<_>.Bind (next, cont))
                }

            member _.Disposer =
                generator.Disposer
              
        static member Bind (g: AsyncGenerator<'T>, cont:unit -> AsyncGenerator<'T>) : AsyncGenerator<'T> =
            match g with
            | :? GenerateCont<'T> as g -> GenerateCont<_>.Bind (g.Generator, (fun () -> GenerateCont<_>.Bind (g.Cont(), cont)))
            | _ -> (new GenerateCont<'T>(g, cont) :> AsyncGenerator<'T>)

    /// Right-associating binder.
    let bindG (g:AsyncGenerator<'T>) (cont:unit -> AsyncGenerator<'T>) : AsyncGenerator<'T> =
        GenerateCont<_>.Bind (g, cont)

    /// Converts a generator to an enumerator.
    /// The generator can point to another generator using Goto, in which case
    /// the enumerator mutates its current generator and continues.
    type AsyncGeneratorEnumerator<'T> (g:AsyncGenerator<'T>) =
        let mutable g = g
        let mutable current = Unchecked.defaultof<'T>
        let mutable fin = false
        member _.Generator = g
        interface IAsyncEnumerator<'T> with
            member x.MoveNextAsync () =
                task {
                    let! step = appG g
                    match step with
                    | Stop ->
                        fin <- true
                        return false
                    | Yield a ->
                        current <- a
                        return true
                    | Goto next ->
                        g <- next
                        return! (x :> IAsyncEnumerator<'T>).MoveNextAsync()
                }
                |> ValueTask<bool>
        member _.Dispose () =
            disposeG g

    /// Converts an enumerator to a generator.
    /// The resulting generator will either yield or stop.
    type AsyncEnumeratorGenerator<'T> (enum:IAsyncEnumerator<'T>) =
        member _.Enumerator = enum
        interface AsyncGenerator<'T> with
            member _.Apply () = task {
                let! next = enum.MoveNextAsync()
                match next with
                | true ->
                  return Yield enum.Current
                | false ->
                  return Stop }
            member _.Disposer = Some ((fun () -> (enum :> IAsyncDisposable).DisposeAsync().AsTask()))

    let enumeratorFromGenerator (g:AsyncGenerator<'T>) : IAsyncEnumerator<'T> =
        match g with
        | :? AsyncEnumeratorGenerator<'T> as g -> g.Enumerator
        | _ -> (new AsyncGeneratorEnumerator<_>(g) :> _)
 
    let generatorFromEnumerator (e:IAsyncEnumerator<'T>) : AsyncGenerator<'T> =
        match e with
        | :? AsyncGeneratorEnumerator<'T> as e -> e.Generator
        | _ -> (new AsyncEnumeratorGenerator<_>(e) :> _)

    let delay (f:unit -> AsyncSeq<'T>) : AsyncSeq<'T> =
        { new IAsyncEnumerable<'T> with
            member x.GetAsyncEnumerator(ct) = f().GetAsyncEnumerator(ct) }

    let emitEnum (e:CancellationToken -> IAsyncEnumerator<'T>) : AsyncSeq<'T> =
        { new IAsyncEnumerable<_> with
            member _.GetAsyncEnumerator ct = e ct }

    let fromGeneratorDelay (f:CancellationToken -> AsyncGenerator<'T>) : AsyncSeq<'T> =
        delay (fun () -> emitEnum (fun ct -> enumeratorFromGenerator (f ct)))

    let toGenerator ct (s:AsyncSeq<'T>) : AsyncGenerator<'T> =
        generatorFromEnumerator (s.GetAsyncEnumerator(ct))

    let append (s1:AsyncSeq<'T>) (s2:AsyncSeq<'T>) : AsyncSeq<'T> =
        fromGeneratorDelay (fun ct -> bindG (toGenerator ct s1) (fun () -> toGenerator ct s2))

    //[<AbstractClass>]
    //type AsyncSeqOp<'T> () =
    //    abstract member ChooseAsync : ('T -> Task<'U option>) -> AsyncSeq<'U>
    //    abstract member FoldAsync : ('S -> 'T -> Task<'S>) -> 'S -> Task<'S>
    //    abstract member MapAsync : ('T -> Task<'U>) -> AsyncSeq<'U>
    //    abstract member IterAsync : ('T -> Task<unit>) -> Task<unit>
    //    default x.MapAsync (f:'T -> Task<'U>) : AsyncSeq<'U> =
    //      x.ChooseAsync (f >> Task.map Some)
    //    default x.IterAsync (f:'T -> Task<unit>) : Task<unit> =
    //      x.FoldAsync (fun () t -> f t) ()

    //[<AutoOpen>]
    //module AsyncSeqOp =

    //  type UnfoldAsyncEnumerator<'S, 'T> (f:'S -> Task<('T * 'S) option>, init:'S) =
    //    inherit AsyncSeqOp<'T> ()

    //    override x.IterAsync g = task {
    //      let rec go s = task {
    //        let! next = f s
    //        match next with
    //        | None -> return ()
    //        | Some (t,s') ->
    //          do! g t
    //            // TODO: tailcall
    //          return! go s' }
    //      return! go init }

    //    override _.FoldAsync (g:'S2 -> 'T -> Task<'S2>) (init2:'S2) = task {
    //      let rec go s s2 = task {
    //        let! next = f s
    //        match next with
    //        | None -> return s2
    //        | Some (t,s') ->
    //          let! s2' = g s2 t
    //            // TODO: tailcall
    //          return! go s' s2' }
    //      return! go init init2 }

    //    override _.ChooseAsync (g:'T -> Task<'U option>) : AsyncSeq<'U> =
    //      let rec h s = task {
    //        let! res = f s
    //        match res with
    //        | None ->
    //          return None
    //        | Some (t,s) ->
    //          let! res' = g t
    //          match res' with
    //          | Some u ->
    //            return Some (u, s)
    //          | None ->
    //            // TODO: tailcall
    //            return! h s }
    //      new UnfoldAsyncEnumerator<'S, 'U> (h, init) :> _

    //    override _.MapAsync (g:'T -> Task<'U>) : AsyncSeq<'U> =
    //      let h s = task {
    //        let! r = f s
    //        match r with
    //        | Some (t,s) ->
    //          let! u = g t
    //          return Some (u,s)
    //        | None ->
    //          return None }
    //      new UnfoldAsyncEnumerator<'S, 'U> (h, init) :> _

    //    interface IAsyncEnumerable<'T> with
    //      member _.GetAsyncEnumerator(ct) =
    //        let mutable s = init
    //        let mutable current = Unchecked.defaultof<_>
    //        { new IAsyncEnumerator<'T> with
    //            member _.MoveNextAsync () : ValueTask<bool> =
    //                task {
    //                    let! next = f s
    //                    match next with
    //                    | None ->
    //                      return false
    //                    | Some (a,s') ->
    //                      s <- s'
    //                      current <- a
    //                      return true
    //                } |> ValueTask<bool>

    //            member _.Current = current 
    //            member _.DisposeAsync () = ValueTask() }

  /// Module with helper functions for working with asynchronous sequences
  module AsyncSeq =

      let inline dispose (d:System.IDisposable) = try d.Dispose() with _ -> ()

      [<GeneralizableValue>]
      let empty<'T> : AsyncSeq<'T> =
            { new IAsyncEnumerable<'T> with
                  member x.GetAsyncEnumerator(ct) =
                      { new IAsyncEnumerator<'T> with
                            member x.Current = invalidOp "no elements"
                            member x.MoveNextAsync() = ValueTask<_>(false)
                            member x.DisposeAsync() = ValueTask() } }

      let singleton (v:'T) : AsyncSeq<'T> =
            { new IAsyncEnumerable<'T> with
                  member x.GetAsyncEnumerator(ct) =
                      let state = ref None
                      { new IAsyncEnumerator<'T> with
                            member x.Current = invalidOp "no elements"
                            member x.MoveNextAsync() =
                                task {
                                    let res = (!state).IsNone
                                    state.Value <- Some v
                                    return res
                                } |> ValueTask<bool>

                            member x.DisposeAsync() = ValueTask() } }

      let append (inp1: AsyncSeq<'T>) (inp2: AsyncSeq<'T>) : AsyncSeq<'T> =
        AsyncGenerator.append inp1 inp2

      let inline delay (f: unit -> AsyncSeq<'T>) : AsyncSeq<'T> =
        AsyncGenerator.delay f

      let bindValueTask (f:'T -> AsyncSeq<'U>) (inp: ValueTask<'T>) : AsyncSeq<'U> =
        { new IAsyncEnumerable<'U> with
            member x.GetAsyncEnumerator(ct) =
              { new AsyncGenerator.AsyncGenerator<'U> with
                  member x.Apply () = task {
                      let! v = inp
                      let cont =
                        (f v).GetAsyncEnumerator(ct)
                        |> AsyncGenerator.generatorFromEnumerator
                      return AsyncGenerator.Goto cont
                    }
                  member x.Disposer = None
              }
              |> AsyncGenerator.enumeratorFromGenerator
        }

      let bindTask (f:'T -> AsyncSeq<'U>) (inp: Task<'T>) : AsyncSeq<'U> =
        { new IAsyncEnumerable<'U> with
            member x.GetAsyncEnumerator(ct) =
              { new AsyncGenerator.AsyncGenerator<'U> with
                  member x.Apply () = task {
                      let! v = inp
                      let cont =
                        (f v).GetAsyncEnumerator(ct)
                        |> AsyncGenerator.generatorFromEnumerator
                      return AsyncGenerator.Goto cont
                    }
                  member x.Disposer = None
              }
              |> AsyncGenerator.enumeratorFromGenerator
        }

      let bindAsync (f:'T -> AsyncSeq<'U>) (inp:Async<'T>) : AsyncSeq<'U> =
        { new IAsyncEnumerable<'U> with
            member x.GetAsyncEnumerator(ct) =
              { new AsyncGenerator.AsyncGenerator<'U> with
                  member _.Apply () =
                      task {
                          let! v = Async.StartImmediateAsTask(inp, cancellationToken=ct)
                          let cont =
                            (f v).GetAsyncEnumerator(ct)
                            |> AsyncGenerator.generatorFromEnumerator
                          return AsyncGenerator.Goto cont
                      }
                  member _.Disposer = None
              }
              |> AsyncGenerator.enumeratorFromGenerator
        }

      type AsyncSeqCancellationToken = | AsyncSeqCancellationToken

      let getCancellationToken() = AsyncSeqCancellationToken

      let bindCancellationToken (body: CancellationToken -> AsyncSeq<'U>): AsyncSeq<'U> =
        { new IAsyncEnumerable<'U> with
            member _.GetAsyncEnumerator(ct) = body(ct).GetAsyncEnumerator(ct) }

      [<RequireQualifiedAccess>]
      type TryWithState<'T> =
         | NotStarted of AsyncSeq<'T>
         | HaveBodyEnumerator of IAsyncEnumerator<'T>
         | HaveHandlerEnumerator of IAsyncEnumerator<'T>
         | Finished

      /// Implements the 'TryWith' functionality for computation builder
      let tryWith (inp: AsyncSeq<'T>) (handler : exn -> AsyncSeq<'T>) : AsyncSeq<'T> =
          // Note: this is put outside the object deliberately, so the object doesn't permanently capture inp1 and inp2
          { new IAsyncEnumerable<'T> with
              member _.GetAsyncEnumerator(ct) =
                  let mutable state = TryWithState.NotStarted inp
                  { new IAsyncEnumerator<'T> with

                      member _.Current =
                          match state with
                          | TryWithState.NotStarted _ -> invalidOp "not started"
                          | TryWithState.HaveBodyEnumerator ie -> ie.Current
                          | TryWithState.HaveHandlerEnumerator ie -> ie.Current
                          | TryWithState.Finished _ -> invalidOp "finished"                           

                      member x.MoveNextAsync() =
                          task {
                              match state with
                              | TryWithState.NotStarted inp ->
                                  let mutable res = Unchecked.defaultof<_>
                                  try
                                      res <- Choice1Of2 (inp.GetAsyncEnumerator(ct))
                                  with exn ->
                                      res <- Choice2Of2 exn
                                  match res with
                                  | Choice1Of2 r ->
                                      state <- TryWithState.HaveBodyEnumerator r
                                      return! x.MoveNextAsync()
                                  | Choice2Of2 exn ->
                                      do! x.DisposeAsync()
                                      let enum = (handler exn).GetAsyncEnumerator(ct)
                                      state <- TryWithState.HaveHandlerEnumerator enum
                                      return! x.MoveNextAsync()
                              | TryWithState.HaveBodyEnumerator e ->
                                  let mutable res = Unchecked.defaultof<_>
                                  try
                                      let! r = e.MoveNextAsync()
                                      res <- Choice1Of2 r
                                  with exn ->
                                      res <- Choice2Of2 exn
                                  match res with
                                  | Choice1Of2 res ->
                                      if not res then 
                                          do! x.DisposeAsync()
                                      return res
                                  | Choice2Of2 exn ->
                                      do! x.DisposeAsync()
                                      let e = (handler exn).GetAsyncEnumerator(ct)
                                      state <- TryWithState.HaveHandlerEnumerator e
                                      return! x.MoveNextAsync()
                              | TryWithState.HaveHandlerEnumerator e ->
                                  let! res = e.MoveNextAsync()
                                  if not res then 
                                      do! x.DisposeAsync()
                                  return false
                              | _ ->
                                  return false }
                          |> ValueTask<bool>

                      member _.DisposeAsync() =
                          task {
                              match state with
                              | TryWithState.HaveBodyEnumerator e | TryWithState.HaveHandlerEnumerator e ->
                                  state <- TryWithState.Finished
                                  do! e.DisposeAsync()
                              | _ -> () }
                          |> ValueTask
                  }
            }


      [<RequireQualifiedAccess>]
      type TryFinallyState<'T> =
          | NotStarted of AsyncSeq<'T>
          | HaveBodyEnumerator of IAsyncEnumerator<'T>
          | Finished

      // This pushes the handler through all the async computations
      // The (synchronous) compensation is run when the Dispose() is called
      let tryFinallyTask (inp: AsyncSeq<'T>) (compensation: unit -> Task) : AsyncSeq<'T> =
          { new IAsyncEnumerable<'T> with
              member _.GetAsyncEnumerator(ct) =
                  let mutable state = TryFinallyState.NotStarted inp
                  { new IAsyncEnumerator<'T> with

                      member _.Current =
                          match state with
                          | TryFinallyState.NotStarted _ -> invalidOp "not started"
                          | TryFinallyState.HaveBodyEnumerator ie -> ie.Current
                          | TryFinallyState.Finished -> invalidOp "finished"                           

                      member x.MoveNextAsync() =
                          task {
                              match state with
                              | TryFinallyState.NotStarted inp ->
                                  let e = inp.GetAsyncEnumerator(ct)
                                  state <- TryFinallyState.HaveBodyEnumerator e
                                  return! x.MoveNextAsync()
                              | TryFinallyState.HaveBodyEnumerator e ->
                                  let! res = e.MoveNextAsync()
                                  if not res then
                                      do! x.DisposeAsync()
                                  return res
                              | _ ->
                                  return false }
                          |> ValueTask<bool>

                      member _.DisposeAsync() =
                          task {
                              match state with
                              | TryFinallyState.HaveBodyEnumerator e->
                                  state <- TryFinallyState.Finished
                                  do! e.DisposeAsync()
                                  do! compensation()
                              | _ -> ()
                          } |> ValueTask
                      }
          }

      // This pushes the handler through all the async computations
      // The (synchronous) compensation is run when the Dispose() is called
      let tryFinally (inp: AsyncSeq<'T>) (compensation: unit -> unit) : AsyncSeq<'T> =
          tryFinallyTask inp (fun () -> compensation(); Task.CompletedTask)

      [<RequireQualifiedAccess>]
      type CollectState<'T,'U> =
          | NotStarted of AsyncSeq<'T>
          | HaveInputEnumerator of IAsyncEnumerator<'T>
          | HaveInnerEnumerator of IAsyncEnumerator<'T> * IAsyncEnumerator<'U>
          | Finished

      let collect (f: 'T -> AsyncSeq<'U>) (inp: AsyncSeq<'T>) : AsyncSeq<'U> =
          { new IAsyncEnumerable<'U> with
              member _.GetAsyncEnumerator(ct) =
                  let mutable state = CollectState.NotStarted inp
                  { new IAsyncEnumerator<'U> with

                      member _.Current =
                          match state with
                          | CollectState.NotStarted _ -> invalidOp "not started"
                          | CollectState.HaveInputEnumerator _ -> invalidOp "not started"
                          | CollectState.HaveInnerEnumerator (_, e2) -> e2.Current
                          | CollectState.Finished -> invalidOp "finished"                           

                      member x.MoveNextAsync() =
                          task {
                              match state with
                              | CollectState.NotStarted inp ->
                                  let e1 = inp.GetAsyncEnumerator(ct)
                                  state <- CollectState.HaveInputEnumerator e1
                                  return! x.MoveNextAsync()
                              | CollectState.HaveInputEnumerator e1 ->
                                  let! res1 = e1.MoveNextAsync()
                                  match res1 with
                                  | true ->
                                      let e2 = (f e1.Current).GetAsyncEnumerator(ct)
                                      state <- CollectState.HaveInnerEnumerator (e1, e2)
                                  | false ->
                                      do! x.DisposeAsync()
                                  return! x.MoveNextAsync()
                              | CollectState.HaveInnerEnumerator (e1, e2) ->
                                  let! res2 = e2.MoveNextAsync()
                                  match res2 with
                                  | false ->
                                      state <- CollectState.HaveInputEnumerator e1
                                      do! e2.DisposeAsync()
                                      return! x.MoveNextAsync()
                                  | true  ->
                                      return res2
                              | _ ->
                                  return false
                          } |> ValueTask<bool>

                      member _.DisposeAsync() =
                          task {
                              match state with
                              | CollectState.HaveInputEnumerator e1 ->
                                  state <- CollectState.Finished
                                  do! e1.DisposeAsync()
                              | CollectState.HaveInnerEnumerator (e1, e2) ->
                                  state <- CollectState.Finished
                                  do! e2.DisposeAsync()
                                  do! e1.DisposeAsync()
                              | _ -> ()
                          } |> ValueTask
                  } 
          } 

    //  let collect (f: 'T -> AsyncSeq<'U>) (inp: AsyncSeq<'T>) : AsyncSeq<'U> =
    //    AsyncGenerator.collect f inp

      [<RequireQualifiedAccess>]
      type CollectSeqState<'T,'U> =
         | NotStarted of seq<'T>
         | HaveInputEnumerator of IEnumerator<'T>
         | HaveInnerEnumerator of IEnumerator<'T> * IAsyncEnumerator<'U>
         | Finished

      // Like collect, but the input is a sequence, where no bind is required on each step of the enumeration
      let collectSeq (f: 'T -> AsyncSeq<'U>) (inp: seq<'T>) : AsyncSeq<'U> =
          { new IAsyncEnumerable<'U> with

              member _.GetAsyncEnumerator(ct) =
                  let mutable state = CollectSeqState.NotStarted inp
                  { new IAsyncEnumerator<'U> with

                      member _.Current =
                          match state with
                          | CollectSeqState.NotStarted _ -> invalidOp "not started"
                          | CollectSeqState.HaveInputEnumerator _ -> invalidOp "not started"
                          | CollectSeqState.HaveInnerEnumerator (_, ie) -> ie.Current
                          | CollectSeqState.Finished -> invalidOp "finished"                           

                      member x.MoveNextAsync() =
                          task {
                              match state with
                              | CollectSeqState.NotStarted inp ->
                                  let e1 = inp.GetEnumerator()
                                  state <- CollectSeqState.HaveInputEnumerator e1
                                  return! x.MoveNextAsync()
                              | CollectSeqState.HaveInputEnumerator e1 ->
                                  if e1.MoveNext()  then
                                      let e2 = (f e1.Current).GetAsyncEnumerator(ct)
                                      state <- CollectSeqState.HaveInnerEnumerator (e1, e2)
                                  else
                                      do! x.DisposeAsync()
                                  return! x.MoveNextAsync()
                              | CollectSeqState.HaveInnerEnumerator (e1, e2)->
                                  let! res2 = e2.MoveNextAsync()
                                  match res2 with
                                  | false ->
                                      state <- CollectSeqState.HaveInputEnumerator e1
                                      do! e2.DisposeAsync()
                                      return! x.MoveNextAsync()
                                  | true ->
                                      return res2
                              | _ -> return false 
                          } |> ValueTask<bool>

                      member _.DisposeAsync() =
                          task {
                              match state with
                              | CollectSeqState.HaveInputEnumerator e1 ->
                                  state <- CollectSeqState.Finished
                                  e1.Dispose()
                              | CollectSeqState.HaveInnerEnumerator (e1, e2) ->
                                  state <- CollectSeqState.Finished
                                  do! e2.DisposeAsync()
                                  e1.Dispose()
                              | _ -> ()
                          } |> ValueTask
                  }
          }

      type AsyncSeqBuilder() =
          member _.Yield(v) = singleton v
          // This looks weird, but it is needed to allow:
          //
          //   while foo do
          //     do! something
          //
          // because F# translates body as Bind(something, fun () -> Return())
          member _.Return () = empty
          member _.YieldFrom(s: AsyncSeq<'T>) = s
          member _.Zero () = empty
          member _.Bind (inp:ValueTask<'T>, body : 'T -> AsyncSeq<'U>) : AsyncSeq<'U> = bindValueTask body inp
          member _.Bind (inp:Task<'T>, body : 'T -> AsyncSeq<'U>) : AsyncSeq<'U> = bindTask body inp
          member _.Bind (inp:Async<'T>, body : 'T -> AsyncSeq<'U>) : AsyncSeq<'U> = bindAsync body inp
          member _.Bind (_inp:AsyncSeqCancellationToken, body : CancellationToken -> AsyncSeq<'U>) : AsyncSeq<'U> = bindCancellationToken body
          member _.Combine (seq1:AsyncSeq<'T>, seq2:AsyncSeq<'T>) = AsyncGenerator.append seq1 seq2
          member _.While (guard, body:AsyncSeq<'T>) =
              // Use F#'s support for Landin's knot for a low-allocation fixed-point
              let rec fix = delay (fun () -> if guard() then AsyncGenerator.append body fix else empty)
              fix
          member _.Delay (f:unit -> AsyncSeq<'T>) =
              delay f

          member _.TryFinally (body: AsyncSeq<'T>, compensation: unit -> unit) =
              tryFinally body compensation

          member _.TryWith (body: AsyncSeq<_>, handler: (exn -> AsyncSeq<_>)) =
              tryWith body handler

          member _.Using (resource: 'T, binder: 'T -> AsyncSeq<'U>) : _ when 'T :> IAsyncDisposable =
              tryFinallyTask (binder resource) (fun () -> task {
                  if box resource <> null then
                      do! resource.DisposeAsync() })

          member _.For (seq:seq<'T>, action:'T -> AsyncSeq<'TResult>) =
              collectSeq action seq

          member _.For (seq:AsyncSeq<'T>, action:'T -> AsyncSeq<'TResult>) =
              collect action seq

      [<AutoOpen>]
      module AsyncSeqBuilderLowPriorityExtensions =
          type AsyncSeqBuilder with
              member _.Using (resource: 'T, binder: 'T -> AsyncSeq<'U>) : _ when 'T :> IDisposable =
                  tryFinally (binder resource) (fun () ->
                     if box resource <> null then dispose resource)

      let asyncSeq = new AsyncSeqBuilder()


      [<RequireQualifiedAccess>]
      type MapState<'T> =
          | NotStarted of seq<'T>
          | HaveEnumerator of IEnumerator<'T>
          | Finished

      let ofSeq (inp: seq<'T>) : AsyncSeq<'T> =
          { new IAsyncEnumerable<'T> with

              member _.GetAsyncEnumerator(ct) =
                  let mutable state = MapState.NotStarted inp
                  { new IAsyncEnumerator<'T> with

                      member _.Current =
                          match state with
                          | MapState.NotStarted _ -> invalidOp "not started"
                          | MapState.HaveEnumerator ie -> ie.Current
                          | MapState.Finished -> invalidOp "finished"                           

                      member x.MoveNextAsync() =
                          task {
                              match state with
                              | MapState.NotStarted inp ->
                                  let e = inp.GetEnumerator()
                                  state <- MapState.HaveEnumerator e
                                  return! x.MoveNextAsync()
                              | MapState.HaveEnumerator e ->
                                  let res = e.MoveNext()
                                  if not res then
                                      do! x.DisposeAsync()
                                  return res
                              | _ ->
                                  return false
                          } |> ValueTask<bool>

                      member _.DisposeAsync() =
                          task {
                              match state with
                              | MapState.HaveEnumerator e ->
                                   state <- MapState.Finished
                                   dispose e
                              | _ -> ()
                          } |> ValueTask
                  }
          }

      let iteriTaskTask ct (f: int -> 'T -> Task<unit>) (source: AsyncSeq<'T>) =
          task {
              use ie = source.GetAsyncEnumerator(ct)
              let mutable count = 0
              ct.ThrowIfCancellationRequested()
              let! move = ie.MoveNextAsync()
              let mutable b = move
              while b do
                  ct.ThrowIfCancellationRequested()
                  do! f count ie.Current
                  let! moven = ie.MoveNextAsync()
                  count <- count + 1
                  b <- moven
          }

      let iteriAsyncAsync (f: _ -> _ -> Async<_>) (source: AsyncSeq<_>) : Async<_> =
          async {
              let! ct = Async.CancellationToken
              let ie = source.GetAsyncEnumerator(ct)
              let mutable exnOpt = None
              try
                  let mutable count = 0
                  let! move = Async.AwaitTask(ie.MoveNextAsync().AsTask())
                  let mutable b = move
                  while b do
                      do! f count ie.Current
                      let! moven = Async.AwaitTask(ie.MoveNextAsync().AsTask())
                      count <- count + 1
                      b <- moven
              with e ->
                 exnOpt <- Some e
              do! Async.AwaitTask(ie.DisposeAsync().AsTask())
              match exnOpt with
              | None -> ()
              | Some exn -> raise exn
                 
          }

      let iterTaskTask ct (f: 'T -> Task<unit>) (source: AsyncSeq<'T>)  =
          iteriTaskTask ct (fun i x -> f x) source

      let iterAsyncAsync (f: 'T -> Async<unit>) (source: AsyncSeq<'T>)  =
          iteriAsyncAsync (fun i x -> f x) source

      let iteri ct (f: int -> 'T -> unit) (inp: AsyncSeq<'T>)  =
          iteriTaskTask ct (fun i x -> Task<_>.FromResult (f i x)) inp

      let unfoldTask (f:'State -> Task<('T * 'State) option>) (s:'State) : AsyncSeq<'T> =
          asyncSeq {
              let mutable state = s
              let mutable b = None
              let! b2 = f state
              b <- b2
              while b.IsSome do
                  yield fst b.Value
                  state <- snd b.Value
                  let! b2 = f state
                  b <- b2
          }

      let unfoldAsync (f:'State -> Async<('T * 'State) option>) (s:'State) : AsyncSeq<'T> =
          asyncSeq {
              let mutable state = s
              let mutable b = None
              let! b2 = f state
              b <- b2
              while b.IsSome do
                  yield fst b.Value
                  state <- snd b.Value
                  let! b2 = f state
                  b <- b2
          }

      let replicateInfinite (v:'T) : AsyncSeq<'T> =
          0 |> unfoldTask (fun _ -> task {
              return Some (v, 0) })

      let replicateInfiniteAsync (v:Async<'T>) : AsyncSeq<'T> =
          0 |> unfoldTask (fun _ -> task {
              let! v = v
              return Some (v, 0) })

      let replicate (count:int) (v:'T) : AsyncSeq<'T> =
          0 |> unfoldTask (fun i -> task { 
              if i = count then return None
              else return Some (v, i + 1) })

      let replicateUntilNoneAsync (next:unit -> Task<'T option>) : AsyncSeq<'T> =
        () |> unfoldTask (fun () -> task {
            let! a = next ()
            match a with
            | None -> return None
            | Some v -> return Some (v, ()) })

      let intervalMs (periodMs:int) = asyncSeq {
        yield DateTime.UtcNow
        while true do
          do! Async.Sleep periodMs
          yield DateTime.UtcNow }

      // --------------------------------------------------------------------------
      // Additional combinators (implemented as async/asyncSeq computations)

      let mapTask (f: 'T -> CancellationToken -> Task<'TResult>) (source: AsyncSeq<'T>) : AsyncSeq<'TResult> =
        //match source with
        //| :? AsyncSeqOp<'T> as source -> source.MapAsync f
        //| _ ->
          asyncSeq {
            let! ct = getCancellationToken()
            for itm in source do
                let! v = f itm ct
                yield v }

      let mapAsync (f: 'T -> Async<'TResult>) (source: AsyncSeq<'T>) : AsyncSeq<'TResult> =
        //match source with
        //| :? AsyncSeqOp<'T> as source -> source.MapAsync f
        //| _ ->
          asyncSeq {
            for itm in source do
                let! v = f itm
                yield v }

      let mapiAsync (f: int64 -> 'T -> Async<'TResult>) (source: AsyncSeq<'T>) : AsyncSeq<'TResult> = asyncSeq {
        let i = ref 0L
        for itm in source do
          let! v = f i.Value itm
          i := i.Value + 1L
          yield v }

      //#if !FABLE_COMPILER
      //let mapAsyncParallel (f:'T -> Task<'b>) (s:AsyncSeq<'T>) : AsyncSeq<'b> = asyncSeq {
      //  use mb = MailboxProcessor.Start (fun _ -> async.Return())
      //  let! ct = CancellationToken
      //  let! err =
      //    s |> iterTaskTask ct (fun a -> task {
      //      let! b = f a
      //      mb.Post (Some b) })
      //  yield!
      //    replicateUntilNoneAsync (fun () -> Task.chooseTask (err |> Task.taskFault) (async.Delay mb.Receive))
      //    |> mapAsync id }
      //#endif

      let chooseAsync (f: 'T -> Async<'T option>) (source:AsyncSeq<'T>) =
        //match source with
        //| :? AsyncSeqOp<'T> as source -> source.ChooseAsync f
        //| _ ->
          asyncSeq {
              for itm in source do
                  let! v = f itm
                  match v with
                  | Some v -> yield v
                  | _ -> ()
          }

      let ofSeqAsync (source: seq<Async<'T>>) : AsyncSeq<'T> =
          asyncSeq {
              for asyncElement in source do
                  let! v = asyncElement
                  yield v
          }

      let filterAsync (f: 'T -> Async<bool>) (source: AsyncSeq<'T>) = asyncSeq {
        for v in source do
          let! b = f v
          if b then yield v }

      let tryLastTask ct (source: AsyncSeq<'T>) =
          task {
              use ie = source.GetAsyncEnumerator(ct)
              let! v = ie.MoveNextAsync()
              let mutable b = v
              let mutable res = None
              while b do
                  res <- Some ie.Current
                  let! moven = ie.MoveNextAsync()
                  b <- moven
              return res
          }

      let tryLastAsync (source: AsyncSeq<'T>) : Async<_> =
          async {
              let! ct = Async.CancellationToken
              use ie = source.GetAsyncEnumerator(ct).DisposeSynchronously()
              let! v = ie.MoveNextAsync().AsTask() |> Async.AwaitTask
              let mutable b = v
              let mutable res = None
              while b do
                  res <- Some ie.Current
                  let! moven = ie.MoveNextAsync().AsTask() |> Async.AwaitTask
                  b <- moven
              return res
          }

      let lastOrDefaultTask ct def (source: AsyncSeq<'T>) =
          task {
              let! v = tryLastTask ct source
              match v with
              | None -> return def
              | Some v -> return v
          }


      let tryFirstTask ct (source: AsyncSeq<'T>) : Task<'T option> =
          task {
              use ie = source.GetAsyncEnumerator(ct)
              let! v = ie.MoveNextAsync()
              let b = ref v
              if b.Value then
                  return Some ie.Current
              else
                 return None
          }

      let firstOrDefaultTask ct def (source: AsyncSeq<'T>) : Task<'T> =
          task {
              let! v = tryFirstTask ct source
              match v with
              | None -> return def
              | Some v -> return v
          }

      let scanTask (f: 'TState -> 'T -> Task<'TState>) (state:'TState) (source: AsyncSeq<'T>) : AsyncSeq<'TState> =
          asyncSeq {
              yield state
              let! ct = getCancellationToken()
              let mutable z = state
              use ie = source.GetAsyncEnumerator(ct)
              let! moveRes0 = ie.MoveNextAsync()
              let mutable b = moveRes0
              while b do
                  let! zNext = f z ie.Current
                  z <- zNext
                  yield z
                  let! moveResNext = ie.MoveNextAsync()
                  b <- moveResNext
          }

      let pairwise (source: AsyncSeq<'T>) =
          asyncSeq {
              let! ct = getCancellationToken()
              use ie = source.GetAsyncEnumerator(ct)
              let! v = ie.MoveNextAsync()
              let mutable b = v
              let mutable prev = None
              while b do
                  let v = ie.Current
                  match prev with
                  | None -> ()
                  | Some p -> yield (p, v)
                  prev <- Some v
                  let! moven = ie.MoveNextAsync()
                  b <- moven
          }

      let pickTask ct (f:'T -> Task<'U option>) (source:AsyncSeq<'T>) =
          task {
              use ie = source.GetAsyncEnumerator(ct)
              let! v = ie.MoveNextAsync()
              let mutable b = v
              let mutable res = None
              while b && not res.IsSome do
                  let! fv = f ie.Current
                  match fv with
                  | None ->
                      let! moven = ie.MoveNextAsync()
                      b <- moven
                  | Some _ as r ->
                      res <- Some r
              match res with
              | Some _ -> return res.Value
              | None -> return raise(KeyNotFoundException())
          }

      let pick ct f (source:AsyncSeq<'T>) =
          source |> pickTask ct (f >> Task.FromResult) 

      let tryPickTaskTask ct (f:'T -> Task<'U option>) (source: AsyncSeq<'T>) : Task<'U option> =
          task {
              use ie = source.GetAsyncEnumerator(ct)
              let! v = ie.MoveNextAsync()
              let mutable b = v
              let mutable res = None
              while b && not res.IsSome do
                  let! fv = f ie.Current
                  match fv with
                  | None ->
                      let! moven = ie.MoveNextAsync()
                      b <- moven
                  | Some _ as r ->
                      res <- r
              return res
          }

      let tryPickTask ct (f:'T -> 'U option) (source: AsyncSeq<'T>) : Task<'U option> =
          source |> tryPickTaskTask ct (f >> Task.FromResult) 

      let contains ct value (source: AsyncSeq<'T>) =
          source |> tryPickTask ct (fun v -> if v = value then Some () else None) |> Task.map Option.isSome

      let tryFindTask ct f (source: AsyncSeq<'T>) : Task<'a option> =
          source |> tryPickTask ct (fun v -> if f v then Some v else None)

      let existsTask ct f (source: AsyncSeq<'T>) : Task<bool> =
          source |> tryFindTask ct f |> Task.map Option.isSome

      let forallTask ct f (source: AsyncSeq<'T>) : Task<bool> =
          source |> existsTask ct (f >> not) |> Task.map not

      let foldTaskTask ct f (state:'State) (source: AsyncSeq<'T>) : Task<'State> =
          //match source with
          //| :? AsyncSeqOp<'T> as source -> source.FoldAsync f state
          //| _ ->
          source |> scanTask f state |> lastOrDefaultTask ct state

      let foldTask ct f (state:'State) (source: AsyncSeq<'T>) =
          foldTaskTask ct (fun st v -> f st v |> Task.FromResult) state source

      let lengthTask ct (source: AsyncSeq<'T>) =
          foldTask ct (fun st _ -> st + 1L) 0L source

      let inline sumTask ct (source: AsyncSeq<'T>) : Task<'T> =
         (LanguagePrimitives.GenericZero, source) ||> foldTask ct (+)

      let scan f (state:'State) (source: AsyncSeq<'T>) =
          scanTask (fun st v -> f st v |> Task.FromResult) state source

      let unfold f (state:'State) =
          unfoldAsync (f >> async.Return) state

      let initInfiniteTask (f: int64 -> Task<'T>) =
          0L |> unfoldTask (fun n -> task {
              let! x = f n
              return Some (x,n+1L) })

      let initTask (count:int64) (f: int64 -> Task<'T>)  =
          0L |> unfoldTask (fun n -> task {
              if n >= count then return None
              else
                  let! x = f n
                  return Some (x,n+1L) })

      let init count f  =
          initTask count (f >> Task.FromResult)

      let initInfinite f  =
          initInfiniteTask (f >> Task.FromResult)

      let mapi f (source: AsyncSeq<'T>) =
          mapiAsync (fun i x -> f i x |> async.Return) source

      let map f (source: AsyncSeq<'T>) =
          mapAsync (f >> async.Return) source

      let indexed (source: AsyncSeq<'T>) =
          mapi (fun i x -> (i,x)) source

      let iterAsync f (source: AsyncSeq<'T>) =
          iterAsyncAsync (f >> async.Return) source

      let choose f (source: AsyncSeq<'T>) =
          chooseAsync (f >> async.Return) source

      let filter f (source: AsyncSeq<'T>) : AsyncSeq<'T> =
          filterAsync (f >> async.Return) source

      //#if !FABLE_COMPILER
      //let iterTaskParallel ct (f:'T -> Task<unit>) (s:AsyncSeq<'T>) : Task<unit> = task {
      //    use mb = MailboxProcessor.Start (ignore >> async.Return)
      //    let! err =
      //        s
      //        |> iterTaskTask ct (fun a -> task {
      //            let! b = Async.StartChild (Async.AwaitTask (f a))
      //            mb.Post (Some b) })
      //        |> Task.map (fun _ -> mb.Post None)

      //    return!
      //        replicateUntilNoneAsync (Task.chooseTask (err |> Task.taskFault) (async.Delay mb.Receive))
      //        |> iterAsyncAsync id }

      //let iterAsyncAsyncParallelThrottled (parallelism:int) (f:'T -> Task<unit>) (s:AsyncSeq<'T>) : Async<unit> = task {
      //  use mb = MailboxProcessor.Start (ignore >> async.Return)
      //  use sm = new SemaphoreSlim(parallelism)
      //  let! err =
      //    s
      //    |> iterAsyncAsync (fun a -> task {
      //      do! sm.WaitAsync () |> Async.awaitTaskUnitCancellationAsError
      //      let! b = Async.StartChild (task {
      //        try do! f a
      //        finally sm.Release () |> ignore })
      //      mb.Post (Some b) })
      //    |> Async.map (fun _ -> mb.Post None)
      //    |> Async.StartChildAsTask
      //  return!
      //    replicateUntilNoneAsync (Task.chooseTask (err |> Task.taskFault) (async.Delay mb.Receive))
      //    |> iterAsyncAsync id }
      //#endif

      // --------------------------------------------------------------------------
      // Converting from/to synchronous sequences or IObservables

      let ofObservableBuffered (source: System.IObservable<_>) =
          asyncSeq {
              let cts = new CancellationTokenSource()
              try
                  // The body of this agent returns immediately.  It turns out this is a valid use of an F# agent, and it
                  // leaves the agent available as a queue that supports an asynchronous receive.
                  //
                  // This makes the cancellation token is somewhat meaningless since the body has already returned.  However
                  // if we don't pass it in then the default cancellation token will be used, so we pass one in for completeness.
                  use agent = MailboxProcessor<_>.Start((fun inbox -> async.Return() ), cancellationToken = cts.Token)
                  use d = source |> Observable.asUpdates |> Observable.subscribe agent.Post
                  let mutable fin = false
                  while not fin do
                      let! msg = agent.Receive()
                      match msg with
                      | Observable.ObservableUpdate.Error e -> e.Throw()
                      | Observable.Completed -> fin <- true
                      | Observable.Next v -> yield v
              finally
                 // Cancel on early exit
                 cts.Cancel()
          }

      [<System.Obsolete("Please use AsyncSeq.ofObservableBuffered. The original AsyncSeq.ofObservable doesn't guarantee that the asynchronous sequence will return all values produced by the observable",true) >]
      let ofObservable (source: System.IObservable<'T>) : AsyncSeq<'T> =
          failwith "no longer supported"

      let toObservable (input:AsyncSeq<_>) =
          { new IObservable<_> with
              member _.Subscribe(obs) =
                  Task.StartDisposable (fun ct ->
                      task {
                          try
                              do! input |> iterTaskTask ct (fun v -> task { return obs.OnNext(v) })
                              obs.OnCompleted()
                          with e ->
                              obs.OnError(e)
                      })
         }

      #if !FABLE_COMPILER
      let toBlockingSeq (source: AsyncSeq<'T>) =
          seq {
              // Write all elements to a blocking buffer
              use buf = new System.Collections.Concurrent.BlockingCollection<_>()

              use cts = new System.Threading.CancellationTokenSource()
              use _cancel = { new IDisposable with member _.Dispose() = cts.Cancel() }
              let iteratorTask =
                  async {
                      try
                         // Cancellable
                         do! iterAsync (Observable.Next >> buf.Add) source
                         buf.CompleteAdding()
                      with err ->
                         buf.Add(Observable.Error (ExceptionDispatchInfo.Capture err))
                         buf.CompleteAdding()
                  }
                  |> fun p -> Async.StartAsTask(p, cancellationToken = cts.Token)

              // Read elements from the blocking buffer & return a sequences
              for x in buf.GetConsumingEnumerable() do
                match x with
                | Observable.Next v -> yield v
                | Observable.Error err -> err.Throw()
                | Observable.Completed -> failwith "unexpected"
          }

      let cache (source: AsyncSeq<'T>) =
          let cacheLock = obj()
          let cache = ResizeArray<_>()
          let fin = TaskCompletionSource<unit>()
          let tryGetCachedItem i =
              lock cacheLock (fun() ->
                  if cache.Count > i then
                      Some cache.[i]
                  else
                      None)

          // NB: no need to dispose since we're not using timeouts
          // NB: this MBP might have some unprocessed messages in internal queue until collected
          let mbp =
               MailboxProcessor.Start (fun mbp -> async {
                   use ie = source.GetAsyncEnumerator(System.Threading.CancellationToken.None).DisposeSynchronously()
                   let rec loop () = async {
                       let! (i:int, rep:TaskCompletionSource<'T option>) = mbp.Receive()
                       if i >= cache.Count then
                           try
                               let! move = ie.MoveNextAsync().AsTask() |> Async.AwaitTask
                               if move then
                                   lock cacheLock (fun() -> cache.Add ie.Current)
                               else
                                   fin.SetResult ()
                           with ex ->
                               rep.SetException ex
                       if not fin.Task.IsCompleted then
                           let a = cache.[i]
                           rep.SetResult (Some a)
                           return! loop ()
                       else
                           rep.SetResult None }
                   return! loop () })

          asyncSeq {
              if fin.Task.IsCompleted then yield! ofSeq cache else
              let rec loop i = asyncSeq {
                  let cachedItem = tryGetCachedItem i
                  match cachedItem with
                  | Some a ->
                      yield a
                      yield! loop (i + 1)
                  | None ->
                      let! next = Task.chooseTasks (fin.Task) (mbp.PostAndAsyncReplyTask (fun rep -> (i,rep)))
                      match next with
                      | Choice2Of2 (Some a,_) ->
                          yield a
                          yield! loop (i + 1)
                      | Choice2Of2 (None,_) -> ()
                      | Choice1Of2 _ ->
                          if i = 0 then yield! ofSeq cache
                          else yield! ofSeq (cache |> Seq.skip i) }
              yield! loop 0 }
      #endif

      // --------------------------------------------------------------------------

      let threadStateAsync (f:'State -> 'T -> Task<'U * 'State>) (state:'State) (source:AsyncSeq<'T>) : AsyncSeq<'U> =
          asyncSeq {
              let! ct = getCancellationToken()
              use ie = source.GetAsyncEnumerator(ct)
              let! v = ie.MoveNextAsync()
              let mutable b = v
              let mutable z = state
              while b do
                  let v = ie.Current
                  let! v2,z2 = f z v
                  yield v2
                  let! moven = ie.MoveNextAsync()
                  b <- moven
                  z <- z2
          }

      let zipWithAsync (f:'T1 -> 'T2 -> Async<'U>) (source1:AsyncSeq<'T1>) (source2:AsyncSeq<'T2>) : AsyncSeq<'U> =
          asyncSeq {
              let! ct = getCancellationToken()
              use ie1 = source1.GetAsyncEnumerator(ct)
              use ie2 = source2.GetAsyncEnumerator(ct)
              let! move1 = ie1.MoveNextAsync().AsTask() |> Async.AwaitTask
              let! move2 = ie2.MoveNextAsync().AsTask() |> Async.AwaitTask
              let mutable b1 = move1
              let mutable b2 = move2
              while b1 && b2 do
                  let! res = f ie1.Current ie2.Current
                  yield res
                  let! move1n = ie1.MoveNextAsync().AsTask() |> Async.AwaitTask
                  let! move2n = ie2.MoveNextAsync().AsTask() |> Async.AwaitTask
                  b1 <- move1n
                  b2 <- move2n
          }

      let zipWithAsyncParallel (f:'T1 -> 'T2 -> Async<'U>) (source1:AsyncSeq<'T1>) (source2:AsyncSeq<'T2>) : AsyncSeq<'U> =
          asyncSeq {
              let! ct = getCancellationToken()
              use ie1 = source1.GetAsyncEnumerator(ct)
              use ie2 = source2.GetAsyncEnumerator(ct)
              let! move1 = ie1.MoveNextAsync().AsTask() |> Async.AwaitTask |> Async.StartChild
              let! move2 = ie2.MoveNextAsync().AsTask() |> Async.AwaitTask |> Async.StartChild
              let! move1 = move1
              let! move2 = move2
              let mutable b1 = move1
              let mutable b2 = move2
              while b1 && b2 do
                  let! res = f ie1.Current ie2.Current
                  yield res
                  let! move1n = ie1.MoveNextAsync().AsTask() |> Async.AwaitTask |> Async.StartChild
                  let! move2n = ie2.MoveNextAsync().AsTask() |> Async.AwaitTask |> Async.StartChild
                  let! move1n = move1n
                  let! move2n = move2n
                  b1 <- move1n
                  b2 <- move2n }

      let zip (source1 : AsyncSeq<'T1>) (source2 : AsyncSeq<'T2>) : AsyncSeq<_> =
          zipWithAsync (fun a b -> async.Return (a,b)) source1 source2

      let zipParallel (source1 : AsyncSeq<'T1>) (source2 : AsyncSeq<'T2>) : AsyncSeq<_> =
          zipWithAsyncParallel (fun a b -> async.Return (a,b)) source1 source2

      let zipWith (z:'T1 -> 'T2 -> 'U) (a:AsyncSeq<'T1>) (b:AsyncSeq<'T2>) : AsyncSeq<'U> =
          zipWithAsync (fun a b -> z a b |> async.Return) a b

      let zipWithParallel (z:'T1 -> 'T2 -> 'U) (a:AsyncSeq<'T1>) (b:AsyncSeq<'T2>) : AsyncSeq<'U> =
          zipWithAsyncParallel (fun a b -> z a b |> async.Return) a b

      let zipWithIndexAsync (f:int64 -> 'T -> Async<'U>) (s:AsyncSeq<'T>) : AsyncSeq<'U> =
        mapiAsync f s

      let zappAsync (fs:AsyncSeq<'T -> Async<'U>>) (s:AsyncSeq<'T>) : AsyncSeq<'U> =
          zipWithAsync (|>) s fs

      let zapp (fs:AsyncSeq<'T -> 'U>) (s:AsyncSeq<'T>) : AsyncSeq<'U> =
          zipWith (|>) s fs

      let takeWhileAsync (p: 'T -> Async<bool>) (source: AsyncSeq<'T>) : AsyncSeq<_> =
          asyncSeq {
              let! ct = getCancellationToken()
              use ie = source.GetAsyncEnumerator(ct)
              let! move = ie.MoveNextAsync()
              let mutable b = move
              while b do
                  let v = ie.Current
                  let! res = p v
                  if res then
                      yield v
                      let! moven = ie.MoveNextAsync()
                      b <- moven
                  else
                      b <- false
          }

      let takeWhile p (source: AsyncSeq<'T>) =
          takeWhileAsync (p >> async.Return) source

      #if !FABLE_COMPILER
      let takeUntilSignal (signal:Async<unit>) (source:AsyncSeq<'T>) : AsyncSeq<'T> =
          asyncSeq {
              let! ct = getCancellationToken()
              use ie = source.GetAsyncEnumerator(ct)
              let! signalT = Async.StartChildAsTask signal
              let moveT = ie.MoveNextAsync().AsTask()
              let! move = Task.chooseTasks signalT moveT
              let mutable b = move
              while (match b with Choice2Of2 (true,_) -> true | _ -> false) do
                  let sg = (match b with Choice2Of2 (_,sg) -> sg | _ -> failwith "unreachable")
                  yield ie.Current
                  let moveT = ie.MoveNextAsync().AsTask()
                  let! move = Task.chooseTasks sg moveT
                  b <- move
          }

      let takeUntil signal source = takeUntilSignal signal source
      #endif

      let takeWhileInclusive (f: 'T -> bool) (s: AsyncSeq<'T>) : AsyncSeq<'T> =
          { new IAsyncEnumerable<'T> with
               member _.GetAsyncEnumerator(ct) =
                 let en = s.GetAsyncEnumerator(ct)
                 let mutable v = Unchecked.defaultof<_>
                 let mutable fin = false
                 { new IAsyncEnumerator<'T> with
                     member _.Current = v
                     member _.MoveNextAsync() =
                         task {
                             if fin then
                                 return true
                             else
                                 let! next = en.MoveNextAsync()
                                 if next then
                                     v <- en.Current
                                     if not (f v) then
                                         fin <- true
                                 return next
                         } |> ValueTask<bool>

                     member _.DisposeAsync() = en.DisposeAsync() } }

      let skipWhileAsync (p: 'T -> Async<bool>) (source: AsyncSeq<'T>) : AsyncSeq<_> =
          asyncSeq {
              let! ct = getCancellationToken()
              use ie = source.GetAsyncEnumerator(ct)
              let! move = ie.MoveNextAsync()
              let mutable b = move
              let mutable doneSkipping = false
              while b do
                  let v = ie.Current
                  if doneSkipping then
                      yield v
                  else
                      let! test = p v
                      if not test then
                          yield v
                          doneSkipping <- true
                  let! moven = ie.MoveNextAsync()
                  b <- moven
          }

      #if !FABLE_COMPILER
      let skipUntilSignal (signal:Async<unit>) (source:AsyncSeq<'T>) : AsyncSeq<'T> =
          asyncSeq {
              let! ct = getCancellationToken()
              use ie = source.GetAsyncEnumerator(ct)
              let! signalT = Async.StartChildAsTask signal
              let moveT = ie.MoveNextAsync().AsTask()
              let! move = Task.chooseTasks signalT moveT
              let mutable b = move
              while (match b with Choice2Of2 (true,_) -> true | _ -> false) do
                  let sg = (match b with Choice2Of2 (_,sg) -> sg | _ -> failwith "unreachable")
                  let moveT = ie.MoveNextAsync().AsTask()
                  let! move = Task.chooseTasks sg moveT
                  b <- move
              match b with
              | Choice2Of2 (false,_) ->
                  ()
              | Choice1Of2 (_,rest) ->
                  let! move = Async.AwaitTask rest
                  let mutable b2 = move
                  // Yield the rest of the sequence
                  while b2 do
                      yield ie.Current
                      let! moven = ie.MoveNextAsync()
                      b2 <- moven
              | Choice2Of2 (true,_) -> failwith "unreachable"
          }

      let skipUntil signal source = skipUntilSignal signal source
      #endif

      let skipWhile p (source: AsyncSeq<'T>) =
          skipWhileAsync (p >> async.Return) source

      let take count (source: AsyncSeq<'T>) : AsyncSeq<_> =
          asyncSeq {
              let! ct = getCancellationToken()
              if (count < 0) then invalidArg "count" "must be non-negative"
              use ie = source.GetAsyncEnumerator(ct)
              let mutable n = count
              if n > 0 then
                  let! move = ie.MoveNextAsync()
                  let mutable b = move
                  while b do
                      yield ie.Current
                      n <- n - 1
                      if n > 0 then
                          let! moven = ie.MoveNextAsync()
                          b <- moven
                      else
                          b <- false
          }

      let truncate count source = take count source

      let skip count (source: AsyncSeq<'T>) : AsyncSeq<_> =
          asyncSeq {
              let! ct = getCancellationToken()
              if (count < 0) then invalidArg "count" "must be non-negative"
              use ie = source.GetAsyncEnumerator(ct)
              let! move = ie.MoveNextAsync()
              let mutable b = move
              let mutable n = count
              while b do
                  if n = 0 then
                      yield ie.Current
                  else
                      n <- n - 1
                  let! moven = ie.MoveNextAsync()
                  b <- moven
          }

      let toArrayAsync (source: AsyncSeq<'T>) : Async<'T[]> =
          async {
              let! ct = Async.CancellationToken
              let ra = (new ResizeArray<_>())
              use ie = source.GetAsyncEnumerator(ct).DisposeSynchronously()
              let! move = ie.MoveNextAsync().AsTask() |> Async.AwaitTask
              let mutable b = move
              while b do
                  ra.Add ie.Current
                  let! moven = ie.MoveNextAsync().AsTask() |> Async.AwaitTask
                  b <- moven
              return ra.ToArray()
          }

      let toListAsync (source:AsyncSeq<'T>) : Async<'T list> = toArrayAsync source |> Async.map Array.toList

      #if !FABLE_COMPILER
      let toListSynchronously (source:AsyncSeq<'T>) = toListAsync source |> Async.RunSynchronously
      let toArraySynchronously (source:AsyncSeq<'T>) = toArrayAsync source |> Async.RunSynchronously
      #endif

      let concatSeq (source:AsyncSeq<#seq<'T>>) : AsyncSeq<'T> =
          asyncSeq {
              let! ct = getCancellationToken()
              use ie = source.GetAsyncEnumerator(ct)
              let! move = ie.MoveNextAsync()
              let mutable b = move
              while b do
                  for x in (ie.Current :> seq<'T>)  do
                      yield x
                  let! moven = ie.MoveNextAsync()
                  b <- moven
          }

      let concat (source:AsyncSeq<AsyncSeq<'T>>) : AsyncSeq<'T> =
          asyncSeq {
              for innerSeq in source do
                  for e in innerSeq do
                      yield e
          }

      let emitEnumerator (ie: IAsyncEnumerator<'T>) =
          asyncSeq {
              let! moven = ie.MoveNextAsync()
              let mutable b = moven
              while b do
                  yield ie.Current
                  let! moven = ie.MoveNextAsync()
                  b <- moven
          }

      let interleaveChoice (source1: AsyncSeq<'T1>) (source2: AsyncSeq<'T2>) =
          asyncSeq {
              let! ct = Async.CancellationToken
              use ie1 = (source1 |> map Choice1Of2).GetAsyncEnumerator(ct)
              use ie2 = (source2 |> map Choice2Of2).GetAsyncEnumerator(ct)
              let! move = ie1.MoveNextAsync()
              let mutable is1 = true
              let mutable b = move
              while b do
                  yield ie1.Current
                  is1 <- not is1
                  let! moven = (if is1 then ie1.MoveNextAsync() else ie2.MoveNextAsync())
                  b <- moven
              // emit the rest
              yield! emitEnumerator (if is1 then ie2 else ie1)
          }

      let interleave (source1:AsyncSeq<'T>) (source2:AsyncSeq<'T>) : AsyncSeq<'T> =
          interleaveChoice source1 source2 |> map (function Choice1Of2 x -> x | Choice2Of2 x -> x)


      let bufferByCount (bufferSize:int) (source:AsyncSeq<'T>) : AsyncSeq<'T[]> =
          if (bufferSize < 1) then invalidArg "bufferSize" "must be positive"
          asyncSeq {
              let! ct = Async.CancellationToken
              let buffer = new ResizeArray<_>()
              use ie = source.GetAsyncEnumerator(ct)
              let! move = ie.MoveNextAsync()
              let mutable b = move
              while b do
                  buffer.Add ie.Current
                  if buffer.Count = bufferSize then
                      yield buffer.ToArray()
                      buffer.Clear()
                  let! moven = ie.MoveNextAsync()
                  b <- moven
              if (buffer.Count > 0) then
                  yield buffer.ToArray()
          }

      let toSortedSeq fn source =
          toArrayAsync source |> Async.map fn |> Async.RunSynchronously

      let sort (source:AsyncSeq<'T>) : array<'T> when 'T : comparison =
          toSortedSeq Array.sort source

      let sortBy (projection:'T -> 'Key) (source:AsyncSeq<'T>) : array<'T> when 'Key : comparison =
          toSortedSeq (Array.sortBy projection) source

      let sortDescending (source:AsyncSeq<'T>) : array<'T> when 'T : comparison =
          toSortedSeq Array.sortDescending source

      let sortByDescending (projection:'T -> 'Key) (source:AsyncSeq<'T>) : array<'T> when 'Key : comparison =
          toSortedSeq (Array.sortByDescending projection) source

      #if !FABLE_COMPILER
      let bufferByCountAndTime (bufferSize:int) (timeoutMs:int) (source:AsyncSeq<'T>) : AsyncSeq<'T[]> =
          if (bufferSize < 1) then invalidArg "bufferSize" "must be positive"
          if (timeoutMs < 1) then invalidArg "timeoutMs" "must be positive"
          asyncSeq {
              let! ct = Async.CancellationToken
              let buffer = new ResizeArray<_>()
              use ie = source.GetAsyncEnumerator(ct)
              let rec loop rem rt = asyncSeq {
                let move =
                    match rem with
                    | Some rem -> rem
                    | None -> ie.MoveNextAsync().AsTask()
                let t = Stopwatch.GetTimestamp()
                let! time = Async.StartChildAsTask(Async.Sleep (max 0 rt))
                let! moveOr = Task.chooseTasks move time
                let delta = int ((Stopwatch.GetTimestamp() - t) * 1000L / Stopwatch.Frequency)
                match moveOr with
                | Choice1Of2 (false, _) ->
                  if buffer.Count > 0 then
                      yield buffer.ToArray()
                | Choice1Of2 (true, _) ->
                  buffer.Add ie.Current
                  if buffer.Count = bufferSize then
                      yield buffer.ToArray()
                      buffer.Clear()
                      yield! loop None timeoutMs
                  else
                      yield! loop None (rt - delta)
                | Choice2Of2 (_, rest) ->
                  if buffer.Count > 0 then
                      yield buffer.ToArray()
                      buffer.Clear()
                      yield! loop (Some rest) timeoutMs
                  else
                    yield! loop (Some rest) timeoutMs
              }
              yield! loop None timeoutMs
          }

      let bufferByTime (timeMs:int) (source:AsyncSeq<'T>) : AsyncSeq<'T[]> =
          asyncSeq {
              let! ct = Async.CancellationToken
              if (timeMs < 1) then invalidArg "timeMs" "must be positive"
              let buf = new ResizeArray<_>()
              use ie = source.GetAsyncEnumerator(ct)
              let rec loop (next:Task<bool> option, waitFor:Task option) =
                  asyncSeq {
                      let next =
                          match next with
                          | Some n -> n
                          | None -> ie.MoveNextAsync().AsTask()
                      let waitFor =
                          match waitFor with
                          | Some w -> w
                          | None -> Task.Delay timeMs
                      let! res = Task.chooseTasks2 next waitFor
                      match res with
                      | Choice1Of2 (true,waitFor) ->
                          buf.Add ie.Current
                          yield! loop (None,Some waitFor)
                      | Choice1Of2 (false,_) ->
                          let arr = buf.ToArray()
                          if arr.Length > 0 then
                              yield arr
                      | Choice2Of2 next ->
                          let arr = buf.ToArray()
                          buf.Clear()
                          yield arr
                          yield! loop (Some next, None)
                  }
              yield! loop (None, None)
          }

      let private mergeChoiceEnum (ie1:IAsyncEnumerator<'T1>) (ie2:IAsyncEnumerator<'T2>) : AsyncSeq<Choice<'T1,'T2>> =
          asyncSeq {
              let move1T = ie1.MoveNextAsync().AsTask()
              let move2T = ie2.MoveNextAsync().AsTask()
              let! move = Task.chooseTasks move1T move2T
              let mutable b = move
              while (match b with Choice1Of2 (true,_) | Choice2Of2 (true,_) -> true | _ -> false) do
                  match b with
                  | Choice1Of2 (true, rest2) ->
                      yield Choice1Of2 ie1.Current
                      let move1T = ie1.MoveNextAsync().AsTask()
                      let! move = Task.chooseTasks move1T rest2
                      b <- move
                  | Choice2Of2 (false, rest1) ->
                      yield Choice2Of2 ie2.Current
                      let move2T = ie2.MoveNextAsync().AsTask()
                      let! move = Task.chooseTasks rest1 move2T
                      b <- move
                  | _ -> failwith "unreachable"

              match b with
              | Choice1Of2 (false, rest2) ->
                  let! move2 = Async.AwaitTask rest2
                  let mutable b2 = move2
                  while b2 do
                      let v2 = ie2.Current
                      yield Choice2Of2 v2
                      let! move2n = ie2.MoveNextAsync()
                      b2 <- move2n
              | Choice2Of2 (false, rest1) ->
                  let! move1 = Async.AwaitTask rest1
                  let mutable b1 = move1
                  while b1 do
                      let v1 = ie1.Current
                      yield Choice1Of2 v1
                      let! move1n = ie1.MoveNextAsync()
                      b1 <- move1n
              | _ -> failwith "unreachable" }

      let mergeChoice (source1:AsyncSeq<'T1>) (source2:AsyncSeq<'T2>) : AsyncSeq<Choice<'T1,'T2>> =
          asyncSeq {
              use ie1 = source1.GetAsyncEnumerator(ct)
              use ie2 = source2.GetAsyncEnumerator(ct)
              yield! mergeChoiceEnum ie1 ie2
          }

      let merge (source1:AsyncSeq<'T>) (source2:AsyncSeq<'T>) : AsyncSeq<'T> =
        mergeChoice source1 source2 |> map (function Choice1Of2 x -> x | Choice2Of2 x -> x)

      type Disposables<'T when 'T :> IAsyncDisposable> (ss: 'T[]) =
          interface System.IAsyncDisposable with
              member _.DisposeAsync() =
                  task {
                      let mutable err = None
                      for i in ss.Length - 1 .. -1 ..  0 do
                          try
                              use _ = (ss[i] :> IAsyncDisposable)
                              ()
                          with e ->
                              err <- Some (ExceptionDispatchInfo.Capture e)
                      match err with
                      | Some e -> e.Throw()
                      | None -> ()
                  } |> ValueTask

      let moveToEnd i (a: 'T[]) =
          let len = a.Length
          if i < 0 || i >= len then
              raise <| System.ArgumentOutOfRangeException()
          if i <> len-1 then
              let x = a[i]
              Array.Copy(a, i+1, a, i, len-1-i)
              a[len-1] <- x

      /// Merges all specified async sequences into an async sequence non-deterministically.
      // By moving the last emitted task to the end of the array, this algorithm achieves max-min fairness when merging AsyncSeqs
      let mergeAll (ss:AsyncSeq<'T> list) : AsyncSeq<'T> =
          asyncSeq {
              let! ct = getCancellationToken()
              let n = ss.Length

              if n > 0 then
                  let ies = [| for source in ss -> source.GetAsyncEnumerator(ct)  |]
                  use _ies = new Disposables<_>(ies)
                  let tasks = 
                      [| for i in 0 .. ss.Length - 1 do
                            ies[i].MoveNextAsync().AsTask() |]
                  let mutable fin = n
                  while fin > 0 do
                      let! ti = Task.WhenAny tasks |> Async.AwaitTask
                      let i  = Array.IndexOf(tasks, ti)
                      if ti.Result then
                          yield ies.[i].Current
                          let task = ies.[i].MoveNextAsync().AsTask()
                          tasks.[i] <- task
                          moveToEnd i tasks
                          moveToEnd i ies
                      else
                          let t = TaskCompletionSource()
                          tasks.[i] <- t.Task // result never gets set
                          fin <- fin - 1
          }

      let combineLatestWithAsync (f:'T1 -> 'T2 -> Task<'TResult>) (source1:AsyncSeq<'T1>) (source2:AsyncSeq<'T2>) : AsyncSeq<'TResult> =
          asyncSeq {
              let! ct = getCancellationToken()
              use en1 = source1.GetAsyncEnumerator(ct)
              use en2 = source2.GetAsyncEnumerator(ct)
              let! a = en1.MoveNextAsync()
              let! b = en2.MoveNextAsync()
              match a,b with
              | true, true ->
                  let! c = f en1.Current en2.Current
                  yield c
                  let merged = mergeChoiceEnum en1 en2
                  use mergedEnum = merged.GetAsyncEnumerator(ct)
                  let rec loop (prevA: 'T1, prevB: 'T2) =
                      asyncSeq {
                          let! next = mergedEnum.MoveNextAsync ()
                          match next with
                          | false -> ()
                          | true ->
                              match mergedEnum.Current with
                              | Choice1Of2 nextA ->
                                  let! c = f nextA prevB
                                  yield c
                                  yield! loop (nextA,prevB)
                              | Choice2Of2 nextB ->
                                  let! c = f prevA nextB
                                  yield c
                                  yield! loop (prevA,nextB)
                      }
                  yield! loop (en1.Current, en2.Current)
              | _ -> () }

      let combineLatestWith (f:'T1 -> 'T2 -> 'TResult) (source1:AsyncSeq<'T1>) (source2:AsyncSeq<'T2>) : AsyncSeq<'TResult> =
        combineLatestWithAsync (fun a b -> f a b |> Task.FromResult) source1 source2

      let combineLatest (source1:AsyncSeq<'T1>) (source2:AsyncSeq<'T2>) : AsyncSeq<'T1 * 'T2> =
        combineLatestWith (fun a b -> a,b) source1 source2
      #endif

      let distinctUntilChangedWithAsync (f:'T -> 'T -> Task<bool>) (source:AsyncSeq<'T>) : AsyncSeq<'T> =
          asyncSeq {
              let! ct = getCancellationToken()
              use ie = source.GetAsyncEnumerator(ct)
              let! move = ie.MoveNextAsync()
              let mutable b = move
              let mutable prev = None
              while b do
                  let v = ie.Current
                  match prev with
                  | None ->
                      yield v
                  | Some p ->
                      let! b = f p v
                      if not b then yield v
                  prev <- Some v
                  let! moven = ie.MoveNextAsync()
                  b <- moven
          }

      let distinctUntilChangedWith (f: 'T -> 'T -> bool) (s: AsyncSeq<'T>) : AsyncSeq<'T> =
        distinctUntilChangedWithAsync (fun a b -> f a b |> Task.FromResult) s

      let distinctUntilChanged (s: AsyncSeq<'T>) : AsyncSeq<'T> =
        distinctUntilChangedWith ((=)) s

      let getIterator ct (s: AsyncSeq<'T>) : (unit -> Task<'T option>) =
          let ie = s.GetAsyncEnumerator(ct)
          fun () -> task { let! v = ie.MoveNextAsync() in if v then return Some ie.Current else return None }

      let traverseOptionAsync (f:'T -> Async<'U option>) (source:AsyncSeq<'T>) : Async<AsyncSeq<'U> option> =
          async {
              let! ct = Async.CancellationToken
              use ie = source.GetAsyncEnumerator(ct).DisposeSynchronously()
              let! move = ie.MoveNextAsync().AsTask() |> Async.AwaitTask
              let mutable b = move
              let buffer = ResizeArray<_>()
              let mutable fail = false
              while b && not fail do
                  let! vOpt = f ie.Current
                  match vOpt  with
                  | Some v -> buffer.Add v
                  | None ->
                      b <- false
                      fail <- true
                  let! moven = ie.MoveNextAsync().AsTask() |> Async.AwaitTask
                  b <- moven
              if fail then
                 return None
              else
                 let res = buffer.ToArray()
                 return Some (asyncSeq { for v in res do yield v })
          }

      let traverseChoiceAsync (f:'T -> Async<Choice<'U, 'e>>) (source:AsyncSeq<'T>) : Async<Choice<AsyncSeq<'U>, 'e>> =
          async {
              let! ct = Async.CancellationToken
              use ie = source.GetAsyncEnumerator(ct).DisposeSynchronously()
              let! move = ie.MoveNextAsync().AsTask() |> Async.AwaitTask
              let mutable b = move
              let buffer = ResizeArray<_>()
              let mutable fail = None
              while b && fail.IsNone do
                  let! vOpt = f ie.Current
                  match vOpt  with
                  | Choice1Of2 v -> buffer.Add v
                  | Choice2Of2 err ->
                      b <- false
                      fail <- Some err
                  let! moven = ie.MoveNextAsync().AsTask() |> Async.AwaitTask
                  b <- moven
              match fail with
              | Some err -> return Choice2Of2 err
              | None ->
                 let res = buffer.ToArray()
                 return Choice1Of2 (asyncSeq { for v in res do yield v })
           }

      #if (NETSTANDARD || NET)
      #if !FABLE_COMPILER

      let ofAsyncEnum (source: IAsyncEnumerable<_>) = source

      let toAsyncEnum (source: AsyncSeq<'T>) = source

      let ofIQueryable (query: IQueryable<'T>) =
         query :?> IAsyncEnumerable<'T> |> ofAsyncEnum

      //module AsyncSeqSrcImpl =

      //  let private createNode () =
      //    new AsyncSeqSrcNode<_>(new TaskCompletionSource<_>())

      //  let create () : AsyncSeqSrc<'T> =
      //    { tail = ref (createNode ()) }

      //  let put (a:'T) (s:AsyncSeqSrc<'T>) =
      //    let newTail = createNode ()
      //    let tail = Interlocked.Exchange(s.tail, newTail)
      //    tail.tcs.SetResult(Some(a, newTail))

      //  let close (s:AsyncSeqSrc<'T>) : unit =
      //    s.tail.Value.tcs.SetResult(None)

      //  let error (ex:exn) (s:AsyncSeqSrc<'T>) : unit =
      //    s.tail.Value.tcs.SetException(ex)

      //  let rec private toAsyncSeqImpl (s:AsyncSeqSrcNode<'T>) : AsyncSeq<'T> =
      //    asyncSeq {
      //      let! next = s.tcs.Task |> Async.AwaitTask
      //      match next with
      //      | None -> ()
      //      | Some (a,tl) ->
      //        yield a
      //        yield! toAsyncSeqImpl tl }

      //  let toAsyncSeq (s:AsyncSeqSrc<'T>) : AsyncSeq<'T> =
      //    toAsyncSeqImpl s.tail.Value


      let groupByAsync (p:'T -> Task<'k>) (s:AsyncSeq<'T>) : AsyncSeq<'k * AsyncSeq<'T>> = asyncSeq {
        let groups = Dictionary<'k, AsyncSeqSrc< 'T>>()
        let close group =
          AsyncSeqSrcImpl.close group
        let closeGroups () =
          groups.Values |> Seq.iter close
        use enum = s.GetAsyncEnumerator(ct)
        let rec go () = asyncSeq {
          try
            let! next = enum.MoveNext ()
            match next with
            | None -> closeGroups ()
            | Some a ->
              let! key = p a
              let mutable group = Unchecked.defaultof<_>
              if groups.TryGetValue(key, &group) then
                AsyncSeqSrcImpl.put a group
                yield! go ()
              else
                let src = AsyncSeqSrcImpl.create ()
                let subSeq = src |> AsyncSeqSrcImpl.toAsyncSeq
                AsyncSeqSrcImpl.put a src
                let group = src
                groups.Add(key, group)
                yield key,subSeq
                yield! go ()
          with ex ->
            closeGroups ()
            raise ex }
        yield! go () }

      let groupBy (p:'T -> 'k) (s:AsyncSeq<'T>) : AsyncSeq<'k * AsyncSeq<'T>> =
        groupByAsync (p >> async.Return) s
  #endif
  #endif



[<AutoOpen>]
module AsyncSeqExtensions =
    let asyncSeq = new AsyncSeq.AsyncSeqBuilder()

    // Add asynchronous for loop to the 'Async' computation builder
    type Microsoft.FSharp.Control.AsyncBuilder with
        member x.For (seq:AsyncSeq<'T>, action:'T -> Async<unit>) =
            seq |> AsyncSeq.iterAsyncAsync action

#if !FABLE_COMPILER
//[<RequireQualifiedAccess>]
//module AsyncSeqSrc =

//    let create () = AsyncSeq.AsyncSeqSrcImpl.create ()
//    let put a s = AsyncSeq.AsyncSeqSrcImpl.put a s
//    let close s = AsyncSeq.AsyncSeqSrcImpl.close s
//    let toAsyncSeq s = AsyncSeq.AsyncSeqSrcImpl.toAsyncSeq s
//    let error e s = AsyncSeq.AsyncSeqSrcImpl.error e s

module Seq =
    let ofAsyncSeq (source: AsyncSeq<'T>) =
        AsyncSeq.toBlockingSeq source
#endif

[<assembly:System.Runtime.CompilerServices.InternalsVisibleTo("FSharp.Control.AsyncSeq.Tests")>]
do ()

