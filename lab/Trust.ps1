#Requires -RunAsAdministrator
param(
   [Parameter(Mandatory)] [string] $Partner,
   [Parameter(Mandatory)] [string] $PartnerDns,
   [ValidateSet('Bidirectional', 'Outbound', 'Inbound')] [string] $Direction = 'Bidirectional',
   [switch] $ForwarderOnly
)

$ErrorActionPreference = 'Stop'
if (-not (Get-DnsServerZone -Name $Partner -ErrorAction SilentlyContinue)) {
   Add-DnsServerConditionalForwarderZone -Name $Partner -MasterServers $PartnerDns -ReplicationScope Forest
}
if ($ForwarderOnly) { return }

$credential = Get-Credential -Message "Administrator of $Partner"
$context = [System.DirectoryServices.ActiveDirectory.DirectoryContext]::new(
   'Forest', $Partner, $credential.UserName, $credential.GetNetworkCredential().Password)
$target = [System.DirectoryServices.ActiveDirectory.Forest]::GetForest($context)
[System.DirectoryServices.ActiveDirectory.Forest]::GetCurrentForest().CreateTrustRelationship(
   $target, [System.DirectoryServices.ActiveDirectory.TrustDirection]::$Direction)
Write-Host "Forest trust to $Partner created: $Direction"
