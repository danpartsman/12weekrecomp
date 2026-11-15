# Snap Workout progression and conversation

Before this change, LIVE main bb3b0f70ee89ab5bd867b2c43fcfa9bf7be0d2e3 was copied to TEST. The deployed Test build preserves the Command Center, nutrition, and reporting changes. Separate environment data and settings were retained. Backup branches record both original branch heads.

Clients explicitly press Start workout. That records a durable start, queues one notification for the assigning coach using existing email/phone preferences, and offers the coach an in-portal popup with View workout progression. Starting does not restart the assigned deadline. The coach roster also exposes the active workout.

Reps, weights, and optional RPE autosave as draft progress. Open progression screens refresh approximately every three seconds; roster and popup checks run every ten seconds while visible. These are saved entries, not independently verified exercise completion. The assigning coach has a read-only results view. The client and assigning coach can append timestamped messages before, during, and after completion; other coaches cannot access the thread.

Only explicit Finish workout posts canonical training results and calculates final charges for new assignments. Existing assignments retain their original late-fee timing. Submitted results can be corrected only through an explicit correction submission, never background autosave. Existing fee caps, schedule rules, and completion summary remain in use.

Drafts and conversations use encrypted, atomic files under the existing persistent private notification storage. File leases, assignment fingerprints, and progress revisions prevent competing tabs from silently overwriting each other. Retried start/chat requests are idempotent, and identical lost-response saves can retry safely. Unsaved input is retained on network or conflict errors. Workouts edited by the coach require reloading; unchanged exercise rows retain saved draft sets.

Validation includes nine server integration/service tests covering isolation, persistence, notification delivery, concurrent tabs, prescription changes, final training and ledger idempotency, deferred fees, and revoked relationships. Eight DOM tests cover the client/coach flow, autosave, explicit Finish, reconnects, conflicts, safe chat rendering, correction flow, and delayed refreshes. Existing application and JavaScript regression suites also run in the LIVE deployment workflow, followed by desktop/mobile navigation and exercise lookup browser checks.
