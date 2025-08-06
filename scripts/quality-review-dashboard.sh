#!/bin/bash
# Interactive Quality Review Dashboard for FaceOFFx
# Provides human-in-the-loop validation of quality assessments

# Default paths
INPUT_DIR="${1:-./test-images/quality-validation}"
RESULTS_DIR="${2:-./test-results}"
FACEOFFX_CLI="${3:-./artifacts/bin/FaceOFFx.Cli/Debug/net8.0/faceoffx}"
REVIEW_FILE="$RESULTS_DIR/human_review.json"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Initialize review file if it doesn't exist
if [ ! -f "$REVIEW_FILE" ]; then
    echo '{"reviews": []}' > "$REVIEW_FILE"
fi

echo -e "${BLUE}═══════════════════════════════════════════════════════════════${NC}"
echo -e "${BLUE}       FaceOFFx Quality Assessment Review Dashboard${NC}"
echo -e "${BLUE}═══════════════════════════════════════════════════════════════${NC}"
echo

# Function to display image info
display_image_info() {
    local image_file="$1"
    local json_file="$2"
    
    if [ -f "$json_file" ]; then
        echo -e "${YELLOW}Image:${NC} $image_file"
        
        # Parse JSON data
        overall=$(jq -r '.OverallScore // 0' "$json_file")
        compliant=$(jq -r '.IsCompliant // false' "$json_file")
        symmetry=$(jq -r '.Scores.Symmetry.Overall // 0' "$json_file")
        sharpness=$(jq -r '.Scores.Sharpness.Overall // 0' "$json_file")
        geometry=$(jq -r '.Scores.Geometry.Overall // 0' "$json_file")
        violations=$(jq -r '.Violations | length // 0' "$json_file")
        
        # Color code compliance status
        if [ "$compliant" = "true" ]; then
            status="${GREEN}COMPLIANT${NC}"
        else
            status="${RED}NON-COMPLIANT${NC}"
        fi
        
        echo -e "Status: $status"
        echo -e "Overall Score: ${YELLOW}$(printf "%.1f%%" "$(echo "$overall * 100" | bc)")${NC}"
        echo
        echo "Component Scores:"
        echo -e "  • Symmetry:  $(printf "%5.1f%%" "$(echo "$symmetry * 100" | bc)")"
        echo -e "  • Sharpness: $(printf "%5.1f%%" "$(echo "$sharpness * 100" | bc)")"
        echo -e "  • Geometry:  $(printf "%5.1f%%" "$(echo "$geometry * 100" | bc)")"
        echo
        
        if [ "$violations" -gt 0 ]; then
            echo -e "${RED}Violations:${NC}"
            jq -r '.Violations[] | "  • \(.Category): \(.Description)"' "$json_file"
            echo
        fi
    else
        echo -e "${RED}No quality assessment found for this image${NC}"
    fi
}

# Function to record human review
record_review() {
    local image_file="$1"
    local reviewer_name="$2"
    local agreement="$3"
    local override="$4"
    local notes="$5"
    
    # Create review entry
    local timestamp=$(date -u +"%Y-%m-%dT%H:%M:%SZ")
    local review_entry=$(jq -n \
        --arg img "$image_file" \
        --arg reviewer "$reviewer_name" \
        --arg agree "$agreement" \
        --arg override "$override" \
        --arg notes "$notes" \
        --arg ts "$timestamp" \
        '{
            image: $img,
            reviewer: $reviewer,
            timestamp: $ts,
            agrees_with_assessment: ($agree == "y"),
            manual_override: $override,
            notes: $notes
        }')
    
    # Append to review file
    jq ".reviews += [$review_entry]" "$REVIEW_FILE" > "$REVIEW_FILE.tmp" && mv "$REVIEW_FILE.tmp" "$REVIEW_FILE"
}

