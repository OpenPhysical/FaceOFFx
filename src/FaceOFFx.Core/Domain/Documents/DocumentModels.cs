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
/// <param name="Name">Human-readable check name.</param>
/// <param name="Description">Short description of the enforced rule.</param>
/// <param name="Citations">Normative citations backing the rule.</param>
public sealed record AutomatedCheckDefinition(
    string Id,
    DocumentCheckStage Stage,
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
/// <param name="RendererKey">Internal renderer selector.</param>
/// <param name="ValidatorKey">Internal validator selector.</param>
/// <param name="ProductionDefaults">Recorded production defaults such as DPI or nominal size.</param>
/// <param name="OutputChecks">Automated checks applied to this artifact.</param>
public sealed record DeliverableDefinition(
    string Id,
    string DisplayName,
    string Format,
    string FileSuffix,
    string RendererKey,
    string ValidatorKey,
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
/// <param name="Citations">Top-level citations for the workflow.</param>
/// <param name="InputChecks">Automated checks run on the source capture.</param>
/// <param name="ManualChecklist">Manual items that must still be reviewed by an operator.</param>
/// <param name="Variants">Named variants supported by the workflow.</param>
public sealed record DocumentDefinition(
    string Id,
    string DisplayName,
    string Description,
    string PrimaryVariantId,
    IReadOnlyList<SpecificationCitation> Citations,
    IReadOnlyList<AutomatedCheckDefinition> InputChecks,
    IReadOnlyList<ManualChecklistItemDefinition> ManualChecklist,
    IReadOnlyDictionary<string, VariantDefinition> Variants);

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
/// <param name="Name">Human-readable check name.</param>
/// <param name="Passed">Whether the check passed.</param>
/// <param name="Summary">Short summary of the evaluation result.</param>
/// <param name="Citations">Normative citations backing the check.</param>
public sealed record AutomatedCheckResult(
    string Id,
    DocumentCheckStage Stage,
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
/// <param name="ProductionDefaults">Recorded production defaults used to create the artifact.</param>
/// <param name="Checks">Per-check results for the artifact.</param>
public sealed record DeliverableResult(
    string Id,
    string DisplayName,
    string OutputPath,
    bool Passed,
    string Summary,
    int FileSizeBytes,
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
