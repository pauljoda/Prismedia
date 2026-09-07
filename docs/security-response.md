# Security response process

## Intake and repository setup

Keep GitHub private vulnerability reporting enabled in repository Settings →
Security → Code security. Check that the Security tab offers **Report a
vulnerability** after repository transfers or settings changes. `SECURITY.md`
and the issue chooser must link to the private form.

Assign a maintainer to each report. Acknowledge receipt privately, establish a
follow-up date with the reporter, and record the affected commit or image,
reproduction, prerequisites, impact, and credit preference. A report received
by email belongs in a private draft advisory, not a public issue. Keep personal
details and deployment evidence out of tracked documentation and public CI logs.

## Reproduce and repair

1. Reproduce on an isolated instance using synthetic files and restricted test
   accounts. Record both the denied case and an allowed control. Distinguish
   observed behavior from claims that still need verification.
2. Trace the shared security boundary and inspect sibling entry points. For
   media access, cover source resolution, direct and transcoded playback, range
   requests, and HEAD requests. Hidden entities should behave as missing without
   opening files or starting transcoding.
3. Add a regression test that fails before the fix and passes afterward. Cover
   library grants, no grants, disabled roots, and NSFW restrictions as applicable.
   Prefer existing authorization services over a second set of permission rules.
4. Collaborate in a GitHub temporary private fork when needed. Do not push exploit
   details or an uncoordinated security patch to a public branch. Run the relevant
   tests and normal release checks; record exact commits and verified results.

## Release and disclosure

Determine affected versions from history and reproduction rather than assuming
all prior releases are vulnerable. Record severity with its assumptions and
request a CVE through the advisory when appropriate. Offer the reporter a chance
to verify the fix and confirm attribution privately.

Publish a tested fixed image through the normal release workflow. Record its
version, immutable digest, and fix commit, and verify the published artifact.
Coordinate advisory publication with availability of that fix. The advisory
should explain impact, prerequisites, affected and fixed versions, update steps,
any tested temporary mitigation, and agreed credit. Do not call a local commit
or a dev-only deployment a released fix.

After publication, link the advisory from release notes, verify the reporting
form still works, and track any remaining affected release lines. Close the
private report after communicating the outcome to the reporter. Keep durable
regressions in the suite; keep incident-specific working notes private.

## GitHub references

- [Managing private reports](https://docs.github.com/en/code-security/how-tos/report-and-fix-vulnerabilities/fix-reported-vulnerabilities/manage-vulnerability-reports)
- [Collaborating in a temporary private fork](https://docs.github.com/en/code-security/tutorials/fix-reported-vulnerabilities/collaborate-in-a-fork)
