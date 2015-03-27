(**

# F# Async: FSharp.Control.AsyncSeq

An AsyncSeq is a sequence in which individual elements are retrieved using an `Async` computation.
It is similar to `seq<'a>` in that subsequent elements are pulled asynchronously. Structurally it is
similar to `list<'a>` with the difference being that each head and tail node or empty node is wrapped
in `Async`. `AsyncSeq` also bears similarity to `IObservable<'a>` with the former being based on an "asynchronous pull" and the
latter based on a "synchronous push". Analogs for most operations defined for `Seq`, `List` and `IObservable` are also defined for 
`AsyncSeq`. The power of `AsyncSeq` lies in that many of these operations also have analogs based on `Async` 
allowing composition of complex asynchronous workflows.

The `AsyncSeq` type is located in the `FSharp.Control.AsyncSeq.dll` assembly which can be loaded in F# Interactive as follows:
*)

#r "../../../bin/FSharp.Control.AsyncSeq.dll"
open FSharp.Control



(**
### Generating asynchronous sequences

An `AsyncSeq<'a>` can be generated using computation expression syntax much like `seq<'a>`:
*)

let asyncS = asyncSeq {
  yield 1
  yield 2
}

(**
Another way to generate an asynchronous sequence is using the `Async.unfoldAsync` function. This
function accepts as an argument a function which can generate individual elements based on a state and 
signal completion of the sequence.

For example, suppose that you're writing a program which consumes the Twitter API and stores tweets
which satisfy some criteria into a database. There are several asynchronous request-reply interactions at play - 
one to retrieve a batch of tweets from the Twitter API, another to determine whether a tweet satisfies some
criteria and finally an operation to write the desired tweet to a database. 

Given the type `Tweet` to represent an individual tweet, the operation to retrieve a batch of tweets can 
be modeled with type `int -> Async<(Tweet[] * int) option>` where the incoming `int` represents the 
offset into the tweet stream. The asynchronous result is an `Option` which when `None` indicates the
end of the stream, and otherwise contains the batch of retrieved tweets as well as the next offset.

The above function to retrieve a batch of tweets can be used to generate an asynchronous sequence 
of tweet batches as follows:
*)

type Tweet = {
  user : string
  message : string
}

let getTweetBatch (offset:int) : Async<(Tweet[] * int) option> = 
  failwith "TODO: call Twitter API"

let tweetBatches : AsyncSeq<Tweet[]> = 
  AsyncSeq.unfoldAsync getTweetBatch 0

(**
The asynchronous sequence `tweetBatches` will when iterated, incrementally consume the entire tweet stream.

Next, suppose that the tweet filtering function makes a call to a web service which determines
whether a particular tweet is of interest and should be stored in the database. This function can be modeled with
type `Tweet -> Async<bool>`. We can flatten the `tweetBatches` sequence and then filter it as follows:
*)

let filterTweet (t:Tweet) : Async<bool> =
  failwith "TODO: call web service"

let filteredTweets : AsyncSeq<Tweet> = 
  tweetBatches
  |> AsyncSeq.concatSeq // flatten
  |> AsyncSeq.filterAsync filterTweet // filter

(**
When the resulting sequence `filteredTweets` is consumed, it will lazily consume the underlying
sequence `tweetBatches`, select individual tweets and filter them using the function `filterTweets`.

Finally, the function which stores a tweet in the database can be modeled by type `Tweet -> Async<unit>`.
We can store all filtered tweets as follows:
*)

let storeTweet (t:Tweet) : Async<unit> =
  failwith "TODO: call database"

let storeFilteredTweets : Async<unit> =
  filteredTweets
  |> AsyncSeq.iterAsync storeTweet

(**
Note that the value `storeFilteredTweets` is an asynchronous computation of type `Async<unit>`. At this point,
it is a *representation* of the workflow which consists of reading batches of tweets, filtering them and storing them
in the database. When executed, the workflow will consume the entire tweet stream. The entire workflow can be
succinctly declared and executed as follows:
*)

