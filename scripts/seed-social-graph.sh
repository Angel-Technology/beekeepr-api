#!/usr/bin/env bash
# Seed test data for the social-graph screens (friends list, blocked list,
# incoming + outgoing friend requests, search results).
#
# Creates 6 fake users with predictable handles and wires relationships against
# the email you pass in. Idempotent — re-running updates relationships in place
# (uses ON CONFLICT DO UPDATE) so you can tweak this script and re-apply.
#
# Usage:
#   ./scripts/seed-social-graph.sh <your-email>
#
# Example:
#   ./scripts/seed-social-graph.sh you@example.com
#
# Override the postgres container name / db with env vars if needed:
#   POSTGRES_CONTAINER=buzzkeepr-postgres POSTGRES_DB=buzzkeepr_dev \
#     ./scripts/seed-social-graph.sh you@example.com
#
# What gets seeded (relative to YOU, the main user identified by the email):
#
#   Handle            | Relationship                            | Where it shows up
#   ------------------|-----------------------------------------|--------------------------
#   friendalice       | Accepted friendship (you sent)          | friends, search
#   friendbob         | Accepted friendship (you received)      | friends, search
#   pendingoutcarol   | Pending request (you sent)              | outgoingFriendRequests, search
#   pendingindave     | Pending request (you received)          | incomingFriendRequests, search
#   blockederin       | You blocked them                        | blockedUsers (hidden from search)
#   flaggedfaith      | You flagged them (flag implies block)   | blockedUsers (hidden from search)
#
# After running, your frontend should see populated friend / blocked / pending lists
# and the search results should respect block/flag exclusion.

set -euo pipefail

EMAIL="${1:-}"
if [[ -z "$EMAIL" ]]; then
  echo "Usage: $0 <your-email>" >&2
  exit 1
fi

POSTGRES_CONTAINER="${POSTGRES_CONTAINER:-buzzkeepr-postgres}"
POSTGRES_DB="${POSTGRES_DB:-buzzkeepr_dev}"
POSTGRES_USER="${POSTGRES_USER:-postgres}"

# Sanity check: container is up.
if ! docker ps --format '{{.Names}}' | grep -q "^${POSTGRES_CONTAINER}$"; then
  echo "Container '${POSTGRES_CONTAINER}' is not running. Start postgres with 'docker compose up -d'." >&2
  exit 1
fi

# Sanity check: the main user exists. The script needs an actual signed-in user
# so the seeded relationships have a real other end.
MAIN_USER_ID=$(docker exec "$POSTGRES_CONTAINER" psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -tA -c \
  "SELECT \"Id\" FROM \"Users\" WHERE \"Email\" = '${EMAIL}';" | tr -d '[:space:]')

if [[ -z "$MAIN_USER_ID" ]]; then
  echo "No user found with email '${EMAIL}'. Sign in via verifyEmailSignIn first, then re-run." >&2
  exit 1
fi

echo "Seeding social graph for ${EMAIL} (id ${MAIN_USER_ID})..."

docker exec -i "$POSTGRES_CONTAINER" psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -v ON_ERROR_STOP=1 -v MAIN_USER_ID="'${MAIN_USER_ID}'" <<'SQL'
-- 1. Upsert the 6 fake users. Email is the unique identity; on re-run we keep the existing Id.
--    We tag the email with -seed so wiping is easy: DELETE FROM "Users" WHERE "Email" LIKE '%-seed@buzzkeepr.test'.
INSERT INTO "Users" ("Id", "Email", "EmailVerified", "CreatedAtUtc") VALUES
  (gen_random_uuid(), 'friendalice-seed@buzzkeepr.test',     true, now() - interval '30 days'),
  (gen_random_uuid(), 'friendbob-seed@buzzkeepr.test',       true, now() - interval '29 days'),
  (gen_random_uuid(), 'pendingoutcarol-seed@buzzkeepr.test', true, now() - interval '5 days'),
  (gen_random_uuid(), 'pendingindave-seed@buzzkeepr.test',   true, now() - interval '3 days'),
  (gen_random_uuid(), 'blockederin-seed@buzzkeepr.test',     true, now() - interval '14 days'),
  (gen_random_uuid(), 'flaggedfaith-seed@buzzkeepr.test',    true, now() - interval '10 days')
ON CONFLICT ("Email") DO NOTHING;

-- 2. Upsert profiles so the search results render with handles, nicknames, etc.
--    The display layer needs at least a handle + nickname to be useful.
INSERT INTO "UserProfiles" ("UserId", "DisplayName", "Nickname", "Handle", "CreatedAtUtc")
SELECT u."Id", v.display_name, v.nickname, v.handle, now()
FROM "Users" u
JOIN (VALUES
  ('friendalice-seed@buzzkeepr.test',     'Alice Friend',     'Alice',          'friendalice'),
  ('friendbob-seed@buzzkeepr.test',       'Bob Friend',       'Bob',            'friendbob'),
  ('pendingoutcarol-seed@buzzkeepr.test', 'Carol Pending',    'Carol',          'pendingoutcarol'),
  ('pendingindave-seed@buzzkeepr.test',   'Dave Pending',     'Dave',           'pendingindave'),
  ('blockederin-seed@buzzkeepr.test',     'Erin Blocked',     'Erin',           'blockederin'),
  ('flaggedfaith-seed@buzzkeepr.test',    'Faith Flagged',    'Faith',          'flaggedfaith')
) AS v(email, display_name, nickname, handle) ON v.email = u."Email"
ON CONFLICT ("UserId") DO UPDATE SET
  "DisplayName" = EXCLUDED."DisplayName",
  "Nickname"    = EXCLUDED."Nickname",
  "Handle"      = EXCLUDED."Handle",
  "UpdatedAtUtc" = now();

