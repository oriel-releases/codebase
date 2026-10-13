using System.Net;

namespace Oriel.Core.Tests;

public sealed class FastConnectTests
{
    [Fact]
    public void Addresses_take_turns_IPv6_first()
    {
        var order = FastConnect.Interleave([
            IPAddress.Parse("1.1.1.1"), IPAddress.Parse("2.2.2.2"), IPAddress.Parse("3.3.3.3"),
            IPAddress.Parse("::1"), IPAddress.Parse("::2"),
        ]).Select(a => a.ToString());
        Assert.Equal(["::1", "1.1.1.1", "::2", "2.2.2.2", "3.3.3.3"], order);
    }

    [Fact]
    public void Only_one_family_keeps_its_order() =>
        Assert.Equal(["1.1.1.1", "2.2.2.2"], FastConnect.Interleave([IPAddress.Parse("1.1.1.1"), IPAddress.Parse("2.2.2.2")]).Select(a => a.ToString()));
}
