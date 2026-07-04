#!/usr/bin/env bash
# Seed test data for the social-graph + visibility screens (friends list, blocked
# list, incoming + outgoing friend requests, search results, profile contact-info
# gating).
#
# Creates 13 fake users with predictable handles + a richly-populated mix of
# friendship/block/visibility states so the frontend can hit every UI path
# without manually walking through GraphQL.
#
# Idempotent — re-running updates relationships in place (uses ON CONFLICT DO
# UPDATE) so you can tweak this script and re-apply.
#
# Usage:
#   ./scripts/seed-social-graph.sh <your-email>
#
# Override the postgres container name / db with env vars if needed:
#   POSTGRES_CONTAINER=buzzkeepr-postgres POSTGRES_DB=buzzkeepr_dev \
#     ./scripts/seed-social-graph.sh you@example.com
#
# Seeded users (all emails end in -seed@buzzkeepr.test):
#
#   ── Social-graph relationship coverage ──
#   Handle            | Relationship to you                     | Where it shows up
#   ------------------|-----------------------------------------|--------------------------
#   friendalice       | Accepted friendship (you sent)          | friends, search
#   friendbob         | Accepted friendship (you received)      | friends, search
#   pendingoutcarol   | Pending request (you sent)              | outgoingFriendRequests, search
#   pendingindave     | Pending request (you received)          | incomingFriendRequests, search
#   blockederin       | You blocked them                        | blockedUsers (hidden from search)
#   flaggedfaith      | You flagged them (flag implies block)   | blockedUsers (hidden from search)
#
#   ── Contact-visibility coverage (all profile fields populated) ──
#   Handle            | ContactVisibility | Friend with you? | Expected contact in search
#   ------------------|-------------------|------------------|--------------------------
#   publicpaul        | CONNECTIONS_ONLY  | yes (friend)     | visible
#   publicpat         | CONNECTIONS_ONLY  | no (stranger)    | null
#   openomar          | CONNECTIONS_ONLY  | no (stranger)    | null
#   friendconnie      | CONNECTIONS_ONLY  | yes (friend)     | visible
#   strangermark      | CONNECTIONS_ONLY  | no (stranger)    | null
#   privatepriya      | PRIVATE           | yes (friend)     | null (strict gating)
#   hiddenharry       | PRIVATE + ProfileVisibility PRIVATE | no | hidden from search entirely
#
#   The handles publicpaul/publicpat/openomar are historical — they originally tested
#   ContactVisibility=Public, which was removed in PR 3. They now all set ConnectionsOnly
#   like everyone else. We left the names for backward stability of seed data.
#
#   ── DiceBear avatars (8 of 13 have ImageUrl set, 5 stay NULL for empty-state) ──
#   Has avatar : friendalice, friendbob, pendingindave, blockederin,
#                publicpaul, publicpat, friendconnie, openomar
#   No avatar  : pendingoutcarol, flaggedfaith, strangermark, privatepriya, hiddenharry
#
#   ── Background-check coverage (8 of 13 have UserBackgroundChecks rows) ──
#   Handle            | Badge    | Last check (last screen) | Expires (next screen)
#   ------------------|----------|--------------------------|----------------------
#   friendalice       | Approved | 14 days ago              | ~2.5 months out
#   friendbob         | Approved | 30 days ago              | ~2 months out
#   publicpaul        | Approved | 85 days ago              | 5 days out  (RENEW SOON UX)
#   publicpat         | Approved | 1 day ago                | ~3 months out (freshest)
#   friendconnie      | Approved | 45 days ago              | ~1.5 months out
#   pendingindave     | Approved | 60 days ago              | ~1 month out
#   strangermark      | Denied   | 7 days ago               | ~3 months out (failed check)
#   openomar          | Approved | 20 days ago              | ~2 months out
#   pendingoutcarol, blockederin, flaggedfaith, privatepriya, hiddenharry → no row
#       → Badge:None, CheckrLastCheckAtUtc:null, BadgeExpiresAtUtc:null (empty state)
#
# Cross-fake blocks (so total block count > main user's blocked list — proves
# blockedUsers scoping is correct):
#   friendalice  → blocks pendingoutcarol
#   friendbob    → blocks blockederin
#   pendingindave→ blocks flaggedfaith

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

echo "Seeding social graph + visibility coverage for ${EMAIL} (id ${MAIN_USER_ID})..."

