#!/usr/bin/env python3
"""
Smoke test del módulo insider_* contra el servidor MCP en stdio.

No lee ningún buzón ni chat: valida el gobierno del caso (doble aprobación,
custodios, kill switch, cadena de auditoría) y las negativas.

Uso:
    python3 scripts/insider-smoke-test.py
"""
import json
import os
import subprocess
import sys
import time
import urllib.error
import urllib.request

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SERVER = os.path.join(ROOT, "src", "ZeroTeamsMcp.Insider")

APPROVER = "smoke-approver-token"
REQUESTER = "miguel@optimaretail.es"
APPROVER_EMAIL = "legal@optimaretail.es"
CUSTODIAN = "sospechoso@optimaretail.es"


def load_env():
    env = dict(os.environ)
    dotenv = os.path.join(ROOT, ".env")
    if os.path.exists(dotenv):
        for line in open(dotenv, encoding="utf-8"):
            line = line.strip()
            if not line or line.startswith("#") or "=" not in line:
                continue
            k, v = line.split("=", 1)
            env.setdefault(k.strip(), v.strip())

    env.update({
        "TRANSPORT": "stdio",
        "ADMIN_EMAIL": REQUESTER,
        "INSIDER_APPROVER_TOKEN": APPROVER,
        "INSIDER_ORG_DOMAINS": "optimaretail.es",
        "INSIDER_TOOLS": "on",
        "INSIDER_DB_PATH": os.path.join(ROOT, "data", "smoke-insider.db"),
        "INSIDER_ACTOR": REQUESTER,
    })
    return env


class Mcp:
    def __init__(self, env):
        self.proc = subprocess.Popen(
            ["dotnet", "run", "--project", SERVER, "-v", "q", "--nologo"],
            stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL,
            text=True, bufsize=1, env=env, cwd=ROOT)
        self.next_id = 1

    def send(self, method, params=None):
        msg = {"jsonrpc": "2.0", "id": self.next_id, "method": method}
        if params is not None:
            msg["params"] = params
        self.next_id += 1
        self.proc.stdin.write(json.dumps(msg) + "\n")
        self.proc.stdin.flush()
        return msg["id"]

    def notify(self, method, params=None):
        msg = {"jsonrpc": "2.0", "method": method}
        if params is not None:
            msg["params"] = params
        self.proc.stdin.write(json.dumps(msg) + "\n")
        self.proc.stdin.flush()

    def read_until(self, target_id, timeout_lines=4000):
        for _ in range(timeout_lines):
            line = self.proc.stdout.readline()
            if not line:
                raise RuntimeError("servidor cerró stdout")
            line = line.strip()
            if not line.startswith("{"):
                continue
            try:
                msg = json.loads(line)
            except json.JSONDecodeError:
                continue
            if msg.get("id") == target_id:
                return msg
        raise RuntimeError(f"sin respuesta para id {target_id}")

    def call(self, name, arguments):
        rid = self.send("tools/call", {"name": name, "arguments": arguments})
        msg = self.read_until(rid)
        if "error" in msg:
            return {"error": msg["error"]}
        result = msg.get("result", {})
        text = "".join(c.get("text", "") for c in result.get("content", []))
        return {"isError": result.get("isError", False), "text": text}

    def close(self):
        try:
            self.proc.stdin.close()
        except Exception:
            pass
        self.proc.terminate()
        try:
            self.proc.wait(timeout=10)
        except subprocess.TimeoutExpired:
            self.proc.kill()



def http_auth_checks(check):
    """Arranca el servidor en modo HTTP y verifica el control de acceso."""
    print("\n[10] Transporte HTTP: control de acceso")
    env = load_env()
    env.update({
        "TRANSPORT": "http",
        "INSIDER_KEYS": "tester=clave-de-prueba-1234567890",
        "INSIDER_BIND": "http://127.0.0.1:5399",
    })

    proc = subprocess.Popen(
        ["dotnet", "run", "--project", SERVER, "-v", "q", "--nologo"],
        stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
        text=True, env=env, cwd=ROOT)
    try:
        base = "http://127.0.0.1:5399"
        up = False
        for _ in range(120):
            try:
                with urllib.request.urlopen(base + "/health", timeout=1) as r:
                    up = r.status == 200
                    break
            except Exception:
                time.sleep(0.5)

        check("servidor HTTP arranca", up, "no respondió /health en 60s")
        if not up:
            return

        payload = json.dumps({"jsonrpc": "2.0", "id": 1, "method": "initialize",
                              "params": {"protocolVersion": "2024-11-05", "capabilities": {},
                                         "clientInfo": {"name": "smoke", "version": "1"}}}).encode()

        def post(headers):
            req = urllib.request.Request(base + "/mcp", data=payload, method="POST",
                                         headers={"Content-Type": "application/json",
                                                  "Accept": "application/json, text/event-stream",
                                                  **headers})
            try:
                with urllib.request.urlopen(req, timeout=10) as r:
                    return r.status
            except urllib.error.HTTPError as e:
                return e.code
            except Exception:
                return -1

        check("sin clave -> 401", post({}) == 401, str(post({})))
        check("clave incorrecta -> 401", post({"X-Insider-Key": "clave-mala"}) == 401,
              str(post({"X-Insider-Key": "clave-mala"})))
        ok = post({"X-Insider-Key": "clave-de-prueba-1234567890"})
        check("clave válida -> autenticado", ok not in (401, 403), f"status={ok}")
    finally:
        proc.terminate()
        try:
            proc.wait(timeout=15)
        except subprocess.TimeoutExpired:
            proc.kill()


