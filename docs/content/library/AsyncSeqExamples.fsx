(**

# F# AsyncSeq Examples

*)


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
|> AsyncSeq.groupBy (fun e -> e.entityId % 4)
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
|> AsyncSeq.groupBy (fun e -> e.entityId % 4) // partition into 4 groups
|> AsyncSeq.mapAsyncParallel (snd 
	>> AsyncSeq.bufferByTimeAndCount 500 1000  // buffer sub-sequences
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