AsyncSeq.unfoldAsync getTweetBatch 0
|> AsyncSeq.concatSeq
|> AsyncSeq.filterAsync filterTweet
|> AsyncSeq.iterAsync storeTweet
|> Async.RunSynchronously

(**
The above snippet effectively orchestrates several asynchronous request-reply interactions into a cohesive unit
composed using familiar operations on sequences. Furthermore, it will be executed efficiently in a non-blocking manner.
*)

(**
### Comparison with seq<'a>

The central difference between `seq<'a>` and `AsyncSeq<'a>` two can be illustrated by introducing the notion of time. 
Suppose that generating subsequent elements of a sequence requires an IO-bound operation. Invoking long 
running IO-bound operations from within a `seq<'a>` will *block* the thread which calls `MoveNext` on the 
corresponding `IEnumerator`. An `AsyncSeq` on the other hand can use facilities provided by the F# `Async` type to make 
more efficient use of system resources.
*)

let withTime = seq {
  System.Threading.Thread.Sleep(1000) // calling thread will block
  yield 1
  System.Threading.Thread.Sleep(1000) // calling thread will block
  yield 1
}

let withTime' = asyncSeq {
  do! Async.Sleep 1000 // non-blocking sleep
  yield 1
  do! Async.Sleep 1000 // non-blocking sleep
  yield 2
}

(**
When the asynchronous sequence `withTime'` is iterated, the calls to `Async.Sleep` won't block threads. Instead,
the *continuation* of the sequence will be scheduled by `Async` while the calling thread will be free to perform other work. 
Overall, a `seq<'a>` can be viewed as a special case of an `AsyncSeq<'a>` where subsequent elements are retrieved
in a blocking manner.
*)


(**
### Comparison with IObservable<'a>

Both `IObservable<'a>` and `AsyncSeq<'a>` represent collections of items and both provide similar operations
for transformation and composition. The central difference between the two is that the former used a *synchronous push*
and the latter uses an *asynchronous pull*. Consumers of an `IObservable<'a>` *subscribe* to receive notifications about
new items or the end of the sequence. By contrast, consumers of an `AsyncSeq<'a>` *asynchronously retrieve* subsequent items on their own
terms. Some domains are more naturally modeled with one or the other, however it is less clear which is a more
suitable tool for a specific task. In many cases, a combination of the two provides the optimal solution and 
restricting yourself to one, while simplifying the programming model, can lead one to view all problems as a nail.

A more specific difference between the two is that `IObservable<'a>` subscribers have the basic type `'a -> unit` 
and are therefore inherently synchronous and imperative. The observer can certainly make a blocking call, but this 
can defeat the purpose of the observable sequence all together. Alternatively, the observer can spawn an operation, but
this can break composition because one can no longer rely on the observer returning to determine that it has 
completed. With the observable model however, we can model blocking operations through composition on sequences rather
than observers.

To illustrate, lets try to implement the above Tweet retrieval, filtering and storage workflow using observable sequences.
Suppose we already have an observable sequence representing tweets `IObservable<Tweet>` and we simply wish 
to filter it and store the resulting tweets. The function `Observable.filter` allows one to filter observable
sequences based on a predicate, however in this case it doesn't quite cut it because the predicate passed to it must
be synchronous `'a -> bool`:
*)

open System

let tweetsObs : IObservable<Tweet> =
  failwith "TODO: create observable"

let filteredTweetsObs =
  tweetsObs
  |> Observable.filter (filterTweet >> Async.RunSynchronously) // blocking IO-call!

(**
To remedy the blocking IO-call we can better adapt the filtering function to the `IObservable<'a>` model. A value
of type `Async<'a>` can be modeled as an `IObservable<'a>` with one element. Suppose that we have 
`Tweet -> IObservable<bool>`. We can define a few helper operators on observables to allow filtering using
an asynchronous predicate as follows:
*)

