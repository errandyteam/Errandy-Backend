using Errandy.Application.Common.Exceptions;
using Errandy.Application.Common.Utils;
using Errandy.Application.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Errandy.Application.Features.Errands.GetProofFile;

public class GetProofFileQueryHandler : IRequestHandler<GetProofFileQuery, ProofFileResult>
{
    private readonly IApplicationDbContext _context;
    private readonly IFileStorage _fileStorage;

    public GetProofFileQueryHandler(IApplicationDbContext context, IFileStorage fileStorage)
    {
        _context = context;
        _fileStorage = fileStorage;
    }

    public async Task<ProofFileResult> Handle(GetProofFileQuery request, CancellationToken cancellationToken)
    {
        var errand = await _context.Errands
            .Include(e => e.Proof)
            .FirstOrDefaultAsync(e => e.Id == request.ErrandId, cancellationToken);

        if (errand is null)
            throw new NotFoundException(nameof(Domain.Entities.Errand), request.ErrandId);

        // Proof photos are private. Only the two people on the errand, or an admin.
        var isParty = request.RequestingUserId == errand.CustomerId || request.RequestingUserId == errand.RunnerId;
        if (!request.IsAdmin && !isParty)
            throw new ForbiddenAccessException("You do not have access to this proof.");

        var key = request.Kind == ProofFileKind.Receipt ? errand.Proof?.ReceiptUrl : errand.Proof?.ImageUrl;

        // Proofs saved before file upload existed hold a web link, not a stored file.
        if (string.IsNullOrWhiteSpace(key) || key.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            throw new NotFoundException("Proof file", errand.Id);

        var stream = await _fileStorage.OpenReadAsync(key, cancellationToken);
        if (stream is null)
            throw new NotFoundException("Proof file", errand.Id);

        return new ProofFileResult(stream, ImageFileInspector.ContentTypeForExtension(Path.GetExtension(key)));
    }
}