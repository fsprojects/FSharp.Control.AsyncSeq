module internal FSharp.Control.Utils

module Choice =
  
  /// Maps over the left result type.
  let mapl (f:'a -> 'b) = function
    | Choice1Of2 a -> f a |> Choice1Of2
    | Choice2Of2 e -> Choice2Of2 e

  /// Maps over the right result type.
  let mapr (f:'b -> 'c) = function
    | Choice1Of2 a -> Choice1Of2 a
    | Choice2Of2 e -> f e |> Choice2Of2