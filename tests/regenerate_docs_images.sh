#!/bin/bash

# Regenerate all JP2 images in docs/samples/processed with ROI level 3, then convert to PNG

echo "Regenerating all JP2 images with ROI level 3 and converting to PNG..."

# Function to process one person with all presets
process_person() {
	local person=$1
	local source_img=$2

	echo "Processing $person..."

	# Process each preset - generate JP2 then convert to PNG
	echo "  - Generating ${person}_minimum.jp2 with preset minimal"
	dotnet run --project src/FaceOFFx.Cli -- process "$source_img" --output "docs/samples/processed/${person}_minimum.jp2" --preset minimal --quiet
	magick "docs/samples/processed/${person}_minimum.jp2" "docs/samples/processed/${person}_minimum.png"

	echo "  - Generating ${person}_piv_min.jp2 with preset piv-min"
	dotnet run --project src/FaceOFFx.Cli -- process "$source_img" --output "docs/samples/processed/${person}_piv_min.jp2" --preset piv-min --quiet
	magick "docs/samples/processed/${person}_piv_min.jp2" "docs/samples/processed/${person}_piv_min.png"

	echo "  - Generating ${person}_piv_balanced.jp2 with preset piv-balanced"
	dotnet run --project src/FaceOFFx.Cli -- process "$source_img" --output "docs/samples/processed/${person}_piv_balanced.jp2" --preset piv-balanced --quiet
	magick "docs/samples/processed/${person}_piv_balanced.jp2" "docs/samples/processed/${person}_piv_balanced.png"

	echo "  - Generating ${person}_piv_high.jp2 with preset piv-high"
	dotnet run --project src/FaceOFFx.Cli -- process "$source_img" --output "docs/samples/processed/${person}_piv_high.jp2" --preset piv-high --quiet
	magick "docs/samples/processed/${person}_piv_high.jp2" "docs/samples/processed/${person}_piv_high.png"
}

# Process each person
process_person "bush" "tests/sample_images/bush_photo.jpg"
process_person "generic_guy" "tests/sample_images/generic_guy.jpg"
process_person "johnson" "tests/sample_images/johnson_photo.jpg"
process_person "starmer" "tests/sample_images/starmer_photo.jpg"
process_person "carter" "tests/sample_images/carter_photo.jpg"

# Special cases for Starmer
echo "Processing special presets..."

echo "  - Generating starmer_archival.jp2"
dotnet run --project src/FaceOFFx.Cli -- process tests/sample_images/starmer_photo.jpg --output docs/samples/processed/starmer_archival.jp2 --preset archival --quiet
magick docs/samples/processed/starmer_archival.jp2 docs/samples/processed/starmer_archival.png

echo "  - Generating starmer_piv_veryhigh.jp2"
dotnet run --project src/FaceOFFx.Cli -- process tests/sample_images/starmer_photo.jpg --output docs/samples/processed/starmer_piv_veryhigh.jp2 --preset piv-veryhigh --quiet
magick docs/samples/processed/starmer_piv_veryhigh.jp2 docs/samples/processed/starmer_piv_veryhigh.png

echo "Done! All JP2 images regenerated with ROI level 3 and converted to PNG."
