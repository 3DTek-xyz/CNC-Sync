param(
    [Parameter(Mandatory = $true)][string]$SourcePath,
    [Parameter(Mandatory = $true)][string]$OutputPath,
    [switch]$UpdateCycY
)

$ErrorActionPreference = "Stop"

$scriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$pythonScript = Join-Path (Join-Path $scriptDirectory "..\shared") "cbwss_mozaik_example.py"
$updateArg = if ($UpdateCycY) { "--update-cyc-y" } else { "" }

& python3 $pythonScript $SourcePath $OutputPath $updateArg
exit $LASTEXITCODE
