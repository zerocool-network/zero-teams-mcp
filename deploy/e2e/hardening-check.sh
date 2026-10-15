#!/usr/bin/env bash
# Verificación E2E del hardening cloud: registro con ADMIN_TOKEN, escritura solo
# admin y scoping de lectura. Sin secretos: se reciben por env.
#
# Uso (local, contra prod):
#   BASE_URL=https://mcp-teams.optimaonline.eu ADMIN_TOKEN=... ADMIN_KEY=zk_... \
#   ./deploy/e2e/hardening-check.sh
#
# Uso (desde el servidor, contra los daemons locales — evita el borde CF):
#   CP_URL=http://127.0.0.1:5100 MCP_URL=http://127.0.0.1:5200 \
#   ADMIN_TOKEN=... ADMIN_KEY=zk_... ./hardening-check.sh
set -u

BASE_URL=${BASE_URL:-https://mcp-teams.optimaonline.eu}
CP_URL=${CP_URL:-$BASE_URL}
MCP_URL=${MCP_URL:-$BASE_URL}
ADM_TOK=${ADMIN_TOKEN:?falta ADMIN_TOKEN}
ADM_KEY=${ADMIN_KEY:?falta ADMIN_KEY}
TEST_EMAIL="hardening-test-$(date +%s)@example.com"
OTHER_EMAIL="otro-usuario-$(date +%s)@example.com"
PASS=0
FAIL=0

ok()  { echo "  PASS: $1"; PASS=$((PASS + 1)); }
bad() { echo "  FAIL: $1"; echo "        salida: ${2:-<vacío>}"; FAIL=$((FAIL + 1)); }

# initialize + notifications/initialized + tools/call. Devuelve el payload JSON.
_mcp_once() { # $1=apikey $2=tool $3=args-json
  local key="$1" tool="$2" args="$3" hdrs sid body raw extra=""
  hdrs=$(mktemp)
  curl -sS -D "$hdrs" -o /dev/null --max-time 20 -X POST "$MCP_URL/mcp" \
    -H 'Content-Type: application/json' -H 'Accept: application/json, text/event-stream' \
    -H "X-Api-Key: $key" \
    -d '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"e2e","version":"1.0"}}}'
  sid=$(grep -i '^mcp-session-id:' "$hdrs" | tr -d '\r' | awk '{print $2}')
  rm -f "$hdrs"
  # SDK 2.2+ usa SessionMode=Stateless por defecto: initialize no devuelve
  # MCP-Session-Id y el servidor no guarda estado entre peticiones. En ese caso
  # se sigue sin sesión (antes el script abortaba y los tests salían vacíos).
  if [ -n "$sid" ]; then
    extra="Mcp-Session-Id: $sid"
    curl -sS -o /dev/null --max-time 20 -X POST "$MCP_URL/mcp" \
      -H 'Content-Type: application/json' -H 'Accept: application/json, text/event-stream' \
      -H "X-Api-Key: $key" -H "Mcp-Session-Id: $sid" \
      -d '{"jsonrpc":"2.0","method":"notifications/initialized"}'
  fi
  body=$(mktemp)
  if [ -n "$extra" ]; then
    curl -sS --max-time 30 -o "$body" -X POST "$MCP_URL/mcp" \
      -H 'Content-Type: application/json' -H 'Accept: application/json, text/event-stream' \
      -H "X-Api-Key: $key" -H "$extra" \
      -d "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/call\",\"params\":{\"name\":\"$tool\",\"arguments\":$args}}"
  else
    curl -sS --max-time 30 -o "$body" -X POST "$MCP_URL/mcp" \
      -H 'Content-Type: application/json' -H 'Accept: application/json, text/event-stream' \
      -H "X-Api-Key: $key" \
      -d "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/call\",\"params\":{\"name\":\"$tool\",\"arguments\":$args}}"
  fi
  raw=$(cat "$body")
  rm -f "$body"
  # El server emite el frame SSE en una sola línea con \n literales: interpretar.
  printf '%b' "$raw" | sed -n 's/^data: *//p'
}

mcp_call() { # reintenta con sesión nueva si la respuesta llega vacía
  local out="" i
  for i in 1 2 3; do
    out=$(_mcp_once "$1" "$2" "$3")
    [ -n "$out" ] && break
    sleep 1
  done
  printf '%s\n' "$out"
}

echo "== 1. Registro abierto bloqueado =="
CODE=$(curl -sS -o /dev/null -w '%{http_code}' --max-time 20 -X POST "$CP_URL/api/clients/register" \
  -H 'Content-Type: application/json' -d "{\"email\":\"$TEST_EMAIL\"}")
[ "$CODE" = "401" ] && ok "POST /register sin ADMIN_TOKEN -> 401" \
  || bad "POST /register sin token -> $CODE (esperado 401)"

echo "== 2. Registro con ADMIN_TOKEN crea cliente regular =="
REG=$(curl -sS --max-time 20 -X POST "$CP_URL/api/clients/register" \
  -H 'Content-Type: application/json' \
  -d "{\"email\":\"$TEST_EMAIL\",\"adminToken\":\"$ADM_TOK\"}")
REG_KEY=$(echo "$REG" | sed -n 's/.*"apiKey":"\([^"]*\)".*/\1/p')
[ -n "$REG_KEY" ] && ok "cliente regular registrado: $TEST_EMAIL" || bad "registro regular" "$REG"

echo "== 3. Registro con flag --admin crea cliente admin =="
ADM2=$(curl -sS --max-time 20 -X POST "$CP_URL/api/clients/register" \
  -H 'Content-Type: application/json' \
  -d "{\"email\":\"$TEST_EMAIL\",\"adminToken\":\"$ADM_TOK\",\"admin\":true}")
ADM2_KEY=$(echo "$ADM2" | sed -n 's/.*"apiKey":"\([^"]*\)".*/\1/p')
ADM2_EMAIL=$(echo "$ADM2" | sed -n 's/.*"email":"\([^"]*\)".*/\1/p')
[ -n "$ADM2_KEY" ] && [ "$ADM2_EMAIL" = "*" ] && ok "cliente admin (*) registrado" \
  || bad "registro admin" "$ADM2"

echo "== 4. Cliente regular: escritura denegada =="
OUT=$(mcp_call "$REG_KEY" teams_send_message '{"chatId":"11111111-1111-1111-1111-111111111111","content":"hola"}')
echo "$OUT" | grep -qi 'permisos de administrador' \
  && ok "teams_send_message denegado a cliente regular" || bad "teams_send_message" "$OUT"

echo "== 5. Cliente regular: lectura de calendario ajeno denegada =="
OUT=$(mcp_call "$REG_KEY" teams_list_calendar_events "{\"userId\":\"$OTHER_EMAIL\"}")
echo "$OUT" | grep -qiE 'solo puede operar sobre su propio|limitado a su propio|no autorizado|Unauthorized' \
  && ok "teams_list_calendar_events de otro usuario denegado" || bad "teams_list_calendar_events" "$OUT"

echo "== 6. Admin: lectura de calendario ajeno permitida (llega a Graph) =="
OUT=$(mcp_call "$ADM_KEY" teams_list_calendar_events "{\"userId\":\"$OTHER_EMAIL\"}")
if [ -z "$OUT" ]; then
  bad "admin: sin respuesta MCP" "$OUT"
elif echo "$OUT" | grep -qi 'solo puede operar sobre su propio'; then
  bad "admin bloqueado por scoping" "$OUT"
else
  ok "admin no bloqueado por scoping"
fi

echo
echo "RESULTADO: PASS=$PASS FAIL=$FAIL"
[ "$FAIL" -eq 0 ]
