namespace FSharpx.Control


/// An infinite async sequence.
type AsyncStream<'a> = Async<AsyncStreamNode<'a>>

/// A node of an async stream consisting of an element and the rest of the stream.
and AsyncStreamNode<'a> = ASN of 'a * AsyncStream<'a>

/// Operations on async stream nodes.
module AsyncStreamNode =
  
  let inline un (ASN(hd,tl)) = hd,tl

  let inline create hd tl = ASN(hd,tl)

  let inline head (ASN(a,_)) = a

  let inline tail (ASN(_,tl)) = tl  

  let rec map (f:'a -> 'b) (ASN(a,tail)) : AsyncStreamNode<'b> =
    ASN(f a, tail |> Async.map (map f))

  let rec repeat (a:'a) : AsyncStreamNode<'a> =    
    ASN (a, async.Delay(fun() -> repeat a |> async.Return))
    
  let rec toAsyncSeq (ASN(a,tail)) : AsyncSeq<'a> = asyncSeq {
    yield a
    yield! tail |> Async.bind toAsyncSeq }    


/// Operations on async streams.
module AsyncStream =
  
  /// Creates an async stream given a head and tail.
  let inline create hd tl = 
    AsyncStreamNode.create hd tl |> async.Return

  /// Creates an async stream which repeatedly returns the provided value.
  let repeat (a:'a) : AsyncStream<'a> = 
    AsyncStreamNode.repeat a |> async.Return
    
  /// Generates an async stream.
  let rec unfoldAsync (f:'s -> Async<'a * 's>) (z:'s) : AsyncStream<'a> =
    f z |> Async.map (fun (a,s') -> AsyncStreamNode.create a (unfoldAsync f s'))

  /// Returns infinite repetition of the specified list.
  let cycleList (xs:'a list) : AsyncStream<'a> =
    let rec loop = function
      | [] -> loop xs
      | hd::tl -> create hd (async.Delay(fun() -> loop tl))
    loop xs

  /// Prepends a list to a stream.
  let rec prefixList (xs:'a list) (a:AsyncStream<'a>) : AsyncStream<'a> =
    match xs with
    | [] -> a
    | hd::tl -> create hd (prefixList tl a)

  /// Prepends an async sequence to a stream.
  let rec prefixAsyncSeq (s:AsyncSeq<'a>) (a:AsyncStream<'a>) : AsyncStream<'a> =
    s |> Async.bind (function
      | Nil -> a
      | Cons(hd,tl) -> create hd (prefixAsyncSeq tl a))

  /// Produces the infinite sequence of repeated applications of f.
  let rec iterate (f:'a -> 'a) (a:'a) : AsyncStream<'a> =
    let a' = f a in
    create a' (iterate f a')

  /// Returns the first element of the stream.
  let head (s:AsyncStream<'a>) : Async<'a> =
    s |> Async.map (AsyncStreamNode.head)

  /// Creates a stream which skips the first element of the provided stream.       
  let tail (s:AsyncStream<'a>) : AsyncStream<'a> =
    s |> Async.bind (fun (ASN(_,tl)) -> tl)             

  /// Creates a stream of tails of the specified stream.
  let rec tails (s:AsyncStream<'a>) : AsyncStream<AsyncStream<'a>> = 
    s |> Async.bind (fun (ASN(_,tl)) -> create tl (tails tl))

  /// Maps a function over an async stream.
  let rec mapAsync (f:'a -> Async<'b>) (s:AsyncStream<'a>) : AsyncStream<'b> = async {
    let! (ASN(a,rest)) = s in
    let! b = f a
    return ASN(b, mapAsync f s)
  }

  /// Maps each element of an async stream onto an async sequences returning a stream
  /// containing consecutive elements of the genereated async sequences.
  let rec mapAsyncSeq (f:'a -> AsyncSeq<'b>) (s:AsyncStream<'a>) : AsyncStream<'b> =
    s |> Async.bind (fun (ASN(hd,tl)) -> prefixAsyncSeq (f hd) (mapAsyncSeq f tl))

  /// Creates an infinite async sequence from the stream.
  let toAsyncSeq (s:AsyncStream<'a>) : AsyncSeq<'a> =
    s |> Async.bind (AsyncStreamNode.toAsyncSeq)

  /// Creates an async sequence which iterates through the first n elements from the stream.
  let take (n:int) (s:AsyncStream<'a>) : AsyncSeq<'a> =
    s |> toAsyncSeq |> AsyncSeq.take n

  /// Takes elements from the stream until the specified predicate is no longer satisfied.
  let takeWhileAsync (p:'a -> Async<bool>) (s:AsyncStream<'a>) : AsyncSeq<'a> =
    s |> toAsyncSeq |> AsyncSeq.takeWhileAsync p

  /// Drops the first n items from the stream.
  let rec drop (n:int) (s:AsyncStream<'a>) : AsyncStream<'a> =
    if n <= 0 then s
    else s |> Async.bind (fun (ASN(_,tl)) -> drop (n - 1) tl)

  /// Returns a pair consisting of the prefix of the stream of the specified length
  /// and the remaining stream immediately following this prefix.
  let splitAtList (n:int) (s:AsyncStream<'a>) : Async<'a list * AsyncStream<'a>> =
    let rec loop n s xs =
      if n <= 0 then (xs |> List.rev,s) |> async.Return
      else s |> Async.bind (fun (ASN(hd,tl)) -> loop (n - 1) tl (hd::xs) )
    loop n s []

  /// Filters a stream based on the specified predicate.
  let rec filterAsync (p:'a -> Async<bool>) (s:AsyncStream<'a>) : AsyncStream<'a> =    
    s
    |> Async.bind (fun (ASN(hd,tl)) ->      
      p hd
      |> Async.bind (function
        | true -> ASN(hd, filterAsync p tl) |> async.Return
        | false -> filterAsync p tl
      )
    )
  
  /// Filters and maps a stream using the specified choose function.
  let rec chooseAsync (f:'a -> Async<'b option>) (s:AsyncStream<'a>) : AsyncStream<'b> =
    s
    |> Async.bind (fun (ASN(a,tl)) ->
      f a
      |> Async.bind (function
        | Some b -> create b (chooseAsync f tl)
        | None -> chooseAsync f tl
      )
    )

  /// Scans the stream applying the specified function to consecutive elements and
  /// returning the stream of results. 
  let rec scanAsync (f:'a -> 'b -> Async<'b>) (z:'b) (s:AsyncStream<'a>) : AsyncStream<'b> =
    s
    |> Async.bind (fun (ASN(hd,tl)) ->      
      f hd z
      |> Async.map (fun b -> ASN(b, scanAsync f b tl))
    )

  /// Creates a computation which applies the function f to elements of the stream forever.
  let rec iterAsync (f:'a -> Async<unit>) (s:AsyncStream<'a>) : Async<unit> =
    s |> Async.bind (fun (ASN(hd,tl)) -> f hd |> Async.bind (fun _ -> iterAsync f tl))
    
  /// Zips two streams using the specified function.
  let rec zipWith (f:'a -> 'b -> 'c) (a:AsyncStream<'a>) (b:AsyncStream<'b>) : AsyncStream<'c> =
    Async.Parallel(a, b)
    |> Async.map (fun (ASN(a,atl),ASN(b,btl)) -> AsyncStreamNode.create (f a b) (zipWith f atl btl))

  /// Zips two streams into a stream of pairs.
  let rec zip (a:AsyncStream<'a>) (b:AsyncStream<'b>) : AsyncStream<'a * 'b> =
    zipWith (fun a b -> (a,b)) a b

  /// Takes a list of streams and produces a stream of lists.
  let rec distributeList (xs:AsyncStream<'a> list) : AsyncStream<'a list> =
    xs 
    |> Async.Parallel 
    |> Async.bind (fun xs ->      
      let items,tails = 
        xs 
        |> List.ofArray
        |> List.map (AsyncStreamNode.un) 
        |> List.unzip
      create items (distributeList tails)
    )