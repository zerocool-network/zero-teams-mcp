<?php
/**
 * Re-chequea el site nuevo (atributos completos) + su nginx.
 * Uso: php deploy/forge/recheck.php
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

$r = $c->get("orgs/$org/servers/$srv/sites");
$j = json_decode((string) $r->getBody(), true);
foreach (($j['data'] ?? []) as $s) {
    if ((string) $s['id'] === (string) $sid) {
        echo "== ATRIBUTOS site $sid ==\n";
        echo json_encode($s['attributes'], JSON_PRETTY_PRINT | JSON_UNESCAPED_SLASHES) . "\n";
    }
}

echo "\n== NGINX site $sid ==\n";
$r = $c->get("orgs/$org/servers/$srv/sites/$sid/nginx");
$j = json_decode((string) $r->getBody(), true);
echo ($j['data']['attributes']['content'] ?? var_export($j, true)) . "\n";
