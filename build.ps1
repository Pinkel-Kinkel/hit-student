# Builds HIT Student into dist\HIT Student and zips it for a GitHub release.
# Needs only Windows (uses the C# compiler that ships with .NET Framework 4.x).
param([string]$InstallTo)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$sdkVersion = '1.0.4314.35'
$sdk = Join-Path $root "build\webview2-$sdkVersion"
$out = Join-Path $root 'dist\HIT Student'
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'

if (-not (Test-Path $sdk)) {
    Write-Host "Downloading Microsoft WebView2 SDK $sdkVersion..."
    New-Item -ItemType Directory -Force $sdk | Out-Null
    $zip = Join-Path $sdk 'webview2.zip'
    Invoke-WebRequest "https://api.nuget.org/v3-flatcontainer/microsoft.web.webview2/$sdkVersion/microsoft.web.webview2.$sdkVersion.nupkg" -OutFile $zip
    Expand-Archive $zip (Join-Path $sdk 'pkg') -Force
}
$lib = Join-Path $sdk 'pkg\lib\net462'

if (-not (Test-Path (Join-Path $root 'assets\app.ico'))) { & (Join-Path $root 'tools\make-icon.ps1') }

Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $out | Out-Null
Copy-Item "$lib\Microsoft.Web.WebView2.Core.dll", "$lib\Microsoft.Web.WebView2.WinForms.dll", (Join-Path $sdk 'pkg\runtimes\win-x64\native\WebView2Loader.dll') $out -Force
# The page the app shows; only this folder is visible to it.
Copy-Item (Join-Path $root 'src\web') (Join-Path $out 'web') -Recurse -Force
# The study skill ships next to the app; the app installs it into ~/.claude/skills on first run.
Copy-Item (Join-Path $root 'skill') (Join-Path $out 'skill') -Recurse -Force

Write-Host 'Compiling...'
$sources = Get-ChildItem (Join-Path $root 'src') -Filter *.cs | ForEach-Object { $_.FullName }
& $csc /nologo /target:winexe /platform:x64 /codepage:65001 /optimize+ `
    "/out:$out\HitStudent.exe" `
    "/win32icon:$(Join-Path $root 'assets\app.ico')" "/win32manifest:$(Join-Path $root 'assets\app.manifest')" `
    /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /r:System.Net.Http.dll /r:System.Security.dll `
    "/r:$out\Microsoft.Web.WebView2.Core.dll" "/r:$out\Microsoft.Web.WebView2.WinForms.dll" `
    $sources
if ($LASTEXITCODE -ne 0) { throw 'Compile failed' }

$zipOut = Join-Path $root 'dist\HIT-Student.zip'
Remove-Item $zipOut -ErrorAction SilentlyContinue
Compress-Archive $out $zipOut
Write-Host "Built: $out"
Write-Host "Zip:   $zipOut"

if ($InstallTo) {
    New-Item -ItemType Directory -Force $InstallTo | Out-Null
    Copy-Item "$out\*" $InstallTo -Recurse -Force
    Write-Host "Installed to: $InstallTo"
}
