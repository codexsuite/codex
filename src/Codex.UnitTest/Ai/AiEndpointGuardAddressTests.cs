using System.Net;
using Codex.Application.Features.Ai.Integrations;

namespace Codex.UnitTest.Ai;

// AiEndpointGuard.IsAllowedAddress, the one rule the guard and the AI HttpClients' connect step share: public always;
// private and loopback only with local endpoints allowed; link-local (cloud metadata) never.
[TestFixture]
public class AiEndpointGuardAddressTests
{
    [TestCase("93.184.216.34", false, true)]
    [TestCase("2606:2800:220:1:248:1893:25c8:1946", false, true)]
    [TestCase("10.0.0.5", false, false)]
    [TestCase("192.168.1.20", false, false)]
    [TestCase("127.0.0.1", false, false)]
    [TestCase("169.254.169.254", false, false)]
    [TestCase("10.0.0.5", true, true)]
    [TestCase("192.168.1.20", true, true)]
    [TestCase("127.0.0.1", true, true)]
    [TestCase("::1", true, true)]
    [TestCase("169.254.169.254", true, false)]
    [TestCase("::ffff:169.254.169.254", true, false)]
    [TestCase("fe80::1", true, false)]
    public void IsAllowedAddress_WhenChecked_ShouldFollowTheLocalSwitchButNeverAllowLinkLocal(string address, bool localAllowed, bool expected)
    {
        AiEndpointGuard.IsAllowedAddress(IPAddress.Parse(address), localAllowed).ShouldBe(expected);
    }
}