# Main review loop
main_menu() {
    while true; do
        echo -e "${BLUE}Main Menu:${NC}"
        echo "1. Review individual images"
        echo "2. Batch review mode"
        echo "3. View review statistics"
        echo "4. Export review report"
        echo "5. Open HTML report in browser"
        echo "6. Exit"
        echo
        read -p "Select option (1-6): " choice
        
        case $choice in
            1) individual_review ;;
            2) batch_review ;;
            3) view_statistics ;;
            4) export_report ;;
            5) open_html_report ;;
            6) echo "Exiting..."; exit 0 ;;
            *) echo -e "${RED}Invalid option${NC}" ;;
        esac
        echo
    done
}

# Individual image review
individual_review() {
    echo -e "\n${BLUE}Individual Image Review${NC}"
    echo "Enter image filename (or 'back' to return):"
    read -p "> " image_name
    
    if [ "$image_name" = "back" ]; then
        return
    fi
    
    local image_path="$INPUT_DIR/$image_name"
    local json_path="$RESULTS_DIR/${image_name%.*}_quality.json"
    local visual_path="$RESULTS_DIR/${image_name%.*}_quality_visual.png"
    
    if [ ! -f "$image_path" ]; then
        echo -e "${RED}Image not found: $image_path${NC}"
        return
    fi
    
    # Display image info
    display_image_info "$image_name" "$json_path"
    
    # Open images if possible
    if command -v open &> /dev/null; then
        echo "Opening images..."
        [ -f "$image_path" ] && open "$image_path"
        [ -f "$visual_path" ] && open "$visual_path"
    elif command -v xdg-open &> /dev/null; then
        echo "Opening images..."
        [ -f "$image_path" ] && xdg-open "$image_path"
        [ -f "$visual_path" ] && xdg-open "$visual_path"
    fi
    
    # Get reviewer input
    read -p "Your name: " reviewer_name
    read -p "Do you agree with the assessment? (y/n): " agreement
    
    override="none"
    if [ "$agreement" = "n" ]; then
        echo "Manual override options:"
        echo "1. Mark as compliant (override non-compliant)"
        echo "2. Mark as non-compliant (override compliant)"
        echo "3. No override (just disagree)"
        read -p "Select (1-3): " override_choice
        
        case $override_choice in
            1) override="compliant" ;;
            2) override="non-compliant" ;;
            3) override="none" ;;
        esac
    fi
    
    read -p "Additional notes (optional): " notes
    
    # Record the review
    record_review "$image_name" "$reviewer_name" "$agreement" "$override" "$notes"
    echo -e "${GREEN}Review recorded!${NC}"
}

