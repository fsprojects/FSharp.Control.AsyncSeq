namespace System
open System.Reflection

[<assembly: AssemblyTitleAttribute("FSharp.Control.AsyncSeq")>]
[<assembly: AssemblyProductAttribute("FSharp.Control.AsyncSeq")>]
[<assembly: AssemblyDescriptionAttribute("Asynchronous sequences for F#")>]
[<assembly: AssemblyVersionAttribute("1.13")>]
[<assembly: AssemblyFileVersionAttribute("1.13")>]
do ()

module internal AssemblyVersionInformation =
    let [<Literal>] Version = "1.13"
