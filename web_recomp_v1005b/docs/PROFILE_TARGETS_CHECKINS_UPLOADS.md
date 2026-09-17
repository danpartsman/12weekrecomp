# Web Recomp — Future Profile, Targets, Check-ins, and Uploads

This document records the planned user Profile capability discussed during v1003.

## Product intent

The Profile area should become the user's long-term control center for personal goals,
baseline information, progress evidence, and recurring check-ins.

It should not merely be a page that overwrites a few fields. Changes that affect Recomp
recommendations should be historically traceable so the engine can understand what the
target was at a given time.

## 1. Profile

Potential Profile capabilities:

- display name / account-facing information
- preferred units
- timezone
- training preferences
- gym/equipment availability
- profile image, if desired
- coach relationship / assigned coach information
- account/access status
- future subscription/entitlement summary

Authentication identity remains separate from editable Recomp profile data.

## 2. Editable targets

Users should be able to review and update targets such as:

- goal body weight
- body-fat / body-composition goal
- calorie or rate-of-loss strategy
- protein target
- training-frequency target
- strength / performance goals
- workout schedule preferences
- cardio / conditioning goals
- recovery / sleep goals
- timeline or milestone dates where appropriate

Important design rule:
Target changes should be effective-dated or versioned rather than destructively replacing
historical targets. This lets future analytics answer questions such as:

- What target was active during this month?
- Did the strategy change?
- Did results improve after the change?

## 3. Check-ins

The Profile area should support recurring check-ins. Some can be quick structured inputs;
others may be configurable later.

Candidate check-ins:

- body weight / measurements
- progress / body-composition review
- training adherence
- nutrition adherence
- recovery / soreness / fatigue
- sleep quality
- motivation / perceived readiness
- pain / injury flags
- hunger / diet difficulty
- stress
- subjective progress notes
- coach-requested check-in questions

Check-ins should be dated and retained as history.

Future Recomp logic may use selected check-in variables in recommendations, but new inputs
should not automatically alter validated engine formulas until explicitly incorporated and
tested.

## 4. Uploads

The Profile/check-in system should support user uploads when implemented.

Likely use cases:

- progress photos
- body-composition reports
- measurement screenshots
- training or nutrition documents
- coach-requested supporting files

Uploads should be attached to the authenticated user/client and optionally to a specific
dated check-in.

Security requirements:

- never expose one client's upload to another client
- coach visibility only when coach access to that client is explicitly granted
- store metadata separately from file storage
- use controlled/private blob storage in production rather than public file URLs
- validate file type and size
- preserve upload date and ownership
- support later deletion/privacy controls

## 5. Coach functionality

For clients assigned to a coach, future coach views may show:

- latest check-in status
- overdue check-ins
- target changes
- progress photos where authorized
- adherence/recovery summaries
- notes from the client
- coach-entered feedback
- trend history

This supports the broader coach product direction:
one coach can supervise many clients while Recomp provides structured day-to-day data
between direct coaching contacts.

## 6. Recommended implementation sequence

Do NOT add all of this during the current authentication checkpoint.

Suggested sequence:

1. Finish and lock v1003 identity/access/navigation.
2. Add Profile shell and current-target display.
3. Add target history/versioning.
4. Add structured check-ins.
5. Add secure uploads/private storage.
6. Add coach-facing client check-in views.
7. Feed selected validated check-in signals into the Recomp engine only after testing.

## 7. Current v1003 rule

The current v1003 patch only adds responsive navigation and Sign Out.

This document records the Profile/Targets/Check-ins/Uploads direction so it is deliberately
designed into later versions rather than forgotten or improvised.
