# Azure next steps

Do not create or change DNS until the Azure LIVE site has been tested on its Azure hostname.

1. Verify the actual Azure resource group names and current subscription.
2. Create isolated LIVE resources:
   - Web App: `recomp-live`
   - Database: `sqldb-recomp-live`
3. Set App Service:
   - `ASPNETCORE_ENVIRONMENT=Production`
   - `SiteMode=LIVE_LAUNCH`
4. Configure connection string `LiveDb`.
5. Run `database/001_launch_interests.sql` against the LIVE database.
6. Connect GitHub `main` to `recomp-live`.
7. Validate:
   - `/`
   - `/evidence`
   - `/join`
   - `/privacy`
   - `/health`
   - valid signup
   - duplicate email update
   - rate limiting
8. Only after Azure-hostname validation, attach:
   - `12weekrecomp.com`
   - `www.12weekrecomp.com`
9. Preserve all email DNS records (MX/SPF/DKIM/DMARC).
10. Enable HTTPS.
