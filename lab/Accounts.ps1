#Requires -RunAsAdministrator
param(
   [Parameter(Mandatory)] [ValidateSet('Corp', 'Partner')] [string] $Role,
   [string] $Partner = 'partner.lab'
)

$ErrorActionPreference = 'Stop'
Import-Module ActiveDirectory
$password = Read-Host 'Password for the lab users' -AsSecureString

function NewLabUser([string] $Name, [bool] $Enabled = $true) {
   if (-not (Get-ADUser -Filter "SamAccountName -eq '$Name'")) {
      New-ADUser -Name $Name -SamAccountName $Name -AccountPassword $password -Enabled $Enabled
   }
}

function NewLabGroup([string] $Name, [string] $Scope) {
   if (-not (Get-ADGroup -Filter "Name -eq '$Name'")) {
      New-ADGroup -Name $Name -GroupScope $Scope -GroupCategory Security
   }
}

if ($Role -eq 'Partner') {
   NewLabUser 'pat'
   NewLabGroup 'Partner Staff' Global
   Add-ADGroupMember 'Partner Staff' -Members 'pat'
   return
}

NewLabUser 'alice'
NewLabUser 'bob' $false
NewLabGroup 'Lab Readers' Global
NewLabGroup 'Lab Editors' DomainLocal
Add-ADGroupMember 'Lab Readers' -Members 'alice'
Add-ADGroupMember 'Lab Editors' -Members 'Lab Readers'
Add-ADGroupMember 'Lab Editors' -Members (Get-ADGroup 'Partner Staff' -Server $Partner)