docker exec -i "$POSTGRES_CONTAINER" psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -v ON_ERROR_STOP=1 -v MAIN_USER_ID="'${MAIN_USER_ID}'" <<'SQL'
-- ============================================================================
-- 1. Upsert all 12 fake users. Email is the unique identity; on re-run we keep
--    the existing Id. We tag the email with -seed so wiping is one-shot:
--      DELETE FROM "Users" WHERE "Email" LIKE '%-seed@buzzkeepr.test';
-- ============================================================================
INSERT INTO "Users" ("Id", "Email", "EmailVerified", "CreatedAtUtc") VALUES
  -- Social-graph coverage (relationship variety, minimal profile detail)
  (gen_random_uuid(), 'friendalice-seed@buzzkeepr.test',     true, now() - interval '30 days'),
  (gen_random_uuid(), 'friendbob-seed@buzzkeepr.test',       true, now() - interval '29 days'),
  (gen_random_uuid(), 'pendingoutcarol-seed@buzzkeepr.test', true, now() - interval '5 days'),
  (gen_random_uuid(), 'pendingindave-seed@buzzkeepr.test',   true, now() - interval '3 days'),
  (gen_random_uuid(), 'blockederin-seed@buzzkeepr.test',     true, now() - interval '14 days'),
  (gen_random_uuid(), 'flaggedfaith-seed@buzzkeepr.test',    true, now() - interval '10 days'),
  -- Visibility coverage (rich profile detail, varied ContactVisibility)
  (gen_random_uuid(), 'publicpaul-seed@buzzkeepr.test',      true, now() - interval '21 days'),
  (gen_random_uuid(), 'publicpat-seed@buzzkeepr.test',       true, now() - interval '18 days'),
  (gen_random_uuid(), 'friendconnie-seed@buzzkeepr.test',    true, now() - interval '15 days'),
  (gen_random_uuid(), 'strangermark-seed@buzzkeepr.test',    true, now() - interval '12 days'),
  (gen_random_uuid(), 'privatepriya-seed@buzzkeepr.test',    true, now() - interval '9 days'),
  (gen_random_uuid(), 'hiddenharry-seed@buzzkeepr.test',     true, now() - interval '7 days'),
  (gen_random_uuid(), 'openomar-seed@buzzkeepr.test',        true, now() - interval '4 days')
ON CONFLICT ("Email") DO NOTHING;

-- ============================================================================
-- 2. Upsert profiles. Minimal-detail profiles for the social-graph users (just
--    DisplayName + Nickname + Handle so the cards render with text). Full-detail
--    profiles for the visibility-coverage users (every contact field populated)
--    so the frontend can verify each gated-field's value.
-- ============================================================================
-- ImageUrl deliberately mixed: 7 of 12 users get a DiceBear avatar (stable per handle
-- since the seed param is the handle string), 5 stay NULL so the frontend can render
-- the empty-state avatar tile. DiceBear "adventurer" style was the one the frontend
-- chose; URLs return SVG so they work in any <img> tag.
INSERT INTO "UserProfiles" (
    "UserId", "DisplayName", "Nickname", "Handle", "ImageUrl",
    "GoogleVoicePhone", "WhatsAppPhone",
    "InstagramHandle", "TelegramHandle", "SnapchatHandle", "SignalPhone",
    "ProfileVisibility", "ContactVisibility",
    "CreatedAtUtc"
)
SELECT u."Id",
       v.display_name, v.nickname, v.handle, v.image_url,
       v.google_voice, v.whatsapp,
       v.instagram, v.telegram, v.snapchat, v.signal,
       v.profile_vis, v.contact_vis,
       now()
