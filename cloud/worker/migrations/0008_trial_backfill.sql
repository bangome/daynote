-- Grant the free trial to accounts that existed before 0006 added the column.
--
-- 0006 added `trial_ends_utc` with a plain ALTER and no backfill, so every account already in the
-- database was left with NULL — and `entitlement.resolve()` reads NULL as "no trial", which is
-- indistinguishable from "trial finished". Those accounts were silently never given the 14 days
-- they were promised, and could never sync attachments without subscribing.
--
-- It went unnoticed for the ordinary reason: a fresh database has no such rows, and every test
-- creates its accounts through the sign-in path, which does set the column. It only exists in a
-- database that was migrated rather than created, which is exactly the one in production. The
-- symptom was a 402 on file sync for an account that had never had a chance to use its trial.
--
-- The backfill is `created_utc + 14 days` rather than `now + 14 days`: that is what sign-up would
-- have written, so an account old enough for its trial to have lapsed gets a lapsed one rather
-- than a windfall, and an account created within the window gets the remainder it was owed.
--
-- `substr(created_utc, 1, 23)` trims .NET's 7 fractional digits to the 3 SQLite's date functions
-- parse; `|| '0000Z'` puts them back, so the result is canonical (src/time.ts) and still compares
-- as a plain string against every other timestamp here.
UPDATE users
   SET trial_ends_utc =
       strftime('%Y-%m-%dT%H:%M:%f', substr(created_utc, 1, 23), '+14 days') || '0000Z'
 WHERE trial_ends_utc IS NULL;
