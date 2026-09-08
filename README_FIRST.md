# 12 Week Recomp — LIVE Upload Package

This is the approved photo-heavy public prelaunch direction converted into a real ASP.NET Core LIVE application.

## Intended environment

- GitHub branch: `main`
- Azure Web App: `recomp-live`
- Azure SQL DB: `sqldb-recomp-live`
- Public domain: `12weekrecomp.com`
- TEST remains isolated at `test.12weekrecomp.com`

## Public pages

- Welcome
- Evidence
- Join
- Thanks
- Privacy
- Error
- `/health`

There are no unfinished Dashboard, account, onboarding, admin, or product routes in this project.

## Included public positioning

- $20 for 12 weeks
- individualized recommendation engine
- daily trainer messages
- weekly progress notifications, hints, and tips
- three default coach roles:
  - Cassie — Main Trainer & Nutritionist
  - Marisol — Motivation, Technique, Mobility & Fat-Loss-Day Specialist
  - Steve — Workout Technique, Progressive Overload & Muscle-Building Specialist

## Signup behavior

Fields:

- Name
- Email
- Interest:
  - `FREE_UPDATES`
  - `FOUNDERS_RATE`

Protection:

- Razor Pages antiforgery
- honeypot
- 6 submissions / 10 minutes rate limit
- duplicate email updates existing row

## Manual GitHub upload

1. Extract this ZIP.
2. In GitHub, switch to branch `main`.
3. Choose **Add file → Upload files**.
4. Drag the CONTENTS of the extracted `12WR_Live_Upload_Package_v1` folder into GitHub.
5. Commit directly to `main`.

Do not upload the ZIP itself.

## Before the signup form can work

Create the isolated LIVE database, then run:

`database/001_launch_interests.sql`

Configure Azure App Service connection string:

- Name: `LiveDb`
- Type: SQLAzure
- Value: LIVE database connection string

Do not place the production connection string in GitHub.
