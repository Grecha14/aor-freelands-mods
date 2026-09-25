# Разбирает код игры в папку GameSrc\ — чтобы искать по нему классы и методы.
# В git GameSrc не идёт: это чужой закрытый код, храните его только у себя.
#
# Нужен ilspycmd:  dotnet tool install -g ilspycmd
#
#   .\tools\decompile.ps1
#   .\tools\decompile.ps1 -GameDir "D:\Games\Age of Reforging The Freelands"

param(
    [string]$GameDir = "C:\Program Files (x86)\Steam\steamapps\common\Age of Reforging The Freelands"
)

$ErrorActionPreference = "Stop"

if (-not (Get-Command ilspycmd -ErrorAction SilentlyContinue)) {
    throw "Нет ilspycmd. Поставьте: dotnet tool install -g ilspycmd"
}

$managed = Join-Path $GameDir "Age of Reforging The Freelands_Data\Managed"
$out = Join-Path $PSScriptRoot "..\GameSrc"
New-Item -ItemType Directory -Force $out | Out-Null

foreach ($assembly in @("Assembly-CSharp", "Assembly-CSharp-firstpass")) {
    $dll = Join-Path $managed "$assembly.dll"
    Write-Host "== $assembly"
    ilspycmd $dll -r $managed -o $out
    if ($LASTEXITCODE -ne 0) { throw "Не разобрался $assembly." }
}

Write-Host "Готово: $out"
