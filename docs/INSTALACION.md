# Guía de Instalación — ZeroTeams MCP

## Requisitos Previos

| Componente | Versión Mínima | Notas |
|------------|----------------|-------|
| **.NET SDK** | 10.0+ | `dotnet --version` |
| **Node.js** | 18+ | `node --version` |
| **pnpm** | 8+ | `pnpm --version` (o `corepack enable`) |
| **Docker** | 24+ | `docker --version` (opcional, para Docker Compose) |
| **Redis** | 7+ | `redis-server --version` (opcional, para multi-replica) |
| **Git** | Cualquier | `git --version` |

---

## 1. Azure AD App Registration (OBLIGATORIO - una sola vez)

1. Ve a [Entra Portal](https://entra.microsoft.com) > **App registrations** > **New registration**
2. Name: `ZeroTeams MCP`
3. Supported account types: **Accounts in this organizational directory only** (Single tenant)
3. Redirect URI: **Web** → `http://localhost:5100/auth/callback` (para OAuth delegado)
4. **Guardar**: `Tenant ID`, `Client ID`
5. **Certificates & secrets** → **New client secret** → Guardar `Client Secret`
6. **API permissions** → **Add permission** → **Microsoft Graph** → **Application permissions**:
   - `Team.ReadBasic.All`
   - `Channel.ReadBasic.All`
   - `Chat.ReadBasic.All`
   - `User.ReadBasic.All`
   - `Chat.ReadWrite.All`
   - `Calendars.ReadWrite`
   - `OnlineMeetings.ReadWrite` (opcional, para meetings)
   - `OnlineMeetingAttendance.Read` (opcional, para meetings)
7. **Grant admin consent** (botón verde)

---

## 2. Instalación en macOS

### Opción A: Docker Compose (RECOMENDADO - más fácil)

```bash
# 1. Clonar
git clone https://github.com/zerocool-network/zero-teams-mcp.git
cd zero-teams-mcp

# 2. Configurar variables
cp deploy/.env.example deploy/.env
# Editar deploy/.env con tus credenciales Azure:
# AZURE_TENANT_ID=xxx
# AZURE_CLIENT_ID=xxx
# AZURE_CLIENT_SECRET=xxx
# ENCRYPTION_KEY=$(openssl rand -base64 32)

# 3. Levantar todo
docker compose -f deploy/docker-compose.yml up -d

# Verificar
curl http://localhost:5100/health
curl http://localhost:3000
curl -X POST http://localhost:5200/mcp -H "Content-Type: application/json" -H "Accept: application/json, text/event-stream" -d '{"jsonrpc":"2.0","id":1,"method":"tools/list"}'
```

### Opción B: Instalación nativa (para desarrollo)

```bash
# 1. Prerequisitos
brew install dotnet@10 node pnpm redis
brew services start redis

# 2. Configurar
cd /ruta/a/zero-teams-mcp
cp deploy/.env.example deploy/.env
# Editar deploy/.env

# 3. Generar ENCRYPTION_KEY
openssl rand -base64 32
# Agregar a deploy/.env: ENCRYPTION_KEY=...

# 4. ControlPlane
cd src/ZeroTeamsMcp.ControlPlane
dotnet run -c Release
# → http://localhost:5100

# 5. MCP Server (nueva terminal)
cd src/ZeroTeamsMcp.Server
TRANSPORT=http dotnet run -c Release
# → http://localhost:5200/mcp

# 6. Panel (nueva terminal)
cd apps/panel
pnpm install
pnpm dev
# → http://localhost:3000
```

---

## 3. Instalación en Windows 11 (Pro / Home)

### Prerequisitos Windows

```powershell
# 1. Instalar Chocolatey (si no tienes)
Set-ExecutionPolicy Bypass -Scope Process -Force
[System.Net.ServicePointManager]::SecurityProtocol = [System.Net.ServicePointManager]::SecurityProtocol -bor 3072
iex ((New-Object System.Net.WebClient).DownloadString('https://community.chocolatey.org/install.ps1'))

# 2. Instalar herramientas
choco install dotnet-sdk nodejs pnpm git redis -y

# 3. Habilitar Redis (si usa Docker Desktop)
# Docker Desktop → Settings → Resources → Advanced → Enable Redis
# O instalar Redis nativo:
choco install memurai -y  # Redis compatible Windows

# 4. Reiniciar terminal
```

### Opción A: Docker Desktop (RECOMENDADO)

```powershell
# 1. Instalar Docker Desktop
# https://desktop.docker.com/win/main/amd64/Docker%20Desktop%20Installer.exe

# 2. Configurar
git clone https://github.com/zerocool-network/zero-teams-mcp.git
cd zero-teams-mcp

# 3. Configurar .env
copy deploy\.env.example deploy\.env
notepad deploy\.env
# Editar con tus credenciales

# 4. Generar ENCRYPTION_KEY
# En PowerShell:
[Convert]::ToBase64String((1..32 | ForEach-Object { Get-Random -Minimum 0 -Maximum 256 }))
# Pegar en deploy/.env

# 5. Levantar
docker compose -f deploy\docker-compose.yml up -d
```

### Opción B: Nativo (sin Docker)

```powershell
# 1. Prerequisitos ya instalados (paso anterior)

# 2. Configurar
git clone https://github.com/zerocool-network/zero-teams-mcp.git
cd zero-teams-mcp
copy deploy\.env.example deploy\.env
notepad deploy\.env
# Editar credenciales

# 3. Generar ENCRYPTION_KEY
[Convert]::ToBase64String((1..32 | ForEach-Object { Get-Random -Minimum 0 -Maximum 256 }))
# Pegar en deploy/.env

# 4. ControlPlane
cd src\ZeroTeamsMcp.ControlPlane
dotnet run -c Release

# 5. MCP Server (nueva terminal)
cd src\ZeroTeamsMcp.Server
$env:TRANSPORT="http"
dotnet run -c Release

# 6. Panel
cd apps\panel
pnpm install
pnpm dev
```

### Windows: Configurar Redis

**Opción A: Docker Desktop** (más fácil)
- Docker Desktop → Settings → Resources → Advanced → Enable "Redis"
- Puerto: 6379

**Opción B: Memurai (Redis nativo Windows)**
```powershell
choco install memurai -y
# Se instala como servicio Windows, puerto 6379
```

**Opción C: WSL2 + Redis Linux**
```powershell
wsl --install -d Ubuntu
wsl sudo apt update && sudo apt install redis-server -y
wsl sudo service redis-server start
```

---

## 4. Verificación Post-Instalación

```bash
# 1. Health checks
curl http://localhost:5100/health
curl http://localhost:5200/mcp -X POST -H "Content-Type: application/json" -H "Accept: application/json, text/event-stream" -d '{"jsonrpc":"2.0","id":1,"method":"tools/list"}'
curl http://localhost:3000

# 2. Registrar tenant
cd /ruta/zero-teams-mcp
export ENCRYPTION_KEY="tu-key"
dotnet run --project src/ZeroTeamsMcp.Cli -- tenant-add --tenant-id TU_TENANT_ID --client-id TU_CLIENT_ID --client-secret TU_SECRET --display-name "Mi Empresa"

# 3. Consent
dotnet run --project src/ZeroTeamsMcp.Cli -- tenant-consent --tenant-id TU_TENANT_ID
# Abrir URL en navegador → Accept

# 4. Probar tools
curl -X POST http://localhost:5200/mcp -H "Content-Type: application/json" -H "Accept: application/json, text/event-stream" -d '{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"teams_list_teams","arguments":{}}}'
```

---

## 5. Integración con Editores

### OpenCode (recomendado)

Crear `.opencode.jsonc` en la raíz de tu proyecto:

```jsonc
{
  "$schema": "https://opencode.ai/config.json",
  "mcp": {
    "zero-teams": {
      "type": "local",
      "command": ["/ruta/completa/a/zero-teams-mcp/scripts/mcp-stdio.sh"],
      "env": {
        "AZURE_TENANT_ID": "TU_TENANT_ID",
        "AZURE_CLIENT_ID": "TU_CLIENT_ID",
        "AZURE_CLIENT_SECRET": "TU_CLIENT_SECRET",
        "CONTROLPLANE_URL": "http://localhost:5100",
        "ENCRYPTION_KEY": "TU_ENCRYPTION_KEY"
      },
      "enabled": true,
      "timeout": 30000
    }
  }
}
```

### Claude Desktop

`~/Library/Application Support/Claude/claude_desktop_config.json` (macOS)  
`%APPDATA%\Claude\claude_desktop_config.json` (Windows)

```json
{
  "mcpServers": {
    "zero-teams": {
      "command": "dotnet",
      "args": ["run", "--project", "C:\\ruta\\a\\zero-teams-mcp\\src\\ZeroTeamsMcp.Server"],
      "env": {
        "AZURE_TENANT_ID": "TU_TENANT_ID",
        "AZURE_CLIENT_ID": "TU_CLIENT_ID",
        "AZURE_CLIENT_SECRET": "TU_CLIENT_SECRET",
        "CONTROLPLANE_URL": "http://localhost:5100",
        "ENCRYPTION_KEY": "TU_ENCRYPTION_KEY"
      }
    }
  }
}
```

### Cursor / VS Code (con MCP extension)

`.vscode/mcp.json` o `.cursor/mcp.json`:

```json
{
  "mcpServers": {
    "zero-teams": {
      "command": "dotnet",
      "args": ["run", "--project", "/ruta/a/zero-teams-mcp/src/ZeroTeamsMcp.Server"],
      "env": {
        "AZURE_TENANT_ID": "TU_TENANT_ID",
        "AZURE_CLIENT_ID": "TU_CLIENT_ID",
        "AZURE_CLIENT_SECRET": "TU_CLIENT_SECRET",
        "CONTROLPLANE_URL": "http://localhost:5100",
        "ENCRYPTION_KEY": "TU_ENCRYPTION_KEY"
      }
    }
  }
}
```

---

## 6. Troubleshooting Común

| Error | Solución |
|-------|----------|
| `ENCRYPTION_KEY not set` | Generar: `openssl rand -base64 32` (Mac/Linux) o PowerShell equivalent |
| `Connection refused: 5100` | ControlPlane no corriendo → `docker compose up` o `dotnet run` |
| `Connection refused: 6379` | Redis no corriendo → `brew services start redis` / `memurai` / Docker |
| `Invalid client secret` | Regenerar en Azure AD → Certificates & secrets |
| `Admin consent required` | Azure AD > Enterprise Applications > Permissions > Grant admin consent |
| `Meeting tools fail` | Configurar Exchange Online Policy (ver `docs/MEETING_TOOLS_SETUP.md`) |
| `Port 5100/5200/3000 in use` | Cambiar puertos en `docker-compose.yml` o variables de entorno |

---

## 7. URLs Útiles

- **Panel**: http://localhost:3000
- **ControlPlane API**: http://localhost:5100
- **Swagger UI**: http://localhost:5100/swagger
- **MCP Server**: http://localhost:5200/mcp
- **Playground**: http://localhost:3000/playground
- **Metrics**: http://localhost:3000/metrics
- **Health**: http://localhost:5100/health

---

## 8. Soporte Multi-Plataforma Resumen

| Plataforma | Docker | Nativo | Notas |
|------------|--------|--------|-------|
| **macOS (Intel/Apple Silicon)** | ✅ | ✅ | `brew` para dependencias |
| **Windows 11 Pro** | ✅ | ✅ | Docker Desktop + WSL2 opcional |
| **Windows 11 Home** | ✅ | ✅ | Docker Desktop (requiere WSL2 backend) |
| **Linux (Ubuntu/Debian)** | ✅ | ✅ | `apt install dotnet-sdk-10 nodejs redis` |

**Recomendación universal**: Usa **Docker Compose** — funciona idéntico en todas las plataformas.
---

## 9. Despliegue en Producción

### 9.1 Docker Compose Production Override

Crear `deploy/docker-compose.prod.yml`:

```yaml
services:
  controlplane:
    build:
      context: ..
      dockerfile: deploy/Dockerfile.controlplane
    restart: unless-stopped
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - DATA_DIR=/data
    volumes:
      - controlplane-data:/data
      - ./logs:/app/logs
    deploy:
      resources:
        limits:
          cpus: '1.0'
          memory: 1G
        reservations:
          cpus: '0.5'
          memory: 512M

  panel:
    build:
      context: ../apps/panel
      dockerfile: Dockerfile
    restart: unless-stopped
    environment:
      - NODE_ENV=production
      - NEXT_PUBLIC_CONTROLPLANE_URL=http://controlplane:5100
    deploy:
      resources:
        limits:
          cpus: '0.5'
          memory: 512M

  mcp-server:
    build:
      context: ..
      dockerfile: deploy/Dockerfile.server
    restart: unless-stopped
    environment:
      - TRANSPORT=http
      - ASPNETCORE_ENVIRONMENT=Production
    deploy:
      resources:
        limits:
          cpus: '1.0'
          memory: 1G

  redis:
    image: redis:7-alpine
    restart: unless-stopped
    volumes:
      - redis-data:/data
    command: redis-server --appendonly yes --maxmemory 256mb --maxmemory-policy allkeys-lru

volumes:
  controlplane-data:
  redis-data:
```

Uso:
```bash
docker compose -f deploy/docker-compose.yml -f deploy/docker-compose.prod.yml up -d
```

---

### 9.2 Reverse Proxy con Caddy (SSL Automático)

`Caddyfile`:

```caddy
{
    admin off
    email admin@tudominio.com
}

# Panel
panel.tudominio.com {
    reverse_proxy panel:3000
    header {
        Strict-Transport-Security "max-age=31536000"
        X-Content-Type-Options "nosniff"
        X-Frame-Options "DENY"
    }
}

# ControlPlane API
api.tudominio.com {
    reverse_proxy controlplane:5100
    header {
        Strict-Transport-Security "max-age=31536000"
    }
}

# MCP Server
mcp.tudominio.com {
    reverse_proxy mcp-server:5200
    header {
        Strict-Transport-Security "max-age=31536000"
    }
}
```

Docker Compose addition:
```yaml
caddy:
  image: caddy:2-alpine
  restart: unless-stopped
  ports:
    - "80:80"
    - "443:443"
    - "443:443/udp"
  volumes:
    - ./Caddyfile:/etc/caddy/Caddyfile
    - caddy-data:/data
    - caddy-config:/config
  depends_on:
    - controlplane
    - panel
    - mcp-server

volumes:
  caddy-data:
  caddy-config:
```

---

### 9.3 systemd Service (Linux)

`/etc/systemd/system/zeroteams-controlplane.service`:

```ini
[Unit]
Description=ZeroTeams MCP ControlPlane
After=network.target redis.service
Requires=redis.service

[Service]
Type=notify
User=zeroteams
Group=zeroteams
WorkingDirectory=/opt/zeroteams-mcp/src/ZeroTeamsMcp.ControlPlane
ExecStart=/usr/bin/dotnet /opt/zeroteams-mcp/src/ZeroTeamsMcp.ControlPlane/ZeroTeamsMcp.ControlPlane.dll
Restart=always
RestartSec=10
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=DATA_DIR=/var/lib/zeroteams
Environment=ENCRYPTION_KEY=your-32-char-key
Environment=REDIS_CONNECTION=localhost:6379
LimitNOFILE=65536

[Install]
WantedBy=multi-user.target
```

`/etc/systemd/system/zeroteams-mcp-server.service`:

```ini
[Unit]
Description=ZeroTeams MCP Server
After=network.target zeroteams-controlplane.service
Requires=zeroteams-controlplane.service

[Service]
Type=notify
User=zeroteams
Group=zeroteams
WorkingDirectory=/opt/zeroteams-mcp/src/ZeroTeamsMcp.Server
ExecStart=/usr/bin/dotnet /opt/zeroteams-mcp/src/ZeroTeamsMcp.Server/ZeroTeamsMcp.Server.dll
Restart=always
RestartSec=10
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=TRANSPORT=http
Environment=AZURE_TENANT_ID=your-tenant-id
Environment=AZURE_CLIENT_ID=your-client-id
Environment=AZURE_CLIENT_SECRET=your-client-secret
Environment=CONTROLPLANE_URL=http://localhost:5100
Environment=ENCRYPTION_KEY=your-32-char-key
Environment=REDIS_CONNECTION=localhost:6379
LimitNOFILE=65536

[Install]
WantedBy=multi-user.target
```

Activar:
```bash
sudo useradd -r -s /bin/false zeroteams
sudo mkdir -p /var/lib/zeroteams
sudo chown zeroteams:zeroteams /var/lib/zeroteams
sudo cp zeroteams-*.service /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now zeroteams-controlplane zeroteams-mcp-server
sudo systemctl status zeroteams-controlplane zeroteams-mcp-server
```

---

### 9.4 Windows Service (NSSM)

```powershell
# Instalar NSSM
choco install nssm -y

# ControlPlane
nssm install ZeroTeamsControlPlane "C:\Program Files\dotnet\dotnet.exe" "C:\zeroteams\src\ZeroTeamsMcp.ControlPlane\ZeroTeamsMcp.ControlPlane.dll"
nssm set ZeroTeamsControlPlane AppDirectory "C:\zeroteams\src\ZeroTeamsMcp.ControlPlane"
nssm set ZeroTeamsControlPlane AppEnvironmentExtra "ASPNETCORE_ENVIRONMENT=Production;DATA_DIR=C:\zeroteams\data;ENCRYPTION_KEY=your-key;REDIS_CONNECTION=localhost:6379"
nssm set ZeroTeamsControlPlane Start SERVICE_AUTO_START
nssm set ZeroTeamsControlPlane Description "ZeroTeams MCP ControlPlane"

# MCP Server
nssm install ZeroTeamsMcpServer "C:\Program Files\dotnet\dotnet.exe" "C:\zeroteams\src\ZeroTeamsMcp.Server\ZeroTeamsMcp.Server.dll"
nssm set ZeroTeamsMcpServer AppDirectory "C:\zeroteams\src\ZeroTeamsMcp.Server"
nssm set ZeroTeamsMcpServer AppEnvironmentExtra "ASPNETCORE_ENVIRONMENT=Production;TRANSPORT=http;AZURE_TENANT_ID=xxx;AZURE_CLIENT_ID=xxx;AZURE_CLIENT_SECRET=xxx;CONTROLPLANE_URL=http://localhost:5100;ENCRYPTION_KEY=xxx"
nssm set ZeroTeamsMcpServer Start SERVICE_AUTO_START
nssm set ZeroTeamsMcpServer Description "ZeroTeams MCP Server"

# Iniciar
Start-Service ZeroTeamsControlPlane
Start-Service ZeroTeamsMcpServer
```

---

### 9.5 Nginx Reverse Proxy (Alternativa a Caddy)

`/etc/nginx/sites-available/zeroteams`:

```nginx
upstream controlplane {
    server localhost:5100;
    keepalive 32;
}

upstream mcp-server {
    server localhost:5200;
    keepalive 32;
}

upstream panel {
    server localhost:3000;
    keepalive 32;
}

server {
    listen 80;
    server_name api.tudominio.com panel.tudominio.com mcp.tudominio.com;
    return 301 https://$host$request_uri;
}

server {
    listen 443 ssl http2;
    server_name panel.tudominio.com;

    ssl_certificate /etc/letsencrypt/live/panel.tudominio.com/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/panel.tudominio.com/privkey.pem;

    location / {
        proxy_pass http://panel;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_cache_bypass $http_upgrade;
    }
}

server {
    listen 443 ssl http2;
    server_name api.tudominio.com;

    ssl_certificate /etc/letsencrypt/live/api.tudominio.com/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/api.tudominio.com/privkey.pem;

    location / {
        proxy_pass http://controlplane;
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
}

server {
    listen 443 ssl http2;
    server_name mcp.tudominio.com;

    ssl_certificate /etc/letsencrypt/live/mcp.tudominio.com/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/mcp.tudominio.com/privkey.pem;

    location / {
        proxy_pass http://mcp-server;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
}
```

---

## 10. Monitoreo y Observabilidad

### 10.1 Prometheus + Grafana

`deploy/prometheus.yml`:

```yaml
global:
  scrape_interval: 15s
  evaluation_interval: 15s

scrape_configs:
  - job_name: 'zeroteams-controlplane'
    static_configs:
      - targets: ['controlplane:5100']
    metrics_path: '/metrics'

  - job_name: 'zeroteams-mcp-server'
    static_configs:
      - targets: ['mcp-server:5200']
    metrics_path: '/metrics'

  - job_name: 'redis'
    static_configs:
      - targets: ['redis:6379']

  - job_name: 'node-exporter'
    static_configs:
      - targets: ['node-exporter:9100']
```

Docker Compose addon:
```yaml
prometheus:
  image: prom/prometheus:latest
  volumes:
    - ./deploy/prometheus.yml:/etc/prometheus/prometheus.yml
    - prometheus-data:/prometheus
  ports:
    - "9090:9090"
  command:
    - '--config.file=/etc/prometheus/prometheus.yml'
    - '--storage.tsdb.path=/prometheus'

grafana:
  image: grafana/grafana:latest
  ports:
    - "3001:3000"
  environment:
    - GF_SECURITY_ADMIN_PASSWORD=admin
    - GF_USERS_ALLOW_SIGN_UP=false
  volumes:
    - grafana-data:/var/lib/grafana
    - ./deploy/grafana-dashboards:/etc/grafana/provisioning/dashboards

node-exporter:
  image: prom/node-exporter:latest
  pid: host
  network_mode: host
  volumes:
    - /proc:/host/proc:ro
    - /sys:/host/sys:ro
    - /:/rootfs:ro

volumes:
  prometheus-data:
  grafana-data:
```

Dashboards recomendados:
- [.NET Runtime Metrics](https://grafana.com/grafana/dashboards/14230)
- [Redis](https://grafana.com/grafana/dashboards/763)
- [ASP.NET Core](https://grafana.com/grafana/dashboards/14481)

---

### 10.2 Alertas Prometheus

`deploy/alerts.yml`:

```yaml
groups:
  - name: zeroteams
    rules:
      - alert: ControlPlaneDown
        expr: up{job="zeroteams-controlplane"} == 0
        for: 1m
        labels:
          severity: critical
        annotations:
          summary: "ControlPlane down"
          description: "ControlPlane has been down for more than 1 minute"

      - alert: McpServerDown
        expr: up{job="zeroteams-mcp-server"} == 0
        for: 1m
        labels:
          severity: critical
        annotations:
          summary: "MCP Server down"

      - alert: HighMemoryUsage
        expr: (container_memory_usage_bytes / container_spec_memory_limit_bytes) > 0.85
        for: 5m
        labels:
          severity: warning
        annotations:
          summary: "High memory usage (>85%)"

      - alert: HighCPUUsage
        expr: (rate(container_cpu_usage_seconds_total[5m]) / container_spec_cpu_quota / container_spec_cpu_period) > 0.85
        for: 5m
        labels:
          severity: warning
        annotations:
          summary: "High CPU usage (>85%)"

      - alert: RedisDown
        expr: up{job="redis"} == 0
        for: 1m
        labels:
          severity: critical
        annotations:
          summary: "Redis down"
```

---

## 11. Mantenimiento y Backup

### 11.1 Backup Automático

Script `scripts/backup.sh`:

```bash
#!/bin/bash
set -euo pipefail

BACKUP_DIR="/backups/zeroteams"
DATE=$(date +%Y%m%d_%H%M%S)
RETENTION_DAYS=30

mkdir -p "$BACKUP_DIR"

# Backup SQLite DB
sqlite3 /var/lib/zeroteams/controlplane.db ".backup $BACKUP_DIR/controlplane_$DATE.db"

# Comprimir
gzip "$BACKUP_DIR/controlplane_$DATE.db"

# Eliminar antiguos
find "$BACKUP_DIR" -name "controlplane_*.db.gz" -mtime +$RETENTION_DAYS -delete

# Log
echo "[$(date)] Backup completado: controlplane_$DATE.db.gz" | tee -a /var/log/zeroteams-backup.log
```

Cron:
```bash
# /etc/cron.d/zeroteams-backup
0 3 * * * root /opt/zeroteams/scripts/backup.sh
```

---

### 11.2 Log Rotation

`/etc/logrotate.d/zeroteams`:

```conf
/var/log/zeroteams/*.log {
    daily
    rotate 30
    compress
    delaycompress
    missingok
    notifempty
    create 640 zeroteams zeroteams
    sharedscripts
    postrotate
        systemctl reload zeroteams-controlplane zeroteams-mcp-server > /dev/null 2>&1 || true
    endscript
}
```

---

### 11.3 Health Checks Automáticos

Script `scripts/health-check.sh`:

```bash
#!/bin/bash
set -euo pipefail

WEBHOOK_URL="${HEALTH_WEBHOOK_URL:-}"
ERRORS=0

check_service() {
    local name=$1
    local url=$2
    local expected=${3:-200}
    
    code=$(curl -s -o /dev/null -w "%{http_code}" --max-time 10 "$url" 2>/dev/null || echo "000")
    
    if [[ "$code" == "$expected" ]]; then
        echo "✅ $name: $code"
    else
        echo "❌ $name: $code (expected $expected)"
        ((ERRORS++))
    fi
}

check_service "ControlPlane" "http://localhost:5100/health" "200"
check_service "MCP Server" "http://localhost:5200/mcp" "200"
check_service "Panel" "http://localhost:3000" "200"
check_service "Redis" "redis://localhost:6379" "PONG"

if [[ $ERRORS -gt 0 ]]; then
    if [[ -n "$WEBHOOK_URL" ]]; then
        curl -s -X POST "$WEBHOOK_URL" \
            -H "Content-Type: application/json" \
            -d "{\"text\":\"🚨 ZeroTeams Health Check Failed: $ERRORS services down\"}" > /dev/null
    fi
    exit 1
fi

echo "✅ All services healthy"
```

Cron:
```bash
*/5 * * * * root /opt/zeroteams/scripts/health-check.sh
```

---

## 12. Seguridad Adicional

### 12.1 Firewall (ufw)

```bash
sudo ufw default deny incoming
sudo ufw default allow outgoing
sudo ufw allow 22/tcp      # SSH
sudo ufw allow 80/tcp      # HTTP (Caddy redirect)
sudo ufw allow 443/tcp     # HTTPS
sudo ufw allow from 10.0.0.0/8 to any port 6379  # Redis solo red privada
sudo ufw allow from 10.0.0.0/8 to any port 5100  # ControlPlane interno
sudo ufw allow from 10.0.0.0/8 to any port 5200  # MCP Server interno
sudo ufw enable
```

### 12.2 Fail2Ban

`/etc/fail2ban/jail.local`:

```ini
[zeroteams-auth]
enabled = true
port = http,https
filter = zeroteams-auth
logpath = /var/log/zeroteams/auth.log
maxretry = 5
bantime = 3600
findtime = 600

[zeroteams-api]
enabled = true
port = 5100,5200
filter = zeroteams-api
logpath = /var/log/zeroteams/api.log
maxretry = 10
bantime = 1800
findtime = 300
```

---

## 13. Actualizaciones

### 13.1 Actualizar a nueva versión

```bash
#!/bin/bash
# scripts/update.sh
set -euo pipefail

cd /opt/zeroteams-mcp

# Backup
./scripts/backup.sh

# Pull cambios
git pull origin main

# Rebuild images
docker compose -f deploy/docker-compose.yml -f deploy/docker-compose.prod.yml build --no-cache

# Rolling restart
docker compose -f deploy/docker-compose.yml -f deploy/docker-compose.prod.yml up -d --no-deps controlplane
sleep 10
docker compose -f deploy/docker-compose.yml -f deploy/docker-compose.prod.yml up -d --no-deps mcp-server
sleep 5
docker compose -f deploy/docker-compose.yml -f deploy/docker-compose.prod.yml up -d --no-deps panel

# Verify
sleep 5
curl -sf http://localhost:5100/health > /dev/null && echo "✅ ControlPlane OK"
curl -sf -X POST http://localhost:5200/mcp -H "Content-Type: application/json" -H "Accept: application/json, text/event-stream" -d '{"jsonrpc":"2.0","id":1,"method":"tools/list"}' > /dev/null && echo "✅ MCP Server OK"
curl -sf http://localhost:3000 > /dev/null && echo "✅ Panel OK"

echo "✅ Update complete"
```

---

## 14. Checklist de Producción

| Ítem | Verificado |
|------|------------|
| ✅ Azure AD App Registration con permisos correctos | |
| ✅ Admin Consent granted | |
| ✅ ENCRYPTION_KEY ≥ 32 chars | |
| ✅ Redis configurado y accesible | |
| ✅ SQLite DB en volumen persistente | |
| ✅ Reverse proxy con SSL (Caddy/Nginx) | |
| ✅ Firewall configurado (solo puertos necesarios) | |
| ✅ systemd/Windows Services configurados | |
| ✅ Log rotation configurado | |
| ✅ Backup automático programado | |
| ✅ Health checks + alertas | |
| ✅ Prometheus + Grafana dashboards | |
| ✅ Fail2Ban configurado | |
| ✅ Documentación de runbooks | |
| ✅ Runbook de rollback | |
| ✅ Prueba de disaster recovery | |

---

## 15. Contacto y Recursos

- **Repo**: https://github.com/zerocool-network/zero-teams-mcp
- **Issues**: https://github.com/zerocool-network/zero-teams-mcp/issues
- **Docs**: `docs/` folder
- **ADR**: `docs/adr/`
- **Arquitectura**: `docs/arquitectura.md`
- **Integración Agentes**: `docs/integracion-agentes.md`
- **Meeting Tools Setup**: `docs/MEETING_TOOLS_SETUP.md`