def main():
    db = os.path.join(ROOT, "data", "smoke-insider.db")
    for suffix in ("", "-wal", "-shm"):
        try:
            os.remove(db + suffix)
        except FileNotFoundError:
            pass

    mcp = Mcp(load_env())
    results = []

    def check(label, condition, detail=""):
        results.append((label, bool(condition), detail))
        print(("  PASS  " if condition else "  FAIL  ") + label + (f"  [{detail}]" if detail and not condition else ""))

    try:
        rid = mcp.send("initialize", {
            "protocolVersion": "2024-11-05",
            "capabilities": {},
            "clientInfo": {"name": "insider-smoke", "version": "1"}
        })
        mcp.read_until(rid)
        mcp.notify("notifications/initialized")

        rid = mcp.send("tools/list")
        tools = [t["name"] for t in mcp.read_until(rid)["result"]["tools"]]
        insider = sorted(t for t in tools if t.startswith("insider_"))
        print(f"\nTools insider_* registrados: {len(insider)}")
        for t in insider:
            print(f"  - {t}")

        check("12 tools insider_* registrados", len(insider) == 12, str(len(insider)))

        print("\n[1] Diagnóstico de permisos")
        r = mcp.call("insider_permissions_check", {})
        check("permissions_check responde", not r["isError"], r["text"][:200])
        if not r["isError"]:
            data = json.loads(r["text"])
            roles = data.get("rolesGranted", [])
            check("token tiene Mail.Read", "Mail.Read" in roles, str(roles))
            check("capability audit_search marcada no disponible", any(
                c["tool"] == "insider_audit_search" and not c["available"]
                for c in data.get("capabilities", [])), "")

        print("\n[2] Doble aprobación")
        base = {
            "name": "Smoke test",
            "justification": "Validacion del flujo de gobierno del modulo insider",
            "custodians": CUSTODIAN,
            "approvedBy": APPROVER_EMAIL,
        }
        r = mcp.call("insider_case_open", {**base, "approverToken": "token-incorrecto"})
        check("token de aprobador inválido -> denegado", r["isError"], r["text"][:160])

        r = mcp.call("insider_case_open", {
            **base, "approvedBy": REQUESTER, "approverToken": APPROVER})
        check("mismo sujeto solicita y aprueba -> denegado", r["isError"], r["text"][:160])

        r = mcp.call("insider_case_open", {
            **base, "justification": "corto", "approverToken": APPROVER})
        check("justificación insuficiente -> denegada", r["isError"], r["text"][:160])

        print("\n[3] Apertura válida")
        r = mcp.call("insider_case_open", {**base, "approverToken": APPROVER})
        check("caso abierto", not r["isError"], r["text"][:300])
        case_id = json.loads(r["text"])["caseId"] if not r["isError"] else None
        print(f"  caseId = {case_id}")

        print("\n[4] Custodio fuera del caso")
        r = mcp.call("insider_mail_search", {
            "caseId": case_id, "custodian": "otro@optimaretail.es"})
        check("custodio no nominado -> denegado", r["isError"], r["text"][:160])

        print("\n[5] Caso inexistente")
        r = mcp.call("insider_mail_search", {
            "caseId": "CASE-00000000-DEAD", "custodian": CUSTODIAN})
        check("caso inexistente -> denegado", r["isError"], r["text"][:160])

        print("\n[6] Ventana temporal fuera de límite")
        r = mcp.call("insider_mail_search", {
            "caseId": case_id, "custodian": CUSTODIAN,
            "from": "2025-01-01", "to": "2026-01-01"})
        check("ventana > 90 días -> denegada", r["isError"], r["text"][:160])

        print("\n[7] Integridad de la cadena de auditoría")
        r = mcp.call("insider_audit_verify", {})
        check("cadena íntegra", not r["isError"] and json.loads(r["text"]).get("chainValid") is True, r["text"][:200])

        r = mcp.call("insider_audit_trail", {"caseId": case_id})
        entries = json.loads(r["text"])["entries"] if not r["isError"] else []
        check("log contiene la apertura del caso", len(entries) >= 1, str(len(entries)))
        check("hay negativas registradas", any(e["outcome"] == "denied" for e in mcp_entries(mcp)), "")

        print("\n[8] Cierre requiere aprobador")
        r = mcp.call("insider_case_close", {
            "caseId": case_id, "reason": "fin smoke", "approverToken": "malo"})
        check("cierre sin token -> denegado", r["isError"], r["text"][:160])

        r = mcp.call("insider_case_close", {
            "caseId": case_id, "reason": "fin smoke", "approverToken": APPROVER})
        check("cierre con token -> ok", not r["isError"], r["text"][:200])

        print("\n[9] Caso cerrado ya no permite consultas")
        r = mcp.call("insider_mail_search", {"caseId": case_id, "custodian": CUSTODIAN})
        check("caso cerrado -> denegado", r["isError"], r["text"][:160])

    finally:
        mcp.close()

    http_auth_checks(check)

    failed = [r for r in results if not r[1]]
    print(f"\n{len(results) - len(failed)}/{len(results)} comprobaciones OK")
    return 1 if failed else 0


def mcp_entries(mcp):
    r = mcp.call("insider_audit_trail", {})
    if r["isError"]:
        return []
    return json.loads(r["text"])["entries"]


if __name__ == "__main__":
    sys.exit(main())
