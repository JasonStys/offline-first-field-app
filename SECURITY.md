# Security policy

## Reporting

Please use GitHub's private vulnerability-reporting feature when available. Do not include secrets,
personal data, or exploitable production details in a public issue.

## Supported version

Only the latest commit on `main` is maintained. This repository is a portfolio reference system,
not a hosted production service.

## Security boundary

The API intentionally has no authentication and binds only where the operator directs it. Keep it
on loopback or an isolated development network. It must not receive real inspection records or
attachments. See [the security model](docs/security.md) for threat assumptions and controls.
