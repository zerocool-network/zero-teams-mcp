#!/usr/bin/env bash
#
# ZeroTeams MCP — Setup wizard
# Walks you through Azure AD app registration + local config.
#

set -euo pipefail

# ──────────────────────────────────────────────────────────────────────────
# Wizard library
# ──────────────────────────────────────────────────────────────────────────

if [[ -t 1 ]] && command -v tput >/dev/null 2>&1 && [[ "$(tput colors 2>/dev/null || echo 0)" -ge 8 ]]; then
  BOLD=$(tput bold); DIM=$(tput dim); RESET=$(tput sgr0)
  BLUE=$(tput setaf 4); GREEN=$(tput setaf 2); YELLOW=$(tput setaf 3); RED=$(tput setaf 1)
else
  BOLD=""; DIM=""; RESET=""; BLUE=""; GREEN=""; YELLOW=""; RED=""
fi

TOTAL_STAGES=0
_STAGE_INDEX=0
ENV_FILE="${ENV_FILE:-deploy/.env}"
WRITTEN_ENV=()
SKIPPED=()

_clear() {
  [[ -t 1 ]] || return 0
  if command -v tput >/dev/null 2>&1; then tput clear; else printf '\033[2J\033[3J\033[H'; fi
}

banner() {
  _clear
  printf '\n%s%s  %s%s\n' "$BOLD" "$BLUE" "$1" "$RESET"
  printf '  %s stages%s\n\n' "$DIM" "$TOTAL_STAGES" "$RESET"
  printf '  Creates an Azure AD app registration for Microsoft Teams/Graph API\n'
  printf '  and configures the MCP server to connect. You need an Azure subscription\n'
  printf '  with permission to create app registrations.%s\n' "$RESET"
  pause "Ready to start?"
}

stage() {
  _clear
  _STAGE_INDEX=$((_STAGE_INDEX + 1))
  printf '\n%s%s▸ Stage %s/%s · %s%s\n' \
    "$BOLD" "$BLUE" "$_STAGE_INDEX" "$TOTAL_STAGES" "$1" "$RESET"
}

say()  { printf '  %s\n' "$1"; }
step() { printf '  %s•%s %s\n' "$BLUE" "$RESET" "$1"; }
note() { printf '  %s%s%s\n' "$DIM" "$1" "$RESET"; }
warn() { printf '  %s⚠ %s%s\n' "$YELLOW" "$1" "$RESET"; }

open_url() {
  local url="$1"
  printf '  %s↗ opening%s %s\n' "$GREEN" "$RESET" "$url"
  { if   command -v wslview     >/dev/null 2>&1; then wslview "$url"
    elif command -v explorer.exe >/dev/null 2>&1; then explorer.exe "$url"
    elif command -v xdg-open    >/dev/null 2>&1; then xdg-open "$url"
    elif command -v open        >/dev/null 2>&1; then open "$url"
    else warn "couldn't open a browser — visit it manually: $url"; fi
  } >/dev/null 2>&1 || warn "couldn't open a browser — visit it manually: $url"
}

pause() {
  printf '  %s%s%s ' "$DIM" "${1:-Press Enter to continue}" "$RESET"
  read -r _ || true
}

confirm() {
  local reply=""
  printf '  %s? %s [y/N] ' "$YELLOW" "$1"
  read -r reply || true
  [[ "$reply" =~ ^[Yy] ]]
}

_existing() {
  [[ -f "$ENV_FILE" ]] || return 1
  local line; line=$(grep -E "^${1}=" "$ENV_FILE" | tail -n1) || return 1
  printf '%s' "${line#*=}"
}

ask() {
  local key="$1" prompt="$2" current input
  current=$(_existing "$key" || true)
  if [[ -n "$current" ]]; then
    printf '  %s%s%s %s[Enter keeps current]%s ' "$BOLD" "$prompt" "$RESET" "$DIM" "$RESET"
  else
    printf '  %s%s%s ' "$BOLD" "$prompt" "$RESET"
  fi
  read -r input || true
  [[ -z "$input" && -n "$current" ]] && input="$current"
  printf -v "$key" '%s' "$input"
}

ask_secret() {
  local key="$1" prompt="$2" current input
  current=$(_existing "$key" || true)
  if [[ -n "$current" ]]; then
    printf '  %s%s%s %s[Enter keeps current]%s ' "$BOLD" "$prompt" "$RESET" "$DIM" "$RESET"
  else
    printf '  %s%s%s ' "$BOLD" "$prompt" "$RESET"
  fi
  read -rs input || true
  printf '\n'
  [[ -z "$input" && -n "$current" ]] && input="$current"
  printf -v "$key" '%s' "$input"
}

write_env() {
  local key="$1" value="$2" tmp
  mkdir -p "$(dirname "$ENV_FILE")"
  touch "$ENV_FILE"
  tmp=$(mktemp)
  grep -vE "^${key}=" "$ENV_FILE" > "$tmp" || true
  printf '%s=%s\n' "$key" "$value" >> "$tmp"
  mv "$tmp" "$ENV_FILE"
  WRITTEN_ENV+=("$key")
  printf '  %s✓ wrote%s %s → %s\n' "$GREEN" "$RESET" "$key" "$ENV_FILE"
}

