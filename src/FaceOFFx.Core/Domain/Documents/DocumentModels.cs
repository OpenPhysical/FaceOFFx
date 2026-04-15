using System;
using System.Collections.Generic;
namespace FaceOFFx.Core.Domain.Documents;

/// <summary>
/// Stage at which an automated document check runs.
/// </summary>
public enum DocumentCheckStage
{
    /// <summary>
    /// The check runs on the source capture before rendering outputs.
    /// </summary>
    Input,

    /// <summary>
    /// The check runs on a rendered output artifact.
    /// </summary>
    Output
}

/// <summary>
/// Whether an automated check blocks compliance or is advisory only.
/// </summary>
public enum DocumentCheckDisposition
{
    /// <summary>
    /// A failed check makes the result non-compliant.
    /// </summary>
    Blocking,

    /// <summary>
    /// A failed check is reported but does not make the result non-compliant.
    /// </summary>
    Advisory
}

/// <summary>
/// Internal workflow family used to render and validate a document.
/// </summary>
public enum DocumentWorkflowFamily
{
    /// <summary>
    /// PIV-specific workflow using PIV crop and compliance validation.
    /// </summary>
    Piv,

    /// <summary>
    /// Passport-style workflow using head-height and eye-line composition rules.
    /// </summary>
    PassportStyle
}

/// <summary>
/// Concrete artifact kind produced by a document workflow.
/// </summary>
public enum DeliverableKind
{
    /// <summary>
    /// PIV JPEG 2000 card image.
    /// </summary>
    PivCardImage,

    /// <summary>
    /// PIV printed Zone 1F image.
    /// </summary>
    PivPrintedPhoto,

    /// <summary>
    /// Passport-style printed photo.
    /// </summary>
    PaperPhoto,

    /// <summary>
    /// Passport-style digital upload photo.
    /// </summary>
    DigitalUploadPhoto
}

/// <summary>
/// Exact normative citation attached to a shipped document workflow.
/// </summary>
/// <param name="Id">Stable citation identifier.</param>
/// <param name="DocumentTitle">Human-readable source document title.</param>
/// <param name="Clause">Section, clause, or page reference.</param>
/// <param name="Url">Canonical source URL.</param>
/// <param name="Summary">Short summary of the cited requirement.</param>
public sealed record SpecificationCitation(
    string Id,
    string DocumentTitle,
    string Clause,
    string Url,
    string Summary);

/// <summary>
/// Blocking automated rule evaluated by a document workflow.
/// </summary>
/// <param name="Id">Stable check identifier.</param>
/// <param name="Stage">Whether the check applies to input or output.</param>
/// <param name="Disposition">Whether the check blocks compliance or is advisory.</param>
/// <param name="Name">Human-readable check name.</param>
/// <param name="Description">Short description of the enforced rule.</param>
/// <param name="Citations">Normative citations backing the rule.</param>
public sealed record AutomatedCheckDefinition(
    string Id,
    DocumentCheckStage Stage,
    DocumentCheckDisposition Disposition,
    string Name,
    string Description,
    IReadOnlyList<SpecificationCitation> Citations);

/// <summary>
/// Manual checklist item that cannot be enforced reliably by automation.
/// </summary>
/// <param name="Id">Stable checklist identifier.</param>
/// <param name="Name">Human-readable checklist name.</param>
/// <param name="Description">Operator-facing action or review item.</param>
/// <param name="Citations">Normative citations backing the checklist item.</param>
public sealed record ManualChecklistItemDefinition(
    string Id,
    string Name,
    string Description,
    IReadOnlyList<SpecificationCitation> Citations);

/// <summary>
/// Concrete artifact produced by a document variant.
/// </summary>
/// <param name="Id">Stable deliverable identifier.</param>
/// <param name="DisplayName">Human-readable deliverable name.</param>
/// <param name="Format">Output format identifier.</param>
/// <param name="FileSuffix">Filename suffix used for the artifact.</param>
/// <param name="Kind">Concrete artifact kind used by the internal workflow.</param>
/// <param name="ProductionDefaults">Recorded production defaults such as DPI or nominal size.</param>
/// <param name="OutputChecks">Automated checks applied to this artifact.</param>
public sealed record DeliverableDefinition(
    string Id,
    string DisplayName,
    string Format,
    string FileSuffix,
    DeliverableKind Kind,
    IReadOnlyDictionary<string, string> ProductionDefaults,
    IReadOnlyList<AutomatedCheckDefinition> OutputChecks);

