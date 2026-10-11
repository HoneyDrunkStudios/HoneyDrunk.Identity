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
function Read-BootstrapSqlCmd([string]$Script){
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
 return @{Variables=$variables;Text=($lines -join "`n")}
}
function Expand-BootstrapSqlCmd($Variables,[string]$Text,[string]$Database){
 if($Variables['DatabaseName'] -cne $Database -or $Variables['__IsSqlCmdEnabled'] -cne 'True'){throw 'SQLCMD target/mode mismatch'}
 foreach($key in $variables.Keys){
  $value=$variables[$key]
  if($key -in @('DatabaseName','DefaultFilePrefix') -and $value -notmatch '^[A-Za-z0-9_-]+$'){throw 'Unsafe database variable'}
  $text=$text.Replace('$('+ $key +')',$value)
 }
 if($text -match '\$\('){throw 'Unresolved SQLCMD variable'}
 return $text
}
function ConvertTo-BootstrapBatches([string]$Script,[string]$Database){
 $sqlcmd=Read-BootstrapSqlCmd $Script
 $text=Expand-BootstrapSqlCmd $sqlcmd.Variables $sqlcmd.Text $Database
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
function Assert-EmptyBootstrapTarget($Connection,[string]$Database,[string]$ApprovedOwnerSid,[switch]$UnderGuard){
 if((Invoke-BootstrapSql $Connection 'SELECT DB_NAME()') -cne $Database){throw 'Connected to wrong database'}
 Assert-BootstrapAzureMetadata $Connection
 if($ApprovedOwnerSid -notmatch '^0x(?:[0-9a-fA-F]{2}){1,85}$'){throw 'An independently approved database owner SID is required'}
 $owner=Invoke-BootstrapSql $Connection 'SELECT CONVERT(varchar(172),owner_sid,1) FROM sys.databases WHERE database_id=DB_ID()'
 if($owner -ine $ApprovedOwnerSid){throw 'Database owner differs from the approved identity'}
 Assert-ZeroBootstrapRows $Connection 'SELECT COUNT(*) FROM sys.database_principals WHERE is_fixed_role=1 AND (owning_principal_id IS NULL OR owning_principal_id<>1)' 'Unexpected fixed-role ownership'
 Assert-ZeroBootstrapRows $Connection 'SELECT COUNT(*) FROM sys.schemas s LEFT JOIN sys.database_principals p ON p.name=s.name WHERE p.principal_id IS NULL OR s.principal_id<>p.principal_id' 'Unexpected built-in schema ownership'
 Assert-ZeroBootstrapRows $Connection 'SELECT COUNT(*) FROM sys.objects WHERE is_ms_shipped=0' 'First initialization refuses a nonempty database'
 Assert-ZeroBootstrapRows $Connection 'SELECT COUNT(*) FROM sys.database_principals WHERE principal_id>4 AND is_fixed_role=0;' 'Unexpected existing database users; review all writers first'
 Assert-ZeroBootstrapRows $Connection "SELECT COUNT(*) FROM sys.schemas WHERE name NOT IN ('dbo','guest','INFORMATION_SCHEMA','sys','db_owner','db_accessadmin','db_securityadmin','db_ddladmin','db_backupoperator','db_datareader','db_datawriter','db_denydatareader','db_denydatawriter')" 'Unexpected existing database schemas'
 # Permit only fresh-database grants; DacFx deliberately does not overwrite permissions.
 $permissionQuery=@"
SELECT COUNT(*) FROM sys.database_permissions p WHERE NOT (
 p.grantor_principal_id=1 AND p.state='G' AND p.minor_id=0 AND (
  (p.class=0 AND p.major_id=0 AND p.grantee_principal_id=1 AND p.type='CO') OR
  (p.class=0 AND p.major_id=0 AND p.grantee_principal_id=0 AND p.type IN ('VWCK','VWCM')) OR
  (p.class=1 AND p.major_id<0 AND p.grantee_principal_id=0 AND p.type='SL')
 ))
"@
 Assert-ZeroBootstrapRows $Connection $permissionQuery 'Unexpected existing database permissions'
 Assert-ZeroBootstrapRows $Connection 'SELECT COUNT(*) FROM sys.database_role_members WHERE NOT (role_principal_id=16384 AND member_principal_id=1)' 'Unexpected existing database role membership'
 Assert-ZeroBootstrapRows $Connection 'SELECT COUNT(*) FROM sys.types WHERE is_user_defined=1' 'Unexpected existing database types'
 Assert-ZeroBootstrapRows $Connection 'SELECT COUNT(*) FROM sys.assemblies WHERE is_user_defined=1' 'Unexpected existing database assemblies'
 foreach($catalog in @('symmetric_keys','asymmetric_keys','certificates','column_master_keys','column_encryption_keys','database_scoped_credentials','external_data_sources','external_file_formats','partition_functions','partition_schemes','fulltext_catalogs','fulltext_stoplists','registered_search_property_lists')){
  Assert-ZeroBootstrapRows $Connection "SELECT COUNT(*) FROM sys.$catalog" "Unexpected existing database artifacts in $catalog"
 }
 Assert-ZeroBootstrapRows $Connection 'SELECT COUNT(*) FROM sys.xml_schema_collections WHERE xml_collection_id>1' 'Unexpected existing XML schema collections'
 $triggerQuery='SELECT COUNT(*) FROM sys.triggers WHERE parent_class=0'
 if($UnderGuard){
  if((Invoke-BootstrapSql $Connection "SELECT COUNT(*) FROM sys.triggers WHERE parent_class=0 AND name='__identity_bootstrap_guard' AND is_disabled=0") -ne 1){throw 'Bootstrap guard is not active'}
  $triggerQuery+=" AND name<>'__identity_bootstrap_guard'"
 }
 Assert-ZeroBootstrapRows $Connection $triggerQuery 'Existing database trigger requires recovery/review'
}
function Assert-ZeroBootstrapRows($Connection,[string]$Query,[string]$Reason){
 if((Invoke-BootstrapSql $Connection $Query) -ne 0){throw $Reason}
}
function Assert-BootstrapAzureMetadata($Connection,$Transaction=$null){
 # These Azure SQL catalogs do not exist on LocalDB. Query errors must fail
 # closed; firewall access and tracing sessions survive schema initialization.
 if((Invoke-BootstrapSql $Connection "SELECT SERVERPROPERTY('EngineEdition')" $Transaction) -eq 5){
  if((Invoke-BootstrapSql $Connection 'SELECT COUNT(*) FROM sys.database_firewall_rules' $Transaction) -ne 0){
   throw 'Unexpected database-level firewall rule; coordinate access recovery before initialization'
  }
  if((Invoke-BootstrapSql $Connection 'SELECT COUNT(*) FROM sys.database_event_sessions' $Transaction) -ne 0){
   throw 'Unexpected database event session; review tracing before initialization'
  }
 }
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
  Assert-BootstrapAzureMetadata $Connection $tx
  foreach($batch in $Batches){
   Invoke-BootstrapSql $Connection $batch $tx|Out-Null
   Assert-BootstrapAzureMetadata $Connection $tx
  }
  if((Invoke-BootstrapSql $Connection 'SELECT @@TRANCOUNT' $tx) -ne 1){throw 'Script escaped outer transaction boundary'}
  $tx.Commit()
 }catch{
  try{$tx.Rollback()}catch{Write-Verbose 'Rollback was unavailable; preserving the original bootstrap failure.'}
  throw
 }finally{$tx.Dispose()}
}
Export-ModuleMember -Function Invoke-BootstrapSql,Open-BootstrapConnection,New-BootstrapOptions,Get-BootstrapReview,ConvertTo-BootstrapBatches,Assert-EmptyBootstrapTarget,Enter-BootstrapGuard,Exit-BootstrapGuard,Invoke-ExactBootstrap
