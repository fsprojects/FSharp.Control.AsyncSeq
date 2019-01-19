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

#nowarn "40"

// ----------------------------------------------------------------------------

type IAsyncEnumerator<'T> =
    abstract MoveNext : unit -> Async<'T option>
    inherit IDisposable

type IAsyncEnumerable<'T> = 
    abstract GetEnumerator : unit -> IAsyncEnumerator<'T>

type AsyncSeq<'T> = IAsyncEnumerable<'T>

type AsyncSeqSrc<'a> = private { tail : AsyncSeqSrcNode<'a> ref }

and private AsyncSeqSrcNode<'a> =
  val tcs : TaskCompletionSource<('a * AsyncSeqSrcNode<'a>) option>
  new (tcs) = { tcs = tcs }

[<AutoOpen>]
module internal Utils = 
    
    module Choice =
  
      /// Maps over the left result type.
      let mapl (f:'T -> 'U) = function
        | Choice1Of2 a -> f a |> Choice1Of2
        | Choice2Of2 e -> Choice2Of2 e

    module Disposable =
  
      let empty : IDisposable =
        { new IDisposable with member __.Dispose () = () }

    // ----------------------------------------------------------------------------

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
        /// kinds of events (error, next, completed)
        let asUpdates (source:IObservable<'T>) = 
          { new IObservable<_> with
              member x.Subscribe(observer) =
                source.Subscribe
                  ({ new IObserver<_> with
                      member x.OnNext(v) = observer.OnNext(Next v)
                      member x.OnCompleted() = observer.OnNext(Completed) 
                      member x.OnError(e) = observer.OnNext(Error (ExceptionDispatchInfo.Capture e)) }) }

    type Microsoft.FSharp.Control.Async with

      static member bind (f:'a -> Async<'b>) (a:Async<'a>) : Async<'b> = async.Bind(a, f)

      static member awaitTaskUnitCancellationAsError (t:Task) : Async<unit> =
        Async.FromContinuations <| fun (ok,err,_) ->
          t.ContinueWith (fun (t:Task) ->
            if t.IsFaulted then err t.Exception
            elif t.IsCanceled then err (OperationCanceledException("Task wrapped with Async has been cancelled."))
            elif t.IsCompleted then ok ()
            else failwith "invalid Task state!") |> ignore

      static member awaitTaskCancellationAsError (t:Task<'a>) : Async<'a> =
        Async.FromContinuations <| fun (ok,err,_) ->
          t.ContinueWith (fun (t:Task<'a>) ->
            if t.IsFaulted then err t.Exception
            elif t.IsCanceled then err (OperationCanceledException("Task wrapped with Async has been cancelled."))
            elif t.IsCompleted then ok t.Result
            else failwith "invalid Task state!") |> ignore

      /// Starts the specified operation using a new CancellationToken and returns
      /// IDisposable object that cancels the computation. This method can be used
      /// when implementing the Subscribe method of IObservable interface.
      static member StartDisposable(op:Async<unit>) =
          let ct = new System.Threading.CancellationTokenSource()
          Async.Start(op, ct.Token)
          { new IDisposable with 
              member x.Dispose() = ct.Cancel() }

      static member map f a = async.Bind(a, f >> async.Return)

      static member internal chooseTasks (a:Task<'T>) (b:Task<'U>) : Async<Choice<'T * Task<'U>, 'U * Task<'T>>> =
        async { 
            let ta, tb = a :> Task, b :> Task
            let! i = Task.WhenAny( ta, tb ) |> Async.AwaitTask
            if i = ta then return (Choice1Of2 (a.Result, b))
            elif i = tb then return (Choice2Of2 (b.Result, a)) 
            else return! failwith "unreachable" }

      static member internal chooseTasks2 (a:Task<'T>) (b:Task) : Async<Choice<'T * Task, Task<'T>>> =
        async { 
            let ta = a :> Task
            let! i = Task.WhenAny( ta, b ) |> Async.AwaitTask
            if i = ta then return (Choice1Of2 (a.Result, b))
            elif i = b then return (Choice2Of2 (a)) 
            else return! failwith "unreachable" }

    type MailboxProcessor<'Msg> with
      member __.PostAndAsyncReplyTask (f:TaskCompletionSource<'a> -> 'Msg) : Task<'a> =
        let tcs = new TaskCompletionSource<'a>()
        __.Post (f tcs)
        tcs.Task

    module Task =

      let inline join (t:Task<Task<'a>>) : Task<'a> =
        t.Unwrap()

      let inline extend (f:Task<'a> -> 'b) (t:Task<'a>) : Task<'b> =
        t.ContinueWith f      

      let chooseTaskAsTask (t:Task<'a>) (a:Async<'a>) = async {
        let! a = Async.StartChildAsTask a
        return Task.WhenAny (t, a) |> join }

      let chooseTask (t:Task<'a>) (a:Async<'a>) : Async<'a> =
        chooseTaskAsTask t a |> Async.bind Async.awaitTaskCancellationAsError

      let toUnit (t:Task) : Task<unit> =
        t.ContinueWith (Func<_, _>(fun (_:Task) -> ()))

      let taskFault (t:Task<'a>) : Task<'b> =
        t 
        |> extend (fun t -> 
          let ivar = TaskCompletionSource<_>()
          if t.IsFaulted then
            ivar.SetException t.Exception
          ivar.Task)
        |> join


// via: https://github.com/fsharp/fsharp/blob/master/src/fsharp/FSharp.Core/seq.fs
module AsyncGenerator =

  type Step<'a> =
    | Stop
    | Yield of 'a
      
    /// Jump to another generator.
    | Goto of AsyncGenerator<'a>

  and AsyncGenerator<'a> =
    abstract Apply : unit -> Async<Step<'a>>
    abstract Disposer : (unit -> unit) option
    
  let disposeG (g:AsyncGenerator<'a>) =
    match g.Disposer with
    | None -> ()
    | Some f -> f ()
  
  let appG (g:AsyncGenerator<'a>) = async {
    let! res = g.Apply ()
    match res with
    | Goto next -> return Goto next
    | Yield _ -> return res
    | Stop ->
      disposeG g
      return res }

  type GenerateCont<'a> (g:AsyncGenerator<'a>, cont:unit -> AsyncGenerator<'a>) =
    member __.Generator = g
    member __.Cont = cont
    interface AsyncGenerator<'a> with
      member x.Apply () = async {
        let! step = appG g
        match step with
        | Stop -> return Goto (cont ())
        | Yield _ as res -> return res
        | Goto next -> return Goto (GenerateCont<_>.Bind (next, cont)) }
      member x.Disposer =
        g.Disposer
                
    static member Bind (g:AsyncGenerator<'a>, cont:unit -> AsyncGenerator<'a>) : AsyncGenerator<'a> =
      match g with
      | :? GenerateCont<'a> as g -> GenerateCont<_>.Bind (g.Generator, (fun () -> GenerateCont<_>.Bind (g.Cont(), cont)))
      | _ -> (new GenerateCont<'a>(g, cont) :> AsyncGenerator<'a>)
      
  /// Right-associating binder.
  let bindG (g:AsyncGenerator<'a>) (cont:unit -> AsyncGenerator<'a>) : AsyncGenerator<'a> =
    GenerateCont<_>.Bind (g, cont)    

  /// Converts a generator to an enumerator.
  /// The generator can point to another generator using Goto, in which case
  /// the enumerator mutates its current generator and continues.
  type AsyncGeneratorEnumerator<'a> (g:AsyncGenerator<'a>) =
    let mutable g = g
    let mutable fin = false
    member __.Generator = g
    interface IAsyncEnumerator<'a> with        
      member x.MoveNext () = async {
        let! step = appG g
        match step with
        | Stop ->
          fin <- true
          return None
        | Yield a ->
          return Some a
        | Goto next ->
          g <- next
          return! (x :> IAsyncEnumerator<_>).MoveNext() }
      member __.Dispose () =
        disposeG g
      
  /// Converts an enumerator to a generator.
  /// The resulting generator will either yield or stop.
  type AsyncEnumeratorGenerator<'a> (enum:IAsyncEnumerator<'a>) =
    member __.Enumerator = enum
    interface AsyncGenerator<'a> with
      member __.Apply () = async {
        let! next = enum.MoveNext()
        match next with
        | Some a ->
          return Yield a
        | None ->
          return Stop }
      member __.Disposer = Some ((fun () -> (enum :> IDisposable).Dispose()))

  let enumeratorFromGenerator (g:AsyncGenerator<'a>) : IAsyncEnumerator<'a> =
    match g with
    | :? AsyncEnumeratorGenerator<'a> as g -> g.Enumerator
    | _ -> (new AsyncGeneratorEnumerator<_>(g) :> _)

  let generatorFromEnumerator (e:IAsyncEnumerator<'a>) : AsyncGenerator<'a> =
    match e with
    | :? AsyncGeneratorEnumerator<'a> as e -> e.Generator
    | _ -> (new AsyncEnumeratorGenerator<_>(e) :> _)
      
  let delay (f:unit -> AsyncSeq<'T>) : AsyncSeq<'T> = 
    { new IAsyncEnumerable<'T> with 
        member x.GetEnumerator() = f().GetEnumerator() }

  let emitEnum (e:IAsyncEnumerator<'a>) : AsyncSeq<'a> =
    { new IAsyncEnumerable<_> with
        member __.GetEnumerator () = e }

  let fromGeneratorDelay (f:unit -> AsyncGenerator<'a>) : AsyncSeq<'a> =
    delay (fun () -> emitEnum (enumeratorFromGenerator (f ())))

  let toGenerator (s:AsyncSeq<'a>) : AsyncGenerator<'a> =
    generatorFromEnumerator (s.GetEnumerator())

  let append (s1:AsyncSeq<'a>) (s2:AsyncSeq<'a>) : AsyncSeq<'a> =
    fromGeneratorDelay (fun () -> bindG (toGenerator s1) (fun () -> toGenerator s2))

//  let collect (f:'a -> AsyncSeq<'b>) (s:AsyncSeq<'a>) : AsyncSeq<'b> =
//    fromGeneratorDelay (fun () -> collectG (toGenerator s) (f >> toGenerator))


[<AbstractClass>]
type AsyncSeqOp<'T> () =
  abstract member ChooseAsync : ('T -> Async<'U option>) -> AsyncSeq<'U>
  abstract member FoldAsync : ('S -> 'T -> Async<'S>) -> 'S -> Async<'S>
  abstract member MapAsync : ('T -> Async<'U>) -> AsyncSeq<'U>
  abstract member IterAsync : ('T -> Async<unit>) -> Async<unit>
  default x.MapAsync (f:'T -> Async<'U>) : AsyncSeq<'U> =
    x.ChooseAsync (f >> Async.map Some)
  default x.IterAsync (f:'T -> Async<unit>) : Async<unit> =
    x.FoldAsync (fun () t -> f t) ()

[<AutoOpen>]
module AsyncSeqOp =

  type UnfoldAsyncEnumerator<'S, 'T> (f:'S -> Async<('T * 'S) option>, init:'S) =
    inherit AsyncSeqOp<'T> ()
    override x.IterAsync g = async {
      let rec go s = async {
        let! next = f s
        match next with
        | None -> return ()
        | Some (t,s') ->
          do! g t
          return! go s' }
      return! go init }
    override __.FoldAsync (g:'S2 -> 'T -> Async<'S2>) (init2:'S2) = async {
      let rec go s s2 = async {
        let! next = f s
        match next with
        | None -> return s2
        | Some (t,s') ->
          let! s2' = g s2 t
          return! go s' s2' }
      return! go init init2 }
    override __.ChooseAsync (g:'T -> Async<'U option>) : AsyncSeq<'U> =
      let rec h s = async {
        let! res = f s
        match res with
        | None -> 
          return None
        | Some (t,s) ->
          let! res' = g t
          match res' with
          | Some u ->
            return Some (u, s)
          | None ->
            return! h s }
      new UnfoldAsyncEnumerator<'S, 'U> (h, init) :> _
    override __.MapAsync (g:'T -> Async<'U>) : AsyncSeq<'U> =
      let h s = async {
        let! r = f s
        match r with
        | Some (t,s) ->
          let! u = g t
          return Some (u,s)
        | None ->
          return None }
      new UnfoldAsyncEnumerator<'S, 'U> (h, init) :> _
    interface IAsyncEnumerable<'T> with
      member __.GetEnumerator () =
        let s = ref init
        { new IAsyncEnumerator<'T> with
            member __.MoveNext () : Async<'T option> = async {
              let! next = f !s 
              match next with
              | None -> 
                return None 
              | Some (a,s') ->
                s := s'
                return Some a }
            member __.Dispose () = () }



/// Module with helper functions for working with asynchronous sequences
module AsyncSeq = 

  let private dispose (d:System.IDisposable) = match d with null -> () | _ -> d.Dispose()


  [<GeneralizableValue>]
  let empty<'T> : AsyncSeq<'T> = 
        { new IAsyncEnumerable<'T> with 
              member x.GetEnumerator() = 
                  { new IAsyncEnumerator<'T> with 
                        member x.MoveNext() = async { return None }
                        member x.Dispose() = () } }
 
  let singleton (v:'T) : AsyncSeq<'T> = 
        { new IAsyncEnumerable<'T> with 
              member x.GetEnumerator() = 
                  let state = ref 0
                  { new IAsyncEnumerator<'T> with 
                        member x.MoveNext() = async { let res = state.Value = 0
                                                      incr state; 
                                                      return (if res then Some v else None) }
                        member x.Dispose() = () } }
    
  let append (inp1: AsyncSeq<'T>) (inp2: AsyncSeq<'T>) : AsyncSeq<'T> =
    AsyncGenerator.append inp1 inp2

  let inline delay (f: unit -> AsyncSeq<'T>) : AsyncSeq<'T> = 
    AsyncGenerator.delay f

  let bindAsync (f:'T -> AsyncSeq<'U>) (inp:Async<'T>) : AsyncSeq<'U> =
    { new IAsyncEnumerable<'U> with
        member x.GetEnumerator () = 
          { new AsyncGenerator.AsyncGenerator<'U> with
              member x.Apply () = async {
                  let! v = inp
                  let cont = 
                    (f v).GetEnumerator() 
                    |> AsyncGenerator.generatorFromEnumerator
                  return AsyncGenerator.Goto cont
                }
              member x.Disposer = None
          } 
          |> AsyncGenerator.enumeratorFromGenerator
    }



  type AsyncSeqBuilder() =
    member x.Yield(v) =
      singleton v
    // This looks weird, but it is needed to allow:
    //
    //   while foo do
    //     do! something
    //
    // because F# translates body as Bind(something, fun () -> Return())
    member x.Return _ = empty
    member x.YieldFrom(s:AsyncSeq<'T>) =
      s
    member x.Zero () = empty
    member x.Bind (inp:Async<'T>, body : 'T -> AsyncSeq<'U>) : AsyncSeq<'U> = bindAsync body inp
    member x.Combine (seq1:AsyncSeq<'T>, seq2:AsyncSeq<'T>) = 
      AsyncGenerator.append seq1 seq2
    member x.While (guard, body:AsyncSeq<'T>) = 
      // Use F#'s support for Landin's knot for a low-allocation fixed-point
      let rec fix = delay (fun () -> if guard() then AsyncGenerator.append body fix else empty)
      fix
    member x.Delay (f:unit -> AsyncSeq<'T>) = 
      delay f

      
  let asyncSeq = new AsyncSeqBuilder()


  let emitEnumerator (ie: IAsyncEnumerator<'T>) = asyncSeq {
      let! moven = ie.MoveNext() 
      let b = ref moven 
      while b.Value.IsSome do
          yield b.Value.Value 
          let! moven = ie.MoveNext() 
          b := moven }

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
              member x.GetEnumerator() = 
                  let state = ref (TryWithState.NotStarted inp)
                  { new IAsyncEnumerator<'T> with 
                        member x.MoveNext() = 
                            async { match !state with 
                                    | TryWithState.NotStarted inp -> 
                                        let res = ref Unchecked.defaultof<_>
                                        try 
                                            res := Choice1Of2 (inp.GetEnumerator())
                                        with exn ->
                                            res := Choice2Of2 exn
                                        match res.Value with
                                        | Choice1Of2 r ->
                                            return! 
                                              (state := TryWithState.HaveBodyEnumerator r
                                               x.MoveNext())
                                        | Choice2Of2 exn -> 
                                            return! 
                                               (x.Dispose()
                                                let enum = (handler exn).GetEnumerator()
                                                state := TryWithState.HaveHandlerEnumerator enum
                                                x.MoveNext())
                                    | TryWithState.HaveBodyEnumerator e ->   
                                        let res = ref Unchecked.defaultof<_>
                                        try 
                                            let! r = e.MoveNext()
                                            res := Choice1Of2 r
                                        with exn -> 
                                            res := Choice2Of2 exn
                                        match res.Value with 
                                        | Choice1Of2 res -> 
                                            return 
                                                (match res with 
                                                 | None -> x.Dispose()
                                                 | _ -> ()
                                                 res)
                                        | Choice2Of2 exn -> 
                                            return! 
                                              (x.Dispose()
                                               let e = (handler exn).GetEnumerator()
                                               state := TryWithState.HaveHandlerEnumerator e
                                               x.MoveNext())
                                    | TryWithState.HaveHandlerEnumerator e ->   
                                        let! res = e.MoveNext() 
                                        return (match res with 
                                                | Some _ -> res
                                                | None -> x.Dispose(); None)
                                    | _ -> 
                                        return None }
                        member x.Dispose() = 
                            match !state with 
                            | TryWithState.HaveBodyEnumerator e | TryWithState.HaveHandlerEnumerator e -> 
                                state := TryWithState.Finished
                                dispose e 
                            | _ -> () } }
 

  [<RequireQualifiedAccess>]
  type TryFinallyState<'T> =
     | NotStarted    of AsyncSeq<'T>
     | HaveBodyEnumerator of IAsyncEnumerator<'T>
     | Finished 

  // This pushes the handler through all the async computations
  // The (synchronous) compensation is run when the Dispose() is called
  let tryFinally (inp: AsyncSeq<'T>) (compensation : unit -> unit) : AsyncSeq<'T> = 
        { new IAsyncEnumerable<'T> with 
              member x.GetEnumerator() = 
                  let state = ref (TryFinallyState.NotStarted inp)
                  { new IAsyncEnumerator<'T> with 
                        member x.MoveNext() = 
                            async { match !state with 
                                    | TryFinallyState.NotStarted inp -> 
                                        return! 
                                           (let e = inp.GetEnumerator()
                                            state := TryFinallyState.HaveBodyEnumerator e
                                            x.MoveNext())
                                    | TryFinallyState.HaveBodyEnumerator e ->   
                                        let! res = e.MoveNext() 
                                        return 
                                           (match res with 
                                            | None -> x.Dispose()
                                            | Some _ -> ()
                                            res)
                                    | _ -> 
                                        return None }
                        member x.Dispose() = 
                            match !state with 
                            | TryFinallyState.HaveBodyEnumerator e-> 
                                state := TryFinallyState.Finished
                                dispose e 
                                compensation()
                            | _ -> () } }


  [<RequireQualifiedAccess>]
  type CollectState<'T,'U> =
     | NotStarted    of AsyncSeq<'T>
     | HaveInputEnumerator of IAsyncEnumerator<'T>
     | HaveInnerEnumerator of IAsyncEnumerator<'T> * IAsyncEnumerator<'U>
     | Finished 

  let collect (f: 'T -> AsyncSeq<'U>) (inp: AsyncSeq<'T>) : AsyncSeq<'U> = 
        { new IAsyncEnumerable<'U> with 
              member x.GetEnumerator() = 
                  let state = ref (CollectState.NotStarted inp)
                  { new IAsyncEnumerator<'U> with 
                        member x.MoveNext() = 
                            async { match !state with 
                                    | CollectState.NotStarted inp -> 
                                        return! 
                                           (let e1 = inp.GetEnumerator()
                                            state := CollectState.HaveInputEnumerator e1
                                            x.MoveNext())
                                    | CollectState.HaveInputEnumerator e1 ->   
                                        let! res1 = e1.MoveNext() 
                                        return! 
                                           (match res1 with
                                            | Some v1 ->
                                                let e2 = (f v1).GetEnumerator()
                                                state := CollectState.HaveInnerEnumerator (e1, e2)
                                            | None -> 
                                                x.Dispose()
                                            x.MoveNext())
                                    | CollectState.HaveInnerEnumerator (e1, e2) ->   
                                        let! res2 = e2.MoveNext() 
                                        match res2 with 
                                        | None ->
                                            state := CollectState.HaveInputEnumerator e1
                                            dispose e2
                                            return! x.MoveNext()
                                        | Some _ -> 
                                            return res2
                                    | _ -> 
                                        return None }
                        member x.Dispose() = 
                            match !state with 
                            | CollectState.HaveInputEnumerator e1 -> 
                                state := CollectState.Finished
                                dispose e1 
                            | CollectState.HaveInnerEnumerator (e1, e2) -> 
                                state := CollectState.Finished
                                dispose e2
                                dispose e1 
                            | _ -> () } }

//  let collect (f: 'T -> AsyncSeq<'U>) (inp: AsyncSeq<'T>) : AsyncSeq<'U> =
//    AsyncGenerator.collect f inp

  [<RequireQualifiedAccess>]
  type CollectSeqState<'T,'U> =
     | NotStarted    of seq<'T>
     | HaveInputEnumerator of IEnumerator<'T>
     | HaveInnerEnumerator of IEnumerator<'T> * IAsyncEnumerator<'U>
     | Finished 

  // Like collect, but the input is a sequence, where no bind is required on each step of the enumeration
  let collectSeq (f: 'T -> AsyncSeq<'U>) (inp: seq<'T>) : AsyncSeq<'U> = 
        { new IAsyncEnumerable<'U> with 
              member x.GetEnumerator() = 
                  let state = ref (CollectSeqState.NotStarted inp)
                  { new IAsyncEnumerator<'U> with 
                        member x.MoveNext() = 
                            async { match !state with 
                                    | CollectSeqState.NotStarted inp -> 
                                        return! 
                                           (let e1 = inp.GetEnumerator()
                                            state := CollectSeqState.HaveInputEnumerator e1
                                            x.MoveNext())
                                    | CollectSeqState.HaveInputEnumerator e1 ->   
                                        return! 
                                          (if e1.MoveNext()  then 
                                               let e2 = (f e1.Current).GetEnumerator()
                                               state := CollectSeqState.HaveInnerEnumerator (e1, e2)
                                           else
                                               x.Dispose()
                                           x.MoveNext())
                                    | CollectSeqState.HaveInnerEnumerator (e1, e2)->   
                                        let! res2 = e2.MoveNext() 
                                        match res2 with 
                                        | None ->
                                            return! 
                                              (state := CollectSeqState.HaveInputEnumerator e1
                                               dispose e2
                                               x.MoveNext())
                                        | Some _ -> 
                                            return res2
                                    | _ -> return None}
                        member x.Dispose() = 
                            match !state with 
                            | CollectSeqState.HaveInputEnumerator e1 -> 
                                state := CollectSeqState.Finished
                                dispose e1 
                            | CollectSeqState.HaveInnerEnumerator (e1, e2) -> 
                                state := CollectSeqState.Finished
                                dispose e2
                                dispose e1
                                x.Dispose()
                            | _ -> () } }

  [<RequireQualifiedAccess>]
  type MapState<'T> =
     | NotStarted    of seq<'T>
     | HaveEnumerator of IEnumerator<'T>
     | Finished 

  let ofSeq (inp: seq<'T>) : AsyncSeq<'T> = 
        { new IAsyncEnumerable<'T> with 
              member x.GetEnumerator() = 
                  let state = ref (MapState.NotStarted inp)
                  { new IAsyncEnumerator<'T> with 
                        member x.MoveNext() = 
                            async { match !state with 
                                    | MapState.NotStarted inp -> 
                                        let e = inp.GetEnumerator()
                                        state := MapState.HaveEnumerator e
                                        return! x.MoveNext()
                                    | MapState.HaveEnumerator e ->   
                                        return 
                                            (if e.MoveNext()  then 
                                                 Some e.Current
                                             else 
                                                 x.Dispose()
                                                 None)
                                    | _ -> return None }
                        member x.Dispose() = 
                            match !state with 
                            | MapState.HaveEnumerator e -> 
                                state := MapState.Finished
                                dispose e 
                            | _ -> () } }

  let iteriAsync f (source : AsyncSeq<_>) = 
      async { 
          use ie = source.GetEnumerator()
          let count = ref 0
          let! move = ie.MoveNext()
          let b = ref move
          while b.Value.IsSome do
              do! f !count b.Value.Value
              let! moven = ie.MoveNext()
              do incr count
                 b := moven
      }
  
  let iterAsync (f: 'T -> Async<unit>) (source: AsyncSeq<'T>)  = 
    match source with
    | :? AsyncSeqOp<'T> as source -> source.IterAsync f
    | _ -> iteriAsync (fun i x -> f x) source
  
  let iteri (f: int -> 'T -> unit) (inp: AsyncSeq<'T>)  = iteriAsync (fun i x -> async.Return (f i x)) inp


  // Add additional methods to the 'asyncSeq' computation builder
  type AsyncSeqBuilder with

    member x.TryFinally (body: AsyncSeq<'T>, compensation) = 
      tryFinally body compensation   

    member x.TryWith (body: AsyncSeq<_>, handler: (exn -> AsyncSeq<_>)) = 
      tryWith body handler

    member x.Using (resource: 'T, binder: 'T -> AsyncSeq<'U>) = 
      tryFinally (binder resource) (fun () -> 
        if box resource <> null then dispose resource)

    member x.For (seq:seq<'T>, action:'T -> AsyncSeq<'TResult>) = 
      collectSeq action seq

    member x.For (seq:AsyncSeq<'T>, action:'T -> AsyncSeq<'TResult>) = 
      collect action seq

       
  // Add asynchronous for loop to the 'async' computation builder
  type Microsoft.FSharp.Control.AsyncBuilder with
    member internal x.For (seq:AsyncSeq<'T>, action:'T -> Async<unit>) = 
      seq |> iterAsync action 

  let unfoldAsync (f:'State -> Async<('T * 'State) option>) (s:'State) : AsyncSeq<'T> = 
    new UnfoldAsyncEnumerator<_, _>(f, s) :> _

  let replicateInfinite (v:'T) : AsyncSeq<'T> =    
    let gen _ = async {
      return Some (v,0) }
    unfoldAsync gen 0

  let replicateInfiniteAsync (v:Async<'T>) : AsyncSeq<'T> =
    let gen _ = async {
      let! v = v
      return Some (v,0) }
    unfoldAsync gen 0

  let replicate (count:int) (v:'T) : AsyncSeq<'T> =    
    let gen i = async {
      if i = count then return None
      else return Some (v,i + 1) }
    unfoldAsync gen 0

  let replicateUntilNoneAsync (next:Async<'a option>) : AsyncSeq<'a> =
    unfoldAsync 
      (fun () -> next |> Async.map (Option.map (fun a -> a,()))) 
      ()

  let intervalMs (periodMs:int) = asyncSeq {
    yield DateTime.UtcNow
    while true do
      do! Async.Sleep periodMs
      yield DateTime.UtcNow }

  // --------------------------------------------------------------------------
  // Additional combinators (implemented as async/asyncSeq computations)

  let mapAsync f (source : AsyncSeq<'T>) : AsyncSeq<'TResult> =
    match source with
    | :? AsyncSeqOp<'T> as source -> source.MapAsync f
    | _ -> 
      asyncSeq {
        for itm in source do 
        let! v = f itm
        yield v }

  let mapiAsync f (source : AsyncSeq<'T>) : AsyncSeq<'TResult> = asyncSeq {
    let i = ref 0L
    for itm in source do 
      let! v = f i.Value itm
      i := i.Value + 1L
      yield v }

  let mapAsyncParallel (f:'a -> Async<'b>) (s:AsyncSeq<'a>) : AsyncSeq<'b> = asyncSeq {
    use mb = MailboxProcessor.Start (fun _ -> async.Return())
    let! err =
      s 
      |> iterAsync (fun a -> async {
        let! b = Async.StartChild (f a)
        mb.Post (Some b) })
      |> Async.map (fun _ -> mb.Post None)
      |> Async.StartChildAsTask
    yield! 
      replicateUntilNoneAsync (Task.chooseTask (err |> Task.taskFault) (async.Delay mb.Receive))
      |> mapAsync id }

  let chooseAsync f (source:AsyncSeq<'T>) =
    match source with
    | :? AsyncSeqOp<'T> as source -> source.ChooseAsync f
    | _ ->
      asyncSeq {
        for itm in source do
          let! v = f itm
          match v with 
          | Some v -> yield v 
          | _ -> () }

  let ofSeqAsync (source:seq<Async<'T>>) : AsyncSeq<'T> =
      asyncSeq {
          for asyncElement in source do
              let! v = asyncElement
              yield v
      }

  let filterAsync f (source : AsyncSeq<'T>) = asyncSeq {
    for v in source do
      let! b = f v
      if b then yield v }

  let tryLast (source : AsyncSeq<'T>) = async { 
      use ie = source.GetEnumerator() 
      let! v = ie.MoveNext()
      let b = ref v
      let res = ref None
      while b.Value.IsSome do
          res := b.Value
          let! moven = ie.MoveNext()
          b := moven
      return res.Value }

  let lastOrDefault def (source : AsyncSeq<'T>) = async { 
      let! v = tryLast source
      match v with
      | None -> return def
      | Some v -> return v }


  let tryFirst (source : AsyncSeq<'T>) = async {
      use ie = source.GetEnumerator() 
      let! v = ie.MoveNext()
      let b = ref v
      if b.Value.IsSome then 
          return b.Value
      else 
         return None }

  let firstOrDefault def (source : AsyncSeq<'T>) = async {
      let! v = tryFirst source
      match v with
      | None -> return def
      | Some v -> return v }

  let scanAsync f (state:'TState) (source : AsyncSeq<'T>) = asyncSeq { 
        yield state 
        let z = ref state
        use ie = source.GetEnumerator() 
        let! moveRes0 = ie.MoveNext()
        let b = ref moveRes0
        while b.Value.IsSome do
          let! zNext = f z.Value b.Value.Value
          z := zNext
          yield z.Value
          let! moveResNext = ie.MoveNext()
          b := moveResNext }

  let pairwise (source : AsyncSeq<'T>) = asyncSeq {
      use ie = source.GetEnumerator() 
      let! v = ie.MoveNext()
      let b = ref v
      let prev = ref None
      while b.Value.IsSome do
          let v = b.Value.Value
          match prev.Value with 
          | None -> ()
          | Some p -> yield (p, v)
          prev := Some v
          let! moven = ie.MoveNext()
          b := moven }

  let pickAsync (f:'T -> Async<'U option>) (source:AsyncSeq<'T>) = async { 
      use ie = source.GetEnumerator() 
      let! v = ie.MoveNext()
      let b = ref v
      let res = ref None
      while b.Value.IsSome && not res.Value.IsSome do
          let! fv = f b.Value.Value
          match fv with 
          | None -> 
              let! moven = ie.MoveNext()
              b := moven
          | Some _ as r -> 
              res := r
      match res.Value with
      | Some _ -> return res.Value.Value
      | None -> return raise(KeyNotFoundException()) }

  let pick f (source:AsyncSeq<'T>) =
    pickAsync (f >> async.Return) source

  let tryPickAsync f (source : AsyncSeq<'T>) = async { 
      use ie = source.GetEnumerator() 
      let! v = ie.MoveNext()
      let b = ref v
      let res = ref None
      while b.Value.IsSome && not res.Value.IsSome do
          let! fv = f b.Value.Value
          match fv with 
          | None -> 
              let! moven = ie.MoveNext()
              b := moven
          | Some _ as r -> 
              res := r
      return res.Value }

  let tryPick f (source : AsyncSeq<'T>) = 
    tryPickAsync (f >> async.Return) source 

  let contains value (source : AsyncSeq<'T>) = 
    source |> tryPick (fun v -> if v = value then Some () else None) |> Async.map Option.isSome

  let tryFind f (source : AsyncSeq<'T>) = 
    source |> tryPick (fun v -> if f v then Some v else None)

  let exists f (source : AsyncSeq<'T>) = 
    source |> tryFind f |> Async.map Option.isSome

  let forall f (source : AsyncSeq<'T>) = 
    source |> exists (f >> not) |> Async.map not

  let foldAsync f (state:'State) (source : AsyncSeq<'T>) =     
    match source with 
    | :? AsyncSeqOp<'T> as source -> source.FoldAsync f state
    | _ -> source |> scanAsync f state |> lastOrDefault state

  let fold f (state:'State) (source : AsyncSeq<'T>) = 
    foldAsync (fun st v -> f st v |> async.Return) state source 

  let length (source : AsyncSeq<'T>) = 
    fold (fun st _ -> st + 1L) 0L source 

  let inline sum (source : AsyncSeq<'T>) : Async<'T> = 
    (LanguagePrimitives.GenericZero, source) ||> fold (+)

  let scan f (state:'State) (source : AsyncSeq<'T>) = 
    scanAsync (fun st v -> f st v |> async.Return) state source 

  let unfold f (state:'State) = 
    unfoldAsync (f >> async.Return) state 

  let initInfiniteAsync f = 
    0L |> unfoldAsync (fun n -> 
        async { let! x = f n 
                return Some (x,n+1L) }) 

  let initAsync (count:int64) f = 
    0L |> unfoldAsync (fun n -> 
        async { 
            if n >= count then return None 
            else 
                let! x = f n 
                return Some (x,n+1L) }) 


  let init count f  = 
    initAsync count (f >> async.Return) 

  let initInfinite f  = 
    initInfiniteAsync (f >> async.Return) 

  let mapi f (source : AsyncSeq<'T>) = 
    mapiAsync (fun i x -> f i x |> async.Return) source

  let map f (source : AsyncSeq<'T>) = 
    mapAsync (f >> async.Return) source

  let indexed (source : AsyncSeq<'T>) = 
    mapi (fun i x -> (i,x)) source 

  let iter f (source : AsyncSeq<'T>) = 
    iterAsync (f >> async.Return) source

  let choose f (source : AsyncSeq<'T>) = 
    chooseAsync (f >> async.Return) source

  let filter f (source : AsyncSeq<'T>) =
    filterAsync (f >> async.Return) source
    
  let iterAsyncParallel (f:'a -> Async<unit>) (s:AsyncSeq<'a>) : Async<unit> = async {
    use mb = MailboxProcessor.Start (ignore >> async.Return)
    let! err =
      s 
      |> iterAsync (fun a -> async {
        let! b = Async.StartChild (f a)
        mb.Post (Some b) })
      |> Async.map (fun _ -> mb.Post None)
      |> Async.StartChildAsTask
    return!
      replicateUntilNoneAsync (Task.chooseTask (err |> Task.taskFault) (async.Delay mb.Receive))
      |> iterAsync id }

  let iterAsyncParallelThrottled (parallelism:int) (f:'a -> Async<unit>) (s:AsyncSeq<'a>) : Async<unit> = async {
    use mb = MailboxProcessor.Start (ignore >> async.Return)
    use sm = new SemaphoreSlim(parallelism)
    let! err =
      s 
      |> iterAsync (fun a -> async {
        do! sm.WaitAsync () |> Async.awaitTaskUnitCancellationAsError
        let! b = Async.StartChild (async {
          try do! f a
          finally sm.Release () |> ignore })
        mb.Post (Some b) })
      |> Async.map (fun _ -> mb.Post None)
      |> Async.StartChildAsTask
    return!
      replicateUntilNoneAsync (Task.chooseTask (err |> Task.taskFault) (async.Delay mb.Receive))
      |> iterAsync id }

  // --------------------------------------------------------------------------
  // Converting from/to synchronous sequences or IObservables


  let ofObservableBuffered (source : System.IObservable<_>) = 
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
        let fin = ref false
        while not fin.Value do 
          let! msg = agent.Receive()
          match msg with
          | Observable.ObservableUpdate.Error e -> e.Throw()
          | Observable.Completed -> fin := true
          | Observable.Next v -> yield v 
      finally 
         // Cancel on early exit 
         cts.Cancel() }

  [<System.Obsolete("Please use AsyncSeq.ofObservableBuffered. The original AsyncSeq.ofObservable doesn't guarantee that the asynchronous sequence will return all values produced by the observable",true) >]
  let ofObservable (source : System.IObservable<'T>) : AsyncSeq<'T> = failwith "no longer supported"

  let toObservable (aseq:AsyncSeq<_>) =
    { new IObservable<_> with
        member x.Subscribe(obs) = 
              async {
                try 
                  for v in aseq do 
                      obs.OnNext(v)
                  obs.OnCompleted()
                with e ->
                  obs.OnError(e) }
              |> Async.StartDisposable }

  let toBlockingSeq (source : AsyncSeq<'T>) = 
      seq { 
          // Write all elements to a blocking buffer 
          use buf = new System.Collections.Concurrent.BlockingCollection<_>()
          
          use cts = new System.Threading.CancellationTokenSource()
          use _cancel = { new IDisposable with member __.Dispose() = cts.Cancel() }
          let iteratorTask = 
              async { 
                  try 
                     do! iter (Observable.Next >> buf.Add) source 
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
    
  let cache (source : AsyncSeq<'T>) = 
    let cache = ResizeArray<_>()
    let fin = TaskCompletionSource<unit>()
    // NB: no need to dispose since we're not using timeouts
    // NB: this MBP might have some unprocessed messages in internal queue until collected
    let mbp = 
      MailboxProcessor.Start (fun mbp -> async {
        use ie = source.GetEnumerator()
        let rec loop () = async {
          let! (i:int, rep:TaskCompletionSource<'T option>) = mbp.Receive()
          if i >= cache.Count then 
            try
              let! move = ie.MoveNext()
              match move with
              | Some v -> 
                cache.Add v
              | None -> 
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
        let! next = Async.chooseTasks (fin.Task) (mbp.PostAndAsyncReplyTask (fun rep -> (i,rep))) 
        match next with
        | Choice2Of2 (Some a,_) ->
          yield a
          yield! loop (i + 1)
        | Choice2Of2 (None,_) -> ()
        | Choice1Of2 _ ->
          if i = 0 then yield! ofSeq cache
          else yield! ofSeq (cache |> Seq.skip i) }
      yield! loop 0 }

  // --------------------------------------------------------------------------

  let rec threadStateAsync (f:'State -> 'T -> Async<'U * 'State>) (state:'State) (source:AsyncSeq<'T>) : AsyncSeq<'U> = asyncSeq {
      use ie = source.GetEnumerator() 
      let! v = ie.MoveNext()
      let b = ref v
      let z = ref state
      while b.Value.IsSome do
          let v = b.Value.Value
          let! v2,z2 = f z.Value v
          yield v2
          let! moven = ie.MoveNext()
          b := moven
          z := z2 }

  let zipWithAsync (f:'T1 -> 'T2 -> Async<'U>) (source1:AsyncSeq<'T1>) (source2:AsyncSeq<'T2>) : AsyncSeq<'U> = asyncSeq {
      use ie1 = source1.GetEnumerator() 
      use ie2 = source2.GetEnumerator() 
      let! move1 = ie1.MoveNext()
      let! move2 = ie2.MoveNext()
      let b1 = ref move1
      let b2 = ref move2
      while b1.Value.IsSome && b2.Value.IsSome do
          let! res = f b1.Value.Value b2.Value.Value
          yield res
          let! move1n = ie1.MoveNext()
          let! move2n = ie2.MoveNext()
          b1 := move1n
          b2 := move2n }

  let zipWithAsyncParallel (f:'T1 -> 'T2 -> Async<'U>) (source1:AsyncSeq<'T1>) (source2:AsyncSeq<'T2>) : AsyncSeq<'U> = asyncSeq {
      use ie1 = source1.GetEnumerator() 
      use ie2 = source2.GetEnumerator() 
      let! move1 = ie1.MoveNext() |> Async.StartChild
      let! move2 = ie2.MoveNext() |> Async.StartChild
      let! move1 = move1
      let! move2 = move2
      let b1 = ref move1
      let b2 = ref move2
      while b1.Value.IsSome && b2.Value.IsSome do
          let! res = f b1.Value.Value b2.Value.Value
          yield res
          let! move1n = ie1.MoveNext() |> Async.StartChild
          let! move2n = ie2.MoveNext() |> Async.StartChild
          let! move1n = move1n
          let! move2n = move2n
          b1 := move1n
          b2 := move2n }

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
    
  let takeWhileAsync p (source : AsyncSeq<'T>) : AsyncSeq<_> = asyncSeq {
      use ie = source.GetEnumerator() 
      let! move = ie.MoveNext()
      let b = ref move
      while b.Value.IsSome do
          let v = b.Value.Value 
          let! res = p v
          if res then 
              yield v
              let! moven = ie.MoveNext()
              b := moven
          else
              b := None }

  let takeWhile p (source : AsyncSeq<'T>) = 
      takeWhileAsync (p >> async.Return) source  

  let takeUntilSignal (signal:Async<unit>) (source:AsyncSeq<'T>) : AsyncSeq<'T> = asyncSeq {
      use ie = source.GetEnumerator() 
      let! signalT = Async.StartChildAsTask signal
      let! moveT = Async.StartChildAsTask (ie.MoveNext())
      let! move = Async.chooseTasks signalT moveT
      let b = ref move
      while (match b.Value with Choice2Of2 (Some _,_) -> true | _ -> false) do
          let v,sg = (match b.Value with Choice2Of2 (Some v,sg) -> v,sg | _ -> failwith "unreachable")
          yield v
          let! moveT = Async.StartChildAsTask (ie.MoveNext())
          let! move = Async.chooseTasks sg moveT
          b := move }

  let takeUntil signal source = takeUntilSignal signal source

  let takeWhileInclusive (f : 'a -> bool) (s : AsyncSeq<'a>) : AsyncSeq<'a> = 
      { new IAsyncEnumerable<'a> with
           member __.GetEnumerator() = 
             let en = s.GetEnumerator()
             let fin = ref false
             { new IAsyncEnumerator<'a> with
                 
                 member __.MoveNext() = 
                     async { 
                         if !fin then return None
                         else 
                             let! next = en.MoveNext()
                             match next with
                             | None -> return None
                             | Some a -> 
                                 if f a then return Some a
                                 else 
                                     fin := true
                                     return Some a
                     }
                 
                 member __.Dispose() = en.Dispose() } } 

  let skipWhileAsync p (source : AsyncSeq<'T>) : AsyncSeq<_> = asyncSeq {
      use ie = source.GetEnumerator() 
      let! move = ie.MoveNext()
      let b = ref move
      let doneSkipping = ref false
      while b.Value.IsSome do
          let v = b.Value.Value
          if doneSkipping.Value then 
              yield v
          else 
              let! test = p v
              if not test then 
                  yield v
                  doneSkipping := true
          let! moven = ie.MoveNext()
          b := moven }

  let skipUntilSignal (signal:Async<unit>) (source:AsyncSeq<'T>) : AsyncSeq<'T> = asyncSeq {
      use ie = source.GetEnumerator() 
      let! signalT = Async.StartChildAsTask signal
      let! moveT = Async.StartChildAsTask (ie.MoveNext())
      let! move = Async.chooseTasks signalT moveT
      let b = ref move
      while (match b.Value with Choice2Of2 (Some _,_) -> true | _ -> false) do
          let v,sg = (match b.Value with Choice2Of2 (Some v,sg) -> v,sg | _ -> failwith "unreachable")
          let! moveT = Async.StartChildAsTask (ie.MoveNext())
          let! move = Async.chooseTasks sg moveT
          b := move 
      match b.Value with 
      | Choice2Of2 (None,_) -> 
          ()
      | Choice1Of2 (_,rest) -> 
          let! move = Async.AwaitTask rest
          let b2 = ref move
          // Yield the rest of the sequence
          while b2.Value.IsSome do
              let v = b2.Value.Value
              yield v
              let! moven = ie.MoveNext()
              b2 := moven 
      | Choice2Of2 (Some _,_) -> failwith "unreachable" }

  let skipUntil signal source = skipUntilSignal signal source

  let skipWhile p (source : AsyncSeq<'T>) = 
      skipWhileAsync (p >> async.Return) source

  let take count (source : AsyncSeq<'T>) : AsyncSeq<_> = asyncSeq {
      if (count < 0) then invalidArg "count" "must be non-negative"
      use ie = source.GetEnumerator() 
      let n = ref count
      if n.Value > 0 then 
          let! move = ie.MoveNext()
          let b = ref move
          while b.Value.IsSome do
              yield b.Value.Value 
              n := n.Value - 1 
              if n.Value > 0 then 
                  let! moven = ie.MoveNext()
                  b := moven
              else b := None }
  
  let truncate count source = take count source

  let skip count (source : AsyncSeq<'T>) : AsyncSeq<_> = asyncSeq {
      if (count < 0) then invalidArg "count" "must be non-negative"
      use ie = source.GetEnumerator() 
      let! move = ie.MoveNext()
      let b = ref move
      let n = ref count
      while b.Value.IsSome do
          if n.Value = 0 then 
              yield b.Value.Value 
          else
              n := n.Value - 1
          let! moven = ie.MoveNext()
          b := moven }

  let toArrayAsync (source : AsyncSeq<'T>) : Async<'T[]> = async {
      let ra = (new ResizeArray<_>()) 
      use ie = source.GetEnumerator() 
      let! move = ie.MoveNext()
      let b = ref move
      while b.Value.IsSome do
          ra.Add b.Value.Value 
          let! moven = ie.MoveNext()
          b := moven
      return ra.ToArray() }

  let toListAsync (source:AsyncSeq<'T>) : Async<'T list> = toArrayAsync source |> Async.map Array.toList
  let toListSynchronously (source:AsyncSeq<'T>) = toListAsync source |> Async.RunSynchronously
  let toArraySynchronously (source:AsyncSeq<'T>) = toArrayAsync source |> Async.RunSynchronously

  let concatSeq (source:AsyncSeq<#seq<'T>>) : AsyncSeq<'T> = asyncSeq {
      use ie = source.GetEnumerator() 
      let! move = ie.MoveNext()
      let b = ref move
      while b.Value.IsSome do
          for x in (b.Value.Value :> seq<'T>)  do
              yield x
          let! moven = ie.MoveNext()
          b := moven }

  let concat (source:AsyncSeq<AsyncSeq<'T>>) : AsyncSeq<'T> =
      asyncSeq {
          for innerSeq in source do
              for e in innerSeq do
                  yield e
      }        

  let interleaveChoice (source1: AsyncSeq<'T1>) (source2: AsyncSeq<'T2>) = asyncSeq {
      use ie1 = (source1 |> map Choice1Of2).GetEnumerator() 
      use ie2 = (source2 |> map Choice2Of2).GetEnumerator() 
      let! move = ie1.MoveNext()
      let is1 = ref true
      let b = ref move
      while b.Value.IsSome do
          yield b.Value.Value 
          is1 := not is1.Value
          let! moven = (if is1.Value then ie1.MoveNext() else ie2.MoveNext())
          b := moven 
      // emit the rest
      yield! emitEnumerator (if is1.Value then ie2 else ie1)
      }

  let interleave (source1:AsyncSeq<'T>) (source2:AsyncSeq<'T>) : AsyncSeq<'T> = 
    interleaveChoice source1 source2 |> map (function Choice1Of2 x -> x | Choice2Of2 x -> x)
      

  let bufferByCount (bufferSize:int) (source:AsyncSeq<'T>) : AsyncSeq<'T[]> = 
    if (bufferSize < 1) then invalidArg "bufferSize" "must be positive"
    asyncSeq {
      let buffer = new ResizeArray<_>()
      use ie = source.GetEnumerator() 
      let! move = ie.MoveNext()
      let b = ref move
      while b.Value.IsSome do
          buffer.Add b.Value.Value 
          if buffer.Count = bufferSize then 
              yield buffer.ToArray()
              buffer.Clear()
          let! moven = ie.MoveNext()
          b := moven 
      if (buffer.Count > 0) then 
          yield buffer.ToArray() }

  let bufferByCountAndTime (bufferSize:int) (timeoutMs:int) (source:AsyncSeq<'T>) : AsyncSeq<'T[]> = 
    if (bufferSize < 1) then invalidArg "bufferSize" "must be positive"
    if (timeoutMs < 1) then invalidArg "timeoutMs" "must be positive"
    asyncSeq {
      let buffer = new ResizeArray<_>()
      use ie = source.GetEnumerator()
      let rec loop rem rt = asyncSeq {
        let! move = 
          match rem with
          | Some rem -> async.Return rem
          | None -> Async.StartChildAsTask(ie.MoveNext())
        let t = Stopwatch.GetTimestamp()
        let! time = Async.StartChildAsTask(Async.Sleep (max 0 rt))
        let! moveOr = Async.chooseTasks move time
        let delta = int ((Stopwatch.GetTimestamp() - t) * 1000L / Stopwatch.Frequency)
        match moveOr with
        | Choice1Of2 (None, _) -> 
          if buffer.Count > 0 then
            yield buffer.ToArray()
        | Choice1Of2 (Some v, _) ->
          buffer.Add v
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

  let bufferByTime (timeMs:int) (source:AsyncSeq<'T>) : AsyncSeq<'T[]> = asyncSeq {
    if (timeMs < 1) then invalidArg "timeMs" "must be positive"
    let buf = new ResizeArray<_>()
    use ie = source.GetEnumerator()
    let rec loop (next:Task<'T option> option, waitFor:Task option) = asyncSeq {
      let! next = 
        match next with
        | Some n -> async.Return n
        | None -> ie.MoveNext () |> Async.StartChildAsTask
      let waitFor = 
        match waitFor with
        | Some w -> w
        | None -> Task.Delay timeMs
      let! res = Async.chooseTasks2 next waitFor
      match res with
      | Choice1Of2 (Some a,waitFor) ->
        buf.Add a
        yield! loop (None,Some waitFor)
      | Choice1Of2 (None,_) ->
        let arr = buf.ToArray()
        if arr.Length > 0 then
          yield arr
      | Choice2Of2 next ->
        let arr = buf.ToArray()
        buf.Clear()
        yield arr
        yield! loop (Some next, None) }
    yield! loop (None, None) }

  let private mergeChoiceEnum (ie1:IAsyncEnumerator<'T1>) (ie2:IAsyncEnumerator<'T2>) : AsyncSeq<Choice<'T1,'T2>> = asyncSeq {
      let! move1T = Async.StartChildAsTask (ie1.MoveNext())
      let! move2T = Async.StartChildAsTask (ie2.MoveNext())
      let! move = Async.chooseTasks move1T move2T
      let b = ref move
      while (match b.Value with Choice1Of2 (Some _,_) | Choice2Of2 (Some _,_) -> true | _ -> false) do
          match b.Value with 
          | Choice1Of2 (Some v1, rest2) -> 
              yield Choice1Of2 v1
              let! move1T = Async.StartChildAsTask (ie1.MoveNext())
              let! move = Async.chooseTasks move1T rest2
              b := move 
          | Choice2Of2 (Some v2, rest1) -> 
              yield Choice2Of2 v2
              let! move2T = Async.StartChildAsTask (ie2.MoveNext())
              let! move = Async.chooseTasks rest1 move2T
              b := move 
          | _ -> failwith "unreachable"
      match b.Value with 
      | Choice1Of2 (None, rest2) -> 
          let! move2 = Async.AwaitTask rest2
          let b2 = ref move2
          while b2.Value.IsSome do
              let v2 = b2.Value.Value 
              yield Choice2Of2 v2
              let! move2n = ie2.MoveNext()
              b2 := move2n 
      | Choice2Of2 (None, rest1) -> 
          let! move1 = Async.AwaitTask rest1
          let b1 = ref move1
          while b1.Value.IsSome do
              let v1 = b1.Value.Value 
              yield Choice1Of2 v1
              let! move1n = ie1.MoveNext()
              b1 := move1n 
      | _ -> failwith "unreachable" }

  let mergeChoice (source1:AsyncSeq<'T1>) (source2:AsyncSeq<'T2>) : AsyncSeq<Choice<'T1,'T2>> = asyncSeq {
      use ie1 = source1.GetEnumerator() 
      use ie2 = source2.GetEnumerator()
      yield! mergeChoiceEnum ie1 ie2 }

  let merge (source1:AsyncSeq<'T>) (source2:AsyncSeq<'T>) : AsyncSeq<'T> = 
    mergeChoice source1 source2 |> map (function Choice1Of2 x -> x | Choice2Of2 x -> x)
      
  type Disposables<'T when 'T :> IDisposable> (ss: 'T[]) = 
      interface System.IDisposable with 
       member x.Dispose() = 
        let err = ref None
        for i in ss.Length - 1 .. -1 ..  0 do 
            try dispose ss.[i] with e -> err := Some (ExceptionDispatchInfo.Capture e)
        match !err with 
        | Some e -> e.Throw()
        | None -> ()
      
  /// Merges all specified async sequences into an async sequence non-deterministically.
  // By moving the last emitted task to the end of the array, this algorithm achieves max-min fairness when merging AsyncSeqs
  let mergeAll (ss:AsyncSeq<'T> list) : AsyncSeq<'T> =
    asyncSeq {
      let n = ss.Length

      let moveToEnd i (a: 'a[]) =
        let len = a.Length
        if i < 0 || i >= len then
          raise <| System.ArgumentOutOfRangeException()
        if i <> len-1 then
          let x = a.[i]
          System.Array.Copy(a, i+1, a, i, len-1-i)
          a.[len-1] <- x

      if n > 0 then
        let ies = [| for source in ss -> source.GetEnumerator()  |]
        use _ies = new Disposables<_>(ies)
        let tasks = Array.zeroCreate n
        for i in 0 .. ss.Length - 1 do
            let! task = Async.StartChildAsTask (ies.[i].MoveNext())
            do tasks.[i] <- task
        let fin = ref n
        while fin.Value > 0 do
            let! ti = Task.WhenAny (tasks) |> Async.AwaitTask
            let i  = Array.IndexOf (tasks, ti)
            let v = ti.Result
            match v with
            | Some res ->
                yield res
                let! task = Async.StartChildAsTask (ies.[i].MoveNext())
                do tasks.[i] <- task
                moveToEnd i tasks
                moveToEnd i ies
            | None ->
                let t = System.Threading.Tasks.TaskCompletionSource()
                tasks.[i] <- t.Task // result never gets set
                fin := fin.Value - 1
    }

  let combineLatestWithAsync (f:'a -> 'b -> Async<'c>) (source1:AsyncSeq<'a>) (source2:AsyncSeq<'b>) : AsyncSeq<'c> =
    asyncSeq {
      use en1 = source1.GetEnumerator()
      use en2 = source2.GetEnumerator()
      let! a = Async.StartChild (en1.MoveNext())
      let! b = Async.StartChild (en2.MoveNext())
      let! a = a
      let! b = b      
      match a,b with
      | Some a, Some b ->
        let! c = f a b
        yield c        
        let merged = mergeChoiceEnum en1 en2
        use mergedEnum = merged.GetEnumerator()
        let rec loop (prevA:'a, prevB:'b) = asyncSeq {
          let! next = mergedEnum.MoveNext ()
          match next with
          | None -> ()
          | Some (Choice1Of2 nextA) ->
            let! c = f nextA prevB
            yield c
            yield! loop (nextA,prevB)                                  
          | Some (Choice2Of2 nextB) ->
            let! c = f prevA nextB
            yield c
            yield! loop (prevA,nextB) }
        yield! loop (a,b)
      | _ -> () }
    
  let combineLatestWith (f:'a -> 'b -> 'c) (source1:AsyncSeq<'a>) (source2:AsyncSeq<'b>) : AsyncSeq<'c> =
    combineLatestWithAsync (fun a b -> f a b |> async.Return) source1 source2

  let combineLatest (source1:AsyncSeq<'a>) (source2:AsyncSeq<'b>) : AsyncSeq<'a * 'b> =
    combineLatestWith (fun a b -> a,b) source1 source2

  let distinctUntilChangedWithAsync (f:'T -> 'T -> Async<bool>) (source:AsyncSeq<'T>) : AsyncSeq<'T> = asyncSeq {
      use ie = source.GetEnumerator() 
      let! move = ie.MoveNext()
      let b = ref move
      let prev = ref None
      while b.Value.IsSome do
          let v = b.Value.Value 
          match prev.Value with 
          | None -> 
              yield v
          | Some p -> 
              let! b = f p v
              if not b then yield v
          prev := Some v 
          let! moven = ie.MoveNext()
          b := moven }
    
  let distinctUntilChangedWith (f:'T -> 'T -> bool) (s:AsyncSeq<'T>) : AsyncSeq<'T> =
    distinctUntilChangedWithAsync (fun a b -> f a b |> async.Return) s

  let distinctUntilChanged (s:AsyncSeq<'T>) : AsyncSeq<'T> =
    distinctUntilChangedWith ((=)) s

  let getIterator (s:AsyncSeq<'T>) : (unit -> Async<'T option>) = 
      let curr = s.GetEnumerator()
      fun () -> curr.MoveNext()
    
  let traverseOptionAsync (f:'T -> Async<'U option>) (source:AsyncSeq<'T>) : Async<AsyncSeq<'U> option> = async {
      use ie = source.GetEnumerator() 
      let! move = ie.MoveNext()
      let b = ref move
      let buffer = ResizeArray<_>()
      let fail = ref false
      while b.Value.IsSome && not fail.Value do
          let! vOpt = f b.Value.Value 
          match vOpt  with 
          | Some v -> buffer.Add v
          | None -> b := None; fail := true
          let! moven = ie.MoveNext()
          b := moven 
      if fail.Value then
         return None
      else 
         let res = buffer.ToArray() 
         return Some (asyncSeq { for v in res do yield v })
       }

  let traverseChoiceAsync (f:'T -> Async<Choice<'U, 'e>>) (source:AsyncSeq<'T>) : Async<Choice<AsyncSeq<'U>, 'e>> = async {
      use ie = source.GetEnumerator() 
      let! move = ie.MoveNext()
      let b = ref move
      let buffer = ResizeArray<_>()
      let fail = ref None
      while b.Value.IsSome && fail.Value.IsNone do
          let! vOpt = f b.Value.Value 
          match vOpt  with 
          | Choice1Of2 v -> buffer.Add v
          | Choice2Of2 err -> b := None; fail := Some err
          let! moven = ie.MoveNext()
          b := moven 
      match fail.Value with 
      | Some err -> return Choice2Of2 err
      | None -> 
         let res = buffer.ToArray() 
         return Choice1Of2 (asyncSeq { for v in res do yield v })
       }

  module AsyncSeqSrcImpl =

    let private createNode () =
      new AsyncSeqSrcNode<_>(new TaskCompletionSource<_>())
    
    let create () : AsyncSeqSrc<'a> =
      { tail = ref (createNode ()) }
      
    let put (a:'a) (s:AsyncSeqSrc<'a>) =      
      let newTail = createNode ()
      let tail = Interlocked.Exchange(s.tail, newTail)
      tail.tcs.SetResult(Some(a, newTail))
      
    let close (s:AsyncSeqSrc<'a>) : unit =
      s.tail.Value.tcs.SetResult(None)

    let error (ex:exn) (s:AsyncSeqSrc<'a>) : unit =
      s.tail.Value.tcs.SetException(ex)

    let rec private toAsyncSeqImpl (s:AsyncSeqSrcNode<'a>) : AsyncSeq<'a> = 
      asyncSeq {
        let! next = s.tcs.Task |> Async.AwaitTask
        match next with
        | None -> ()
        | Some (a,tl) ->
          yield a
          yield! toAsyncSeqImpl tl }

    let toAsyncSeq (s:AsyncSeqSrc<'a>) : AsyncSeq<'a> =
      toAsyncSeqImpl s.tail.Value
      

  
  let groupByAsync (p:'a -> Async<'k>) (s:AsyncSeq<'a>) : AsyncSeq<'k * AsyncSeq<'a>> = asyncSeq {
    let groups = Dictionary<'k, AsyncSeqSrc< 'a>>()
    let close group =
      AsyncSeqSrcImpl.close group
    let closeGroups () =
      groups.Values |> Seq.iter close    
    use enum = s.GetEnumerator()
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

  let groupBy (p:'a -> 'k) (s:AsyncSeq<'a>) : AsyncSeq<'k * AsyncSeq<'a>> =
    groupByAsync (p >> async.Return) s




[<AutoOpen>]
module AsyncSeqExtensions = 
  let asyncSeq = new AsyncSeq.AsyncSeqBuilder()

  // Add asynchronous for loop to the 'async' computation builder
  type Microsoft.FSharp.Control.AsyncBuilder with
    member x.For (seq:AsyncSeq<'T>, action:'T -> Async<unit>) = 
      seq |> AsyncSeq.iterAsync action 

module AsyncSeqSrc =
    
  let create () = AsyncSeq.AsyncSeqSrcImpl.create ()
  let put a s = AsyncSeq.AsyncSeqSrcImpl.put a s
  let close s = AsyncSeq.AsyncSeqSrcImpl.close s
  let toAsyncSeq s = AsyncSeq.AsyncSeqSrcImpl.toAsyncSeq s
  let error e s = AsyncSeq.AsyncSeqSrcImpl.error e s

module Seq = 

  let ofAsyncSeq (source : AsyncSeq<'T>) =
    AsyncSeq.toBlockingSeq source

[<assembly:System.Runtime.CompilerServices.InternalsVisibleTo("FSharp.Control.AsyncSeq.Tests")>]
do ()

