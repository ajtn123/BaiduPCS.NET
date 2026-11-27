param(
    [Parameter(Mandatory)]
    [ValidateSet('win-x64', 'win-arm64', 'osx-x64', 'osx-arm64', 'linux-x64', 'linux-arm64', 'linux-musl-x64', 'linux-musl-arm64')]
    [string] $Runtime,
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string] $ReleaseVersion,
    [switch] $FrameworkDependent
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
$project = Join-Path $root 'src/BaiduPCS.NET.Cli/BaiduPCS.NET.Cli.csproj'
$artifacts = Join-Path $root 'artifacts'
$scratch = Join-Path $artifacts ([Guid]::NewGuid().ToString('N'))
$publish = Join-Path $scratch 'publish'
$output = Join-Path $artifacts 'standalone'
$arguments = @()
if ($ReleaseVersion) { $arguments += "-p:Version=$ReleaseVersion" }
$version = dotnet msbuild $project -nologo -getProperty:Version @arguments
$extension = if ($Runtime.StartsWith('win-')) { '.exe' } else { '' }
$flavor = if ($FrameworkDependent) { '-framework-dependent' } else { '' }
$name = "BaiduPCS.NET-v$version-$Runtime$flavor$extension"
$executable = Join-Path $output $name

# A self-contained publish adds SDK-versioned runtime packs to the restore graph.
# Use a separate, untracked lock-file path to keep the framework-dependent lock files intact.
$selfContained = (!$FrameworkDependent).ToString().ToLowerInvariant()
$optimization = if ($FrameworkDependent) { @() } else {
    @('-p:PublishReadyToRun=true', '-p:PublishReadyToRunComposite=false', '-p:EnableCompressionInSingleFile=true')
}
dotnet publish $project --configuration Release --runtime $Runtime --self-contained $selfContained `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=embedded `
    -p:NuGetLockFilePath=packages.publish.lock.json -p:RestorePackagesWithLockFile=false `
    --output $publish @arguments @optimization

$files = @(Get-ChildItem -LiteralPath $publish -Force)
if ($files.Count -ne 1 -or $files[0].Name -ne "baidupcs$extension") {
    throw "Expected one executable for $Runtime."
}
New-Item -ItemType Directory -Path $output -Force | Out-Null
Copy-Item -LiteralPath $files[0].FullName -Destination $executable
if (!$IsWindows) { chmod a+x $executable }

if (!$FrameworkDependent) {
    $assets = Get-Content -Raw -LiteralPath (Join-Path (Split-Path $project) 'obj/project.assets.json') | ConvertFrom-Json -AsHashtable
    $runtimePackage = "Microsoft.NETCore.App.Runtime.$Runtime"
    $download = $assets.project.frameworks.'net10.0'.downloadDependencies | Where-Object name -EQ $runtimePackage
    if ($download.version -notmatch '^\[([^,]+),') { throw "Missing restored $runtimePackage version." }
    $packagePath = Join-Path $runtimePackage.ToLowerInvariant() $Matches[1]
    $package = $assets.packageFolders.Keys | ForEach-Object { Join-Path $_ $packagePath } |
        Where-Object { Test-Path -LiteralPath $_ -PathType Container } | Select-Object -First 1
    if (!$package) { throw "Cannot locate the restored $runtimePackage package." }
    @(
        Get-Content -Raw -LiteralPath (Join-Path $package 'LICENSE.TXT')
        Get-Content -Raw -LiteralPath (Join-Path $package 'THIRD-PARTY-NOTICES.TXT')
    ) | Set-Content -LiteralPath (Join-Path $output "$name.runtime-notices.txt")
}

$fixture = Join-Path $scratch 'fixture'
New-Item -ItemType Directory -Path $fixture | Out-Null
$legacy = Join-Path $fixture 'pcs_config.json'
'{"baidu_active_uid":21,"baidu_user_list":[{"uid":21,"name":"Packaged Fixture","workdir":"/fixture","bduss":"synthetic-fixture"}]}' |
    Set-Content -LiteralPath $legacy

$musl = $Runtime.StartsWith('linux-musl-')
if ($musl) {
    $image = if ($FrameworkDependent) { 'mcr.microsoft.com/dotnet/runtime:10.0-alpine-extra' }
        else { 'mcr.microsoft.com/dotnet/runtime-deps:10.0-alpine-extra' }
    $mount = "type=bind,source=$output,target=/test,readonly"
    $fixtureMount = "type=bind,source=$fixture,target=/fixture"
    $legacy = '/fixture/pcs_config.json'
}

function Invoke-PackagedCli([string[]] $CommandLine) {
    if ($musl) {
        docker run --rm --user 0:0 --mount $mount --mount $fixtureMount -e BAIDUPCS_CONFIG_DIR=/fixture/profile $image "/test/$name" @CommandLine
    } else {
        & $executable @CommandLine
    }
}

$originalProfile = $env:BAIDUPCS_CONFIG_DIR
try {
    $env:BAIDUPCS_CONFIG_DIR = Join-Path $fixture 'profile'
    $actualVersion = Invoke-PackagedCli @('--version')
    $help = Invoke-PackagedCli @('sync', '--help')
    Invoke-PackagedCli @('account', 'import-go', $legacy) | Out-Null
    $accounts = Invoke-PackagedCli @('account', 'ls')
    $config = (Invoke-PackagedCli @('config', 'show')) | ConvertFrom-Json
    if (($actualVersion -join '').Trim() -ne $version -or ($help -join "`n") -notmatch '--dry-run\b' -or
        ($accounts -join "`n") -notmatch 'Packaged Fixture' -or $config.'download-parallel' -lt 1) {
        throw "Packaged executable smoke test failed for $Runtime."
    }
} finally {
    $env:BAIDUPCS_CONFIG_DIR = $originalProfile
}
Write-Host "Verified $executable"
