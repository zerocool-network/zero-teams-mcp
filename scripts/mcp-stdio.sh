#!/bin/bash
# MCP stdio wrapper - runs the ZeroTeams MCP server in stdio mode
# Usage: ./mcp-stdio.sh
# Environment variables can be overridden: AZURE_TENANT_ID, AZURE_CLIENT_ID, AZURE_CLIENT_SECRET, CONTROLPLANE_URL, DATA_DIR, ENCRYPTION_KEY

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
SERVER_PROJECT="$PROJECT_ROOT/src/ZeroTeamsMcp.Server"

# Default to local development values, allow override via environment
export AZURE_TENANT_ID="${AZURE_TENANT_ID:-}"
export AZURE_CLIENT_ID="${AZURE_CLIENT_ID:-}"
export AZURE_CLIENT_SECRET="${AZURE_CLIENT_SECRET:-}"
export CONTROLPLANE_URL="${CONTROLPLANE_URL:-http://localhost:5100}"
export DATA_DIR="${DATA_DIR:-$PROJECT_ROOT/data}"
export ENCRYPTION_KEY="${ENCRYPTION_KEY:-}"
export TRANSPORT=stdio

# Check if required env vars are set
if [[ -z "$AZURE_TENANT_ID" || -z "$AZURE_CLIENT_ID" || -z "$AZURE_CLIENT_SECRET" ]]; then
    echo "ERROR: Missing required environment variables" >&2
    echo "Please set: AZURE_TENANT_ID, AZURE_CLIENT_ID, AZURE_CLIENT_SECRET" >&2
    exit 1
fi

exec dotnet run \
  --project "$SERVER_PROJECT" \
  -c Release \
  --no-build