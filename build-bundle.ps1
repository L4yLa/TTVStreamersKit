<#
    TTVStreamersKit - AssetBundle の生成

    ChatEmoteParticles のシェーダーとマテリアル、チャット表示の静的フォントアセットを含む AssetBundle を
    ゲームバージョンごとに生成し、src\TTVStreamersKit\Resources\<ゲームバージョン>\ へ出力する。

    AssetBundle は Unity のメジャーバージョンをまたいで読めないため、ゲームと同じ Unity で生成する。

        1.40.8 : Unity 2022.3.33f1 / unity\project
        1.44.1 : Unity 6000.0.40f1 / unity\project-6000

    シェーダー、マテリアル、フォント、文字一覧、BundleExporter.cs の原本は unity\project に置く。
    unity\project-6000 へは生成前にこのスクリプトが .meta ごとコピーする（マテリアルはシェーダーを
    GUID で参照するため .meta も揃える）。unity\project-6000 のこれらのファイルは直接編集しない。

    事前の準備（プロジェクトごとに1回）:
        Unity でプロジェクトを開き、Window > TextMeshPro > Import TMP Essential Resources を実行する。
        取り込んだファイルはリポジトリに含めない（.gitignore 済み）。

    生成物はリポジトリにコミットするため、通常のコード修正では実行不要。
    原本のファイルを変更したときだけ実行する。

    使い方:
        powershell -NoProfile -ExecutionPolicy Bypass -File .\build-bundle.ps1                      # 全バージョン。更新があるときだけ生成
        powershell -NoProfile -ExecutionPolicy Bypass -File .\build-bundle.ps1 -Force               # 全バージョン。常に生成
        powershell -NoProfile -ExecutionPolicy Bypass -File .\build-bundle.ps1 -Target 1.44.1       # 指定バージョンのみ
        powershell -NoProfile -ExecutionPolicy Bypass -File .\build-bundle.ps1 -Target 1.44.1 -UnityExe <path>   # Unity の場所を明示（単一バージョン指定時のみ）
#>

[CmdletBinding()]
param(
    [ValidateSet("All", "1.40.8", "1.44.1")]
    [string]$Target = "All",
    [switch]$Force,
    [string]$UnityExe
)

$ErrorActionPreference = "Stop"

$root          = $PSScriptRoot
$masterProject = Join-Path $root "unity\project"
$resourcesDir  = Join-Path $root "src\TTVStreamersKit\Resources"
$bundleName    = "ttvstreamerskit.bundle"

# ── ゲームバージョンごとの設定 ──────────────────────────────
$targets = [ordered]@{
    "1.40.8" = @{ UnityVersion = "2022.3.33f1"; Project = "unity\project" }
    "1.44.1" = @{ UnityVersion = "6000.0.40f1"; Project = "unity\project-6000" }
}

# 再生成要否の判定に使う原本
$sources = @(
    Join-Path $masterProject "Assets\Shaders\ChatEmoteParticle.shader"
    Join-Path $masterProject "Assets\Materials\ChatEmoteParticle.mat"
    Join-Path $masterProject "Assets\Fonts\BIZUDPGothic-Regular.ttf"
    Join-Path $masterProject "Assets\Fonts\charset.txt"
    Join-Path $masterProject "Assets\Editor\BundleExporter.cs"
)

# unity\project-6000 へコピーするファイル（unity\project からの相対パス）
$syncFiles = @(
    "Assets\Shaders.meta"
    "Assets\Shaders\ChatEmoteParticle.shader"
    "Assets\Shaders\ChatEmoteParticle.shader.meta"
    "Assets\Materials.meta"
    "Assets\Materials\ChatEmoteParticle.mat"
    "Assets\Materials\ChatEmoteParticle.mat.meta"
    "Assets\Fonts.meta"
    "Assets\Fonts\BIZUDPGothic-Regular.ttf"
    "Assets\Fonts\BIZUDPGothic-Regular.ttf.meta"
    "Assets\Fonts\charset.txt"
    "Assets\Fonts\charset.txt.meta"
    "Assets\Fonts\OFL.txt"
    "Assets\Fonts\OFL.txt.meta"
    "Assets\Editor.meta"
    "Assets\Editor\BundleExporter.cs"
    "Assets\Editor\BundleExporter.cs.meta"
)

$selected = if ($Target -eq "All") { @($targets.Keys) } else { @($Target) }

if ($UnityExe -and $selected.Count -ne 1) {
    Write-Error "-UnityExe は -Target で単一のバージョンを指定したときだけ使用できます。"
    exit 1
}

# ── Unity 実行ファイルの解決 ──────────────────────────────
function Resolve-UnityExe([string]$version) {
    if ($UnityExe) { return $UnityExe }

    $candidates = @(
        "C:\Program Files\Unity\Hub\Editor\$version\Editor\Unity.exe"
        "D:\Unity\Hub\Editor\$version\Editor\Unity.exe"
        "U:\Unity-editor\$version\Editor\Unity.exe"
    )
    return $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}

