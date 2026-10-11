Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
if(-not $IsWindows -or [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture -ne 'X64'){
 throw 'Identity bootstrap requires Windows x64 PowerShell 7.'
}
$version=(Get-Content (Join-Path $PSScriptRoot '../dotnet-tools.json') -Raw|ConvertFrom-Json).tools.'microsoft.sqlpackage'.version
$nugetOutput=& dotnet nuget locals global-packages --list
if($LASTEXITCODE -ne 0){throw 'Unable to resolve the NuGet package cache'}
$nugetRoot=($nugetOutput -join "`n").Split(':',2)[1].Trim()
$dacHome=Join-Path $nugetRoot "microsoft.sqlpackage/$version/tools/net10.0/any"
Add-Type -Path (Join-Path $dacHome 'runtimes/win/lib/net9.0/Microsoft.Data.SqlClient.dll')
$null=[System.Runtime.InteropServices.NativeLibrary]::Load((Join-Path $dacHome 'runtimes/win-x64/native/Microsoft.Data.SqlClient.SNI.dll'))
foreach($name in @('Microsoft.SqlServer.Dac','Microsoft.SqlServer.TransactSql.ScriptDom')){Add-Type -Path (Join-Path $dacHome "$name.dll")}
if(-not ('IdentityBootstrap.TokenProvider' -as [type])){
 Add-Type -ReferencedAssemblies (Join-Path $dacHome 'Microsoft.SqlServer.Dac.dll') -TypeDefinition @'
namespace IdentityBootstrap {
 public sealed class TokenProvider : Microsoft.SqlServer.Dac.IUniversalAuthProvider {
  private readonly string token;
  public TokenProvider(string value) { token = value; }
  public string GetValidAccessToken() { return token; }
 }
}
'@
}
function Invoke-BootstrapSql($Connection,[string]$Sql,$Transaction=$null){
 $cmd=$Connection.CreateCommand();$cmd.CommandText=$Sql;$cmd.CommandTimeout=120
 if($null -ne $Transaction){$cmd.Transaction=$Transaction}
 try{return $cmd.ExecuteScalar()}finally{$cmd.Dispose()}
}
function Open-BootstrapConnection([string]$ConnectionString,[string]$AccessToken){
 $c=[Microsoft.Data.SqlClient.SqlConnection]::new($ConnectionString)
 if($AccessToken){$c.AccessToken=$AccessToken}
 $c.Open();return $c
}
function New-BootstrapOptions {
 $o=[Microsoft.SqlServer.Dac.DacDeployOptions]::new()
 $o.BlockOnPossibleDataLoss=$true;$o.DropObjectsNotInSource=$false
 $o.ScriptDatabaseOptions=$false;$o.IncludeTransactionalScripts=$true
 $o.DisableAndReenableDdlTriggers=$false
 $o.IgnorePermissions=$true;$o.IgnoreRoleMembership=$true
 $o.ExcludeObjectTypes=[Microsoft.SqlServer.Dac.ObjectType[]]@('Users','Logins','RoleMembership','Permissions','DatabaseTriggers')
 return $o
}
function Get-BootstrapReview([string]$ConnectionString,[string]$AccessToken,[string]$Package,[string]$Database){
 $auth=[IdentityBootstrap.TokenProvider]::new($AccessToken)
 $services=if($AccessToken){[Microsoft.SqlServer.Dac.DacServices]::new($ConnectionString,$auth)}else{[Microsoft.SqlServer.Dac.DacServices]::new($ConnectionString)}
 $pkg=[Microsoft.SqlServer.Dac.DacPackage]::Load($Package)
 try {
  $options=New-BootstrapOptions
  return @{Script=$services.GenerateDeployScript($pkg,$Database,$options);Report=$services.GenerateDeployReport($pkg,$Database,$options)}
 }finally{$pkg.Dispose()}
}
function ConvertTo-BootstrapBatches([string]$Script,[string]$Database){
 $variables=@{}
 $lines=[Collections.Generic.List[string]]::new()
 foreach($line in ($Script -split '\r?\n')){
  if($line -match '^:setvar ([A-Za-z_][A-Za-z0-9_]*) "([^"\r\n]*)"\s*$'){
   if($matches[1] -notin @('DatabaseName','DefaultFilePrefix','DefaultDataPath','DefaultLogPath','__IsSqlCmdEnabled')){throw 'Unexpected SQLCMD variable'}
   if($variables.ContainsKey($matches[1])){throw 'Duplicate SQLCMD variable'}
   $variables[$matches[1]]=$matches[2];$lines.Add('');continue
  }
  if($line -eq ':on error exit'){$lines.Add('');continue}
  if($line -match '^\s*:'){throw 'Unsupported SQLCMD directive'}
  $lines.Add($line)
 }
 if($variables['DatabaseName'] -cne $Database -or $variables['__IsSqlCmdEnabled'] -cne 'True'){throw 'SQLCMD target/mode mismatch'}
 $text=$lines -join "`n"
 foreach($key in $variables.Keys){
  $value=$variables[$key]
  if($key -in @('DatabaseName','DefaultFilePrefix') -and $value -notmatch '^[A-Za-z0-9_-]+$'){throw 'Unsafe database variable'}
  $text=$text.Replace('$('+ $key +')',$value)
 }
 if($text -match '\$\('){throw 'Unresolved SQLCMD variable'}
 $errors=$null
 $parser=[Microsoft.SqlServer.TransactSql.ScriptDom.TSql160Parser]::new($true)
 $tree=$parser.Parse([IO.StringReader]::new($text),[ref]$errors)
 if($errors.Count){throw 'Generated SQL does not parse cleanly'}
 foreach($batch in $tree.Batches){
  foreach($statement in $batch.Statements){
   if($statement -is [Microsoft.SqlServer.TransactSql.ScriptDom.UseStatement] -and $statement.DatabaseName.Value -cne $Database){throw 'Cross-database statement refused'}
  }
  # The reviewed initial script must never create/drop a database or alter its
  # options. This bound is in addition to the parsed batch/target checks.
  $sql=$text.Substring($batch.StartOffset,$batch.FragmentLength)
  if($sql -match '(?im)\b(CREATE|DROP|ALTER)\s+DATABASE\b' -or $sql -match '__identity_bootstrap_guard'){throw 'Database/guard mutation refused'}
  if($sql.Trim()){Write-Output $sql}
 }
}
function Assert-EmptyBootstrapTarget($Connection,[string]$Database){
 if((Invoke-BootstrapSql $Connection 'SELECT DB_NAME()') -cne $Database){throw 'Connected to wrong database'}
 $objects=Invoke-BootstrapSql $Connection "SELECT COUNT(*) FROM sys.objects WHERE is_ms_shipped=0"
 if($objects -ne 0){throw 'First initialization refuses a nonempty database'}
 $principals=Invoke-BootstrapSql $Connection "SELECT COUNT(*) FROM sys.database_principals WHERE principal_id>4 AND is_fixed_role=0;"
 if($principals -ne 0){throw 'Unexpected existing database users; review all writers first'}
 $schemas=Invoke-BootstrapSql $Connection "SELECT COUNT(*) FROM sys.schemas WHERE name NOT IN ('dbo','guest','INFORMATION_SCHEMA','sys','db_owner','db_accessadmin','db_securityadmin','db_ddladmin','db_backupoperator','db_datareader','db_datawriter','db_denydatareader','db_denydatawriter')"
 if($schemas -ne 0){throw 'Unexpected existing database schemas'}
 # Permit only fresh-database grants; DacFx deliberately does not overwrite permissions.
 $permissions=Invoke-BootstrapSql $Connection @"
SELECT COUNT(*) FROM sys.database_permissions p WHERE NOT (
 p.grantor_principal_id=1 AND p.state='G' AND p.minor_id=0 AND (
  (p.class=0 AND p.major_id=0 AND p.grantee_principal_id=1 AND p.type='CO') OR
  (p.class=0 AND p.major_id=0 AND p.grantee_principal_id=0 AND p.type IN ('VWCK','VWCM')) OR
  (p.class=1 AND p.major_id<0 AND p.grantee_principal_id=0 AND p.type='SL')
 ))
"@
 if($permissions -ne 0){throw 'Unexpected existing database permissions'}
 if((Invoke-BootstrapSql $Connection 'SELECT COUNT(*) FROM sys.database_role_members WHERE NOT (role_principal_id=16384 AND member_principal_id=1)') -ne 0){throw 'Unexpected existing database role membership'}
 if((Invoke-BootstrapSql $Connection 'SELECT COUNT(*) FROM sys.types WHERE is_user_defined=1') -ne 0){throw 'Unexpected existing database types'}
 if((Invoke-BootstrapSql $Connection 'SELECT COUNT(*) FROM sys.assemblies WHERE is_user_defined=1') -ne 0){throw 'Unexpected existing database assemblies'}
 if((Invoke-BootstrapSql $Connection 'SELECT COUNT(*) FROM sys.triggers WHERE parent_class=0') -ne 0){throw 'Existing database trigger requires recovery/review'}
}
function Enter-BootstrapGuard($Connection){
 $spid=[int](Invoke-BootstrapSql $Connection 'SELECT @@SPID')
 $nonce=[guid]::NewGuid().ToString()
 Invoke-BootstrapSql $Connection "EXEC sys.sp_set_session_context @key=N'IdentityBootstrap', @value=N'$nonce', @read_only=1"|Out-Null
 Invoke-BootstrapSql $Connection @"
CREATE TRIGGER [__identity_bootstrap_guard] ON DATABASE FOR DDL_DATABASE_LEVEL_EVENTS AS
BEGIN
 SET NOCOUNT ON;
 IF @@SPID <> $spid OR ISNULL(CONVERT(nvarchar(36),SESSION_CONTEXT(N'IdentityBootstrap')),N'') <> N'$nonce'
 BEGIN
  ;THROW 51001, 'Identity bootstrap maintenance: competing DDL refused.', 1;
 END
END
"@|Out-Null
}
function Exit-BootstrapGuard($Connection){
 Invoke-BootstrapSql $Connection 'DROP TRIGGER [__identity_bootstrap_guard] ON DATABASE'|Out-Null
}
function Invoke-ExactBootstrap($Connection,[string[]]$Batches){
 $tx=$Connection.BeginTransaction()
 try {
  foreach($batch in $Batches){Invoke-BootstrapSql $Connection $batch $tx|Out-Null}
  if((Invoke-BootstrapSql $Connection 'SELECT @@TRANCOUNT' $tx) -ne 1){throw 'Script escaped outer transaction boundary'}
  $tx.Commit()
 }catch{
  try{$tx.Rollback()}catch{}
  throw
 }finally{$tx.Dispose()}
}
Export-ModuleMember -Function Invoke-BootstrapSql,Open-BootstrapConnection,New-BootstrapOptions,Get-BootstrapReview,ConvertTo-BootstrapBatches,Assert-EmptyBootstrapTarget,Enter-BootstrapGuard,Exit-BootstrapGuard,Invoke-ExactBootstrap
