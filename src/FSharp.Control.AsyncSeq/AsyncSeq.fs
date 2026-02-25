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
#if !FABLE_COMPILER
open System.Linq
open System.Threading.Channels
#endif

#nowarn "40" "3218"

// ----------------------------------------------------------------------------

/// Internal pull-based enumerator; not part of the public API.
/// Use AsyncSeq<'T> = System.Collections.Generic.IAsyncEnumerable<'T> as the public type.
[<NoEquality; NoComparison>]
type IAsyncSeqEnumerator<'T> =
    abstract MoveNext : unit -> Async<'T option>
    inherit IDisposable

#if FABLE_COMPILER
/// AsyncSeq<'T> for Fable: a library-specific interface that avoids ValueTask.
[<NoEquality; NoComparison>]
type AsyncSeq<'T> =
    abstract GetEnumerator : unit -> IAsyncSeqEnumerator<'T>

/// Adapter: wraps an internal pull-enumerator factory into an AsyncSeq<'T>.
[<Sealed>]
type AsyncSeqImpl<'T>(getEnum: unit -> IAsyncSeqEnumerator<'T>) =
    member _.GetInternalEnumerator() = getEnum()
    interface AsyncSeq<'T> with
        member _.GetEnumerator() = getEnum()
#else
/// AsyncSeq<'T> is now the BCL IAsyncEnumerable<'T>.
type AsyncSeq<'T> = System.Collections.Generic.IAsyncEnumerable<'T>

/// Adapter: wraps an internal pull-enumerator factory into a BCL IAsyncEnumerable<'T>.
/// This is the canonical way to create AsyncSeq<'T> values from internal generators.
/// Not part of the public API (hidden by .fsi file).
[<Sealed>]
type AsyncSeqImpl<'T>(getEnum: unit -> IAsyncSeqEnumerator<'T>) =
    member _.GetInternalEnumerator() = getEnum()
    interface System.Collections.Generic.IAsyncEnumerable<'T> with
        member _.GetAsyncEnumerator(ct) =
            let inner = getEnum()
            let mutable current = Unchecked.defaultof<'T>
            { new System.Collections.Generic.IAsyncEnumerator<'T> with
                member _.Current = current
                member _.MoveNextAsync() =
                    Async.StartAsTask(
                        async {
                            let! res = inner.MoveNext()
                            match res with
                            | Some v -> current <- v; return true
                            | None -> return false
                        }, cancellationToken=ct) |> System.Threading.Tasks.ValueTask<bool>
                member _.DisposeAsync() =
                    inner.Dispose()
                    System.Threading.Tasks.ValueTask() }


/// Extension adding GetEnumerator() to AsyncSeq<'T> for internal use.
/// This preserves internal pull-based iteration without touching all call sites.
[<AutoOpen>]
module AsyncSeqEnumeratorExtensions =
    type System.Collections.Generic.IAsyncEnumerable<'T> with
        /// Gets an internal pull-based enumerator. For internal use only.
        member this.GetEnumerator() : IAsyncSeqEnumerator<'T> =
            match this with
            | :? AsyncSeqImpl<'T> as x -> x.GetInternalEnumerator()
            | _ ->
                let e = this.GetAsyncEnumerator(System.Threading.CancellationToken.None)
                { new IAsyncSeqEnumerator<'T> with
                    member _.MoveNext() = async {
                        let! moved = e.MoveNextAsync().AsTask() |> Async.AwaitTask
                        if moved then return Some e.Current
                        else return None }
                  interface System.IDisposable with
                    member _.Dispose() = e.DisposeAsync() |> ignore }
#endif

#if !FABLE_COMPILER
type AsyncSeqSrc<'a> = private { tail : AsyncSeqSrcNode<'a> ref }

and private AsyncSeqSrcNode<'a> =
  val tcs : TaskCompletionSource<('a * AsyncSeqSrcNode<'a>) option>
  new (tcs) = { tcs = tcs }
#endif

