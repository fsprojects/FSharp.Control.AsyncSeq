(*** condition: prepare ***)
#nowarn "211"
#I "../src/FSharp.Control.AsyncSeq/bin/Release/netstandard2.1"
#r "FSharp.Control.AsyncSeq.dll"
(*** condition: fsx ***)
#if FSX
#r "nuget: FSharp.Control.AsyncSeq,{{package-version}}"
#endif // FSX
(*** condition: ipynb ***)
#if IPYNB
#r "nuget: FSharp.Control.AsyncSeq,{{package-version}}"
#endif // IPYNB


(**
[![Binder](https://mybinder.org/badge_logo.svg)](https://mybinder.org/v2/gh/fsprojects/FSharp.Control.AsyncSeq/gh-pages?filepath=AsyncSeq.ipynb)

# F# Async: FSharp.Control.AsyncSeq

> NOTE: There is also the option to use [FSharp.Control.TaskSeq](https://github.com/fsprojects/FSharp.Control.TaskSeq) which has a very similar usage model.

An AsyncSeq is a sequence in which individual elements are retrieved using an `Async` computation.
It is similar to `seq<'a>` in that subsequent elements are pulled on-demand.
`AsyncSeq` also bears similarity to `IObservable<'a>` with the former being based on an "asynchronous pull" and the
latter based on a "synchronous push". Analogs for most operations defined for `Seq`, `List` and `IObservable` are also defined for 
`AsyncSeq`. The power of `AsyncSeq` lies in that many of these operations also have analogs based on `Async` 
allowing composition of complex asynchronous workflows.

> **v4.0 and later:** `AsyncSeq<'T>` is a type alias for `System.Collections.Generic.IAsyncEnumerable<'T>`.
> Any `IAsyncEnumerable<'T>` value (e.g. from EF Core, ASP.NET Core channels, or `taskSeq { }`) can be used
> directly as an `AsyncSeq<'T>` without conversion, and vice-versa.

The `AsyncSeq` type is located in the `FSharp.Control.AsyncSeq.dll` assembly which can be loaded in F# Interactive as follows:
*)

#r "../../../bin/FSharp.Control.AsyncSeq.dll"
open FSharp.Control

(**
### Generating asynchronous sequences

An `AsyncSeq<'T>` can be generated using computation expression syntax much like `seq<'T>`:
*)

let async12 = asyncSeq {
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

let getTweetBatch (offset: int) : Async<(Tweet[] * int) option> = 
  failwith "TODO: call Twitter API"

let tweetBatches : AsyncSeq<Tweet[]> = 
  AsyncSeq.unfoldAsync getTweetBatch 0

(**
The asynchronous sequence `tweetBatches` will when iterated, incrementally consume the entire tweet stream.

Next, suppose that the tweet filtering function makes a call to a web service which determines
whether a particular tweet is of interest and should be stored in the database. This function can be modeled with
type `Tweet -> Async<bool>`. We can flatten the `tweetBatches` sequence and then filter it as follows:
*)

let filterTweet (t: Tweet) : Async<bool> =
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

let storeTweet (t: Tweet) : Async<unit> =
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
### Comparison with seq<'T>

The central difference between `seq<'T>` and `AsyncSeq<'T>` can be illustrated by introducing the notion of time.
Suppose that generating subsequent elements of a sequence requires an IO-bound operation. Invoking long 
running IO-bound operations from within a `seq<'T>` will *block* the thread which calls `MoveNext` on the 
corresponding `IEnumerator`. An `AsyncSeq` on the other hand can use facilities provided by the F# `Async` type to make 
more efficient use of system resources.
*)

let withTime = seq {
  Thread.Sleep(1000) // calling thread will block
  yield 1
  Thread.Sleep(1000) // calling thread will block
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

### Performance Considerations

While an asynchronous computation obviates the need to block an OS thread for the duration of an operation, it isn't always the case
that this will improve the overall performance of an application. Note however that an async computation does not *require* a
non-blocking operation, it simply allows for it. Also of note is that unlike calling `IEnumerable.MoveNext()`, consuming
an item from an asynchronous sequence requires several allocations. Usually this is greatly outweighed by the
benefits, it can make a difference in some scenarios.

## Related Articles

 * [Programming with F# asynchronous sequences](http://tomasp.net/blog/async-sequences.aspx/)

*)
