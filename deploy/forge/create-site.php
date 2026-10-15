<?php
/**
 * Crea el site mcp-teams.optimaonline.eu en optimafront-1 (server 1216150)
 * vía Forge API. Idempotente: si ya existe, no hace nada.
 *
 * Uso:  php deploy/forge/create-site.php
 */

$phar = '/Users/zerocool/Library/Application Support/Herd/bin/forge';
Phar::loadPhar($phar, 'forgecli.phar');
require 'phar://forgecli.phar/vendor/autoload.php';

$cfg  = json_decode(file_get_contents(getenv('HOME') . '/.laravel-forge/config.json'), true);
$tok  = $cfg['token'];
$org  = 'optima-retail-sl';
$srv  = 1216150;
$name = 'mcp-teams.optimaonline.eu';

$baseHeaders = [
    'Authorization' => 'Bearer ' . $tok,
    'User-Agent'    => 'Laravel Forge PHP/4.0',
];

$old = new GuzzleHttp\Client([
    'http_errors'     => false,
    'headers'         => $baseHeaders + ['Accept' => 'application/json', 'Content-Type' => 'application/json'],
    'timeout'         => 30,
    'connect_timeout' => 8,
]);

$new = new GuzzleHttp\Client([
    'base_uri'        => 'https://forge.laravel.com/api/',
    'http_errors'     => false,
    'headers'         => $baseHeaders + ['Accept' => 'application/vnd.api+json', 'Content-Type' => 'application/vnd.api+json'],
    'timeout'         => 30,
    'connect_timeout' => 8,
]);

function listSites($new, $org, $srv)
{
    $r = $new->get("orgs/$org/servers/$srv/sites");
    $j = json_decode((string) $r->getBody(), true);
    $out = [];
    foreach (($j['data'] ?? []) as $s) {
        $out[] = ['id' => $s['id'], 'name' => $s['attributes']['name'] ?? '?'];
    }
    return $out;
}

echo "== Sites actuales en server $srv ==\n";
foreach (listSites($new, $org, $srv) as $s) {
    echo "  id={$s['id']}  {$s['name']}\n";
}

foreach (listSites($new, $org, $srv) as $s) {
    if ($s['name'] === $name) {
        echo "\n>>> YA EXISTE: id={$s['id']} — nada que hacer.\n";
        exit(0);
    }
}

echo "\n== Intento API antigua (/api/v2) ==\n";
$r = $old->post("https://forge.laravel.com/api/v2/servers/$srv/sites", ['json' => [
    'name'         => $name,
    'project_type' => 'php',
    'php_version'  => 'php83',
]]);
$code = $r->getStatusCode();
echo "HTTP $code\n" . substr((string) $r->getBody(), 0, 500) . "\n";

if ($code < 200 || $code >= 300) {
    echo "\n== Intento API nueva (probar domain_mode) ==\n";
    foreach (['subdomain', 'custom', 'alias', 'www', 'new', 'existing', 'none', 'apex', 'active', 'primary'] as $dm) {
        $r = $new->post("orgs/$org/servers/$srv/sites", ['json' => [
            'type'        => 'php',
            'domain_mode' => $dm,
            'name'        => $name,
        ]]);
        $code = $r->getStatusCode();
        echo "domain_mode=$dm -> HTTP $code " . substr((string) $r->getBody(), 0, 220) . "\n";
        if ($code >= 200 && $code < 300) {
            echo ">>> CREADO con domain_mode=$dm\n";
            break;
        }
    }
}

echo "\n== Sites tras el intento ==\n";
foreach (listSites($new, $org, $srv) as $s) {
    echo "  id={$s['id']}  {$s['name']}\n";
}
