#!/usr/bin/env bash
# E2E del acceso al panel + "Mi conexión" (se ejecuta EN el servidor):
#   - guardas de sesión en ControlPlane (admin vs cliente)
#   - autoalta de API key (idempotente) y uso real contra el MCP Server
#   - middleware de Next (sin sesión -> /login)
# La cookie se firma con el mismo SESSION_SECRET derivado de ENCRYPTION_KEY,
# así se prueba todo el camino menos el round-trip de Microsoft.
set -uo pipefail

BASE=/home/forge/mcp-teams.optimaonline.eu
CP=http://127.0.0.1:5100
PANEL=http://127.0.0.1:3000
MCP=http://127.0.0.1:5200
ADMIN_EMAIL=${ADMIN_EMAIL:-noe.martinez@hifenix.com}
REGULAR_EMAIL=${REGULAR_EMAIL:-e2e-sesion@example.com}

# shellcheck disable=SC1091
set -a; . "$BASE/controlplane.env"; set +a

pass=0; fail=0
check() { # check <descripcion> <esperado> <obtenido>
  if [ "$2" = "$3" ]; then printf 'PASS  %-58s %s\n' "$1" "$3"; pass=$((pass+1));
  else printf 'FAIL  %-58s esperado=%s obtenido=%s\n' "$1" "$2" "$3"; fail=$((fail+1)); fi
}
contains() { # contains <descripcion> <subcadena> <texto>
  if printf '%s' "$3" | grep -q "$2"; then printf 'PASS  %-58s (contiene %s)\n' "$1" "$2"; pass=$((pass+1));
  else printf 'FAIL  %-58s no contiene %s\n' "$1" "$2"; fail=$((fail+1)); fi
}

