namespace System
open System.Reflection

[<assembly: AssemblyTitleAttribute("AsyncSeq")>]
[<assembly: AssemblyProductAttribute("AsyncSeq")>]
[<assembly: AssemblyDescriptionAttribute("Asynchronous sequences for F#")>]
[<assembly: AssemblyVersionAttribute("1.12.1")>]
[<assembly: AssemblyFileVersionAttribute("1.12.1")>]
do ()

module internal AssemblyVersionInformation =
    let [<Literal>] Version = "1.12.1"
