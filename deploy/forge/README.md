# deploy/forge

Scripts PHP (Guzzle) para automatizar el despliegue en Laravel Forge vía API.
Complementan a los run scripts del servidor (`*.run.sh`) y a `deploy/.env`.

## Requisitos

- Token de Forge en `~/.laravel-forge/config.json` (clave `token`).
- El phar del CLI de Forge (viene con Herd):
  `/Users/zerocool/Library/Application Support/Herd/bin/forge`.
- PHP de Herd con extensión `phar`:
  `fish -lc 'php deploy/forge/<script>.php'`.

Cada script tiene al principio las constantes `$org`, `$srv` (server id), `$sid`
(site id) y `$base` (ruta del site en el servidor). Ajústalas por entorno.

## Scripts

| Script | Para qué |
|---|---|
| `create-site.php` | Crea (idempotente) el site con dominio custom. |
| `put-nginx.php` | Escribe las directivas nginx del site: `/panel`→3000, `/mcp`→5200, `/`→5100. |
| `create-bg.php` | Crea los daemons supervisor (ControlPlane, Server, Panel). |
| `create-cert.php` / `poll-cert.php` | Emite el certificado Let's Encrypt (HTTP-01) y lo sondea. |
| `bg-list.php` | Lista los background processes y su estado. |
| `recheck.php` | Relee site + nginx + cert para verificar el estado. |

Los despliegues de código (ControlPlane, Server, Panel) se hacen por `rsync` +
reinicio del daemon correspondiente; ver `docs/CLOUD.md`.