finish() {
  _clear
  printf '\n%s%s  ✓ Setup complete%s\n' "$BOLD" "$GREEN" "$RESET"
  (( ${#WRITTEN_ENV[@]} )) && note "wrote ${#WRITTEN_ENV[@]} value(s) to $ENV_FILE: ${WRITTEN_ENV[*]}"
  if (( ${#SKIPPED[@]} )); then
    printf '\n'; warn "still to do by hand:"
    for s in "${SKIPPED[@]}"; do note "  - $s"; done
  fi
  printf '\n'
  say "Next steps:"
  say "  1. docker compose -f deploy/docker-compose.yml up -d"
  say "  2. Open http://localhost:3000 for the panel"
  say "  3. Open http://localhost:5100/swagger for the ControlPlane API"
  say "  4. Add a tenant via the panel or CLI:"
  say "     dotnet run --project src/ZeroTeamsMcp.Cli -- tenant-add"
  printf '\n'
}

# ──────────────────────────────────────────────────────────────────────────
# STAGES
# ──────────────────────────────────────────────────────────────────────────

TOTAL_STAGES=5

banner "ZeroTeams MCP Setup"

# ── Stage 1: Azure AD App Registration ───────────────────────────────────
stage "Azure AD — App Registration"
say "We'll register an app in Microsoft Entra ID for Graph API access."
open_url "https://entra.microsoft.com/#view/Microsoft_AAD_RegisteredApps/ApplicationsListBlade"
step "Click '+ New registration' at the top"
step "Name: 'ZeroTeams MCP' (or whatever you like)"
step "Supported account types: 'Accounts in this organizational directory only'"
step "Redirect URI: leave empty (we use client credentials, no browser)"
step "Click 'Register'"
pause "Done? Press Enter when you see the app's overview page."

# ── Stage 2: Copy IDs ───────────────────────────────────────────────────
stage "Azure AD — Copy Tenant ID + Client ID"
say "From the app overview page:"
step "Copy the 'Application (client) ID' — it looks like: xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"
ask CLIENT_ID "Paste the Client ID:"
step "Now get the Tenant ID:"
step "  Go to: https://entra.microsoft.com/#view/Microsoft_AAD_TenantManagement/TenantPropertiesBlade"
step "  Copy the 'Tenant ID'"
ask TENANT_ID "Paste the Tenant ID:"
write_env AZURE_TENANT_ID "$TENANT_ID"
write_env AZURE_CLIENT_ID "$CLIENT_ID"

# ── Stage 3: Client Secret ──────────────────────────────────────────────
stage "Azure AD — Create Client Secret"
say "Back in your app registration:"
step "Click 'Certificates & secrets' in the left menu"
step "Under 'Client secrets', click '+ New client secret'"
step "Description: 'ZeroTeams MCP'"
step "Expiry: choose what you prefer (6 months, 12 months, or never)"
step "Click 'Add'"
step "IMPORTANT: Copy the 'Value' column immediately — it disappears after you leave!"
ask_secret CLIENT_SECRET "Paste the Client Secret:"
write_env AZURE_CLIENT_SECRET "$CLIENT_SECRET"

# ── Stage 4: API Permissions ────────────────────────────────────────────
stage "Azure AD — Graph API Permissions"
say "Now configure what the app can access:"
step "Click 'API permissions' in the left menu"
step "Click '+ Add a permission'"
step "Select 'Microsoft Graph' → 'Application permissions'"
step "Add these permissions (search for each):"
say ""
say "  Team.ReadBasic.All"
say "  Channel.ReadBasic.All"
say "  Chat.ReadWrite.All"
say "  ChatMessage.Send"
say "  Calendars.ReadWrite"
say "  OnlineMeetings.ReadWrite"
say "  OnlineMeetingAttendance.Read"
say "  User.Read.All"
say ""
step "Click 'Add permissions' at the bottom"
step "Click 'Grant admin consent for [your org]' and confirm"
step "All permissions should show green checkmarks"
pause "Done? Press Enter when all permissions show green checkmarks."

# ── Stage 5: Validate ───────────────────────────────────────────────────
stage "Validate — Test Connection"
say "Let's verify the credentials work by testing the Graph API:"
TENANT_ID=$(_existing AZURE_TENANT_ID || true)
CLIENT_ID=$(_existing AZURE_CLIENT_ID || true)
CLIENT_SECRET=$(_existing AZURE_CLIENT_SECRET || true)

if [[ -n "$TENANT_ID" && -n "$CLIENT_ID" && -n "$CLIENT_SECRET" ]]; then
  TOKEN=$(curl -s -X POST "https://login.microsoftonline.com/${TENANT_ID}/oauth2/v2.0/token" \
    -d "client_id=${CLIENT_ID}" \
    -d "client_secret=${CLIENT_SECRET}" \
    -d "scope=https://graph.microsoft.com/.default" \
    -d "grant_type=client_credentials" | python3 -c "import sys,json; print(json.load(sys.stdin).get('access_token','FAIL'))" 2>/dev/null || echo "FAIL")

  if [[ "$TOKEN" != "FAIL" && -n "$TOKEN" ]]; then
    printf '\n  %s✓ Token obtained successfully!%s\n' "$GREEN" "$RESET"
    # Test listing teams
    TEAMS=$(curl -s -H "Authorization: Bearer $TOKEN" "https://graph.microsoft.com/v1.0/me/joinedTeams" 2>/dev/null || echo "{}")
    COUNT=$(echo "$TEAMS" | python3 -c "import sys,json; d=json.load(sys.stdin); print(len(d.get('value',[])))" 2>/dev/null || echo "?")
    say "You have access to $COUNT team(s)."
  else
    warn "Token request failed. Check your credentials and permissions."
    say "  Common issues:"
    say "  - Client secret expired or wrong"
    say "  - Admin consent not granted"
    say "  - Wrong tenant ID"
  fi
else
  warn "Missing credentials — check $ENV_FILE"
fi

finish
