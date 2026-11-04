<#
.SYNOPSIS
    Собирает дистрибутив FileDial: один самодостаточный FileDial.exe (Windows x64) + README в zip.

.DESCRIPTION
    1. Прогоняет тесты (можно пропустить ключом -SkipTests).
    2. dotnet publish с профилем win-x64: .NET внутри exe, ставить ничего не нужно.
    2a. Подписывает exe, если задан сертификат: -CertificateThumbprint (хранилище CurrentUser\My)
        или переменные окружения FILEDIAL_SIGN_PFX (путь к .pfx) и FILEDIAL_SIGN_PASSWORD.
        signtool — из Windows SDK, метка времени — DigiCert. Не задано ничего — exe остаётся без подписи.
    3. Упаковывает dist\FileDial в dist\FileDial-<версия>-win-x64.zip (переносная версия).
    4. Кладёт рядом установщик dist\FileDial-Setup-<версия>.exe — это тот же exe: по слову «Setup» в имени
       он открывает мастер установки. Печатает SHA-256 обоих файлов.

    ffmpeg и модель распознавания речи в дистрибутив не входят: программа ставит их сама одной кнопкой.
    Для полностью офлайн-дистрибутива положите в dist\FileDial перед упаковкой:
      ffmpeg.exe и ffprobe.exe — рядом с FileDial.exe;
      models\ggml-base.bin (или ggml-small.bin) — модель whisper.
    и запустите скрипт с ключом -KeepExtras.

.EXAMPLE
    .\publish.ps1
    .\publish.ps1 -SkipTests
    .\publish.ps1 -CertificateThumbprint 0123456789ABCDEF0123456789ABCDEF01234567
#>
param(
    [switch]$SkipTests,
    # Не очищать dist\FileDial (сохранить положенные туда ffmpeg и модели).
    [switch]$KeepExtras,
    # Отпечаток сертификата подписи кода в CurrentUser\My.
    [string]$CertificateThumbprint
)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$project = 'src\FileDial.App\FileDial.App.csproj'
[xml]$csproj = Get-Content $project -Encoding UTF8
$version = ($csproj.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
$out = Join-Path $PSScriptRoot 'dist\FileDial'
$zip = Join-Path $PSScriptRoot "dist\FileDial-$version-win-x64.zip"
$setup = Join-Path $PSScriptRoot "dist\FileDial-Setup-$version.exe"

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

# Подпись — до упаковки и копирования под именем установщика: zip и Setup получают подписанный exe.
$exe = Join-Path $out 'FileDial.exe'
$pfx = $env:FILEDIAL_SIGN_PFX
if ($CertificateThumbprint -or $pfx) {
    $signtool = Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin\*\x64\signtool.exe' -ErrorAction SilentlyContinue |
        Sort-Object { try { [version]$_.Directory.Parent.Name } catch { [version]'0.0' } } -Descending |
        Select-Object -First 1
    if (-not $signtool) { throw 'signtool.exe не найден: установите Windows SDK (Signing Tools).' }
    $signArgs = @('sign', '/fd', 'SHA256', '/tr', 'http://timestamp.digicert.com', '/td', 'SHA256', '/d', 'FileDial')
    if ($CertificateThumbprint) {
        $signArgs += @('/s', 'My', '/sha1', ($CertificateThumbprint -replace '\s', ''))
    } else {
        if (-not (Test-Path $pfx)) { throw "FILEDIAL_SIGN_PFX: файл не найден — $pfx" }
        $signArgs += @('/f', $pfx)
        if ($env:FILEDIAL_SIGN_PASSWORD) { $signArgs += @('/p', $env:FILEDIAL_SIGN_PASSWORD) }
    }
    Write-Host '== Подпись' -ForegroundColor Cyan
    & $signtool.FullName @signArgs $exe
    if ($LASTEXITCODE -ne 0) { throw 'signtool не смог подписать exe.' }
} else {
    Write-Host 'Подпись пропущена: не задан -CertificateThumbprint или FILEDIAL_SIGN_PFX.' -ForegroundColor DarkGray
}

if (Test-Path $zip) { Remove-Item -Force $zip }
Compress-Archive -Path (Join-Path $out '*') -DestinationPath $zip -CompressionLevel Optimal
Copy-Item $exe $setup -Force

Write-Host '== Готово' -ForegroundColor Green
Get-ChildItem $out -Recurse -File | ForEach-Object {
    '{0,-40} {1,8:N1} МБ' -f $_.FullName.Substring($out.Length + 1), ($_.Length / 1MB)
}
'{0} — {1:N1} МБ' -f (Split-Path $zip -Leaf), ((Get-Item $zip).Length / 1MB)
'SHA-256: ' + (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
'{0} — {1:N1} МБ' -f (Split-Path $setup -Leaf), ((Get-Item $setup).Length / 1MB)
'SHA-256: ' + (Get-FileHash $setup -Algorithm SHA256).Hash.ToLowerInvariant()
