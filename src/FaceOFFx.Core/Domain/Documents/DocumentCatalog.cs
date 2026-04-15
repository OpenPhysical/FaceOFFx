using System;
using System.Collections.Generic;
namespace FaceOFFx.Core.Domain.Documents;

/// <summary>
/// Built-in catalog of shipped, citation-backed document workflows.
/// </summary>
public static class DocumentCatalog
{
    private static readonly SpecificationCitation FipsBiometricStorage = new(
        "fips201-3-4.2.3.1",
        "FIPS 201-3 Personal Identity Verification (PIV) of Federal Employees and Contractors",
        "Section 4.2.3.1",
        "https://pages.nist.gov/FIPS201/FIPS201.html#s-4-2-3-1",
        "The PIV Card shall store an electronic facial image.");

    private static readonly SpecificationCitation FipsPrintedPhoto = new(
        "fips201-3-4.1.4.1",
        "FIPS 201-3 Personal Identity Verification (PIV) of Federal Employees and Contractors",
        "Section 4.1.4.1, Zone 1F",
        "https://pages.nist.gov/FIPS201/FIPS201.html#s-4-1-4-1",
        "The printed photograph shall be frontal from top of head to shoulder and use a minimum of 300 DPI.");

    private static readonly SpecificationCitation Sp80076Capture = new(
        "sp800-76-2-table12-rows37-49",
        "NIST SP 800-76-2 Biometric Specifications for Personal Identity Verification",
        "Table 12 rows 37-49",
        "https://nvlpubs.nist.gov/nistpubs/specialpublications/nist.sp.800-76-2.pdf",
        "PIV facial capture is frontal, neutral, uniformly lit, without shadows or hot spots, and in focus.");

    private static readonly SpecificationCitation Sp80076FullFrontal = new(
        "sp800-76-2-table12-note4",
        "NIST SP 800-76-2 Biometric Specifications for Personal Identity Verification",
        "Table 12, Normative Note 4",
        "https://nvlpubs.nist.gov/nistpubs/specialpublications/nist.sp.800-76-2.pdf",
        "PIV facial images shall conform to the Full Frontal Image Type defined in Clause 8 of INCITS 385-2004.");

    private static readonly SpecificationCitation Incits385Geometry = new(
        "incits385-2004-clause8",
        "INCITS 385-2004 Information technology - Face recognition format for data interchange",
        "Clause 8, Full Frontal Image Type",
        "https://webstore.ansi.org/standards/incits/incits3852004",
        "The full frontal image type constrains horizontal centering, eye-line position, and head-width geometry.");

    private static readonly SpecificationCitation UsCompositionTemplate = new(
        "travel-state-photo-template",
        "U.S. Department of State Photo Composition Template",
        "Paper and Digital Head Size Template",
        "https://travel.state.gov/content/travel/en/us-visas/visa-information-resources/photos/photo-composition-template.html",
        "Paper photos must be 2 x 2 inches with a 1 to 1 3/8 inch head height and 1 1/8 to 1 3/8 inch eye height from the bottom. Digital images must keep head height between 50% and 69% and eye height between 56% and 69% of image height.");

    private static readonly SpecificationCitation UsDigitalRequirements = new(
        "travel-state-digital-photo",
        "U.S. Department of State Digital Image Requirements",
        "Digital image requirements",
        "https://travel.state.gov/content/travel/en/us-visas/visa-information-resources/photos/digital-image-requirements.html",
        "Digital images must be square, between 600 x 600 and 1200 x 1200 pixels, in JPEG format, and no larger than 240 KB.");

    private static readonly SpecificationCitation UsPhotoOverview = new(
        "travel-state-photo-overview",
        "U.S. Department of State Photo Requirements",
        "Photo overview",
        "https://travel.state.gov/content/travel/en/us-visas/visa-information-resources/photos.html",
        "Passport and visa application photos must be recent, unaltered, neutral, and use a plain white or off-white background.");

    private static readonly SpecificationCitation CanadaPrPhotoSpec = new(
        "canada-pr-photos",
        "Canada.ca Permanent resident photos",
        "Photo specifications",
        "https://www.canada.ca/en/immigration-refugees-citizenship/services/permanent-residents/card/photos.html",
        "Permanent resident card photos must be 50 mm x 70 mm with a chin-to-crown head size between 31 mm and 36 mm, centered, sharp, and taken on a plain white background.");

