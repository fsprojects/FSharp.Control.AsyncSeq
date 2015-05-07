// ----------------------------------------------------------------------------
// F# async extensions (AsyncSeq.fs)
// (c) Tomas Petricek, 2011, Available under Apache 2.0 license.
// ----------------------------------------------------------------------------
namespace FSharp.Control

open System
open System.IO
open System.Threading
open System.Threading.Tasks

// ----------------------------------------------------------------------------

type AsyncSeq<'T> = Async<AsyncSeqInner<'T>> 

and AsyncSeqInner<'T> = 
    internal
    | Nil
    | Cons of 'T * AsyncSeq<'T>

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
            | Error of exn
            | Completed


        /// Turns observable into an observable that only calls OnNext method of the
        /// observer, but gives it a discriminated union that represents different
        /// kinds of events (error, next, completed)
        let asUpdates (input:IObservable<'T>) = 
          { new IObservable<_> with
              member x.Subscribe(observer) =
                input.Subscribe
                  ({ new IObserver<_> with
                      member x.OnNext(v) = observer.OnNext(Next v)
                      member x.OnCompleted() = observer.OnNext(Completed) 
                      member x.OnError(e) = observer.OnNext(Error e) }) }

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
      static member Parallel(a:Async<'T>, b:Async<'U>) : Async<'T * 'U> = async {
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
      static member internal chooseBoth (a:Async<'T>) (b:Async<'T>) : Async<'T * Async<'T>> =
        Async.FromContinuations <| fun (ok,err,cnc) ->
          let state = ref 0            
          let tcs = TaskCompletionSource<'T>()            
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

      /// Creates a computation which produces a tuple consiting of the value produces by the first
      /// argument computation to complete and a handle to the other computation. The second computation
      /// to complete is memoized.
      static member internal chooseBoths (a:Async<'T>) (b:Async<'U>) : Async<Choice<'T * Async<'U>, 'U * Async<'T>>> =
        Async.chooseBoth (a |> Async.map Choice1Of2) (b |> Async.map Choice2Of2)
        |> Async.map (fun (first,second) ->          
          match first with
          | Choice1Of2 a -> (a,(second |> Async.map (function Choice2Of2 b -> b | _ -> failwith "invalid state"))) |> Choice1Of2
          | Choice2Of2 b -> (b,(second |> Async.map (function Choice1Of2 a -> a | _ -> failwith "invalid state"))) |> Choice2Of2
        )

/// Module with helper functions for working with asynchronous sequences
module AsyncSeq = 

  [<GeneralizableValue>]
  let empty<'T> : AsyncSeq<'T> = 
    async { return Nil }
 
  let singleton (v:'T) : AsyncSeq<'T> = 
    async { return Cons(v, empty) }
    
  let rec unfoldAsync (f:'State -> Async<('T * 'State) option>) (s:'State) : AsyncSeq<'T> = 
    f s
    |> Async.map (function
      | Some (a,s) -> Cons(a, unfoldAsync f s)
      | None -> Nil)

  let rec replicate (v:'T) : AsyncSeq<'T> =    
    Cons(v, async.Delay (fun() -> replicate v)) |> async.Return    

  let rec append (seq1: AsyncSeq<'T>) (seq2: AsyncSeq<'T>) : AsyncSeq<'T> = 
    async { let! v1 = seq1
            match v1 with 
            | Nil -> return! seq2
            | Cons (h,t) -> return Cons(h,append t seq2) }


  type AsyncSeqBuilder() =
    member x.Yield(v) = singleton v
    // This looks weird, but it is needed to allow:
    //
    //   while foo do
    //     do! something
    //
    // because F# translates body as Bind(something, fun () -> Return())
    member x.Return _ = empty
    member x.YieldFrom(s) = s
    member x.Zero () = empty
    member x.Bind (inp:Async<'T>, body : 'T -> AsyncSeq<'U>) : AsyncSeq<'U> = 
      async.Bind(inp, body)
    member x.Combine (seq1:AsyncSeq<'T>,seq2:AsyncSeq<'T>) = 
      append seq1 seq2
    member x.While (gd, seq:AsyncSeq<'T>) = 
      if gd() then x.Combine(seq,x.Delay(fun () -> x.While (gd, seq))) else x.Zero()
    member x.Delay (f:unit -> AsyncSeq<'T>) = 
      async.Delay(f)

      
  let asyncSeq = new AsyncSeqBuilder()

  /// Tries to get the next element of an asynchronous sequence
  /// and returns either the value or an exception
  let internal tryNext (input:AsyncSeq<_>) = async { 
    try 
      let! v = input
      return Choice1Of2 v
    with e -> 
      return Choice2Of2 e }

  /// Implements the 'TryWith' functionality for computation builder
  let rec internal tryWith (input : AsyncSeq<'T>) handler =  asyncSeq { 
    let! v = tryNext input
    match v with 
    | Choice1Of2 Nil -> ()
    | Choice1Of2 (Cons (h, t)) -> 
        yield h
        yield! tryWith t handler
    | Choice2Of2 rest -> 
        yield! handler rest }
 
  /// Implements the 'TryFinally' functionality for computation builder
  let rec internal tryFinally (input : AsyncSeq<'T>) compensation = asyncSeq {
      let! v = tryNext input
      match v with 
      | Choice1Of2 Nil -> 
          compensation()
      | Choice1Of2 (Cons (h, t)) -> 
          yield h
          yield! tryFinally t compensation
      | Choice2Of2 e -> 
          compensation()
          yield! raise e }

  let rec collect f (input : AsyncSeq<'T>) : AsyncSeq<'TResult> = asyncSeq {
      let! v = input
      match v with
      | Nil -> ()
      | Cons(h, t) ->
          yield! f h
          yield! collect f t }


  // Add additional methods to the 'asyncSeq' computation builder
  type AsyncSeqBuilder with

    member x.TryFinally (body: AsyncSeq<'T>, compensation) = 
      tryFinally body compensation   

    member x.TryWith (body: AsyncSeq<_>, handler: (exn -> AsyncSeq<_>)) = 
      tryWith body handler

    member x.Using (resource:#IDisposable, binder) = 
      tryFinally (binder resource) (fun () -> 
        if box resource <> null then resource.Dispose())

    member x.For(seq:seq<'T>, action:'T -> AsyncSeq<'TResult>) = 
      let enum = seq.GetEnumerator()
      x.TryFinally(x.While((fun () -> enum.MoveNext()), x.Delay(fun () -> 
        action enum.Current)), (fun () -> 
          if enum <> null then enum.Dispose() ))

    member x.For (seq:AsyncSeq<'T>, action:'T -> AsyncSeq<'TResult>) = 
      collect action seq


  // Add asynchronous for loop to the 'async' computation builder
  type Microsoft.FSharp.Control.AsyncBuilder with
    member internal x.For (seq:AsyncSeq<'T>, action:'T -> Async<unit>) = 
      async.Bind(seq, function
        | Nil -> async.Zero()
        | Cons(h, t) -> async.Combine(action h, x.For(t, action)))

  // --------------------------------------------------------------------------
  // Additional combinators (implemented as async/asyncSeq computations)

  let mapAsync f (input : AsyncSeq<'T>) : AsyncSeq<'TResult> = asyncSeq {
    for itm in input do 
      let! v = f itm
      yield v }

  let chooseAsync f (input : AsyncSeq<'T>) : AsyncSeq<'R> = asyncSeq {
    for itm in input do
      let! v = f itm
      match v with 
      | Some v -> yield v 
      | _ -> () }

  let filterAsync f (input : AsyncSeq<'T>) = asyncSeq {
    for v in input do
      let! b = f v
      if b then yield v }

  let rec lastOrDefault def (input : AsyncSeq<'T>) = async {
    let! v = input
    match v with 
    | Nil -> return def
    | Cons(h, t) -> return! lastOrDefault h t }

  let firstOrDefault def (input : AsyncSeq<'T>) = async {
    let! v = input
    match v with 
    | Nil -> return def
    | Cons(h, _) -> return h }

  let scanAsync f (state:'TState) (input : AsyncSeq<'T>) =    
    let rec go f state s = asyncSeq {
      let! v = s
      match v with
      | Nil -> ()
      | Cons(h, t) ->
        let! v = f state h
        yield v
        yield! t |> go f v }
    asyncSeq { yield state ; yield! go f state input }

  let iterAsync f (input : AsyncSeq<'T>) = async {
    for itm in input do 
      do! f itm }

  let pairwise (input : AsyncSeq<'T>) = asyncSeq {
    let! v = input
    match v with
    | Nil -> ()
    | Cons(h, t) ->
        let prev = ref h
        for v in t do
          yield (!prev, v)
          prev := v }

  let foldAsync f (state:'State) (input : AsyncSeq<'T>) = 
    input |> scanAsync f state |> lastOrDefault state

  let fold f (state:'State) (input : AsyncSeq<'T>) = 
    foldAsync (fun st v -> f st v |> async.Return) state input 

  let rec scan f (state:'State) (input : AsyncSeq<'T>) = 
    scanAsync (fun st v -> f st v |> async.Return) state input 

  let map f (input : AsyncSeq<'T>) = 
    mapAsync (f >> async.Return) input

  let iter f (input : AsyncSeq<'T>) = 
    iterAsync (f >> async.Return) input

  let choose f (input : AsyncSeq<'T>) = 
    chooseAsync (f >> async.Return) input

  let filter f (input : AsyncSeq<'T>) =
    filterAsync (f >> async.Return) input
    
  // --------------------------------------------------------------------------
  // Converting from/to synchronous sequences or IObservables

  let ofSeq (input : seq<'T>) = asyncSeq {
    for el in input do 
      yield el }

  /// A helper type for implementation of buffering when converting 
  /// observable to an asynchronous sequence
  type internal BufferMessage<'T> = 
    | Get of AsyncReplyChannel<'T>
    | Put of 'T

  /// Converts observable to an asynchronous sequence using an agent with
  /// a body specified as the argument. The returnd async sequence repeatedly 
  /// sends 'Get' message to the agent to get the next element. The observable
  /// sends 'Put' message to the agent (as new inputs are generated).
  let internal ofObservableUsingAgent (input : System.IObservable<_>) f = 
    asyncSeq {  
      use agent = AutoCancelAgent.Start(f)
      use d = input |> Observable.asUpdates
                    |> Observable.subscribe (Put >> agent.Post)
      
      let rec loop() = asyncSeq {
        let! msg = agent.PostAndAsyncReply(Get)
        match msg with
        | Observable.Error e -> raise e
        | Observable.Completed -> ()
        | Observable.Next v ->
            yield v
            yield! loop() }
      yield! loop() }

  let ofObservableBuffered (input : System.IObservable<_>) = 
    ofObservableUsingAgent input (fun mbox -> async {
        let buffer = new System.Collections.Generic.Queue<_>()
        let repls = new System.Collections.Generic.Queue<_>()
        while true do
          // Receive next message (when observable ends, caller will
          // cancel the agent, so we need timeout to allow cancleation)
          let! msg = mbox.TryReceive(200)
          match msg with 
          | Some(Put(v)) -> buffer.Enqueue(v)
          | Some(Get(repl)) -> repls.Enqueue(repl)
          | _ -> () 
          // Process matching calls from buffers
          while buffer.Count > 0 && repls.Count > 0 do
            repls.Dequeue().Reply(buffer.Dequeue())  })


  let ofObservableDiscarding (input : System.IObservable<_>) = 
    ofObservableUsingAgent input (fun mbox -> async {
      while true do 
        // Allow timeout (when the observable ends, caller will
        // cancel the agent, so we need timeout to allow cancellation)
        let! msg = mbox.TryReceive(200)
        match msg with 
        | Some(Put _) | None -> 
            () // Ignore put or no message 
        | Some(Get repl) ->
            // Reader is blocked, so next will be Put
            // (caller will not stop the agent at this point,
            // so timeout is not necessary)
            let! v = mbox.Receive()
            match v with 
            | Put v -> repl.Reply(v)
            | _ -> failwith "Unexpected Get" })

  [<System.Obsolete("Use AsyncSeq.ofObservableDiscarding. This function doesn't guarantee that the asynchronous sequence will return all values produced by the observable")>]
  let ofObservable (input : System.IObservable<_>) = 
      ofObservableDiscarding input 

  let toObservable (aseq:AsyncSeq<_>) =
    let start (obs:IObserver<_>) =
      async {
        try 
          for v in aseq do obs.OnNext(v)
          obs.OnCompleted()
        with e ->
          obs.OnError(e) }
      |> Async.StartDisposable
    { new IObservable<_> with
        member x.Subscribe(obs) = start obs }

  let toBlockingSeq (input : AsyncSeq<'T>) = 
      seq { 
          // Write all elements to a blocking buffer and then add None to denote end
          let buf = new BlockingQueueAgent<_>(1)
          
          use cts = new System.Threading.CancellationTokenSource()
          use _cancel = { new IDisposable with member __.Dispose() = cts.Cancel() }
          let iteratorTask = 
              async { 
                  let! res = iterAsync (Some >> buf.AsyncAdd) input |> Async.Catch
                  do! buf.AsyncAdd(None)
                  return res
              }
              |> fun p -> Async.StartAsTask(p, cancellationToken = cts.Token)
          
          // Read elements from the blocking buffer & return a sequences
          let fin = ref false
          while not fin.Value do
              match buf.Get() with
              | None -> 
                  fin := true
                  match iteratorTask.Result with
                  | Choice1Of2() -> ()
                  | Choice2Of2 exn -> raise exn
              | Some v -> yield v
      }

  let rec cache (input : AsyncSeq<'T>) = 
    let agent = MailboxProcessor<AsyncReplyChannel<_>>.Start(fun agent -> async {
      let! (repl:AsyncReplyChannel<AsyncSeqInner<'T>>) = agent.Receive()
      let! next = input
      let res = 
        match next with 
        | Nil -> Nil
        | Cons(h, t) -> Cons(h, cache t)
      repl.Reply(res)
      while true do
        let! (repl:AsyncReplyChannel<AsyncSeqInner<'T>>) = agent.Receive()
        repl.Reply(res) })
    async { return! agent.PostAndAsyncReply(id) }

  // --------------------------------------------------------------------------

  let rec threadStateAsync (f:'State -> 'T -> Async<'U * 'State>) (st:'State) (s:AsyncSeq<'T>) : AsyncSeq<'U> = asyncSeq {
    let! s = s
    match s with
    | Nil -> ()
    | Cons(a,tl) ->       
      let! b,st' = f st a
      yield b
      yield! threadStateAsync f st' tl }

  let rec zip (input1 : AsyncSeq<'T1>) (input2 : AsyncSeq<'T2>) : AsyncSeq<_> = async {
    let! ft = input1 |> Async.StartChild
    let! s = input2
    let! f = ft
    match f, s with 
    | Cons(hf, tf), Cons(hs, ts) ->
        return Cons( (hf, hs), zip tf ts)
    | _ -> return Nil }

  let rec zipWithAsync (z:'T1 -> 'T2 -> Async<'U>) (a:AsyncSeq<'T1>) (b:AsyncSeq<'T2>) : AsyncSeq<'U> = async {
    let! a,b = Async.Parallel(a, b)    
    match a,b with 
    | Cons(a, atl), Cons(b, btl) ->
      let! c = z a b
      return Cons(c, zipWithAsync z atl btl)
    | _ -> return Nil }

  let inline zipWith (z:'T1 -> 'T2 -> 'U) (a:AsyncSeq<'T1>) (b:AsyncSeq<'T2>) : AsyncSeq<'U> =
    zipWithAsync (fun a b -> z a b |> async.Return) a b

  let zipWithIndexAsync (f:int -> 'T -> Async<'U>) (s:AsyncSeq<'T>) : AsyncSeq<'U> =
    threadStateAsync (fun i a -> f i a |> Async.map (fun b -> b,i + 1)) 0 s        

  let inline zappAsync (fs:AsyncSeq<'T -> Async<'U>>) (s:AsyncSeq<'T>) : AsyncSeq<'U> =
    zipWithAsync (|>) s fs

  let inline zapp (fs:AsyncSeq<'T -> 'U>) (s:AsyncSeq<'T>) : AsyncSeq<'U> =
    zipWith (|>) s fs

  let rec traverseOptionAsync (f:'T -> Async<'U option>) (s:AsyncSeq<'T>) : Async<AsyncSeq<'U> option> = async {
    let! s = s
    match s with
    | Nil -> return Some (Nil |> async.Return)
    | Cons(a,tl) ->
      let! b = f a
      match b with
      | Some b -> 
        return! traverseOptionAsync f tl |> Async.map (Option.map (fun tl -> Cons(b, tl) |> async.Return))
      | None -> 
        return None }

  let rec traverseChoiceAsync (f:'T -> Async<Choice<'U, 'e>>) (s:AsyncSeq<'T>) : Async<Choice<AsyncSeq<'U>, 'e>> = async {
    let! s = s
    match s with
    | Nil -> return Choice1Of2 (Nil |> async.Return)
    | Cons(a,tl) ->
      let! b = f a
      match b with
      | Choice1Of2 b -> 
        return! traverseChoiceAsync f tl |> Async.map (Choice.mapl (fun tl -> Cons(b, tl) |> async.Return))
      | Choice2Of2 e -> 
        return Choice2Of2 e }

  let rec takeWhileAsync p (input : AsyncSeq<'T>) : AsyncSeq<_> = async {
    let! v = input
    match v with
    | Cons(h, t) -> 
        let! res = p h
        if res then 
          return Cons(h, takeWhileAsync p t)
        else return Nil
    | Nil -> return Nil }

  let rec takeUntil (signal:Async<unit>) (s:AsyncSeq<'T>) : AsyncSeq<'T> =
    Async.chooseBoth (signal |> Async.map Choice1Of2) (s |> Async.map Choice2Of2)
    |> Async.map (fun (first,second) ->
      match first with
      | Choice1Of2 _ -> Nil
      | Choice2Of2 Nil -> Nil
      | Choice2Of2 (Cons(a,tl)) ->        
        let signal = second |> Async.map (function Choice1Of2 x -> x | _ -> failwith "unexpected state")
        Cons(a, takeUntil signal tl))

  let rec skipWhileAsync p (input : AsyncSeq<'T>) : AsyncSeq<_> = async {
    let! v = input
    match v with
    | Cons(h, t) ->
        let! res = p h
        if res then return! skipWhileAsync p t
        else return v
    | Nil -> return Nil }

  let rec skipUntil (signal:Async<unit>) (s:AsyncSeq<'T>) : AsyncSeq<'T> =
    Async.chooseBoth (signal |> Async.map Choice1Of2) (s |> Async.map Choice2Of2)
    |> Async.bind (fun (first,second) ->
      match first with
      | Choice1Of2 _ -> second |> Async.map (function Choice2Of2 tl -> tl | _ -> failwith "unexpected state")
      | Choice2Of2 Nil -> Nil |> async.Return
      | Choice2Of2 (Cons(_,tl)) -> 
        let signal = second |> Async.map (function Choice1Of2 x -> x | _ -> failwith "unexpected state")
        skipUntil signal tl)

  let rec takeWhile p (input : AsyncSeq<'T>) = 
    takeWhileAsync (p >> async.Return) input  

  let rec skipWhile p (input : AsyncSeq<'T>) = 
    skipWhileAsync (p >> async.Return) input

  let rec take count (input : AsyncSeq<'T>) : AsyncSeq<_> = async {
    if count > 0 then
      let! v = input
      match v with
      | Cons(h, t) -> 
          return Cons(h, take (count - 1) t)
      | Nil -> return Nil 
    else return Nil }

  let rec skip count (input : AsyncSeq<'T>) : AsyncSeq<_> = async {
    if count > 0 then
      let! v = input
      match v with
      | Cons(h, t) -> 
          return! skip (count - 1) t
      | Nil -> return Nil 
    else return! input }

  let toArray (input:AsyncSeq<'T>) : Async<'T[]> =
    input
    |> fold (fun (arr:ResizeArray<_>) a -> arr.Add(a) ; arr) (new ResizeArray<_>()) 
    |> Async.map (fun arr -> arr.ToArray())

  let toList (input:AsyncSeq<'T>) : Async<'T list> =
    input 
    |> fold (fun arr a -> a::arr) []
    |> Async.map List.rev

  let rec concatSeq (input:AsyncSeq<#seq<'T>>) : AsyncSeq<'T> = asyncSeq {
    let! v = input
    match v with
    | Nil -> ()
    | Cons (hd, tl) ->
      for item in hd do 
        yield item
      yield! concatSeq tl }

  let interleave (firstSeq: AsyncSeq<'T1>) (secondSeq: AsyncSeq<'T2>) = 

    let rec left (a:AsyncSeq<'T1>) (b:AsyncSeq<'T2>) : AsyncSeq<Choice<_,_>> = async {
      let! a = a        
      match a with
      | Cons (a1, t1) -> return Cons (Choice1Of2 a1, right t1 b)
      | Nil -> return! b |> map Choice2Of2 }

    and right (a:AsyncSeq<'T1>) (b:AsyncSeq<'T2>) : AsyncSeq<Choice<_,_>> = async {
      let! b = b        
      match b with
      | Cons (a2, t2) -> return Cons (Choice2Of2 a2, left a t2)
      | Nil -> return! a |> map Choice1Of2 }

    left firstSeq secondSeq

  let rec bufferByCount (bufferSize:int) (s:AsyncSeq<'T>) : AsyncSeq<'T[]> = 
    if (bufferSize < 1) then invalidArg "bufferSize" "must be positive"
    async {                  
      let buffer = ResizeArray<_>()
      let rec loop s = async {
        let! step = s
        match step with
        | Nil ->
          if (buffer.Count > 0) then return Cons(buffer.ToArray(),async.Return Nil)
          else return Nil
        | Cons(a,tl) ->
          buffer.Add(a)
          if buffer.Count = bufferSize then 
            let buf = buffer.ToArray()
            buffer.Clear()
            return Cons(buf, loop tl)
          else 
            return! loop tl            
      }
      return! loop s
    }

  let bufferByCountAndTime (bufferSize:int) (timeoutMs:int) (s:AsyncSeq<'T>) : AsyncSeq<'T []> =
    if (bufferSize < 1) then invalidArg "bufferSize" "must be positive"
    if (timeoutMs < 1) then invalidArg "timeoutMs" "must be positive"
    async {                  
      let buffer = ResizeArray<_>()
      let rec loop s rt = async {
        let t0 = DateTime.Now
        let! choice = Async.chooseBoths s (Async.Sleep (max 0 rt))
        let delta = int (DateTime.Now - t0).TotalMilliseconds
        match choice with
        | Choice1Of2 (Nil,_) ->
          if (buffer.Count > 0) then return Cons(buffer.ToArray(),async.Return Nil)
          else return Nil
        | Choice1Of2 (Cons(a,tl),_) ->
          buffer.Add(a)
          if buffer.Count = bufferSize then 
            let buf = buffer.ToArray()
            buffer.Clear()
            return Cons(buf, loop tl timeoutMs)
          else 
            return! loop tl (rt - delta)
        | Choice2Of2 ((),tl) ->
          if buffer.Count > 0 then
            let buf = buffer.ToArray()
            buffer.Clear()              
            return Cons(buf, loop tl timeoutMs)
          else
            return! loop tl timeoutMs
      }
      return! loop s timeoutMs
    }

  let rec merge (a:AsyncSeq<'T>) (b:AsyncSeq<'T>) : AsyncSeq<'T> = async {
    let! one,other = Async.chooseBoth a b
    match one with
    | Nil -> return! other
    | Cons(hd,tl) ->
      return Cons(hd, merge tl other) }

  let rec mergeAll (ss:AsyncSeq<'T> list) : AsyncSeq<'T> =
    match ss with
    | [] -> empty
    | [s] -> s
    | [a;b] -> merge a b
    | hd::tl -> merge hd (mergeAll tl)
      
  let distinctUntilChangedWithAsync (f:'T -> 'T -> Async<bool>) (s:AsyncSeq<'T>) : AsyncSeq<'T> =     
    
    // return the head, if any, then the tail passing the previous element
    let rec head s =
      s |> Async.map (function
        | Nil -> Nil
        | Cons(a,tl) -> Cons(a, tail a tl))
    
    // returns the tail comparing with the previous element
    and tail prev s =
      s |> Async.bind (function
        | Nil -> Nil |> async.Return
        | Cons(a,tl) ->
          f a prev 
            |> Async.bind (function
            | true -> tail a tl
            | false -> Cons(a, tail a tl) |> async.Return))

    head s

  let distinctUntilChangedWith (f:'T -> 'T -> bool) (s:AsyncSeq<'T>) : AsyncSeq<'T> =
    distinctUntilChangedWithAsync (fun a b -> f a b |> async.Return) s

  let distinctUntilChanged (s:AsyncSeq<'T>) : AsyncSeq<'T> =
    distinctUntilChangedWith ((=)) s
        
    


[<AutoOpen>]
module AsyncSeqExtensions = 
  let asyncSeq = new AsyncSeq.AsyncSeqBuilder()

  // Add asynchronous for loop to the 'async' computation builder
  type Microsoft.FSharp.Control.AsyncBuilder with
    member x.For (seq:AsyncSeq<'T>, action:'T -> Async<unit>) = 
      async.Bind(seq, function
        | Nil -> async.Zero()
        | Cons(h, t) -> async.Combine(action h, x.For(t, action)))

module Seq = 
  open FSharp.Control

  let ofAsyncSeq (input : AsyncSeq<'T>) =
    AsyncSeq.toBlockingSeq input

namespace System

[<assembly:System.Runtime.CompilerServices.InternalsVisibleTo("FSharp.Control.AsyncSeq.Tests")>]
do ()
