using Errandy.Domain.Entities;
using Errandy.Domain.Enums;
using Errandy.Tests.Support;
using Xunit;

namespace Errandy.Tests.Domain;

public class ProofCostTests
{
    [Fact]
    public void Proof_WithAFinalCostAboveTheAgreedCost_IsRefused()
    {
        var runnerId = Guid.NewGuid();
        var errand = TestData.InProgressErrand(Guid.NewGuid(), runnerId); // agreed cost 5000
        var proof = Proof.Create(errand.Id, "key", null, TestData.Now);

        Assert.Throws<ArgumentException>(() =>
            errand.AttachProofAndSubmitForConfirmation(runnerId, proof, 6000m, TestData.Now));
    }

    [Fact]
    public void Proof_ClearsAPriceRequestThatWasNeverAnswered()
    {
        var runnerId = Guid.NewGuid();
        var errand = TestData.InProgressErrand(Guid.NewGuid(), runnerId);
        errand.ProposePriceAdjustment(runnerId, 6000m, "Prices went up", TestData.Now);
        var proof = Proof.Create(errand.Id, "key", null, TestData.Now);

        errand.AttachProofAndSubmitForConfirmation(runnerId, proof, 4800m, TestData.Now);

        Assert.Null(errand.ProposedCost);
        Assert.Equal(ErrandStatus.PendingConfirmation, errand.Status);
    }
}