    private static readonly SpecificationCitation CanadaPrGuide = new(
        "canada-pr-guide-5530",
        "Canada.ca Guide 5530 - Request to Reissue a Permanent Resident Card",
        "Notes to the photographer",
        "https://www.canada.ca/en/immigration-refugees-citizenship/services/application/application-forms-guides/guide-5530-request-reissue-permanent-resident-card-card.html",
        "The back of one permanent resident photo must include the subject name and date of birth, the studio name and address, and the date the photo was taken.");

    private static readonly SpecificationCitation CanadaPassportPhotoSpec = new(
        "canada-passport-photos",
        "Government of Canada passport photo requirements",
        "Photo specifications",
        "https://www.canada.ca/en/immigration-refugees-citizenship/services/canadian-passports/photos.html",
        "Passport photos must be 50 mm x 70 mm with a chin-to-crown head size between 31 mm and 36 mm, centered and facing the camera.");

    private static readonly SpecificationCitation CanadaPassportDigitalSpec = new(
        "canada-passport-online-digital",
        "Government of Canada online passport application photo requirements",
        "Digital photo specifications",
        "https://www.canada.ca/en/immigration-refugees-citizenship/services/canadian-passports/online-renew-adult-passport/photo.html",
        "Online passport digital photos must preserve the same composition as the paper photo and meet the published file-format and pixel-size requirements.");

    private static readonly AutomatedCheckDefinition PivInputCapture = new(
        "piv-input-capture",
        DocumentCheckStage.Input,
        "Capture Suitability",
        "Checks whether the source image is frontal, in focus, and suitable for deriving a conformant PIV portrait.",
        new[] { Sp80076Capture });

    private static readonly AutomatedCheckDefinition PivOutputGeometry = new(
        "piv-output-geometry",
        DocumentCheckStage.Output,
        "Full Frontal Geometry",
        "Checks the rendered portrait against the PIV full-frontal geometry requirements.",
        new[] { Sp80076FullFrontal, Incits385Geometry });

    private static readonly AutomatedCheckDefinition PivPrintedDpi = new(
        "piv-print-dpi",
        DocumentCheckStage.Output,
        "Printed Photo Resolution",
        "Checks that the printed Zone 1F artifact is tagged at 300 DPI or higher.",
        new[] { FipsPrintedPhoto });

    private static readonly AutomatedCheckDefinition UsInputCapture = new(
        "us-input-capture",
        DocumentCheckStage.Input,
        "Capture Suitability",
        "Checks whether the source photo is frontal, neutral, sharp, and suitable for a passport-style rendering.",
        new[] { UsPhotoOverview, UsCompositionTemplate });

    private static readonly AutomatedCheckDefinition UsPaperComposition = new(
        "us-paper-composition",
        DocumentCheckStage.Output,
        "Paper Photo Composition",
        "Checks 2 x 2 composition, head height, and eye-line placement for printed U.S. passport-style photos.",
        new[] { UsCompositionTemplate });

    private static readonly AutomatedCheckDefinition UsDigitalTechnical = new(
        "us-digital-technical",
        DocumentCheckStage.Output,
        "Digital Upload Technical Rules",
        "Checks square JPEG output, pixel dimensions, and maximum file size for U.S. digital submissions.",
        new[] { UsCompositionTemplate, UsDigitalRequirements });

    private static readonly AutomatedCheckDefinition CanadaInputCapture = new(
        "canada-input-capture",
        DocumentCheckStage.Input,
        "Capture Suitability",
        "Checks whether the source photo is sharp, centered, and suitable for Canada passport or PR paper photos.",
        new[] { CanadaPassportPhotoSpec, CanadaPrPhotoSpec });

    private static readonly AutomatedCheckDefinition CanadaPaperComposition = new(
        "canada-paper-composition",
        DocumentCheckStage.Output,
        "Paper Photo Composition",
        "Checks 50 x 70 mm framing and 31-36 mm chin-to-crown head height for Canadian paper submissions.",
        new[] { CanadaPassportPhotoSpec, CanadaPrPhotoSpec });

