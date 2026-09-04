-- Subscriptions (docs/CLOUD_SYNC.md §14).
--
-- Cloud sync is a paid feature; the app itself is not. Two things follow from that and are visible
-- in the shapes below.
--
-- First, entitlement is deliberately NOT a column on `users`. A webhook can arrive before the
-- account exists (a checkout completed in the browser while the app was closed), arrive twice, or
-- arrive out of order, so the billing state is its own row keyed by the provider's ids and every
-- delivery is recorded for idempotency.
--
-- Second, nothing here reads or touches note content. Billing knows a user id, a status, and a
-- date. It never needs the notes, which is why turning the opt-in lock on (§4.1b) has no effect on
-- any of this.

-- Trial: granted once, at account creation, and never re-granted. Held on `users` because it is a
-- property of the account rather than of a subscription that may not exist.
ALTER TABLE users ADD COLUMN trial_ends_utc TEXT;

CREATE TABLE subscriptions (
    user_id            TEXT PRIMARY KEY REFERENCES users(id) ON DELETE CASCADE,
    provider           TEXT NOT NULL DEFAULT 'paddle',
    -- The provider's own identifiers, kept so a webhook can be matched to an account even when it
    -- carries no custom data, and so the app can be sent to the right management portal.
    customer_id        TEXT,
    subscription_id    TEXT,
    -- Mirrors the provider's vocabulary rather than inventing our own: a status we do not recognise
    -- is stored verbatim and treated as unentitled, which fails closed without losing the record.
    status             TEXT NOT NULL,
    -- Access runs to here. A cancelled subscription keeps working until this instant, which policy
    -- 10.8.6 requires ("if you discontinue an active subscription, you must continue to provide
    -- purchased digital goods or services until the subscription expires").
    current_period_end_utc TEXT,
    -- Set while the provider is retrying a failed payment, so a card problem does not read as a
    -- cancellation on the day it happens.
    grace_ends_utc     TEXT,
    updated_utc        TEXT NOT NULL
);

CREATE INDEX subscriptions_subscription ON subscriptions(subscription_id);
CREATE INDEX subscriptions_customer ON subscriptions(customer_id);

-- Every webhook delivery, by the provider's event id. Paddle retries, and a retried
-- "subscription.canceled" applied twice is harmless while a retried payment is not — so the id is
-- the primary key and a duplicate is a no-op rather than a second application.
CREATE TABLE billing_events (
    event_id     TEXT PRIMARY KEY,
    event_type   TEXT NOT NULL,
    user_id      TEXT,
    received_utc TEXT NOT NULL
);

CREATE INDEX billing_events_received ON billing_events(received_utc);
