<?php
$phar = '/Users/zerocool/Library/Application Support/Herd/bin/forge';
Phar::loadPhar($phar, 'forgecli.phar');
require 'phar://forgecli.phar/vendor/autoload.php';
$tok = json_decode(file_get_contents(getenv('HOME') . '/.laravel-forge/config.json'), true)['token'];
$org = 'optima-retail-sl'; $srv = 1216150; $sid = 3428889;
$c = new GuzzleHttp\Client([
    'base_uri' => 'https://forge.laravel.com/api/', 'http_errors' => false,
    'headers' => ['Authorization' => 'Bearer ' . $tok, 'Accept' => 'application/vnd.api+json',
        'Content-Type' => 'application/vnd.api+json', 'User-Agent' => 'Laravel Forge PHP/4.0'],
    'timeout' => 30, 'connect_timeout' => 8,
]);
for ($i = 1; $i <= 24; $i++) {
    $r = $c->get("orgs/$org/servers/$srv/sites/$sid/certificates");
    $j = json_decode((string) $r->getBody(), true);
    $d = $j['data'][0] ?? null;
    if (!$d) { echo "sin certs\n"; break; }
    $a = $d['attributes'];
    echo "[$i] status=" . $a['status'] . " request_status=" . $a['request_status'] . " active=" . var_export($a['active'], true) . "\n";
    if ($a['active'] === true || ($a['request_status'] ?? '') === 'failed') {
        break;
    }
    sleep(10);
}