FROM "Users" u
JOIN (VALUES
  --                              email                              display_name        nickname  handle             image_url                                                                          google_voice       whatsapp           instagram     telegram           snapchat          signal             profile_vis  contact_vis
  ('friendalice-seed@buzzkeepr.test',     'Alice Friend',     'Alice',          'friendalice',     'https://api.dicebear.com/9.x/adventurer/svg?seed=friendalice',     NULL,              NULL,              NULL,         NULL,              NULL,             NULL,              'Public',    'Private'),
  ('friendbob-seed@buzzkeepr.test',       'Bob Friend',       'Bob',            'friendbob',       'https://api.dicebear.com/9.x/adventurer/svg?seed=friendbob',       NULL,              NULL,              NULL,         NULL,              NULL,             NULL,              'Public',    'Private'),
  ('pendingoutcarol-seed@buzzkeepr.test', 'Carol Pending',    'Carol',          'pendingoutcarol', NULL,                                                              NULL,              NULL,              NULL,         NULL,              NULL,             NULL,              'Public',    'Private'),
  ('pendingindave-seed@buzzkeepr.test',   'Dave Pending',     'Dave',           'pendingindave',   'https://api.dicebear.com/9.x/adventurer/svg?seed=pendingindave',   NULL,              NULL,              NULL,         NULL,              NULL,             NULL,              'Public',    'Private'),
  ('blockederin-seed@buzzkeepr.test',     'Erin Blocked',     'Erin',           'blockederin',     'https://api.dicebear.com/9.x/adventurer/svg?seed=blockederin',     NULL,              NULL,              NULL,         NULL,              NULL,             NULL,              'Public',    'Private'),
  ('flaggedfaith-seed@buzzkeepr.test',    'Faith Flagged',    'Faith',          'flaggedfaith',    NULL,                                                              NULL,              NULL,              NULL,         NULL,              NULL,             NULL,              'Public',    'Private'),
  -- ConnectionsOnly contact → fields visible only to accepted friends
  ('publicpaul-seed@buzzkeepr.test',      'Paul Public',      'Paul',           'publicpaul',      'https://api.dicebear.com/9.x/adventurer/svg?seed=publicpaul',      '(415) 555-2001',  '(415) 555-3001',  'paulinsta',  '(415) 555-4001',  'paulsnap',       '(415) 555-5001',  'Public',    'ConnectionsOnly'),
  ('publicpat-seed@buzzkeepr.test',       'Pat Public',       'Pat',            'publicpat',       'https://api.dicebear.com/9.x/adventurer/svg?seed=publicpat',       '(415) 555-2002',  '(415) 555-3002',  'patinsta',   '(415) 555-4002',  'patsnap',        '(415) 555-5002',  'Public',    'ConnectionsOnly'),
  ('friendconnie-seed@buzzkeepr.test',    'Connie Connect',   'Connie',         'friendconnie',    'https://api.dicebear.com/9.x/adventurer/svg?seed=friendconnie',    '(415) 555-2003',  '(415) 555-3003',  'connieinsta','(415) 555-4003',  'conniesnap',     '(415) 555-5003',  'Public',    'ConnectionsOnly'),
  ('strangermark-seed@buzzkeepr.test',    'Mark Stranger',    'Mark',           'strangermark',    NULL,                                                              '(415) 555-2004',  '(415) 555-3004',  'markinsta',  '(415) 555-4004',  'marksnap',       '(415) 555-5004',  'Public',    'ConnectionsOnly'),
  -- Private contact → fields hidden from everyone, even friends (strict gating)
  ('privatepriya-seed@buzzkeepr.test',    'Priya Private',    'Priya',          'privatepriya',    NULL,                                                              '(415) 555-2005',  '(415) 555-3005',  'priyainsta', '(415) 555-4005',  'priyasnap',      '(415) 555-5005',  'Public',    'Private'),
  -- Private profile → entire row excluded from search results
  ('hiddenharry-seed@buzzkeepr.test',     'Harry Hidden',     'Harry',          'hiddenharry',     NULL,                                                              '(415) 555-2006',  '(415) 555-3006',  'harryinsta', '(415) 555-4006',  'harrysnap',      '(415) 555-5006',  'Private',   'Private'),
  -- Second stranger with ConnectionsOnly contact so the stranger case has two test rows
  ('openomar-seed@buzzkeepr.test',        'Omar Open',        'Omar',           'openomar',        'https://api.dicebear.com/9.x/adventurer/svg?seed=openomar',        '(415) 555-2007',  '(415) 555-3007',  'omarinsta',  '(415) 555-4007',  'omarsnap',       '(415) 555-5007',  'Public',    'ConnectionsOnly')
) AS v(email, display_name, nickname, handle, image_url, google_voice, whatsapp, instagram, telegram, snapchat, signal, profile_vis, contact_vis) ON v.email = u."Email"
ON CONFLICT ("UserId") DO UPDATE SET
  "DisplayName"      = EXCLUDED."DisplayName",
  "Nickname"         = EXCLUDED."Nickname",
  "Handle"           = EXCLUDED."Handle",
  "ImageUrl"         = EXCLUDED."ImageUrl",
  "GoogleVoicePhone" = EXCLUDED."GoogleVoicePhone",
  "WhatsAppPhone"    = EXCLUDED."WhatsAppPhone",
  "InstagramHandle"  = EXCLUDED."InstagramHandle",
  "TelegramHandle"   = EXCLUDED."TelegramHandle",
  "SnapchatHandle"   = EXCLUDED."SnapchatHandle",
  "SignalPhone"      = EXCLUDED."SignalPhone",
  "ProfileVisibility" = EXCLUDED."ProfileVisibility",
  "ContactVisibility" = EXCLUDED."ContactVisibility",
  "UpdatedAtUtc"     = now();

