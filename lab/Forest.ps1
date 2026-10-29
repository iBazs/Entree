#Requires -RunAsAdministrator
param(
   [Parameter(Mandatory)] [string] $DomainName,
   [Parameter(Mandatory)] [string] $NetBiosName
)

$ErrorActionPreference = 'Stop'
Install-WindowsFeature AD-Domain-Services -IncludeManagementTools | Out-Null
Install-ADDSForest -DomainName $DomainName -DomainNetbiosName $NetBiosName -InstallDns -Force `
   -SafeModeAdministratorPassword (Read-Host 'Directory restore password' -AsSecureString)
