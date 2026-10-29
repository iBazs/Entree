param(
   [Parameter(Mandatory)] [string] $Entree,
   [string] $Share = '\\FS1\LabShare',
   [string] $Corp = 'CORP',
   [string] $Partner = 'PARTNER',
   [switch] $Cloned,
   [switch] $Mapped
)

$ErrorActionPreference = 'Stop'
$server = $Share.TrimStart('\').Split('\')[0].Split('.')[0]
$work = New-Item -ItemType Directory -Force (Join-Path ([IO.Path]::GetTempPath()) "entreelab$([guid]::NewGuid().ToString('N'))")
try {
   $target = $Share
   if ($Mapped) {
      $target = 'MNOPQRSTUVWXYZ'.ToCharArray() | ForEach-Object { "$($_):" } |
                Where-Object { -not (Test-Path "$_\") } | Select-Object -First 1
      net use $target $Share | Out-Null
      if ($LASTEXITCODE) { throw "Cannot map $Share" }
   }
   $config = Join-Path $work 'lab.entree'
   ConvertTo-Json -Compress -InputObject @(@{
      SearchDirectory = $target; SaveDirectory = $work.FullName; SearchFor = 1; DepthLevel = -1
      LocalSearch = $false; InheritedAccess = $true; ShowOwner = $true; ShowGroups = $true; ShowMatrix = $true
      OpenResults = $false; ExportAs = 0; ExcludedAccounts = @()
   }) | Set-Content $config

   try {
      $run = Start-Process $Entree -ArgumentList '--silent', '--search', "`"$config`"" -Wait -PassThru
   }
   finally {
      if ($Mapped) { net use $target /delete /y | Out-Null }
   }
   if ($run.ExitCode) { throw "Entree exited with $($run.ExitCode)" }

   $zip = Get-ChildItem $work -Filter *.zip | Select-Object -First 1
   if (-not $zip) { throw 'No report was written' }
   Expand-Archive $zip.FullName (Join-Path $work 'report')
   $identities = Import-Csv (Join-Path $work 'report\Identity.csv') -Delimiter ';'
   $groups = Import-Csv (Join-Path $work 'report\Groups.csv') -Delimiter ';'

   $failures = [Collections.Generic.List[string]]::new()
   function Check([string] $Name, [bool] $Passed) {
      Write-Host ("{0} {1}" -f ($(if ($Passed) { 'PASS' } else { 'FAIL' })), $Name)
      if (-not $Passed) { $failures.Add($Name) }
   }
   function Account([string] $FullName) { $identities | Where-Object { $_.'Full Name' -eq $FullName } }
   function BySid([string] $Sid) { @($identities | Where-Object { $_.'Security Identifier(SID)' -eq $Sid }) }
   function Members([string] $Group) { @($groups | ForEach-Object { $_.$Group } | Where-Object { $_ }) }

   $editors = Account "$Corp\Lab Editors"
   Check 'Domain group resolved through the joined domain' ($editors.Type -eq 'Group' -and $editors.Status -eq 'Enabled')
   Check 'Disabled domain user is Disabled' ((Account "$Corp\bob").Status -eq 'Disabled')
   $staff = Account "$Partner\Partner Staff"
   Check 'Trusted forest group resolved without a stored account' ($staff -and $staff.Status -in 'Enabled', 'Limited')
   Check 'Nested group expanded' ((Members "$Corp\Lab Readers") -contains "$Corp\alice")

   $ops = Account "$server\LocalOps"
   Check 'Local group of the share server resolved' ($ops -and $ops.Status -in 'Enabled', 'Limited')
   if ($ops.Status -eq 'Enabled') {
      Check 'Local group expanded on the share server' ((Members "$server\LocalOps") -contains "$server\LocalAuditor")
   }

   $admins = BySid 'S-1-5-32-544' | Select-Object -First 1
   Check 'BUILTIN Administrators resolved' ($admins -and $admins.Status -in 'Enabled', 'Limited')
   if ($admins.Status -eq 'Enabled') {
      $local = Members $admins.'Full Name' | Where-Object { $_ -like "$env:COMPUTERNAME\*" }
      Check 'BUILTIN Administrators expanded on the share server, not this computer' (-not $local)
   }

   $unknown = BySid 'S-1-5-21-1-2-3-4242'
   Check 'Unknown SID stays unresolved' ($unknown.Count -eq 1 -and -not $unknown[0].Name -and $unknown[0].Error)

   $conflicts = @($identities | Group-Object 'Security Identifier(SID)' | Where-Object Count -gt 1)
   if ($Cloned) {
      Check 'Cloned machine SID listed twice' (@($conflicts | Where-Object { $_.Name -like '*-500' }).Count -eq 1)
   } else {
      Check 'No SID listed twice' ($conflicts.Count -eq 0)
   }
}
finally {
   Remove-Item $work -Recurse -Force
}

if ($failures.Count) { throw "$($failures.Count) lab checks failed" }
Write-Host 'All lab checks passed'
