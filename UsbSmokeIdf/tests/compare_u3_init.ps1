$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$referencePath = Join-Path $repoRoot 'referenciaspresentacion\dasboardservo\firmware.ino'
$runtimePath = Join-Path $repoRoot 'UsbSmokeIdf\main\u3_motor_control.cpp'
$referenceText = Get-Content -LiteralPath $referencePath -Raw -Encoding UTF8
$runtimeText = Get-Content -LiteralPath $runtimePath -Raw -Encoding UTF8

$referenceBlock = [regex]::Match(
    $referenceText,
    '(?s)static const char\* U3_INIT_SEQ\[\] = \{(.*?)nullptr').Groups[1].Value
$runtimeBlock = [regex]::Match(
    $runtimeText,
    '(?s)static const char \*const U3_INIT_SEQUENCE\[\] = \{(.*?)\};').Groups[1].Value

if ([string]::IsNullOrWhiteSpace($referenceBlock) -or [string]::IsNullOrWhiteSpace($runtimeBlock)) {
    throw 'No se pudo localizar U3_INIT_SEQ o U3_INIT_SEQUENCE.'
}

$referenceItems = @([regex]::Matches($referenceBlock, '"([^"]+)"') | ForEach-Object { $_.Groups[1].Value })
$runtimeItems = @([regex]::Matches($runtimeBlock, '"([^"]+)"') | ForEach-Object { $_.Groups[1].Value })
$referenceFrames = @($referenceItems | Where-Object { $_ -notlike 'WAIT *' })
$runtimeFrames = @($runtimeItems | Where-Object { $_ -notlike 'WAIT *' })
$referenceWaits = @($referenceItems | Where-Object { $_ -like 'WAIT *' })
$runtimeWaits = @($runtimeItems | Where-Object { $_ -like 'WAIT *' })
$differences = @(Compare-Object -ReferenceObject $referenceItems -DifferenceObject $runtimeItems -SyncWindow 0)

Write-Output "frames_reference=$($referenceFrames.Count)"
Write-Output "frames_runtime=$($runtimeFrames.Count)"
Write-Output "waits_reference=$($referenceWaits.Count)"
Write-Output "waits_runtime=$($runtimeWaits.Count)"
Write-Output "diff=$($differences.Count)"

if ($referenceWaits | Where-Object { $_ -ne 'WAIT 200' }) {
    throw 'La referencia contiene un WAIT diferente de 200 ms.'
}
if ($differences.Count -ne 0) {
    $differences | Format-Table -AutoSize
    throw 'U3_INIT_SEQUENCE difiere de la referencia.'
}
if ($referenceFrames.Count -ne 148 -or $runtimeFrames.Count -ne 148 -or $referenceWaits.Count -ne 148 -or $runtimeWaits.Count -ne 148) {
    throw 'El conteo U3 INIT esperado es 148 frames y 148 WAIT.'
}