    private static readonly AutomatedCheckDefinition CanadaDigitalTechnical = new(
        "canada-digital-technical",
        DocumentCheckStage.Output,
        "Digital Upload Technical Rules",
        "Checks digital file dimensions and file type for Canada online passport submissions.",
        new[] { CanadaPassportDigitalSpec, CanadaPassportPhotoSpec });

    private static readonly ManualChecklistItemDefinition UsManualChecklist = new(
        "us-manual-review",
        "Manual U.S. Photo Checklist",
        "Confirm the photo is recent, unaltered, and uses a plain white or off-white background.",
        new[] { UsPhotoOverview });

    private static readonly ManualChecklistItemDefinition CanadaPassportManualChecklist = new(
        "canada-passport-manual-review",
        "Manual Canada Passport Checklist",
        "Confirm the back of one printed photo includes the photographer or studio information and the date taken.",
        new[] { CanadaPassportPhotoSpec });

    private static readonly ManualChecklistItemDefinition CanadaPrManualChecklist = new(
        "canada-pr-manual-review",
        "Manual Canada PR Checklist",
        "Confirm the back of one printed photo includes the subject name and date of birth, studio details, and the date taken.",
        new[] { CanadaPrGuide });

    private static readonly DeliverableDefinition PivCardDeliverable = new(
        "piv-card-image",
        "PIV Card Facial Image",
        "jp2",
        ".piv.jp2",
        "piv-card",
        "piv-card",
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["format"] = "jp2",
            ["roi"] = "enabled"
        },
        new[] { PivOutputGeometry });

    private static readonly DeliverableDefinition PivPrintedDeliverable = new(
        "piv-printed-photo",
        "PIV Printed Zone 1F Photo",
        "jpg",
        ".piv.print.jpg",
        "piv-print",
        "piv-print",
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["format"] = "jpg",
            ["dpi"] = "300"
        },
        new[] { PivOutputGeometry, PivPrintedDpi });

    private static readonly DeliverableDefinition UsPaperDeliverable = new(
        "us-paper-photo",
        "U.S. Paper Photo",
        "jpg",
        ".us-passport.jpg",
        "us-paper",
        "us-paper",
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["format"] = "jpg",
            ["dpi"] = "600",
            ["size"] = "2x2in"
        },
        new[] { UsPaperComposition });

    private static readonly DeliverableDefinition UsDigitalDeliverable = new(
        "us-digital-photo",
        "U.S. Digital Upload Photo",
        "jpg",
        ".us-passport.digital.jpg",
        "us-digital",
        "us-digital",
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["format"] = "jpg",
            ["pixels"] = "600x600",
            ["maxBytes"] = "245760"
        },
        new[] { UsPaperComposition, UsDigitalTechnical });

    private static readonly DeliverableDefinition CanadaPaperDeliverable = new(
        "canada-paper-photo",
        "Canada Paper Photo",
        "jpg",
        ".canada-paper.jpg",
        "canada-paper",
        "canada-paper",
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["format"] = "jpg",
            ["dpi"] = "600",
            ["size"] = "50x70mm"
        },
        new[] { CanadaPaperComposition });

    private static readonly DeliverableDefinition CanadaDigitalDeliverable = new(
        "canada-digital-photo",
        "Canada Digital Upload Photo",
        "jpg",
        ".canada-passport.digital.jpg",
        "canada-digital",
        "canada-digital",
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["format"] = "jpg",
            ["pixels"] = "1200x1800"
        },
        new[] { CanadaPaperComposition, CanadaDigitalTechnical });

    private static readonly IReadOnlyDictionary<string, DocumentDefinition> Documents =
        new Dictionary<string, DocumentDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["piv"] = new DocumentDefinition(
                "piv",
                "PIV",
                "Federal PIV facial image and printed Zone 1F photo.",
                "standard",
                new[] { FipsBiometricStorage, FipsPrintedPhoto, Sp80076Capture, Sp80076FullFrontal, Incits385Geometry },
                new[] { PivInputCapture },
                Array.Empty<ManualChecklistItemDefinition>(),
                new Dictionary<string, VariantDefinition>(StringComparer.OrdinalIgnoreCase)
                {
                    ["standard"] = new("standard", "Card Image + Printed Photo", new[] { PivCardDeliverable, PivPrintedDeliverable }),
                    ["card-only"] = new("card-only", "Card Image Only", new[] { PivCardDeliverable }),
                    ["print-only"] = new("print-only", "Printed Photo Only", new[] { PivPrintedDeliverable })
                }),
            ["us-passport"] = new DocumentDefinition(
                "us-passport",
                "U.S. Passport",
                "U.S. passport photo with paper default and digital upload variant.",
                "paper",
                new[] { UsPhotoOverview, UsCompositionTemplate, UsDigitalRequirements },
                new[] { UsInputCapture },
                new[] { UsManualChecklist },
                new Dictionary<string, VariantDefinition>(StringComparer.OrdinalIgnoreCase)
                {
                    ["paper"] = new("paper", "Paper Photo", new[] { UsPaperDeliverable }),
                    ["online-renewal-digital"] = new("online-renewal-digital", "Digital Upload", new[] { UsDigitalDeliverable })
                }),
            ["us-pr-photo"] = new DocumentDefinition(
                "us-pr-photo",
                "U.S. Permanent Resident Application Photo",
                "Passport-style U.S. permanent resident application photo.",
                "paper",
                new[] { UsPhotoOverview, UsCompositionTemplate, UsDigitalRequirements },
                new[] { UsInputCapture },
                new[] { UsManualChecklist },
                new Dictionary<string, VariantDefinition>(StringComparer.OrdinalIgnoreCase)
                {
                    ["paper"] = new("paper", "Paper Photo", new[] { UsPaperDeliverable with { FileSuffix = ".us-pr-photo.jpg", DisplayName = "U.S. PR Paper Photo" } }),
                    ["digital-upload"] = new("digital-upload", "Digital Upload", new[] { UsDigitalDeliverable with { FileSuffix = ".us-pr-photo.digital.jpg", DisplayName = "U.S. PR Digital Upload Photo" } })
                }),
            ["canada-passport"] = new DocumentDefinition(
                "canada-passport",
                "Canada Passport",
                "Canadian passport photo with paper default and online digital variant.",
                "paper",
                new[] { CanadaPassportPhotoSpec, CanadaPassportDigitalSpec },
                new[] { CanadaInputCapture },
                new[] { CanadaPassportManualChecklist },
                new Dictionary<string, VariantDefinition>(StringComparer.OrdinalIgnoreCase)
                {
                    ["paper"] = new("paper", "Paper Photo", new[] { CanadaPaperDeliverable with { FileSuffix = ".canada-passport.jpg", DisplayName = "Canada Passport Paper Photo" } }),
                    ["online-renewal-digital"] = new("online-renewal-digital", "Digital Upload", new[] { CanadaDigitalDeliverable })
                }),
            ["canada-pr-card"] = new DocumentDefinition(
                "canada-pr-card",
                "Canada Permanent Resident Card",
                "Canadian permanent resident card paper photo.",
                "paper",
                new[] { CanadaPrPhotoSpec, CanadaPrGuide },
                new[] { CanadaInputCapture },
                new[] { CanadaPrManualChecklist },
                new Dictionary<string, VariantDefinition>(StringComparer.OrdinalIgnoreCase)
                {
                    ["paper"] = new("paper", "Paper Photo", new[] { CanadaPaperDeliverable with { FileSuffix = ".canada-pr-card.jpg", DisplayName = "Canada PR Card Photo" } })
                })
        };

    /// <summary>
    /// Gets all shipped document workflows.
    /// </summary>
    public static IReadOnlyCollection<DocumentDefinition> GetAll() => Documents.Values.ToArray();

    /// <summary>
    /// Looks up a document workflow by identifier.
    /// </summary>
    public static bool TryGetDocument(string documentId, out DocumentDefinition document) =>
        Documents.TryGetValue(documentId, out document!);

    /// <summary>
    /// Gets a document workflow or throws when it is not shipped.
    /// </summary>
    public static DocumentDefinition GetDocumentOrThrow(string documentId)
    {
        if (TryGetDocument(documentId, out var document))
        {
            return document;
        }

        throw new InvalidOperationException(
            $"Unsupported document '{documentId}'. Supported documents: {string.Join(", ", Documents.Keys)}");
    }
}
