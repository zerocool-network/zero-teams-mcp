<?php
$phar = '/Users/zerocool/Library/Application Support/Herd/bin/forge';
Phar::loadPhar($phar, 'forgecli.phar');
require 'phar://forgecli.phar/vendor/autoload.php';
$tok = json_decode(file_get_contents(getenv('HOME') . '/.laravel-forge/config.json'), true)['token'];
$org = 'optima-retail-sl';
$srv = 1216150;

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

$r = $c->get("orgs/$org/servers/$srv/background-processes");
$j = json_decode((string) $r->getBody(), true);
echo "HTTP " . $r->getStatusCode() . "\n";
foreach (($j['data'] ?? []) as $p) {
    echo "id=" . $p['id'] . " -> " . json_encode($p['attributes'], JSON_UNESCAPED_SLASHES) . "\n";
}
