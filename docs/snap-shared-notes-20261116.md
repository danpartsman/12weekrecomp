# One shared Notes thread for each Snap Workout

The coach's optional assignment note opens the workout's Notes thread. Both the client and assigning coach use the same reply field before, during, and after the workout. The active-workout screen places Notes above the exercise entries; the completion screen shows the same thread and composer beside the fee summary. Separate coach-note, client-completion-note, and chat boxes have been removed from those screens.

Existing assignment and completion notes are retained as attributed, timestamped entries in the encrypted workout conversation. Stable content keys prevent duplicate imports across refreshes and concurrent requests. Changed legacy assignment notes append an entry without erasing prior replies. Imports do not start a workout, alter progress revisions, post ledger entries, or send notifications. The old completion-note endpoint remains compatible with an already-open form and appends to the shared thread in production.

Replies remain private to the client and assigning coach. The shared thread updates automatically without requiring a workout to be started or a results form to be present. Messages also continue arriving if a prescription change requires the exercise panel to reload. Existing notes imported after newer replies display in chronological order; history pagination retains its sequence-based cursor.

Validation covers preserved legacy notes, duplicate imports, attributed replies, coach isolation, CSRF protection, one reply field, completion-page messaging, chronological display, pagination, and conversation availability during prescription conflicts, alongside the existing application and JavaScript regression suites. Billing and saved-workout behavior are unchanged.
