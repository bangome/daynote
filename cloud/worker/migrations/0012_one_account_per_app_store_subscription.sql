-- One App Store subscription entitles one account (docs/CLOUD_SYNC.md §14.8).
--
-- An App Store subscription is named by its originalTransactionId for its whole life, across
-- renewals, upgrades, downgrades and a resubscription in the same group. appleBilling.ts refuses a
-- second account that tries to hold one (requireOwnership, 403); this index is the rule itself, so
-- a race between two requests, or a bug, cannot leave two accounts entitled by one payment.
--
-- Scoped to provider 'apple'. Every row that can match was written by the Worker that 0011 shipped
-- with, which no deployment ran before this migration, so no existing row can violate it — and the
-- Paddle rows, which have their own one-subscription rule (billing.ts, ownership), are untouched.
CREATE UNIQUE INDEX subscriptions_apple_subscription
    ON subscriptions(provider, subscription_id)
    WHERE provider = 'apple' AND subscription_id IS NOT NULL;
