// --------------------------------------------------------------------------------------
// FAKE build script
// --------------------------------------------------------------------------------------

// This is a FAKE 5.0 script, run using
//    dotnet fake build

#r "paket: groupref fake //"

#if !FAKE
#load ".fake/build.fsx/intellisense.fsx"
#r "netstandard"
#endif

open Fake.Core
open Fake.Core.TargetOperators
open Fake.IO.Globbing.Operators
open Fake.IO.FileSystemOperators
open Fake.DotNet
open Fake.IO
open Fake.Tools
//open Fake.ReleaseNotesHelper
//open Fake.AssemblyInfoFile
open System
open System.IO

// --------------------------------------------------------------------------------------
// START TODO: Provide project-specific details below
// --------------------------------------------------------------------------------------

// The name of the project
// (used by attributes in AssemblyInfo, name of a NuGet package and directory in 'src')
let project = "src/FSharp.Control.AsyncSeq"

// File system information 
let solutionFile = "FSharp.Control.AsyncSeq.sln"

let summary = "Asynchronous sequences for F#"

let license = "Apache 2.0 License"

let buildDir = "bin"

// Git configuration (used for publishing documentation in gh-pages branch)
// The profile where the project is posted
let projectRepo = "https://github.com/fsprojects/FSharp.Control.AsyncSeq"

let configuration = DotNet.BuildConfiguration.fromEnvironVarOrDefault "configuration" DotNet.BuildConfiguration.Release

// Folder to deposit deploy artifacts
let artifactsDir = __SOURCE_DIRECTORY__ @@ "artifacts"

// Read additional information from the release notes document
let release = ReleaseNotes.load "RELEASE_NOTES.md"

// --------------------------------------------------------------------------------------
// Clean build results

Target.create "Clean" (fun _ ->
    Shell.cleanDirs ["bin"; "temp"]
)

// Generate assembly info files with the right version & up-to-date information
Target.create "AssemblyInfo" (fun _ ->
    let info = [
        AssemblyInfo.Product project
        AssemblyInfo.Description summary
        AssemblyInfo.Version release.AssemblyVersion
        AssemblyInfo.FileVersion release.AssemblyVersion
        AssemblyInfo.InformationalVersion release.NugetVersion
        AssemblyInfo.Copyright license
    ]

    AssemblyInfoFile.createFSharp "src/Common/AssemblyInfo.fs" info
    AssemblyInfoFile.createCSharp "src/Common/AssemblyInfo.cs" info
    let releaseNotes = String.toLines release.Notes |> System.Net.WebUtility.HtmlEncode
    File.WriteAllText("version.props", sprintf """<Project>
      <PropertyGroup>
        <Version>%s</Version>
      </PropertyGroup>
    </Project>""" release.NugetVersion)
)

// --------------------------------------------------------------------------------------
// Build library & test project

Target.create "Build" (fun _ ->
    solutionFile
    |> DotNet.build (fun opts -> { opts with Configuration = configuration } ))

// --------------------------------------------------------------------------------------
// Run the unit tests using test runner

Target.create "RunTests" (fun _ ->
    solutionFile
    |> DotNet.test (fun opts ->
        { opts with
            Blame = true
            NoBuild = true
            Configuration = configuration
            ResultsDirectory = Some "TestResults"
            Logger = Some "trx"
        })
)

// --------------------------------------------------------------------------------------
// Build a NuGet package

Target.create "NuGet" (fun _ ->
    DotNet.pack (fun pack ->
        { pack with
            OutputPath = Some artifactsDir 
            Configuration = configuration 
        }) solutionFile
)

// --------------------------------------------------------------------------------------
// Generate the documentation

Target.create "GenerateDocs" (fun _ ->
    Shell.cleanDir ".fsdocs"
    DotNet.exec id "fsdocs" "build --clean" |> ignore
)

// --------------------------------------------------------------------------------------
// Release 

Target.create "PublishNuget" (fun _ ->
    let source = "https://api.nuget.org/v3/index.json"
    let apikey =  Environment.environVar "NUGET_KEY"
    for artifact in !! (artifactsDir + "/*nupkg") do
        let result = DotNet.exec id "nuget" (sprintf "push -s %s -k %s %s" source apikey artifact)
        if not result.OK then failwith "failed to push packages"  
)
Target.create "ReleaseDocs" (fun _ ->
    Git.Repository.clone "" projectRepo "temp/gh-pages"
    Git.Branches.checkoutBranch "temp/gh-pages" "gh-pages"
    Shell.copyRecursive "output" "temp/gh-pages" true |> printfn "%A"
    Git.CommandHelper.runSimpleGitCommand "temp/gh-pages" "add ." |> printfn "%s"
    let cmd = sprintf """commit -a -m "Update generated documentation for version %s""" release.NugetVersion
    Git.CommandHelper.runSimpleGitCommand "temp/gh-pages" cmd |> printfn "%s"
    Git.Branches.push "temp/gh-pages"
)

Target.create "Release" (fun _ ->
    Git.Branches.tag "" release.NugetVersion
    Git.Branches.pushTag "" projectRepo release.NugetVersion
    
)

// --------------------------------------------------------------------------------------
// Run all targets by default. Invoke 'build <Target>' to override

Target.create "All" ignore

"Clean"
  ==> "Build"
  ==> "RunTests"
  ==> "All"
  ==> "NuGet"
  ==> "PublishNuget"
  ==> "GenerateDocs"
  ==> "ReleaseDocs"
  ==> "Release"

Target.runOrDefault "All"
