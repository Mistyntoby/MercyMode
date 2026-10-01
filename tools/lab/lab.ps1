# MercyMode lab on Windows.
#
#   .\tools\lab\lab.ps1 test [scenarios]   build, then play scripted battles headless on a throwaway server (default: all)
#   .\tools\lab\lab.ps1 client             build, copy the mod into the lab and open the game on the lab world
#   .\tools\lab\lab.ps1 server             build, then run a normal dedicated server on a lab world (port $Port)
#   .\tools\lab\lab.ps1 join [-Instance 2]  open a game client to join it (Multiplayer > Join via IP > 127.0.0.1, port 7778);
#                                         each -Instance has its own save folder, so two can run side by side
#
# The headless test uses its own folder ($HeadlessDir), never your saves or the client lab. It needs no window and
# tests the battle rules, not how things look; use "client" for that.
param(
	[ValidateSet("test", "client", "server", "join")] [string]$Mode = "test",
	[string]$Scenarios = "all",
	[string]$TmlDir = "C:\Program Files (x86)\Steam\steamapps\common\tModLoader",
	[string]$LabDir = "$env:USERPROFILE\tml-lab",
	[string]$HeadlessDir = "$env:USERPROFILE\tml-lab-headless",
	[int]$Port = 7778,
	[int]$Speed = 8,
	[int]$Instance = 1
)
$ErrorActionPreference = "Stop"
$source = Resolve-Path "$PSScriptRoot\..\.."
$built = "$([Environment]::GetFolderPath('MyDocuments'))\My Games\Terraria\tModLoader\Mods\MercyMode.tmod"

Write-Host "== build"
Push-Location $source
dotnet build -c Release -nologo -v q
if ($LASTEXITCODE -ne 0) { Pop-Location; throw "Build failed" }
Pop-Location

function Install-Mod($dir) {
	New-Item -ItemType Directory -Force "$dir\Mods", "$dir\Worlds" | Out-Null
	Copy-Item $built "$dir\Mods\MercyMode.tmod" -Force
	Set-Content "$dir\Mods\enabled.json" '["MercyMode"]'
}

Set-Location $TmlDir
switch ($Mode) {
	"client" {
		Install-Mod $LabDir
		dotnet tModLoader.dll -tmlsavedirectory $LabDir -skipselect "nick:MercyLab"
	}
	"join" {
		$dir = if ($Instance -le 1) { $LabDir } else { "$LabDir-$Instance" }
		Install-Mod $dir
		# -nosteam: a second copy of the game can run next to the first
		dotnet tModLoader.dll -tmlsavedirectory $dir -nosteam
	}
	"server" {
		Install-Mod $HeadlessDir
		dotnet tModLoader.dll -server -nosteam -tmlsavedirectory $HeadlessDir -autocreate 1 -world "$HeadlessDir\Worlds\MercyLab.wld" -worldname MercyLab -port $Port -players 8
	}
	"test" {
		Install-Mod $HeadlessDir
		Remove-Item "$HeadlessDir\lab-results.txt" -ErrorAction SilentlyContinue
		$env:MERCYMODE_LAB = $Scenarios
		$env:MERCYMODE_LAB_OUT = $HeadlessDir
		$env:MERCYMODE_LAB_SPEED = $Speed
		try {
			dotnet tModLoader.dll -server -nosteam -tmlsavedirectory $HeadlessDir -autocreate 1 -world "$HeadlessDir\Worlds\MercyLab.wld" -worldname MercyLab -port $Port -players 8 *> "$HeadlessDir\server.out"
		} finally {
			Remove-Item Env:MERCYMODE_LAB, Env:MERCYMODE_LAB_OUT, Env:MERCYMODE_LAB_SPEED -ErrorAction SilentlyContinue
		}
		if (Test-Path "$HeadlessDir\lab-results.txt") { Get-Content "$HeadlessDir\lab-results.txt" }
		else { Get-Content "$HeadlessDir\server.out" -Tail 30 }
	}
}
