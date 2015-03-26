namespace System
open System.Reflection

[<assembly: AssemblyTitleAttribute("AsyncSeq")>]
[<assembly: AssemblyProductAttribute("AsyncSeq")>]
[<assembly: AssemblyDescriptionAttribute("Async extensions for F#")>]
[<assembly: AssemblyVersionAttribute("1.11.1")>]
[<assembly: AssemblyFileVersionAttribute("1.11.1")>]
do ()

module internal AssemblyVersionInformation =
    let [<Literal>] Version = "1.11.1"
