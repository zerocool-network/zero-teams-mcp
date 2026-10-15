<?php
$phar = '/Users/zerocool/Library/Application Support/Herd/bin/forge';
Phar::loadPhar($phar, 'forgecli.phar');
require 'phar://forgecli.phar/vendor/autoload.php';
$tok = json_decode(file_get_contents(getenv('HOME') . '/.laravel-forge/config.json'), true)['token'];
$org = 'optima-retail-sl';
$srv = 1216150;
$base = '/home/forge/mcp-teams.optimaonline.eu';

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

$existing = [];
$r = $c->get("orgs/$org/servers/$srv/background-processes");
$j = json_decode((string) $r->getBody(), true);
foreach (($j['data'] ?? []) as $p) {
    $existing[$p['attributes']['command'] ?? ''] = $p['id'];
}

$daemons = [
    'mcp-controlplane' => "$base/controlplane.run.sh",
    'mcp-server'       => "$base/server.run.sh",
    'mcp-panel'        => "$base/panel.run.sh",
];

foreach ($daemons as $name => $command) {
    if (isset($existing[$command])) {
        echo "[$name] YA EXISTE id={$existing[$command]}\n";
        continue;
    }
    $r = $c->post("orgs/$org/servers/$srv/background-processes", ['json' => [
        'name'      => $name,
        'command'   => $command,
        'user'      => 'forge',
        'directory' => $base,
        'processes' => 1,
    ]]);
    echo "[$name] POST -> HTTP " . $r->getStatusCode() . " " . substr((string) $r->getBody(), 0, 300) . "\n";
}

echo "\n== Background processes ahora ==\n";
$r = $c->get("orgs/$org/servers/$srv/background-processes");
$j = json_decode((string) $r->getBody(), true);
foreach (($j['data'] ?? []) as $p) {
    echo "  id={$p['id']} status=" . ($p['attributes']['status'] ?? '?') . " cmd=" . ($p['attributes']['command'] ?? '?') . "\n";
}