# Batch review mode
batch_review() {
    echo -e "\n${BLUE}Batch Review Mode${NC}"
    read -p "Your name: " reviewer_name
    
    # Get list of JSON files
    for json_file in "$RESULTS_DIR"/*_quality.json; do
        [ -f "$json_file" ] || continue
        
        # Extract image name
        basename=$(basename "$json_file" _quality.json)
        image_file=$(find "$INPUT_DIR" -name "$basename.*" | head -1)
        
        if [ -z "$image_file" ]; then
            continue
        fi
        
        clear
        echo -e "${BLUE}═══════════════════════════════════════════════════════════════${NC}"
        display_image_info "$(basename "$image_file")" "$json_file"
        
        # Quick review options
        echo "Quick review:"
        echo "  [a] Agree with assessment"
        echo "  [d] Disagree (needs manual review)"
        echo "  [s] Skip"
        echo "  [q] Quit batch review"
        
        read -p "Choice: " quick_choice
        
        case $quick_choice in
            a) record_review "$(basename "$image_file")" "$reviewer_name" "y" "none" "Batch reviewed" ;;
            d) record_review "$(basename "$image_file")" "$reviewer_name" "n" "none" "Needs manual review" ;;
            s) continue ;;
            q) break ;;
        esac
    done
    
    echo -e "${GREEN}Batch review complete!${NC}"
}

# View review statistics
view_statistics() {
    echo -e "\n${BLUE}Review Statistics${NC}"
    
    if [ ! -f "$REVIEW_FILE" ]; then
        echo "No reviews recorded yet."
        return
    fi
    
    local total_reviews=$(jq '.reviews | length' "$REVIEW_FILE")
    local agreements=$(jq '[.reviews[] | select(.agrees_with_assessment == true)] | length' "$REVIEW_FILE")
    local disagreements=$(jq '[.reviews[] | select(.agrees_with_assessment == false)] | length' "$REVIEW_FILE")
    local overrides=$(jq '[.reviews[] | select(.manual_override != "none")] | length' "$REVIEW_FILE")
    
    echo "Total reviews: $total_reviews"
    echo "Agreements: $agreements ($(echo "scale=1; $agreements * 100 / $total_reviews" | bc)%)"
    echo "Disagreements: $disagreements ($(echo "scale=1; $disagreements * 100 / $total_reviews" | bc)%)"
    echo "Manual overrides: $overrides"
    echo
    
    # Show reviewers
    echo "Reviewers:"
    jq -r '.reviews[].reviewer' "$REVIEW_FILE" | sort | uniq -c | sort -nr
    echo
    
    # Show images needing attention
    echo -e "${YELLOW}Images flagged for manual review:${NC}"
    jq -r '.reviews[] | select(.agrees_with_assessment == false) | .image' "$REVIEW_FILE" | sort | uniq
}

# Export review report
export_report() {
    local report_file="$RESULTS_DIR/quality_review_report_$(date +%Y%m%d_%H%M%S).md"
    
    echo "# FaceOFFx Quality Assessment Review Report" > "$report_file"
    echo "Generated: $(date)" >> "$report_file"
    echo >> "$report_file"
    
    # Add statistics
    echo "## Summary Statistics" >> "$report_file"
    local total_reviews=$(jq '.reviews | length' "$REVIEW_FILE")
    local agreements=$(jq '[.reviews[] | select(.agrees_with_assessment == true)] | length' "$REVIEW_FILE")
    local disagreements=$(jq '[.reviews[] | select(.agrees_with_assessment == false)] | length' "$REVIEW_FILE")
    
    echo "- Total reviews: $total_reviews" >> "$report_file"
    echo "- Agreement rate: $(echo "scale=1; $agreements * 100 / $total_reviews" | bc)%" >> "$report_file"
    echo "- Images flagged: $disagreements" >> "$report_file"
    echo >> "$report_file"
    
    # Add detailed reviews
    echo "## Detailed Reviews" >> "$report_file"
    echo >> "$report_file"
    
    jq -r '.reviews[] | "### \(.image)\n- Reviewer: \(.reviewer)\n- Timestamp: \(.timestamp)\n- Agrees: \(.agrees_with_assessment)\n- Override: \(.manual_override)\n- Notes: \(.notes)\n"' "$REVIEW_FILE" >> "$report_file"
    
    echo -e "${GREEN}Report exported to: $report_file${NC}"
}

# Open HTML report
open_html_report() {
    local html_report="$RESULTS_DIR/quality_report.html"
    
    if [ ! -f "$html_report" ]; then
        echo -e "${RED}HTML report not found. Run batch quality test first.${NC}"
        return
    fi
    
    if command -v open &> /dev/null; then
        open "$html_report"
    elif command -v xdg-open &> /dev/null; then
        xdg-open "$html_report"
    else
        echo "HTML report location: $html_report"
    fi
}

# Check dependencies
check_dependencies() {
    local missing=0
    
    for cmd in jq bc; do
        if ! command -v $cmd &> /dev/null; then
            echo -e "${RED}Missing dependency: $cmd${NC}"
            missing=$((missing + 1))
        fi
    done
    
    if [ $missing -gt 0 ]; then
        echo "Please install missing dependencies before running this script."
        exit 1
    fi
}

# Main execution
check_dependencies
main_menu