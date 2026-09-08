#!/bin/bash
# Generate a secure ENCRYPTION_KEY for TokenStore
# Usage: ./scripts/generate-encryption-key.sh

set -euo pipefail

# Generate 32 bytes (256 bits) as base64
KEY=$(openssl rand -base64 32)

echo "# Add to your .env or environment:"
echo "ENCRYPTION_KEY=$KEY"
echo ""
echo "# Or export directly:"
echo "export ENCRYPTION_KEY=$KEY"