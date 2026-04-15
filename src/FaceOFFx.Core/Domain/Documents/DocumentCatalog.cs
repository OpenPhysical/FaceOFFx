using System;
using System.Collections.Generic;
using FaceOFFx.Core.Domain.Common;

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
        "The PIV card shall store an electronic facial image.");

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

    private static readonly SpecificationCitation UsPhotoOverview = new(
        "travel-state-photo-overview",
        "U.S. Department of State Photo Requirements",
        "Photo overview",
        "https://travel.state.gov/content/travel/en/us-visas/visa-information-resources/photos.html",
        "Passport and permanent resident application photos must be recent, unaltered, neutral, and use a plain white or off-white background.");

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

    private static readonly SpecificationCitation CanadaPassportPhotoSpec = new(
        "canada-passport-photos",
        "Government of Canada passport photos (printed copies)",
        "Photo specifications",
        "https://www.canada.ca/en/immigration-refugees-citizenship/services/canadian-passports/photos.html",
        "Passport photos must be 50 mm x 70 mm with a chin-to-crown head size between 31 mm and 36 mm, centered and facing the camera.");

    private static readonly SpecificationCitation CanadaPrPhotoSpec = new(
        "canada-pr-photos",
        "Canada.ca Permanent resident photos",
        "Photo specifications",
        "https://www.canada.ca/en/immigration-refugees-citizenship/services/permanent-residents/card/photos.html",
        "Permanent resident card photos must be 50 mm x 70 mm with a chin-to-crown head size between 31 mm and 36 mm, centered, sharp, and taken on a plain white background.");

    private static readonly SpecificationCitation CanadaPrPhotographerSheet = new(
        "canada-pr-5445eb-e",
        "Guide 5445EB - Photograph Specifications",
        "Digital photo specifications",
        "https://www.canada.ca/content/dam/ircc/migration/ircc/english/information/applications/guides/pdf/5445eb-e.pdf",
        "Digital permanent resident photos may be JPEG or PNG, between 715 x 1000 and 2000 x 2800 pixels, and 4 MB or less.");

    private static readonly SpecificationCitation CanadaCitizenshipPhotoSpec = new(
        "canada-citizenship-photo-spec",
        "Citizenship application photograph specifications",
        "Photo specifications",
        "https://www.canada.ca/en/immigration-refugees-citizenship/services/application/application-forms-guides/citizenship-application-photograph-specifications.html",
        "Citizenship paper photos must be 50 mm x 70 mm with a chin-to-crown head size between 31 mm and 36 mm.");

    private static readonly SpecificationCitation CanadaCitizenshipDigitalSpec = new(
        "canada-citizenship-digital-spec",
        "Citizenship application photograph specifications",
        "Online digital photo specifications",
        "https://www.canada.ca/en/immigration-refugees-citizenship/services/application/application-forms-guides/citizenship-application-photograph-specifications.html",
        "Online citizenship digital photos must be JPG or JPEG, at least 420 x 540 pixels, approximately 240 kB but no more than 4 MB, and ideally saved directly from the camera without changes to the original file.");

    private static readonly AutomatedCheckDefinition PivInputCapture = new(
        "piv-input-capture",
        DocumentCheckStage.Input,
        DocumentCheckDisposition.Blocking,
        "Capture Suitability",
        "Checks whether the source image is frontal, in focus, and suitable for deriving a conformant PIV portrait.",
        new[] { Sp80076Capture });

    private static readonly AutomatedCheckDefinition PivOutputGeometry = new(
        "piv-output-geometry",
        DocumentCheckStage.Output,
        DocumentCheckDisposition.Blocking,
        "Full Frontal Geometry",
        "Checks the rendered portrait against the PIV full-frontal geometry requirements.",
        new[] { Sp80076FullFrontal, Incits385Geometry });

    private static readonly AutomatedCheckDefinition PivPrintedDpi = new(
        "piv-print-dpi",
        DocumentCheckStage.Output,
        DocumentCheckDisposition.Blocking,
        "Printed Photo Resolution",
        "Checks that the printed Zone 1F artifact is tagged at 300 DPI or higher.",
        new[] { FipsPrintedPhoto });

    private static readonly AutomatedCheckDefinition UsInputCapture = new(
        "us-input-capture",
        DocumentCheckStage.Input,
        DocumentCheckDisposition.Blocking,
        "Capture Suitability",
        "Checks whether the source photo has one usable face with a level eye line for a passport-style rendering.",
        new[] { UsPhotoOverview, UsCompositionTemplate });

    private static readonly AutomatedCheckDefinition UsPrintComposition = new(
        "us-print-composition",
        DocumentCheckStage.Output,
        DocumentCheckDisposition.Blocking,
        "Print Photo Composition",
        "Checks 2 x 2 composition, head height, and eye-line placement for printed U.S. photos.",
        new[] { UsCompositionTemplate });

    private static readonly AutomatedCheckDefinition UsDigitalTechnical = new(
        "us-digital-technical",
        DocumentCheckStage.Output,
        DocumentCheckDisposition.Blocking,
        "Digital Upload Technical Rules",
        "Checks square JPEG output, pixel dimensions, and maximum file size for U.S. digital submissions.",
        new[] { UsDigitalRequirements });

    private static readonly AutomatedCheckDefinition CanadaInputCapture = new(
        "canada-input-capture",
        DocumentCheckStage.Input,
        DocumentCheckDisposition.Blocking,
        "Capture Suitability",
        "Checks whether the source photo has one usable face with a level eye line for Canadian portrait rendering.",
        new[] { CanadaPassportPhotoSpec, CanadaPrPhotoSpec, CanadaCitizenshipPhotoSpec });

    private static readonly AutomatedCheckDefinition CanadaPrintComposition = new(
        "canada-print-composition",
        DocumentCheckStage.Output,
        DocumentCheckDisposition.Blocking,
        "Print Photo Composition",
        "Checks 50 x 70 mm framing and 31-36 mm chin-to-crown head height for Canadian print submissions.",
        new[] { CanadaPassportPhotoSpec, CanadaPrPhotoSpec, CanadaCitizenshipPhotoSpec });

    private static readonly AutomatedCheckDefinition CanadaPrDigitalTechnical = new(
        "canada-pr-digital-technical",
        DocumentCheckStage.Output,
        DocumentCheckDisposition.Blocking,
        "Digital Upload Technical Rules",
        "Checks JPEG output dimensions and file size for Canada permanent resident digital submissions.",
        new[] { CanadaPrPhotographerSheet });

    private static readonly AutomatedCheckDefinition CanadaCitizenshipDigitalTechnical = new(
        "canada-citizenship-digital-technical",
        DocumentCheckStage.Output,
        DocumentCheckDisposition.Blocking,
        "Digital Upload Technical Rules",
        "Checks JPEG output dimensions and file size for Canada citizenship digital submissions.",
        new[] { CanadaCitizenshipDigitalSpec });

    private static readonly AutomatedCheckDefinition CanadaDigitalOriginality = new(
        "canada-digital-originality",
        DocumentCheckStage.Output,
        DocumentCheckDisposition.Advisory,
        "Original Digital File",
        "Reports whether the digital output could be preserved as the unchanged original file.",
        new[] { CanadaPrPhotographerSheet, CanadaCitizenshipDigitalSpec });

    private static readonly ManualChecklistItemDefinition UsManualChecklist = new(
        "us-manual-review",
        "Manual U.S. Photo Checklist",
        "Confirm the photo is recent, unaltered, and uses a plain white or off-white background.",
        new[] { UsPhotoOverview });

    private static readonly ManualChecklistItemDefinition CanadaPrintBackChecklist = new(
        "canada-print-back-review",
        "Manual Canada Print Checklist",
        "Confirm the back of one printed photo includes the photographer or studio information and the date taken where the form requires it.",
        new[] { CanadaPassportPhotoSpec, CanadaPrPhotoSpec, CanadaCitizenshipPhotoSpec });

    private static readonly ManualChecklistItemDefinition CanadaDigitalSupportChecklist = new(
        "canada-digital-support-review",
        "Manual Canada Digital Checklist",
        "Confirm the subject name, photographer or studio name and address, and the photo date are supplied with the upload.",
        new[] { CanadaPrPhotographerSheet, CanadaCitizenshipDigitalSpec });

    private static readonly IReadOnlyDictionary<string, InputProfileDefinition> InputProfiles =
        new Dictionary<string, InputProfileDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["piv-capture"] = new("piv-capture", "PIV Capture", 0.80f, true, 15f),
            ["us-portrait-capture"] = new("us-portrait-capture", "U.S. Portrait Capture", 0.80f, true, 20f),
            ["canada-portrait-capture"] = new("canada-portrait-capture", "Canada Portrait Capture", 0.80f, true, 20f)
        };

    private static readonly IReadOnlyDictionary<string, PassportPhotoSpec> PortraitSpecs =
        new Dictionary<string, PassportPhotoSpec>(StringComparer.OrdinalIgnoreCase)
        {
            ["us-print"] = new(1200, 1200, 0.60f, 0.625f, "jpeg", 600, 0.492f, 0.689f, 0.551f, 0.689f, true, 1200, 1200, 1200, 1200, null, false, false),
            ["us-digital"] = new(600, 600, 0.60f, 0.625f, "jpeg", null, 0.50f, 0.69f, 0.56f, 0.69f, true, 600, 1200, 600, 1200, 245_760, false, false),
            ["canada-print"] = new(1181, 1654, 0.48f, 0.62f, "jpeg", 600, 31f / 70f, 36f / 70f, 0.55f, 0.72f, false, 1181, 1181, 1654, 1654, null, false, false),
            ["canada-pr-digital"] = new(715, 1000, 0.48f, 0.62f, "jpeg", null, 31f / 70f, 36f / 70f, 0.55f, 0.72f, false, 715, 2000, 1000, 2800, 4_194_304, true, true),
            ["canada-citizenship-digital"] = new(840, 1080, 0.48f, 0.62f, "jpeg", null, 31f / 70f, 36f / 70f, 0.55f, 0.72f, false, 420, int.MaxValue, 540, int.MaxValue, 4_194_304, true, true)
        };

    private static readonly DeliverableDefinition PivDigitalDeliverable = new(
        "piv-digital",
        "PIV Card Facial Image",
        "jp2",
        ".piv.jp2",
        DeliverableKind.PivCardImage,
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["format"] = "jp2",
            ["variant"] = "digital"
        },
        new[] { PivOutputGeometry });

    private static readonly DeliverableDefinition PivPrintDeliverable = new(
        "piv-print",
        "PIV Printed Zone 1F Photo",
        "jpg",
        ".piv.print.jpg",
        DeliverableKind.PivPrintedPhoto,
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["format"] = "jpg",
            ["dpi"] = "300",
            ["variant"] = "print"
        },
        new[] { PivOutputGeometry, PivPrintedDpi });

    private static readonly DeliverableDefinition UsPrintDeliverable = new(
        "us-print",
        "U.S. Print Photo",
        "jpeg",
        ".us-passport.print.jpeg",
        DeliverableKind.PaperPhoto,
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["format"] = "jpeg",
            ["dpi"] = "600",
            ["spec"] = "us-print",
            ["variant"] = "print"
        },
        new[] { UsPrintComposition });

    private static readonly DeliverableDefinition UsDigitalDeliverable = new(
        "us-digital",
        "U.S. Digital Photo",
        "jpeg",
        ".us-passport.digital.jpeg",
        DeliverableKind.DigitalUploadPhoto,
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["format"] = "jpeg",
            ["spec"] = "us-digital",
            ["variant"] = "digital"
        },
        new[] { UsPrintComposition, UsDigitalTechnical });

    private static readonly DeliverableDefinition CanadaPrintDeliverable = new(
        "canada-print",
        "Canada Print Photo",
        "jpeg",
        ".canada.print.jpeg",
        DeliverableKind.PaperPhoto,
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["format"] = "jpeg",
            ["dpi"] = "600",
            ["spec"] = "canada-print",
            ["variant"] = "print"
        },
        new[] { CanadaPrintComposition });

    private static readonly DeliverableDefinition CanadaPrDigitalDeliverable = new(
        "canada-pr-digital",
        "Canada Permanent Resident Digital Photo",
        "jpeg",
        ".canada-permanent-resident.digital.jpeg",
        DeliverableKind.DigitalUploadPhoto,
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["format"] = "jpeg",
            ["spec"] = "canada-pr-digital",
            ["variant"] = "digital"
        },
        new[] { CanadaPrintComposition, CanadaPrDigitalTechnical, CanadaDigitalOriginality });

    private static readonly DeliverableDefinition CanadaCitizenshipDigitalDeliverable = new(
        "canada-citizenship-digital",
        "Canada Citizenship Digital Photo",
        "jpeg",
        ".canada-citizenship.digital.jpeg",
        DeliverableKind.DigitalUploadPhoto,
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["format"] = "jpeg",
            ["spec"] = "canada-citizenship-digital",
            ["variant"] = "digital"
        },
        new[] { CanadaPrintComposition, CanadaCitizenshipDigitalTechnical, CanadaDigitalOriginality });

    private static readonly IReadOnlyDictionary<string, DocumentDefinition> Documents =
        new Dictionary<string, DocumentDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["piv"] = new(
                "piv",
                "PIV",
                "Federal PIV facial image and printed Zone 1F photo.",
                "standard",
                DocumentWorkflowFamily.Piv,
                "piv-capture",
                new[] { FipsBiometricStorage, FipsPrintedPhoto, Sp80076Capture, Sp80076FullFrontal, Incits385Geometry },
                new[] { PivInputCapture },
                Array.Empty<ManualChecklistItemDefinition>(),
                new Dictionary<string, VariantDefinition>(StringComparer.OrdinalIgnoreCase)
                {
                    ["standard"] = new("standard", "Digital Card Image + Print Photo", new[] { PivDigitalDeliverable, PivPrintDeliverable }),
                    ["digital"] = new("digital", "Digital Card Image", new[] { PivDigitalDeliverable }),
                    ["print"] = new("print", "Printed Photo", new[] { PivPrintDeliverable })
                }),
            ["us-passport"] = new(
                "us-passport",
                "U.S. Passport",
                "U.S. passport photo workflow.",
                "print",
                DocumentWorkflowFamily.PassportStyle,
                "us-portrait-capture",
                new[] { UsPhotoOverview, UsCompositionTemplate, UsDigitalRequirements },
                new[] { UsInputCapture },
                new[] { UsManualChecklist },
                new Dictionary<string, VariantDefinition>(StringComparer.OrdinalIgnoreCase)
                {
                    ["print"] = new("print", "Print Photo", new[] { UsPrintDeliverable }),
                    ["digital"] = new("digital", "Digital Upload Photo", new[] { UsDigitalDeliverable })
                }),
            ["us-permanent-resident"] = new(
                "us-permanent-resident",
                "U.S. Permanent Resident",
                "U.S. permanent resident application photo workflow.",
                "print",
                DocumentWorkflowFamily.PassportStyle,
                "us-portrait-capture",
                new[] { UsPhotoOverview, UsCompositionTemplate, UsDigitalRequirements },
                new[] { UsInputCapture },
                new[] { UsManualChecklist },
                new Dictionary<string, VariantDefinition>(StringComparer.OrdinalIgnoreCase)
                {
                    ["print"] = new("print", "Print Photo", new[] { UsPrintDeliverable with { FileSuffix = ".us-permanent-resident.print.jpeg", DisplayName = "U.S. Permanent Resident Print Photo" } }),
                    ["digital"] = new("digital", "Digital Upload Photo", new[] { UsDigitalDeliverable with { FileSuffix = ".us-permanent-resident.digital.jpeg", DisplayName = "U.S. Permanent Resident Digital Photo" } })
                }),
            ["canada-passport"] = new(
                "canada-passport",
                "Canada Passport",
                "Canadian passport print photo workflow.",
                "print",
                DocumentWorkflowFamily.PassportStyle,
                "canada-portrait-capture",
                new[] { CanadaPassportPhotoSpec },
                new[] { CanadaInputCapture },
                new[] { CanadaPrintBackChecklist },
                new Dictionary<string, VariantDefinition>(StringComparer.OrdinalIgnoreCase)
                {
                    ["print"] = new("print", "Print Photo", new[] { CanadaPrintDeliverable with { FileSuffix = ".canada-passport.print.jpeg", DisplayName = "Canada Passport Print Photo" } })
                }),
            ["canada-permanent-resident"] = new(
                "canada-permanent-resident",
                "Canada Permanent Resident",
                "Canadian permanent resident card photo workflow.",
                "print",
                DocumentWorkflowFamily.PassportStyle,
                "canada-portrait-capture",
                new[] { CanadaPrPhotoSpec, CanadaPrPhotographerSheet },
                new[] { CanadaInputCapture },
                new[] { CanadaPrintBackChecklist, CanadaDigitalSupportChecklist },
                new Dictionary<string, VariantDefinition>(StringComparer.OrdinalIgnoreCase)
                {
                    ["print"] = new("print", "Print Photo", new[] { CanadaPrintDeliverable with { FileSuffix = ".canada-permanent-resident.print.jpeg", DisplayName = "Canada Permanent Resident Print Photo" } }),
                    ["digital"] = new("digital", "Digital Upload Photo", new[] { CanadaPrDigitalDeliverable })
                }),
            ["canada-citizenship-grant"] = new(
                "canada-citizenship-grant",
                "Canada Citizenship Grant",
                "Canadian citizenship grant photo workflow.",
                "print",
                DocumentWorkflowFamily.PassportStyle,
                "canada-portrait-capture",
                new[] { CanadaCitizenshipPhotoSpec, CanadaCitizenshipDigitalSpec },
                new[] { CanadaInputCapture },
                new[] { CanadaPrintBackChecklist, CanadaDigitalSupportChecklist },
                new Dictionary<string, VariantDefinition>(StringComparer.OrdinalIgnoreCase)
                {
                    ["print"] = new("print", "Print Photo", new[] { CanadaPrintDeliverable with { FileSuffix = ".canada-citizenship-grant.print.jpeg", DisplayName = "Canada Citizenship Grant Print Photo" } }),
                    ["digital"] = new("digital", "Digital Upload Photo", new[] { CanadaCitizenshipDigitalDeliverable with { FileSuffix = ".canada-citizenship-grant.digital.jpeg", DisplayName = "Canada Citizenship Grant Digital Photo" } })
                }),
            ["canada-proof-of-citizenship"] = new(
                "canada-proof-of-citizenship",
                "Canada Proof of Citizenship",
                "Canadian proof of citizenship photo workflow.",
                "print",
                DocumentWorkflowFamily.PassportStyle,
                "canada-portrait-capture",
                new[] { CanadaCitizenshipPhotoSpec, CanadaCitizenshipDigitalSpec },
                new[] { CanadaInputCapture },
                new[] { CanadaPrintBackChecklist, CanadaDigitalSupportChecklist },
                new Dictionary<string, VariantDefinition>(StringComparer.OrdinalIgnoreCase)
                {
                    ["print"] = new("print", "Print Photo", new[] { CanadaPrintDeliverable with { FileSuffix = ".canada-proof-of-citizenship.print.jpeg", DisplayName = "Canada Proof of Citizenship Print Photo" } }),
                    ["digital"] = new("digital", "Digital Upload Photo", new[] { CanadaCitizenshipDigitalDeliverable with { FileSuffix = ".canada-proof-of-citizenship.digital.jpeg", DisplayName = "Canada Proof of Citizenship Digital Photo" } })
                })
        };

    /// <summary>
    /// Gets all shipped document workflows.
    /// </summary>
    public static IReadOnlyCollection<DocumentDefinition> GetAll() => Documents.Values.ToArray();

    /// <summary>
    /// Gets an input capture profile used by a shipped document.
    /// </summary>
    public static Result<InputProfileDefinition, PipelineError> GetInputProfile(string profileId) =>
        InputProfiles.TryGetValue(profileId, out var profile)
            ? Result.Success<InputProfileDefinition, PipelineError>(profile)
            : Result.Failure<InputProfileDefinition, PipelineError>(
                new ConfigurationError($"Unknown input profile '{profileId}'.", profileId));

    /// <summary>
    /// Gets a portrait spec used by a shipped deliverable.
    /// </summary>
    public static Result<PassportPhotoSpec, PipelineError> GetPassportPhotoSpec(string specId) =>
        PortraitSpecs.TryGetValue(specId, out var spec)
            ? Result.Success<PassportPhotoSpec, PipelineError>(spec)
            : Result.Failure<PassportPhotoSpec, PipelineError>(
                new ConfigurationError($"Unknown portrait spec '{specId}'.", specId));

    /// <summary>
    /// Looks up a document workflow by identifier.
    /// </summary>
    public static bool TryGetDocument(string documentId, out DocumentDefinition document) =>
        Documents.TryGetValue(documentId, out document!);

    /// <summary>
    /// Gets a document workflow when it is shipped.
    /// </summary>
    public static Result<DocumentDefinition, PipelineError> GetDocument(string documentId) =>
        TryGetDocument(documentId, out var document)
            ? Result.Success<DocumentDefinition, PipelineError>(document)
            : Result.Failure<DocumentDefinition, PipelineError>(
                new ConfigurationError(
                    $"Unsupported document '{documentId}'. Supported documents: {string.Join(", ", Documents.Keys)}",
                    documentId));
}
