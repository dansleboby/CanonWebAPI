<#
.SYNOPSIS
    Runs the photo booth camera checks against CanonWebAPI: live view, captures, flash changes during the live view
    and a camera power cycle.

.DESCRIPTION
    Copy this script next to Canon.API.exe. When the API does not answer, it is started from that folder with detailed
    camera logs and without the update check. Pictures, the result of every step and a copy of the API log are saved
    in test-results\<date-time> next to the script. Needs curl.exe (included in Windows 10 1803 and later).

.PARAMETER BaseUrl
    Address of the API. The release listens on http://localhost:5000.

.PARAMETER Captures
    Number of pictures taken with the live view open.

.PARAMETER CameraTimeoutSeconds
    How long to wait for the camera to be connected, at the start and after the power cycle.

.PARAMETER SkipPowerCycle
    Skips the step that asks to turn the camera off and on.

.PARAMETER NoBrowser
    Does not open the live view in the browser (a hidden client keeps it running anyway).

.EXAMPLE
    .\Test-Camera.ps1

.EXAMPLE
    .\Test-Camera.ps1 -Captures 6 -SkipPowerCycle
#>
[CmdletBinding()]
param(
    [string]$BaseUrl = 'http://localhost:5000',
    [ValidateRange(1, 50)][int]$Captures = 4,
    [ValidateRange(5, 600)][int]$CameraTimeoutSeconds = 120,
    [switch]$SkipPowerCycle,
    [switch]$NoBrowser
)

# Compatible with Windows PowerShell 5.1: no PowerShell 7 only syntax.
$ErrorActionPreference = 'Stop'

$onWindows = [System.Environment]::OSVersion.Platform -eq 'Win32NT'
$curl = if ($onWindows) { 'curl.exe' } else { 'curl' }
$nullDevice = if ($onWindows) { 'NUL' } else { '/dev/null' }
$apiExe = Join-Path $PSScriptRoot $(if ($onWindows) { 'Canon.API.exe' } else { 'Canon.API' })
$results = Join-Path (Join-Path $PSScriptRoot 'test-results') (Get-Date -Format 'yyyyMMdd-HHmmss')
$steps = New-Object System.Collections.Generic.List[object]
$script:captureNumber = 0

function Add-Step([string]$Name, [bool]$Passed, [string]$Detail) {
    $result = if ($Passed) { 'PASS' } else { 'FAIL' }
    $steps.Add([pscustomobject]@{ Step = $Name; Result = $result; Detail = $Detail })
    $color = if ($Passed) { 'Green' } else { 'Red' }
    Write-Host ('{0}  {1}: {2}' -f $result, $Name, $Detail) -ForegroundColor $color
}

# Calls the API with curl; returns the HTTP status (0 when unreachable), the duration and the X-File-Name header.
function Invoke-Api([string]$Method, [string]$Path, [string]$JsonBody, [string]$OutFile) {
    $headerFile = "$OutFile.headers"
    $curlArgs = @('-s', '-X', $Method, "$BaseUrl$Path", '-o', $OutFile, '-D', $headerFile,
        '-w', '%{http_code} %{time_total}', '--max-time', '90')

    if ($JsonBody) {
        # Through a file: Windows PowerShell 5.1 strips the double quotes of native command arguments.
        $bodyFile = Join-Path $results 'request-body.json'
        [System.IO.File]::WriteAllText($bodyFile, $JsonBody)
        $curlArgs += @('-H', 'Content-Type: application/json', '--data-binary', "@$bodyFile")
    }

    $out = & $curl @curlArgs
    $parts = "$out".Trim().Split(' ')
    $fileName = $null

    if (Test-Path $headerFile) {
        foreach ($line in Get-Content $headerFile) {
            if ($line -match '^X-File-Name:\s*(.+)$') { $fileName = $Matches[1].Trim() }
        }
        Remove-Item $headerFile
    }

    [pscustomobject]@{
        Status = [int]$parts[0]
        Seconds = [double]::Parse($parts[1], [System.Globalization.CultureInfo]::InvariantCulture)
        FileName = $fileName
    }
}

function Get-ProblemDetail([string]$File) {
    try {
        $problem = Get-Content $File -Raw | ConvertFrom-Json
        return "$($problem.title) - $($problem.detail)"
    }
    catch {
        return 'no details'
    }
}

function Get-Status {
    $file = Join-Path $results 'status.json'
    $response = Invoke-Api 'GET' '/status' $null $file
    if ($response.Status -ne 200) { return $null }
    return Get-Content $file -Raw | ConvertFrom-Json
}

# Polls the condition once a second; true as soon as it holds, false after the timeout.
function Wait-Until([int]$TimeoutSeconds, [scriptblock]$Condition) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if (& $Condition) { return $true }
        Start-Sleep -Seconds 1
    }
    return $false
}

function Test-LiveView {
    $status = Get-Status
    return [bool]($status -and $status.connected -and $status.liveViewActive)
}

