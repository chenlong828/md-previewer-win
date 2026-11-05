# MDPreview for Windows 构建脚本
#
#   pwsh ./build.ps1                 # 全量：内核门 → 壳 Release → 绿色目录
#   pwsh ./build.ps1 -SkipKernel     # 只编壳（内核产物已在 kernel/dist）
#   pwsh ./build.ps1 -SkipGates      # 跳过内核门（仅构建）
#   pwsh ./build.ps1 -Dev            # Debug 构建，输出到 src/.../bin（不做绿色目录）
#
# 产物：dist/MDPreview/（self-contained，含 web/ 与 portable.flag，可整体拷贝运行）
param(
    [switch]$SkipKernel,
    [switch]$SkipGates,
    [switch]$SkipTests,
    [switch]$Dev
)

# 原生工具（node/esbuild/dotnet）会把进度写到 stderr，PowerShell 会当成错误记录；
# 这里不因 stderr 中断，失败一律靠 $LASTEXITCODE 显式判定后 throw。
$ErrorActionPreference = 'Continue'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

# 依赖目录固定在仓库内：受限沙箱下只有仓库可写
$env:DOTNET_CLI_HOME = Join-Path $root '.dotnet-home'
$env:NUGET_PACKAGES = Join-Path $root '.nuget'

$configuration = if ($Dev) { 'Debug' } else { 'Release' }
$dist = Join-Path $root 'dist\MDPreview'
$web = Join-Path $root 'kernel\dist'

function Step($text) { Write-Host "`n==> $text" -ForegroundColor Cyan }

function Require-Node {
    $node = Get-Command node -ErrorAction SilentlyContinue
    if (-not $node) { throw '找不到 node（内核构建需要；本机可用 ELECTRON_RUN_AS_NODE 提供的 node）' }
    return $node.Source
}

# ---------------------------------------------------------------- 内核

if (-not $SkipKernel) {
    $node = Require-Node
    $env:ELECTRON_RUN_AS_NODE = '1'

    if (-not $SkipGates) {
        Step '内核门：契约/语义/core/minimap/paths-win32 + 禁边'
        & $node 'kernel\check-kernel.mjs' 2>&1 | ForEach-Object { Write-Host $_ }
        if ($LASTEXITCODE -ne 0) { throw "check-kernel 失败（$LASTEXITCODE）" }

        # node --test 在受限沙箱会 spawn EPERM，直接逐个执行测试文件
        $tests = Get-ChildItem 'kernel\test\*.test.js' | Sort-Object Name
        $failed = 0
        foreach ($test in $tests) {
            $output = & $node $test.FullName 2>&1
            $pass = ($output | Select-String -Pattern 'pass (\d+)' | ForEach-Object { $_.Matches[0].Groups[1].Value })
            $fail = ($output | Select-String -Pattern 'fail (\d+)' | ForEach-Object { $_.Matches[0].Groups[1].Value })
            if ($LASTEXITCODE -ne 0 -or [int]$fail -gt 0) {
                $failed++
                $output | ForEach-Object { Write-Host $_ }
                Write-Host "  x $($test.Name) pass=$pass fail=$fail" -ForegroundColor Red
            } else {
                Write-Host "  ok $($test.Name) pass=$pass" -ForegroundColor Green
            }
        }
        if ($failed -gt 0) { throw "$failed 个内核测试套件失败" }

        Step '语言表门：resources/languages.json 与内核语法一致'
        & $node 'tools\check-languages.mjs' 2>&1 | ForEach-Object { Write-Host $_ }
        if ($LASTEXITCODE -ne 0) { throw "check-languages 失败（$LASTEXITCODE）" }
    }

    if (-not (Test-Path (Join-Path $root 'kernel\node_modules'))) {
        throw 'kernel/node_modules 缺失：先执行 pnpm install --node-linker=hoisted --ignore-scripts（见 kernel/SOURCE.md）'
    }

    Step '内核打包：kernel/dist（bundle/css/KaTeX）'
    Push-Location 'kernel'
    try {
        & $node 'build.mjs' 2>&1 | ForEach-Object { Write-Host $_ }
        if ($LASTEXITCODE -ne 0) { throw "kernel build 失败（$LASTEXITCODE）" }
    } finally {
        Pop-Location
    }
} elseif (-not (Test-Path (Join-Path $web 'index.html'))) {
    throw "kernel/dist 缺失（index.html）；去掉 -SkipKernel 重新构建"
}

# ---------------------------------------------------------------- 壳

