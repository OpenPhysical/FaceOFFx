#!/bin/bash
# Setup test images for quality assessment validation
# Uses synthetic faces or guides users to obtain them legally

OUTPUT_DIR="./test-images/quality-validation"
mkdir -p "$OUTPUT_DIR"

cat << 'EOF'
=================================================================
FaceOFFx Quality Assessment Test Image Setup
=================================================================

This script helps you set up test images for quality assessment.
We use only synthetic or properly licensed images.

OPTIONS FOR TEST IMAGES:

1. SYNTHETIC FACES (Recommended)
   - Generated.photos free dataset (100k synthetic faces)
   - This Person Does Not Exist images
   - No privacy concerns, GDPR compliant

2. OPEN IMAGES V7
   - Apache 2.0 licensed dataset
   - Must verify individual image licenses

3. YOUR OWN TEST IMAGES
   - Use your own photos with proper consent
   - Or use public domain images

=================================================================
EOF

echo -e "\nPlease choose an option:"
echo "1) Download instructions for synthetic faces"
echo "2) Use Open Images V7 (requires Python + TensorFlow)"
echo "3) Use your own images"
echo "4) Create simple geometric test patterns (no faces)"
read -p "Enter choice (1-4): " choice

case $choice in
    1)
        cat << 'EOF' > "$OUTPUT_DIR/DOWNLOAD_INSTRUCTIONS.txt"
SYNTHETIC FACE DOWNLOAD INSTRUCTIONS
====================================

Option A: Generated.photos Free Dataset
---------------------------------------
1. Visit: https://generated.photos/datasets
2. Download the free academic dataset (100,000 images)
3. Extract to: $OUTPUT_DIR/generated_photos/
4. Run: ./scripts/apply-quality-variations.sh

Option B: This Person Does Not Exist
------------------------------------
1. Visit: https://thispersondoesnotexist.com
2. Download several images (refresh for new faces)
3. Save to: $OUTPUT_DIR/synthetic_faces/
4. Images are 1024x1024, perfect for testing

Option C: Use Python Script (StyleGAN)
-------------------------------------
# Install: pip install requests pillow
# Run the included download_synthetic_faces.py script

IMPORTANT: These synthetic faces have no privacy concerns
and are safe for commercial use in testing.
EOF
        echo "Instructions saved to: $OUTPUT_DIR/DOWNLOAD_INSTRUCTIONS.txt"
        
        # Create Python helper script
        cat << 'EOF' > "$OUTPUT_DIR/download_synthetic_faces.py"
#!/usr/bin/env python3
"""
Download synthetic faces for testing
No real people - completely artificial faces
"""
import os
import time
import requests
from PIL import Image
from io import BytesIO

def download_thispersondoesnotexist(count=10, output_dir="synthetic_faces"):
    """Download synthetic faces from This Person Does Not Exist"""
    os.makedirs(output_dir, exist_ok=True)
    
    print(f"Downloading {count} synthetic faces...")
    for i in range(count):
        try:
            # Download image
            response = requests.get("https://thispersondoesnotexist.com/image", 
                                  headers={'User-Agent': 'Mozilla/5.0'})
            if response.status_code == 200:
                # Save image
                img = Image.open(BytesIO(response.content))
                img.save(f"{output_dir}/synthetic_face_{i:03d}.jpg", "JPEG")
                print(f"Downloaded face {i+1}/{count}")
                time.sleep(1)  # Be respectful
            else:
                print(f"Failed to download face {i+1}")
        except Exception as e:
            print(f"Error downloading face {i+1}: {e}")
    
    print(f"\nSynthetic faces saved to: {output_dir}/")
    print("These are AI-generated faces - no real people!")

if __name__ == "__main__":
    download_thispersondoesnotexist(10, "synthetic_faces")
EOF
        chmod +x "$OUTPUT_DIR/download_synthetic_faces.py"
        echo "Python download script created: $OUTPUT_DIR/download_synthetic_faces.py"
        ;;
        
    2)
        cat << 'EOF' > "$OUTPUT_DIR/download_openimages.py"
#!/usr/bin/env python3
"""
Download face images from Open Images V7
Apache 2.0 licensed dataset
"""
import tensorflow_datasets as tfds
import os

# Note: This requires tensorflow-datasets
# pip install tensorflow-datasets

def download_open_images_faces(num_images=10):
    """Download face images from Open Images V7"""
    
    print("Loading Open Images V7 dataset...")
    print("Note: Individual images may have different licenses (CC BY 2.0)")
    print("Verify licenses before commercial use!")
    
    # Load dataset
    dataset = tfds.load('open_images_v7/subset', 
                       split=f'train[:{num_images}]',
                       download=True)
    
    # Process would go here...
    print(f"Dataset loaded. Process {num_images} images with face detection.")
    print("Implementation requires additional face detection step.")

if __name__ == "__main__":
    print("This script requires TensorFlow Datasets")
    print("Install with: pip install tensorflow-datasets")
    download_open_images_faces()
