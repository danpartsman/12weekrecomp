# Web Recomp v1005b - Recovery Checkpoint

## Release status

v1005b is the locked authentication/customer-account baseline.

Validated:

- Microsoft Entra External ID customer sign-up
- Returning customer sign-in
- ASP.NET Core OIDC authentication
- Customer account selection
- Promptless sign-out
- New customer -> Onboarding
- Completed customer -> Dashboard
- Multi-user isolation
- Dashboard missing-weight handling
- Autosave operational entry screens
- Required-field asterisks
- Evidence/public navigation cleanup
- Arithmetic regression gate passed
- Custom External ID branding working

---

## Authentication architecture

Authentication is handled inside the ASP.NET Core application using:

- Microsoft.Identity.Web
- OpenID Connect
- ASP.NET Core authentication cookie

Azure App Service Easy Auth is DISABLED.

Do not re-enable Easy Auth unless intentionally rolling back the
application-level authentication architecture.

Primary flow:

test.12weekrecomp.com
    ->
/Account/SignIn
    ->
Microsoft Entra External ID
    ->
/signin-oidc
    ->
ASP.NET Core authentication cookie
    ->
/Home/AfterSignIn
    ->
Onboarding or Dashboard

---

## Microsoft Entra External ID

External tenant:

Display name:
12 Week Recomp

Tenant domain:
12weekrecomp.onmicrosoft.com

Tenant ID:
ea48872e-ed8d-4910-9429-b22defdfbab2

External ID authority:
https://12weekrecomp.ciamlogin.com/

Test application registration:

12 Week Recomp - Test

Application / Client ID:
dff3324c-faec-4860-9f15-a78c999005ba

Primary application redirect URI:
https://test.12weekrecomp.com/signin-oidc

Signed-out callback:
https://test.12weekrecomp.com/signout-callback-oidc

External ID user flow:
RecompSignUpSignInTest

Authentication:
Email + password

Signup attributes:
- Display Name
- Given Name
- Surname

ID token issuance:
Enabled

Optional ID-token claim:
login_hint

IMPORTANT:
Client-secret VALUES are intentionally NOT stored in this document.

ASP.NET Core App Service secret-setting name:

AzureAd__ClientCredentials__0__ClientSecret

---

## Azure

Resource group:
recomp_group

Test App Service:
recomp-test

Working Azure hostname:
https://recomp-test-cxhvadh7acd9hmb3.westus2-01.azurewebsites.net

Public test hostname:
https://test.12weekrecomp.com

Application framework:
ASP.NET Core / .NET 10

App Service authentication / Easy Auth:
DISABLED

Application authentication:
Microsoft.Identity.Web / OIDC

Health endpoint:
/health

System status endpoint:
/api/system/status

Expected specification:
1005

Expected engine:
1001.0

IMPORTANT:
Database passwords, connection strings, client secrets and other
credential values are intentionally NOT stored in this document.

---

## Cloudflare

Domain:
12weekrecomp.com

Test hostname:
test.12weekrecomp.com

DNS:

Type:
CNAME

Name:
test

Target:
recomp-test-cxhvadh7acd9hmb3.westus2-01.azurewebsites.net

Proxy:
Enabled / orange-cloud

Worker:
recomp-test-proxy

Worker route:
test.12weekrecomp.com/*

Worker purpose:
Reverse proxy the branded hostname to the Azure F1 App Service while
preserving the public host/protocol information required by ASP.NET OIDC.

Current Worker architecture:

- Origin is the long Azure App Service hostname
- X-Forwarded-Host is supplied
- X-Forwarded-Proto is supplied
- X-Original-Host is supplied
- Redirects from the Azure origin are rewritten to test.12weekrecomp.com

---

## ASP.NET forwarded-header configuration

Cloudflare supplies:

X-Original-Host
X-Forwarded-Proto

ASP.NET Core processes forwarded headers BEFORE:

- HTTPS redirection
- Authentication
- Authorization

This ordering is required for the branded OIDC callback URL.

---

## Identity mapping

Recomp customer identity is mapped primarily using the External ID:

oid

Fallback:
sub

Email address is not the durable database identity key.

This allows customer email information to change without changing the
Recomp ownership identity.

---

## Version progression

LOCKED:
v1005b

NEXT DEVELOPMENT VERSION:
v1006

v1006 scope:

- Guided Demo
- Fresh Demo
- Demo invitation codes/links
- Demo administration
- expiration / usage limits
- persistent demo sessions
- isolated demo workspaces
- demo cleanup
- Guided -> Fresh conversion
- Fresh -> permanent customer conversion

Previously planned centralized recalculation/custom-workout work moves
to v1007.

---

## Security

Never commit or archive plaintext:

- Entra client-secret values
- SQL passwords
- database connection strings containing credentials
- access tokens
- refresh tokens
- /.auth/me output
- Azure publishing credentials

Secret values belong in Azure configuration / secret storage only.
