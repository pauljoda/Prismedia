---
id: security
title: Security & Responsible Disclosure
sidebar_label: Reporting Security Issues
description: Report a Prismedia vulnerability privately, learn how security reports are handled, and see researcher acknowledgments.
---

# Security & Responsible Disclosure

If you find a way to access media without permission, bypass sign-in, or expose
private data, please report it privately so we can investigate and coordinate a fix.

## Report a vulnerability

Use [GitHub's private vulnerability reporting form](https://github.com/pauljoda/Prismedia/security/advisories/new).
You can also open the repository's **Security** tab and choose **Report a vulnerability**.
You will need to sign in to GitHub.

Keep suspected vulnerabilities and reproduction details out of public issues,
pull requests, and community posts. For ordinary setup questions or bugs,
use the [support page](/support).

Include:

- The Prismedia version and image digest or commit you tested.
- What should have been protected, the account permissions involved, and what happened.
- Reproduction steps using test accounts and synthetic media where possible.
- Whether you would like public credit, and the name or handle to use.

Remove passwords, access tokens, private library paths, and personal media from
attachments. Test only instances and accounts you own or have permission to assess.

## What happens next

Maintainers investigate the report and coordinate verification, an update, and
disclosure with the reporter. Follow up in the private report for status. Response
and release times depend on maintainer availability and the issue's impact.

Security fixes target the latest stable release. Testing channels may receive a
fix first; the version or image digest identified in an advisory is what determines
whether an installation includes it.

The repository's [security policy](https://github.com/pauljoda/Prismedia/blob/main/SECURITY.md)
defines the reporting scope and supported versions. For deployment guidance, see
[Authentication](./authentication.md) and [Reverse Proxy](./reverse-proxy.md).

## Security acknowledgments

Thank you to the researchers who help make Prismedia safer through responsible
disclosure. Contributors are listed with their permission.

- Furkan Arslan
