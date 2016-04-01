(**

# F# AsyncSeq Examples

*)

#r "../../../bin/FSharp.Control.AsyncSeq.dll"
open System
open FSharp.Control


(**

### Group By

Suppose we would like to consume a stream of events `AsyncSeq<Event>` and perform an operation on each event. 
The operation on each event is of type `Event -> Async<unit>`. This can be done as follows:

*)


type Event = {
  entityId : int64
  data : string 
}

let stream : AsyncSeq<Event> =
  failwith "undefined"

let action (e:Event) : Async<unit> =
  failwith "undefined"

stream 
|> AsyncSeq.iterAsync action


(**

The above workflow will read an event from the stream, perform an operation and then read the next event.
While the read operation and the operation on the event are *asynchronous*, the stream is processed *sequentially*.
It may be desirable to parallelize the processing of the stream. Suppose that events correspond to some entity, 
such as a shopping cart. Events belonging to some shopping cart must be processed in a sequential order, however they
are independent from events belonging to other shopping carts. Therefore, events belonging to distinct shopping carts
can be processed in parallel. Using `AsyncSeq.groupBy`, we can partition the stream into a fixed set of sub-streams 
and then process the sub-streams in parallel using `AsyncSeq.mapAsyncParallel`:

*)

stream
|> AsyncSeq.groupBy (fun e -> int e.entityId % 4)
|> AsyncSeq.mapAsyncParallel (snd >> AsyncSeq.iterAsync action)
|> AsyncSeq.iter ignore

(**

`AsyncSeq.groupBy` partitions the input sequence into sub-sequences based on a key returned by a projection function. 
The resulting sub-sequences emit elements when the source sequence emits an element corresponding to the key of the 
sub-sequence. Elements of the resulting sequence are pairs of keys and sub-sequences, in this case `int * AsyncSeq<Event>`. Since by definition, these sub-sequences are independent, they can be processed in parallel. In fact, the sub-sequences *must* be processed in parallel, because it isn't possible to complete the processing of a sub-sequence until all elements of the source sequence are exhausted.

To continue improving the efficiency of our workflow, we can make use of batching. Specifically, we can read the incoming
events in batches and we can perform actions on entire batches of events.

*)

let batchStream : AsyncSeq<Event[]> =
  failwith "undefined"

let batchAction (es:Event[]) : Async<unit> =
  failwith "undefined"


(**

Ordering is still important. For example, the batch action could write events into a full-text search index. We would like the full-text search index to be sequentially consistent. As such, the events need to be applied in the order they were emitted. The following workflow has the desired properties:

*)

batchStream
|> AsyncSeq.concatSeq // flatten the sequence of event arrays
|> AsyncSeq.groupBy (fun e -> int e.entityId % 4) // partition into 4 groups
|> AsyncSeq.mapAsyncParallel (snd 
  >> AsyncSeq.bufferByCountAndTime 500 1000 // buffer sub-sequences
  >> AsyncSeq.iterAsync batchAction) // perform the batch operation
|> AsyncSeq.iter ignore


(**

The above workflow:

1. Reads events in batches.
2. Flattens the batches.
3. Partitions the events into mutually exclusive sub-sequences.
4. Buffers elements of each sub-sequence by time and space.
5. Processes the sub-sequences in parallel, but individual sub-sequences sequentially.

*)



(**

### Merge

`AsyncSeq.merge` non-deterministically merges two async sequences into one. It is non-deterministic in the sense that the resulting sequence emits elements 
whenever *either* input sequence emits a value. Since it isn't always known which will emit a value first, if at all, the operation is non-deterministic. This operation
is in contrast to `AsyncSeq.zip` which also takes two async sequences and returns a single async sequence, but as opposed to emitting an element when *either* input
sequence produces a value, it emits an element when *both* sequences emit a value.

Suppose we would like to trigger an operation whenever a change occurs. We can represent changes as an `AsyncSeq`. To gain intuition for this, consider the [Consul](https://www.consul.io/)
configuration management system. It stores configuration information in a tree-like structure. For this purpose of this discussion, it can be thought of as a key-value store
exposed via HTTP. In addition, `Consul` supports change notifications using HTTP long-polling - when an HTTP GET request is made to retrieve the value of a key, 
if the request specified a modify-index, `Consul` won't respond to the request until a change has occurred *since* the modify-index. We can represent this operation using 
the type `Key * ModifyIndex -> Async<Value * ModifyIndex>`. Next, we can take this operation and turn it into an `AsyncSeq` of changes as follows:
*)

type Key = string

type Value = string

type ModifyIndex = int64

let longPollKey (key:Key, mi:ModifyIndex) : Async<Value * ModifyIndex> =
  failwith "undefined"

let changes (key:Key, mi:ModifyIndex) : AsyncSeq<Value> =
  AsyncSeq.unfoldAsync 
    (fun (mi:ModifyIndex) -> async {
      let! value,mi = longPollKey (key, mi)
      return Some (value,mi) })
    mi

(**

The function `changes` produces an async sequence which emits elements whenever the value corresponding to the key changes. Suppose also that we would like to trigger an operation
whenever the key changes or based on a fixed interval. We can represent a fixed interval as an async sequence as follows:

*)

let intervalMs (periodMs:int) = asyncSeq {
  yield DateTime.UtcNow
  while true do
    do! Async.Sleep periodMs
    yield DateTime.UtcNow }

(**

Putting it all together:

*)

let changesOrInterval : AsyncSeq<Choice<Value, DateTime>> =
  AsyncSeq.mergeChoice (changes ("myKey", 0L)) (intervalMs (1000 * 60))


(**

We can now consume this async sequence and use it to trigger downstream operations, such as updating the configuration of a running program, in flight.

*)