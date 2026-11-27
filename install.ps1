<#
.SYNOPSIS
Installs the latest stable BaiduPCS.NET executable for this computer.
.PARAMETER InstallDir
Overrides the per-user install directory.
.PARAMETER SelfContained
Installs the executable with its own .NET runtime.
#>
[CmdletBinding()]
param(
    [string] $InstallDir = $env:BAIDUPCS_INSTALL_DIR,
    [switch] $SelfContained
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

function Test-Musl {
    if (Get-Command ldd -ErrorAction SilentlyContinue) {
        $ErrorActionPreference = 'Continue'
        if ((& ldd --version 2>&1 | Out-String) -match 'musl') { return $true }
    }
    return [bool](Get-ChildItem /lib -Filter 'ld-musl-*.so.1' -ErrorAction SilentlyContinue)
}

function Test-DotnetRuntime([string] $Architecture, [string] $Executable) {
    $archRoot = [Environment]::GetEnvironmentVariable('DOTNET_ROOT_' + $Architecture.ToUpperInvariant())
    $dotnet = Get-Command $Executable -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    $roots = @($archRoot, $env:DOTNET_ROOT, (Join-Path $HOME '.dotnet'))
    if ($env:OS -eq 'Windows_NT') {
        $programFiles = if ($env:ProgramW6432) { $env:ProgramW6432 } else { $env:ProgramFiles }
        if ($programFiles) { $roots += Join-Path $programFiles 'dotnet' }
    } else {
        $roots += '/usr/share/dotnet', '/usr/lib/dotnet', '/usr/local/share/dotnet'
    }
    $candidates = @($dotnet.Source) + @($roots | Where-Object { $_ } | ForEach-Object { Join-Path $_ $Executable })
    foreach ($candidate in ($candidates | Where-Object { $_ } | Select-Object -Unique)) {
        if (!(Test-Path -LiteralPath $candidate -PathType Leaf)) { continue }
        $runtimes = & $candidate --list-runtimes --arch $Architecture 2>$null
        if ($LASTEXITCODE -ne 0) { $runtimes = & $candidate --list-runtimes 2>$null }
        if ($LASTEXITCODE -ne 0) { continue }
        foreach ($runtime in $runtimes) {
            if ($runtime -match '^Microsoft\.NETCore\.App (\d+)\.\d+\.\d+ \[' -and [int] $Matches[1] -ge 10) {
                return $true
            }
        }
    }
    return $false
}

function Install-Candidate([string] $Flavor) {
    $name = "BaiduPCS.NET-$tag-$rid$Flavor$extension"
    $asset = @($release.assets | Where-Object name -CEQ $name)
    if ($asset.Count -ne 1 -or $asset[0].digest -cnotmatch '^sha256:[a-f0-9]{64}$') {
        throw "GitHub did not provide a SHA-256 digest for $name."
    }
    Invoke-WebRequest -UseBasicParsing -Uri "$repository/releases/download/$tag/$name" -OutFile $candidate -UserAgent 'BaiduPCS.NET installer'
    $expected = $asset[0].digest.Substring(7)
    if ((Get-FileHash -LiteralPath $candidate -Algorithm SHA256).Hash -ine $expected) {
        throw "SHA-256 verification failed for $name."
    }
    if ($platform -ne 'win') {
        & chmod +x $candidate
        if ($LASTEXITCODE -ne 0) { throw "Could not make $name executable." }
    }
    # A dotnet muxer on PATH may find a runtime that a generated apphost cannot locate.
    try {
        $ErrorActionPreference = 'Continue'
        $version = & $candidate --version 2>$null
        return $LASTEXITCODE -eq 0 -and ($version -join '').Trim() -ceq $tag.Substring(1)
    } catch {
        return $false
    }
}

$platform = switch ($true) {
    ($env:OS -eq 'Windows_NT') { 'win'; break }
    ($IsMacOS) { 'osx'; break }
    ($IsLinux) { if (Test-Musl) { 'linux-musl' } else { 'linux' }; break }
    default { throw 'This operating system is unsupported.' }
}
$architecture = try {
    [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant()
} catch {
    $native = if ($env:PROCESSOR_ARCHITEW6432) { $env:PROCESSOR_ARCHITEW6432 } else { $env:PROCESSOR_ARCHITECTURE }
    if ($native -eq 'AMD64') { 'x64' } else { $native.ToLowerInvariant() }
}
if ($architecture -notin @('x64', 'arm64')) { throw "Unsupported architecture: $architecture." }
$rid = "$platform-$architecture"
$extension = if ($platform -eq 'win') { '.exe' } else { '' }

if (!$InstallDir) {
    $InstallDir = if ($platform -eq 'win') { Join-Path $env:LOCALAPPDATA 'BaiduPCS.NET/bin' } else { Join-Path $HOME '.local/bin' }
}
$InstallDir = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($InstallDir)
$repository = 'https://github.com/ajtn123/BaiduPCS.NET'
$release = Invoke-RestMethod -Uri 'https://api.github.com/repos/ajtn123/BaiduPCS.NET/releases/latest' -UserAgent 'BaiduPCS.NET installer'
$tag = $release.tag_name
if ($tag -cnotmatch '^v[0-9]+\.[0-9]+\.[0-9]+$' -or $release.draft -or $release.prerelease) {
    throw 'GitHub did not return a stable BaiduPCS.NET release.'
}

New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
$destination = Join-Path $InstallDir "baidupcs$extension"
$scratch = Join-Path $InstallDir ('.baidupcs-install-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $scratch | Out-Null
$candidate = Join-Path $scratch "baidupcs$extension"
try {
    $variant = 'self-contained'
    if (!$SelfContained -and (Test-DotnetRuntime $architecture "dotnet$extension")) {
        if (Install-Candidate '-framework-dependent') {
            $variant = 'framework-dependent'
        } else {
            Write-Host 'The framework-dependent executable could not use the detected runtime. Trying the self-contained build.'
        }
    }
    if ($variant -eq 'self-contained' -and !(Install-Candidate '')) {
        throw "The self-contained executable failed to start or reported an unexpected version."
    }
    if ([IO.File]::Exists($destination)) {
        [IO.File]::Replace($candidate, $destination, [NullString]::Value)
    } else {
        [IO.File]::Move($candidate, $destination)
    }
    Write-Host "Installed BaiduPCS.NET $tag ($variant, $rid) at $destination"
    if (($env:PATH -split [IO.Path]::PathSeparator) -notcontains $InstallDir) {
        Write-Host "Add $InstallDir to PATH to run baidupcs by name."
    }
} finally {
    if (Test-Path -LiteralPath $candidate) { Remove-Item -LiteralPath $candidate -Force }
    Remove-Item -LiteralPath $scratch
}
