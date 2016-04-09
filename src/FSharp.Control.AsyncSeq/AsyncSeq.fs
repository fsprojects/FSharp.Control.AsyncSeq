// ----------------------------------------------------------------------------
// F# async extensions (AsyncSeq.fs)
// (c) Tomas Petricek, 2011, Available under Apache 2.0 license.
// ----------------------------------------------------------------------------
namespace FSharp.Control

open System
open System.IO
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
//    abstract GetEnumerator : unit -> IAsyncEnumerator<'T>

type AsyncSeqSrc<'a> = private { tail : AsyncSeqSrcNode<'a> ref }

and private AsyncSeqSrcNode<'a> =
  val tcs : TaskCompletionSource<('a * AsyncSeqSrcNode<'a>) option>
  new (tcs) = { tcs = tcs }

[<AutoOpen>]
module internal Utils = 
    module internal Choice =
  
      /// Maps over the left result type.
      let mapl (f:'T -> 'U) = function
        | Choice1Of2 a -> f a |> Choice1Of2
        | Choice2Of2 e -> Choice2Of2 e

    // ----------------------------------------------------------------------------

    module internal Observable =

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
            let! ct = Async.CancellationToken
            let i = Task.WaitAny( [| (a :> Task);(b :> Task) |],ct)
            if i = 0 then return (Choice1Of2 (a.Result, b))
            elif i = 1 then return (Choice2Of2 (b.Result, a)) 
            else return! failwith (sprintf "unreachable, i = %d" i) }


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
    
  [<RequireQualifiedAccess>]
  type AppendState<'T> =
     | NotStarted1     of AsyncSeq<'T> * AsyncSeq<'T>
     | HaveEnumerator1 of IAsyncEnumerator<'T> * AsyncSeq<'T>
     | NotStarted2     of AsyncSeq<'T>
     | HaveEnumerator2 of IAsyncEnumerator<'T> 
     | Finished        

  let append (inp1: AsyncSeq<'T>) (inp2: AsyncSeq<'T>) : AsyncSeq<'T> =
        { new IAsyncEnumerable<'T> with 
              member x.GetEnumerator() = 
                  let state = ref (AppendState.NotStarted1 (inp1, inp2) )
                  { new IAsyncEnumerator<'T> with 
                        member x.MoveNext() = 
                            async { match !state with 
                                    | AppendState.NotStarted1 (inp1, inp2) -> 
                                        return! 
                                         (let enum1 = inp1.GetEnumerator()
                                          state := AppendState.HaveEnumerator1 (enum1, inp2)
                                          x.MoveNext())
                                    | AppendState.HaveEnumerator1 (enum1, inp2) ->   
                                        let! res = enum1.MoveNext() 
                                        match res with 
                                        | None -> 
                                            return! 
                                              (state := AppendState.NotStarted2 inp2
                                               dispose enum1
                                               x.MoveNext())
                                        | Some _ -> 
                                            return res
                                    | AppendState.NotStarted2 inp2 -> 
                                        return! 
                                         (let enum2 = inp2.GetEnumerator()
                                          state := AppendState.HaveEnumerator2 enum2
                                          x.MoveNext())
                                    | AppendState.HaveEnumerator2 enum2 ->   
                                        let! res = enum2.MoveNext() 
                                        return (match res with
                                                | None -> 
                                                    state := AppendState.Finished
                                                    dispose enum2
                                                    None
                                                | Some _ -> 
                                                    res)
                                    | _ -> 
                                        return None }
                        member x.Dispose() = 
                            match !state with 
                            | AppendState.HaveEnumerator1 (enum, _) 
                            | AppendState.HaveEnumerator2 enum -> 
                                state := AppendState.Finished
                                dispose enum 
                            | _ -> () } }


  let delay (f: unit -> AsyncSeq<'T>) : AsyncSeq<'T> = 
      { new IAsyncEnumerable<'T> with 
          member x.GetEnumerator() = f().GetEnumerator() }


  [<RequireQualifiedAccess>]
  type BindState<'T,'U> =
     | NotStarted of Async<'T>
     | HaveEnumerator of IAsyncEnumerator<'U>
     | Finished        

  let bindAsync (f: 'T -> AsyncSeq<'U>) (inp : Async<'T>) : AsyncSeq<'U> = 
        { new IAsyncEnumerable<'U> with 
              member x.GetEnumerator() = 
                  let state = ref (BindState.NotStarted inp)
                  { new IAsyncEnumerator<'U> with 
                        member x.MoveNext() = 
                            async { match !state with 
                                    | BindState.NotStarted inp -> 
                                        let! v = inp 
                                        return! 
                                           (let s = f v
                                            let e = s.GetEnumerator()
                                            state := BindState.HaveEnumerator e
                                            x.MoveNext())
                                    | BindState.HaveEnumerator e ->   
                                        let! res = e.MoveNext() 
                                        return (match res with
                                                | None -> x.Dispose()
                                                | Some _ -> ()
                                                res)
                                    | _ -> 
                                        return None }
                        member x.Dispose() = 
                            match !state with 
                            | BindState.HaveEnumerator e -> 
                                state := BindState.Finished
                                dispose e 
                            | _ -> () } }



  type AsyncSeqBuilder() =
    member x.Yield(v) = singleton v
    // This looks weird, but it is needed to allow:
    //
    //   while foo do
    //     do! something
    //
    // because F# translates body as Bind(something, fun () -> Return())
    member x.Return _ = empty
    member x.YieldFrom(s:AsyncSeq<'T>) = s
    member x.Zero () = empty
    member x.Bind (inp:Async<'T>, body : 'T -> AsyncSeq<'U>) : AsyncSeq<'U> = bindAsync body inp
    member x.Combine (seq1:AsyncSeq<'T>,seq2:AsyncSeq<'T>) = append seq1 seq2
    member x.While (guard, body:AsyncSeq<'T>) = 
      // Use F#'s support for Landin's knot for a low-allocation fixed-point
      let rec fix = delay (fun () -> if guard() then append body fix else empty)
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

  
  let iterAsync (f: 'T -> Async<unit>) (inp: AsyncSeq<'T>)  = iteriAsync (fun i x -> f x) inp
  
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

  let rec unfoldAsync (f:'State -> Async<('T * 'State) option>) (s:'State) : AsyncSeq<'T> = 
    asyncSeq { 
        let! v = f s 
        match v with 
        | None -> ()
        | Some (v,s2) -> 
            yield v
            yield! unfoldAsync f s2 }

  let replicateInfinite (v:'T) : AsyncSeq<'T> =    
    asyncSeq { 
        while true do 
            yield v }

  let replicate (count:int) (v:'T) : AsyncSeq<'T> =    
    asyncSeq { 
        for i in 1 .. count do 
           yield v }
  // --------------------------------------------------------------------------
  // Additional combinators (implemented as async/asyncSeq computations)

  let mapAsync f (source : AsyncSeq<'T>) : AsyncSeq<'TResult> = asyncSeq {
    for itm in source do 
      let! v = f itm
      yield v }

  let mapiAsync f (source : AsyncSeq<'T>) : AsyncSeq<'TResult> = asyncSeq {
    let i = ref 0L
    for itm in source do 
      let! v = f i.Value itm
      i := i.Value + 1L
      yield v }

  let mapAsyncParallel (f:'a -> Async<'b>) (s:AsyncSeq<'a>) = asyncSeq {
    use mbp = MailboxProcessor.Start (fun _ -> async.Return())
    do! s |> iterAsync (fun a -> async {
      let! b = Async.StartChild (f a)
      mbp.Post (Some b) })
    mbp.Post None
    let rec loop () = asyncSeq {
      let! b = mbp.Receive()
      match b with
      | None -> ()
      | Some b -> 
        let! b = b
        yield b
        yield! loop () }
    yield! loop () }

  let chooseAsync f (source : AsyncSeq<'T>) : AsyncSeq<'R> = asyncSeq {
    for itm in source do
      let! v = f itm
      match v with 
      | Some v -> yield v 
      | _ -> () }

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
    source |> scanAsync f state |> lastOrDefault state

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
      let fin = ref false
      asyncSeq {
          if !fin then yield! ofSeq cache
          else
              use mre = new ManualResetEventSlim(true)
              use ie = source.GetEnumerator() 
              let iRef = ref 0            
              while not fin.Value do
                  let i = iRef.Value
                  try 
                      // the following is non-blocking but it allocates a waithandle and doesn't spin
                      // consider an AsyncManualResetEvent which spins like ManualResetEventSlim before allocating waithandle
                      //do! mre.WaitHandle |> Async.AwaitWaitHandle |> Async.Ignore
                      mre.Wait()
                      if i >= cache.Count then 
                          let! move = ie.MoveNext()
                          match move with
                          | Some v -> cache.Add v
                          | None -> fin := true
                          iRef := i + 1
                  finally
                      mre.Set()
                  if not !fin then 
                    yield cache.[i] }

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

  let zip (source1 : AsyncSeq<'T1>) (source2 : AsyncSeq<'T2>) : AsyncSeq<_> = 
      zipWithAsync (fun a b -> async.Return (a,b)) source1 source2

  let zipWith (z:'T1 -> 'T2 -> 'U) (a:AsyncSeq<'T1>) (b:AsyncSeq<'T2>) : AsyncSeq<'U> =
      zipWithAsync (fun a b -> z a b |> async.Return) a b

  let zipWithIndexAsync (f:int64 -> 'T -> Async<'U>) (s:AsyncSeq<'T>) : AsyncSeq<'U> = mapiAsync f s 

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
  let toList (source:AsyncSeq<'T>) = toListAsync source |> Async.RunSynchronously
  let toArray (source:AsyncSeq<'T>) = toArrayAsync source |> Async.RunSynchronously

  let concatSeq (source:AsyncSeq<#seq<'T>>) : AsyncSeq<'T> = asyncSeq {
      use ie = source.GetEnumerator() 
      let! move = ie.MoveNext()
      let b = ref move
      while b.Value.IsSome do
          for x in (b.Value.Value :> seq<'T>)  do
              yield x
          let! moven = ie.MoveNext()
          b := moven }

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
        let t = DateTime.Now
        let! time = Async.StartChildAsTask(Async.Sleep (max 0 rt))
        let! moveOr = Async.chooseTasks move time
        let delta = int (DateTime.Now - t).TotalMilliseconds      
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
      
  let mergeAll (ss:AsyncSeq<'T> list) : AsyncSeq<'T> =
      asyncSeq { 
        let n = ss.Length
        if n > 0 then 
          let ies = [| for source in ss -> source.GetEnumerator()  |]
          use _ies = new Disposables<_>(ies)
          let tasks = Array.zeroCreate n
          for i in 0 .. ss.Length - 1 do 
              let! task = Async.StartChildAsTask (ies.[i].MoveNext())
              do tasks.[i] <- (task :> Task)
          let fin = ref n
          while fin.Value > 0 do 
              let! ct = Async.CancellationToken
              let i = Task.WaitAny(tasks, ct)
              let v = (tasks.[i] :?> Task<'T option>).Result
              match v with 
              | Some res -> 
                  yield res
                  let! task = Async.StartChildAsTask (ies.[i].MoveNext())
                  do tasks.[i] <- (task :> Task)
              | None -> 
                  let t = System.Threading.Tasks.TaskCompletionSource()
                  tasks.[i] <- (t.Task :> Task) // result never gets set
                  fin := fin.Value - 1
      }

  let combineLatestAsync (f:'a -> 'b -> Async<'c>) (source1:AsyncSeq<'a>) (source2:AsyncSeq<'b>) : AsyncSeq<'c> =
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
    
  let combineLatest (f:'a -> 'b -> 'c) (source1:AsyncSeq<'a>) (source2:AsyncSeq<'b>) : AsyncSeq<'c> =
    combineLatestAsync (fun a b -> f a b |> async.Return) source1 source2

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
      

  
  type private Group<'k, 'a> = { key : 'k ; src : AsyncSeqSrc<'a> }
    
  let groupByAsync (p:'a -> Async<'k>) (s:AsyncSeq<'a>) : AsyncSeq<'k * AsyncSeq<'a>> = asyncSeq {
    let groups = Collections.Generic.Dictionary<'k, Group<'k, 'a>>()
    let close group =
      groups.Remove(group.key) |> ignore
      AsyncSeqSrcImpl.close group.src
    let closeGroups () =
      groups.Values |> Seq.toArray |> Array.iter close
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
            AsyncSeqSrcImpl.put a group.src
            yield! go ()
          else
            let src = AsyncSeqSrcImpl.create ()
            let subSeq = src |> AsyncSeqSrcImpl.toAsyncSeq
            AsyncSeqSrcImpl.put a src
            let group = { key = key ; src = src }
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

