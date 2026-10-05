<#
.SYNOPSIS
    Checks that a published CanonWebAPI package starts and loads the Canon EDSDK.

.DESCRIPTION
    Copies the folder of the executable to a temporary folder, so that its logs do not end up in the package, starts
    it, polls GET /status and passes when the API answers with a connected camera or "No Canon camera detected" (no
    camera on the machine). Any other answer, such as an EDSDK library that cannot be loaded, fails. Used by the CI and
    release workflows on the Windows and Linux runners (PowerShell 7).

.EXAMPLE
    ./tools/Test-Package.ps1 -Executable ./publish/Canon.API.exe
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Executable,
    [string]$Url = 'http://localhost:5000',
    [int]$TimeoutSeconds = 60
)

$ErrorActionPreference = 'Stop'

$source = (Resolve-Path $Executable).Path
$work = Join-Path ([System.IO.Path]::GetTempPath()) ('canonwebapi-package-' + [guid]::NewGuid())
Copy-Item (Split-Path $source) $work -Recurse
$exe = Join-Path $work (Split-Path $source -Leaf)
if ($IsLinux) { & chmod 755 $exe }
$output = Join-Path $work 'console.log'
$errorOutput = Join-Path $work 'console-error.log'

$process = Start-Process $exe -WorkingDirectory $work -PassThru -RedirectStandardOutput $output `
    -RedirectStandardError $errorOutput -ArgumentList @("--urls=$Url", '--AutoUpdate:Enabled=false')
$status = $null

try {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while (-not $status -and -not $process.HasExited -and (Get-Date) -lt $deadline) {
        try {
            $status = Invoke-RestMethod "$Url/status" -TimeoutSec 10
        }
        catch {
            Start-Sleep -Seconds 1
        }
    }
}
finally {
    if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force }
    $process.WaitForExit()
}

if ($status -and ($status.connected -or "$($status.error)" -match 'No Canon camera detected')) {
    $detail = if ($status.connected) { "camera $($status.cameraName) connected" } else { $status.error }
    Write-Host "Package OK: $detail"
    Remove-Item $work -Recurse -Force
    exit 0
}

$answer = if ($status) { $status | ConvertTo-Json -Compress } else { 'no answer' }
Write-Host "Package check failed. /status: $answer"
Write-Host '--- application output ---'
Get-Content $output, $errorOutput -ErrorAction SilentlyContinue | ForEach-Object { Write-Host $_ }
exit 1
