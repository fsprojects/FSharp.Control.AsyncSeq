module AsyncSeqTests

open Fable.Core
open Fable.Jester
open Fable.FastCheck
open Fable.FastCheck.Jest
open FSharp.Control
open System

type AsyncOps = AsyncOps with
    static member never = async { do! Async.Sleep(-1) }
    static member timeoutMs (timeoutMs: int) (a: Async<'a>) =
        async {
            let! a = Async.StartChild(a, timeoutMs)

            return! a
        }

module AsyncSeq =
    [<GeneralizableValue>]
    let never<'a> : AsyncSeq<'a> =
        asyncSeq {
            do! AsyncOps.never
            yield invalidOp ""
        }

let [<Literal>] DEFAULT_TIMEOUT_MS = 2000

let catch (f: unit -> 'b) : Choice<'b,exn> =
    try f() |> Choice1Of2
    with ex -> ex |> Choice2Of2

let inline IsCancellationExn (e:exn) =
    match e.Message with
    | s when s.Contains("Task wrapped with Async has been cancelled.") -> true
    | s when s.Contains("The operation has timed-out") -> true
    | s when s.Contains("Task wrapped with Async has been cancelled") ||
             s.Contains("The operation has timed-out") -> true
    | _ -> false

let AreCancellationExns (e1:exn) (e2:exn) =
    IsCancellationExn e1 && IsCancellationExn e2

let runTimeout (timeoutMs:int) (a:Async<'a>) : 'a =
    Async.RunSynchronously (a, timeoutMs)

expect.extend("toEqualAsyncSeq", fun (actual: AsyncSeq<obj>) (expected: AsyncSeq<obj>) (timeout: int) (exnEq: exn -> exn -> bool) ->
    async {
        let! expected = expected |> AsyncSeq.toListAsync |> AsyncOps.timeoutMs timeout |> Async.Catch
        let! actual = actual |> AsyncSeq.toListAsync |> AsyncOps.timeoutMs timeout |> Async.Catch

        return
            match expected, actual with
            | Choice1Of2 exp, Choice1Of2 act -> exp = act
            | Choice2Of2 exp, Choice2Of2 act -> exnEq exp act
            | _ -> false
            |> fun b -> { pass = b; message = fun () -> sprintf "expected = %A actual = %A" expected actual }
    } |> Async.StartAsPromise)
    
expect.extend("toEqualLooseChoice", fun (actual: Choice<obj,exn>) (expected: Choice<obj,exn>) ->
    match expected,actual with
    | Choice1Of2 exp, Choice1Of2 act -> exp = act
    | Choice2Of2 _, Choice2Of2 _ -> true
    | _ -> false
    |> fun b -> { pass = b; message = fun () -> sprintf "expected = %A actual = %A" expected actual })

[<NoComparison>]
[<NoEquality>]
[<Global("expect")>]
type expectedDelayed<'a> =
    inherit expected<JS.Promise<unit>>

    member _.toEqualLooseChoice (expected: Choice<'a,exn>) : 'Return = jsNative

[<NoComparison>]
[<NoEquality>]
[<Global("expect")>]
type expectedAsyncSeq =
    inherit expected<JS.Promise<unit>>

    member _.not : expectedAsyncSeq = jsNative

    member _.toEqualAsyncSeq (expected: AsyncSeq<'a>, timeout: int, exnEq: (exn -> exn -> bool)) : JS.Promise<unit> = jsNative

    member inline this.toEqualAsyncSeq (expected: AsyncSeq<'a>) = this.toEqualAsyncSeq(expected, DEFAULT_TIMEOUT_MS, (fun _ _ -> true))

    member inline this.toEqualAsyncSeq (expected: AsyncSeq<'a>, exnEq: (exn -> exn -> bool)) = this.toEqualAsyncSeq(expected, DEFAULT_TIMEOUT_MS, exnEq)

    member inline this.toEqualAsyncSeq (expected: AsyncSeq<'a>, timeout: int) = this.toEqualAsyncSeq(expected, timeout, (fun _ _ -> true))

type Jest with
    [<Emit("expect($0)")>]
    static member expect (value: AsyncSeq<'a>) : expectedAsyncSeq = jsNative

    [<Emit("expect($0)")>]
    static member expect (value: Choice<'a,exn>) : expectedDelayed<'a> = jsNative

Jest.test("AsyncSeq.never should equal itself", async {
    let! n1 = AsyncSeq.never<int> |> AsyncSeq.toListAsync |> Async.Catch
    let! n2 = AsyncSeq.never<int> |> AsyncSeq.toListAsync |> Async.Catch
    
    Jest.expect(n1).toEqualLooseChoice(n2)
})

Jest.describe("AsyncSeq.concat", fun () -> 
    Jest.test("AsyncSeq.concatSeq works", async {
        let ls = [ [1;2] ; [3;4] ]
        let actual = AsyncSeq.ofSeq ls |> AsyncSeq.concatSeq
        let expected = ls |> List.concat |> AsyncSeq.ofSeq

        do! Jest.expect(actual).toEqualAsyncSeq(expected)
    })

    Jest.test("AsynSeqc.concat works", async {
        for n in 0..10 do
            for m in 0..10 do
                let! actual =
                    Seq.init m (fun _ -> Seq.init n (id >> async.Return) |> AsyncSeq.ofSeqAsync)
                    |> AsyncSeq.ofSeq
                    |> AsyncSeq.concat
                    |> AsyncSeq.toArrayAsync
        
                let! expected =
                    Seq.init m (fun _ -> Seq.init n id)
                    |> AsyncSeq.ofSeq
                    |> AsyncSeq.concatSeq
                    |> AsyncSeq.toArrayAsync

                Jest.expect(actual).toEqual(expected)
    })
)

Jest.test("AsyncSeq.sum works", async {
    for i in 0 .. 10 do
        let ls = [ 1 .. i ]
        let actual = AsyncSeq.ofSeq ls |> AsyncSeq.sum
        let expected = ls |> List.sum

        do! Jest.expect(actual).toEqual(expected)
})

Jest.test("AsyncSeq.length works", async {
    for i in 0 .. 10 do
        let ls = [ 1 .. i ]
        let! actual64 = AsyncSeq.ofSeq ls |> AsyncSeq.length
        let expected = ls |> List.length

        Jest.expect(actual64 |> int).toEqual(expected)
})
    
Jest.test("AsyncSeq.contains works", async {
    for i in 0 .. 10 do
        let ls = [ 1 .. i ]
        for j in [0;i;i+1] do
            let actual = AsyncSeq.ofSeq ls |> AsyncSeq.contains j
            let expected = ls |> List.exists (fun x -> x = j)

            do! Jest.expect(actual).toEqual(expected)
})

Jest.describe("AsyncSeq.pick", fun () ->
    Jest.test("AsyncSeq.pick works", async {
        for i in 0 .. 10 do
            let ls = [ 1 .. i ]
            for j in [0;i;i+1] do
                let chooser x = if x = j then Some (string (x+1)) else None
                let! actual = AsyncSeq.ofSeq ls |> AsyncSeq.pick chooser |> Async.Catch
                let expected = (fun () -> ls |> Seq.pick chooser) |> catch
    
                do Jest.expect(actual).toEqualLooseChoice(expected)
    })

    Jest.test("AsyncSeq.tryPick works", async {
        for i in 0 .. 10 do
            let ls = [ 1 .. i ]
            for j in [0;i;i+1] do
                let actual = AsyncSeq.ofSeq ls |> AsyncSeq.tryPick (fun x -> if x = j then Some (string (x+1)) else None)
                let expected = ls |> Seq.tryPick (fun x -> if x = j then Some (string (x+1)) else None)
    
                do! Jest.expect(actual).toEqual(expected)
    })
)

Jest.test("AsyncSeq.tryFind works", async {
    for i in 0 .. 10 do
        let ls = [ 1 .. i ]
        for j in [0;i;i+1] do
            let actual = AsyncSeq.ofSeq ls |> AsyncSeq.tryFind (fun x -> x = j)
            let expected = ls |> Seq.tryFind (fun x -> x = j)

            do! Jest.expect(actual).toEqual(expected)
})

Jest.test("AsyncSeq.exists works", async {
    for i in 0 .. 10 do
        let ls = [ 1 .. i ]
        for j in [0;i;i+1] do
            let actual = AsyncSeq.ofSeq ls |> AsyncSeq.exists (fun x -> x = j)
            let expected = ls |> Seq.exists (fun x -> x = j)

            do! Jest.expect(actual).toEqual(expected)
})

Jest.test("AsyncSeq.forall works", async {
    for i in 0 .. 10 do
        let ls = [ 1 .. i ]
        for j in [0;i;i+1] do
            let actual = AsyncSeq.ofSeq ls |> AsyncSeq.forall (fun x -> x = j)
            let expected = ls |> Seq.forall (fun x -> x = j)

            do! Jest.expect(actual).toEqual(expected)
})

Jest.describe("AsyncSeq.unfold", fun () -> 
    Jest.test("AsyncSeq.unfoldAsync", async {
        let gen s =
            if s < 3 then (s,s + 1) |> Some
            else None
        let actual = AsyncSeq.unfoldAsync (gen >> async.Return) 0
        let expected = Seq.unfold gen 0 |> AsyncSeq.ofSeq

        do! Jest.expect(actual).toEqualAsyncSeq(expected)
    })
    
    Jest.test("AsyncSeq.unfold", async {
        let gen s =
            if s < 3 then (s,s + 1) |> Some
            else None
        let actual = AsyncSeq.unfold gen 0
        let expected = Seq.unfold gen 0 |> AsyncSeq.ofSeq

        do! Jest.expect(actual).toEqualAsyncSeq(expected)
    })

    Jest.test("AsyncSeq.unfold choose", async {
        let n = 3
        let chooser x = if x % 2 = 0 then Some x else None
        let gen s =
            if s < n then (s,s + 1) |> Some
            else None
        let actual = AsyncSeq.unfold gen 0 |> AsyncSeq.choose chooser
        let expected = Seq.unfold gen 0 |> Seq.choose chooser |> AsyncSeq.ofSeq

        do! Jest.expect(actual).toEqualAsyncSeq(expected)
    })

    Jest.test("AsyncSeq.unfoldAsync should be iterable in finite resources", async {
        let generator state =
            async {
                if state < 10000 then
                    return Some ((), state + 1)
                else
                    return None
            }

        let actual = AsyncSeq.unfoldAsync generator 0 |> AsyncSeq.iter ignore

        do! Jest.expect(actual).not.toThrow()
    })
)

Jest.describe("AsyncSeq.interleaveChoice", fun () -> 
    Jest.test("AsyncSeq.interleaveChoice", async {
        let s1 = AsyncSeq.ofSeq ["a";"b";"c"]
        let s2 = AsyncSeq.ofSeq [1;2;3]
        let! actual = AsyncSeq.interleaveChoice s1 s2 |> AsyncSeq.toListAsync
        let expected = [Choice1Of2 "a" ; Choice2Of2 1 ; Choice1Of2 "b" ; Choice2Of2 2 ; Choice1Of2 "c" ; Choice2Of2 3]

        Jest.expect(actual).toEqual(expect.arrayContaining expected)
        Jest.expect(actual).toHaveLength(expected.Length)
    })

    Jest.test("AsyncSeq.interleaveChoice second smaller", async {
        let s1 = AsyncSeq.ofSeq ["a";"b";"c"]
        let s2 = AsyncSeq.ofSeq [1]
        let! actual = AsyncSeq.interleaveChoice s1 s2 |> AsyncSeq.toListAsync
        let expected = [Choice1Of2 "a" ; Choice2Of2 1 ; Choice1Of2 "b" ; Choice1Of2 "c"]

        Jest.expect(actual).toEqual(expect.arrayContaining expected)
        Jest.expect(actual).toHaveLength(expected.Length)
    })

    Jest.test("AsyncSeq.interleaveChoice second empty", async {
        let s1 = AsyncSeq.ofSeq ["a";"b";"c"]
        let s2 = AsyncSeq.ofSeq []
        let! actual = AsyncSeq.interleaveChoice s1 s2 |> AsyncSeq.toListAsync
        let expected = [Choice1Of2 "a" ; Choice1Of2 "b" ; Choice1Of2 "c"]

        Jest.expect(actual).toEqual(expect.arrayContaining expected)
        Jest.expect(actual).toHaveLength(expected.Length)
    })

    Jest.test("AsyncSeq.interleaveChoice both empty", async {
        let s1 = AsyncSeq.ofSeq<int> []
        let s2 = AsyncSeq.ofSeq<int> []
        let! actual = AsyncSeq.interleaveChoice s1 s2 |> AsyncSeq.toListAsync
        let expected = []

        Jest.expect(actual).toEqual(expect.arrayContaining expected)
        Jest.expect(actual).toHaveLength(expected.Length)
    })

    Jest.test("AsyncSeq.interleaveChoice first smaller", async {
        let s1 = AsyncSeq.ofSeq ["a"]
        let s2 = AsyncSeq.ofSeq [1;2;3]
        let! actual = AsyncSeq.interleaveChoice s1 s2 |> AsyncSeq.toListAsync
        let expected = [Choice1Of2 "a" ; Choice2Of2 1 ; Choice2Of2 2 ; Choice2Of2 3]

        Jest.expect(actual).toEqual(expect.arrayContaining expected)
        Jest.expect(actual).toHaveLength(expected.Length)
    })

    Jest.test("AsyncSeq.interleaveChoice first empty", async {
        let s1 = AsyncSeq.ofSeq []
        let s2 = AsyncSeq.ofSeq [1;2;3]
        let! actual = AsyncSeq.interleaveChoice s1 s2 |> AsyncSeq.toListAsync
        let expected = [Choice2Of2 1 ; Choice2Of2 2 ; Choice2Of2 3]

        Jest.expect(actual).toEqual(expect.arrayContaining expected)
        Jest.expect(actual).toHaveLength(expected.Length)
    })
)

Jest.describe("AsyncSeq.interleave", fun () -> 
    Jest.test("AsyncSeq.interleave", async {
        let s1 = AsyncSeq.ofSeq ["a";"b";"c"]
        let s2 = AsyncSeq.ofSeq ["1";"2";"3"]
        let! actual = AsyncSeq.interleave s1 s2 |> AsyncSeq.toListAsync
        let expected = ["a" ; "1" ; "b" ; "2" ; "c" ; "3"]

        Jest.expect(actual).toEqual(expect.arrayContaining expected)
        Jest.expect(actual).toHaveLength(expected.Length)
    })

    Jest.test("AsyncSeq.interleave second smaller", async {
        let s1 = AsyncSeq.ofSeq ["a";"b";"c"]
        let s2 = AsyncSeq.ofSeq ["1"]
        let! actual = AsyncSeq.interleave s1 s2 |> AsyncSeq.toListAsync
        let expected = ["a" ; "1" ; "b" ; "c"]

        Jest.expect(actual).toEqual(expect.arrayContaining expected)
        Jest.expect(actual).toHaveLength(expected.Length)
    })

    Jest.test("AsyncSeq.interleave second empty", async {
        let s1 = AsyncSeq.ofSeq ["a";"b";"c"]
        let s2 = AsyncSeq.ofSeq []
        let! actual = AsyncSeq.interleave s1 s2 |> AsyncSeq.toListAsync
        let expected = ["a" ; "b" ; "c"]

        Jest.expect(actual).toEqual(expect.arrayContaining expected)
        Jest.expect(actual).toHaveLength(expected.Length)
    })

    Jest.test("AsyncSeq.interleave both empty", async {
        let s1 = AsyncSeq.ofSeq<int> []
        let s2 = AsyncSeq.ofSeq<int> []
        let! actual = AsyncSeq.interleave s1 s2 |> AsyncSeq.toListAsync
        let expected = []

        Jest.expect(actual).toEqual(expect.arrayContaining expected)
        Jest.expect(actual).toHaveLength(expected.Length)
    })

    Jest.test("AsyncSeq.interleave first smaller", async {
        let s1 = AsyncSeq.ofSeq ["a"]
        let s2 = AsyncSeq.ofSeq ["1";"2";"3"]
        let! actual = AsyncSeq.interleave s1 s2 |> AsyncSeq.toListAsync
        let expected = ["a" ; "1" ; "2" ; "3"]

        Jest.expect(actual).toEqual(expect.arrayContaining expected)
        Jest.expect(actual).toHaveLength(expected.Length)
    })

    Jest.test("AsyncSeq.interleave first empty", async {
        let s1 = AsyncSeq.ofSeq []
        let s2 = AsyncSeq.ofSeq [1;2;3]
        let! actual = AsyncSeq.interleave s1 s2 |> AsyncSeq.toListAsync
        let expected = [1 ; 2 ; 3]

        Jest.expect(actual).toEqual(expect.arrayContaining expected)
        Jest.expect(actual).toHaveLength(expected.Length)
    })

    Jest.test("AsyncSeq.interleave should fail with Exception if a task fails", async {
        for i in 0 .. 1 do
            let f =
                async {
                    let s1 =
                        asyncSeq {
                            yield 1
                            if i % 2 = 0 then failwith "fail"
                            yield 2
                        }

                    let s2 =
                        asyncSeq {
                            yield 1
                            if i % 2 = 1 then failwith "fail"
                            yield 2
                        }

                    return! AsyncSeq.interleave s1 s2 |> AsyncSeq.toArrayAsync
                }
            
            do! Jest.expect(f |> Async.StartAsPromise).rejects.toThrow()
    })
)

Jest.describe("AsyncSeq.bufferBy", fun () -> 
    Jest.test("AsyncSeq.bufferByCount", async {
        let! actual =
            asyncSeq {
                yield 1
                yield 2
                yield 3
                yield 4
                yield 5
            }
            |> AsyncSeq.bufferByCount 2 |> AsyncSeq.toArrayAsync
        let expected = [|[|1;2|];[|3;4|];[|5|]|]

        Jest.expect(actual).toEqual(expected)
        Jest.expect(actual).toHaveLength(expected.Length)
    })

    Jest.test("AsyncSeq.bufferByCount various sizes", async {
        for sz in 0 .. 10 do
            let! actual =
                asyncSeq {
                    for i in 1 .. sz do
                       yield i
                }
                |> AsyncSeq.bufferByCount 1 |> AsyncSeq.toArrayAsync
            let expected = [|for i in 1 .. sz -> [|i|]|]
            
            Jest.expect(actual).toEqual(expected)
            Jest.expect(actual).toHaveLength(expected.Length)
    })

    Jest.test("AsyncSeq.bufferByCount empty", async {
        let! actual =
            AsyncSeq.empty<int>
            |> AsyncSeq.bufferByCount 2
            |> AsyncSeq.toArrayAsync
        let expected = [||]

        Jest.expect(actual).toEqual(expected)
        Jest.expect(actual).toHaveLength(expected.Length)
    })
)

Jest.describe("AsyncSeq.try", fun () ->
    Jest.test("try finally works no exception", async {
        let x = ref 0
        let s =
            asyncSeq {
                try yield 1
                finally x := x.Value + 3
            }

        Jest.expect(x.Value).toBe(0)

        let! _ = s |> AsyncSeq.toListAsync

        Jest.expect(x.Value).toBe(3)

        let! _ = s |> AsyncSeq.toListAsync

        Jest.expect(x.Value).toBe(6)
    })
    
    Jest.test("try finally works exception", async {
        let x = ref 0
        let s =
            asyncSeq {
                try
                    try
                        yield 1
                        failwith "fffail"
                    finally x := x.Value + 1
                finally x := x.Value + 2
            }

        Jest.expect(x.Value).toBe(0)

        do! Jest.expect(s |> AsyncSeq.toListAsync |> Async.StartAsPromise).rejects.toThrow()

        Jest.expect(x.Value).toBe(3)

        do! Jest.expect(s |> AsyncSeq.toListAsync |> Async.StartAsPromise).rejects.toThrow()

        Jest.expect(x.Value).toBe(6)
    })

    Jest.test("try with works exception", async {
        let x = ref 0
        let s =
            asyncSeq {
                try failwith "ffail"
                with _ -> x := x.Value + 3
            }

        Jest.expect(x.Value).toBe(0)

        do! Jest.expect(s |> AsyncSeq.toArrayAsync).toEqual([||])

        Jest.expect(x.Value).toBe(3)

        do! Jest.expect(s |> AsyncSeq.toArrayAsync).toEqual([||])

        Jest.expect(x.Value).toBe(6)
    })

    Jest.test("try with works no exception", async {
        let x = ref 0
        let s =
            asyncSeq {
                try yield 1
                with _ -> x := x.Value + 3
            }

        Jest.expect(x.Value).toBe(0)

        do! Jest.expect(s |> AsyncSeq.toArrayAsync).toEqual([|1|])

        Jest.expect(x.Value).toBe(0)

        do! Jest.expect(s |> AsyncSeq.toArrayAsync).toEqual([|1|])

        Jest.expect(x.Value).toBe(0)
    })
)

Jest.describe("AsyncSeq.zip", fun () ->
    Jest.test("AsyncSeq.zip", async {
        for la in [ []; [1]; [1;2;3;4;5] ] do
            for lb in [ []; [1]; [1;2;3;4;5] ] do
                 let a = la |> AsyncSeq.ofSeq
                 let b = lb |> AsyncSeq.ofSeq
                 let! actual = AsyncSeq.zip a b |> AsyncSeq.toArrayAsync
                 let! expected = Seq.zip la lb |> AsyncSeq.ofSeq |> AsyncSeq.toArrayAsync

                 Jest.expect(actual).toEqual(expected)
    })

    Jest.test("AsyncSeq.zipWithAsync", async {
        for la in [ []; [1]; [1;2;3;4;5] ] do
            for lb in [ []; [1]; [1;2;3;4;5] ] do
                 let a = la |> AsyncSeq.ofSeq
                 let b = lb |> AsyncSeq.ofSeq
                 let! actual = AsyncSeq.zipWithAsync (fun a b -> a + b |> async.Return) a b |> AsyncSeq.toArrayAsync
                 let! expected = Seq.zip la lb |> Seq.map ((<||) (+)) |> AsyncSeq.ofSeq |> AsyncSeq.toArrayAsync

                 Jest.expect(actual).toEqual(expected)
    })
)

Jest.test("AsyncSeq.append works", async {
    for la in [ []; [1]; [1;2;3;4;5] ] do
        for lb in [ []; [1]; [1;2;3;4;5] ] do
             let a = la |> AsyncSeq.ofSeq
             let b = lb |> AsyncSeq.ofSeq
             let! actual = AsyncSeq.append a b |> AsyncSeq.toArrayAsync
             let! expected = List.append la lb |> AsyncSeq.ofSeq |> AsyncSeq.toArrayAsync

             Jest.expect(actual).toEqual(expected)
})

let takeSkipXofY =
    arbitrary {
        let! listLen = Arbitrary.ConstrainedDefaults.integer(5, 20)

        let arbList =
            Arbitrary.ConstrainedDefaults.integer(1, 20)
            |> Arbitrary.List.ofLength listLen

        let takeSkipCount = Arbitrary.ConstrainedDefaults.integer(0, listLen)

        return! Arbitrary.zip arbList takeSkipCount
    }

Jest.describe("AsyncSeq.skip", fun () ->
    Jest.test("AsyncSeq.skipWhileAsync", async {
        for ls in [ []; [1]; [3]; [1;2;3;4;5] ] do
            let p i = i <= 2
            let! actual = ls |> AsyncSeq.ofSeq |> AsyncSeq.skipWhileAsync (p >> async.Return) |> AsyncSeq.toArrayAsync
            let! expected = ls |> Seq.skipWhile p |> AsyncSeq.ofSeq |> AsyncSeq.toArrayAsync
    
            Jest.expect(actual).toEqual(expected)
    })

    Jest.test.prop("AsyncSeq.skip x of y", takeSkipXofY, fun (ls, c) -> async {
        let! actual = ls |> AsyncSeq.ofSeq |> AsyncSeq.skip c |> AsyncSeq.toArrayAsync
        let! expected = ls |> Seq.skip c |> AsyncSeq.ofSeq |> AsyncSeq.toArrayAsync

        Jest.expect(actual).toEqual(expected)
    })
)

Jest.describe("AsyncSeq.take", fun () ->
    Jest.test("AsyncSeq.takeWhileAsync", async {
        for ls in [ []; [1]; [1;2;3;4;5] ] do
            let p i = i < 4
            let! actual = ls |> AsyncSeq.ofSeq |> AsyncSeq.takeWhileAsync (p >> async.Return) |> AsyncSeq.toArrayAsync
            let! expected = ls |> Seq.takeWhile p |> AsyncSeq.ofSeq |> AsyncSeq.toArrayAsync
    
            Jest.expect(actual).toEqual(expected)
    })

    Jest.test("AsyncSeq.take should work", async {
        let! sa = asyncSeq { yield ["a",1] |> Map.ofList } |> AsyncSeq.take 1 |> AsyncSeq.toArrayAsync
        let actual = sa |> (Array.tryHead >> Option.bind (Map.tryFind("a")))
        
        Jest.expect(actual).toBeDefined()
        Jest.expect(actual.Value).toBe(1)
    })

    Jest.test("AsyncSeq.takeWhileInclusive", async {
        for ls in [ []; [1]; [1;2;3;4;5] ] do
            let p i = i < 4
            let pInclusive i = i <= 4
            let! actual = ls |> AsyncSeq.ofSeq |> AsyncSeq.takeWhileInclusive p |> AsyncSeq.toArrayAsync
            let! expected = ls |> Seq.filter(pInclusive) |> AsyncSeq.ofSeq |> AsyncSeq.toArrayAsync
    
            Jest.expect(actual).toEqual(expected)
    })

    Jest.test.prop("AsyncSeq.take x of y", takeSkipXofY, fun (ls, c) -> async {
        let! actual = ls |> AsyncSeq.ofSeq |> AsyncSeq.take c |> AsyncSeq.toArrayAsync
        let! expected = ls |> Seq.take c |> AsyncSeq.ofSeq |> AsyncSeq.toArrayAsync

        Jest.expect(actual).toEqual(expected)
    })
)

Jest.test("AsyncSeq.threadStateAsync", async {
    let ls = [1;2;3;4;5]
    let! actual = ls |> AsyncSeq.ofSeq |> AsyncSeq.threadStateAsync (fun i a -> async.Return(i + a, i + 1)) 0 |> AsyncSeq.toArrayAsync
    let! expected = [1;3;5;7;9] |> AsyncSeq.ofSeq |> AsyncSeq.toArrayAsync

    Jest.expect(actual).toEqual(expected)
})


Jest.describe("AsyncSeq.scan", fun () ->
    Jest.test("AsyncSeq.scanAsync", async {
        for ls in [ []; [1]; [3]; [1;2;3;4;5] ] do
            let f i a = i + a
            let z = 0
            let! actual = ls |> AsyncSeq.ofSeq |> AsyncSeq.scanAsync (fun i a -> f i a |> async.Return) z |> AsyncSeq.toArrayAsync
            let! expected = ls |> List.scan f z |> AsyncSeq.ofSeq |> AsyncSeq.toArrayAsync
    
            Jest.expect(actual).toEqual(expected)
    })

    Jest.test("AsyncSeq.scan", async {
        for ls in [ []; [1]; [3]; [1;2;3;4;5] ] do
            let f i a = i + a
            let z = 0
            let! actual = ls |> AsyncSeq.ofSeq |> AsyncSeq.scan (fun i a -> f i a) z |> AsyncSeq.toArrayAsync
            let! expected = ls |> List.scan f z |> AsyncSeq.ofSeq |> AsyncSeq.toArrayAsync
    
            Jest.expect(actual).toEqual(expected)
    })
)

Jest.describe("AsyncSeq.fold", fun () -> 
    Jest.test("AsyncSeq.foldAsync", async {
        for ls in [ []; [1]; [3]; [1;2;3;4;5] ] do
            let f i a = i + a
            let z = 0
            let actual = ls |> AsyncSeq.ofSeq |> AsyncSeq.foldAsync (fun i a -> f i a |> async.Return) z
            let expected = ls |> Seq.fold f z

            do! Jest.expect(actual).toBe(expected)
    })
    
    Jest.test("AsyncSeq.fold", async {
        for ls in [ []; [1]; [3]; [1;2;3;4;5] ] do
            let f i a = i + a
            let z = 0
            let actual = ls |> AsyncSeq.ofSeq |> AsyncSeq.fold (fun i a -> f i a) z
            let expected = ls |> Seq.fold f z

            do! Jest.expect(actual).toBe(expected)
    })
)

Jest.describe("AsyncSeq.filter", fun () -> 
    Jest.test("AsyncSeq.filterAsync", async {
        for ls in [ []; [1]; [4]; [1;2;3;4;5] ] do
            let p i = i > 3
            let! actual = ls |> AsyncSeq.ofSeq |> AsyncSeq.filterAsync (p >> async.Return) |> AsyncSeq.toArrayAsync
            let! expected = ls |> Seq.filter p |> AsyncSeq.ofSeq |> AsyncSeq.toArrayAsync

            Jest.expect(actual).toEqual(expected)
    })
    
    Jest.test("AsyncSeq.filter", async {
        for ls in [ []; [1]; [4]; [1;2;3;4;5] ] do
            let p i = i > 3
            let! actual = ls |> AsyncSeq.ofSeq |> AsyncSeq.filter p |> AsyncSeq.toArrayAsync
            let! expected = ls |> Seq.filter p |> AsyncSeq.ofSeq |> AsyncSeq.toArrayAsync

            Jest.expect(actual).toEqual(expected)
    })
)

Jest.describe("AsyncSeq.replicate", fun () -> 
    Jest.test("AsyncSeq.replicate", async {
        let c = 10
        let x = "hello"
        let! actual = AsyncSeq.replicate 100 x |> AsyncSeq.take c |> AsyncSeq.toArrayAsync
        let! expected = List.replicate c x |> AsyncSeq.ofSeq |> AsyncSeq.toArrayAsync

        Jest.expect(actual).toEqual(expected)
    })

    Jest.test("AsyncSeq.replicateInfinite", async {
        let c = 10
        let x = "hello"
        let! actual = AsyncSeq.replicateInfinite x |> AsyncSeq.take c |> AsyncSeq.toArrayAsync
        let! expected = List.replicate c x |> AsyncSeq.ofSeq |> AsyncSeq.toArrayAsync

        Jest.expect(actual).toEqual(expected)
    })

    Jest.test("AsyncSeq.replicateInfiniteAsync", async {
        let c = 10
        let x = "hello"
        let! actual = async { return x } |> AsyncSeq.replicateInfiniteAsync |> AsyncSeq.take c |> AsyncSeq.toArrayAsync
        let! expected = List.replicate c x |> AsyncSeq.ofSeq |> AsyncSeq.toArrayAsync

        Jest.expect(actual).toEqual(expected)
    })

    Jest.test.prop("AsyncSeq.replicateUntilNoneAsync", Arbitrary.Defaults.boolean, fun empty -> async {
        let c = 10
        let rng = System.Random()
        let! actual =
            async {
                if empty then return None
                else
                    if rng.Next(0,10) > 7 then return None
                    else return Some "hello"
            }
            |> AsyncSeq.replicateUntilNoneAsync |> AsyncSeq.take c |> AsyncSeq.toArrayAsync

        if empty then
            Jest.expect(actual).toHaveLength(0)
            Jest.expect(actual).toEqual([||])
        elif actual.Length > 0 then
            Jest.expect(actual).toContain("hello")
        else Jest.expect(actual).toEqual([||])
    })
)

Jest.describe("AsyncSeq.init", fun () -> 
    Jest.test("AsyncSeq.init", async {
        for c in [0; 1; 100] do
            let! actual = AsyncSeq.init (int64 c) string |> AsyncSeq.toArrayAsync
            let! expected = List.init c string |> AsyncSeq.ofSeq |> AsyncSeq.toArrayAsync

            Jest.expect(actual).toEqual(expected)
    })
    
    Jest.test("AsyncSeq.initInfinite", async {
        for c in [0; 1; 100] do
            let! actual = AsyncSeq.initInfinite string |> AsyncSeq.take c |> AsyncSeq.toArrayAsync
            let! expected = List.init c string |> AsyncSeq.ofSeq |> AsyncSeq.toArrayAsync

            Jest.expect(actual).toEqual(expected)
    })

    Jest.test("AsyncSeq.initInfinite scales", async {
        let actual = AsyncSeq.initInfinite string |> AsyncSeq.take 1000 |> AsyncSeq.iter ignore

        do! Jest.expect(actual).not.toThrow()
    })

    Jest.test("AsyncSeq.initAsync", async {
        for c in [0; 1; 100] do
            let! actual = AsyncSeq.initAsync (int64 c) (string >> async.Return) |> AsyncSeq.toArrayAsync
            let! expected = List.init c string |> AsyncSeq.ofSeq |> AsyncSeq.toArrayAsync

            Jest.expect(actual).toEqual(expected)
    })
    
    Jest.test("AsyncSeq.initInfiniteAsync", async {
        for c in [0; 1; 100] do
            let! actual = AsyncSeq.initInfiniteAsync (string >> async.Return) |> AsyncSeq.take c |> AsyncSeq.toArrayAsync
            let! expected = List.init c string |> AsyncSeq.ofSeq |> AsyncSeq.toArrayAsync

            Jest.expect(actual).toEqual(expected)
    })
)

Jest.test("AsyncSeq.collect works", async {
    for c in [0; 1; 10] do
        let! actual = AsyncSeq.collect (fun i -> AsyncSeq.ofSeq [ 0 .. i]) (AsyncSeq.ofSeq [ 0 .. c ]) |> AsyncSeq.toArrayAsync
        let! expected = [ for i in 0 .. c do yield! [ 0 .. i ] ] |> AsyncSeq.ofSeq |> AsyncSeq.toArrayAsync

        Jest.expect(actual).toEqual(expected)
})

Jest.describe("AsyncSeq.traverse", fun () -> 
    Jest.test("AsyncSeq.traverseOptionAsync", async {
        let seen = ResizeArray<_>()
        let s = [1;2;3;4;5] |> AsyncSeq.ofSeq
        let f i =
            seen.Add i
            if i < 2 then Some i |> async.Return
            else None |> async.Return
        let! r = AsyncSeq.traverseOptionAsync f s

        Jest.expect(r).toBeUndefined()
        Jest.expect(ResizeArray [1;2]).toEqual(seen)
    })

    Jest.test("AsyncSeq.traverseChoiceAsync", async {
        let seen = ResizeArray<_>()
        let s = [1;2;3;4;5] |> AsyncSeq.ofSeq
        let f i =
            seen.Add i
            if i < 2 then Choice1Of2 i |> async.Return
            else Choice2Of2 "oh no" |> async.Return
        let! r = AsyncSeq.traverseChoiceAsync f s

        Jest.expect(r |> function | Choice1Of2 _ -> false | _ -> true).toBe(true)
        Jest.expect(ResizeArray [1;2]).toEqual(seen)
    })
)

Jest.test("AsyncSeq.distinctUntilChangedWithAsync", async {
    let ls = [1;1;2;2;3;4;5;1]
    let s = ls |> AsyncSeq.ofSeq
    let c a b =
        if a = b then true |> async.Return
        else false |> async.Return
    let! actual = s |> AsyncSeq.distinctUntilChangedWithAsync c |> AsyncSeq.toArrayAsync
    let! expected = [1;2;3;4;5;1] |> AsyncSeq.ofSeq |> AsyncSeq.toArrayAsync

    Jest.expect(actual).toEqual(expected)
})

Jest.test("AsyncSeq.singleton works", async {
    let! actual = AsyncSeq.singleton 1 |> AsyncSeq.toArrayAsync
    let! expected = [1] |> AsyncSeq.ofSeq |> AsyncSeq.toArrayAsync

    Jest.expect(actual).toEqual(expected)
})

Jest.test("AsyncSeq.while should allow do at end", async {
    let x = ref 0
    let! actual =
        asyncSeq {
            while false do
                yield 1
            do! async { x := x.Value + 3 }
        }
        |> AsyncSeq.toArrayAsync

    Jest.expect(actual).toEqual([||])
    Jest.expect(x.Value).toBe(3)
})

Jest.test("AsyncSeq.getIterator should work", async {
    let s1 = [1..2] |> AsyncSeq.ofSeq
    use i = s1.GetEnumerator()

    match! i.MoveNext() with
    | None as v -> Jest.expect(v).toBeDefined() 
    | Some v ->
        Jest.expect(v).toBe(1)

        match! i.MoveNext() with
        | None as v -> Jest.expect(v).toBeDefined() 
        | Some v ->
            Jest.expect(v).toBe(2)
            do! Jest.expect(i.MoveNext()).toBeUndefined()
})

Jest.test("asyncSeq.For should delay", async {
    let (s: seq<int>) =
        { new System.Collections.Generic.IEnumerable<int> with
              member x.GetEnumerator() = failwith "fail"
          interface System.Collections.IEnumerable with
              member x.GetEnumerator() = failwith "fail"  }

    let actual () = asyncSeq.For(s, fun _ -> AsyncSeq.empty) |> ignore

    Jest.expect(actual).not.toThrow()
})

Jest.test("AsyncSeq.mapi should work", async {
    for i in 0..100 do
        let ls = Array.init i (fun x -> x + 100)
        let expected =
            ls
            |> Array.mapi (fun i x -> sprintf "%i_%i" i x)
        let! actual =
            ls
            |> AsyncSeq.ofSeq
            |> AsyncSeq.mapi (fun i x -> sprintf "%i_%i" i x)
            |> AsyncSeq.toArrayAsync

        Jest.expect(actual).toEqual(expected)
})

Jest.test("AsyncSeq.truncate should work like take", async {
    let s = asyncSeq { yield ["a",1] |> Map.ofList }
    let! expected = s |> AsyncSeq.take 1 |> AsyncSeq.toArrayAsync
    let! actual =  s |> AsyncSeq.truncate 1 |> AsyncSeq.toArrayAsync

    Jest.expect(actual).toEqual(expected)
})

Jest.describe("AsyncSeq.intervalMs", fun () ->
    fun () ->
        Jest.useFakeTimers()
        Jest.setSystemTime(0)
    |> Jest.beforeAll

    Jest.test("AsyncSeq.intervalMs works", async {
        let interval = AsyncSeq.intervalMs 10

        let actual = ref [||]

        async {
            while actual.Value.Length < 10 do
                do! Async.Sleep 10
                let! timestamp = interval |> AsyncSeq.take 1 |> AsyncSeq.toArrayAsync
                actual := (timestamp |> Array.map (fun d -> d.Ticks)) |> Array.append actual.Value
        }
        |> Async.StartImmediate
        
        for i in [0 .. 10] do
            Jest.expect(actual.Value).toHaveLength(i)

            let expected = Array.init i (fun i -> (int64 i) * 100000L + 621355968000100000L)

            Jest.expect(actual.Value).toEqual(expected)

            Jest.advanceTimersToNextTimer()
    })
)

let intArr =
    arbitrary {
        let content = Arbitrary.ConstrainedDefaults.integer(1, 10)
        let! len = content
        return! Arbitrary.Array.ofLength len content
    }

Jest.describe("AsyncSeq.map", fun () ->
    Jest.test.prop("AsyncSeq.mapAsync works", intArr, fun xs -> async {
        let! actual = xs |> AsyncSeq.ofSeq |> AsyncSeq.mapAsync (fun i -> async { return i * 2}) |> AsyncSeq.toArrayAsync
        let expected = xs |> Array.map (fun i -> i * 2)

        Jest.expect(actual).toEqual(expected)
    })

    Jest.test.prop("AsyncSeq.map works", intArr, fun xs -> async {
        let! actual = xs |> AsyncSeq.ofSeq |> AsyncSeq.map (fun i -> i * 2) |> AsyncSeq.toArrayAsync
        let expected = xs |> Array.map (fun i -> i * 2)

        Jest.expect(actual).toEqual(expected)
    })
)

let observe vs err =
    let discarded = ref false
    { new IObservable<'U> with
            member x.Subscribe(observer) =
                for v in vs do
                   observer.OnNext v
                if err then
                   observer.OnError (Failure "fail")
                observer.OnCompleted()
                { new IDisposable with member __.Dispose() = discarded := true }  },
    (fun _ -> discarded.Value)

Jest.describe("AsyncSeq.ofObservableBuffered", fun () ->
    Jest.test("AsyncSeq.ofObservableBuffered should work (empty)", async {
        let src, discarded = observe [] false
        let! actual = src |> AsyncSeq.ofObservableBuffered |> AsyncSeq.toArrayAsync

        Jest.expect(actual).toEqual([||])
        Jest.expect(discarded()).toBe(true)
    })

    Jest.test("AsyncSeq.ofObservableBuffered should work (singleton)", async {
        let src, discarded = observe [1] false
        let! actual = src |> AsyncSeq.ofObservableBuffered |> AsyncSeq.toArrayAsync

        Jest.expect(actual).toEqual([|1|])
        Jest.expect(discarded()).toBe(true)
    })

    Jest.test("AsyncSeq.ofObservableBuffered should work (ten)", async {
        let src, discarded = observe [1..10] false
        let! actual = src |> AsyncSeq.ofObservableBuffered |> AsyncSeq.toArrayAsync

        Jest.expect(actual).toEqual([|1..10|])
        Jest.expect(discarded()).toBe(true)
    })

    Jest.test("AsyncSeq.ofObservableBuffered should work (empty, fail)", async {
        let src, discarded = observe [] true
        let actual = src |> AsyncSeq.ofObservableBuffered |> AsyncSeq.toArrayAsync

        do! Jest.expect(actual |> Async.StartAsPromise).rejects.toThrow()
        Jest.expect(discarded()).toBe(true)
    })

    Jest.test("AsyncSeq.ofObservableBuffered should work (one, fail)", async {
        let src, discarded = observe [1] true
        let actual = src |> AsyncSeq.ofObservableBuffered |> AsyncSeq.toArrayAsync

        do! Jest.expect(actual |> Async.StartAsPromise).rejects.toThrow()
        Jest.expect(discarded()).toBe(true)
    })

    Jest.test("AsyncSeq.ofObservableBuffered should work (one, take)", async {
        let src, discarded = observe [1] true
        let! actual = src |> AsyncSeq.ofObservableBuffered |> AsyncSeq.take 1 |> AsyncSeq.toArrayAsync

        Jest.expect(actual).toEqual([|1|])
        Jest.expect(discarded()).toBe(true)
    })
)