/// <summary>
/// Named document variant such as paper or digital upload.
/// </summary>
/// <param name="Id">Stable variant identifier.</param>
/// <param name="DisplayName">Human-readable variant name.</param>
/// <param name="Deliverables">Artifacts produced for the variant.</param>
public sealed record VariantDefinition(
    string Id,
    string DisplayName,
    IReadOnlyList<DeliverableDefinition> Deliverables);

/// <summary>
/// Shipped document workflow backed by exact citations.
/// </summary>
/// <param name="Id">Stable document identifier used by the CLI and API.</param>
/// <param name="DisplayName">Human-readable document name.</param>
/// <param name="Description">Short description of the supported workflow.</param>
/// <param name="PrimaryVariantId">Default variant used when the caller does not choose one.</param>
/// <param name="WorkflowFamily">Internal workflow family that renders and validates the document.</param>
/// <param name="InputProfileId">Input suitability profile used for the document capture checks.</param>
/// <param name="Citations">Top-level citations for the workflow.</param>
/// <param name="InputChecks">Automated checks run on the source capture.</param>
/// <param name="ManualChecklist">Manual items that must still be reviewed by an operator.</param>
/// <param name="Variants">Named variants supported by the workflow.</param>
public sealed record DocumentDefinition(
    string Id,
    string DisplayName,
    string Description,
    string PrimaryVariantId,
    DocumentWorkflowFamily WorkflowFamily,
    string InputProfileId,
    IReadOnlyList<SpecificationCitation> Citations,
    IReadOnlyList<AutomatedCheckDefinition> InputChecks,
    IReadOnlyList<ManualChecklistItemDefinition> ManualChecklist,
    IReadOnlyDictionary<string, VariantDefinition> Variants);

/// <summary>
/// Input-stage capture suitability profile for a document workflow.
/// </summary>
/// <param name="Id">Stable profile identifier.</param>
/// <param name="DisplayName">Human-readable profile name.</param>
/// <param name="MinimumFaceConfidence">Minimum acceptable face detection confidence.</param>
/// <param name="RequireSingleFace">Whether multiple faces should fail the check.</param>
/// <param name="MaxRollDegrees">Maximum acceptable eye-line rotation in degrees.</param>
public sealed record InputProfileDefinition(
    string Id,
    string DisplayName,
    float MinimumFaceConfidence,
    bool RequireSingleFace,
    float MaxRollDegrees);

/// <summary>
/// Passport-style rendering and validation specification for paper or digital photo outputs.
/// </summary>
/// <param name="TargetWidth">Output width in pixels.</param>
/// <param name="TargetHeight">Output height in pixels.</param>
/// <param name="TargetHeadHeightRatio">Target chin-to-crown ratio used while cropping.</param>
/// <param name="TargetEyeFromBottomRatio">Target eye-line placement from the bottom used while cropping.</param>
/// <param name="OutputFormat">Output file format emitted by the workflow.</param>
/// <param name="Dpi">Optional output DPI metadata.</param>
/// <param name="MinHeadHeightRatio">Minimum allowed head-height ratio in the final output.</param>
/// <param name="MaxHeadHeightRatio">Maximum allowed head-height ratio in the final output.</param>
/// <param name="MinEyeFromBottomRatio">Minimum allowed eye-line ratio from the bottom in the final output.</param>
/// <param name="MaxEyeFromBottomRatio">Maximum allowed eye-line ratio from the bottom in the final output.</param>
/// <param name="RequireSquare">Whether the final image must be square.</param>
/// <param name="MinWidth">Minimum allowed output width in pixels.</param>
/// <param name="MaxWidth">Maximum allowed output width in pixels.</param>
/// <param name="MinHeight">Minimum allowed output height in pixels.</param>
/// <param name="MaxHeight">Maximum allowed output height in pixels.</param>
/// <param name="MaxFileSizeBytes">Maximum file size in bytes, if any.</param>
/// <param name="PreserveOriginalFileWhenValid">Whether a digital workflow should pass through an unchanged original JPEG when it already satisfies the measurable rules.</param>
/// <param name="RequiresSupportingInfoSidecar">Whether the workflow should emit a supporting-info sidecar template.</param>
public sealed record PassportPhotoSpec(
    int TargetWidth,
    int TargetHeight,
    float TargetHeadHeightRatio,
    float TargetEyeFromBottomRatio,
    string OutputFormat,
    int? Dpi,
    float MinHeadHeightRatio,
    float MaxHeadHeightRatio,
    float MinEyeFromBottomRatio,
    float MaxEyeFromBottomRatio,
    bool RequireSquare,
    int MinWidth,
    int MaxWidth,
    int MinHeight,
    int MaxHeight,
    int? MaxFileSizeBytes,
    bool PreserveOriginalFileWhenValid,
    bool RequiresSupportingInfoSidecar);

