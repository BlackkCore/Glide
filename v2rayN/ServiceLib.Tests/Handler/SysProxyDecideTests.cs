using ServiceLib.Handler.SysProxy;

namespace ServiceLib.Tests.Handler;

public class SysProxyDecideTests
{
    // Glide's ownership rule (F-02/F-04/F-08): never point Windows at a port another program owns,
    // and only clear a Windows proxy that points at our own port.
    [Test]
    [Arguments(ESysProxyType.ForcedChange, false, false, ESysProxyType.ForcedChange, true)]
    [Arguments(ESysProxyType.ForcedChange, true, false, ESysProxyType.Unchanged, false)]
    [Arguments(ESysProxyType.Pac, true, true, ESysProxyType.Unchanged, false)]
    [Arguments(ESysProxyType.ForcedClear, false, true, ESysProxyType.ForcedClear, true)]
    [Arguments(ESysProxyType.ForcedClear, false, false, ESysProxyType.Unchanged, false)]
    [Arguments(ESysProxyType.ForcedClear, true, true, ESysProxyType.Unchanged, false)]
    [Arguments(ESysProxyType.Unchanged, true, true, ESysProxyType.Unchanged, true)]
    public async Task Decide_Matrix(ESysProxyType requested, bool portIsForeign, bool proxyPointsToUs, ESysProxyType expectedType, bool expectedAllowed)
    {
        var (type, allowed) = SysProxyHandler.Decide(requested, portIsForeign, proxyPointsToUs);
        await type.Should().BeEqualTo(expectedType);
        await allowed.Should().BeEqualTo(expectedAllowed);
    }
}
