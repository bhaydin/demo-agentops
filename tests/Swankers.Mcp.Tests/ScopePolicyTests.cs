using Swankers.Mcp.Security;

namespace Swankers.Mcp.Tests;

public class ScopePolicyTests
{
    private static readonly CallerScope Owner = new(ScopeKind.Owner, "0001", GateEnabled: true);
    private static readonly CallerScope Commissioner = new(ScopeKind.Commissioner, "0001", GateEnabled: true);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0001")]
    [InlineData(" 0001 ")]
    public void Owner_acts_for_its_own_franchise(string? requested)
        => Assert.Equal("0001", ScopePolicy.ResolveEffectiveFranchise(Owner, requested));

    [Theory]
    [InlineData("0002")]
    [InlineData("0000")]
    [InlineData("0099")]
    public void Owner_cannot_act_for_another_franchise(string requested)
    {
        var ex = Assert.Throws<ScopeViolationException>(() => ScopePolicy.ResolveEffectiveFranchise(Owner, requested));

        Assert.Equal(requested, ex.RequestedFranchiseId);
        Assert.Contains("Scope denied", ex.Message);
    }

    [Theory]
    [InlineData("0002", "0002")]
    [InlineData("0000", "0000")]
    [InlineData("0099", "0099")]
    [InlineData(null, "0001")]
    public void Commissioner_acts_for_any_franchise_including_0000(string? requested, string expected)
        // DEMO: intentionally vulnerable (Friday talk). See docs/ARCHITECTURE.md#security-demo.
        => Assert.Equal(expected, ScopePolicy.ResolveEffectiveFranchise(Commissioner, requested));
}