EOF
        chmod +x "$OUTPUT_DIR/download_openimages.py"
        echo "Open Images script created: $OUTPUT_DIR/download_openimages.py"
        echo "Note: Requires tensorflow-datasets installation"
        ;;
        
    3)
        echo "Creating directory for your images: $OUTPUT_DIR/user_images/"
        mkdir -p "$OUTPUT_DIR/user_images"
        cat << 'EOF' > "$OUTPUT_DIR/user_images/README.txt"
USING YOUR OWN IMAGES
====================

Place your test face images in this directory.

Requirements:
- Ensure you have proper rights/consent for all images
- Recommended: PIV-compliant dimensions (420x560)
- Format: JPEG or PNG
- Naming: descriptive names like "good_lighting.jpg"

The quality variations script will create test cases
from your base images.
EOF
        echo "Please place your test images in: $OUTPUT_DIR/user_images/"
        ;;
        
    4)
        echo "Creating geometric test patterns..."
        # Create non-face test patterns for basic quality testing
        
        # Perfect centered circle
        convert -size 420x560 xc:white \
            -fill gray -draw "circle 210,280 210,180" \
            "$OUTPUT_DIR/geometric_centered.png"
            
        # Off-center circle
        convert -size 420x560 xc:white \
            -fill gray -draw "circle 310,280 310,180" \
            "$OUTPUT_DIR/geometric_offcenter.png"
            
        # Blurred circle
        convert -size 420x560 xc:white \
            -fill gray -draw "circle 210,280 210,180" \
            -blur 0x8 \
            "$OUTPUT_DIR/geometric_blurred.png"
            
        # Small circle
        convert -size 420x560 xc:white \
            -fill gray -draw "circle 210,280 210,230" \
            "$OUTPUT_DIR/geometric_small.png"
            
        echo "Geometric test patterns created in: $OUTPUT_DIR/"
        echo "Note: These are NOT faces - just for basic quality metric testing"
        ;;
esac

# Create quality variation application script
cat << 'EOF' > "$OUTPUT_DIR/apply-quality-variations.sh"
#!/bin/bash
# Apply quality variations to base images

if [ $# -ne 2 ]; then
    echo "Usage: $0 <input_dir> <output_dir>"
    exit 1
fi

INPUT_DIR="$1"
OUTPUT_DIR="$2"
mkdir -p "$OUTPUT_DIR"

echo "Applying quality variations to images in $INPUT_DIR..."

for img in "$INPUT_DIR"/*.{jpg,jpeg,png} 2>/dev/null; do
    if [ ! -f "$img" ]; then
        continue
    fi
    
    base=$(basename "$img" | sed 's/\.[^.]*$//')
    ext="${img##*.}"
    
    echo "Processing: $base.$ext"
    
    # Original (resized to PIV)
    convert "$img" -resize 420x560! "$OUTPUT_DIR/${base}_01_original.$ext"
    
    # Blur variations
    convert "$img" -resize 420x560! -blur 0x2 "$OUTPUT_DIR/${base}_02_slight_blur.$ext"
    convert "$img" -resize 420x560! -blur 0x5 "$OUTPUT_DIR/${base}_03_moderate_blur.$ext"
    convert "$img" -resize 420x560! -blur 0x10 "$OUTPUT_DIR/${base}_04_severe_blur.$ext"
    
    # Rotation variations
    convert "$img" -resize 420x560! -rotate 5 -background white "$OUTPUT_DIR/${base}_05_rotate_5deg.$ext"
    convert "$img" -resize 420x560! -rotate 15 -background white "$OUTPUT_DIR/${base}_06_rotate_15deg.$ext"
    
    # Size variations
    convert "$img" -resize 420x560! -resize 80% -background white -gravity center -extent 420x560 "$OUTPUT_DIR/${base}_07_small.$ext"
    convert "$img" -resize 420x560! -resize 120% -gravity center -crop 420x560+0+0 "$OUTPUT_DIR/${base}_08_large.$ext"
    
    # Position variations
    convert "$img" -resize 420x560! -roll +50+0 "$OUTPUT_DIR/${base}_09_offset_right.$ext"
    convert "$img" -resize 420x560! -roll +0+50 "$OUTPUT_DIR/${base}_10_offset_down.$ext"
    
    # Lighting variations
    convert "$img" -resize 420x560! \
        \( +clone -fill black -colorize 50% \) \
        \( +clone -fill white -colorize 100% \) \
        -delete 0 -morph 1 -delete 1 \
        "$OUTPUT_DIR/${base}_11_low_contrast.$ext"
    
    # Asymmetric lighting
    convert "$img" -resize 420x560! \
        \( -size 420x560 gradient:gray80-white -rotate 90 \) \
        -compose multiply -composite \
        "$OUTPUT_DIR/${base}_12_side_lighting.$ext"
done

echo "Quality variations created in: $OUTPUT_DIR"
EOF

chmod +x "$OUTPUT_DIR/apply-quality-variations.sh"

echo -e "\n==================================================================="
echo "Setup complete!"
echo "Output directory: $OUTPUT_DIR"
echo -e "\nNext steps:"
echo "1. Obtain test images using one of the methods above"
echo "2. Run: $OUTPUT_DIR/apply-quality-variations.sh <input_dir> <output_dir>"
echo "3. Use the batch quality test script to assess all variations"
echo "==================================================================="