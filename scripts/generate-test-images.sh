#!/bin/bash
# Generate test images for quality assessment validation

# Create output directory
OUTPUT_DIR="./test-images/quality-validation"
mkdir -p "$OUTPUT_DIR"

echo "Generating test images for quality assessment validation..."

# 1. Perfect PIV-compliant image (white background, centered black circle as face)
echo "Creating perfect PIV image..."
convert -size 420x560 xc:white \
    -fill black -draw "circle 210,280 210,180" \
    -fill white -draw "circle 170,250 170,260" \
    -fill white -draw "circle 250,250 250,260" \
    -fill black -draw "circle 170,250 170,255" \
    -fill black -draw "circle 250,250 250,255" \
    -fill black -draw "arc 170,300 250,340 200,160" \
    "$OUTPUT_DIR/01_perfect_piv.png"

# 2. Uneven illumination (gradient background)
echo "Creating uneven illumination image..."
convert -size 420x560 \
    gradient:white-gray40 \
    -fill black -draw "circle 210,280 210,180" \
    -fill white -draw "circle 170,250 170,260" \
    -fill white -draw "circle 250,250 250,260" \
    "$OUTPUT_DIR/02_uneven_illumination.png"

# 3. Off-center face (shifted to the right)
echo "Creating off-center image..."
convert -size 420x560 xc:white \
    -fill black -draw "circle 310,280 310,180" \
    -fill white -draw "circle 270,250 270,260" \
    -fill white -draw "circle 350,250 350,260" \
    "$OUTPUT_DIR/03_off_center.png"

# 4. Blurry image
echo "Creating blurry image..."
convert "$OUTPUT_DIR/01_perfect_piv.png" \
    -blur 0x8 \
    "$OUTPUT_DIR/04_blurry.png"

# 5. Too small face
echo "Creating small face image..."
convert -size 420x560 xc:white \
    -fill black -draw "circle 210,280 210,230" \
    -fill white -draw "circle 190,270 190,275" \
    -fill white -draw "circle 230,270 230,275" \
    "$OUTPUT_DIR/05_small_face.png"

# 6. Too large face
echo "Creating large face image..."
convert -size 420x560 xc:white \
    -fill black -draw "circle 210,280 210,80" \
    -fill white -draw "circle 140,230 140,250" \
    -fill white -draw "circle 280,230 280,250" \
    "$OUTPUT_DIR/06_large_face.png"

# 7. Rotated face (15 degrees)
echo "Creating rotated face image..."
convert "$OUTPUT_DIR/01_perfect_piv.png" \
    -distort SRT "210,280 15" \
    -background white -flatten \
    "$OUTPUT_DIR/07_rotated_15deg.png"

# 8. High contrast edges (checkerboard pattern in face area)
echo "Creating high contrast image..."
convert -size 420x560 xc:white \
    -fill black -draw "circle 210,280 210,180" \
    \( -size 200x200 pattern:checkerboard -scale 25% \) \
    -geometry +110+180 -compose multiply -composite \
    "$OUTPUT_DIR/08_high_contrast.png"

# 9. Low contrast (gray on light gray)
echo "Creating low contrast image..."
convert -size 420x560 xc:gray90 \
    -fill gray60 -draw "circle 210,280 210,180" \
    -fill gray80 -draw "circle 170,250 170,260" \
    -fill gray80 -draw "circle 250,250 250,260" \
    "$OUTPUT_DIR/09_low_contrast.png"

# 10. Asymmetric illumination (half shadow)
echo "Creating asymmetric illumination..."
convert -size 420x560 xc:white \
    -fill black -draw "circle 210,280 210,180" \
    -fill white -draw "circle 170,250 170,260" \
    -fill white -draw "circle 250,250 250,260" \
    \( -size 210x560 gradient:black-none -rotate 180 \) \
    -geometry +0+0 -compose multiply -composite \
    "$OUTPUT_DIR/10_asymmetric_lighting.png"

# 11. Motion blur simulation
echo "Creating motion blur image..."
convert "$OUTPUT_DIR/01_perfect_piv.png" \
    -motion-blur 0x20+45 \
    "$OUTPUT_DIR/11_motion_blur.png"

# 12. Noise addition
echo "Creating noisy image..."
convert "$OUTPUT_DIR/01_perfect_piv.png" \
    -attenuate 0.25 +noise Gaussian \
    "$OUTPUT_DIR/12_noisy.png"

# Create a montage for easy viewing
echo "Creating overview montage..."
montage "$OUTPUT_DIR"/*.png \
    -tile 4x3 -geometry 105x140+5+5 \
    -background gray -bordercolor white -border 1 \
    -font Helvetica -pointsize 10 \
    -set label '%t' \
    "$OUTPUT_DIR/00_overview.png"

# Generate metadata file
cat > "$OUTPUT_DIR/test_metadata.json" << EOF
{
  "test_images": [
    {
      "file": "01_perfect_piv.png",
      "description": "Perfect PIV-compliant image",
      "expected_score": "high",
      "expected_violations": []
    },
    {
      "file": "02_uneven_illumination.png",
      "description": "Uneven illumination gradient",
      "expected_score": "medium",
      "expected_violations": ["illumination"]
    },
    {
      "file": "03_off_center.png",
      "description": "Face shifted to the right",
      "expected_score": "medium",
      "expected_violations": ["centering"]
    },
    {
      "file": "04_blurry.png",
      "description": "Gaussian blur applied",
      "expected_score": "low",
      "expected_violations": ["sharpness"]
    },
    {
      "file": "05_small_face.png",
      "description": "Face too small for frame",
      "expected_score": "low",
      "expected_violations": ["head_size"]
    },
    {
      "file": "06_large_face.png",
      "description": "Face too large for frame",
      "expected_score": "medium",
      "expected_violations": ["head_size"]
    },
    {
      "file": "07_rotated_15deg.png",
      "description": "15-degree rotation",
      "expected_score": "medium",
      "expected_violations": ["pose"]
    },
    {
      "file": "08_high_contrast.png",
      "description": "Checkerboard pattern overlay",
      "expected_score": "medium",
      "expected_violations": []
    },
    {
      "file": "09_low_contrast.png",
      "description": "Low contrast gray on gray",
      "expected_score": "low",
      "expected_violations": ["contrast"]
    },
    {
      "file": "10_asymmetric_lighting.png",
      "description": "Half face in shadow",
      "expected_score": "low",
      "expected_violations": ["symmetry", "illumination"]
    },
    {
      "file": "11_motion_blur.png",
      "description": "Directional motion blur",
      "expected_score": "low",
      "expected_violations": ["sharpness"]
    },
    {
      "file": "12_noisy.png",
      "description": "Gaussian noise added",
      "expected_score": "medium",
      "expected_violations": ["sharpness"]
    }
  ]
}
EOF

echo "Test images generated in $OUTPUT_DIR"
echo "View overview at: $OUTPUT_DIR/00_overview.png"