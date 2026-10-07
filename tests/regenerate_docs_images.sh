#!/bin/bash
set -euo pipefail
python3 -c 'from PIL import features; assert features.check("jpg_2000"), "Pillow with OpenJPEG is required"; import numpy'

# Regenerate through the public PIV encoder, then verify and preview its actual JP2 files.

dotnet run --configuration Release --framework net8.0 --project scripts/ReadmeGallery -- docs/samples
python3 scripts/ReadmeGallery/verify_gallery.py docs/samples
