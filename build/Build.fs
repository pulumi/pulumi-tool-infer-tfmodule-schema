open System.Threading.Tasks
open Fake.Core
open Fake.IO
open System
open System.IO
open System.Linq
open System.IO.Compression
open System.Xml
open System.Text
open ICSharpCode.SharpZipLib.GZip
open ICSharpCode.SharpZipLib.Tar
open Octokit

let cwd = __SOURCE_DIRECTORY__
let toolPath = Path.Combine(cwd, "..", "src")

let toolVersion() =
    let projectFilePath = Path.Combine(toolPath, "InferModuleSchema.csproj")
    let content = File.ReadAllText projectFilePath
    let doc = XmlDocument()
    use content = new MemoryStream(Encoding.UTF8.GetBytes content)
    doc.Load(content)
    doc.GetElementsByTagName("Version").[0].InnerText

let artifacts = "./artifacts"

let createTarGz (source: string) (target: string)  =
    let outStream = File.Create target
    let gzipOutput = new GZipOutputStream(outStream)
    let tarArchive = TarArchive.CreateOutputTarArchive(gzipOutput);
    for file in Directory.GetFiles source do
        let tarEntry = TarEntry.CreateEntryFromFile file
        tarEntry.Name <- Path.GetFileName file
        tarArchive.WriteEntry(tarEntry, false)

    for directory in Directory.GetDirectories source do
        for file in Directory.GetFiles directory do
            let tarEntry = TarEntry.CreateEntryFromFile file
            tarEntry.Name <- Path.GetFileName file
            tarArchive.WriteEntry(tarEntry, false)

    tarArchive.Close()

let cleanArtifacts() = Shell.deleteDirs [artifacts]

let createArtifacts() =
    let version = toolVersion()
    let cwd = toolPath
    let runtimes = [
        "linux-x64"
        "linux-arm64"
        "osx-x64"
        "osx-arm64"
        "win-x64"
        "win-arm64"
    ]

    Shell.deleteDirs [
        Path.Combine(cwd, "bin")
        Path.Combine(cwd, "obj")
        artifacts
    ]

    let binary = "pulumi-tool-infer-tfmodule-schema"
    for runtime in runtimes do
        printfn $"Building binary {binary} for {runtime}"
        let args = [
            "publish"
            "--configuration Release"
            $"--runtime {runtime}"
            "--self-contained true"
            "-p:PublishSingleFile=true"
            "/p:DebugType=None"
            "/p:DebugSymbols=false"
        ]
        let exitCode = Shell.Exec("dotnet", String.concat " " args, cwd)
        if exitCode <> 0 then
            failwith $"failed to build for runtime {runtime}"

    Directory.create artifacts
    for runtime in runtimes do
        let publishPath = Path.Combine(cwd, "bin", "Release", "net9.0", runtime, "publish")
        let destinationRuntime =
            match runtime with
            | "osx-x64" -> "darwin-amd64"
            | "osx-arm64" -> "darwin-arm64"
            | "linux-x64" -> "linux-amd64"
            | "linux-arm64" -> "linux-arm64"
            | "win-x64" -> "windows-amd64"
            | "win-arm64" -> "windows-arm64"
            | _ -> runtime

        let destination = Path.Combine(artifacts, $"{binary}-v{version}-{destinationRuntime}.tar.gz")
        createTarGz publishPath destination

let inline await (task: Task<'t>) =
    task
    |> Async.AwaitTask
    |> Async.RunSynchronously

let releaseVersion (release: Release) =
    if not (String.IsNullOrWhiteSpace(release.Name)) then
        release.Name.Substring(1, release.Name.Length - 1)
    elif not (String.IsNullOrWhiteSpace(release.TagName)) then
        release.TagName.Substring(1, release.TagName.Length - 1)
    else
        ""

let createAndPublishArtifacts() =
    let version = toolVersion()
    let github = GitHubClient(ProductHeaderValue "PulumiTFModuleSchemaInference")
    let githubToken = Environment.GetEnvironmentVariable "GITHUB_TOKEN"
    // only assign github token to the client when it is available (usually in Github CI)
    if not (isNull githubToken) then
        printfn "GITHUB_TOKEN is available"
        github.Credentials <- Credentials(githubToken)
    else
        printfn "GITHUB_TOKEN is not set"

    let githubUsername = "pulumi"
    let githubRepo = "pulumi-tool-infer-tfmodule-schema"
    let releases = await (github.Repository.Release.GetAll(githubUsername, githubRepo))
    let alreadyReleased = releases |> Seq.exists (fun release -> releaseVersion release = version)

    if alreadyReleased then
        printfn $"Release v{version} already exists, skipping publish"
    else
        printfn $"Preparing artifacts to release v{version}"
        createArtifacts()
        let releaseInfo = NewRelease($"v{version}")
        let release = await (github.Repository.Release.Create(githubUsername, githubRepo, releaseInfo))
        for file in Directory.EnumerateFiles artifacts do
            let asset = ReleaseAssetUpload()
            asset.FileName <- Path.GetFileName file
            asset.ContentType <- "application/tar"
            asset.RawData <- File.OpenRead(file)
            let uploadedAsset = await (github.Repository.Release.UploadAsset(release, asset))
            printfn $"Uploaded {uploadedAsset.Name} into assets of v{version}"

[<EntryPoint>]
let main args = 
    match args with 
    | [| "clean" |] -> cleanArtifacts()
    | [| "artifacts" |] -> createArtifacts()
    | [| "publish" |] -> createAndPublishArtifacts()
    | _ -> failwithf "Invalid arguments %A" args

    0