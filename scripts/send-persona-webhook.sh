#!/usr/bin/env bash
# Send a synthetic Persona webhook to the local API for manual testing.
#
# Usage:
#   ./scripts/send-persona-webhook.sh <inquiryId> <status> [endpoint] [webhookSecret]
#
# Examples:
#   # Approve an inquiry on localhost (auto-reads the secret from user-secrets)
#   ./scripts/send-persona-webhook.sh inq_abc123 approved
#
#   # Walk through a full lifecycle:
#   ./scripts/send-persona-webhook.sh inq_abc123 started
#   ./scripts/send-persona-webhook.sh inq_abc123 completed
#   ./scripts/send-persona-webhook.sh inq_abc123 approved
#
#   # Simulate a stale event (older `updated-at`) — handler should skip it
#   STALE_AT=$(date -u -v-1H +"%Y-%m-%dT%H:%M:%S.000Z") \
#     ./scripts/send-persona-webhook.sh inq_abc123 completed
#
#   # Override endpoint (e.g., ngrok URL) and secret explicitly
#   ./scripts/send-persona-webhook.sh inq_abc123 approved \
#     https://yourtunnel.ngrok.app/webhooks/persona \
#     wbhsec_xxx
#
# Notes:
#   - You don't need ngrok for this. The script POSTs directly at your local API,
#     skipping Persona's infrastructure entirely. ngrok is only needed when Persona
#     itself is sending the webhook from its servers.
#   - Valid status values: created | started | pending | completed | needs-review |
#     marked-for-review | approved | declined | failed | expired
#   - The handler matches the user by `inquiryId`, so make sure that id exists in
#     `users.persona_inquiry_id` (run `startPersonaInquiry` first to populate it,
#     or UPDATE the row directly in psql).
#   - `verified_*` columns only populate on `approved`/`completed` AND only if the
#     real Persona API has government-ID data for that inquiry. Synthetic ids
#     won't have any, so those fields stay null. Status fields update normally.

set -euo pipefail

INQUIRY_ID="${1:?usage: $0 <inquiryId> <status> [endpoint] [webhookSecret]}"
STATUS="${2:?usage: $0 <inquiryId> <status> [endpoint] [webhookSecret]}"
ENDPOINT="${3:-http://localhost:5158/webhooks/persona}"
SECRET="${4:-}"

# Auto-resolve the secret from user-secrets if not provided. Stays in the local
# shell — never logged anywhere outside this terminal.
if [ -z "$SECRET" ]; then
  SECRET=$(dotnet user-secrets list --project "$(dirname "$0")/../BuzzKeepr.Presentation" 2>/dev/null \
    | awk -F' = ' '/^Persona:WebhookSecrets:0/ {print $2}')
  if [ -z "$SECRET" ]; then
    echo "ERROR: no webhook secret configured." >&2
    echo "  Pass it as the 4th arg, or set it via:" >&2
    echo "  dotnet user-secrets set 'Persona:WebhookSecrets:0' wbhsec_xxx" >&2
    exit 1
  fi
fi

UPDATED_AT="${STALE_AT:-$(date -u +"%Y-%m-%dT%H:%M:%S.000Z")}"
TIMESTAMP=$(date +%s)

BODY=$(cat <<EOF
{"data":{"attributes":{"name":"inquiry.${STATUS}","payload":{"data":{"type":"inquiry","id":"${INQUIRY_ID}","attributes":{"status":"${STATUS}","updated-at":"${UPDATED_AT}"}}}}}}
EOF
)

# HMAC-SHA256 of "<body>.<timestamp>" using the webhook secret, lowercase hex.
SIGNATURE=$(printf '%s.%s' "$BODY" "$TIMESTAMP" | openssl dgst -sha256 -hmac "$SECRET" | awk '{print $NF}')

echo "→ ${STATUS} for ${INQUIRY_ID}"
echo "→ POST ${ENDPOINT}"
echo

curl -i -X POST "$ENDPOINT" \
  -H 'Content-Type: application/json' \
  -H "Persona-Signature: t=${TIMESTAMP},v1=${SIGNATURE}" \
  -d "$BODY"
