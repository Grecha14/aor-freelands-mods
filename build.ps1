# Собирает все три мода и кладёт DLL в release\. С -Install ещё и ставит их в игру.
#
#   .\build.ps1
#   .\build.ps1 -Install
#   .\build.ps1 -GameDir "D:\Games\Age of Reforging The Freelands" -Install

param(
    [string]$GameDir = "C:\Program Files (x86)\Steam\steamapps\common\Age of Reforging The Freelands",
    [switch]$Install
)

$ErrorActionPreference = "Stop"
$mods = @("DemonLook", "ItemForge", "EncounterScale")

if (-not (Test-Path (Join-Path $GameDir "BepInEx\core\BepInEx.dll"))) {
    throw "В '$GameDir' нет BepInEx. Поставьте BepInEx 5.4.23 x64 в папку игры и запустите её один раз."
}

if ($Install -and (Get-Process -Name "Age of Reforging The Freelands" -ErrorAction SilentlyContinue)) {
    throw "Игра запущена: DLL заняты. Закройте игру и повторите."
}

foreach ($mod in $mods) {
    Write-Host "== $mod"
    dotnet build (Join-Path $PSScriptRoot $mod) -c Release "-p:GameDir=$GameDir"
    if ($LASTEXITCODE -ne 0) { throw "Сборка $mod не удалась." }

    $dll = Join-Path $PSScriptRoot "$mod\bin\Release\$mod.dll"
    $release = Join-Path $PSScriptRoot "release\BepInEx\plugins\$mod"
    New-Item -ItemType Directory -Force $release | Out-Null
    Copy-Item $dll $release -Force

    if ($Install) {
        $target = Join-Path $GameDir "BepInEx\plugins\$mod"
        New-Item -ItemType Directory -Force $target | Out-Null
        Copy-Item $dll $target -Force
    }
}

if ($Install) {
    # Перевод лежит рядом с ItemForge: без него ItemForge работает, но без русских текстов.
    $from = Join-Path $PSScriptRoot "release\BepInEx\plugins\ItemForge\LocalizationPatch"
    $to = Join-Path $GameDir "BepInEx\plugins\ItemForge"
    Copy-Item $from $to -Recurse -Force
    Write-Host "Поставлено в $GameDir\BepInEx\plugins"
} else {
    Write-Host "Готово: release\BepInEx\plugins"
}