-- 3. Accepted friendships (Alice + Bob).
--    Alice: main user is Requester. Bob: main user is Addressee.
INSERT INTO "Friendships" ("Id", "RequesterId", "AddresseeId", "Status", "CreatedAtUtc", "RespondedAtUtc")
SELECT gen_random_uuid(),
       (CASE WHEN u."Email" = 'friendalice-seed@buzzkeepr.test' THEN :MAIN_USER_ID::uuid ELSE u."Id" END),
       (CASE WHEN u."Email" = 'friendalice-seed@buzzkeepr.test' THEN u."Id" ELSE :MAIN_USER_ID::uuid END),
       'Accepted',
       now() - interval '20 days',
       now() - interval '19 days'
FROM "Users" u
WHERE u."Email" IN ('friendalice-seed@buzzkeepr.test', 'friendbob-seed@buzzkeepr.test')
ON CONFLICT ("RequesterId", "AddresseeId") DO UPDATE SET
  "Status"         = 'Accepted',
  "RespondedAtUtc" = EXCLUDED."RespondedAtUtc";

-- 4. Pending requests.
--    Carol: main user sent the request (outgoing).
--    Dave: main user received the request (incoming).
INSERT INTO "Friendships" ("Id", "RequesterId", "AddresseeId", "Status", "CreatedAtUtc")
SELECT gen_random_uuid(),
       (CASE WHEN u."Email" = 'pendingoutcarol-seed@buzzkeepr.test' THEN :MAIN_USER_ID::uuid ELSE u."Id" END),
       (CASE WHEN u."Email" = 'pendingoutcarol-seed@buzzkeepr.test' THEN u."Id" ELSE :MAIN_USER_ID::uuid END),
       'Pending',
       now() - interval '1 day'
FROM "Users" u
WHERE u."Email" IN ('pendingoutcarol-seed@buzzkeepr.test', 'pendingindave-seed@buzzkeepr.test')
ON CONFLICT ("RequesterId", "AddresseeId") DO UPDATE SET
  "Status" = 'Pending';

-- 5. Block (Erin). Main user blocks Erin → Erin disappears from search both ways.
INSERT INTO "UserBlocks" ("Id", "BlockerId", "BlockedId", "CreatedAtUtc")
SELECT gen_random_uuid(), :MAIN_USER_ID::uuid, u."Id", now() - interval '2 days'
FROM "Users" u
WHERE u."Email" = 'blockederin-seed@buzzkeepr.test'
ON CONFLICT ("BlockerId", "BlockedId") DO NOTHING;

-- 6. Flag (Faith). Flag implies block — we insert both so the end state matches what
--    ConnectionsService.FlagUserAsync would produce.
INSERT INTO "UserFlags" ("Id", "FlaggerId", "FlaggedUserId", "CreatedAtUtc")
SELECT gen_random_uuid(), :MAIN_USER_ID::uuid, u."Id", now() - interval '6 hours'
FROM "Users" u
WHERE u."Email" = 'flaggedfaith-seed@buzzkeepr.test'
ON CONFLICT ("FlaggerId", "FlaggedUserId") DO NOTHING;

INSERT INTO "UserBlocks" ("Id", "BlockerId", "BlockedId", "CreatedAtUtc")
SELECT gen_random_uuid(), :MAIN_USER_ID::uuid, u."Id", now() - interval '6 hours'
FROM "Users" u
WHERE u."Email" = 'flaggedfaith-seed@buzzkeepr.test'
ON CONFLICT ("BlockerId", "BlockedId") DO NOTHING;
SQL

echo ""
echo "Seeded. Verify with these queries:"
echo ""
echo "  query { friends(first: 20)                  { edges { node { handle nickname } } } }"
echo "  query { incomingFriendRequests(first: 20)   { edges { node { handle nickname } } } }"
echo "  query { outgoingFriendRequests(first: 20)   { edges { node { handle nickname } } } }"
echo "  query { blockedUsers(first: 20)             { edges { node { handle nickname } } } }"
echo "  query { searchUsers(query: \"friend\", first: 10)  { edges { node { handle viewerFriendshipState } } } }"
echo "  query { searchUsers(query: \"blocked\", first: 10) { edges { node { handle } } } }   # → empty"
echo ""
echo "To wipe the seeded users:"
echo "  docker exec ${POSTGRES_CONTAINER} psql -U ${POSTGRES_USER} -d ${POSTGRES_DB} -c \\"
echo "    \"DELETE FROM \\\"Users\\\" WHERE \\\"Email\\\" LIKE '%-seed@buzzkeepr.test';\""
