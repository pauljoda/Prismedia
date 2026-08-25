# shellcheck shell=bash

set -euo pipefail

if [[ ! -f package.json || ! -f apps/backend/Prismedia.slnx ]]; then
  echo "Run prismedia-setup from the Prismedia repository root." >&2
  exit 64
fi

echo "Installing locked pnpm dependencies..."
pnpm install --frozen-lockfile

echo "Restoring .NET tools and NuGet dependencies..."
(
  cd apps/backend
  dotnet tool restore
  dotnet restore Prismedia.slnx
)

echo
echo "Prismedia dependencies are installed. Run prismedia-doctor next."