cookie_for() { # cookie_for <userId> <email> -> zt_session=...
  node -e '
    const crypto = require("crypto");
    const [userId, email, encKey] = process.argv.slice(1);
    const key = crypto.createHash("sha256").update("zt-session-v1:" + encKey).digest();
    const enc = (s) => Buffer.from(s).toString("base64").replace(/=+$/, "").replace(/\+/g, "-").replace(/\//g, "_");
    const payload = enc(JSON.stringify({ u: userId, e: email, x: Math.floor(Date.now() / 1000) + 3600 }));
    const sig = crypto.createHmac("sha256", key).update(payload).digest("base64").replace(/=+$/, "").replace(/\+/g, "-").replace(/\//g, "_");
    process.stdout.write("zt_session=" + payload + "." + sig);
  ' "$1" "$2" "$ENCRYPTION_KEY"
}

json_field() { # json_field <campo> (lee JSON por stdin)
  node -e 'let d="";process.stdin.on("data",c=>d+=c).on("end",()=>{try{const v=JSON.parse(d)[process.argv[1]];process.stdout.write(v===null||v===undefined?"":String(v))}catch{process.stdout.write("")}})' "$1"
}

code() { curl -s -o /dev/null -w '%{http_code}' "$@"; }
body() { curl -s "$@"; }

ADMIN_COOKIE=$(cookie_for "e2e-admin" "$ADMIN_EMAIL")
REGULAR_COOKIE=$(cookie_for "e2e-regular" "$REGULAR_EMAIL")

echo "== ControlPlane: sesión =="
check "GET /api/session sin cookie" 401 "$(code $CP/api/session)"
check "GET /api/session admin" 200 "$(code -H "Cookie: $ADMIN_COOKIE" $CP/api/session)"
check "GET /api/session cliente" 200 "$(code -H "Cookie: $REGULAR_COOKIE" $CP/api/session)"
check "GET /api/session cookie inválida" 401 "$(code -H 'Cookie: zt_session=basura.basura' $CP/api/session)"

echo "== ControlPlane: guardas por rol =="
check "GET /api/tenants sin cookie" 401 "$(code $CP/api/tenants)"
check "GET /api/tenants cliente (antes público)" 401 "$(code -H "Cookie: $REGULAR_COOKIE" $CP/api/tenants)"
check "GET /api/tenants admin" 200 "$(code -H "Cookie: $ADMIN_COOKIE" $CP/api/tenants)"
check "GET /metrics cliente" 401 "$(code -H "Cookie: $REGULAR_COOKIE" $CP/metrics)"
check "GET /metrics admin" 200 "$(code -H "Cookie: $ADMIN_COOKIE" $CP/metrics)"
check "GET /health/detailed cliente" 401 "$(code -H "Cookie: $REGULAR_COOKIE" $CP/health/detailed)"
check "GET /auth/users sin cookie (antes filtraba)" 401 "$(code $CP/auth/users)"
check "GET /auth/users admin" 200 "$(code -H "Cookie: $ADMIN_COOKIE" $CP/auth/users)"
check "GET /auth/token de otro sin cookie" 401 "$(code $CP/auth/token/e2e-admin)"
check "GET /auth/token de otro (cliente)" 401 "$(code -H "Cookie: $REGULAR_COOKIE" $CP/auth/token/e2e-admin)"
check "GET /health sigue público" 200 "$(code $CP/health)"

echo "== Mi conexión: autoalta de API key =="
check "GET /api/session/connection sin cookie" 401 "$(code $CP/api/session/connection)"
CONN=$(body -H "Cookie: $REGULAR_COOKIE" $CP/api/session/connection)
API_KEY=$(printf '%s' "$CONN" | json_field apiKey)
contains "connection devuelve email" "$REGULAR_EMAIL" "$CONN"
contains "connection devuelve apiKey" '"apiKey":"zk_' "$CONN"
contains "connection devuelve serverUrl" '"serverUrl":"https://mcp-teams.optimaonline.eu/mcp"' "$CONN"
CONN2=$(body -H "Cookie: $REGULAR_COOKIE" $CP/api/session/connection)
API_KEY2=$(printf '%s' "$CONN2" | json_field apiKey)
check "segunda llamada: keyCreated=false" "false" "$(printf '%s' "$CONN2" | grep -o '"keyCreated":[a-z]*' | cut -d: -f2)"
check "segunda llamada: misma key (idempotente)" "$API_KEY" "$API_KEY2"

echo "== La API key funciona contra el MCP Server =="
mcp_call() { # mcp_call <json> -> respuesta con \n reales
  local out; out=$(curl -s "$MCP/mcp" \
    -H "X-Api-Key: $API_KEY" \
    -H 'Content-Type: application/json' \
    -H 'Accept: application/json, text/event-stream' \
    -d "$1")
  printf '%b' "$out"
}
TOOLS=$(mcp_call '{"jsonrpc":"2.0","id":1,"method":"tools/list"}')
contains "tools/list con la key del panel" "teams_list_chats" "$TOOLS"
ESCRITURA=$(mcp_call '{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"teams_send_message","arguments":{"chatId":"e2e","content":"x"}}}')
contains "escritura denegada para key de cliente" "administrador" "$ESCRITURA"

echo "== Panel: middleware y rutas =="
check "GET /panel sin cookie" 307 "$(code $PANEL/panel)"
check "GET /panel/connection sin cookie" 307 "$(code $PANEL/panel/connection)"
check "GET /panel/tenants sin cookie" 307 "$(code $PANEL/panel/tenants)"
check "GET /panel/login sin cookie" 200 "$(code $PANEL/panel/login)"
check "GET /panel con sesión admin" 200 "$(code -H "Cookie: $ADMIN_COOKIE" $PANEL/panel)"
check "GET /panel/tenants con sesión admin" 200 "$(code -H "Cookie: $ADMIN_COOKIE" $PANEL/panel/tenants)"
check "GET /panel/connection con sesión cliente" 200 "$(code -H "Cookie: $REGULAR_COOKIE" $PANEL/panel/connection)"
check "Location del redirect apunta a /panel/login" \
  "1" "$(curl -sD - -o /dev/null $PANEL/panel/tenants | grep -ci 'location:.*/panel/login')"
check "Location no filtra localhost" \
  "0" "$(curl -sD - -o /dev/null -H 'X-Forwarded-Host: mcp-teams.optimaonline.eu' -H 'X-Forwarded-Proto: https' $PANEL/panel/tenants | grep -ci 'localhost')"

echo "== Logout =="
check "POST /api/session/logout" 200 "$(code -X POST -H "Cookie: $ADMIN_COOKIE" $CP/api/session/logout)"
check "Cookie borrada en la respuesta" "1" "$(curl -sD - -o /dev/null -X POST -H "Cookie: $ADMIN_COOKIE" $CP/api/session/logout | grep -ci 'zt_session=;')"

echo
echo "RESULTADO: $pass PASS / $fail FAIL"
[ "$fail" -eq 0 ]
