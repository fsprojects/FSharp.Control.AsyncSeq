#r @"../../bin/FSharp.Control.AsyncSeq.dll"
#r @"../../packages/NUnit/lib/nunit.framework.dll"
#time "on"
//#load "AsyncSeqTests.fs"

open System
open FSharp.Control

let mapFst f (a,b) = (f a, b)
let mapSnd f (a,b) = (a, f b)

module Async =
  
  let inline map f a = async.Bind (a, f >> async.Return)

module Disposable =
  
  let ofFun (f:unit -> unit) : IDisposable =
    { new IDisposable with member __.Dispose () = f () }

  let empty : IDisposable =
    ofFun ignore


module AsyncSeq =
    
  type IFuse<'T> =
    abstract member FuseMapAsync : ('T -> Async<'U>) -> AsyncSeq<'U>
    abstract member FuseMap : ('T -> 'U) -> AsyncSeq<'U>
    abstract member FuseChooseAsync : ('T -> Async<'U option>) -> AsyncSeq<'U>
    abstract member FuseFoldAsync : ('S -> 'T -> Async<'S>) -> 'S -> Async<'S>
    abstract member FuseIterAsync : ('T -> Async<unit>) -> Async<unit>
    abstract member FuseIter : ('T -> unit) -> Async<unit>

  [<AutoOpen>]
  module Ex =

    type IFuse<'T> with
      member x.FuseIterAsyncDefault (f:'T -> Async<unit>) =
        x.FuseFoldAsync (fun () t -> f t) ()
      member x.FuseIterDefault (f:'T -> unit) =
        x.FuseIterAsyncDefault (f >> async.Return)
      member x.FuseChooseDefault (f:'T -> 'U option) =
        x.FuseChooseAsync (f >> async.Return)

  type ChooseAsyncEnumerable<'S, 'T, 'U> (f:'S -> 'T -> Async<('U * 'S) option>, init:'S, source:AsyncSeq<'T>) =
    interface IFuse<'U> with
      member x.FuseIterAsync f = x.FuseIterAsyncDefault f
      member x.FuseIter f = x.FuseIterDefault f
      member __.FuseFoldAsync (g:'S2 -> 'U -> Async<'S2>) (init2:'S2) = async {
        printfn "ChooseAsyncEnumerable.FuseFoldAsync"
        use en = source.GetEnumerator ()
        let rec go s s2 = async {
          let! next = en.MoveNext ()
          match next with
          | Some t ->
            let! res = f s t
            match res with
            | Some (u,s') ->
              let! s2' = g s2 u
              return! go s' s2'
            | None ->
              return s2
          | None ->
            return s2 }
        return! go init init2 }
      member __.FuseChooseAsync (g:'U -> Async<'V option>) : AsyncSeq<'V> =
        printfn "ChooseAsyncEnumerable.FuseChooseAsync"
        let f s t = async {
          let! res = f s t
          match res with
          | None -> 
            return None
          | Some (u,s) ->
            let! res' = g u
            match res' with
            | Some v ->
              return Some (v, s)
            | None ->
              return None }
        new ChooseAsyncEnumerable<'S, 'T, 'V> (f, init, source) :> _
      member __.FuseMap (g:'U -> 'V) : AsyncSeq<'V> =
        printfn "ChooseAsyncEnumerable.FuseMap"
        let f s t = async {
          let! res = f s t
          match res with
          | None -> return None
          | Some (u,s) ->
            let v = g u
            return Some (v,s) }          
        new ChooseAsyncEnumerable<'S, 'T, 'V> (f, init, source) :> _
      member __.FuseMapAsync (g:'U -> Async<'V>) : AsyncSeq<'V> =
        printfn "ChooseAsyncEnumerable.FuseMapAsync"
        let f s t = async {
          let! res = f s t
          match res with
          | None -> return None
          | Some (u,s) ->
            let! v = g u
            return Some (v,s) }          
        new ChooseAsyncEnumerable<'S, 'T, 'V> (f, init, source) :> _
    interface IAsyncEnumerable<'U> with
      member __.GetEnumerator () =
        let s = ref init
        let st = ref 0
        let mutable en = Unchecked.defaultof<_>
        { new IAsyncEnumerator<'U> with
            member __.MoveNext () : Async<'U option> = async {
              match !st with
              | 0 ->
                en <- source.GetEnumerator()
                st := 1
                return! __.MoveNext ()
              | _ ->
                let! next = en.MoveNext () 
                match next with
                | None -> return None 
                | Some t ->
                  let! res = f !s t
                  match res with
                  | Some (u,s') ->
                    s := s'
                    return Some u
                  | None ->
                    return None }
            member __.Dispose () =
              match !st with
              | 1 ->  en.Dispose()
              | _ -> () }

  type UnfoldAsyncEnumerator<'S, 'T> (f:'S -> Async<('T * 'S) option>, init:'S, disp:IDisposable) =
    interface IFuse<'T> with
      member x.FuseIterAsync f = x.FuseIterAsyncDefault f
      member x.FuseIter g = async {
        printfn "UnfoldAsyncEnumerator.FuseIter"
        let rec go s = async {
          let! next = f s
          match next with
          | None -> return ()
          | Some (t,s') ->
            do g t
            return! go s' }
        return! go init }
      member __.FuseFoldAsync (g:'S2 -> 'T -> Async<'S2>) (init2:'S2) = async {
        printfn "UnfoldAsyncEnumerator.FuseFoldAsync"
        let rec go s s2 = async {
          let! next = f s
          match next with
          | None -> return s2
          | Some (t,s') ->
            let! s2' = g s2 t
            return! go s' s2' }
        return! go init init2 }
      member __.FuseChooseAsync (g:'T -> Async<'U option>) : AsyncSeq<'U> =
        printfn "UnfoldAsyncEnumerator.FuseChooseAsync"
        let f s = async {
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
              return None }
        new UnfoldAsyncEnumerator<'S, 'U> (f, init, disp) :> _
      member __.FuseMap (g:'T -> 'U) : AsyncSeq<'U> =
        printfn "UnfoldAsyncEnumerator.FuseMap"
        let h s = async {
          let! r = f s
          match r with
          | Some (t,s) ->
            let u = g t
            return Some (u,s)
          | None ->
            return None }
        new UnfoldAsyncEnumerator<'S, 'U> (h, init, disp) :> _
      member __.FuseMapAsync (g:'T -> Async<'U>) : AsyncSeq<'U> =
        printfn "UnfoldAsyncEnumerator.FuseMapAsync"
        let h s = async {
          let! r = f s
          match r with
          | Some (t,s) ->
            let! u = g t
            return Some (u,s)
          | None ->
            return None }
        new UnfoldAsyncEnumerator<'S, 'U> (h, init, disp) :> _
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
            member __.Dispose () =
              disp.Dispose () }

  type UnfoldEnumerator<'S, 'T> (f:'S -> Async<('T * 'S) option>, init:'S, disp:IDisposable) =
    interface IFuse<'T> with
      member x.FuseIterAsync f = x.FuseIterAsyncDefault f
      member x.FuseIter f = x.FuseIterDefault f
      member __.FuseFoldAsync (g:'S2 -> 'T -> Async<'S2>) (init2:'S2) = async {
        printfn "UnfoldEnumerator.FuseFoldAsync"
        let init = ref init
        let init2 = ref init2 
        let fin = ref false
        while not !fin do
          let! next = f !init 
          match next with
          | None -> 
            fin := true
          | Some (t,s') ->
            let! s2' = g !init2 t
            init := s'
            init2 := s2'
        return !init2 }
      member __.FuseChooseAsync (g:'T -> Async<'U option>) : AsyncSeq<'U> = 
        printfn "UnfoldEnumerator.FuseChooseAsync"
        let f s = async {
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
              return None }
        new UnfoldAsyncEnumerator<'S, 'U> (f, init, disp) :> _
      member __.FuseMap (g:'T -> 'U) : AsyncSeq<'U> =
        printfn "UnfoldEnumerator.FuseMap"
        let h s = async {
          let! r = f s
          match r with
          | Some (t,s) ->
            let u = g t
            return Some (u,s)
          | None ->
            return None }
        new UnfoldAsyncEnumerator<'S, 'U> (h, init, disp) :> _
      member __.FuseMapAsync (g:'T -> Async<'U>) : AsyncSeq<'U> =
        printfn "UnfoldEnumerator.FuseMapAsync"
        let h s = async {
          let! r = f s
          match r with
          | Some (t,s) ->
            let! u = g t
            return Some (u,s)
          | None ->
            return None }
        new UnfoldAsyncEnumerator<'S, 'U> (h, init, disp) :> _
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
    
  type MapEnumerable<'T, 'U> (f:'T -> 'U, source:AsyncSeq<'T>) =
    interface IFuse<'U> with
      member x.FuseIterAsync f = x.FuseIterAsyncDefault f
      member x.FuseIter f = x.FuseIterDefault f
      member __.FuseFoldAsync (g:'S -> 'U -> Async<'S>) (init:'S) = async {
        printfn "MapEnumerable.FuseFoldAsync"
        use en = source.GetEnumerator ()
        let rec go s = async {
          let! next = en.MoveNext ()
          match next with
          | Some t ->
            let u = f t
            let! s' = g s u
            return! go s'
          | None ->
            return s }
        return! go init }
      member __.FuseChooseAsync (g:'U -> Async<'V option>) : AsyncSeq<'V> =
        let f () t = async {
          let u = f t
          let! res' = g u
          match res' with
          | Some v ->
            return Some (v, ())
          | None ->
            return None }
        new ChooseAsyncEnumerable<unit, 'T, 'V> (f, (), source) :> _
      member __.FuseMap (g:'U -> 'V) : AsyncSeq<'V> = 
        new MapEnumerable<'T, 'V> (f >> g, source) :> _ 
      member __.FuseMapAsync (g:'U -> Async<'V>) : AsyncSeq<'V> =
        new MapAsyncEnumerable<'T, 'V> (f >> g, source) :> _
    interface IAsyncEnumerable<'U> with
      member __.GetEnumerator () =
        let mutable st = 0
        let mutable en = Unchecked.defaultof<_>
        { new IAsyncEnumerator<'U> with
            member __.MoveNext () = async {
              match st with
              | 0 -> 
                en <- source.GetEnumerator()
                st <- 1                
                return! __.MoveNext ()
              | _ ->
                let! next = en.MoveNext ()
                match next with
                | Some a -> return Some (f a)
                | None -> return None }
            member __.Dispose () =
              match st with
              | 1 -> en.Dispose ()
              | _ -> () }

  and MapAsyncEnumerable<'T, 'U> (f:'T -> Async<'U>, source:AsyncSeq<'T>) =
    interface IFuse<'U> with
      member x.FuseIterAsync f = x.FuseIterAsyncDefault f
      member x.FuseIter f = x.FuseIterDefault f
      member __.FuseFoldAsync (g:'S -> 'U -> Async<'S>) (init:'S) = async {
        printfn "MapAsyncEnumerable.FuseFoldAsync"
        use en = source.GetEnumerator ()
        let rec go s = async {
          let! next = en.MoveNext ()
          match next with
          | Some t ->
            let! u = f t
            let! s' = g s u
            return! go s'
          | None ->
            return s }
        return! go init }      
      member __.FuseChooseAsync (g:'U -> Async<'V option>) : AsyncSeq<'V> =
        let f () t = async {
          let! u = f t
          let! res' = g u
          match res' with
          | Some v ->
            return Some (v, ())
          | None ->
            return None }
        new ChooseAsyncEnumerable<unit, 'T, 'V> (f, (), source) :> _
      member  __.FuseMap (g:'U -> 'V) : AsyncSeq<'V> = 
        let f t = async {
          let! u = f t
          return g u }
        new MapAsyncEnumerable<'T, 'V> (f, source) :> _          
      member  __.FuseMapAsync (g:'U -> Async<'V>) : AsyncSeq<'V> = 
        let f t = async {
          let! u = f t
          return! g u }
        new MapAsyncEnumerable<'T, 'V> (f, source) :> _    
    interface IAsyncEnumerable<'U> with
      member __.GetEnumerator () =
        let mutable st = 0
        let mutable en = Unchecked.defaultof<_>
        { new IAsyncEnumerator<'U> with
            member __.MoveNext () = async {
              match st with
              | 0 -> 
                en <- source.GetEnumerator()
                st <- 1                
                return! __.MoveNext ()
              | _ ->
                let! next = en.MoveNext ()
                match next with
                | Some a -> 
                  let! b = f a
                  return Some b
                | None -> return None }
            member __.Dispose () =
              match st with
              | 1 -> en.Dispose ()
              | _ -> () }
  
  type FoldEnumerable<'T, 'U> (f:'U -> 'T -> 'U, init:'U, source:AsyncSeq<'T>) =
    member __.Fold () = async {
      use en = source.GetEnumerator ()
      let fin = ref false
      let init = ref init
      while not !fin do
        let! next = en.MoveNext ()
        match next with
        | Some t ->
          init := f !init t
        | None ->
          fin := true
      return !init }

  type FoldAsyncEnumerable<'T, 'U> (f:'U -> 'T -> Async<'U>, init:'U, source:AsyncSeq<'T>) =
    member __.FoldAsync () = async {
      use en = source.GetEnumerator ()
      let fin = ref false
      let init = ref init
      while not !fin do
        let! next = en.MoveNext ()
        match next with
        | Some t ->
          let! i = f !init t
          init := i 
        | None ->
          fin := true
      return !init }

  let rec unfoldAsync2 (f:'State -> Async<('T * 'State) option>) (s:'State) : AsyncSeq<'T> = 
    new UnfoldAsyncEnumerator<_, _>(f, s, Disposable.empty) :> _

  let rec unfoldDisposeAsync (f:'State -> Async<('T * 'State) option>) (s:'State) (d:IDisposable) : AsyncSeq<'T> = 
    new UnfoldAsyncEnumerator<_, _>(f, s, d) :> _

  
  let chooseStateAsync (f:'S -> 'T -> Async<('U * 'S) option>) (init:'S) (source:AsyncSeq<'T>) : AsyncSeq<'U> =
//    match source with
//    | :? IFuseMap<'T> as source -> source.
    new ChooseAsyncEnumerable<'S, 'T, 'U> (f, init, source) :> _
    
  let chooseState (f:'S -> 'T -> ('U * 'S) option) (init:'S) (source:AsyncSeq<'T>) : AsyncSeq<'U> =
    chooseStateAsync (fun s t -> f s t |> async.Return) init source

  let take2 (count:int) source = 
    let f i t =
      if i = count then None
      else Some (t, i + 1)
    chooseState f 0 source

  let chooseAsync2 f (source:AsyncSeq<'T>) =
    let f () = f >> Async.map (Option.map (fun u -> u, ()))
    chooseStateAsync f () source

  let chooseAsync3 f (source:AsyncSeq<'T>) =
    match source with
    | :? IFuse<'T> as source -> source.FuseChooseAsync f
    | _ ->
      let f () = f >> Async.map (Option.map (fun u -> u, ()))
      chooseStateAsync f () source

  let map2 (f:'T -> 'U) (source:AsyncSeq<'T>) : AsyncSeq<'U> =
    new MapEnumerable<_, _>(f, source) :> _
                           
  let map3 (f:'T -> 'U) (source:AsyncSeq<'T>) : AsyncSeq<'U> =
    match source with
    | :? IFuse<'T> as source -> source.FuseMap f
    | _ -> new MapEnumerable<_, _>(f, source) :> _

  let mapAsync2 (f:'T -> Async<'U>) (source:AsyncSeq<'T>) : AsyncSeq<'U> =
    new MapAsyncEnumerable<_, _>(f, source) :> _

  let mapAsync3 (f:'T -> Async<'U>) (source:AsyncSeq<'T>) : AsyncSeq<'U> =
    match source with
    | :? IFuse<'T> as source -> source.FuseMapAsync f
    | _ -> new MapAsyncEnumerable<_, _>(f, source) :> _

  let foldAsync2 f i (s:AsyncSeq<'T>) : Async<'U> =
    let e = new FoldAsyncEnumerable<_, _>(f, i, s) in e.FoldAsync ()

  let foldAsync3 f i (s:AsyncSeq<'T>) : Async<'U> =
    match s with 
    | :? IFuse<'T> as source -> source.FuseFoldAsync f i
    | _ -> let e = new FoldAsyncEnumerable<_, _>(f, i, s) in e.FoldAsync ()

  let fold2 f i s : Async<'U> =
    foldAsync2 (fun s t -> f s t |> async.Return) i s

  let fold3 f i (s:AsyncSeq<'T>) : Async<'U> =
    foldAsync3 (fun s t -> f s t |> async.Return) i s

  let inline sum2 (s:AsyncSeq<'a>) : Async<'a> =
    fold2 (+) LanguagePrimitives.GenericZero s

  let inline sum3 (s:AsyncSeq<'a>) : Async<'a> =
    fold3 (+) LanguagePrimitives.GenericZero s

  let inline length2 (s:AsyncSeq<'a>) : Async<int> =
    fold3 (fun l _ -> l + 1) 0 s

  let iterAsync2 f (source:AsyncSeq<'T>) =
    match source with
    | :? IFuse<'T> as source -> source.FuseIterAsync f
    | _ -> AsyncSeq.iterAsync f source
  
  let iter2 f (source:AsyncSeq<'T>) =  
    iterAsync2 (f >> async.Return) source

  let iter3 f (source:AsyncSeq<'T>) : Async<unit> = 
    match source with
    | :? IFuse<'T> as source -> source.FuseIter f
    | _ ->
      async {
        use en = source.GetEnumerator()
        let rec go () = async {
          let! next = en.MoveNext()
          match next with
          | None -> return ()
          | Some a ->
            do f a
            return! go () }
        return! go () }

  let skip2 (count:int) (source:AsyncSeq<'T>) : AsyncSeq<'T> =
    let en = source.GetEnumerator ()
    let rec go (en:IAsyncEnumerator<'T>, n:int) = async {
      let! next = en.MoveNext ()
      match next with
      | None -> return None
      | Some a ->
        if n > count then return Some (a, (en, n + 1))
        else return! go (en, n + 1) }
    unfoldDisposeAsync go (en, 0) (en :> IDisposable)

//  let inline sum3 (s:AsyncSeq<'a>) : Async<'a> = async {
//    use en = s.GetEnumerator()
//    let sum = ref LanguagePrimitives.GenericZero
//    let mutable fin = false
//    while not fin do
//      let! next = en.MoveNext ()
//      match next with
//      | Some a ->
//        sum := !sum + a
//      | None -> 
//        fin <- true
//    return !sum }





  // ------------------------------------------------------------------------------------


  let map = map3
  let unfoldAsync = unfoldAsync2
  let sum = sum3
  let take = take2
  let skip = skip2
  let iter = iter3
  let chooseAsync = chooseAsync2

  // ------------------------------------------------------------------------------------
    


let N = 10000000
//let N = 500000

let generator state =
    async {
        //printfn "gen=%A" state
        if state < N then
            return Some (state, state + 1)
        else
            return None
    }


//AsyncSeq.unfoldAsync generator 0
//|> AsyncSeq.skip (N / 2)
//|> AsyncSeq.take (N / 2)
//|> AsyncSeq.map id
//|> AsyncSeq.chooseAsync (Some >> async.Return)
//|> AsyncSeq.iter ignore
//|> Async.RunSynchronously

AsyncSeq.unfoldAsync generator 0
|> AsyncSeq.map id
|> AsyncSeq.chooseAsync (Some >> async.Return)
|> AsyncSeq.iter ignore
|> Async.RunSynchronously





