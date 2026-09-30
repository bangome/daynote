-- Two paid tiers: Pro and Premium (docs/CLOUD_SYNC.md §14).
--
-- Both sync images and files. They differ only in storage: Pro includes 2 GB, Premium is sold as
-- unlimited and held to a fair-use ceiling (entitlement.ts, FAIR_USE_BYTES). The tier is decided by
-- the Paddle price the subscription is on, and it arrives on the webhook.

-- The tier, recorded from the webhook. Every subscription that exists before this migration was
-- sold as the only paid plan there was, which is Pro, and the default says exactly that.
ALTER TABLE subscriptions ADD COLUMN tier TEXT NOT NULL DEFAULT 'pro';

-- The billing interval ('monthly' | 'annual'), when the price is one this deployment knows. NULL
-- for the rows above: nothing recorded which price they were on. The app then just says "Pro".
ALTER TABLE subscriptions ADD COLUMN plan TEXT;

-- The Paddle price (`pri_...`) the tier was read from, kept verbatim so a price that was not
-- configured when its event arrived can still be reconciled by hand.
ALTER TABLE subscriptions ADD COLUMN price_id TEXT;

-- When the event that set the three columns above occurred, by Paddle's clock. An upgrade and a
-- downgrade can be delivered out of order; the tier follows whichever happened last, not whichever
-- arrived last. (The period end has its own rule: it never moves backwards.)
ALTER TABLE subscriptions ADD COLUMN price_occurred_utc TEXT;

-- A second Paddle subscription seen for an account that already has a live one: two checkouts paid
-- close together. The live one stays on the row; this one is kept here, and logged, for the operator
-- to refund. NULL for every account where that never happened.
ALTER TABLE subscriptions ADD COLUMN duplicate_subscription_id TEXT;

-- The quota used to be `users.quota_bytes`, NOT NULL with a 2 GiB default on every account. With
-- tiers, the quota comes from the tier, and a column that every row fills with the same default
-- cannot also say "an operator set this on purpose". So the override is its own, nullable column:
-- NULL follows the tier, a number is a grant that raises the quota above the tier's (it never lowers
-- what a paid tier includes; see entitlement.ts `storage`).
--
-- `quota_bytes` is left in place and no longer read (dropping a column from `users` means a table
-- rebuild under ON DELETE CASCADE, as 0009 showed). Any value an operator had changed by hand is
-- carried over, so no account loses a grant.
ALTER TABLE users ADD COLUMN quota_override_bytes INTEGER;

UPDATE users SET quota_override_bytes = quota_bytes WHERE quota_bytes <> 2147483648;
