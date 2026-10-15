<?php
/**
 * Escribe el nginx del site mcp-teams (proxy /mcp -> 5200, / -> 5100).
 * Uso: php deploy/forge/put-nginx.php
 */

$phar = '/Users/zerocool/Library/Application Support/Herd/bin/forge';
Phar::loadPhar($phar, 'forgecli.phar');
require 'phar://forgecli.phar/vendor/autoload.php';

$tok = json_decode(file_get_contents(getenv('HOME') . '/.laravel-forge/config.json'), true)['token'];
$org = 'optima-retail-sl';
$srv = 1216150;
$sid = 3428889;

$c = new GuzzleHttp\Client([
    'base_uri'        => 'https://forge.laravel.com/api/',
    'http_errors'     => false,
    'headers'         => [
        'Authorization' => 'Bearer ' . $tok,
        'Accept'        => 'application/vnd.api+json',
        'Content-Type'  => 'application/vnd.api+json',
        'User-Agent'    => 'Laravel Forge PHP/4.0',
    ],
    'timeout'         => 30,
    'connect_timeout' => 8,
]);

$config = <<<'NGINX'
ssl_protocols TLSv1.2 TLSv1.3;
ssl_ciphers ECDHE-ECDSA-AES128-GCM-SHA256:ECDHE-RSA-AES128-GCM-SHA256:ECDHE-ECDSA-AES256-GCM-SHA384:ECDHE-RSA-AES256-GCM-SHA384:ECDHE-ECDSA-CHACHA20-POLY1305:ECDHE-RSA-CHACHA20-POLY1305:DHE-RSA-AES128-GCM-SHA256:DHE-RSA-AES256-GCM-SHA384;
ssl_prefer_server_ciphers off;
ssl_dhparam /etc/nginx/dhparams.pem;

add_header X-Frame-Options "SAMEORIGIN";
add_header X-XSS-Protection "1; mode=block";
add_header X-Content-Type-Options "nosniff";

charset utf-8;

# FORGE CONFIG (DO NOT REMOVE!)
include forge-conf/3428889/server/*;

# MCP Server (streamable HTTP) -> 127.0.0.1:5200
location /mcp {
    proxy_pass http://127.0.0.1:5200;
    proxy_http_version 1.1;
    proxy_set_header Upgrade $http_upgrade;
    proxy_set_header Connection "upgrade";
    proxy_set_header Host $host;
    proxy_set_header X-Real-IP $remote_addr;
    proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
    proxy_set_header X-Forwarded-Proto $scheme;
    proxy_read_timeout 3600s;
    proxy_send_timeout 3600s;
    proxy_buffering off;
    proxy_cache off;
}

# Panel Next.js (basePath=/panel) -> 127.0.0.1:3000
location /panel {
    proxy_pass http://127.0.0.1:3000;
    proxy_http_version 1.1;
    proxy_set_header Upgrade $http_upgrade;
    proxy_set_header Connection "upgrade";
    proxy_set_header Host $host;
    proxy_set_header X-Real-IP $remote_addr;
    proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
    proxy_set_header X-Forwarded-Proto $scheme;
    proxy_read_timeout 120s;
    proxy_send_timeout 120s;
}

# ControlPlane (API/registration/health/oauth/metrics) -> 127.0.0.1:5100
location / {
    proxy_pass http://127.0.0.1:5100;
    proxy_http_version 1.1;
    proxy_set_header Host $host;
    proxy_set_header X-Real-IP $remote_addr;
    proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
    proxy_set_header X-Forwarded-Proto $scheme;
}

access_log off;
error_log  /var/log/nginx/3428889-error.log error;

location ~ /\.(?!well-known).* {
    deny all;
}
NGINX;

$r = $c->put("orgs/$org/servers/$srv/sites/$sid/nginx", ['json' => ['config' => $config]]);
echo "PUT nginx -> HTTP " . $r->getStatusCode() . "\n" . substr((string) $r->getBody(), 0, 300) . "\n";

// Releer para confirmar
$r = $c->get("orgs/$org/servers/$srv/sites/$sid/nginx");
$j = json_decode((string) $r->getBody(), true);
$content = $j['data']['attributes']['content'] ?? '';
echo "\n-- contenido guardado (primeras lineas) --\n";
echo implode("\n", array_slice(explode("\n", $content), 0, 8)) . "\n";
echo (strpos($content, 'proxy_pass http://127.0.0.1:5200') !== false ? "OK: proxy 5200 presente\n" : "FALLO: sin proxy 5200\n");
