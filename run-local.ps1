# HIM Portfolio: Local Docker Orchestration Script
# This script automates the full teardown, rebuild, and launch process.

Write-Host "--- H.I.M. LOCAL DEPLOYMENT STARTING ---" -ForegroundColor Cyan

# 1. Cleanup old containers and volumes
Write-Host "[1/3] Cleaning up old environment..." -ForegroundColor Yellow
docker-compose down -v

# delete the cache file:
Remove-Item -Path "HIM.Microservices/HIM.AiService/knowledge-base.embeddings.bin" -Force -ErrorAction SilentlyContinue

#clear the old local known host key:
ssh-keygen -R '[localhost:2222]'

# 2. Force a fresh build (to ensure appsettings changes are caught)
Write-Host "[2/3] Building fresh images (No Cache)..." -ForegroundColor Yellow
docker-compose build --no-cache

# 3. Launch the stack
Write-Host "[3/3] Launching containers..." -ForegroundColor Yellow
docker-compose up -d

Write-Host "`nSUCCESS: The Neural Gateway is live on Port 2222." -ForegroundColor Green
Write-Host "Connect using: ssh guest@localhost -p 2222" -ForegroundColor White
Write-Host "(Note: If you get a Host Key error, run: ssh-keygen -R '[localhost]:2222')" -ForegroundColor Gray

Write-Host "Waiting for Neural Gateway to boot.." -ForegroundColor Yellow
Start-Sleep -Seconds 2

Write-Host "Connecting..." -ForegroundColor Green
#automatically connect locally
ssh guest@localhost -p 2222