<#
    ゲームと MOD の DLL から、指定した型のメンバーの一覧（シグネチャ）を書き出す開発用のスクリプト。
    ゲームの版ごとに型や引数が違う箇所（選曲画面の操作など）を確かめるために使う。
    DLL を読み込まず、Mono.Cecil でメタデータだけを読む（ゲームのフォルダーにある Mono.Cecil.dll を使う）。

    使い方:
        powershell -NoProfile -ExecutionPolicy Bypass -File tools\dump-game-types.ps1 -GameDir "<Beat Saber のフォルダー>" -Out "<出力ファイル>"
#>
param(
    [Parameter(Mandatory = $true)][string]$GameDir,
    [Parameter(Mandatory = $true)][string]$Out
)

$ErrorActionPreference = "Stop"

$cecil = Get-ChildItem -Path $GameDir -Filter "Mono.Cecil.dll" -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $cecil) { throw "Mono.Cecil.dll が $GameDir にありません" }
Add-Type -Path $cecil.FullName

$managed = Join-Path $GameDir "Beat Saber_Data\Managed"
$plugins = Join-Path $GameDir "Plugins"

# 調べる型（アセンブリのファイル名 → 型名の一部。前方一致ではなく、名前に含むものすべて）
$targets = [ordered]@{
    (Join-Path $managed "Main.dll") = @(
        "MainFlowCoordinator", "SoloFreePlayFlowCoordinator", "LevelSelectionFlowCoordinator",
        "LevelSelectionNavigationController", "LevelCollectionNavigationController", "LevelFilteringNavigationController",
        "SelectLevelCategoryViewController", "LevelCollectionViewController", "LevelCollectionTableView",
        "LevelSearchViewController", "StandardLevelDetailViewController", "StandardLevelDetailView",
        "CustomLevelPathHelper", "BeatmapLevelsModel", "AnnotatedBeatmapLevelCollectionsViewController"
    )
    (Join-Path $plugins "BetterSongList.dll") = @("Filter", "Sort", "Search", "Ui", "UI")
    (Join-Path $plugins "SongBrowser.dll") = @("Filter", "SongBrowserModel", "SongBrowserUI")
}
# 型名が完全一致で必要なもの（Main.dll 以外のアセンブリにあることがある）
$exactTypes = @("BeatmapLevel", "BeatmapKey", "BeatmapLevelPack", "BeatmapCharacteristicSO", "BeatmapDifficulty")

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add("# GameDir: $GameDir")
$verFile = Join-Path $GameDir "BeatSaberVersion.txt"
if (Test-Path $verFile) { $lines.Add("# Version: $((Get-Content $verFile -Raw).Trim())") }

function Format-Method($m) {
    $ps = ($m.Parameters | ForEach-Object { "$($_.ParameterType.FullName) $($_.Name)" }) -join ", "
    $mods = @()
    if ($m.IsPublic) { $mods += "public" } elseif ($m.IsFamily) { $mods += "protected" } elseif ($m.IsAssembly) { $mods += "internal" } else { $mods += "private" }
    if ($m.IsStatic) { $mods += "static" }
    if ($m.IsVirtual) { $mods += "virtual" }
    return "    $($mods -join ' ') $($m.ReturnType.FullName) $($m.Name)($ps)"
}

function Dump-Type($t) {
    $lines.Add("")
    $base = if ($t.BaseType) { " : $($t.BaseType.FullName)" } else { "" }
    $lines.Add("type $($t.FullName)$base")
    foreach ($f in $t.Fields) {
        $mod = if ($f.IsPublic) { "public" } elseif ($f.IsFamily) { "protected" } elseif ($f.IsAssembly) { "internal" } else { "private" }
        $st = if ($f.IsStatic) { " static" } else { "" }
        $lines.Add("    field $mod$st $($f.FieldType.FullName) $($f.Name)")
    }
    foreach ($p in $t.Properties) {
        $lines.Add("    property $($p.PropertyType.FullName) $($p.Name)")
    }
    foreach ($e in $t.Events) {
        $lines.Add("    event $($e.EventType.FullName) $($e.Name)")
    }
    foreach ($m in $t.Methods) {
        if ($m.IsGetter -or $m.IsSetter -or $m.IsAddOn -or $m.IsRemoveOn) { continue }
        $lines.Add((Format-Method $m))
    }
    if ($t.IsEnum) {
        $lines.Add("    values: " + (($t.Fields | Where-Object { $_.IsStatic } | ForEach-Object { $_.Name }) -join ", "))
    }
    foreach ($n in $t.NestedTypes) { Dump-Type $n }
}

$resolver = New-Object Mono.Cecil.DefaultAssemblyResolver
$resolver.AddSearchDirectory($managed)
$resolver.AddSearchDirectory($plugins)
$resolver.AddSearchDirectory((Join-Path $GameDir "Libs"))
$params = New-Object Mono.Cecil.ReaderParameters
$params.AssemblyResolver = $resolver

foreach ($path in $targets.Keys) {
    if (-not (Test-Path $path)) { $lines.Add(""); $lines.Add("## (none) $path"); continue }
    $asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($path, $params)
    $lines.Add(""); $lines.Add("## $($asm.FullName)")
    $names = $targets[$path]
    foreach ($t in $asm.MainModule.Types) {
        $hit = ($names | Where-Object { $t.Name -like "*$_*" }) -or ($exactTypes -contains $t.Name)
        if ($hit) { Dump-Type $t }
    }
}

# 完全一致の型が Main.dll 以外にあれば探す
foreach ($dll in Get-ChildItem -Path $managed -Filter "*.dll") {
    if ($dll.Name -eq "Main.dll") { continue }
    try { $asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($dll.FullName, $params) } catch { continue }
    foreach ($t in $asm.MainModule.Types) {
        if ($exactTypes -contains $t.Name) { $lines.Add(""); $lines.Add("## $($asm.Name.Name)"); Dump-Type $t }
    }
}

$dir = Split-Path -Parent $Out
if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir | Out-Null }
[System.IO.File]::WriteAllLines($Out, $lines, (New-Object System.Text.UTF8Encoding($false)))
Write-Host "書き出しました: $Out ($($lines.Count) 行)"
