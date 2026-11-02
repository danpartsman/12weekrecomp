# Separate balances for each coach

Fees and payments are now viewed by `(client, coach)` rather than as a single shared balance. Client owners choose between current and previous coaches. A coach sees only their own account. Entry and reversal endpoints derive the coach from the authenticated user; selecting a URL cannot redirect a payment to another coach or reverse another coach's entry.

The original append-only event history is preserved. New ledger entries include `coachUserId`. Existing manual entries are attributed to the recording coach when their relationship is documented; automatic Snap charges use the first saved configuration containing that assignment. Reversals inherit the original entry's coach. Unresolved entries remain in a separate, client-visible “Earlier shared account · coach unconfirmed” account. They are never silently assigned to the current coach. Signed agreement JSON is unchanged. No SQL migration is needed.

Disconnecting preserves history and allows the former coach to record final payments and reverse their own payment entries. They cannot add new fees or access current workout/progress screens through the ledger. Automatic posting skips disconnected coaches and Snap assignments from an earlier connection when the same coach reconnects through a new invitation. The client's My Coach page retains its accounts link after all coaches disconnect. Coaches have a Previous client accounts section for settlement.

The financial views share the existing audit stream and its revision concurrency control. This change does not create additional independent signed agreements or change the existing shared Snap Workout page.

Validation: web test suite, including independent balances, idempotent payments, cross-coach reversal rejection, result/deadline attribution, former-coach settlement, reconnect behavior, legacy attribution, and HTTP access/selection tests. JavaScript accountability calculations and existing ledger browser checks are also run.

Rollback: retain a ledger reader that accepts the optional `coachUserId` JSON field. An application version predating this field uses strict JSON parsing and cannot read new entries without that compatibility change. Do not delete or rewrite financial events to roll back presentation changes.
