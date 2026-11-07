# Code signing policy

Free code signing provided by SignPath.io, certificate by SignPath Foundation.

> **Status note:** DiskCheck TW is applying to the SignPath Foundation open-source
> code signing program. Releases up to and including v0.1.3 are unsigned. Once
> approval is granted, signed releases will be marked in the release notes.

## What is signed

The following artifacts are signed:

- `DiskCheckTW.App.exe`, the main application executable.
- The bundled files included in the portable package.
- The NSIS installer `DiskCheckTW-<version>-setup.exe`.

Only artifacts produced by the project's own build are signed. Third-party upstream
files keep their original signatures when present and are not re-signed by this
project.

## How releases are built

Every release is built by GitHub Actions on GitHub-hosted `windows-latest` runners
from the public repository
https://github.com/365freegotw-tech/diskcheck-tw using the workflows in
`.github/workflows/` (`build.yml` for continuous integration and `release-signed.yml` for
signed releases). Only artifacts from that CI build are submitted for
signing; nothing built on a personal machine is signed.

Signing requests are only allowed for builds from the `main` branch or from release
tags matching `v*`. The build configuration, the build scripts and the installer
script are part of the repository and are reviewed like source code.

## Signing process

The private key is generated and stored in SignPath's hardware security module (HSM);
the project never has access to the private key. Every signing request must be
manually approved by an approver before a signature is applied.

## Team roles

| Role | Responsibility | Members |
| --- | --- | --- |
| Authors | Committers, trusted to modify source code | 365freegotw-tech |
| Reviewers | Review every change from non-committers, such as pull requests, including CI and build scripts | 365freegotw-tech |
| Approvers | Approve each signing request | 365freegotw-tech |

GitHub profile: https://github.com/365freegotw-tech

All team members use multi-factor authentication for GitHub and for SignPath.

## Privacy policy

This program will not transfer any information to other networked systems unless specifically requested by the user or the person installing or operating it.

DiskCheck TW has no telemetry and no network access. The only outbound action is
opening the official website https://keelungit.com/diskcheck-tw/ in the default
browser when the user clicks the link. HTML reports are saved only where the user
chooses, with serial numbers masked by default.

## Reporting a problem

For security issues, see SECURITY.md. For all other issues, open a report in GitHub
Issues.
