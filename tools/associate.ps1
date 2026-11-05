# Markdown 文件关联（用户级 HKCU，无需管理员）：与壳内 --install-association 同语义。
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\associate.ps1 -Status
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\associate.ps1 -Install -Exe dist\MDPreview\MDPreview.exe
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\associate.ps1 -Uninstall
# 安装前把原默认值备份到 HKCU\Software\MDPreview\AssociationBackup，卸载时还原，不劫持用户原有选择。
param(
    [switch]$Install,
    [switch]$Uninstall,
    [switch]$Status,
    [string]$Exe = ''
)

$progId = 'MDPreview.Markdown'
$progIdLabel = 'Markdown 文档'
$extensions = @('.md', '.markdown', '.mdown', '.mkd', '.mdx')
$classes = 'HKCU:\Software\Classes'
$backupRoot = 'HKCU:\Software\MDPreview\AssociationBackup'

if (-not $Status) { $Status = -not ($Install -or $Uninstall) }

if ([string]::IsNullOrWhiteSpace($Exe)) {
    $Exe = Join-Path (Split-Path -Parent $PSScriptRoot) 'dist\MDPreview\MDPreview.exe'
}
$Exe = [System.IO.Path]::GetFullPath($Exe)

function Get-Default([string]$path) {
    $item = Get-Item -LiteralPath $path -ErrorAction SilentlyContinue
    if ($null -eq $item) { return '' }
    $value = $item.GetValue('')
    if ($null -eq $value) { return '' }
    return [string]$value
}

if ($Status) {
    Write-Output ("exe      {0} (exists={1})" -f $Exe, (Test-Path $Exe))
    Write-Output ("progId   {0} (registered={1})" -f $progId, (Test-Path "$classes\$progId"))
    foreach ($ext in $extensions) {
        $current = Get-Default "$classes\$ext"
        $backup = Get-Default "$backupRoot\$ext"
        if ([string]::IsNullOrEmpty($current)) { $current = '(无)' }
        if ([string]::IsNullOrEmpty($backup)) { $backup = '(无)' }
        Write-Output ("{0,-11} current={1}  backup={2}" -f $ext, $current, $backup)
    }
    exit 0
}

if ($Install) {
    if (-not (Test-Path $Exe)) { Write-Error "找不到 exe：$Exe"; exit 1 }

    New-Item -Path "$classes\$progId" -Force | Out-Null
    Set-ItemProperty -Path "$classes\$progId" -Name '(default)' -Value $progIdLabel
    New-Item -Path "$classes\$progId\DefaultIcon" -Force | Out-Null
    Set-ItemProperty -Path "$classes\$progId\DefaultIcon" -Name '(default)' -Value "`"$Exe`",0"
    New-Item -Path "$classes\$progId\shell\open\command" -Force | Out-Null
    Set-ItemProperty -Path "$classes\$progId\shell\open\command" -Name '(default)' -Value "`"$Exe`" `"%1`""

    foreach ($ext in $extensions) {
        $previous = Get-Default "$classes\$ext"
        # 只在首次安装时记录原始默认值；重复安装不能把「自己的」ProgId 备份成「原值」
        if (-not (Test-Path "$backupRoot\$ext")) {
            New-Item -Path "$backupRoot\$ext" -Force | Out-Null
            Set-ItemProperty -Path "$backupRoot\$ext" -Name '(default)' -Value $previous
        } else {
            $previous = Get-Default "$backupRoot\$ext"
        }

        New-Item -Path "$classes\$ext" -Force | Out-Null
        Set-ItemProperty -Path "$classes\$ext" -Name '(default)' -Value $progId
        New-Item -Path "$classes\$ext\OpenWithProgids" -Force | Out-Null
        New-ItemProperty -Path "$classes\$ext\OpenWithProgids" -Name $progId -Value ([byte[]]@()) -PropertyType None -Force | Out-Null

        $shown = if ([string]::IsNullOrEmpty($previous)) { '(无)' } else { $previous }
        Write-Output ("{0}: {1} -> {2}" -f $ext, $shown, $progId)
    }
    Write-Output '已写入关联（HKCU，无需管理员）。'
    exit 0
}

if ($Uninstall) {
    foreach ($ext in $extensions) {
        $current = Get-Default "$classes\$ext"
        $previous = Get-Default "$backupRoot\$ext"
        if ($current -eq $progId) {
            # 默认值真实名称是空串，PowerShell 的 '(default)' 只能设置不能删，这里走 .NET
            $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey("Software\Classes\$ext", $true)
            if ($null -ne $key) {
                if (-not [string]::IsNullOrEmpty($previous) -and $previous -ne $progId) {
                    $key.SetValue('', $previous)
                } else {
                    $key.DeleteValue('', $false)
                }
                $key.Close()
            }
        }
        Remove-ItemProperty -Path "$classes\$ext\OpenWithProgids" -Name $progId -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath "$backupRoot\$ext" -Recurse -Force -ErrorAction SilentlyContinue
        $shown = if ([string]::IsNullOrEmpty($previous)) { '(无)' } else { $previous }
        Write-Output ("{0}: -> {1}" -f $ext, $shown)
    }
    Remove-Item -LiteralPath "$classes\$progId" -Recurse -Force -ErrorAction SilentlyContinue
    Write-Output '已撤销关联并还原原默认值。'
    exit 0
}
