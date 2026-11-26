# 用 Windows 自带的 csc 编译（无需安装 Visual Studio / .NET SDK）
# 用法：powershell -ExecutionPolicy Bypass -File build.ps1

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$src  = Join-Path $root 'src'
$dist = Join-Path $root 'dist'
$icon = Join-Path $root 'assets\icon.ico'
$exe  = Join-Path $dist '肥鱼阅读器.exe'

New-Item -ItemType Directory -Force -Path $dist | Out-Null

$fx  = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$wpf = Join-Path $fx 'WPF'
$csc = Join-Path $fx 'csc.exe'
if (-not (Test-Path $csc)) { throw "找不到 csc.exe：$csc（需要 Windows 自带的 .NET Framework 4.x）" }

$files = @(
    (Join-Path $src 'TxtReader.cs'),
    (Join-Path $src 'TextExtract.cs'),
    (Join-Path $src 'ImageCanvas.cs'),
    (Join-Path $src 'WebSearch.cs'),
    (Join-Path $src 'History.cs'),
    (Join-Path $src 'BufferedTextBox.cs')
)

& $csc '/nologo' '/target:winexe' '/platform:anycpu' '/optimize+' "/win32icon:$icon" "/out:$exe" `
    /r:System.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll `
    /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll `
    /r:System.Web.Extensions.dll `
    "/r:$(Join-Path $fx 'System.Xaml.dll')" `
    "/r:$(Join-Path $wpf 'WindowsBase.dll')" `
    "/r:$(Join-Path $wpf 'PresentationCore.dll')" `
    $files

if ($LASTEXITCODE -ne 0) { throw "编译失败（csc 退出码 $LASTEXITCODE）" }
Write-Host "编译完成：$exe"
Get-Item $exe | Select-Object Name, @{n='KB';e={[math]::Round($_.Length/1KB,1)}}

