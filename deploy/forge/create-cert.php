<?php
/**
 * Intenta crear un certificado Let's Encrypt para el dominio del site mcp-teams.
 * Uso: php deploy/forge/create-cert.php
 */
$phar = '/Users/zerocool/Library/Application Support/Herd/bin/forge';
Phar::loadPhar($phar, 'forgecli.phar');
require 'phar://forgecli.phar/vendor/autoload.php';

$tok = json_decode(file_get_contents(getenv('HOME') . '/.laravel-forge/config.json'), true)['token'];
$org = 'optima-retail-sl';
$srv = 1216150;
$sid = 3428889;
$did = 2153640;

$c = new GuzzleHttp\Client([
    'base_uri'        => 'https://forge.laravel.com/api/',
    'http_errors'     => false,
    'headers'         => [
        'Authorization' => 'Bearer ' . $tok,
        'Accept'        => 'application/vnd.api+json',
        'Content-Type'  => 'application/vnd.api+json',
        'User-Agent'    => 'Laravel Forge PHP/4.0',
    ],
    'timeout'         => 60,
    'connect_timeout' => 8,
]);

$path = "orgs/$org/servers/$srv/sites/$sid/domains/$did/certificates";

// 1) Intento simple
$r = $c->post($path, ['json' => [
    'type' => 'letsencrypt',
    'letsencrypt' => [
        'verification_method' => 'http-01',
        'key_type' => 'rsa',
    ],
]]);
echo "POST {type:letsencrypt, http-01/rsa} -> HTTP " . $r->getStatusCode() . "\n";
echo substr((string) $r->getBody(), 0, 900) . "\n\n";

// 2) Listar certificados
$r = $c->get("orgs/$org/servers/$srv/sites/$sid/certificates");
echo "GET certificates -> HTTP " . $r->getStatusCode() . "\n";
$j = json_decode((string) $r->getBody(), true);
foreach (($j['data'] ?? []) as $d) {
    echo "  id=" . $d['id'] . " " . json_encode($d['attributes'], JSON_UNESCAPED_SLASHES) . "\n";
}
