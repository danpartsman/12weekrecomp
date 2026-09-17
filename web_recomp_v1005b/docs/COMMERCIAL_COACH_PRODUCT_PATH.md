# Web Recomp — Commercial / Coach Product Path

This document records the product direction agreed during v1003 development.

## 1. Test site versus production

`recomp-test` remains a development and invited-testing environment.

Invited testers are not charged.

Production will later be created as a separate environment rather than converting the
test site in place. Production should have its own:

- Azure App Service
- production SQL database
- identity configuration
- secrets
- payment-provider credentials
- hostname/custom domain
- monitoring and operational settings

The test environment should remain useful for feature validation after production exists.

## 2. Public customer experience

The customer-facing site should eventually provide a public Welcome page that explains
Web Recomp and provides:

- Sign In
- Create Account
- Pricing
- Terms
- Privacy

Intake, Workout Plan, Workout Actual, Nutrition, Weigh In Stats, Dashboard, and coach/client
data should require authenticated access.

Expected member flows:

### Returning member
Welcome -> Sign In -> Authentication -> entitlement check -> Dashboard

If authenticated but Intake is incomplete:
Authentication -> Intake

### New member
Welcome -> Create Account -> Authentication -> entitlement/payment path -> Intake ->
Workout Plan -> Dashboard

For the test site, invited testers bypass payment.

## 3. Identity is separate from entitlement

Authentication answers:

> Who is this person?

Recomp entitlement answers:

> Why is this person allowed to use the program, and until when?

Payment must not be the identity system.

Potential entitlement/access states or reasons include:

- TESTER
- TRIAL
- COMP
- COACH_SPONSORED
- PAID
- PAST_DUE
- CANCELED
- EXPIRED

Cancellation or expiration should not delete historical client data. It changes access,
not ownership/history.

## 4. Coach model

A normal member generally owns one personal Client profile.

A coach may have access to multiple Client profiles.

An administrator may have explicitly granted access across multiple clients.

`UserClientAccess` is the foundation for this:

- OWNER — member's own profile
- COACH — coach access to a client's profile
- future administrative/support roles as needed

Normal member pages should prefer the user's OWNER profile.

Coach workflows should explicitly select a client. The system must never silently choose
an arbitrary client when a coach has access to multiple people.

## 5. Invitations

Recomp should support invitations issued by an administrator or authorized coach.

Examples:

- Invite Tester — complimentary access, no expiration
- Invite Tester — 90-day access
- Invite Client — 30-day free trial
- Invite Client — 60-day free trial
- Invite Client — coach-sponsored complimentary access

Conceptual flow:

Coach/Admin -> Invite Client -> email/link -> recipient authenticates -> Intake ->
Client/profile created -> inviter receives appropriate COACH access -> entitlement/trial
is attached to the member.

The invitation should be unique, time-limited where appropriate, and auditable.

## 6. Why this matters as a coach product

Coach-facing functionality is not merely administration; it is a potential commercial
selling point.

Potential coach value proposition:

- One coach account can manage multiple Recomp clients.
- Invite clients directly into the platform.
- Grant trial or complimentary access without requiring payment.
- Review each client's Workout Plan, actual workouts, Nutrition, body measurements,
  recovery, and Recomp recommendations.
- Recomp provides a structured daily decision engine between coaching contacts.
- Coach and client can see planned versus recommended versus actual behavior.
- The platform can surface adherence, fueling, recovery, and training-load trends.
- Coaches can use Recomp to scale personalized oversight across more clients without
  manually recalculating every daily recommendation.
- Future coach plans can be sold as multi-seat / client-capacity subscriptions.

Potential future commercial packaging:

- Individual Recomp subscription
- Coach Starter — limited number of active clients
- Coach Pro — larger client roster and coach analytics
- Organization/Team — multiple coaches and shared client administration

## 7. Version path

### v1003
Identity and access foundation:
- authentication
- public Welcome/sign-in/create-account path
- user isolation
- OWNER / COACH capable access model
- invited tester/trial/complimentary-access foundation
- keep test users free

### v1004
Core engine expansion already planned:
- centralized recalculation
- reusable custom workouts / My Workouts
- preserve validated formulas initially

### Later commercial release
- production External ID/customer identity architecture if needed
- payment checkout/provider integration
- subscriptions and renewals
- failed-payment handling
- coupons/promotions
- production entitlement enforcement
- coach subscription tiers / client-seat limits
- invitation email delivery and richer coach portal

## 8. Current rule

Do not add payment complexity merely to make the test site commercial-looking.

Build the identity/access/entitlement boundaries correctly now, allow testers and coach-
invited users to receive free/trial access, and add real charging when the production
environment is ready.
