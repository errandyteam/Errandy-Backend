using Errandy.Domain.Entities;
namespace Errandy.Application.Common.DTOs;

public record RunnerProfileDto(
    Guid Id,
    Guid UserId,
    string KycStatus,
    string? DocumentType,
    string? DocumentNumber,
    string? RejectionReason,
    decimal Rating,
    decimal CompletionRate,
    decimal CancellationRate,
    DateTime? ReviewedAt,
    DateTime CreatedAt)
{
    public static RunnerProfileDto FromEntity(RunnerProfile p) => new(
        p.Id,
        p.UserId,
        p.KycStatus.ToString(),
        p.DocumentType,
        p.DocumentNumber,
        p.RejectionReason,
        p.Rating,
        p.CompletionRate,
        p.CancellationRate,
        p.ReviewedAt,
        p.CreatedAt);
}