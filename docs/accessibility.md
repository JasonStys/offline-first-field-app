# Accessibility

## Implemented baseline

- Native MAUI `Entry`, `Editor`, `Button`, `Label`, and `CollectionView` controls.
- Visible labels plus semantic descriptions for the input fields.
- Stable automation IDs for title, notes, save, sync, status, and history.
- Minimum 44-device-unit interactive control height.
- Text uses platform scaling instead of fixed pixel rendering.
- Sync and save outcomes are announced through the platform semantic screen reader.
- Layout avoids color-only status meaning and supports right-to-left platform behavior.
- Android declares RTL support and disables cleartext traffic/backups.

## Manual verification

The [manual accessibility checklist](reports/manual-accessibility-checklist.md) covers Windows
Narrator, Android TalkBack, keyboard/switch order, text scaling, contrast, orientation, motion, and
offline/error announcements. Items remain unchecked until someone records the device, OS, assistive
technology, version, and observations.

Platform compilation and semantic properties are useful evidence but are not a WCAG or mobile
accessibility certification.
