# increment-csproj-version.ps1
param(
    [string]$CsprojPath = "SyncRoomChatToolV2.csproj"
)

# .csprojファイルの読み込み
[xml]$xml = Get-Content $CsprojPath

# FileVersionノードの取得
$versionNode = $xml.Project.PropertyGroup.FileVersion
if (-not $versionNode) {
    Write-Error "FileVersionタグが見つかりません。"
    exit 1
}

# バージョン分解
$version = $versionNode
$parts = $version -split '\.'
if ($parts.Count -ne 4) {
    Write-Error "FileVersionの形式が不正です: $version"
    exit 1
}

# 各パートを数値化
[int]$major = $parts[0]
[int]$minor = $parts[1]
[int]$build = $parts[2]
[int]$revision = $parts[3]

# カウントアップ（下位から）
$revision++
if ($revision -gt 9) {
    $revision = 0
    $build++
    if ($build -gt 9) {
        $build = 0
        $minor++
        if ($minor -gt 9) {
            $minor = 0
            $major++
            if ($major -gt 9) {
                $major = 0
            }
        }
    }
}

# 新バージョン文字列
$newVersion = "$major.$minor.$build.$revision"
$versionNode = $newVersion

# 上書き保存
$xml.Save($CsprojPath)

# git add
git add $CsprojPath