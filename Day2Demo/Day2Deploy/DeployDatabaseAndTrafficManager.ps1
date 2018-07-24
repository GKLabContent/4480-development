##Values that map to the deployment
$rg = "4480CH02"
$uniquePrefix = "tsw4480c2"
$location = "EastUS"
$sqlAdmin = "student"
$sqlPassword = "Pa55w.rd1234"
$dbFilePath = "$($PSScriptRoot)\$($dbFileName)"
# If you are running this piecemeal, set the $dbFilePath manually
# $dbFilePath = "<local path to file>\trucksmart.bacpac"

#Calculated values
$saName = "$($uniquePrefix)sa"
$container = "uploads"
$dbName = "TruckSmart"
$dbServerName = "$($uniquePrefix)sql"
$dbFileName = "trucksmart.bacpac"
$redisName = "$($uniquePrefix)redis"
$tmpName = "$($uniquePrefix)tmp"
$sqlPasswordSecure = ConvertTo-SecureString -String $sqlPassword -AsPlainText -Force
$webAppNames = @("$($uniquePrefix)east","$($uniquePrefix)west")

#Upload the bacpac file
New-AzureRmStorageAccount -ResourceGroupName $rg -Name $saName -SkuName Standard_LRS `
    -Location $location -Kind StorageV2 -AccessTier Hot

$key = (Get-AzureRmStorageAccountKey -ResourceGroupName $rg -Name $saName)[0].Value
$context = New-AzureStorageContext -StorageAccountName $saName -StorageAccountKey $key

New-AzureStorageContainer -Name $container -Permission Off -Context $context
Set-AzureStorageBlobContent -File $dbFilePath -Container $container -Blob $dbFileName `
    -BlobType Block -Context $context

$bacpacUri = "https://$($saName).blob.core.windows.net/$($container)/$($dbFileName)"

#Import the bacpac file
New-AzureRmSqlDatabaseImport -ResourceGroupName $rg -ServerName $dbServerName -DatabaseName $dbName `
    -StorageKeyType "StorageAccessKey" -StorageKey $key -StorageUri $bacpacUri `
     -AdministratorLogin $sqlAdmin  -AdministratorLoginPassword $sqlPasswordSecure `
     -Edition Basic -ServiceObjectiveName Basic -DatabaseMaxSizeBytes 5000000

#Update the web apps to point to the SQL database and Redis cache
$connectionString = "Server=tcp:$($dbServerName).database.windows.net,1433;Initial Catalog=$($dbName);" + `
    "Persist Security Info=False;User ID=$($sqlAdmin);Password=$($sqlPassword);MultipleActiveResultSets=False;" + `
    "Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;"

$connectionStrings = @{TruckSmartDB=@{Type="Custom"; Value=$connectionString}}

$redisKey =  (Get-AzureRmRedisCacheKey -ResourceGroupName $rg -Name $redisName).PrimaryKey
$redisConnection =  "$($redisName).redis.cache.windows.net:6380,password=$($redisKey),ssl=True,abortConnect=False"
$settings = (Get-AzureRmWebApp -ResourceGroupName $rg -Name $webAppNames[0]).SiteConfig.AppSettings
$newSettings = @{}
foreach($setting in $settings) {
    $newSettings[$setting.Name] = $setting.Value
}
$newSettings["redis"] = $redisConnection

foreach($webAppName in $webAppNames) {
    Set-AzureRmWebApp -ResourceGroupName $rg -Name $webAppName -ConnectionStrings $connectionStrings -AppSettings $newSettings
}

#Update the web apps with the redis connection string

#Create traffic manager profile and endpoints.
$tmp = New-AzureRmTrafficManagerProfile -Name $tmpName -ResourceGroupName $rg -TrafficRoutingMethod Performance `
    -RelativeDnsName $tmpName -Ttl 300 -MonitorProtocol HTTPS -MonitorPort 443 -MonitorPath "/"
foreach($ws in (Get-AzureRmWebApp -ResourceGroupName $rg)) {
    $endpoint1 = New-AzureRmTrafficManagerEndpoint -Name $ws.Name -ProfileName $tmpName -ResourceGroupName $rg -Type AzureEndpoints `
        -TargetResourceId $ws.Id -EndpointStatus Enabled
    }


#Output connection information

Write-Output "SQL Connection String:"
Write-Output $connectionString
Write-Output ""
Write-Output "Redis Connection String"
