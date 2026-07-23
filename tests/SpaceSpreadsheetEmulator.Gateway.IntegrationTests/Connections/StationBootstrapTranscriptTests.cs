using System.Numerics;
using SpaceSpreadsheetEmulator.Gateway.IntegrationTests.Support;
using SpaceSpreadsheetEmulator.Protocol.Values;

namespace SpaceSpreadsheetEmulator.Gateway.IntegrationTests.Connections;

public sealed class StationBootstrapTranscriptTests
{
    [Fact]
    public async Task PostSelectionRoutesThroughStationEntryKeepObservedOuterShapes()
    {
        await using GatewayHostHarness gateway = await GatewayHostHarness.StartAsync(1);
        using var client = new ProtocolLoopbackClient(await LoopbackClient.ConnectAsync(gateway.Endpoint));
        await client.CompleteHandshakeAsync();
        await SelectCharacterAsync(client);

        Assert.IsType<PyExtendedObject>(await Call(client, "officeManager", "GetMyCorporationsOffices"));
        Assert.Equal(2, Assert.IsType<PyTuple>(
            await Call(client, "agentMgr", "GetMyJournalDetails")).Items.Length);
        Assert.IsType<PyExtendedObject>(await Call(client, "standingMgr", "GetCorpStandings"));
        Assert.Equal(3, Assert.IsType<PyTuple>(
            await client.CallAsync(
                "jumpTimers",
                "GetTimers",
                new PyTuple(new PyInteger(90_000_007)))).Items.Length);
        Assert.Equal(3, Assert.IsType<PyTuple>(
            await Call(client, "accessGroupBookmarkMgr", "GetMyActiveBookmarks")).Items.Length);
        Assert.Equal(0, Assert.IsType<PyInteger>(
            await Call(
                client,
                "nonDiminishingInjectionMgr",
                "GetAvailableNonDiminishingInjections")).Value);
        Assert.IsType<PyText>(await Call(client, "XmppChatMgr", "Hostname"));

        PyObject settings = Assert.IsType<PyObject>(
            await Call(client, "charMgr", "GetCharacterSettings"));
        Assert.IsType<PyDictionary>(settings.State);
        Assert.IsType<PyBoolean>(
            await client.CallAsync(
                "operationsManager",
                "can_character_play_the_tutorial",
                new PyTuple(new PyInteger(90_000_007))));
        Assert.IsType<PyList>(await client.CallAsync(
            "structureDirectory",
            "GetMyDockableStructures",
            new PyTuple(new PyInteger(30_002_780))));
        Assert.IsType<PyExtendedObject>(
            await Call(client, "securityMgr", "get_modified_systems"));
        Assert.Equal(2, Assert.IsType<PyTuple>(
            await Call(client, "fwWarzoneSolarsystem", "GetLocalOccupationState")).Items.Length);
        Assert.IsType<PyDictionary>(
            await Call(
                client,
                "achievementTrackerMgr",
                "GetCompletedAchievementsAndClientEventCount"));
        Assert.Equal(3, Assert.IsType<PyTuple>(
            await Call(client, "fighterMgr", "GetFightersForShip")).Items.Length);
        Assert.Equal(2, Assert.IsType<PyTuple>(
            await Call(client, "charMgr", "GetSettingsInfo")).Items.Length);
        Assert.IsType<PyBoolean>(
            await Call(client, "ProjectDiscovery", "initialize_tutorial_status"));

        Assert.IsType<PyObject>(await Call(client, "charMgr", "GetContactList"));
        Assert.IsType<PyExtendedObject>(await client.CallAsync(
            service: null,
            "GetCorporateContacts",
            new PyTuple(),
            boundObject: "N=1:contacts"));
        Assert.IsType<PyExtendedObject>(await Call(client, "onlineStatus", "GetInitialState"));
        Assert.IsType<PyNull>(await client.CallAsync(
            "charMgr",
            "LogSettings",
            new PyTuple(new PyDictionary())));
        Assert.Equal(2, Assert.IsType<PyTuple>(
            await Call(client, "dynamicBountyMgr", "GetOutputForClientSolarSystem")).Items.Length);
        Assert.Single(Assert.IsType<PyList>(await Call(client, "station", "GetGuests")).Items);
        Assert.IsType<PyText>(
            await Call(client, "chatAuthenticationService", "GetAuthenticationToken"));
        Assert.Equal(2, Assert.IsType<PyTuple>(
            await client.CallAsync(
                "config",
                "GetMultiLocationsEx",
                new PyTuple(new PyList(new PyInteger(190_000_007))))).Items.Length);
        Assert.Equal(2, Assert.IsType<PyList>(
            await Call(client, "XmppChatMgr", "ResyncSystemChannelAccess")).Items.Length);
        Assert.IsType<PyBuffer>(
            await Call(client, "browserLockdownSvc", "GetFlaggedSitesHash"));

        Assert.IsType<PyExtendedObject>(await client.CallAsync(
            service: null,
            "GetMyApplications",
            new PyTuple(),
            boundObject: "N=1:applications"));
        Assert.Equal(2, Assert.IsType<PyTuple>(await client.CallAsync(
            service: null,
            "GetSkillQueueAndFreePoints",
            new PyTuple(),
            boundObject: "N=1:skills")).Items.Length);
        Assert.IsType<PyDictionary>(await Call(
            client,
            "userSvc",
            "GetMultiCharactersTrainingSlots"));
        Assert.IsType<PyObject>(await Call(client, "contractProxy", "GetLoginInfo"));
        Assert.IsType<PyExtendedObject>(await client.CallAsync(
            service: null,
            "GetSkillHistory",
            new PyTuple(new PyInteger(10)),
            boundObject: "N=1:skills"));
        Assert.IsType<PyDictionary>(await client.CallAsync(
            service: null,
            "GetSkills",
            new PyTuple(),
            boundObject: "N=1:skills"));
        Assert.IsType<PyExtendedObject>(
            await Call(client, "notificationMgr", "GetAllNotifications"));
        Assert.IsType<PyDictionary>(await client.CallAsync(
            service: null,
            "GetAttributes",
            new PyTuple(),
            boundObject: "N=1:skills"));
        Assert.IsType<PyList>(
            await Call(client, "insurgencySolarsystem", "GetAllVisibleCampaigns"));
        Assert.Equal(3, Assert.IsType<PyInteger>(
            await Call(client, "air_npe", "get_air_npe_state")).Value);

        var stationObject = new PyTuple(new PyInteger(60_000_004), new PyInteger(15));
        PyTuple shipBinding = Assert.IsType<PyTuple>(await client.CallAsync(
            "ship",
            "MachoBindObject",
            new PyTuple(
                stationObject,
                new PyTuple(
                    new PyBuffer("GetDirtTimestamp"u8),
                    new PyTuple(new PyBigInteger(new BigInteger(190_000_007))),
                    new PyDictionary()))));
        Assert.Equal(2, shipBinding.Items.Length);
        Assert.IsType<PyBigInteger>(shipBinding.Items[1]);

        Assert.IsType<PyInteger>(await client.CallAsync(
            "charMgr",
            "MachoResolveObject",
            new PyTuple(new PyTuple(new PyInteger(90_000_007), new PyInteger(10_002)))));
        PyTuple stations = Assert.IsType<PyTuple>(await client.CallAsync(
            "charMgr",
            "MachoBindObject",
            new PyTuple(
                new PyTuple(new PyInteger(90_000_007), new PyInteger(10_002)),
                new PyTuple(new PyBuffer("ListStations"u8), new PyTuple(), new PyDictionary()))));
        Assert.IsType<PyExtendedObject>(stations.Items[1]);
        Assert.IsType<PyObject>(await client.CallAsync(
            "charMgr",
            "GetPublicInfo",
            new PyTuple(new PyInteger(90_000_007))));
    }

    private static Task<PyValue> Call(
        ProtocolLoopbackClient client,
        string service,
        string method)
        => client.CallAsync(service, method, new PyTuple());

    private static async Task SelectCharacterAsync(ProtocolLoopbackClient client)
    {
        Assert.IsType<PyList>(await client.CallAsync(
            "charUnboundMgr",
            "GetCharacterSelectionData",
            new PyTuple()));
        long callId = await client.WriteCallAsync(
            "charUnboundMgr",
            "SelectCharacterID",
            new PyTuple(
                new PyInteger(90_000_007),
                PyNull.Instance,
                new PyBoolean(false)));
        Assert.Equal(16, (await client.ReadPacketAsync()).NumericType);
        Assert.IsType<PyNull>(await client.ReadCallResponseAsync(callId));
    }
}
