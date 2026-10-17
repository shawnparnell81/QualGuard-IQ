#!/usr/bin/env bash
# Regenerates docs/api/openapi.yaml from the code (ASP.NET Core built-in OpenAPI). CI runs the same test without UPDATE_OPENAPI and fails if the file is stale.
set -euo pipefail
cd "$(dirname "$0")/.."
UPDATE_OPENAPI=1 dotnet test tests/QualGuardIQ.Tests --filter "FullyQualifiedName~OpenApiTests.Committed_openapi_yaml_is_current"
