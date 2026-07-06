using FluentValidation;
using Yarpa.Contracts;

namespace Yarpa.Api.Validation;

/// <summary>
/// Validates required fields on an incoming <see cref="DiagnosticsSnapshot"/>.
/// </summary>
public sealed class DiagnosticsSnapshotValidator : AbstractValidator<DiagnosticsSnapshot>
{
    public DiagnosticsSnapshotValidator()
    {
        RuleFor(x => x.SnapshotId)
            .NotEqual(Guid.Empty)
            .WithMessage("שדה snapshotId הוא חובה");

        RuleFor(x => x.MachineId)
            .NotEmpty()
            .WithMessage("שדה machineId הוא חובה");

        RuleFor(x => x.CollectedAtUtc)
            .NotEqual(default(DateTimeOffset))
            .WithMessage("שדה collectedAtUtc הוא חובה");

        RuleFor(x => x.SchemaVersion)
            .NotEmpty()
            .WithMessage("שדה schemaVersion הוא חובה");
    }
}
