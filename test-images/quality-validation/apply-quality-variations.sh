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
