# Fitdays TEST preview

Route: /Fitdays, linked from Profile only on the Azure application named recomp-test. Every controller action checks WEBSITE_SITE_NAME server-side; request host headers cannot enable the feature on LIVE.

This is an authenticated, explicit-consent, read-only preview. It reads the last 30 days, requires person selection when multiple profiles exist, and writes no measurement or connection database records. A random HttpOnly/Secure/SameSite=Strict cookie points to a server-memory session bound to the Recomp client ID. Sessions expire absolutely after 20 minutes; passwords are never stored. Disconnect removes the cached token/readings. App restart clears the cache. Multi-instance deployments may require reconnecting; this preview is not a durable connector.

Protocol source: https://github.com/roquerodrigo/fitdays-api (MIT). Login: POST api/users/login; sync: POST api/sync/syncFromServer. Date bounds are reversed by provider convention (start_time newest, end_time oldest). Provider HTTP logging is disabled because token is a query parameter. Redirects are bounded and allow only the three fixed HTTPS Fitdays regional origins. No automatic HTTP redirects or cookie forwarding. Responses limited to 2MB and requests time-bounded; connect requests rate limited. Error pages never display upstream responses or credentials.

Weight uses explicit grams or kilograms converted to lb, with lb fallback. Body-composition codes are displayed raw until verified against a real account. Imported dates would need to preserve the client's configured time zone. Deleted records and unrecognized profiles are excluded.

Not yet implemented: durable credential/token storage, automatic sync, token refresh, importing records, manual override precedence, provider-delete propagation. No real account has been tested by the coding agent; the user can connect through the TEST form, without putting credentials in chat.

Protocol attribution: copyright (c) 2026 Rodrigo Roque. The Fitdays SDK license is reproduced in FITDAYS_SDK_LICENSE.txt.
