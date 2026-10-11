# External Connections module

Independent .NET library for provider protocols and data contracts. It has no reference to the Recomp web app, EF database, dashboards, or prescription engine. The first adapter is Fitdays/FitdaysClient (cloud account connection; no new phone app).

The web host supplies authenticated user/admin screens, persistent encrypted account storage, scheduling and import orchestration under Services/ExternalConnections. All imported values pass through an explicit mapping into Recomp's measurement records. New protocol adapters belong here rather than inside fitness calculations.

Fitdays protocol attribution and license: ../../Documentation/FITDAYS_SDK_LICENSE.txt. This adapter remains unofficial. No programmable arbitrary URLs are accepted. UI settings manage supported provider availability, sync frequency/history, API app version, and metric units; a new provider still needs an adapter.
