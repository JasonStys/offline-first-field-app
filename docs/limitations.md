# Limitations

- The API store is volatile and single-process; restarting it loses records, cursors, and idempotency
  results. That makes protocol behavior inspectable but is not production durability.
- The API has no authentication, authorization, tenant isolation, or rate limiting. Keep it local.
- Local database and attachment encryption rely on future platform integration.
- Version one captures text and implements attachment storage, but the dashboard does not yet expose
  attachment selection or conflict-resolution screens.
- Synchronization processes one bounded page per operator-triggered cycle; no background scheduler or
  automatic backoff is claimed.
- Android and Windows compile in automation. iOS/Mac Catalyst source targets are intentionally not
  declared until signing hosts and device validation are available.
- Hosted device UI automation is not present. Semantic controls, automation IDs, builds, and a manual
  accessibility checklist do not equal assistive-technology certification.
- The performance budget uses generated desktop/CI fixtures; it is not a representative field-device
  benchmark or load test.
- The reference cursor is numeric and unsigned. A public multi-tenant protocol should use opaque,
  signed, expiring tokens with retention semantics.
- All data is generated. The project has not been validated for regulated, medical, biometric,
  location, or customer information.