[<AutoOpen>]
module internal Utils =

    module Disposable =

      let empty : IDisposable =
        { new IDisposable with member __.Dispose () = () }

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

      #if !FABLE_COMPILER
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
      #endif

      /// Starts the specified operation using a new CancellationToken and returns
      /// IDisposable object that cancels the computation. This method can be used
      /// when implementing the Subscribe method of IObservable interface.
      static member StartDisposable(op:Async<unit>) =
          let ct = new System.Threading.CancellationTokenSource()
          Async.Start(op, ct.Token)
          { new IDisposable with
              member x.Dispose() = ct.Cancel() }

      static member map f a = async.Bind(a, f >> async.Return)

    #if !FABLE_COMPILER
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

    [<RequireQualifiedAccess>]
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
          else if t.IsCanceled then
            ivar.SetCanceled()
          ivar.Task)
        |> join
    #endif

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
    interface IAsyncSeqEnumerator<'a> with
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
          return! (x :> IAsyncSeqEnumerator<_>).MoveNext() }
      member __.Dispose () =
        disposeG g

  /// Converts an enumerator to a generator.
  /// The resulting generator will either yield or stop.
  type AsyncEnumeratorGenerator<'a> (enum:IAsyncSeqEnumerator<'a>) =
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

  let enumeratorFromGenerator (g:AsyncGenerator<'a>) : IAsyncSeqEnumerator<'a> =
    match g with
    | :? AsyncEnumeratorGenerator<'a> as g -> g.Enumerator
    | _ -> (new AsyncGeneratorEnumerator<_>(g) :> _)

  let generatorFromEnumerator (e:IAsyncSeqEnumerator<'a>) : AsyncGenerator<'a> =
    match e with
    | :? AsyncGeneratorEnumerator<'a> as e -> e.Generator
    | _ -> (new AsyncEnumeratorGenerator<_>(e) :> _)

  let delay (f:unit -> AsyncSeq<'T>) : AsyncSeq<'T> =
    AsyncSeqImpl(fun () -> (f()).GetEnumerator()) :> AsyncSeq<'T>

  let emitEnum (e:IAsyncSeqEnumerator<'a>) : AsyncSeq<'a> =
    AsyncSeqImpl(fun () -> e) :> AsyncSeq<'a>

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

[<AutoOpen>]
module AsyncSeqOp =

  // Optimized enumerator for unfoldAsync with reduced allocations
  [<Sealed>]
  type OptimizedUnfoldEnumerator<'S, 'T> (f:'S -> Async<('T * 'S) option>, init:'S) =
    let mutable currentState = init
    let mutable disposed = false

    interface IAsyncSeqEnumerator<'T> with
      member __.MoveNext () : Async<'T option> =
        if disposed then async.Return None
        else async {
          let! result = f currentState
          match result with
          | None ->
            return None
          | Some (value, nextState) ->
            currentState <- nextState
            return Some value
        }
      member __.Dispose () =
        disposed <- true

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
#if FABLE_COMPILER
    interface AsyncSeq<'T> with
      member __.GetEnumerator() =
        new OptimizedUnfoldEnumerator<'S, 'T>(f, init) :> IAsyncSeqEnumerator<'T>
#else
    interface System.Collections.Generic.IAsyncEnumerable<'T> with
      member __.GetAsyncEnumerator(ct) =
        (AsyncSeqImpl(fun () -> new OptimizedUnfoldEnumerator<'S, 'T>(f, init) :> IAsyncSeqEnumerator<'T>)
         :> System.Collections.Generic.IAsyncEnumerable<'T>).GetAsyncEnumerator(ct)
#endif



/// Module with helper functions for working with asynchronous sequences
module AsyncSeq =
  let inline dispose (d:System.IDisposable) = try d.Dispose() with _ -> ()

  [<GeneralizableValue>]
  let empty<'T> : AsyncSeq<'T> =
    AsyncSeqImpl(fun () ->
        { new IAsyncSeqEnumerator<'T> with
            member _.MoveNext() = async { return None }
          interface System.IDisposable with
            member _.Dispose() = () }) :> AsyncSeq<'T>

  let emptyAsync<'T> (action : Async<unit>) : AsyncSeq<'T> =
    AsyncSeqImpl(fun () ->
        { new IAsyncSeqEnumerator<'T> with
            member _.MoveNext() = async {
                do! action
                return None }
          interface System.IDisposable with
            member _.Dispose() = () }) :> AsyncSeq<'T>

  let singleton (v:'T) : AsyncSeq<'T> =
    AsyncSeqImpl(fun () ->
        let state = ref 0
        { new IAsyncSeqEnumerator<'T> with
            member _.MoveNext() = async {
                let res = state.Value = 0
                incr state
                return (if res then Some v else None) }
          interface System.IDisposable with
            member _.Dispose() = () }) :> AsyncSeq<'T>

  let append (inp1: AsyncSeq<'T>) (inp2: AsyncSeq<'T>) : AsyncSeq<'T> =
    // Optimized append implementation that doesn't create generator chains
    // This fixes the memory leak issue in Issue #35
    AsyncSeqImpl(fun () ->
        let mutable currentEnumerator : IAsyncSeqEnumerator<'T> option = None
        let mutable useSecond = false
        let rec moveNext (self: IAsyncSeqEnumerator<'T>) = async {
            match currentEnumerator with
            | None ->
                let enum1 = inp1.GetEnumerator()
                currentEnumerator <- Some enum1
                return! moveNext self
            | Some enum when not useSecond ->
                let! result = enum.MoveNext()
                match result with
                | Some v -> return Some v
                | None ->
                    dispose enum
                    let enum2 = inp2.GetEnumerator()
                    currentEnumerator <- Some enum2
                    useSecond <- true
                    return! moveNext self
            | Some enum ->
                return! enum.MoveNext()
        }
        { new IAsyncSeqEnumerator<'T> with
            member x.MoveNext() = moveNext x
          interface System.IDisposable with
            member _.Dispose() =
                match currentEnumerator with
                | Some enum -> dispose enum
                | None -> () }) :> AsyncSeq<'T>

  let inline delay (f: unit -> AsyncSeq<'T>) : AsyncSeq<'T> =
    AsyncGenerator.delay f

  let bindAsync (f:'T -> AsyncSeq<'U>) (inp:Async<'T>) : AsyncSeq<'U> =
    AsyncSeqImpl(fun () ->
        { new AsyncGenerator.AsyncGenerator<'U> with
            member _.Apply() = async {
                let! v = inp
                let cont =
                    (f v).GetEnumerator()
                    |> AsyncGenerator.generatorFromEnumerator
                return AsyncGenerator.Goto cont }
            member _.Disposer = None }
        |> AsyncGenerator.enumeratorFromGenerator) :> AsyncSeq<'U>



  type AsyncSeqBuilder() =
    member x.Yield(v) =
      singleton v
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


  let emitEnumerator (ie: IAsyncSeqEnumerator<'T>) = asyncSeq {
      let! moven = ie.MoveNext()
      let b = ref moven
      while b.Value.IsSome do
          yield b.Value.Value
          let! moven = ie.MoveNext()
          b := moven }

  [<RequireQualifiedAccess>]
  type TryWithState<'T> =
     | NotStarted of AsyncSeq<'T>
     | HaveBodyEnumerator of IAsyncSeqEnumerator<'T>
     | HaveHandlerEnumerator of IAsyncSeqEnumerator<'T>
     | Finished

  /// Implements the 'TryWith' functionality for computation builder
  let tryWith (inp: AsyncSeq<'T>) (handler : exn -> AsyncSeq<'T>) : AsyncSeq<'T> =
        // Note: this is put outside the object deliberately, so the object doesn't permanently capture inp1 and inp2
        AsyncSeqImpl(fun () ->
            let state = ref (TryWithState.NotStarted inp)
            { new IAsyncSeqEnumerator<'T> with
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
                            | _ -> () }) :> AsyncSeq<'T>


  [<RequireQualifiedAccess>]
  type TryFinallyState<'T> =
     | NotStarted    of AsyncSeq<'T>
     | HaveBodyEnumerator of IAsyncSeqEnumerator<'T>
     | Finished

  // This pushes the handler through all the async computations
  // The (synchronous) compensation is run when the Dispose() is called
  let tryFinally (inp: AsyncSeq<'T>) (compensation : unit -> unit) : AsyncSeq<'T> =
        AsyncSeqImpl(fun () ->
            let state = ref (TryFinallyState.NotStarted inp)
            { new IAsyncSeqEnumerator<'T> with
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
                            | _ -> () }) :> AsyncSeq<'T>


  [<RequireQualifiedAccess>]
  type CollectState<'T,'U> =
     | NotStarted    of AsyncSeq<'T>
     | HaveInputEnumerator of IAsyncSeqEnumerator<'T>
     | HaveInnerEnumerator of IAsyncSeqEnumerator<'T> * IAsyncSeqEnumerator<'U>
     | Finished

  // Optimized collect implementation using direct field access instead of ref cells
  type OptimizedCollectEnumerator<'T, 'U>(f: 'T -> AsyncSeq<'U>, inp: AsyncSeq<'T>) =
    // Mutable fields instead of ref cells to reduce allocations
    let mutable inputEnumerator: IAsyncSeqEnumerator<'T> option = None
    let mutable innerEnumerator: IAsyncSeqEnumerator<'U> option = None
    let mutable disposed = false

    // Tail-recursive optimization to avoid deep continuation chains
    let rec moveNextLoop () : Async<'U option> = async {
      if disposed then return None
      else
        match innerEnumerator with
        | Some inner ->
            let! result = inner.MoveNext()
            match result with
            | Some value -> return Some value
            | None ->
                inner.Dispose()
                innerEnumerator <- None
                return! moveNextLoop ()
        | None ->
            match inputEnumerator with
            | Some outer ->
                let! result = outer.MoveNext()
                match result with
                | Some value ->
                    let newInner = (f value).GetEnumerator()
                    innerEnumerator <- Some newInner
                    return! moveNextLoop ()
                | None ->
                    outer.Dispose()
                    inputEnumerator <- None
                    disposed <- true
                    return None
            | None ->
                let newOuter = inp.GetEnumerator()
                inputEnumerator <- Some newOuter
                return! moveNextLoop ()
    }

    interface IAsyncSeqEnumerator<'U> with
      member _.MoveNext() = moveNextLoop ()
      member _.Dispose() =
        if not disposed then
          disposed <- true
          match innerEnumerator with
          | Some inner -> inner.Dispose(); innerEnumerator <- None
          | None -> ()
          match inputEnumerator with
          | Some outer -> outer.Dispose(); inputEnumerator <- None
          | None -> ()

  let collect (f: 'T -> AsyncSeq<'U>) (inp: AsyncSeq<'T>) : AsyncSeq<'U> =
    AsyncSeqImpl(fun () -> new OptimizedCollectEnumerator<'T, 'U>(f, inp) :> IAsyncSeqEnumerator<'U>) :> AsyncSeq<'U>

//  let collect (f: 'T -> AsyncSeq<'U>) (inp: AsyncSeq<'T>) : AsyncSeq<'U> =
//    AsyncGenerator.collect f inp

  [<RequireQualifiedAccess>]
  type CollectSeqState<'T,'U> =
     | NotStarted    of seq<'T>
     | HaveInputEnumerator of IEnumerator<'T>
     | HaveInnerEnumerator of IEnumerator<'T> * IAsyncSeqEnumerator<'U>
     | Finished

  // Like collect, but the input is a sequence, where no bind is required on each step of the enumeration
  let collectSeq (f: 'T -> AsyncSeq<'U>) (inp: seq<'T>) : AsyncSeq<'U> =
        AsyncSeqImpl(fun () ->
            let state = ref (CollectSeqState.NotStarted inp)
            { new IAsyncSeqEnumerator<'U> with
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
                            | _ -> () }) :> AsyncSeq<'U>

  [<RequireQualifiedAccess>]
  type MapState<'T> =
     | NotStarted    of seq<'T>
     | HaveEnumerator of IEnumerator<'T>
     | Finished

  let ofSeq (inp: seq<'T>) : AsyncSeq<'T> =
        AsyncSeqImpl(fun () ->
            let state = ref (MapState.NotStarted inp)
            { new IAsyncSeqEnumerator<'T> with
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
                            | _ -> () }) :> AsyncSeq<'T>

  // Optimized iterAsync implementation to reduce allocations
  type internal OptimizedIterAsyncEnumerator<'T>(enumerator: IAsyncSeqEnumerator<'T>, f: 'T -> Async<unit>) =
    let mutable disposed = false

    member _.IterateAsync() =
      let rec loop() = async {
        let! next = enumerator.MoveNext()
        match next with
        | Some value ->
            do! f value
            return! loop()
        | None -> return ()
      }
      loop()

    interface IDisposable with
      member _.Dispose() =
        if not disposed then
          disposed <- true
          enumerator.Dispose()

  // Optimized iteriAsync implementation with direct tail recursion
  type internal OptimizedIteriAsyncEnumerator<'T>(enumerator: IAsyncSeqEnumerator<'T>, f: int -> 'T -> Async<unit>) =
    let mutable disposed = false

    member _.IterateAsync() =
      let rec loop count = async {
        let! next = enumerator.MoveNext()
        match next with
        | Some value ->
            do! f count value
            return! loop (count + 1)
        | None -> return ()
      }
      loop 0

    interface IDisposable with
      member _.Dispose() =
        if not disposed then
          disposed <- true
          enumerator.Dispose()

  let iteriAsync f (source : AsyncSeq<_>) =
      async {
          let enum = source.GetEnumerator()
          use optimizer = new OptimizedIteriAsyncEnumerator<_>(enum, f)
          return! optimizer.IterateAsync()
      }

  let iterAsync (f: 'T -> Async<unit>) (source: AsyncSeq<'T>)  =
    match source with
    | :? AsyncSeqOp<'T> as source -> source.IterAsync f
    | _ ->
        async {
          let enum = source.GetEnumerator()
          use optimizer = new OptimizedIterAsyncEnumerator<_>(enum, f)
          return! optimizer.IterateAsync()
        }

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

    member x.YieldFrom (s:seq<'T>) =
      ofSeq s

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

  // Optimized mapAsync enumerator that avoids computation builder overhead
  type private OptimizedMapAsyncEnumerator<'T, 'TResult>(source: IAsyncSeqEnumerator<'T>, f: 'T -> Async<'TResult>) =
    let mutable disposed = false

    interface IAsyncSeqEnumerator<'TResult> with
      member _.MoveNext() = async {
        let! moveResult = source.MoveNext()
        match moveResult with
        | None -> return None
        | Some value ->
            let! mapped = f value
            return Some mapped
      }

      member _.Dispose() =
        if not disposed then
          disposed <- true
          source.Dispose()

  let mapAsync f (source : AsyncSeq<'T>) : AsyncSeq<'TResult> =
    match source with
    | :? AsyncSeqOp<'T> as source -> source.MapAsync f
    | _ ->
      AsyncSeqImpl(fun () -> new OptimizedMapAsyncEnumerator<'T, 'TResult>(source.GetEnumerator(), f) :> IAsyncSeqEnumerator<'TResult>) :> AsyncSeq<'TResult>

  let mapiAsync f (source : AsyncSeq<'T>) : AsyncSeq<'TResult> = asyncSeq {
    let i = ref 0L
    for itm in source do
      let! v = f i.Value itm
      i := i.Value + 1L
      yield v }

  #if !FABLE_COMPILER
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

  let mapAsyncUnorderedParallel (f:'a -> Async<'b>) (s:AsyncSeq<'a>) : AsyncSeq<'b> = asyncSeq {
    use mb = MailboxProcessor.Start (fun _ -> async.Return())
    let! err =
      s
      |> iterAsync (fun a -> async {
        let! b = Async.StartChild (async {
          try
            let! result = f a
            return Choice1Of2 result
          with ex ->
            return Choice2Of2 ex
        })
        mb.Post (Some b) })
      |> Async.map (fun _ -> mb.Post None)
      |> Async.StartChildAsTask
    yield!
      replicateUntilNoneAsync (Task.chooseTask (err |> Task.taskFault) (async.Delay mb.Receive))
      |> mapAsync (fun childAsync -> async {
        let! result = childAsync
        match result with
        | Choice1Of2 value -> return value
        | Choice2Of2 ex -> return raise ex })
  }

  let mapAsyncUnorderedParallelThrottled (parallelism:int) (f:'a -> Async<'b>) (s:AsyncSeq<'a>) : AsyncSeq<'b> = asyncSeq {
    use mb = MailboxProcessor.Start (fun _ -> async.Return())
    use sm = new SemaphoreSlim(parallelism)
    let! err =
      s
      |> iterAsync (fun a -> async {
        do! sm.WaitAsync () |> Async.awaitTaskUnitCancellationAsError
        let! b = Async.StartChild (async {
          try
            let! result = f a
            sm.Release() |> ignore
            return Choice1Of2 result
          with ex ->
            sm.Release() |> ignore
            return Choice2Of2 ex
        })
        mb.Post (Some b) })
      |> Async.map (fun _ -> mb.Post None)
      |> Async.StartChildAsTask
    yield!
      replicateUntilNoneAsync (Task.chooseTask (err |> Task.taskFault) (async.Delay mb.Receive))
      |> mapAsync (fun childAsync -> async {
        let! result = childAsync
        match result with
        | Choice1Of2 value -> return value
        | Choice2Of2 ex -> return raise ex })
  }
  #endif

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

  let exactlyOne (source : AsyncSeq<'T>) = async {
      use ie = source.GetEnumerator()
      let! first = ie.MoveNext()
      match first with
      | None -> return raise (System.InvalidOperationException("The input sequence was empty."))
      | Some v ->
          let! second = ie.MoveNext()
          match second with
          | None -> return v
          | Some _ -> return raise (System.InvalidOperationException("The input sequence contains more than one element.")) }

  let tryExactlyOne (source : AsyncSeq<'T>) = async {
      use ie = source.GetEnumerator()
      let! first = ie.MoveNext()
      match first with
      | None -> return None
      | Some v ->
          let! second = ie.MoveNext()
          match second with
          | None -> return Some v
          | Some _ -> return None }

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

  let windowed (windowSize:int) (source:AsyncSeq<'T>) : AsyncSeq<'T[]> =
    if windowSize < 1 then invalidArg (nameof windowSize) "must be positive"
    asyncSeq {
      let window = System.Collections.Generic.Queue<'T>(windowSize)
      use ie = source.GetEnumerator()
      let! move = ie.MoveNext()
      let b = ref move
      while b.Value.IsSome do
          window.Enqueue(b.Value.Value)
          if window.Count = windowSize then
              yield window.ToArray()
              window.Dequeue() |> ignore
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

  let reduceAsync (f: 'T -> 'T -> Async<'T>) (source: AsyncSeq<'T>) : Async<'T> = async {
      use ie = source.GetEnumerator()
      let! first = ie.MoveNext()
      match first with
      | None -> return raise (InvalidOperationException("The input sequence was empty."))
      | Some v ->
          let acc = ref v
          let! next = ie.MoveNext()
          let b = ref next
          while b.Value.IsSome do
              let! newAcc = f acc.Value b.Value.Value
              acc := newAcc
              let! more = ie.MoveNext()
              b := more
          return acc.Value }

  let reduce (f: 'T -> 'T -> 'T) (source: AsyncSeq<'T>) : Async<'T> =
      reduceAsync (fun a b -> f a b |> async.Return) source

  let length (source : AsyncSeq<'T>) =
    fold (fun st _ -> st + 1L) 0L source

  let inline sum (source : AsyncSeq<'T>) : Async<'T> =
    (LanguagePrimitives.GenericZero, source) ||> fold (+)

  let minByAsync (projection: 'T -> Async<'Key>) (source: AsyncSeq<'T>) : Async<'T> =
    async {
      let! result =
        source |> foldAsync (fun (acc: ('T * 'Key) option) v ->
          async {
            let! k = projection v
            match acc with
            | None -> return Some (v, k)
            | Some (_, ak) -> return if k < ak then Some (v, k) else acc
          }) None
      match result with
      | None -> return raise (System.InvalidOperationException("The input sequence was empty."))
      | Some (v, _) -> return v }

  let minBy (projection: 'T -> 'Key) (source: AsyncSeq<'T>) : Async<'T> =
    minByAsync (projection >> async.Return) source

  let maxByAsync (projection: 'T -> Async<'Key>) (source: AsyncSeq<'T>) : Async<'T> =
    async {
      let! result =
        source |> foldAsync (fun (acc: ('T * 'Key) option) v ->
          async {
            let! k = projection v
            match acc with
            | None -> return Some (v, k)
            | Some (_, ak) -> return if k > ak then Some (v, k) else acc
          }) None
      match result with
      | None -> return raise (System.InvalidOperationException("The input sequence was empty."))
      | Some (v, _) -> return v }

  let maxBy (projection: 'T -> 'Key) (source: AsyncSeq<'T>) : Async<'T> =
    maxByAsync (projection >> async.Return) source

  let min (source: AsyncSeq<'T>) : Async<'T> =
    minBy id source

  let max (source: AsyncSeq<'T>) : Async<'T> =
    maxBy id source

  let inline sumBy (projection : 'T -> ^U) (source : AsyncSeq<'T>) : Async<^U> =
    fold (fun s x -> s + projection x) LanguagePrimitives.GenericZero source

  let inline sumByAsync (projection : 'T -> Async<^U>) (source : AsyncSeq<'T>) : Async<^U> =
    foldAsync (fun s x -> async { let! v = projection x in return s + v }) LanguagePrimitives.GenericZero source

  let inline average (source : AsyncSeq<^T>) : Async<^T> =
    async {
      let! sum, count = fold (fun (s, n) x -> (s + x, n + 1)) (LanguagePrimitives.GenericZero, 0) source
      if count = 0 then invalidArg "source" "The input sequence was empty."
      return LanguagePrimitives.DivideByInt sum count
    }

  let inline averageBy (projection : 'T -> ^U) (source : AsyncSeq<'T>) : Async<^U> =
    async {
      let! sum, count = fold (fun (s, n) x -> (s + projection x, n + 1)) (LanguagePrimitives.GenericZero, 0) source
      if count = 0 then invalidArg "source" "The input sequence was empty."
      return LanguagePrimitives.DivideByInt sum count
    }

  let inline averageByAsync (projection : 'T -> Async<^U>) (source : AsyncSeq<'T>) : Async<^U> =
    async {
      let! sum, count = foldAsync (fun (s, n) x -> async { let! v = projection x in return (s + v, n + 1) }) (LanguagePrimitives.GenericZero, 0) source
      if count = 0 then invalidArg "source" "The input sequence was empty."
      return LanguagePrimitives.DivideByInt sum count
    }

  let countByAsync (projection : 'T -> Async<'Key>) (source : AsyncSeq<'T>) : Async<('Key * int) array> =
    async {
      let! dict =
        source |> foldAsync (fun (d : System.Collections.Generic.Dictionary<'Key,int>) v ->
          async {
            let! k = projection v
            let mutable cnt = 0
            if d.TryGetValue(k, &cnt) then d.[k] <- cnt + 1
            else d.[k] <- 1
            return d
          }) (System.Collections.Generic.Dictionary<'Key,int>())
      return dict |> Seq.map (fun kv -> kv.Key, kv.Value) |> Seq.toArray }

  let countBy (projection : 'T -> 'Key) (source : AsyncSeq<'T>) : Async<('Key * int) array> =
    countByAsync (projection >> async.Return) source

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

  #if !FABLE_COMPILER
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
  #endif

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
                  do! iterAsync (fun v -> async { return obs.OnNext(v) }) aseq
                  obs.OnCompleted()
                with e ->
                  obs.OnError(e) }
              |> Async.StartDisposable }

  #if !FABLE_COMPILER
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
        use ie = source.GetEnumerator()
        let rec loop () = async {
          let! (i:int, rep:TaskCompletionSource<'T option>) = mbp.Receive()
          if i >= cache.Count then
            try
              let! move = ie.MoveNext()
              match move with
              | Some v ->
                lock cacheLock (fun() -> cache.Add v)
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
        let cachedItem = tryGetCachedItem i
        match cachedItem with
        | Some a ->
            yield a
            yield! loop (i + 1)
        | None ->
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
  #endif

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

  let zipWithAsync3 (f:'T1 -> 'T2 -> 'T3 -> Async<'U>) (source1:AsyncSeq<'T1>) (source2:AsyncSeq<'T2>) (source3:AsyncSeq<'T3>) : AsyncSeq<'U> = asyncSeq {
      use ie1 = source1.GetEnumerator()
      use ie2 = source2.GetEnumerator()
      use ie3 = source3.GetEnumerator()
      let! move1 = ie1.MoveNext()
      let! move2 = ie2.MoveNext()
      let! move3 = ie3.MoveNext()
      let b1 = ref move1
      let b2 = ref move2
      let b3 = ref move3
      while b1.Value.IsSome && b2.Value.IsSome && b3.Value.IsSome do
          let! res = f b1.Value.Value b2.Value.Value b3.Value.Value
          yield res
          let! move1n = ie1.MoveNext()
          let! move2n = ie2.MoveNext()
          let! move3n = ie3.MoveNext()
          b1 := move1n
          b2 := move2n
          b3 := move3n }

  let zip3 (source1:AsyncSeq<'T1>) (source2:AsyncSeq<'T2>) (source3:AsyncSeq<'T3>) : AsyncSeq<'T1 * 'T2 * 'T3> =
      zipWithAsync3 (fun a b c -> async.Return (a, b, c)) source1 source2 source3

  let zipWith3 (f:'T1 -> 'T2 -> 'T3 -> 'U) (source1:AsyncSeq<'T1>) (source2:AsyncSeq<'T2>) (source3:AsyncSeq<'T3>) : AsyncSeq<'U> =
      zipWithAsync3 (fun a b c -> f a b c |> async.Return) source1 source2 source3

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

  #if !FABLE_COMPILER
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
  #endif

  let takeWhileInclusive (f : 'a -> bool) (s : AsyncSeq<'a>) : AsyncSeq<'a> =
    AsyncSeqImpl(fun () ->
        let en = s.GetEnumerator()
        let fin = ref false
        { new IAsyncSeqEnumerator<'a> with
            member _.MoveNext() = async {
                if !fin then return None
                else
                    let! next = en.MoveNext()
                    match next with
                    | None -> return None
                    | Some a ->
                        if f a then return Some a
                        else fin := true; return Some a }
          interface System.IDisposable with
            member _.Dispose() = en.Dispose() }) :> AsyncSeq<'a>

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

  #if !FABLE_COMPILER
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
  #endif

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
  #if !FABLE_COMPILER
  let toListSynchronously (source:AsyncSeq<'T>) = toListAsync source |> Async.RunSynchronously
  let toArraySynchronously (source:AsyncSeq<'T>) = toArrayAsync source |> Async.RunSynchronously
  #endif

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

  let interleaveMany (xs : #seq<AsyncSeq<'T>>) : AsyncSeq<'T> =
    let mutable result = empty
    for x in xs do
      result <- interleave result x
    result

  let chunkBySize (chunkSize:int) (source:AsyncSeq<'T>) : AsyncSeq<'T[]> =
    if chunkSize < 1 then invalidArg (nameof chunkSize) "must be positive"
    asyncSeq {
      let buffer = new ResizeArray<_>()
      use ie = source.GetEnumerator()
      let! move = ie.MoveNext()
      let b = ref move
      while b.Value.IsSome do
          buffer.Add b.Value.Value
          if buffer.Count = chunkSize then
              yield buffer.ToArray()
              buffer.Clear()
          let! moven = ie.MoveNext()
          b := moven
      if (buffer.Count > 0) then
          yield buffer.ToArray() }

  [<Obsolete("Use AsyncSeq.chunkBySize instead")>]
  let bufferByCount (bufferSize:int) (source:AsyncSeq<'T>) : AsyncSeq<'T[]> =
    chunkBySize bufferSize source

  let chunkByAsync (projection:'T -> Async<'Key>) (source:AsyncSeq<'T>) : AsyncSeq<'Key * 'T list> = asyncSeq {
    use ie = source.GetEnumerator()
    let! move = ie.MoveNext()
    let b = ref move
    if b.Value.IsSome then
      let! key0 = projection b.Value.Value
      let mutable currentKey = key0
      let buffer = ResizeArray<'T>()
      buffer.Add b.Value.Value
      let! moveNext = ie.MoveNext()
      b := moveNext
      while b.Value.IsSome do
        let! key = projection b.Value.Value
        if key = currentKey then
          buffer.Add b.Value.Value
        else
          yield (currentKey, buffer |> Seq.toList)
          currentKey <- key
          buffer.Clear()
          buffer.Add b.Value.Value
        let! moveNext = ie.MoveNext()
        b := moveNext
      yield (currentKey, buffer |> Seq.toList) }

  let chunkBy (projection:'T -> 'Key) (source:AsyncSeq<'T>) : AsyncSeq<'Key * 'T list> =
    chunkByAsync (projection >> async.Return) source

  #if !FABLE_COMPILER
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
  #endif

  #if !FABLE_COMPILER
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
        let! time = Async.StartChildAsTask(Async.Sleep (Operators.max 0 rt))
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

  let private mergeChoiceEnum (ie1:IAsyncSeqEnumerator<'T1>) (ie2:IAsyncSeqEnumerator<'T2>) : AsyncSeq<Choice<'T1,'T2>> = asyncSeq {
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
  let mergeAll (ss:seq<AsyncSeq<'T>>) : AsyncSeq<'T> =
    asyncSeq {
      let ss = Seq.toArray ss
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
  #endif

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

  let distinctByAsync (projection : 'T -> Async<'Key>) (source : AsyncSeq<'T>) : AsyncSeq<'T> = asyncSeq {
      let seen = System.Collections.Generic.HashSet<'Key>()
      for v in source do
        let! k = projection v
        if seen.Add(k) then
          yield v }

  let distinctBy (projection : 'T -> 'Key) (source : AsyncSeq<'T>) : AsyncSeq<'T> =
    distinctByAsync (projection >> async.Return) source

  let distinct (source : AsyncSeq<'T>) : AsyncSeq<'T> =
    distinctBy id source

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

  #if (NETSTANDARD || NET)
  #if !FABLE_COMPILER

  /// Converts a BCL IAsyncEnumerable to AsyncSeq. Identity function since AsyncSeq<'T> IS IAsyncEnumerable<'T> in v4+.
  [<Obsolete("AsyncSeq<'T> is now identical to IAsyncEnumerable<'T>. This function is a no-op and can be removed.")>]
  let ofAsyncEnum (source: System.Collections.Generic.IAsyncEnumerable<'T>) : AsyncSeq<'T> = source

  /// Returns the AsyncSeq as a BCL IAsyncEnumerable<'a>. Identity function since AsyncSeq<'a> IS IAsyncEnumerable<'a> in v4+.
  [<Obsolete("AsyncSeq<'T> is now identical to IAsyncEnumerable<'T>. This function is a no-op and can be removed.")>]
  let toAsyncEnum (source: AsyncSeq<'a>) : System.Collections.Generic.IAsyncEnumerable<'a> = source

  let ofIQueryable (query : IQueryable<'a>) : AsyncSeq<'a> =
     query :?> Collections.Generic.IAsyncEnumerable<'a>

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


  [<CompilerMessage("The result of groupByAsync must be consumed with a parallel combinator such as AsyncSeq.mapAsyncParallel. Sequential consumption will deadlock because sub-sequence completion depends on other sub-sequences being consumed concurrently.", 9999)>]
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

  [<CompilerMessage("The result of groupBy must be consumed with a parallel combinator such as AsyncSeq.mapAsyncParallel. Sequential consumption will deadlock because sub-sequence completion depends on other sub-sequences being consumed concurrently.", 9999)>]
  let groupBy (p:'a -> 'k) (s:AsyncSeq<'a>) : AsyncSeq<'k * AsyncSeq<'a>> =
    groupByAsync (p >> async.Return) s
  #endif
  #endif


  #if !FABLE_COMPILER
  open System.Threading.Channels

  let toChannel (writer : ChannelWriter<'a>) (xs :  AsyncSeq<'a>) : Async<unit> =
    async {
      try
        do!
          xs
          |> iterAsync
            (fun x ->
              async {
                if not (writer.TryWrite(x)) then
                  let! ct = Async.CancellationToken

                  do!
                    writer.WriteAsync(x, ct).AsTask()
                    |> Async.AwaitTask
              })

        writer.Complete()
      with exn ->
        writer.Complete(error = exn)
    }

  let fromChannel (reader : ChannelReader<'a>) : AsyncSeq<'a> =
    asyncSeq {
      let mutable keepGoing = true

      while keepGoing do
        let mutable item = Unchecked.defaultof<'a>

        if reader.TryRead(&item) then
          yield item
        else
          let! ct = Async.CancellationToken

          let! hasMoreData =
            reader.WaitToReadAsync(ct).AsTask()
            |> Async.AwaitTask

          if not hasMoreData then
            keepGoing <- false
    }

  let prefetch (numberToPrefetch : int) (xs : AsyncSeq<'a>) : AsyncSeq<'a> =
    if numberToPrefetch = 0 then
      xs
    else
      if numberToPrefetch < 1 then
        invalidArg (nameof numberToPrefetch) "must be at least zero"
      asyncSeq {
        let opts = BoundedChannelOptions(numberToPrefetch)
        opts.SingleWriter <- true
        opts.SingleReader <- true

        let channel = Channel.CreateBounded(opts)

        let! fillChannelTask =
          toChannel channel.Writer xs
          |> Async.StartChild

        yield!
          append
            (fromChannel channel.Reader)
            (emptyAsync fillChannelTask)
      }

  #endif


[<AutoOpen>]
module AsyncSeqExtensions =
  let asyncSeq = new AsyncSeq.AsyncSeqBuilder()

  // Add asynchronous for loop to the 'async' computation builder
  type Microsoft.FSharp.Control.AsyncBuilder with
    member x.For (seq:AsyncSeq<'T>, action:'T -> Async<unit>) =
      seq |> AsyncSeq.iterAsync action

#if !FABLE_COMPILER
[<RequireQualifiedAccess>]
module AsyncSeqSrc =

  let create () = AsyncSeq.AsyncSeqSrcImpl.create ()
  let put a s = AsyncSeq.AsyncSeqSrcImpl.put a s
  let close s = AsyncSeq.AsyncSeqSrcImpl.close s
  let toAsyncSeq s = AsyncSeq.AsyncSeqSrcImpl.toAsyncSeq s
  let error e s = AsyncSeq.AsyncSeqSrcImpl.error e s

module Seq =
  let ofAsyncSeq (source : AsyncSeq<'T>) =
    AsyncSeq.toBlockingSeq source
#endif

[<assembly:System.Runtime.CompilerServices.InternalsVisibleTo("FSharp.Control.AsyncSeq.Tests")>]
do ()

