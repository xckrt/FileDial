<#
.SYNOPSIS
    Собирает дистрибутив FileDial: один самодостаточный FileDial.exe (Windows x64) + README в zip.

.DESCRIPTION
    1. Прогоняет тесты (можно пропустить ключом -SkipTests).
    2. dotnet publish с профилем win-x64: .NET внутри exe, ставить ничего не нужно.
    3. Упаковывает dist\FileDial в dist\FileDial-<версия>-win-x64.zip и печатает SHA-256.

    ffmpeg и модель распознавания речи в дистрибутив не входят: программа ставит их сама одной кнопкой.
    Для полностью офлайн-дистрибутива положите в dist\FileDial перед упаковкой:
      ffmpeg.exe и ffprobe.exe — рядом с FileDial.exe;
      models\ggml-base.bin (или ggml-small.bin) — модель whisper.
    и запустите скрипт с ключом -KeepExtras.

.EXAMPLE
    .\publish.ps1
    .\publish.ps1 -SkipTests
#>
param(
    [switch]$SkipTests,
    # Не очищать dist\FileDial (сохранить положенные туда ffmpeg и модели).
    [switch]$KeepExtras
)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$project = 'src\FileDial.App\FileDial.App.csproj'
[xml]$csproj = Get-Content $project -Encoding UTF8
$version = ($csproj.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
$out = Join-Path $PSScriptRoot 'dist\FileDial'
$zip = Join-Path $PSScriptRoot "dist\FileDial-$version-win-x64.zip"

if (-not $SkipTests) {
    Write-Host '== Тесты' -ForegroundColor Cyan
    dotnet test --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'Тесты не прошли — дистрибутив не собран.' }
}

if (-not $KeepExtras -and (Test-Path $out)) {
    Remove-Item -Recurse -Force $out
}

Write-Host "== Публикация $version" -ForegroundColor Cyan
# DebugType передаём всей сборке: иначе у FileDial.Core рядом с exe остаётся отдельный .pdb.
dotnet publish $project -p:PublishProfile=win-x64 -p:DebugType=embedded --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish завершился с ошибкой.' }

if (Test-Path $zip) { Remove-Item -Force $zip }
Compress-Archive -Path (Join-Path $out '*') -DestinationPath $zip -CompressionLevel Optimal

Write-Host '== Готово' -ForegroundColor Green
Get-ChildItem $out -Recurse -File | ForEach-Object {
    '{0,-40} {1,8:N1} МБ' -f $_.FullName.Substring($out.Length + 1), ($_.Length / 1MB)
}
'{0} — {1:N1} МБ' -f (Split-Path $zip -Leaf), ((Get-Item $zip).Length / 1MB)
'SHA-256: ' + (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
