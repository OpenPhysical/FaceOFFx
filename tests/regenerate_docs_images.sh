#!/bin/bash

# Regenerate docs/sample crops from the canonical people corpus using the diagnostics CLI.

echo "Regenerating docs sample crops from faceoffx-diagnostics..."

process_person() {
	local person=$1
	local source_img=$2

	echo "Processing $person..."

	dotnet run --project src/FaceOFFx.Diagnostics.Cli -- crop "$source_img" --profile piv --variant standard --output "artifacts/diagnostics/docs/${person}"

	cp "artifacts/diagnostics/docs/${person}/source.piv.jp2" "docs/samples/processed/${person}_piv_standard.jp2"
	cp "artifacts/diagnostics/docs/${person}/source.piv.print.jpg" "docs/samples/processed/${person}_piv_standard.jpg"
}

process_person "bush" "tests/test-images/people/bush/source.jpg"
process_person "generic_guy" "tests/test-images/people/generic-guy/source.jpg"
process_person "johnson" "tests/test-images/people/johnson/source.jpg"
process_person "starmer" "tests/test-images/people/starmer/source.jpg"
process_person "carter" "tests/test-images/people/carter/source.jpg"

echo "Done."
