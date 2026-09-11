$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$main = Join-Path $repoRoot 'UsbSmokeIdf\main'
$stubs = Join-Path $repoRoot 'UsbSmokeIdf\tests\stubs'
$testSource = Join-Path $repoRoot 'UsbSmokeIdf\tests\test_head_sequence_executor.cpp'
$ino = Join-Path $repoRoot 'referencias\programa2_final\firmware_programa2_final.ino'
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'

if (-not (Test-Path -LiteralPath $vswhere)) {
    throw 'No se encontro vswhere.exe; se necesita MSVC Build Tools.'
}

$vsInstall = & $vswhere -latest -products * -property installationPath
if (-not $vsInstall) {
    throw 'No se encontro una instalacion de MSVC Build Tools.'
}

$msvcRoot = Join-Path $vsInstall 'VC\Tools\MSVC'
$msvc = Get-ChildItem -LiteralPath $msvcRoot -Directory |
    Sort-Object Name |
    Select-Object -Last 1
$sdkRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10'
$sdk = Get-ChildItem -LiteralPath (Join-Path $sdkRoot 'Include') -Directory |
    Sort-Object Name |
    Select-Object -Last 1
if (-not $msvc -or -not $sdk) {
    throw 'No se encontraron MSVC y Windows SDK completos.'
}

$cl = Join-Path $msvc.FullName 'bin\Hostx64\x64\cl.exe'
$sdkVersion = $sdk.Name
$env:Path = (Join-Path $msvc.FullName 'bin\Hostx64\x64') + ';' +
    (Join-Path $sdkRoot "bin\$sdkVersion\x64") + ';' + $env:Path
$env:Include = (Join-Path $msvc.FullName 'include') + ';' +
    (Join-Path $sdkRoot "Include\$sdkVersion\ucrt") + ';' +
    (Join-Path $sdkRoot "Include\$sdkVersion\shared") + ';' +
    (Join-Path $sdkRoot "Include\$sdkVersion\um") + ';' +
    (Join-Path $sdkRoot "Include\$sdkVersion\winrt")
$env:Lib = (Join-Path $msvc.FullName 'lib\x64') + ';' +
    (Join-Path $sdkRoot "Lib\$sdkVersion\ucrt\x64") + ';' +
    (Join-Path $sdkRoot "Lib\$sdkVersion\um\x64")

$outputDir = Join-Path ([IO.Path]::GetTempPath()) 'acx-p2-head-sequence-tests'
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
$testExe = Join-Path $outputDir 'test_head_sequence_executor.exe'
$executor = Join-Path $main 'head_sequence_executor.cpp'
$program2 = Join-Path $main 'head_unified_program_2_sequence.cpp'

Push-Location $outputDir
try {
    & $cl /nologo /std:c++20 /EHsc /W4 "/I$stubs" "/I$main" `
        $testSource $executor $program2 "/Fe:$testExe"
    if ($LASTEXITCODE -ne 0) {
        throw "La compilacion host fallo con codigo $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}

& $testExe $ino
if ($LASTEXITCODE -ne 0) {
    throw "La validacion de secuencia fallo con codigo $LASTEXITCODE."
}

$profileTestSource = Join-Path $repoRoot 'UsbSmokeIdf\tests\test_unified_program_2_yarn_profile.cpp'
$profileTestExe = Join-Path $outputDir 'test_unified_program_2_profile.exe'
$program2Profile = Join-Path $main 'head_unified_program_2_commands.cpp'
$frameBuilder = Join-Path $main 'head_command_frame_builder.cpp'

Push-Location $outputDir
try {
    & $cl /nologo /std:c++20 /EHsc /W4 "/I$stubs" "/I$main" `
        $profileTestSource $program2Profile $frameBuilder "/Fe:$profileTestExe"
    if ($LASTEXITCODE -ne 0) {
        throw "La compilacion host del perfil P2 fallo con codigo $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}

& $profileTestExe
if ($LASTEXITCODE -ne 0) {
    throw "La validacion del perfil fisico P2 fallo con codigo $LASTEXITCODE."
}

$u3TestSource = Join-Path $repoRoot 'UsbSmokeIdf\tests\test_u3_motor_protocol.cpp'
$u3TestExe = Join-Path $outputDir 'test_u3_motor_protocol.exe'
$u3Protocol = Join-Path $main 'u3_motor_protocol.cpp'

Push-Location $outputDir
try {
    & $cl /nologo /std:c++20 /EHsc /W4 "/I$stubs" "/I$main" `
        $u3TestSource $u3Protocol "/Fe:$u3TestExe"
    if ($LASTEXITCODE -ne 0) {
        throw "La compilacion host del protocolo U3 fallo con codigo $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}

& $u3TestExe
if ($LASTEXITCODE -ne 0) {
    throw "La validacion del protocolo U3 fallo con codigo $LASTEXITCODE."
}

& (Join-Path $PSScriptRoot 'compare_u3_init.ps1')
if ($LASTEXITCODE -ne 0) {
    throw "La comparacion U3 INIT fallo con codigo $LASTEXITCODE."
}

$neutralFiles = @(
    'head_state_manager.cpp',
    'head_command_profile.h',
    'head_command_frame_builder.cpp',
    'command_unified_head_processor.cpp'
) | ForEach-Object { Join-Path $main $_ }
$protectedProgramFiles = @(
    'head_program_1_commands.cpp',
    'head_program_1_commands.h',
    'head_program_3_commands.cpp',
    'head_program_3_commands.h',
    'head_unified_program_1_commands.cpp',
    'head_unified_program_1_commands.h',
    'head_unified_program_3_commands.cpp',
    'head_unified_program_3_commands.h'
) | ForEach-Object { Join-Path $main $_ }

$p2Leak = Select-String -LiteralPath ($neutralFiles + $protectedProgramFiles) `
    -Pattern '(?i)program2|unified_program2|PROGRAM2_' -ErrorAction Stop
if ($p2Leak) {
    throw "Se encontraron nombres/datos P2 fuera de sus archivos propios: $($p2Leak.Path):$($p2Leak.LineNumber)"
}

Write-Output 'OK: capas neutrales y archivos P1/P3 sin nombres ni datos P2.'
