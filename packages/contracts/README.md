# @prismedia/contracts

This TypeScript package is a frontend compatibility package for constants and
helpers that have not yet moved to generated .NET OpenAPI types.

It may still own frontend-only constants, media helpers, plugin normalizer
shapes, and compatibility DTOs used by migrated Svelte surfaces. It must not
own server contracts, database schema, queues, or worker behavior.

New .NET API request and response shapes should be added to `apps/backend/src/Prismedia.Contracts` so OpenAPI and Orval remain the public contract source for migrated surfaces.
