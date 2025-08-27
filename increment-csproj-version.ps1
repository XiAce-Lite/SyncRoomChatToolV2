# increment-csproj-version.ps1
param(
    [string]$CsprojPath = "SyncRoomChatToolV2.csproj"
)

# .csproj�t�@�C���̓ǂݍ���
[xml]$xml = Get-Content $CsprojPath

# FileVersion�m�[�h�̎擾
$versionNode = $xml.Project.PropertyGroup.FileVersion
if (-not $versionNode) {
    Write-Error "FileVersion�^�O��������܂���B"
    exit 1
}

# �o�[�W��������
$version = $versionNode
$parts = $version -split '\.'
if ($parts.Count -ne 4) {
    Write-Error "FileVersion�̌`�����s���ł�: $version"
    exit 1
}

# �e�p�[�g�𐔒l��
[int]$major = $parts[0]
[int]$minor = $parts[1]
[int]$build = $parts[2]
[int]$revision = $parts[3]

# �J�E���g�A�b�v�i���ʂ���j
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

# �V�o�[�W����������
$newVersion = "$major.$minor.$build.$revision"
$versionNode = $newVersion

# �㏑���ۑ�
$xml.Save($CsprojPath)

# git add
git add $CsprojPath