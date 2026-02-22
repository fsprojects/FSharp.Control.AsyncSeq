# FSharp.Control.AsyncSeq [![NuGet Status](http://img.shields.io/nuget/v/FSharp.Control.AsyncSeq.svg?style=flat)](https://www.nuget.org/packages/FSharp.Control.AsyncSeq/)

**FSharp.Control.AsyncSeq** is a collection of asynchronous programming utilities for F#. 

See [the home page](http://fsprojects.github.io/FSharp.Control.AsyncSeq/) for details. The home page can be [edited, forked or cloned](https://github.com/fsprojects/FSharp.Control.AsyncSeq/tree/master/docs/content)
Please contribute to this project. Don't ask for permission, just fork the repository and send pull requests.

## Version 4.0 — BCL IAsyncEnumerable Compatibility

As of **v4.0**, `AsyncSeq<'T>` is a type alias for `System.Collections.Generic.IAsyncEnumerable<'T>` (the BCL type). This means:

- `AsyncSeq<'T>` values are directly usable anywhere `IAsyncEnumerable<'T>` is expected (e.g. `await foreach` in C#, `IAsyncEnumerable<T>` APIs).
- `IAsyncEnumerable<'T>` values (from EF Core, ASP.NET Core streaming endpoints, etc.) can be used directly wherever `AsyncSeq<'T>` is expected — no conversion needed.
- The `AsyncSeq.ofAsyncEnum` and `AsyncSeq.toAsyncEnum` helpers are now **no-ops** and have been marked `[<Obsolete>]`. Remove any calls to them.

### Migrating from v3.x

If you called `.GetEnumerator()` / `.MoveNext()` directly on an `AsyncSeq<'T>`, update to `.GetAsyncEnumerator(ct)` / `.MoveNextAsync()` (the `IAsyncEnumerator<'T>` BCL contract). All other `AsyncSeq` module combinators are unchanged.

# Maintainer(s)

- [@dsyme](https://github.com/dsyme)
- [@martinmoec](https://github.com/martinmoec)
- [@xperiandri](https://github.com/xperiandri)
- [@abelbraaksma](https://github.com/abelbraaksma)

The default maintainer account for projects under "fsprojects" is [@fsprojectsgit](https://github.com/fsprojectsgit) - F# Community Project Incubation Space (repo management)
