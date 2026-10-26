<#
    叶历 DayLeaf · Windows 客户端构建脚本

    做的事：同步界面 HTML → 准备 WebView2 依赖 → 编译 → 打包到 dist\
    用法：powershell -ExecutionPolicy Bypass -File build.ps1
          powershell -ExecutionPolicy Bypass -File build.ps1 -Version 1.0.1
#>
param(
    [string]$Version = "1.0.0",
    [switch]$SkipPack
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"
Push-Location $PSScriptRoot

try {
    $here      = $PSScriptRoot
    $software  = Split-Path (Split-Path $here -Parent) -Parent
    $htmlSrc   = Join-Path $software "mobile\frontend\app\src\main\assets\www\index.html"
    $xlsxSrc   = Join-Path $software "mobile\frontend\app\src\main\assets\www\xlsx.full.min.js"
    $libDir    = Join-Path $here "lib"
    $resWww    = Join-Path $here "res\www"
    $outDir    = Join-Path $here "bin\Release\net472"

    # ---------- 1. 界面 HTML：单一来源，构建时同步 ----------
    if (-not (Test-Path $htmlSrc)) { throw "找不到移动端界面文件：$htmlSrc" }
    New-Item -ItemType Directory -Force -Path $resWww | Out-Null
    Copy-Item $htmlSrc (Join-Path $resWww "index.html") -Force
    if (Test-Path $xlsxSrc) { Copy-Item $xlsxSrc (Join-Path $resWww "xlsx.full.min.js") -Force }
    $kb = [math]::Round((Get-Item (Join-Path $resWww "index.html")).Length / 1KB)
    Write-Host "· 已同步界面 index.html ($kb KB)" -ForegroundColor DarkGray

    # ---------- 2. WebView2 依赖 ----------
    $wv2Version = "1.0.4314.35"
    $needDeps = -not (Test-Path (Join-Path $libDir "Microsoft.Web.WebView2.Core.dll"))
    if ($needDeps) {
        Write-Host "· 下载 WebView2 SDK $wv2Version ..." -ForegroundColor DarkGray
        $tmp = Join-Path $env:TEMP ("dayleaf-wv2-" + [guid]::NewGuid().ToString("N"))
        New-Item -ItemType Directory -Force -Path $tmp | Out-Null
        $nupkg = Join-Path $tmp "wv2.nupkg"
        $url = "https://nuget.azure.cn/v3-flatcontainer/microsoft.web.webview2/$wv2Version/microsoft.web.webview2.$wv2Version.nupkg"
        Invoke-WebRequest -Uri $url -OutFile $nupkg -UseBasicParsing

        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $zip = Join-Path $tmp "wv2.zip"
        Copy-Item $nupkg $zip
        [System.IO.Compression.ZipFile]::ExtractToDirectory($zip, (Join-Path $tmp "pkg"))

        New-Item -ItemType Directory -Force -Path $libDir | Out-Null
        Copy-Item (Join-Path $tmp "pkg\lib\net462\Microsoft.Web.WebView2.Core.dll")     $libDir -Force
        Copy-Item (Join-Path $tmp "pkg\lib\net462\Microsoft.Web.WebView2.WinForms.dll") $libDir -Force
        Copy-Item (Join-Path $tmp "pkg\runtimes\win-x64\native\WebView2Loader.dll")     $libDir -Force
        Remove-Item -Recurse -Force $tmp
        Write-Host "· WebView2 依赖就绪" -ForegroundColor DarkGray
    }

    # ---------- 3. 编译 ----------
    $dotnet = "dotnet"
    if (Test-Path "C:\Program Files\dotnet\dotnet.exe") { $dotnet = "C:\Program Files\dotnet\dotnet.exe" }
    Write-Host "· 编译 (v$Version) ..." -ForegroundColor DarkGray
    & $dotnet build -c Release "/p:Version=$Version" "/p:FileVersion=$Version.0" "/p:AssemblyVersion=$Version.0"
    if ($LASTEXITCODE -ne 0) { throw "编译失败" }

    # ---------- 4. 打包 ----------
    if ($SkipPack) { return }

    $distRoot = Join-Path $here "dist"
    $pkgName  = "DayLeaf-windows-$Version"
    $pkgDir   = Join-Path $distRoot $pkgName
    if (Test-Path $pkgDir) { Remove-Item -Recurse -Force $pkgDir }
    New-Item -ItemType Directory -Force -Path $pkgDir | Out-Null

    Copy-Item (Join-Path $outDir "DayLeaf.exe")                     $pkgDir -Force
    Copy-Item (Join-Path $outDir "Microsoft.Web.WebView2.Core.dll")     $pkgDir -Force
    Copy-Item (Join-Path $outDir "Microsoft.Web.WebView2.WinForms.dll") $pkgDir -Force
    Copy-Item (Join-Path $outDir "WebView2Loader.dll")             $pkgDir -Force
    if (Test-Path (Join-Path $outDir "DayLeaf.exe.config")) {
        Copy-Item (Join-Path $outDir "DayLeaf.exe.config") $pkgDir -Force
    }

    $zipPath = Join-Path $distRoot "$pkgName.zip"
    if (Test-Path $zipPath) { Remove-Item -Force $zipPath }
    Compress-Archive -Path (Join-Path $pkgDir "*") -DestinationPath $zipPath -CompressionLevel Optimal

    # ---------- 5. 安装包（NSIS） ----------
    $nsi      = Join-Path (Split-Path $here -Parent) "setup\DayLeaf.nsi"
    $setupOut = Join-Path $distRoot "DayLeaf-Setup-$Version.exe"
    if (Test-Path $nsi) {
        $makensis = $null
        foreach ($p in @("${env:ProgramFiles(x86)}\NSIS\makensis.exe",
                         "$env:ProgramFiles\NSIS\makensis.exe")) {
            if (Test-Path $p) { $makensis = $p; break }
        }
        if (-not $makensis) { throw "没找到 makensis.exe，请先安装 NSIS（https://nsis.sourceforge.io）" }

        Write-Host "· 编译安装包（NSIS）..." -ForegroundColor DarkGray
        & $makensis "/DVERSION=$Version" "/DSRCDIR=$pkgDir" "/DOUTFILE=$setupOut" $nsi | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "安装包编译失败" }
        if (-not (Test-Path $setupOut)) { throw "没生成安装包：$setupOut" }
    }

    $exeSize = [math]::Round((Get-Item (Join-Path $pkgDir "DayLeaf.exe")).Length / 1KB)
    $zipSize = [math]::Round((Get-Item $zipPath).Length / 1KB)
    Write-Host ""
    Write-Host "完成：" -ForegroundColor Green
    Write-Host "  程序目录  $pkgDir  (exe $exeSize KB)" -ForegroundColor Gray
    Write-Host "  免安装包  $zipPath  ($zipSize KB)" -ForegroundColor Gray
    if (Test-Path $setupOut) {
        $setupSize = [math]::Round((Get-Item $setupOut).Length / 1KB)
        Write-Host "  安装包    $setupOut  ($setupSize KB)" -ForegroundColor Gray
    }
    Write-Host ""
}
finally {
    Pop-Location
}
