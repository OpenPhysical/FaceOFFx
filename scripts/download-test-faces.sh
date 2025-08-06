#!/bin/bash
# Download public domain test face images and create quality test variations

OUTPUT_DIR="./test-images/quality-validation"
mkdir -p "$OUTPUT_DIR"

echo "Downloading public domain test faces..."

# Download truly free/open source test images
# Options:
# - Generated faces (no copyright)
# - CC0/Public domain photos
# - Synthetic patterns

# Since we can't guarantee external URLs will work, let's just create 
# a synthetic face-like pattern that's good enough for testing
echo "Creating synthetic test face pattern..."
    echo "Failed to download Lena image, creating synthetic test pattern instead..."
    # Fallback: create a simple synthetic pattern
    convert -size 420x560 xc:white \
        -fill gray -draw "ellipse 210,280 80,100 0,360" \
        -fill white -draw "ellipse 180,260 15,20 0,360" \
        -fill white -draw "ellipse 240,260 15,20 0,360" \
        -fill black -draw "ellipse 180,260 5,5 0,360" \
        -fill black -draw "ellipse 240,260 5,5 0,360" \
        -fill gray20 -draw "ellipse 210,300 30,15 0,180" \
        "$OUTPUT_DIR/source_synthetic.png"
}

# Use whichever source image we have
SOURCE_IMAGE=$(ls "$OUTPUT_DIR"/source_*.png | head -1)

if [ -z "$SOURCE_IMAGE" ]; then
    echo "No source image available. Please provide a face image manually."
    exit 1
fi

echo "Using source image: $SOURCE_IMAGE"

# Resize to PIV dimensions
convert "$SOURCE_IMAGE" -resize 420x560! "$OUTPUT_DIR/01_base_piv_size.png"

# Now create quality variations
BASE_IMAGE="$OUTPUT_DIR/01_base_piv_size.png"

# 2. Add uneven illumination
echo "Creating illumination variations..."
convert "$BASE_IMAGE" \
    \( -size 420x560 gradient:white-gray40 \) \
    -compose multiply -composite \
    "$OUTPUT_DIR/02_uneven_illumination.png"

# 3. Create blur variations
echo "Creating sharpness variations..."
convert "$BASE_IMAGE" -blur 0x2 "$OUTPUT_DIR/03_slight_blur.png"
convert "$BASE_IMAGE" -blur 0x5 "$OUTPUT_DIR/04_moderate_blur.png"
convert "$BASE_IMAGE" -blur 0x10 "$OUTPUT_DIR/05_severe_blur.png"

# 4. Create rotation variations
echo "Creating pose variations..."
convert "$BASE_IMAGE" -rotate 5 -background white "$OUTPUT_DIR/06_rotate_5deg.png"
convert "$BASE_IMAGE" -rotate 10 -background white "$OUTPUT_DIR/07_rotate_10deg.png"
convert "$BASE_IMAGE" -rotate 20 -background white "$OUTPUT_DIR/08_rotate_20deg.png"

# 5. Create scaling variations
echo "Creating size variations..."
convert "$BASE_IMAGE" -resize 80% -background white -gravity center -extent 420x560 "$OUTPUT_DIR/09_small_face.png"
convert "$BASE_IMAGE" -resize 120% -gravity center -crop 420x560+0+0 "$OUTPUT_DIR/10_large_face.png"

# 6. Create offset variations
echo "Creating centering variations..."
convert "$BASE_IMAGE" -roll +50+0 "$OUTPUT_DIR/11_offset_right.png"
convert "$BASE_IMAGE" -roll -50+0 "$OUTPUT_DIR/12_offset_left.png"
convert "$BASE_IMAGE" -roll +0+50 "$OUTPUT_DIR/13_offset_down.png"

# 7. Create contrast variations
echo "Creating contrast variations..."
convert "$BASE_IMAGE" -brightness-contrast -20x-30 "$OUTPUT_DIR/14_low_contrast.png"
convert "$BASE_IMAGE" -brightness-contrast 10x40 "$OUTPUT_DIR/15_high_contrast.png"

# 8. Create noise variations
echo "Creating noise variations..."
convert "$BASE_IMAGE" -attenuate 0.1 +noise Gaussian "$OUTPUT_DIR/16_low_noise.png"
convert "$BASE_IMAGE" -attenuate 0.3 +noise Gaussian "$OUTPUT_DIR/17_high_noise.png"

# 9. Create asymmetric lighting
echo "Creating asymmetric lighting..."
convert "$BASE_IMAGE" \
    \( -size 420x560 gradient:gray80-white -rotate 90 \) \
    -compose multiply -composite \
    "$OUTPUT_DIR/18_side_lighting.png"

# 10. Create compression artifacts
echo "Creating compression artifacts..."
convert "$BASE_IMAGE" -quality 20 "$OUTPUT_DIR/19_low_quality_jpeg.jpg"
convert "$BASE_IMAGE" -quality 50 "$OUTPUT_DIR/20_medium_quality_jpeg.jpg"

# Create metadata
cat > "$OUTPUT_DIR/test_metadata.json" << 'EOF'
{
  "test_suite": "quality_validation",
  "source": "public_domain_test_image",
  "variations": [
    {"file": "01_base_piv_size.png", "category": "baseline", "expected_quality": "high"},
    {"file": "02_uneven_illumination.png", "category": "illumination", "expected_quality": "medium"},
    {"file": "03_slight_blur.png", "category": "sharpness", "expected_quality": "high"},
    {"file": "04_moderate_blur.png", "category": "sharpness", "expected_quality": "medium"},
    {"file": "05_severe_blur.png", "category": "sharpness", "expected_quality": "low"},
    {"file": "06_rotate_5deg.png", "category": "pose", "expected_quality": "high"},
    {"file": "07_rotate_10deg.png", "category": "pose", "expected_quality": "medium"},
    {"file": "08_rotate_20deg.png", "category": "pose", "expected_quality": "low"},
    {"file": "09_small_face.png", "category": "geometry", "expected_quality": "medium"},
    {"file": "10_large_face.png", "category": "geometry", "expected_quality": "medium"},
    {"file": "11_offset_right.png", "category": "centering", "expected_quality": "medium"},
    {"file": "12_offset_left.png", "category": "centering", "expected_quality": "medium"},
    {"file": "13_offset_down.png", "category": "centering", "expected_quality": "medium"},
    {"file": "14_low_contrast.png", "category": "contrast", "expected_quality": "low"},
    {"file": "15_high_contrast.png", "category": "contrast", "expected_quality": "high"},
    {"file": "16_low_noise.png", "category": "noise", "expected_quality": "high"},
    {"file": "17_high_noise.png", "category": "noise", "expected_quality": "low"},
    {"file": "18_side_lighting.png", "category": "illumination", "expected_quality": "low"},
    {"file": "19_low_quality_jpeg.jpg", "category": "compression", "expected_quality": "low"},
    {"file": "20_medium_quality_jpeg.jpg", "category": "compression", "expected_quality": "medium"}
  ]
}
EOF

echo "Test images generated in $OUTPUT_DIR"
echo "Note: You may want to replace the source image with an actual face photo for better testing"