function Invoke-Capture([string]$Name) {
    $script:captureNumber++
    $file = Join-Path $results ('capture-{0:00}.jpg' -f $script:captureNumber)
    $response = Invoke-Api 'POST' '/takepicture' $null $file

    if ($response.Status -eq 200) {
        $detail = '{0} in {1:0.0} s' -f $response.FileName, $response.Seconds
        # The photo booth takes about 1.5 s; a busy camera retries for 5 s per attempt.
        if ($response.Seconds -gt 5) { $detail += ' (slow: the camera answered busy, see the log)' }
        Add-Step $Name $true $detail
    }
    else {
        $problemFile = [System.IO.Path]::ChangeExtension($file, '.json')
        Move-Item $file $problemFile -Force
        Add-Step $Name $false ('HTTP {0} after {1:0.0} s: {2}' -f $response.Status, $response.Seconds, (Get-ProblemDetail $problemFile))
    }
}

function Invoke-FlashChange([string]$Value) {
    $file = Join-Path $results ('flash-{0}-{1:00}.json' -f $Value, ($script:captureNumber + 1))
    $response = Invoke-Api 'POST' '/flash' ('"' + $Value + '"') $file

    if ($response.Status -ne 200) {
        Add-Step "Flash $Value" $false ('HTTP {0}: {1}' -f $response.Status, (Get-ProblemDetail $file))
        return
    }

    # The live view is paused while the setting is written, then restarted.
    $resumed = Wait-Until 10 { Test-LiveView }
    $detail = if ($resumed) { 'set, live view resumed' } else { 'set, but the live view did not resume within 10 s' }
    Add-Step "Flash $Value" $resumed $detail
    Invoke-Capture "Capture after flash $Value"
}

function Invoke-Tests {
    if (-not (Get-Status)) {
        if (-not (Test-Path $apiExe)) {
            throw "The API does not answer at $BaseUrl and $apiExe was not found: copy this script next to it."
        }

        Write-Host "Starting $apiExe with detailed camera logs..."
        Start-Process $apiExe -WorkingDirectory $PSScriptRoot `
            -ArgumentList @('--Logging:LogLevel:Canon.Core=Debug', '--AutoUpdate:Enabled=false', "--urls=$BaseUrl")

        if (-not (Wait-Until 60 { Get-Status })) {
            throw "The API did not answer at $BaseUrl within 60 s."
        }
    }

    Write-Host 'Waiting for the camera (turn it on if needed)...'
    if (-not (Wait-Until $CameraTimeoutSeconds { $s = Get-Status; [bool]($s -and $s.connected) })) {
        Add-Step 'Camera connected' $false ("no camera within {0} s: {1}" -f $CameraTimeoutSeconds, (Get-Status).error)
        return
    }

    $status = Get-Status
    Add-Step 'Camera connected' $true ('{0}, {1}, flash firing: {2}' -f $status.cameraName, $status.mode.aeMode, $status.flash.firing)

    # Hidden client, so the live view runs as in the photo booth even without the browser.
    $streamArgs = @('-s', '-N', '-o', $nullDevice, "$BaseUrl/videostream")
    if ($onWindows) {
        $script:stream = Start-Process $curl -ArgumentList $streamArgs -WindowStyle Hidden -PassThru
    }
    else {
        $script:stream = Start-Process $curl -ArgumentList $streamArgs -PassThru
    }

    if (-not $NoBrowser) { Start-Process "$BaseUrl/videostream" }

    $liveView = Wait-Until 20 { Test-LiveView }
    Add-Step 'Live view started' $liveView $(if ($liveView) { 'running' } else { 'not running after 20 s' })

    for ($i = 1; $i -le $Captures; $i++) {
        Invoke-Capture "Capture $i with live view"
        Start-Sleep -Seconds 2
    }

    foreach ($value in @('fire', 'off', 'fire')) {
        Invoke-FlashChange $value
        Start-Sleep -Seconds 2
    }

    if (-not $SkipPowerCycle) {
        Write-Host ''
        Write-Host 'Turn the camera OFF, wait 10 s, turn it back ON, then press Enter.' -ForegroundColor Yellow
        Write-Host '(In a virtual machine, connect the camera to it again if needed.)'
        [void](Read-Host)

        $back = Wait-Until $CameraTimeoutSeconds { Test-LiveView }
        Add-Step 'Reconnected after power cycle' $back $(if ($back) { 'camera and live view back' } else { "not back within $CameraTimeoutSeconds s" })
        if ($back) { Invoke-Capture 'Capture after power cycle' }
    }
}

New-Item -ItemType Directory -Path $results -Force | Out-Null

if (-not (Get-Command $curl -ErrorAction SilentlyContinue)) {
    throw "$curl was not found: it is included in Windows 10 1803 and later."
}

$script:stream = $null

try {
    Invoke-Tests
}
finally {
    if ($script:stream -and -not $script:stream.HasExited) { Stop-Process -Id $script:stream.Id -Force }

    $logs = Join-Path $PSScriptRoot 'logs'
    $log = Get-ChildItem $logs -Filter 'canon-api*.log' -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($log) { Copy-Item $log.FullName $results }

    $table = $steps | Format-Table -AutoSize -Wrap | Out-String -Width 200
    Set-Content (Join-Path $results 'results.txt') $table
    Write-Host $table

    $failed = @($steps | Where-Object { $_.Result -eq 'FAIL' }).Count
    $color = if ($failed -eq 0) { 'Green' } else { 'Red' }
    Write-Host ('{0} step(s), {1} failed. Pictures, results and API log: {2}' -f $steps.Count, $failed, $results) -ForegroundColor $color
}

if (@($steps | Where-Object { $_.Result -eq 'FAIL' }).Count -gt 0) { exit 1 }
