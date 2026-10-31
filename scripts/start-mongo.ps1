param([string]$MongoD = 'C:/Program Files/MongoDB/Server/8.2/bin/mongod.exe')
$repoPath = (Resolve-Path "$PSScriptRoot/..").Path
$dataPath = Join-Path $repoPath '.local/mongo'
New-Item -ItemType Directory -Force $dataPath | Out-Null
Start-Process $MongoD -WindowStyle Hidden -ArgumentList @('--dbpath', $dataPath, '--port', '27018', '--replSet', 'eco-rs', '--bind_ip', '127.0.0.1', '--logpath', (Join-Path $dataPath 'mongod.log'))
Write-Output 'MongoDB listening on 27018. Initialize replica set with the documented command before API startup.'
