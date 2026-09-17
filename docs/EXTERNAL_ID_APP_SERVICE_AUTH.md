# App Service Authentication / Microsoft Entra External ID

The application intentionally does not store customer passwords. It expects App Service Authentication to authenticate the browser before the request reaches ASP.NET Core.

The application reads:

- `X-MS-CLIENT-PRINCIPAL-ID`
- `X-MS-CLIENT-PRINCIPAL-NAME`

and maps that authenticated identity to `app.UserAccounts`.

## Test environment

1. Open the Web App in Azure Portal.
2. Open **Authentication**.
3. Add the Microsoft identity provider using the External ID tenant/app registration you prepared.
4. Require authentication for normal browser traffic.
5. Use a sign-up/sign-in user flow appropriate for the pilot.
6. Save.
7. Open the site in an InPrivate/incognito browser.
8. Register a test account.
9. Complete the Intake screen.

`WebRecomp__AllowDevelopmentUser` is set to `false` by the Azure configuration script, so an unauthenticated request cannot silently become a development user in Azure.

## Isolation test

After two pilot accounts exist:

1. Account A creates its client/program.
2. Account B creates a separate client/program.
3. Verify each dashboard shows only its own authorized client.
4. Do not use a browser-supplied client ID as authorization proof. The server resolves access from `app.UserClientAccess`.
