# Installed Windows 1.1.0 validation · 11 November 2026

By Jason Stys. These observations refer to the actual independently downloaded, published **1.1.0** application built from `514913be78a45876ad90a8ced6d9cbeaecbb4b37`. Historical Office editions, failed-run evidence and published assets remain unchanged.

## Observed validation

**Actual installed MAUI: Passed.** Two real installed native launches passed editing, synchronization, explicit conflicts, exact retained records and normal exit codes 0/0.

**Domain and boundaries: Passed.** The source suite passed 90 tests with 94% line coverage. Fourteen real Windows profile controls and 105-row/53-filtered exact-export regressions passed.

**Build and security: Passed.** Current main CI, actual Windows native acceptance, Android release build and processed C# analysis of both core/API and MAUI source passed.

**Portable distribution: Passed.** Five independently downloaded assets matched provider hashes. Installed application and .NET 10.0.15 payload hashes matched; individual icon targets passed read-back.

## Illustrated workflow and implementation

The [four-page application guide](windows-application-guide-1.1.0.docx) contains two actual installed-app screenshots, a worked user journey, an important immutable-source code excerpt and detailed results/operating limits. Every final page content was inspected and the OOXML schema passed. The [manifest](windows-application-guide-1.1.0.json) binds document, screenshot, source and reviewed-page identities. Prepared examples are inputs inside the actual application, never separate demo apps.

## Installation, data and limits

Download the Windows ZIP and its checksum from official Releases. Extract into a fresh folder and launch FieldOps.App.exe; retain old versions and stopped-profile backups. The self-contained app needs no system .NET setup.

Open the individual Desktop icon, read Help and runtime notices, and use the application's own Quit controls. Prepared examples are inputs inside the actual app, never separate demo applications.

This Windows portable app is unsigned; it is not an MSI or Store package, and no clean-machine or mobile installation test is claimed. Local and hosted SDK patch versions differ; the installed self-contained runtime is verified as 10.0.15.

The companion is a trusted-OS-owner reference, not a production multiuser identity service or cryptographic remote attestation. Profiles are plaintext local data. Normal profiles, system SDKs and Docker were unchanged.

Individual Desktop and Start-menu targets were read-back verified. Actual installed-executable testing is distinct from normal default-profile shortcut activation, which was not independently exercised. Normal profiles, account data and Docker were not changed. This documentation revision must pass its own current hosted workflows and processed security analysis; historical runtime evidence does not certify later source changes.