-- ============================================================================
-- 2b. UserBackgroundChecks for users who have "completed" a Checkr inquiry.
--     Mix of Approved badges at varying expiry distances (most fresh, one
--     close to renewal so the frontend can exercise the "renew soon" UX) plus
--     one Denied row to show that visual state. Users not listed here have no
--     UserBackgroundChecks row at all — Badge defaults to None and the two
--     timestamp fields (CheckrLastCheckAtUtc + BadgeExpiresAtUtc) come back
--     null, exercising the "not yet verified" empty state on the frontend.
--
--     CheckrProfileId / CheckrLastCheckId use a 'prf_seed_*' / 'chk_seed_*'
--     prefix so the BackgroundCheckRenewalBackgroundService is easy to spot
--     fake-id failures from in logs if it ever sweeps these rows.
-- ============================================================================
INSERT INTO "UserBackgroundChecks" (
    "UserId", "CheckrProfileId", "CheckrLastCheckId",
    "CheckrLastCheckAtUtc", "CheckrLastCheckHasPossibleMatches",
    "Badge", "BadgeExpiresAtUtc"
)
SELECT u."Id",
       'prf_seed_' || split_part(u."Email", '-', 1),
       'chk_seed_' || split_part(u."Email", '-', 1),
       v.last_check_at,
       v.has_matches,
       v.badge,
       v.expires_at
FROM "Users" u
JOIN (VALUES
  --                              email                              badge        last check (last screen)      expires (next screen)               has_matches
  ('friendalice-seed@buzzkeepr.test',  'Approved',  now() - interval '14 days',  now() + interval '2 months 16 days', false),
  ('friendbob-seed@buzzkeepr.test',    'Approved',  now() - interval '30 days',  now() + interval '2 months',         false),
  ('publicpaul-seed@buzzkeepr.test',   'Approved',  now() - interval '85 days',  now() + interval '5 days',           false),  -- "renew soon" UX
  ('publicpat-seed@buzzkeepr.test',    'Approved',  now() - interval '1 day',    now() + interval '3 months',         false),  -- "freshly verified"
  ('friendconnie-seed@buzzkeepr.test', 'Approved',  now() - interval '45 days',  now() + interval '1 month 15 days',  false),
  ('pendingindave-seed@buzzkeepr.test','Approved',  now() - interval '60 days',  now() + interval '1 month',          false),
  ('strangermark-seed@buzzkeepr.test', 'Denied',    now() - interval '7 days',   now() + interval '2 months 23 days', true),   -- failed check
  ('openomar-seed@buzzkeepr.test',     'Approved',  now() - interval '20 days',  now() + interval '2 months 10 days', false)
) AS v(email, badge, last_check_at, expires_at, has_matches) ON v.email = u."Email"
ON CONFLICT ("UserId") DO UPDATE SET
  "CheckrLastCheckAtUtc"              = EXCLUDED."CheckrLastCheckAtUtc",
  "CheckrLastCheckHasPossibleMatches" = EXCLUDED."CheckrLastCheckHasPossibleMatches",
  "Badge"                             = EXCLUDED."Badge",
  "BadgeExpiresAtUtc"                 = EXCLUDED."BadgeExpiresAtUtc";

-- ============================================================================
-- 3. Accepted friendships with the main user.
--    Alice + Paul + Connie + Priya → main is Requester.
--    Bob              → main is Addressee.
-- ============================================================================
INSERT INTO "Friendships" ("Id", "RequesterId", "AddresseeId", "Status", "CreatedAtUtc", "RespondedAtUtc")
SELECT gen_random_uuid(),
       (CASE WHEN u."Email" = 'friendbob-seed@buzzkeepr.test' THEN u."Id" ELSE :MAIN_USER_ID::uuid END),
       (CASE WHEN u."Email" = 'friendbob-seed@buzzkeepr.test' THEN :MAIN_USER_ID::uuid ELSE u."Id" END),
       'Accepted',
       now() - interval '20 days',
       now() - interval '19 days'
FROM "Users" u
WHERE u."Email" IN (
  'friendalice-seed@buzzkeepr.test',
  'friendbob-seed@buzzkeepr.test',
  'publicpaul-seed@buzzkeepr.test',
  'friendconnie-seed@buzzkeepr.test',
  'privatepriya-seed@buzzkeepr.test'
)
ON CONFLICT ("RequesterId", "AddresseeId") DO UPDATE SET
  "Status"         = 'Accepted',
  "RespondedAtUtc" = EXCLUDED."RespondedAtUtc";