# ── 原本を各プロジェクトへコピー（内容が違うときだけ） ─────────
function Sync-Sources([string]$projectDir) {
    foreach ($rel in $syncFiles) {
        $src = Join-Path $masterProject $rel
        $dst = Join-Path $projectDir $rel

        if (-not (Test-Path $src)) {
            # .meta は Unity で unity\project を開くまで無いことがある
            if ($rel.EndsWith(".meta")) { continue }
            throw "原本が見つかりません: $src"
        }

        if (Test-Path $dst) {
            $srcHash = (Get-FileHash -Path $src -Algorithm SHA256).Hash
            $dstHash = (Get-FileHash -Path $dst -Algorithm SHA256).Hash
            if ($srcHash -eq $dstHash) { continue }
        }
        else {
            $dstDir = Split-Path -Parent $dst
            if (-not (Test-Path $dstDir)) { New-Item -ItemType Directory -Path $dstDir | Out-Null }
        }

        Copy-Item -Path $src -Destination $dst -Force
        Write-Host "  同期: $rel"
    }
}

# ── 1 バージョン分の生成。成功（またはスキップ）なら $true ──────
function Invoke-BundleBuild([string]$gameVersion) {
    $cfg          = $targets[$gameVersion]
    $unityVersion = $cfg.UnityVersion
    $projectDir   = Join-Path $root $cfg.Project
    $bundle       = Join-Path $resourcesDir "$gameVersion\$bundleName"
    $logFile      = Join-Path $root "unity-build-$gameVersion.log"

    Write-Host ""
    Write-Host "── [$gameVersion] Unity $unityVersion / $($cfg.Project) ──" -ForegroundColor Cyan

    if (-not (Test-Path (Join-Path $projectDir "ProjectSettings"))) {
        if (Test-Path $bundle) {
            Write-Warning "[$gameVersion] $($cfg.Project) がありません。既存の AssetBundle を使用します。"
            return $true
        }
        Write-Host "[$gameVersion] $($cfg.Project) が無く、AssetBundle もありません。" -ForegroundColor Red
        return $false
    }

    # 再生成が必要かどうか
    $needsBuild = $Force -or -not (Test-Path $bundle)
    if (-not $needsBuild) {
        $bundleTime = (Get-Item $bundle).LastWriteTime
        foreach ($src in $sources) {
            if ((Test-Path $src) -and ((Get-Item $src).LastWriteTime -gt $bundleTime)) {
                $needsBuild = $true
                break
            }
        }
    }

    if (-not $needsBuild) {
        Write-Host "[$gameVersion] AssetBundle は最新です。生成をスキップします。" -ForegroundColor Green
        return $true
    }

    # Unity が無い環境ではスキップする（生成物はコミットされているため MOD 本体はビルドできる）
    $exe = Resolve-UnityExe $unityVersion
    if (-not $exe -or -not (Test-Path $exe)) {
        if (Test-Path $bundle) {
            Write-Warning "[$gameVersion] Unity $unityVersion が見つかりません。既存の AssetBundle を使用します。"
            Write-Warning "[$gameVersion] 原本を変更した場合、その変更は反映されません。"
            return $true
        }
        Write-Host "[$gameVersion] Unity $unityVersion が見つからず、AssetBundle もありません。" -ForegroundColor Red
        Write-Host "  Unity Hub から $unityVersion を導入するか、-UnityExe でパスを指定してください。" -ForegroundColor Red
        return $false
    }

    # 原本の同期（unity\project 自身は対象外）
    if ((Resolve-Path $projectDir).Path -ne (Resolve-Path $masterProject).Path) {
        Sync-Sources $projectDir
    }

    Write-Host "[$gameVersion] AssetBundle を生成します ..."
    Write-Host "  Unity: $exe"

    # Unity.exe は GUI アプリのため、パイプしないと終了を待たずに戻ってくる。
    # 関数の戻り値に標準出力が混ざらないよう Out-Null で捨てる（ログは -logFile 側）。
    & $exe -batchmode -quit -nographics `
        -projectPath $projectDir `
        -executeMethod TTVStreamersKit.EditorTools.BundleExporter.Build `
        -logFile $logFile | Out-Null

    if ($LASTEXITCODE -ne 0) {
        Write-Host "[$gameVersion] Unity のバッチ実行に失敗しました (exit $LASTEXITCODE)。詳細は $logFile を確認してください。" -ForegroundColor Red
        return $false
    }

    if (-not (Test-Path $bundle)) {
        Write-Host "[$gameVersion] AssetBundle が生成されませんでした。詳細は $logFile を確認してください。" -ForegroundColor Red
        return $false
    }

    $size = (Get-Item $bundle).Length
    Write-Host "[$gameVersion] 生成しました: $bundle ($('{0:N0}' -f $size) bytes)" -ForegroundColor Green
    return $true
}

# ── 実行 ──────────────────────────────────────────────
$failed = @()
foreach ($gameVersion in $selected) {
    $ok = $false
    try {
        $ok = Invoke-BundleBuild $gameVersion
    }
    catch {
        Write-Host "[$gameVersion] エラー: $($_.Exception.Message)" -ForegroundColor Red
        $ok = $false
    }
    if (-not $ok) { $failed += $gameVersion }
}

Write-Host ""
if ($failed.Count -gt 0) {
    Write-Host "失敗: $($failed -join ', ')" -ForegroundColor Red
    exit 1
}
Write-Host "完了: $($selected -join ', ')" -ForegroundColor Green
exit 0
