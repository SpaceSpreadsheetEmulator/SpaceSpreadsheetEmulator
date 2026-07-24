using System.Buffers;
using System.Text;
using SpaceSpreadsheetEmulator.Gateway.IntegrationTests.Support;
using SpaceSpreadsheetEmulator.Protocol;
using SpaceSpreadsheetEmulator.Protocol.Codec;
using SpaceSpreadsheetEmulator.Protocol.Profiles;
using SpaceSpreadsheetEmulator.Protocol.Values;

namespace SpaceSpreadsheetEmulator.Gateway.IntegrationTests.Connections;

public sealed class SkillBootstrapTests
{
    [Theory]
    [InlineData("GetSkills")]
    [InlineData("GetBoosters")]
    public async Task SelectedCharacterCanBindSkillHandlerWithObservedInitialMethod(
        string initialMethod)
    {
        await using GatewayHostHarness gateway = await GatewayHostHarness.StartAsync(1);
        using var client = new ProtocolLoopbackClient(await LoopbackClient.ConnectAsync(gateway.Endpoint));
        await client.CompleteHandshakeAsync();
        await SelectCharacterAsync(client);

        PyTuple binding = Assert.IsType<PyTuple>(await client.CallAsync(
            "skillMgr2",
            "MachoBindObject",
            new PyTuple(
                new PyInteger(90_000_007),
                new PyTuple(
                    new PyBuffer(Encoding.ASCII.GetBytes(initialMethod)),
                    new PyTuple(),
                    new PyDictionary()))));

        Assert.Equal(2, binding.Items.Length);
        Assert.StartsWith("N=1:", LeaseBinding(binding.Items[0]), StringComparison.Ordinal);
        Assert.Empty(Assert.IsType<PyDictionary>(binding.Items[1]).Entries);
    }

    [Fact]
    public async Task SkillHandlerBindRejectsUnknownInitialMethod()
    {
        await using GatewayHostHarness gateway = await GatewayHostHarness.StartAsync(1);
        using var client = new ProtocolLoopbackClient(await LoopbackClient.ConnectAsync(gateway.Endpoint));
        await client.CompleteHandshakeAsync();
        await SelectCharacterAsync(client);

        PyValue result = await client.CallAsync(
            "skillMgr2",
            "MachoBindObject",
            new PyTuple(
                new PyInteger(90_000_007),
                new PyTuple(
                    new PyBuffer("UnknownMethod"u8),
                    new PyTuple(),
                    new PyDictionary())));

        Assert.IsType<PyNull>(result);
    }

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

    private static string LeaseBinding(PyValue value)
    {
        PySubstream substream = Assert.IsType<PySubstream>(
            Assert.IsType<PySubstructure>(value).Value);
        DecodeResult<PyValue> decoded = BlueMarshalCodec.Decode(
            new ReadOnlySequence<byte>(substream.Data.AsMemory()),
            ProtocolProfileCatalog.GetRequired(3_396_210));
        Assert.True(decoded.IsSuccess, decoded.Error?.ToString());
        PyValue binding = Assert.IsType<PyTuple>(decoded.Value).Items[0];
        return binding switch
        {
            PyText text => text.Value,
            PyBuffer buffer => Encoding.UTF8.GetString(buffer.Value.AsSpan()),
            _ => throw new Xunit.Sdk.XunitException(
                $"Expected a text or buffer binding, received {binding.GetType().Name}."),
        };
    }
}
