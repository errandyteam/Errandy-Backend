using Errandy.Application.Common.Exceptions;
using Errandy.Application.Common.Utils;
using Errandy.Application.Interfaces;
using Errandy.Domain.Entities;
using Errandy.Domain.Enums;
using Errandy.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Errandy.Application.Features.Errands.UploadProof;

public class UploadProofCommandHandler : IRequestHandler<UploadProofCommand>
{
    private readonly IApplicationDbContext _context;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly IFileStorage _fileStorage;
    private readonly ILogger<UploadProofCommandHandler> _logger;

    public UploadProofCommandHandler(
        IApplicationDbContext context,
        IDateTimeProvider dateTimeProvider,
        IFileStorage fileStorage,
        ILogger<UploadProofCommandHandler> logger)
    {
        _context = context;
        _dateTimeProvider = dateTimeProvider;
        _fileStorage = fileStorage;
        _logger = logger;
    }

    public async Task Handle(UploadProofCommand request, CancellationToken cancellationToken)
    {
        var errand = await _context.Errands
            .FirstOrDefaultAsync(e => e.Id == request.ErrandId, cancellationToken);

        if (errand is null)
            throw new NotFoundException(nameof(Domain.Entities.Errand), request.ErrandId);

        // Check BEFORE touching the disk, so nobody can fill the server with files
        // by uploading to errands that aren't theirs or aren't in progress.
        if (errand.RunnerId != request.RunnerId)
            throw new ForbiddenAccessException("Only the runner assigned to this errand can upload proof.");

        if (errand.Status != ErrandStatus.InProgress)
            throw new InvalidErrandStateException(errand.Status, "upload proof");

        var imageKind = ImageFileInspector.Inspect(request.ImageContent)
            ?? throw new ArgumentException("The proof photo must be a JPEG, PNG or WebP image.");

        var folder = $"proofs/{errand.Id}";
        var savedKeys = new List<string>();

        try
        {
            var imageKey = await _fileStorage.SaveAsync(folder, imageKind.Extension, request.ImageContent, cancellationToken);
            savedKeys.Add(imageKey);

            string? receiptKey = null;
            if (request.ReceiptContent is not null)
            {
                var receiptKind = ImageFileInspector.InspectReceipt(request.ReceiptContent)
    ?? throw new ArgumentException("The receipt must be a JPEG, PNG or WebP image, or a PDF.");

                receiptKey = await _fileStorage.SaveAsync(folder, receiptKind.Extension, request.ReceiptContent, cancellationToken);
                savedKeys.Add(receiptKey);
            }

            var utcNow = _dateTimeProvider.UtcNow;

            // Proof.ImageUrl / ReceiptUrl now hold storage keys, not web links. The column
            // names are unchanged to avoid a database migration; rename them later.
            var proof = Proof.Create(errand.Id, imageKey, receiptKey, utcNow);
            errand.AttachProofAndSubmitForConfirmation(request.RunnerId, proof, request.FinalCost, utcNow);

            _context.Proofs.Add(proof);
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // Nothing in the database points at these files, so don't leave them on disk.
            foreach (var key in savedKeys)
            {
                try { await _fileStorage.DeleteAsync(key, CancellationToken.None); }
                catch (Exception ex) { _logger.LogWarning(ex, "Could not clean up stored file {Key} after a failed proof upload.", key); }
            }
            throw;
        }

        // Nothing is scheduled here. The errand now has PendingConfirmationAt set, and the
        // background sweep (ErrandAutoReleaseWorker) releases it once the confirmation window
        // has passed. Nothing is held in memory, so a restart loses nothing.
    }
}