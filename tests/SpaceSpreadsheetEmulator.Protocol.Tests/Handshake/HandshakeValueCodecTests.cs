using System.Collections.Immutable;
using SpaceSpreadsheetEmulator.Protocol;
using SpaceSpreadsheetEmulator.Protocol.Handshake;
using SpaceSpreadsheetEmulator.Protocol.Profiles;
using SpaceSpreadsheetEmulator.Protocol.Values;

namespace SpaceSpreadsheetEmulator.Protocol.Tests.Handshake;

public class HandshakeValueCodecTests
{
    private static readonly ProtocolProfile Profile = ProtocolProfileCatalog.GetRequired(3396210);

    [Fact]
    public void Build3396210VersionTupleIsAccepted()
    {
        PyValue value = HandshakeValueCodec.EncodeServerVersion(Profile);

        DecodeResult<ClientVersionExchange> decoded = HandshakeValueCodec.DecodeClientVersion(value, Profile);

        Assert.True(decoded.IsSuccess, decoded.Error?.ToString());
        Assert.Equal(3396210, decoded.Value!.ClientBuild);
        Assert.Equal("V24.01@ccp", decoded.Value.ProjectVersion);
    }

    [Fact]
    public void MismatchedBuildIsRejectedBeforeSessionCreation()
    {
        var value = new PyTuple(
            new PyInteger(170472),
            new PyInteger(496),
            new PyInteger(0),
            new PyFloat(24.01),
            new PyInteger(3441022),
            new PyText("V24.01@ccp"));

        DecodeResult<ClientVersionExchange> decoded = HandshakeValueCodec.DecodeClientVersion(value, Profile);

        Assert.False(decoded.IsSuccess);
        Assert.Equal(ProtocolErrorCodes.IncompatibleBuild, decoded.Error!.Code);
        Assert.Equal("$handshake.version", decoded.Error.ValuePath);
    }

    [Fact]
    public void CryptoRequestRequiresExactKeyAndIvSizes()
    {
        var value = new PyTuple(
            new PyText("placebo"),
            new PyDictionary(
                new PyDictionaryEntry(new PyText("crypting_sessionkey"), new PyBuffer(ImmutableArray.Create<byte>(1, 2))),
                new PyDictionaryEntry(new PyText("crypting_sessioniv"), new PyBuffer(ImmutableArray.Create<byte>(3, 4)))));

        DecodeResult<CryptoRequest> decoded = HandshakeValueCodec.DecodeCryptoRequest(value);

        Assert.False(decoded.IsSuccess);
        Assert.Equal(ProtocolErrorCodes.InvalidHandshake, decoded.Error!.Code);
    }

    [Fact]
    public void EmptyPlaceboRequestSelectsPlaintextContinuation()
    {
        var value = new PyTuple(new PyText("placebo"), new PyDictionary());

        DecodeResult<CryptoRequest> decoded = HandshakeValueCodec.DecodeCryptoRequest(value);

        Assert.True(decoded.IsSuccess, decoded.Error?.ToString());
        Assert.Empty(decoded.Value!.SessionKey);
        Assert.Empty(decoded.Value.SessionInitializationVector);
    }

    [Fact]
    public void ObservedPlaceboContainersExposeTheirAesPrefixes()
    {
        byte[] keyContainer = Enumerable.Range(0, 512).Select(value => (byte)value).ToArray();
        byte[] ivContainer = Enumerable.Range(64, 512).Select(value => (byte)value).ToArray();
        var value = new PyTuple(
            new PyText("placebo"),
            new PyDictionary(
                new PyDictionaryEntry(new PyText("crypting_sessionkey"), new PyBuffer(keyContainer)),
                new PyDictionaryEntry(new PyText("crypting_sessioniv"), new PyBuffer(ivContainer))));

        DecodeResult<CryptoRequest> decoded = HandshakeValueCodec.DecodeCryptoRequest(value);

        Assert.True(decoded.IsSuccess, decoded.Error?.ToString());
        Assert.Equal(keyContainer[..32], decoded.Value!.SessionKey);
        Assert.Equal(ivContainer[..16], decoded.Value.SessionInitializationVector);
    }

    [Fact]
    public void ServerHandshakeCarriesAnIndependentlyConstructedNoOpExpression()
    {
        PyTuple handshake = Assert.IsType<PyTuple>(
            HandshakeValueCodec.EncodeCryptoServerHandshake(Profile, 1, 1_000_007));
        PyTuple signedFunction = Assert.IsType<PyTuple>(handshake.Items[1]);
        PyBuffer payload = Assert.IsType<PyBuffer>(signedFunction.Items[0]);

        Assert.Equal("74040000004E6F6E65", Convert.ToHexString(payload.Value.AsSpan()));
        Assert.False(Assert.IsType<PyBoolean>(signedFunction.Items[1]).Value);
    }
}
