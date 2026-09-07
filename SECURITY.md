# Security policy

## Supported versions

Security fixes target the latest stable Prismedia release. Dev, alpha, and beta
images may receive a fix first, but are testing channels rather than separately
supported release lines. An advisory identifies the affected versions and the
first verified fixed version or image digest; a moving channel tag alone does
not identify whether an installation is fixed.

## Report a vulnerability privately

Use [GitHub's private vulnerability reporting form](https://github.com/pauljoda/Prismedia/security/advisories/new).
Please do not post suspected vulnerabilities, credentials, or exploit details
in public issues or discussions.

Include the Prismedia version and image digest or commit, the expected security
boundary, the account permissions involved, reproducible steps, and the impact.
Use synthetic media and test accounts where possible. Remove tokens, passwords,
private library paths, and personal media from attachments. Tell us whether you
would like public credit and which name or handle to use.

Maintainers investigate reports, coordinate fixes and releases, and arrange
credit with the reporter. Please allow time for a verified update to reach users
before public disclosure. Follow up in the private report for status; response
and release times depend on maintainer availability and the issue's impact.

## Scope

Relevant boundaries include authentication, library access and NSFW permissions,
media streaming and downloads, filesystem containment, archive handling, plugin
execution, secrets, and update artifacts. A private LAN deployment does not make
an authorization bypass acceptable. Report a bypass even if it requires knowing
an entity identifier.

Test only instances and accounts you own or have permission to assess. Do not
access other people's media or interrupt their downloads. Third-party services
and plugins may need a coordinated report to their own maintainers.

Maintainers follow the [security response process](docs/security-response.md).

## Security acknowledgments

Thank you to the researchers who help make Prismedia safer through responsible
disclosure. Contributors are listed with their permission.

- Furkan Arslan