module Observable =
  
  /// a |> Async.StartAsTask |> (fun t -> t.ToObservable())
  let ofAsync (a:Async<'a>) : IObservable<'a> =
    failwith "TODO"

  /// Observable.SelectMany
  let bind (f:'a -> IObservable<'b>) (o:IObservable<'a>) : IObservable<'b> =
    failwith "TODO"

  /// Filter an observable sequence using a predicate producing a observable
  /// which emits a single boolean value.
  let filterObs (f:'a -> IObservable<bool>) : IObservable<'a> -> IObservable<'a> =
    bind <| fun a -> 
      f a
      |> Observable.choose (function
        | true -> Some a
        | false -> None
      )
  
  /// Filter an observable sequence using a predicate which returns an async
  /// computation producing a boolean value.
  let filterAsync (f:'a -> Async<bool>) : IObservable<'a> -> IObservable<'a> =
    filterObs (f >> ofAsync)

  /// Maps over an observable sequence using an async-returning function.
  let mapAsync (f:'a -> Async<'b>) : IObservable<'a> -> IObservable<'b> =
    bind (f >> ofAsync)

let filteredTweetsObs' : IObservable<Tweet> =
  filteredTweetsObs
  |> Observable.filterAsync filterTweet


(**
With a little effort, we were able to adapt `IObservable<'a>` to our needs. Next lets try implementing the storage of
filtered tweets. Again, we can adapt the function `storeTweet` defined above to the observable model and bind the
observable of filtered tweets to it:
*)

let storedTweetsObs : IObservable<unit> =
  filteredTweetsObs'
  |> Observable.mapAsync storeTweet

(**
The observable sequence `storedTweetsObs` will produces a value each time a filtered tweet is stored. The entire
workflow can be expressed as follows:
*)

let storedTeetsObs' : IObservable<unit> =
  tweetsObs
  |> Observable.filterAsync filterTweet
  |> Observable.mapAsync storeTweet

(**
Overall, both solutions are succinct and composable and deciding which one to use can ultimately be a matter of preference. 
Some things to consider are the "synchronous push" vs. "asynchronous pull" semantics. On the one hand, tweets are pushed based - the consumer has no control 
over their generation. On the other hand, the program at hand will process the tweets on its own terms regardless of how quickly 
they are being generated. Moreover, the underlying Twitter API will likely utilize a request-reply protocol to retrieve batches of 
tweets from persistent storage. As such, the distinction between "synchronous push" vs. "asynchronous pull" becomes less interesting. If the underlying source 
is truly push-based, then one can buffer its output and consume it using an asynchronous sequence. If the underlying source is pull-based, 
then one can turn it into an observable sequence by first pulling, then pushing. Note however that in a true real-time reactive system, 
notifications must be pushed immediately without delay.

Upon closer inspection, the consumption approaches between the two models aren't all too different. While `AsyncSeq` is based on an asynchronous-pull operation,
it is usually consumed using an operator such as `AsyncSeq.iterAsync` as shown above. This is a function of type 
`('a -> Async<unit>) -> AsyncSeq<'a> -> Async<unit>` where the first argument is a function `'a -> Async<unit>` which performs 
some work on an item of the sequence and is applied repeatedly to subsequent items. In a sense, `iterAsync` *pushes* values to this 
function. The primary difference from observers of observable sequences is the return type `Async<unit>` rather than simply `unit`.
*)


(**
### Performance Considerations

While an asynchronous computation obviates the need to block an OS thread for the duration of an operation, it isn't always the case
that this will improve the overall performance of an application. Note however that an async computation does not *require* a
non-blocking operation, it simply allows for it. Also of note is that unlike calling `IEnumerable.MoveNext()`, consuming
and item from an asynchronous sequence requires several allocations. Usually this is greatly outweighed by the
benefits, it can make a difference in some scenarios.

*)


(**
## Related Articles

 * [Programming with F# asynchronous sequences](http://tomasp.net/blog/async-sequences.aspx/)

*)
