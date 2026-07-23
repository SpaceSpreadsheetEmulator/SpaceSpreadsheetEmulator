using SpaceSpreadsheetEmulator.Gateway.Compatibility;
using SpaceSpreadsheetEmulator.Protocol.Values;

namespace SpaceSpreadsheetEmulator.Gateway.IntegrationTests.Compatibility;

public sealed class Build3396210StartupProfileTests
{
    [Fact]
    public void EveryFallbackRouteReturnsItsAuthoredShape()
    {
        var profile = new Build3396210StartupProfile(new GatewayCompatibilityOptions());
        CapturedStartupReplayCursor cursor = profile.CreateCursor();
        string[] routes =
        [
            "machoNet.GetServiceInfo",
            "machoNet.GetConnectedClusterName",
            "alert.BeanCount",
            "config.GetBlackListedPlanets",
            "config.GetOldStationData",
            "config.GetAverageMarketPrices",
            "air_npe.is_air_npe_enabled",
            "raffleProxy.AmIBanned",
            "invbroker.GetItemDescriptor",
            "map.GetSecurityModifiedSystems",
            "userSvc.GetRedeemTokens",
            "subscriptionMgr.GetCloneGrade",
            "loginCampaignManager.get_client_campaign_state",
            "seasonalLoginCampaignManager.get_active_campaign",
            "charUnboundMgr.GetCharOmegaDowngradeStatus",
            "charUnboundMgr.GetCharacterLockType",
            "eventLog.LogPlayerRequestedDisconnect",
            "objectCaching.GetCachableObject",
        ];

        StartupResponse[] responses = routes
            .Select(route => Assert.IsType<StartupResponse>(
                Build3396210StartupProfile.CreateResponse(cursor, route, new PyTuple())))
            .ToArray();

        Assert.IsType<PyDictionary>(responses[0].Value);
        Assert.IsType<PyText>(responses[1].Value);
        Assert.IsType<PyTuple>(responses[2].Value);
        Assert.IsType<PyList>(responses[3].Value);
        Assert.IsType<PyList>(responses[4].Value);
        Assert.IsType<PyDictionary>(responses[5].Value);
        Assert.IsType<PyBoolean>(responses[6].Value);
        Assert.IsType<PyBoolean>(responses[7].Value);
        Assert.IsType<PyExtendedObject>(responses[8].Value);
        Assert.IsType<PyExtendedObject>(responses[9].Value);
        Assert.IsType<PyExtendedObject>(responses[10].Value);
        Assert.IsType<PyInteger>(responses[11].Value);
        Assert.IsType<PyDictionary>(responses[12].Value);
        Assert.IsType<PyList>(responses[13].Value);
        Assert.IsType<PyBoolean>(responses[14].Value);
        Assert.All(responses[15..], response => Assert.IsType<PyNull>(response.Value));
        Assert.Null(Build3396210StartupProfile.CreateResponse(
            cursor,
            "unsupported.Route",
            new PyTuple()));
    }
}
