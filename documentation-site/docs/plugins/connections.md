---
sidebar_position: 5
title: Connections and Integration Capabilities
description: Configure independent application instances through installed Prismedia plugins.
---

# Connections and integration capabilities

A **plugin** is an installed package. A **Connection** is one configured application
or catalog using that package. Several connections can use the same plugin with
independent URLs, settings, credentials, and enabled capabilities.

## Configure a connection

1. Install a compatible integration plugin from **Plugins**.
2. Open **Settings → Connections** and choose **Add connection**.
3. Select the plugin, name this instance, and enter the URL reachable by the Prismedia server.
4. Choose the capabilities to enable and enter connection-specific settings and credentials.
5. Save, then select **Test connection** to verify remote support.

Changing configuration requires another test. Disabling a connection revokes its
negotiated capabilities. Saved credentials are never returned to the browser:
leaving a saved credential blank preserves it, and selecting its removal clears it.
A verified application's address is bound to the connection. Create a new connection
for a different address; this prevents existing remote identifiers from being sent
to another server inadvertently.

The connection reports when an application supplies no persistent installation ID.
Such identifiers remain scoped to the configured connection; Prismedia cannot detect
a replaced installation solely from a stable URL. When an application does report
an installation ID, a changed ID blocks negotiated access and requires a new connection.

## Separate responsibilities

| Family | Responsibility |
| --- | --- |
| Metadata | Resolve identities and propose metadata through the existing identify protocol. |
| Catalog discovery | Search, browse, or inspect source items and selectable editions or releases. |
| Acquisition source | Resolve a selection into an acquisition offer. |
| Transfer executor | Submit and reconcile transfers, inspect durable jobs, and retrieve retained output manifests. |
| External manager | Delegate scoped monitoring, acquisition, and file organization. |
| Connected library | Search and inspect externally owned holdings. |

An integration declares only the operations it implements. Metadata search results
are not automatically downloadable items. A manager's completed command is not proof
of a file being available to Prismedia.

Connections provide configuration, capability testing, and a catalog browser at
**Requests → Browse catalogs**. The browser shows only tested connections that
currently advertise discovery. Administrators can choose a connection and media type,
open catalog sections, search supported sources, and move through result pages.

Catalog pages distinguish full-publication offers from loans, purchases, samples,
and external workflows. These distinctions do not grant permission to execute an
external workflow. Acquisition operations use separate typed orchestration contracts;
declaring an operation alone does not expose a generic executable RPC endpoint.

## OPDS catalogs

The OPDS Catalogs plugin consumes Atom OPDS 1.2 and JSON OPDS 2 catalogs. Configure
the catalog root URL, including a trailing slash when relative links require one.
Authentication can be Basic username/password or a bearer token. Each catalog is a
separate connection. Search is enabled only when the catalog advertises a supported
search template or OpenSearch description.

Navigation and authenticated retrieval are restricted to the configured origin.
Cross-origin downloads, checkout, borrowing, and indirect/DRM acquisition remain
external offers. EPUB/PDF ebook and CBZ/CBR comic catalogs are supported; an unmarked
PDF is treated as a book. This is a consumer interface: the remote catalog remains
the authority for which publications are visible to its configured account.

The discovery API replaces source locators with protected, expiring selection tokens.
Continuation tokens are bound to the connection, media type, query, container, and
page size. Changing a query requires starting a new page sequence. Plugin responses
are bounded and validated before reaching the browser. Resolved download addresses
and authentication headers remain server-only.

## Manifest declaration

Manifest v2 accepts an optional `integration` section. Its `protocolVersion` is
independent of metadata identify protocol v2. An integration-only package may declare
an empty metadata `supports` list. Metadata-only packages remain compatible.

```json
{
  "integration": {
    "protocolVersion": 1,
    "capabilities": [
      {
        "kind": "catalog-discovery",
        "operations": ["search", "browse"],
        "entityKinds": ["book"]
      }
    ],
    "settings": []
  }
}
```

Nonsecret `settings` use the existing typed field schema. The manifest's `auth`
fields describe write-only credentials for each connection. Metadata credentials
remain configured through the metadata provider's existing settings.

Effective support is the intersection of package declarations, the remote probe,
the host's supported operations, and the capabilities enabled for this connection.
An undeclared remote operation never grants additional authority. Invalid or duplicate
capability declarations and unsupported integration versions are rejected.

## Probe envelope

Native integration invocations use the same bounded .NET process transport as
metadata, with a distinct envelope discriminator:

```json
{
  "protocol": "prismedia-integration",
  "protocolVersion": 1,
  "invocationId": "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
  "operation": "probe",
  "connection": {
    "id": "11111111-2222-3333-4444-555555555555",
    "baseUrl": "http://catalog:8080",
    "expectedInstanceId": null,
    "settings": {},
    "auth": {}
  },
  "input": {}
}
```

Responses echo the protocol, version, and invocation ID. A successful `result`
contains `instanceId` (null if unavailable), `displayName`, optional `version`, and
`capabilities`. A failure returns `ok: false` with an error. Each call returns promptly;
future remote job polling must not keep a plugin process alive indefinitely.

## Credential storage and recovery

Connection credentials use ASP.NET Core Data Protection with a key ring in
`/data/keys/connections` (under the configured data directory). API and worker share
that directory. Each encrypted value is bound to its connection and credential key.
The Unix key directory is owner-only. Filesystem keys rely on those permissions;
this does not protect against access as the Prismedia operating-system user.

Back up the persistent data volume, including this key directory. A database-only
backup does not contain the keys. On another installation, restore both the database
and key directory, or re-enter connection credentials. There is no plaintext fallback
when a key is missing. See Microsoft's
[Data Protection configuration guidance](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/overview?view=aspnetcore-10.0).

Native plugins remain trusted executable code, not sandboxed extensions. See
[Native process limits and trust](./overview.md#native-process-limits-and-trust).
