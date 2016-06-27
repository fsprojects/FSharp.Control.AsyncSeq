(**

# F# AsyncSeq Examples

*)

#r "../../../bin/FSharp.Control.AsyncSeq.dll"
open System
open FSharp.Control


(**

## Group By

`AsyncSeq.groupBy` partitions an input sequence into sub-sequences with respect to the specified `projection` function. This operation is the asynchronous analog to `Seq.groupBy`.


## Use Case

Suppose we would like to consume a stream of events `AsyncSeq<Event>` and perform an operation on each event. The operation on each event is of type `Event -> Async<unit>`. This can be done as follows:

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

let stream : AsyncSeq<Event[]> =
  failwith "undefined"

let action (es:Event[]) : Async<unit> =
  failwith "undefined"


(**

Ordering is still important. For example, the batch action could write events into a full-text search index. We would like the full-text search index to be sequentially consistent. As such, the events need to be applied in the order they were emitted. The following workflow has the desired properties:

*)




stream
|> AsyncSeq.concatSeq
|> AsyncSeq.groupBy (fun e -> int e.entityId % 4)
|> AsyncSeq.mapAsyncParallel (snd
  >> AsyncSeq.bufferByCountAndTime 500 1000
  >> AsyncSeq.iterAsync action)
|> AsyncSeq.iter ignore


(**

The above workflow:

1. Reads events in batches.
2. Flattens the batches.
3. Partitions the events into mutually exclusive sub-sequences.
4. Buffers elements of each sub-sequence by time and space.
5. Processes the sub-sequences in parallel, but individual sub-sequences sequentially.

---

*)



(**

## Merge

`AsyncSeq.merge` non-deterministically merges two async sequences into one. It is non-deterministic in the sense that the resulting sequence emits elements whenever *either* input sequence emits a value. Since it isn't always known which will emit a value first, if at all, the operation is non-deterministic. This operation is in contrast to `AsyncSeq.zip` which also takes two async sequences and returns a single async sequence, but as opposed to emitting an element when *either* input sequence produces a value, it emits an element when *both* sequences emit a value. This operation is also in contrast to `AsyncSeq.append` which concatenates two async sequences, emitting all element of one, followed by all elements of the another.

### Example Execution

An example execution can be depicted visually as follows:

-----------------------------------------
| source1 | t0 |    | t1 |    |    | t2 |
| source2 |    | u0 |    |    | u1 |    |
| result  | t0 | u0 | t1 |    | u1 | t2 |
-----------------------------------------

### Use Case

Suppose you wish to perform an operation when either of two async sequences emits an element. One way to do this is two start consuming both async sequences in parallel. If we would like to perform only one operation at a time, we can use `AsyncSeq.merge` as follows:

```

/// Represents an stream emitting elements on a specified interval.
let intervalMs (periodMs:int) = asyncSeq {
  yield DateTime.UtcNow
  while true do
    do! Async.Sleep periodMs
    yield DateTime.UtcNow }

let either : AsyncSeq<DateTime> =
  AsyncSeq.merge (intervalMs 20) (intervalMs 30)

The sequence `either` emits an element every 20ms and every 30ms.

```

---

*)



(**

## Combine Latest


`AsyncSeq.combineLatest` non-deterministically merges two async sequences much like `AsyncSeq.merge`, combining their elements using the specified `combine` function. The resulting async sequence will only contain elements if both of the source sequences produce at least one element. After combining the first elements the source sequences, this operation emits elements when either source sequence emits an element, passing the newly emitted element as one of the arguments to the `combine` function, the other being the previously emitted element of that type.

### Example Execution

An example execution can be depicted visually as follows:

```

----------------------------------------
| source1 | a0 |    |    | a1 |   | a2 |
| source2 |    | b0 | b1 |    |   |    |
| result  |    | c0 | c1 | c2 |   | c3 |
----------------------------------------

where

c0 = f a0 b0
c1 = f a0 b1
c2 = f a1 b1
c3 = f a2 b1


```

### Use Case

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

let changesOrInterval : AsyncSeq<Value> =
  AsyncSeq.combineLatest (fun v _ -> v) (changes ("myKey", 0L)) (intervalMs (1000 * 60))


(**

We can now consume this async sequence and use it to trigger downstream operations, such as updating the configuration of a running program, in flight.

---

*)





(**

## Distinct Until Changed

`AsyncSeq.distinctUntilChanged` returns an async sequence which returns every element of the source sequence, skipping elements which equal its predecessor.

## Example

An example execution can be visualized as follows:

-----------------------------------
| source  | a | a | b | b | b | a |
| result  | a |   | b |   |   | a |
-----------------------------------

## Use Case

Suppose you're polling a resource which returns status information of a background job.

*)

type Status = {
  completed : int
  finished : bool
  result : string
}

/// Gets the status of a job.
let status : Async<Status> =
  failwith ""

let statuses : AsyncSeq<Status> =
  asyncSeq {
    let! s = status
    while true do
      do! Async.Sleep 1000
      let! s = status
      yield s }

(**

The async sequence `statuses` will return a status every second. It will return a status regardless of whether the status changed. Assuming the status changes monotonically, we can use `AsyncSeq.distinctUntilChanged` to transform `statuses` into an async sequence of distinct statuses:

*)

let distinctStatuses : AsyncSeq<Status> =
  statuses |> AsyncSeq.distinctUntilChanged


(**

Finally, we can create a workflow which prints the status every time a change is detected and terminates when the underlying job reaches the `finished` state:

*)

let result : Async<string> =
  distinctStatuses
  |> AsyncSeq.pick (fun st -> 
    printfn "status=%A" st
    if st.finished then Some st.result
    else None)

(**

---

*)    



  














