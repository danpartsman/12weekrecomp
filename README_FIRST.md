# 12 Week Recomp LIVE v2.3

This package is the visual refresh of the working v2.2 LIVE launch application.

## What changed
- Uses the supplied official 12 Week Recomp logo in the site header/footer.
- Uses supplied real Cassie, Marisol, and Steve photography.
- Keeps the existing six-person hero image on the Welcome page.
- Refreshes the Welcome, Evidence, and Join pages with the supplied team photography.
- Replaces the prior individual Cassie/Marisol/Steve visuals with crops from the supplied real team photo.

## What did NOT change
- Microsoft Graph email transport.
- Signup welcome email logic.
- Admin broadcast email logic.
- Unsubscribe workflow.
- Email delivery logging.
- Entra Administrator/Trainer role authorization.
- Database schema or SQL migrations.
- LIVE health endpoint.

No database migration is required for v2.3.
Existing Azure App Service settings such as `Email__SenderMailbox`, `Email__Enabled`, and the LIVE database connection remain Azure-side settings and are not replaced by this source package.
