using Microsoft.EntityFrameworkCore;
using SkillSamples.Cqrs;

namespace SkillSamples.Compliance;

// A command that processes a patient's data for a purpose that needs their consent.
public interface IRequiresConsent
{
    int SubjectId { get; }
    string Purpose { get; }
}

public sealed class ConsentRequiredException(int subjectId, string purpose)
    : Exception($"Subject {subjectId} has no active consent for '{purpose}'.")
{
    public string Purpose => purpose;
}

public sealed class ConsentStore(ComplianceDbContext db)
{
    public Task<bool> HasActiveConsentAsync(int subjectId, string purpose, CancellationToken ct) =>
        db.Consents.AnyAsync(c => c.SubjectId == subjectId && c.Purpose == purpose && c.WithdrawnAt == null, ct);
}

// Checks the consent of the person the data is about. The signed-in user is the doctor or the
// researcher: their own consent says nothing about the patient's data.
public sealed class ConsentDecorator<TCommand, TResult>(ICommandHandler<TCommand, TResult> inner, ConsentStore consents)
    : ICommandHandler<TCommand, TResult> where TCommand : ICommand<TResult>
{
    public async Task<TResult> HandleAsync(TCommand command, CancellationToken ct)
    {
        if (command is IRequiresConsent needs && !await consents.HasActiveConsentAsync(needs.SubjectId, needs.Purpose, ct))
            throw new ConsentRequiredException(needs.SubjectId, needs.Purpose);   // → 403 ProblemDetails

        return await inner.HandleAsync(command, ct);
    }
}
