#!/bin/bash
set -euo pipefail

# Regenerate README sample assets from the canonical people corpus using the diagnostics CLI.

dotnet run --framework net8.0 --project src/FaceOFFx.Diagnostics.Cli -- \
	docs samples \
	--corpus people \
	--output docs/samples \
	--quality-subject starmer \
	--crop-subject starmer
