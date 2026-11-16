# Builds ClearSteps.exe with the C# compiler that ships in Windows (no SDK needed).
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
New-Item -ItemType Directory -Force dist | Out-Null
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$src = 'src\Audio.cs','src\Core.cs','src\Eapo.cs','src\Voicemeeter.cs','src\Setup.cs','src\Guardian.cs','src\Ui.cs','src\MainForm.cs'
& $csc /nologo /optimize+ /target:winexe /platform:x64 /out:dist\ClearSteps.exe `
    /win32manifest:src\app.manifest /resource:catalog.json,catalog.json `
    /r:System.Web.Extensions.dll /r:System.ServiceProcess.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll $src
if ($LASTEXITCODE -ne 0) { throw "build failed" }
Get-Item dist\ClearSteps.exe | Select-Object Name, Length, LastWriteTime