/// <summary>
/// Request to execute a document workflow.
/// </summary>
/// <param name="InputPath">Source image path.</param>
/// <param name="DocumentId">Document workflow identifier.</param>
/// <param name="VariantId">Optional variant override; defaults to the document primary variant.</param>
/// <param name="OutputDirectory">Optional output directory.</param>
/// <param name="Json">Whether the caller expects machine-readable stdout.</param>
/// <param name="Explain">Whether the caller wants cited clauses echoed in human output.</param>
public sealed record DocumentJobRequest(
    string InputPath,
    string DocumentId,
    string? VariantId = null,
    string? OutputDirectory = null,
    bool Json = false,
    bool Explain = false);

/// <summary>
/// Result of evaluating one automated check.
/// </summary>
/// <param name="Id">Stable check identifier.</param>
/// <param name="Stage">Input or output stage.</param>
/// <param name="Disposition">Whether the check is blocking or advisory.</param>
/// <param name="Name">Human-readable check name.</param>
/// <param name="Passed">Whether the check passed.</param>
/// <param name="Summary">Short summary of the evaluation result.</param>
/// <param name="Citations">Normative citations backing the check.</param>
public sealed record AutomatedCheckResult(
    string Id,
    DocumentCheckStage Stage,
    DocumentCheckDisposition Disposition,
    string Name,
    bool Passed,
    string Summary,
    IReadOnlyList<SpecificationCitation> Citations);

/// <summary>
/// Manual checklist item surfaced in output and provenance.
/// </summary>
/// <param name="Id">Stable checklist identifier.</param>
/// <param name="Name">Human-readable checklist name.</param>
/// <param name="Summary">Operator-facing review instruction.</param>
/// <param name="Citations">Normative citations backing the checklist item.</param>
public sealed record ManualChecklistResult(
    string Id,
    string Name,
    string Summary,
    IReadOnlyList<SpecificationCitation> Citations);

/// <summary>
/// Result of rendering and validating one deliverable artifact.
/// </summary>
/// <param name="Id">Stable deliverable identifier.</param>
/// <param name="DisplayName">Human-readable deliverable name.</param>
/// <param name="OutputPath">Filesystem path of the written artifact.</param>
/// <param name="Passed">Whether all automated checks passed.</param>
/// <param name="Summary">Short deliverable summary.</param>
/// <param name="FileSizeBytes">Written artifact size in bytes.</param>
/// <param name="SupportingInfoPath">Optional sidecar file path with supporting information or receipt fields.</param>
/// <param name="OriginalFileRequirementSatisfied">Whether a workflow-specific unchanged-original requirement was satisfied.</param>
/// <param name="ProductionDefaults">Recorded production defaults used to create the artifact.</param>
/// <param name="Checks">Per-check results for the artifact.</param>
public sealed record DeliverableResult(
    string Id,
    string DisplayName,
    string OutputPath,
    bool Passed,
    string Summary,
    int FileSizeBytes,
    string? SupportingInfoPath,
    bool? OriginalFileRequirementSatisfied,
    IReadOnlyDictionary<string, string> ProductionDefaults,
    IReadOnlyList<AutomatedCheckResult> Checks);

/// <summary>
/// Full result of a document workflow run.
/// </summary>
/// <param name="Document">Document workflow that ran.</param>
/// <param name="Variant">Variant that was selected.</param>
/// <param name="InputPath">Source image path.</param>
/// <param name="InputPassed">Whether the input checks passed.</param>
/// <param name="InputSummary">Short summary of input analysis.</param>
/// <param name="ProvenancePath">Written provenance file path.</param>
/// <param name="InputChecks">Per-check input results.</param>
/// <param name="ManualChecklist">Manual review items surfaced for the operator.</param>
/// <param name="Deliverables">Rendered deliverables and their validation results.</param>
/// <param name="GeneratedAtUtc">UTC timestamp for the completed run.</param>
public sealed record DocumentJobResult(
    DocumentDefinition Document,
    VariantDefinition Variant,
    string InputPath,
    bool InputPassed,
    string InputSummary,
    string ProvenancePath,
    IReadOnlyList<AutomatedCheckResult> InputChecks,
    IReadOnlyList<ManualChecklistResult> ManualChecklist,
    IReadOnlyList<DeliverableResult> Deliverables,
    DateTimeOffset GeneratedAtUtc);
