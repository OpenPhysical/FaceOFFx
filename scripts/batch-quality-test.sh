#!/bin/bash
# Batch quality assessment for test images

INPUT_DIR="${1:-./test-images/quality-validation}"
OUTPUT_DIR="${2:-./test-results}"
FACEOFFX_CLI="${3:-./artifacts/bin/FaceOFFx.Cli/Debug/net8.0/faceoffx}"

mkdir -p "$OUTPUT_DIR"

# Check if CLI exists
if [ ! -f "$FACEOFFX_CLI" ]; then
    echo "Error: FaceOFFx CLI not found at $FACEOFFX_CLI"
    echo "Usage: $0 [input_dir] [output_dir] [faceoffx_cli_path]"
    exit 1
fi

# Create CSV header
echo "Image,Overall,Symmetry,Illumination,Pose,Sharpness,Geometry,HeadSize,Centering,IPD,Compliant,Violations" > "$OUTPUT_DIR/quality_results.csv"

# Create HTML report header
cat > "$OUTPUT_DIR/quality_report.html" << 'EOF'
<!DOCTYPE html>
<html>
<head>
    <title>FaceOFFx Quality Assessment Report</title>
    <style>
        body { font-family: Arial, sans-serif; margin: 20px; }
        table { border-collapse: collapse; width: 100%; margin-top: 20px; }
        th, td { border: 1px solid #ddd; padding: 8px; text-align: left; }
        th { background-color: #4CAF50; color: white; }
        tr:nth-child(even) { background-color: #f2f2f2; }
        .high { color: green; font-weight: bold; }
        .medium { color: orange; }
        .low { color: red; font-weight: bold; }
        .compliant { background-color: #d4edda; }
        .non-compliant { background-color: #f8d7da; }
        img { max-width: 100px; cursor: pointer; }
        .modal { display: none; position: fixed; z-index: 1; left: 0; top: 0; width: 100%; height: 100%; background-color: rgba(0,0,0,0.9); }
        .modal-content { margin: auto; display: block; max-width: 80%; max-height: 80%; margin-top: 50px; }
        .close { position: absolute; top: 15px; right: 35px; color: #f1f1f1; font-size: 40px; font-weight: bold; cursor: pointer; }
    </style>
</head>
<body>
    <h1>FaceOFFx Quality Assessment Report</h1>
    <p>Generated: <span id="timestamp"></span></p>
    <table id="results">
        <tr>
            <th>Image</th>
            <th>Original</th>
            <th>Quality Overlay</th>
            <th>Overall</th>
            <th>Symmetry</th>
            <th>Sharpness</th>
            <th>Geometry</th>
            <th>Status</th>
            <th>Violations</th>
        </tr>
EOF

echo "Processing images in $INPUT_DIR..."

# Process each image
for image in "$INPUT_DIR"/*.png "$INPUT_DIR"/*.jpg "$INPUT_DIR"/*.jpeg; do
    if [ ! -f "$image" ]; then
        continue
    fi
    
    filename=$(basename "$image")
    echo "Processing $filename..."
    
    # Run quality assessment
    json_output="$OUTPUT_DIR/${filename%.???}_quality.json"
    visual_output="$OUTPUT_DIR/${filename%.???}_quality_visual.png"
    
    # Run quality assessment with JSON output
    "$FACEOFFX_CLI" quality -i "$image" -f json -o "$json_output" 2>&1
    
    # Generate visual overlay if JSON was created successfully
    if [ -f "$json_output" ]; then
        "$FACEOFFX_CLI" quality -i "$image" --visual -f text > /dev/null 2>&1
        # Move the generated visual overlay to our output directory
        visual_source="${image%.???}_quality.png"
        if [ -f "$visual_source" ]; then
            mv "$visual_source" "$visual_output"
        fi
    fi
    
    if [ -f "$json_output" ]; then
        # Parse JSON results
        overall=$(jq -r '.OverallScore // 0' "$json_output")
        symmetry=$(jq -r '.Scores.Symmetry.Overall // 0' "$json_output")
        illumination=$(jq -r '.Scores.Symmetry.Illumination // 0' "$json_output")
        pose=$(jq -r '.Scores.Symmetry.Pose // 0' "$json_output")
        sharpness=$(jq -r '.Scores.Sharpness.Overall // 0' "$json_output")
        geometry=$(jq -r '.Scores.Geometry.Overall // 0' "$json_output")
        headsize=$(jq -r '.Scores.Geometry.HeadSize // 0' "$json_output")
        centering=$(jq -r '.Scores.Geometry.Centering // 0' "$json_output")
        ipd=$(jq -r '.Scores.Geometry.InterPupillaryDistance // 0' "$json_output")
        compliant=$(jq -r '.IsCompliant // false' "$json_output")
        violations=$(jq -r '.Violations | length // 0' "$json_output")
        violation_details=$(jq -r '.Violations | map(.Category + ": " + .Description) | join("; ")' "$json_output")
        
        # Add to CSV
        echo "$filename,$overall,$symmetry,$illumination,$pose,$sharpness,$geometry,$headsize,$centering,$ipd,$compliant,$violations" >> "$OUTPUT_DIR/quality_results.csv"
        
        # Add to HTML
        status_class=$([ "$compliant" = "true" ] && echo "compliant" || echo "non-compliant")
        visual_path="${filename%.???}_quality_visual.png"
        cat >> "$OUTPUT_DIR/quality_report.html" << EOF
        <tr class="$status_class">
            <td>$filename</td>
            <td><img src="$(realpath --relative-to="$OUTPUT_DIR" "$image")" onclick="showModal('$(realpath --relative-to="$OUTPUT_DIR" "$image")')" /></td>
            <td>$([ -f "$OUTPUT_DIR/$visual_path" ] && echo "<img src=\"$visual_path\" onclick=\"showModal('$visual_path')\" />" || echo "N/A")</td>
            <td>$(printf "%.2f" $overall)</td>
            <td>$(printf "%.2f" $symmetry)</td>
            <td>$(printf "%.2f" $sharpness)</td>
            <td>$(printf "%.2f" $geometry)</td>
            <td>$([ "$compliant" = "true" ] && echo "✅ Compliant" || echo "❌ Non-compliant")</td>
            <td>$violation_details</td>
        </tr>
EOF
    else
        echo "Failed to process $filename"
        echo "$filename,ERROR,,,,,,,,,," >> "$OUTPUT_DIR/quality_results.csv"
    fi
done

# Complete HTML report
cat >> "$OUTPUT_DIR/quality_report.html" << 'EOF'
    </table>
    
    <div id="myModal" class="modal">
        <span class="close" onclick="closeModal()">&times;</span>
        <img class="modal-content" id="modalImg">
    </div>
    
    <script>
        document.getElementById('timestamp').textContent = new Date().toLocaleString();
        
        function showModal(src) {
            var modal = document.getElementById("myModal");
            var modalImg = document.getElementById("modalImg");
            modal.style.display = "block";
            // Handle relative paths correctly
            if (!src.startsWith('../') && !src.startsWith('./') && !src.startsWith('/')) {
                src = './' + src;
            }
            modalImg.src = src;
        }
        
        function closeModal() {
            document.getElementById("myModal").style.display = "none";
        }
        
        function get_quality_class(value) {
            if (value >= 0.8) return 'high';
            if (value >= 0.6) return 'medium';
            return 'low';
        }
    </script>
</body>
</html>
EOF

# Helper function for quality class (bash doesn't have this in the HTML context)
get_quality_class() {
    if (( $(echo "$1 >= 0.8" | bc -l) )); then
        echo "high"
    elif (( $(echo "$1 >= 0.6" | bc -l) )); then
        echo "medium"
    else
        echo "low"
    fi
}

echo "Quality assessment complete!"
echo "Results saved to:"
echo "  - CSV: $OUTPUT_DIR/quality_results.csv"
echo "  - HTML: $OUTPUT_DIR/quality_report.html"
echo "  - JSON: $OUTPUT_DIR/*_quality.json"

# Create summary statistics
echo -e "\n=== Summary Statistics ==="
awk -F',' 'NR>1 && $2!="ERROR" {
    count++
    overall+=$2
    if($11=="true") compliant++
} END {
    if(count>0) {
        printf "Total images: %d\n", count
        printf "Average overall score: %.2f\n", overall/count
        printf "Compliant images: %d (%.1f%%)\n", compliant, (compliant/count)*100
    }
}' "$OUTPUT_DIR/quality_results.csv"