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
    [switch]$Dev
)

$ErrorActionPreference = 'Stop'
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

    Step '内核门：契约/语义/core/minimap/paths-win32 + 禁边'
    & $node 'kernel\check-kernel.mjs'
    if ($LASTEXITCODE -ne 0) { throw "check-kernel 失败（$LASTEXITCODE）" }

    # node --test 在受限沙箱会 spawn EPERM，直接逐个执行测试文件
    $tests = Get-ChildItem 'kernel\test\*.test.js' | Sort-Object Name
    $failed = 0
    foreach ($test in $tests) {
        $output = & $node $test.FullName 2>&1
        $pass = ($output | Select-String -Pattern '^ℹ pass (\d+)' | ForEach-Object { $_.Matches[0].Groups[1].Value })
        $fail = ($output | Select-String -Pattern '^ℹ fail (\d+)' | ForEach-Object { $_.Matches[0].Groups[1].Value })
        if ($LASTEXITCODE -ne 0 -or [int]$fail -gt 0) {
            $failed++
            $output | ForEach-Object { Write-Host $_ }
            Write-Host "  ✗ $($test.Name) pass=$pass fail=$fail" -ForegroundColor Red
        } else {
            Write-Host "  ✓ $($test.Name) pass=$pass" -ForegroundColor Green
        }
    }
    if ($failed -gt 0) { throw "$failed 个内核测试套件失败" }

    if (-not (Test-Path (Join-Path $root 'kernel\node_modules'))) {
        throw 'kernel/node_modules 缺失：先执行 pnpm install --node-linker=hoisted --ignore-scripts（见 kernel/SOURCE.md）'
    }

    Step '内核打包：kernel/dist（bundle/css/KaTeX）'
    Push-Location 'kernel'
    try {
        & $node 'build.mjs'
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
}

Step '完成'
