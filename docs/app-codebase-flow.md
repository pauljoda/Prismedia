# App Codebase Flow Map

The rendered developer guide lives in the documentation site at
`documentation-site/docs/developers/codebase-flow.md`.

The focused Entity review guide lives at
`documentation-site/docs/developers/entity-definitions-and-data-flow.md`. It maps
kind definitions, domain capabilities, EF rows, document projection, code
generation, and both the Svelte and Swift presentation paths.

It covers:

- runtime topology and dependency direction
- frontend route/component flow
- API, generated-client, and code-constant flow
- background job and scan flow
- entity/capability projection flow
- definition discovery, persistence, and cross-client Entity data flow
- current code-quality signals and release-readiness hotspots
- practical starting points for common changes

Keep this file as the repo-level pointer so contributors browsing `docs/` can
find the richer Docusaurus page without duplicating the full guide in two places.
