using MediatR;

namespace Errandy.Application.Features.Errands.GetProofFile;

public enum ProofFileKind
{
    Image = 0,
    Receipt = 1
}

public record GetProofFileQuery : IRequest<ProofFileResult>
{
    public Guid ErrandId { get; init; }
    public Guid RequestingUserId { get; init; }
    public bool IsAdmin { get; init; }
    public ProofFileKind Kind { get; init; }
}

public sealed record ProofFileResult(Stream Content, string ContentType);