if ($Dev) {
    Step "壳：dotnet build ($configuration)"
    dotnet build 'src\MDPreview.Win\MDPreview.Win.csproj' -c $configuration --nologo
    if ($LASTEXITCODE -ne 0) { throw 'dotnet build 失败' }
} else {
    Step "壳：dotnet publish self-contained ($configuration, win-x64)"
    if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
    dotnet publish 'src\MDPreview.Win\MDPreview.Win.csproj' -c $configuration -r win-x64 --self-contained true `
        -p:PublishReadyToRun=false -o $dist --nologo
    if ($LASTEXITCODE -ne 0) { throw 'dotnet publish 失败' }

    Step '组装绿色目录：portable.flag + 版本说明'
    New-Item -ItemType File -Path (Join-Path $dist 'portable.flag') -Force | Out-Null
    $exe = Join-Path $dist 'MDPreview.exe'
    $size = [math]::Round((Get-ChildItem $dist -Recurse -File | Measure-Object Length -Sum).Sum / 1MB, 1)
    Write-Host "  产物：$dist（$size MB）"

    if (-not (Test-Path $exe)) { throw "缺少 $exe" }
    if (-not (Test-Path (Join-Path $dist 'web\index.html'))) { throw '缺少 web/index.html（内核产物未随包）' }

    Step '便携 zip：dist/MDPreview-<version>-win-x64-portable.zip'
    $version = (Get-Item $exe).VersionInfo.ProductVersion
    if ([string]::IsNullOrWhiteSpace($version)) { $version = '0.0.0' }
    $version = ($version -split '\+')[0]
    $zip = Join-Path $root "dist\MDPreview-$version-win-x64-portable.zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    # 用系统自带 bsdtar 打包：条目分隔符是 '/'，且 Win10 1803+ / Win11 都自带；
    # .NET Framework 的 ZipFile 会把分隔符写成 '\'，跨平台解压工具会当成文件名的一部分。
    $tar = Get-Command tar.exe -ErrorAction SilentlyContinue
    if (-not $tar) { throw '找不到 tar.exe（Win10 1803+ 自带；缺失请手动 zip 打包）' }
    Push-Location $dist
    try { & $tar.Source -a -c -f $zip * 2>&1 | ForEach-Object { Write-Host "    $_" } }
    finally { Pop-Location }
    if ($LASTEXITCODE -ne 0) { throw "tar 打包失败（$LASTEXITCODE）" }
    $zipStream.Dispose()
    $zipSize = [math]::Round((Get-Item $zip).Length / 1MB, 1)
    Write-Host "  压缩包：$zip（$zipSize MB）"

    # 解压即用自检：把 zip 释放到临时目录，跑 --diag info，确认内核与语言表都在包里
    $smoke = Join-Path $root 'dist\.smoke'
    if (Test-Path $smoke) { Remove-Item $smoke -Recurse -Force }
    [System.IO.Compression.ZipFile]::ExtractToDirectory($zip, $smoke)
    $smokeExe = Join-Path $smoke 'MDPreview.exe'
    $info = & $smokeExe --diag info 2>&1 | Out-String
    Write-Host ($info.Trim() -replace '(?m)^', '    ')
    if ($LASTEXITCODE -ne 0 -or $info -notmatch 'kernel=True' -or $info -notmatch 'languages  44') {
        throw "便携包自检失败（exit=$LASTEXITCODE）"
    }
    if (-not (Test-Path (Join-Path $smoke 'portable.flag'))) { throw '便携包缺少 portable.flag' }
    Remove-Item $smoke -Recurse -Force
}

# ---------------------------------------------------------------- 单元测试（xunit v3 / MTP）

if (-not $SkipTests) {
    Step '单元测试：TextFile / AppSettings'
    dotnet build 'tests\MDPreview.Win.Tests\MDPreview.Win.Tests.csproj' -c $configuration --nologo 2>&1 |
        ForEach-Object { Write-Host $_ }
    if ($LASTEXITCODE -ne 0) { throw '测试项目编译失败' }

    $testExe = Get-ChildItem 'tests\MDPreview.Win.Tests\bin' -Recurse -Filter 'MDPreview.Win.Tests.exe' |
        Where-Object { $_.FullName -like "*$configuration*" } | Select-Object -First 1
    if (-not $testExe) { throw '找不到测试可执行文件' }

    & $testExe.FullName 2>&1 | ForEach-Object { Write-Host $_ }
    if ($LASTEXITCODE -ne 0) { throw "单元测试失败（$LASTEXITCODE）" }
}

Step '完成'
