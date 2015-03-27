// ----------------------------------------------------------------------------
// F# async extensions (Observable.fs)
// (c) Tomas Petricek, Phil Trelford, and Ryan Riley, 2011-2012, Available under Apache 2.0 license.
// ----------------------------------------------------------------------------
#nowarn "40"
namespace FSharp.Control

open System
open System.Collections.Generic
open System.Threading

// ----------------------------------------------------------------------------

/// Union type that represents different messages that can be sent to the
/// IObserver interface. The IObserver type is equivalent to a type that has
/// just OnNext method that gets 'ObservableUpdate' as an argument.
type internal ObservableUpdate<'T> = 
    | Next of 'T
    | Error of exn
    | Completed

module internal Observable =

    /// Turns observable into an observable that only calls OnNext method of the
    /// observer, but gives it a discriminated union that represents different
    /// kinds of events (error, next, completed)
    let asUpdates (input:IObservable<'T>) = 
      { new IObservable<_> with
          member x.Subscribe(observer) =
            input.Subscribe
              ({ new IObserver<_> with
                  member x.OnNext(v) = observer.OnNext(Next v)
                  member x.OnCompleted() = observer.OnNext(Completed) 
                  member x.OnError(e) = observer.OnNext(Error e) }) }

