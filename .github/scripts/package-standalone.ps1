param(
    [Parameter(Mandatory)]
    [ValidateSet('win-x64', 'win-arm64', 'osx-x64', 'osx-arm64', 'linux-x64', 'linux-arm64', 'linux-musl-x64', 'linux-musl-arm64')]
    [string] $Runtime,
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string] $ReleaseVersion
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
$project = Join-Path $root 'src/BaiduPCS.NET.Cli/BaiduPCS.NET.Cli.csproj'
$artifacts = Join-Path $root 'artifacts'
$scratch = Join-Path $artifacts ([Guid]::NewGuid().ToString('N'))
$publish = Join-Path $scratch 'publish'
$expanded = Join-Path $scratch 'smoke'
$arguments = @()
if ($ReleaseVersion) { $arguments += "-p:Version=$ReleaseVersion" }
$version = dotnet msbuild $project -nologo -getProperty:Version @arguments
$name = "BaiduPCS.NET-v$version-$Runtime.zip"
$archive = Join-Path $artifacts $name

# A self-contained publish adds SDK-versioned runtime packs to the restore graph.
# Use a separate, untracked lock-file path to keep the framework-dependent lock files intact.
dotnet publish $project --configuration Release --runtime $Runtime --self-contained true `
    -p:PackAsTool=false -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=embedded `
    -p:NuGetLockFilePath=packages.publish.lock.json -p:RestorePackagesWithLockFile=false `
    --output $publish @arguments

Copy-Item -LiteralPath (Join-Path $root 'LICENSE'), (Join-Path $root 'THIRD-PARTY-NOTICES.md') -Destination $publish
Copy-Item -LiteralPath (Join-Path $root 'licenses') -Recurse -Destination $publish
$assets = Get-Content -Raw -LiteralPath (Join-Path (Split-Path $project) 'obj/project.assets.json') | ConvertFrom-Json -AsHashtable
$runtimePackage = "Microsoft.NETCore.App.Runtime.$Runtime"
$download = $assets.project.frameworks.'net10.0'.downloadDependencies | Where-Object name -EQ $runtimePackage
if ($download.version -notmatch '^\[([^,]+),') { throw "Missing restored $runtimePackage version." }
$packagePath = Join-Path $runtimePackage.ToLowerInvariant() $Matches[1]
$package = $assets.packageFolders.Keys | ForEach-Object { Join-Path $_ $packagePath } |
    Where-Object { Test-Path -LiteralPath $_ -PathType Container } | Select-Object -First 1
if (!$package) { throw "Cannot locate the restored $runtimePackage package." }
Copy-Item -LiteralPath (Join-Path $package 'LICENSE.TXT') -Destination (Join-Path $publish 'licenses/dotnet-runtime.txt')
Copy-Item -LiteralPath (Join-Path $package 'THIRD-PARTY-NOTICES.TXT') -Destination (Join-Path $publish 'licenses/dotnet-runtime-notices.txt')

if ($IsWindows) {
    Compress-Archive -Path (Join-Path $publish '*') -DestinationPath $archive -Force
    Expand-Archive -LiteralPath $archive -DestinationPath $expanded
} else {
    if (Test-Path -LiteralPath $archive) { Remove-Item -LiteralPath $archive }
    Push-Location $publish
    try { zip -q -r $archive . } finally { Pop-Location }
    unzip -q $archive -d $expanded
}

if ($Runtime.StartsWith('linux-musl-')) {
    $image = 'mcr.microsoft.com/dotnet/runtime-deps:10.0-alpine-extra'
    $mount = "type=bind,source=$expanded,target=/test,readonly"
    $actualVersion = docker run --rm --mount $mount $image /test/baidupcs --version
    $help = docker run --rm --mount $mount $image /test/baidupcs --help
} else {
    $executable = Join-Path $expanded $(if ($IsWindows) { 'baidupcs.exe' } else { 'baidupcs' })
    $actualVersion = & $executable --version
    $help = & $executable --help
}
if (($actualVersion -join '').Trim() -ne $version -or ($help -join "`n") -notmatch '\bdownload\b') {
    throw "Version or help smoke test failed for $Runtime."
}
Write-Host "Verified $archive"
