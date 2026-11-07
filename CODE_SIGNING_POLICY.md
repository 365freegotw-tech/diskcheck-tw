# Code signing policy

## Current status

- **GitHub Releases and website downloads** (installer and portable ZIP) are not code
  signed. Windows SmartScreen may show "Unknown publisher". Verify the SHA256 listed on
  the release page before running.
- **Microsoft Store edition** (MSIX) is signed by Microsoft after Store certification.
  The package is built by `.github/workflows/msix.yml`.

A SignPath Foundation application is not active at the moment. The signing workflow
draft (`.github/workflows/release-signed.yml`) stays disabled unless the repository
variable `SIGNPATH_ENABLED` is set to `true`.

## How releases are built

Every release is built by GitHub Actions on GitHub-hosted `windows-latest` runners
from the public repository https://github.com/365freegotw-tech/diskcheck-tw using the
workflows in `.github/workflows/`. Nothing built on a personal machine is published.
The build configuration, the build scripts and the installer script are part of the
repository and are reviewed like source code.

## Team roles

| Role | Responsibility | Members |
| --- | --- | --- |
| Authors | Committers, trusted to modify source code | 365freegotw-tech |
| Reviewers | Review every change from non-committers, including CI and build scripts | 365freegotw-tech |
| Approvers | Approve each release | 365freegotw-tech |

## Privacy policy

This program will not transfer any information to other networked systems unless specifically requested by the user or the person installing or operating it.

Full policy: https://keelungit.com/diskcheck-tw/privacy/

## Reporting a problem

For security issues, see SECURITY.md. For all other issues, open a report in GitHub
Issues.
