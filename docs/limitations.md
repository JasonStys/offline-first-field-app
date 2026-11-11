# Limitations

- The standalone API default is volatile and single-process; restarting it loses records, cursors, and idempotency
  results. That makes protocol behavior inspectable but is not production durability.
- The native companion adds a bounded durable checkpoint and local capability; this is not an
  authenticated multi-user server or production database. Keep all reference modes local.
- Local database and attachment encryption rely on future platform integration.
- The dashboard exposes explicit text conflict review but does not yet expose attachment selection.
- Older queued work without an original recorded snapshot cannot reconstruct that snapshot; the
  review labels its baseline unavailable and requires explicit choices for different values.
- Synchronization processes one bounded page per operator-triggered cycle; no background scheduler or
  automatic backoff is claimed.
- Android and Windows compile in automation. iOS/Mac Catalyst source targets are intentionally not
  declared until signing hosts and device validation are available.
- The Windows native harness exercises actual controls and captures their own render surface;
  physical-mobile/Appium testing and assistive-technology certification are not claimed.
- Windows x64 packaging is unsigned and unpackaged. No MSIX/installer, clean-machine or macOS/Linux
  native distribution is claimed. Existing .NET/Windows App SDK runtime bytes are bundled.
- A client already synchronized with another authority is not silently migrated to the local
  companion. Restore a complete matching profile or use the original source development API.
- Visible JSON export contains at most 100 displayed records, not outbox/attachments/server state
  or a complete backup. Native content search is a bounded scan, separate from indexed prefix search.
- The performance budget uses generated desktop/CI fixtures; it is not a representative field-device
  benchmark or load test.
- The reference cursor is numeric and unsigned. A public multi-tenant protocol should use opaque,
  signed, expiring tokens with retention semantics.
- All data is generated. The project has not been validated for regulated, medical, biometric,
  location, or customer information.
