# Live test of the ClearSteps guardian on this PC. Run elevated. Breaks settings on purpose and watches them heal.
$ErrorActionPreference = 'Continue'
$root = Split-Path $PSScriptRoot -Parent
$exe  = Join-Path $root 'dist\ClearSteps.exe'
$cli  = Join-Path $root 'build\cs-cli.exe'
$out  = Join-Path $root 'build\guardian-test.log'
$glog = Join-Path $env:LOCALAPPDATA 'ClearSteps\guardian.log'
function Say($s) { $line = "$(Get-Date -Format HH:mm:ss)  $s"; Add-Content $out $line; Write-Host $line }
function Audit($label) { Say "--- audit: $label"; & $cli audit | ForEach-Object { Say "    $_" } }
function App { Get-Process ClearSteps -ErrorAction SilentlyContinue | Select-Object -First 1 }
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class W { [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l); }
'@
Set-Content $out "ClearSteps guardian live test $(Get-Date)"

Get-Process ClearSteps -ErrorAction SilentlyContinue | Stop-Process -Force; Start-Sleep 2
$mark = (Get-Content $glog -ErrorAction SilentlyContinue | Measure-Object).Count

Say "1. start in tray mode"
Start-Process $exe -ArgumentList '--tray'; Start-Sleep 12
$p = App; Say "   running=$([bool]$p)  window visible=$($p.MainWindowHandle -ne 0)  (expected: running, not visible)"
Audit 'baseline'

Say "2. fake game start (a copy of ping.exe named cod.exe, 75 s)"
$fake = Join-Path $env:TEMP 'cstest\cod.exe'; New-Item -ItemType Directory -Force (Split-Path $fake) | Out-Null
Copy-Item "$env:WINDIR\System32\PING.EXE" $fake -Force
$game = Start-Process $fake -ArgumentList '-n 75 127.0.0.1' -WindowStyle Hidden -PassThru
Start-Sleep 25

Say "3. break everything while the game runs (format, old device, default device, Voicemeeter, tune line, Equalizer APO)"
& $cli break x eapo | ForEach-Object { Say "    $_" }
Audit 'right after breaking'
Start-Sleep 40
Audit '40 s later, game still running (expected: all fixed except eapo, which waits for the game to close)'

Say "4. wait for the fake game to close, then give the guardian 50 s"
$game.WaitForExit(); Start-Sleep 50
Audit 'after game closed (expected: everything OK)'

Say "5. second launch should bring the hidden window back"
Start-Process $exe; Start-Sleep 5
$p = App; Say "   window visible=$($p.MainWindowHandle -ne 0)  (expected: visible)  processes=$((Get-Process ClearSteps).Count) (expected: 1)"

Say "6. close button should hide to tray, not exit"
[void][W]::PostMessage($p.MainWindowHandle, 0x0112, [IntPtr]0xF060, [IntPtr]::Zero); Start-Sleep 4
$p = App; Say "   still running=$([bool]$p)  window visible=$($p.MainWindowHandle -ne 0)  (expected: running, not visible)"

Say "7. Start with Windows switch"
$a = Start-Process $exe -ArgumentList '--autostart','on' -Wait -PassThru
$task = schtasks /query /tn 'ClearSteps Guardian' /fo list /v 2>$null | Select-String 'Task To Run|Run As User|Logon Mode|Schedule Type'
Say "   on: exit=$($a.ExitCode)"; $task | ForEach-Object { Say "    $($_.Line.Trim())" }
$b = Start-Process $exe -ArgumentList '--autostart','off' -Wait -PassThru
schtasks /query /tn 'ClearSteps Guardian' 2>$null | Out-Null
Say "   off: exit=$($b.ExitCode)  task still exists=$($LASTEXITCODE -eq 0)  (expected: False)"

Say "--- guardian's own log during the test"
Get-Content $glog | Select-Object -Skip $mark | ForEach-Object { Say "    $_" }
Remove-Item (Split-Path $fake) -Recurse -Force -ErrorAction SilentlyContinue
Say "DONE"
