(**

# F# Async: AsyncSeq

An AsyncSeq is an sequence in which individual elements are retrieved using an `Async` computation.
It is similar to `seq<'a>` in that subsequent elements are pulled lazily. Structurally it is
similar to `list<'a>` with the difference being that each head and tail node or empty node is wrapped
in `Async`. `AsyncSeq` also bears similarity to `IObservable<'a>` with the former being pull-based and the
latter push-based. Analogs for most operations defined for `Seq`, `List` and `IObservable` are also defined for 
`AsyncSeq`. The power of `AsyncSeq` lies in that many of these operations also have analogs based on `Async` 
allowing one to compose complex asynchronous workflows.

The `AsyncSeq` type is located in the `FSharpx.Async.dll assembly which can be loaded in F# Interactive as follows:
*)

#r "../../../bin/FSharpx.Async.dll"
open FSharpx.Control



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
function takes another function which can generate individual elements based on a state. It can 
signal completion of the sequence.

For example, suppose that you're writing a program which consumes the Twitter API and stores tweets
which satisfy some criteria into a database. There are several asynchronous request-reply operations at play - 
one to retrieve a batch of tweets from the Twitter API, another to determine whether a tweet satisfies some
criteria and finally an operation to write the desired tweet to a database. 

Given the type `Tweet` to represent an individual tweet, the operation to retrieve a batch of tweets can 
be modeled with a type `int -> Async<(Tweet[] * int) option>` where the incoming `int` represents the 
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
The asynchronous sequence `tweetBatches` will when iterated consume the entire tweet stream.

Next, suppose that the tweet filtering function makes a call to a web service which determines
whether a particular tweet should be stored in the database. This function can be modeled with
type `Tweet -> Async<bool>`. We can flatten the `tweetBatches` sequence and then filter it using 
this function:
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
it is a **representation** of the workflow which consists of reading batches of tweets, filtering them and storing them
in the database. When executed, the workflow will consume the entire tweet stream. The entire workflow can be
succinctly expressed and executed as follows:
*)

AsyncSeq.unfoldAsync getTweetBatch 0
|> AsyncSeq.concatSeq
|> AsyncSeq.filterAsync filterTweet
|> AsyncSeq.iterAsync storeTweet
|> Async.RunSynchronously

(**
The above snippet effectively orchestrates several asynchronous request-reply interactions into a cohesive unit
composed with familiar operations on sequences. Furthermore, it can be executed efficiently in a non-blocking manner.
*)

(**
### Comparison with seq<'a>

The central difference between `seq<'a>` and `AsyncSeq<'a>` two can be illustrated by introducing the notion of time. 
Suppose that generating subsequent elements of a sequence requires an IO-bound operation. Invoking long 
running IO-bound operations from within a `seq<'a>` will **block** the thread which calls `MoveNext` on the 
corresponding `IEnumerator`. An `AsyncSeq` can use facilities provided by the F# `Async` type to make more efficient 
use of system resources.
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
the **continuation** of the sequence will be scheduled by a `ThreadPool` thread, while the calling thread
will be free to perform other work.
*)


(**
### Comparison with IObservable<'a>

Both `IObservable<'a>` and `AsyncSeq<'a>` represent collections of items and both provide similar operations
for transformation and composition. The central difference between the two is that the former is push-based 
and the latter is pull-based. Consumers of an `IObservable<'a>` **subscribe** to receive notifications about
new items or completion. By contrast, consumers of an `AsyncSeq<'a>` **retrieve** subsequent items on their own
terms. Some domains are more naturally modeled with one or the other, however it is less clear which is a more
suitable tool for a specific task. In many cases, a combination of the two provides the optimal solution and 
restricting yourself to one, while simplifying the programming model, can lead one two view all problems as a nail.

A more specific difference between the two is that `IObservable<'a>` subscribers have the basic type `'a -> unit` 
and are therefore inherently synchronous and imperative. The observer can certainly make a blocking call, but this 
can defeat the purpose of the observable sequence all together. Alternatively, the observer can spawn an operation, but
this can break composition because one can no longer rely on the observer operation returning to determine that it has 
completed. With the observable model however, we can model blocking operations through composition.

To illustrate, lets try to implement the above Tweet retrieval, filtering and storage workflow using observable sequences.
Suppose we already have an observable sequence representing tweets `IObservable<Tweet>` and we simply wish 
to filter it and store the resulting tweets. The function `Observable.filter` allows one to filter observable
sequences based on a predicate, however in this case it doesn't quite cut it because the predicate is synchronous 
`'a -> bool`:
*)

open System

let tweetsObs : IObservable<Tweet> =
  failwith "TODO: create observable"

let filteredTweetsObs =
  tweetsObs
  |> Observable.filter (filterTweet >> Async.RunSynchronously) // blocking IO-call!

(**
To remedy the blocking IO-call we can adapt the filtering function to the `IObservable<'a>` model. An `Async<'a>` 
can be modeled as an `IObservable<'a>` with one element so suppose that we have `Tweet -> IObservable<bool>`. We can 
then compose an observable that filters tweets using this function as follows:
*)

module Observable =
  
  let ofAsync (a:Async<'a>) : IObservable<'a> =
    failwith "TODO"

  /// Observable.SelectMany in Rx
  let bind (f:'a -> IObservable<'b>) (o:IObservable<'a>) : IObservable<'b> =
    failwith "TODO"

  let filterObs (f:'a -> IObservable<bool>) : IObservable<'a> -> IObservable<'a> =
    bind <| fun a -> 
      f a
      |> Observable.choose (function
        | true -> Some a
        | false -> None
      )
  
  let filterAsync (f:'a -> Async<bool>) : IObservable<'a> -> IObservable<'a> =
    filterObs (f >> ofAsync)

  let mapAsync (f:'a -> Async<'b>) : IObservable<'a> -> IObservable<'b> =
    bind (f >> ofAsync)

let filteredTweetsObs' : IObservable<Tweet> =
  filteredTweetsObs
  |> Observable.filterAsync filterTweet


(**
With little effort we were able to adapt `IObservable<'a>` to our needs. Next lets try implementing the storage of
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
Overall, both solutions are succinct and composable and can ultimately be a matter of preference. Some things to consider
are the push vs. pull semantics. On the one hand, tweets are pushed based - the consumer has no control over their generation.
On the other hand, the program at hand will process the tweets on its own terms regardless of how quickly they are being generated.
Moreover, the underlying Twitter API will likely utilize a request-reply protocol to retrieve batches of tweets from persistent 
storage. As such, the distinction between push vs. pull becomes less interesting. If the underlying source is truly push-based, then
one can buffer its output and consume it using an asynchronous sequence. If the underlying source is pull-based, then one can turn
it into an observable sequence by first pulling, then pushing. In a real-time reactive system, notifications must be pushed 
immediately without delay. This point however is moot since neither `IObservable<'a>` nor `Async<'a>` are well suited for 
real-time systems.
*)


(**
### Performance Considerations

While an async computation obviates the need to block an OS thread for the duration of an operation, it isn't always the case
that this will improve the overall performance of an application. Note however that an async computation does not **require** a
non-blocking operation, it simply allows for it.

*)


(**
## Related Articles

 * [Programming with F# asynchronous sequences](http://tomasp.net/blog/async-sequences.aspx/)

*)
