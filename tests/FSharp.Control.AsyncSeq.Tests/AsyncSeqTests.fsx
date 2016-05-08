#r @"../../bin/FSharp.Control.AsyncSeq.dll"
#r @"../../packages/NUnit/lib/nunit.framework.dll"
#time "on"
//#load "AsyncSeqTests.fs"

open System
open FSharp.Control

module AsyncSeq =

  let rec unfoldAsync2 (f:'State -> Async<('T * 'State) option>) (s:'State) : AsyncSeq<'T> = 
    { new IAsyncEnumerable<'T> with
        member __.GetEnumerator () =
          let s = ref s
          { new IAsyncEnumerator<'T> with
              member __.MoveNext () : Async<'T option> = async {
                let! next = f !s 
                match next with
                | None -> return None 
                | Some (a,s') ->
                  s := s'
                  return Some a }
              member __.Dispose () = () } }

  let rec unfoldAsync (f:'State -> Async<('T * 'State) option>) (s:'State) : AsyncSeq<'T> = 
    asyncSeq {
      let s = ref s
      let fin = ref false
      while not !fin do        
        let! next = f !s
        //Printf.printfn "unfoldAsync|next=%A" next
        match next with
        | None ->
          fin := true
        | Some (a,s') ->
          //Printf.printfn "unfoldAsync|yielding=%A|s'=%A" a s'
          yield a
          s := s' }
      



let generator state =
    async {
        //printfn "gen=%A" state
        if state < 10000 then
            return Some (state, state + 1)
        else
            return None
    }

AsyncSeq.unfoldAsync generator 0
//|> AsyncSeq.iter (printfn "iter=%A")
|> AsyncSeq.iter ignore
|> Async.RunSynchronously


//AsyncSeqTests.``AsyncSeq.unfoldAsync should be iterable in finite resources``()