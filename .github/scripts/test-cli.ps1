$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
$cli = Join-Path $root 'src/BaiduPCS.NET.Cli/bin/Release/net10.0/baidupcs.dll'
$scratch = Join-Path $root ('artifacts/cli-smoke-' + [Guid]::NewGuid().ToString('N'))
$legacy = Join-Path $scratch 'pcs_config.json'
$originalProfile = $env:BAIDUPCS_CONFIG_DIR
New-Item -ItemType Directory -Path $scratch | Out-Null
@'
{
  "baidu_active_uid": 21,
  "baidu_user_list": [
    {"uid":21,"name":"First account","workdir":"/First folder","bduss":"first-fixture-value"},
    {"uid":22,"name":"Second account","workdir":"/Second folder","bduss":"second-fixture-value"}
  ],
  "max_parallel": 17
}
'@ | Set-Content -LiteralPath $legacy
$sourceHash = (Get-FileHash -LiteralPath $legacy).Hash

function Test-ClearConfirmation($branch) {
    $PSNativeCommandUseErrorActionPreference = $false
    $null = '' | dotnet $cli $branch delete --all 2>$null
    if ($LASTEXITCODE -ne 2) { throw "$branch clear-all did not require confirmation." }
}

try {
    $env:BAIDUPCS_CONFIG_DIR = Join-Path $scratch 'profile'
    dotnet $cli account import-go $legacy
    $config = (dotnet $cli config show) | ConvertFrom-Json
    if ($config.'download-parallel' -ne 17) { throw 'Legacy download concurrency did not import.' }
    $expected = @(dotnet $cli pwd; dotnet $cli account current)
    if ($expected[0] -ne '/First folder' -or $expected[1] -notlike "21`tFirst account`t*") {
        throw 'The imported active account or its working directory is incorrect.'
    }
    $shell = @('pwd', 'account current') | dotnet $cli shell
    if (($shell -join "`n") -cne ($expected -join "`n")) {
        throw 'The one-shot interface and shell returned different profile data.'
    }

    @('account switch "Second account"', 'config reset') | dotnet $cli shell
    $path = dotnet $cli pwd
    if ($path -ne '/Second folder' -or (Get-FileHash -LiteralPath $legacy).Hash -ne $sourceHash) {
        throw 'The shell did not persist account selection or the legacy config was modified.'
    }
    Test-ClearConfirmation 'offline'
    Test-ClearConfirmation 'recycle'
    Write-Host 'CLI profile import, one-shot and shell commands, persistence, and clear-all confirmation passed.'
} finally {
    $env:BAIDUPCS_CONFIG_DIR = $originalProfile
}

exit 0
