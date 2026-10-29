#Requires -RunAsAdministrator
param(
   [string] $Path = 'C:\LabShare',
   [string] $Corp = 'CORP',
   [string] $Partner = 'PARTNER'
)

$ErrorActionPreference = 'Stop'
$password = Read-Host 'Password for the local lab user' -AsSecureString
if (-not (Get-LocalUser -Name 'LocalAuditor' -ErrorAction SilentlyContinue)) {
   New-LocalUser -Name 'LocalAuditor' -Password $password | Out-Null
}
if (-not (Get-LocalGroup -Name 'LocalOps' -ErrorAction SilentlyContinue)) {
   New-LocalGroup -Name 'LocalOps' | Out-Null
   Add-LocalGroupMember -Group 'LocalOps' -Member 'LocalAuditor', "$Corp\alice"
}

$folders = @{
   'Public'  = @('*S-1-5-32-545:(OI)(CI)RX', "$Corp\bob:(OI)(CI)RX")
   'Finance' = @("$Corp\Lab Editors:(OI)(CI)M", "$env:COMPUTERNAME\LocalOps:(OI)(CI)F", '*S-1-5-32-544:(OI)(CI)F')
   'Partner' = @("$Partner\Partner Staff:(OI)(CI)RX", '*S-1-5-21-1-2-3-4242:(OI)(CI)R')
   'Admin'   = @("*$((Get-LocalUser | Where-Object { $_.SID.Value -like '*-500' }).SID.Value):(OI)(CI)F")
}

foreach ($folder in $folders.Keys) {
   $target = New-Item -ItemType Directory -Force (Join-Path $Path $folder)
   foreach ($grant in $folders[$folder]) {
      icacls $target.FullName /grant $grant | Out-Null
      if ($LASTEXITCODE) { throw "icacls failed for $grant on $folder" }
   }
}

if (-not (Get-SmbShare -Name 'LabShare' -ErrorAction SilentlyContinue)) {
   New-SmbShare -Name 'LabShare' -Path $Path -FullAccess 'Everyone' | Out-Null
}
Write-Host "\\$env:COMPUTERNAME\LabShare is ready"
