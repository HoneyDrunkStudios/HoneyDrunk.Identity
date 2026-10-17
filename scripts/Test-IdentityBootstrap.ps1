[CmdletBinding()]
param(
 [string]$Package="$PSScriptRoot/../src/HoneyDrunk.Identity.Database/bin/Release/HoneyDrunk.Identity.Database.dacpac"
)
# Exercises only a uniquely named disposable LocalDB database; no Azure login.
$ErrorActionPreference='Stop'
Import-Module "$PSScriptRoot/IdentityBootstrap.psm1" -Force
$name='Identity_Bootstrap_'+[guid]::NewGuid().ToString('N')
$masterString='Server=(localdb)\MSSQLLocalDB;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;Initial Catalog=master;Pooling=False'
$master=Open-BootstrapConnection $masterString ''
$c=$null;$other=$null;$guard=$false
$passed=[Collections.Generic.List[string]]::new()
function Reject([scriptblock]$Action,[string]$Label){
 $rejected=$false
 try {& $Action|Out-Null}catch{$rejected=$true}
 if(-not $rejected){throw "Expected rejection: $Label"}
 $passed.Add($Label)
}
try {
 Invoke-BootstrapSql $master "CREATE DATABASE [$name]"|Out-Null
 # LocalDB defaults AUTO_CLOSE on; Azure SQL does not support that option.
 # Match the target before generating SQL, without filtering the reviewed script.
 Invoke-BootstrapSql $master "ALTER DATABASE [$name] SET AUTO_CLOSE OFF"|Out-Null
 $cs=$masterString.Replace('Initial Catalog=master',"Initial Catalog=$name")
 $c=Open-BootstrapConnection $cs '';$other=Open-BootstrapConnection $cs ''
 # This uniquely created test database is owned by the known LocalDB creator.
 $approvedOwnerSid=Invoke-BootstrapSql $c 'SELECT CONVERT(varchar(172),owner_sid,1) FROM sys.databases WHERE database_id=DB_ID()'
 $originalOwner=Invoke-BootstrapSql $c 'SELECT SUSER_SNAME(owner_sid) FROM sys.databases WHERE database_id=DB_ID()'

 Assert-EmptyBootstrapTarget $c $name $approvedOwnerSid
 $keyPassword=[guid]::NewGuid().ToString('N')+'a!1'
 foreach($artifact in @(
  @{Create='CREATE SYNONYM dbo.Unreviewed FOR dbo.Missing';Drop='DROP SYNONYM dbo.Unreviewed';Label='synonym'},
  @{Create='CREATE SCHEMA Unreviewed';Drop='DROP SCHEMA Unreviewed';Label='schema'},
  @{Create='CREATE TYPE dbo.Unreviewed FROM int';Drop='DROP TYPE dbo.Unreviewed';Label='type'},
  @{Create='ALTER AUTHORIZATION ON ROLE::db_owner TO [public]';Drop='ALTER AUTHORIZATION ON ROLE::db_owner TO dbo';Label='fixed-role ownership'},
  @{Create='ALTER AUTHORIZATION ON SCHEMA::db_datareader TO [public]';Drop='ALTER AUTHORIZATION ON SCHEMA::db_datareader TO db_datareader';Label='built-in schema ownership'},
  @{Create='CREATE ROLE Unreviewed';Drop='DROP ROLE Unreviewed';Label='role'},
  @{Create='CREATE USER Unreviewed WITHOUT LOGIN';Drop='DROP USER Unreviewed';Label='user'},
  @{Create='GRANT CONNECT TO guest; GRANT SELECT TO guest';Drop='REVOKE SELECT FROM guest; REVOKE CONNECT FROM guest';Label='guest permissions'},
  @{Create='GRANT SELECT TO public';Drop='REVOKE SELECT FROM public';Label='public permissions'},
  @{Create='ALTER ROLE db_datareader ADD MEMBER guest';Drop='ALTER ROLE db_datareader DROP MEMBER guest';Label='built-in role membership'},
  @{Create="CREATE MASTER KEY ENCRYPTION BY PASSWORD='$keyPassword'; CREATE DATABASE SCOPED CREDENTIAL Unreviewed WITH IDENTITY='fixture', SECRET='$keyPassword'";Drop='DROP DATABASE SCOPED CREDENTIAL Unreviewed; DROP MASTER KEY';Label='secret-bearing database credential'},
  @{Create="CREATE CERTIFICATE Unreviewed ENCRYPTION BY PASSWORD='$keyPassword' WITH SUBJECT='Disposable test certificate'";Drop='DROP CERTIFICATE Unreviewed';Label='certificate'},
  @{Create="CREATE COLUMN MASTER KEY Unreviewed WITH (KEY_STORE_PROVIDER_NAME=N'MSSQL_CERTIFICATE_STORE', KEY_PATH=N'CurrentUser/My/0000000000000000000000000000000000000000')";Drop='DROP COLUMN MASTER KEY Unreviewed';Label='column master key metadata'}
 )){
  Invoke-BootstrapSql $c $artifact.Create|Out-Null
  try {Reject {Assert-EmptyBootstrapTarget $c $name $approvedOwnerSid} ("preexisting "+$artifact.Label+" rejected")}
  finally {Invoke-BootstrapSql $c $artifact.Drop|Out-Null}
  Assert-EmptyBootstrapTarget $c $name $approvedOwnerSid
 }
 # Changing owner must fail even when no user-defined objects/principals exist.
 Invoke-BootstrapSql $c "ALTER AUTHORIZATION ON DATABASE::[$name] TO [sa]"|Out-Null
 try {Reject {Assert-EmptyBootstrapTarget $c $name $approvedOwnerSid} 'unapproved database owner rejected'}
 finally {
  $quotedOwner='['+$originalOwner.Replace(']',']]')+']'
  Invoke-BootstrapSql $c "ALTER AUTHORIZATION ON DATABASE::[$name] TO $quotedOwner"|Out-Null
 }
 Assert-EmptyBootstrapTarget $c $name $approvedOwnerSid
 $packagePath=(Resolve-Path -LiteralPath $Package).Path
 $review=Get-BootstrapReview $cs '' $packagePath $name
 $script=$review.Script
 $results=Join-Path $PSScriptRoot '../TestResults'
 New-Item -ItemType Directory -Path $results -Force|Out-Null
 [IO.File]::WriteAllText((Join-Path $results 'bootstrap-generated.sql'),$script)
 $batches=@(ConvertTo-BootstrapBatches $script $name)
 if($batches.Count -lt 50){throw 'Actual generated schema script was not exercised'}
 Reject {ConvertTo-BootstrapBatches $script 'WrongDatabase'} 'wrong target rejected'
 Reject {ConvertTo-BootstrapBatches ($script+"`n:r unsafe.sql") $name} 'external script include rejected'
 Reject {ConvertTo-BootstrapBatches ($script+"`nGO`nDROP DATABASE [anything];") $name} 'database mutation rejected'
 # Simulate an administrator granting access after inspection but before locking.
 Invoke-BootstrapSql $other 'GRANT SELECT TO guest'|Out-Null
 Enter-BootstrapGuard $c;$guard=$true
 Reject {Assert-EmptyBootstrapTarget $c $name $approvedOwnerSid -UnderGuard} 'permission race before guard rejected'
 Exit-BootstrapGuard $c;$guard=$false
 Invoke-BootstrapSql $other 'REVOKE SELECT FROM guest'|Out-Null
 $c.Dispose();$c=Open-BootstrapConnection $cs ''
 Enter-BootstrapGuard $c;$guard=$true
 Assert-EmptyBootstrapTarget $c $name $approvedOwnerSid -UnderGuard
 Reject {Invoke-BootstrapSql $other 'GRANT SELECT TO guest'} 'competing permission grant blocked'
 $fresh=Get-BootstrapReview $cs '' $packagePath $name
 if($fresh.Script -cne $script -or $fresh.Report -cne $review.Report){throw 'Maintenance guard changed the generated plan'}
 $passed.Add('fresh planning under guard preserves exact SQL and report')
 Reject {Invoke-BootstrapSql $other 'CREATE TABLE dbo.CompetingWriter(Id int)'} 'competing DDL blocked'
 if((Invoke-BootstrapSql $c "SELECT COUNT(*) FROM sys.tables WHERE name='CompetingWriter'") -ne 0){throw 'Competing DDL was committed'}
 $cut=0
 for($i=0;$i -lt $batches.Count;$i++){if($batches[$i] -match 'CREATE TABLE \[outbox\]'){$cut=$i;break}}
 if($cut -eq 0){throw 'No real table batch found'}
 $broken=@($batches[0..$cut])+@(";THROW 51002, 'Injected executor failure', 1;")
 Reject {Invoke-ExactBootstrap $c $broken} 'partial DDL failure rolls back'
 if((Invoke-BootstrapSql $c 'SELECT COUNT(*) FROM sys.tables WHERE is_ms_shipped=0') -ne 0 -or (Invoke-BootstrapSql $c "SELECT COUNT(*) FROM sys.schemas WHERE name='outbox'") -ne 0){throw 'Partial schema survived rollback'}
 $passed.Add('no tables or schema survived failure')
 # A fresh session restores default SET/temporary-object state after rollback.
 Exit-BootstrapGuard $c;$guard=$false;$c.Dispose();$c=Open-BootstrapConnection $cs ''
 Assert-EmptyBootstrapTarget $c $name $approvedOwnerSid
 Enter-BootstrapGuard $c;$guard=$true
 Invoke-ExactBootstrap $c $batches
 if((Invoke-BootstrapSql $c 'SELECT COUNT(*) FROM sys.tables WHERE is_ms_shipped=0') -ne 7){throw 'Expected all seven DACPAC tables'}
 if((Invoke-BootstrapSql $c 'SELECT COUNT(*) FROM sys.foreign_keys WHERE is_disabled=1 OR is_not_trusted=1') -ne 0){throw 'Untrusted constraints after initialization'}
 $passed.Add('complete generated schema committed with trusted constraints')
 Exit-BootstrapGuard $c;$guard=$false
 Reject {Assert-EmptyBootstrapTarget $c $name $approvedOwnerSid} 'reinitialization refuses populated schema'
 Invoke-BootstrapSql $other 'CREATE TABLE dbo.AfterMaintenance(Id int); DROP TABLE dbo.AfterMaintenance;'|Out-Null
 $passed.Add('maintenance guard removed and normal DDL restored')
 $results=Join-Path $PSScriptRoot '../TestResults'
 New-Item -ItemType Directory -Path $results -Force|Out-Null
 @{passed=$passed;actualDacpacTables=7;database='Unique disposable LocalDB database';packageSha256=(Get-FileHash $packagePath).Hash;completedUtc=(Get-Date).ToUniversalTime().ToString('o')}|ConvertTo-Json|Set-Content (Join-Path $results 'bootstrap-executor-tests.json')
 Write-Output "$($passed.Count) real SQL executor checks passed."
}finally{
 if($guard -and $c.State -eq 'Open'){Exit-BootstrapGuard $c}
 if($other){$other.Dispose()};if($c){$c.Dispose()}
 if($name -notmatch '^Identity_Bootstrap_[a-f0-9]{32}$'){throw 'Unsafe test cleanup target'}
 Invoke-BootstrapSql $master "ALTER DATABASE [$name] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$name];"|Out-Null
 $master.Dispose()
}