-- ============================================================================
-- 4. Pending requests.
--    Carol: main user sent (outgoing).
--    Dave: main user received (incoming).
-- ============================================================================
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

-- ============================================================================
-- 5. Block + flag relationships with the main user.
-- ============================================================================
INSERT INTO "UserBlocks" ("Id", "BlockerId", "BlockedId", "CreatedAtUtc")
SELECT gen_random_uuid(), :MAIN_USER_ID::uuid, u."Id", now() - interval '2 days'
FROM "Users" u
WHERE u."Email" = 'blockederin-seed@buzzkeepr.test'
ON CONFLICT ("BlockerId", "BlockedId") DO NOTHING;

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

-- ============================================================================
-- 6. Cross-fake blocks (relationships between fakes, NOT involving the main
--    user). These verify that the blockedUsers query is correctly scoped to the
--    caller — they bring the total UserBlocks count above the main user's
--    personal blocked count.
-- ============================================================================
INSERT INTO "UserBlocks" ("Id", "BlockerId", "BlockedId", "CreatedAtUtc")
SELECT gen_random_uuid(),
       (SELECT "Id" FROM "Users" WHERE "Email" = blocker_email),
       (SELECT "Id" FROM "Users" WHERE "Email" = blocked_email),
       now() - interval '1 day'
FROM (VALUES
  ('friendalice-seed@buzzkeepr.test',     'pendingoutcarol-seed@buzzkeepr.test'),
  ('friendbob-seed@buzzkeepr.test',       'blockederin-seed@buzzkeepr.test'),
  ('pendingindave-seed@buzzkeepr.test',   'flaggedfaith-seed@buzzkeepr.test')
) AS pairs(blocker_email, blocked_email)
ON CONFLICT ("BlockerId", "BlockedId") DO NOTHING;
SQL

echo ""
echo "Seeded. Verify with these queries (as ${EMAIL}):"
echo ""
echo "  # Friends list — should show 5 rows (alice, bob, paul, connie, priya)."
echo "  # Backgrounds: alice/bob/paul/connie Approved, priya None (no row → empty state)."
echo "  query { friends(first: 20) {"
echo "    edges { node {"
echo "      handle nickname contactVisibility snapchatHandle"
echo "      backgroundCheckBadge checkrLastCheckAtUtc backgroundCheckBadgeExpiresAtUtc"
echo "    } }"
echo "  } }"
echo ""
echo "  # Blocked list — should show 2 rows (erin, faith)"
echo "  query { blockedUsers(first: 20) { edges { node { handle nickname } } } }"
echo ""
echo "  # Pending — 1 outgoing (carol), 1 incoming (dave)"
echo "  query { incomingFriendRequests(first: 20) { edges { node { handle } } } }"
echo "  query { outgoingFriendRequests(first: 20) { edges { node { handle } } } }"
echo ""
echo "  # Search 'public' — both publicpaul + publicpat appear, contact fields populated"
echo "  query { searchUsers(query: \"public\", first: 10) {"
echo "    edges { node { handle contactVisibility snapchatHandle instagramHandle viewerFriendshipState } }"
echo "  } }"
echo ""
echo "  # Search 'connect' — friendconnie shows with contact (friend), strangermark shows with NULL contact (not friend)"
echo "  query { searchUsers(query: \"connect\", first: 10) {"
echo "    edges { node { handle contactVisibility snapchatHandle viewerFriendshipState } }"
echo "  } }"
echo ""
echo "  # Search 'priya' — appears but contact is NULL (Private always hides, even on friend list)"
echo "  query { searchUsers(query: \"priya\", first: 10) {"
echo "    edges { node { handle contactVisibility snapchatHandle } }"
echo "  } }"
echo ""
echo "  # Search 'harry' — empty (ProfileVisibility=Private excludes from search entirely)"
echo "  query { searchUsers(query: \"harry\", first: 10) { edges { node { handle } } } }"
echo ""
echo "To wipe the seeded users:"
echo "  docker exec ${POSTGRES_CONTAINER} psql -U ${POSTGRES_USER} -d ${POSTGRES_DB} -c \\"
echo "    \"DELETE FROM \\\"Users\\\" WHERE \\\"Email\\\" LIKE '%-seed@buzzkeepr.test';\""
