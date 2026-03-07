#r "nuget: FSharp.Control.AsyncSeq,{{package-version}}"
(**
[![Binder](https://mybinder.org/badge_logo.svg)](https://mybinder.org/v2/gh/fsprojects/FSharp.Control.AsyncSeq/gh-pages?filepath=AsyncSeq.ipynb)

# Comparison with IObservable

Both `IObservable<'T>` and `AsyncSeq<'T>` represent collections of items and both provide similar operations
for transformation and composition. The central difference between the two is that the former uses a *synchronous push*
to a subscriber and the latter uses an *asynchronous pull* by a consumer. 
Consumers of an `IObservable<'T>` *subscribe* to receive notifications about
new items or the end of the sequence. By contrast, consumers of an `AsyncSeq<'T>` *asynchronously retrieve* subsequent items on their own
terms. Some domains are more naturally modeled with one or the other, however it is less clear which is a more
suitable tool for a specific task. In many cases, a combination of the two provides the optimal solution and 
restricting yourself to one, while simplifying the programming model, can lead one to view all problems as a nail.

A more specific difference between the two is that `IObservable<'T>` subscribers have the basic type `'T -> unit` 
and are therefore inherently synchronous and imperative. The observer can certainly make a blocking call, but this 
can defeat the purpose of the observable sequence all together. Alternatively, the observer can spawn an operation, but
this can break composition because one can no longer rely on the observer returning to determine that it has 
completed. With the observable model however, we can model blocking operations through composition on sequences rather
than observers.

To illustrate, let's try to implement the above Tweet retrieval, filtering and storage workflow using observable sequences.
Suppose we already have an observable sequence representing tweets `IObservable<Tweet>` and we simply wish 
to filter it and store the resulting tweets. The function `Observable.filter` allows one to filter observable
sequences based on a predicate, however in this case it doesn't quite cut it because the predicate passed to it must
be synchronous `'T -> bool`:

*)
open System

// At the top of docs/ComparisonWithObservable.fsx
type Tweet = { Id: int; Content: string }
let filterTweet (tweet: Tweet) = async { return tweet.Content.Contains("keyword") }
let storeTweet (tweet: Tweet) = printfn "Storing tweet %d" tweet.Id

let tweetsObs : IObservable<Tweet> =
  failwith "TODO: create observable"

let filteredTweetsObs =
  tweetsObs
  |> Observable.filter (filterTweet >> Async.RunSynchronously) // blocking IO-call!
(**
To remedy the blocking IO-call we can better adapt the filtering function to the `IObservable<'T>` model. A value
of type `Async<'T>` can be modeled as an `IObservable<'T>` with one element. Suppose that we have 
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
With a little effort, we were able to adapt `IObservable<'a>` to our needs. Next let's try implementing the storage of
filtered tweets. Again, we can adapt the function `storeTweet` defined above to the observable model and bind the
observable of filtered tweets to it:

*)
let storeTweet (tweet: Tweet) : Async<unit> =
  failwith "TODO"

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
`('T -> Async<unit>) -> AsyncSeq<'T> -> Async<unit>` where the first argument is a function `'T -> Async<unit>` which performs 
some work on an item of the sequence and is applied repeatedly to subsequent items. In a sense, `iterAsync` *pushes* values to this 
function. The primary difference from observers of observable sequences is the return type `Async<unit>` rather than simply `unit`.

## Related Articles

 * [Programming with F# asynchronous sequences](http://tomasp.net/blog/async-sequences.aspx/)


*)

