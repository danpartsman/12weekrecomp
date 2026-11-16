# Snap Workout late fee outside workout maximum

The workout maximum caps completion fees, incomplete-exercise fees, and extra-rep incentives. A late-completion fee is added separately. With a $30 workout maximum and a $20 late fee, the combined charge may reach $50. A zero workout maximum does not waive a late fee.

This applies to new and unfinished Snap Workouts. The first final submission records the separate-fee policy on the result; corrections preserve it. Workouts already finalized before this change retain their original calculation and posted amounts, preventing background ledger refreshes from increasing historical charges. Assignment timing, on-time minimum incentives, and the Finish submission flow remain unchanged.

The assignment form, workout view, and completion summary explain which maximum applies. Regression tests cover capped incentives, incomplete-work fees, zero/small caps, on-time minimums, legacy finalized results, first final submission, repeated submissions, and corrections.
