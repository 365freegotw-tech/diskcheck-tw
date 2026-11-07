# Security policy

## Supported versions

Only the latest release on [GitHub Releases](https://github.com/365freegotw-tech/diskcheck-tw/releases) receives fixes.

## Reporting a vulnerability

Please report security issues privately through GitHub:
**Security → Report a vulnerability** on this repository
(<https://github.com/365freegotw-tech/diskcheck-tw/security/advisories/new>).
Do not open a public issue for security problems.

Include the version, Windows version, steps to reproduce and the impact you observed.
We aim to acknowledge reports within 7 days.

## Scope notes

DiskCheck TW is designed to be read-only: the surface scan opens disks with `GENERIC_READ`
and never writes, locks or dismounts volumes, and SMART queries are limited to an allow-list
of read-only commands. Any way to make the program modify disk contents, send data over the
network, or run code from untrusted input is in scope.

See also: [Code signing policy](CODE_SIGNING_POLICY.md).
