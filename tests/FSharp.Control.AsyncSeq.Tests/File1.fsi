namespace Pres

open FSharp.Control


type Event
type StreamId
type Offset
type Count
//type AsyncSeq<'a>

module EventStore =
  
  /// Gets a slice of up to count events from a stream, starting at the specified offset.
  val getEvents : StreamId * Offset * Count -> Async<Event[]>

  /// Writes a batch of events to a stream.
  val writeEvents : StreamId * Event[] -> Async<unit>

  /// Streams all events from a stream starting at the specified offset in batches of count.
  val streamEvents : StreamId * Offset * Count -> AsyncSeq<Event[]>





type Key
type Value
type Version

module Consul =
  
  /// Gets the value of the specified key.
  /// If a version is specified, suspends the operation until a value with
  /// a greater version is put.
  val get : Key * Version option -> Async<Value * Version>

  /// Puts a key-value pair.
  val put : Key * Value * Version option -> Async<unit>

  /// Returns a stream of changes to the specified key.
  val changes : Key * Version option -> AsyncSeq<Value * Version>





open System.Net.Sockets

module Socket =
  
  /// Reads from a socket.
  val read : Socket -> Async<byte[]>

  /// Writes to a socket.
  val write : Socket * byte[] -> Async<int>

  /// Returns a stream of byte arrays read from the socket.
  val stream : Socket -> AsyncSeq<byte[]>

  /// Frames a stream of byte arrays using a length-prefixed encoding.
  val lengthPrefixedFramer : AsyncSeq<byte[]> -> AsyncSeq<byte[]>









