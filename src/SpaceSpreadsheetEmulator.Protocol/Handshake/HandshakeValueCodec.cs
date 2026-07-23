using System.Collections.Immutable;
using System.Buffers.Binary;
using System.Text;
using SpaceSpreadsheetEmulator.Protocol.Crypto;
using SpaceSpreadsheetEmulator.Protocol.Profiles;
using SpaceSpreadsheetEmulator.Protocol.Values;

namespace SpaceSpreadsheetEmulator.Protocol.Handshake;

/// <summary>
/// Maps build-specific handshake messages between typed models and protocol values.
/// </summary>
public static class HandshakeValueCodec
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static PyValue EncodeServerVersion(ProtocolProfile profile, int userCount = 0)
    {
        HandshakeProfile handshake = profile.Handshake;
        return new PyTuple(
            new PyInteger(handshake.EveBirthday),
            new PyInteger(handshake.MachoVersion),
            new PyInteger(userCount),
            new PyFloat(handshake.ClientVersion),
            new PyInteger(handshake.ClientBuild),
            new PyText(handshake.ProjectVersion),
            PyNull.Instance);
    }

    public static DecodeResult<ClientVersionExchange> DecodeClientVersion(PyValue value, ProtocolProfile profile)
    {
        if (value is not PyTuple tuple
            || tuple.Items.Length < 6
            || !TryInt32(tuple.Items[0], out int birthday)
            || !TryInt32(tuple.Items[1], out int machoVersion)
            || !TryDouble(tuple.Items[3], out double clientVersion)
            || !TryInt32(tuple.Items[4], out int build)
            || !TryString(tuple.Items[5], out string? projectVersion))
        {
            return Failure<ClientVersionExchange>("The client version exchange has an invalid shape.", "$handshake.version");
        }

        HandshakeProfile expected = profile.Handshake;
        if (birthday != expected.EveBirthday
            || machoVersion != expected.MachoVersion
            || Math.Abs(clientVersion - expected.ClientVersion) > 0.000_001
            || build != expected.ClientBuild
            || !string.Equals(projectVersion, expected.ProjectVersion, StringComparison.Ordinal))
        {
            return DecodeResult<ClientVersionExchange>.Failure(new ProtocolError(
                ProtocolErrorCodes.IncompatibleBuild,
                0,
                "$handshake.version",
                "The client compatibility tuple does not match profile 3396210."));
        }

        return DecodeResult<ClientVersionExchange>.Success(new ClientVersionExchange(
            birthday,
            machoVersion,
            clientVersion,
            build,
            projectVersion!));
    }

    public static DecodeResult<HandshakeCommand> DecodeCommand(PyValue value)
    {
        if (value is not PyTuple tuple || tuple.Items.Length is < 2 or > 3 || !TryString(tuple.Items[1], out string? command))
        {
            return Failure<HandshakeCommand>("The handshake command has an invalid shape.", "$handshake.command");
        }

        if (tuple.Items.Length == 2 && command == "QC")
        {
            return DecodeResult<HandshakeCommand>.Success(new HandshakeCommand(HandshakeCommandKind.QueueCheck, []));
        }

        if (tuple.Items.Length == 3 && command == "VK" && TryBytes(tuple.Items[2], out ImmutableArray<byte> key))
        {
            return DecodeResult<HandshakeCommand>.Success(new HandshakeCommand(HandshakeCommandKind.VersionKey, key));
        }

        return Failure<HandshakeCommand>("The handshake command is not supported.", "$handshake.command");
    }

    public static DecodeResult<CryptoRequest> DecodeCryptoRequest(PyValue value)
    {
        if (value is not PyTuple { Items.Length: 2 } tuple
            || !TryString(tuple.Items[0], out string? keyVersion)
            || tuple.Items[1] is not PyDictionary parameters)
        {
            return Failure<CryptoRequest>(
                "The crypto request does not contain a key-version and parameter dictionary.",
                "$handshake.crypto");
        }

        if (keyVersion == "placebo" && parameters.Entries.IsEmpty)
        {
            return DecodeResult<CryptoRequest>.Success(new CryptoRequest(keyVersion, [], []));
        }

        if (!TryDictionaryValue(parameters, "crypting_sessionkey", out PyValue? keyValue)
            || !TryDictionaryValue(parameters, "crypting_sessioniv", out PyValue? ivValue))
        {
            return Failure<CryptoRequest>(
                "A non-empty crypto request does not contain both placebo key and IV entries.",
                "$handshake.crypto");
        }

        if (!TryBytes(keyValue!, out ImmutableArray<byte> key)
            || !TryBytes(ivValue!, out ImmutableArray<byte> iv))
        {
            return Failure<CryptoRequest>("The placebo key and IV entries must be byte buffers.", "$handshake.crypto");
        }

        if (key.Length is not (AesCbcFrameCipher.KeyLength or 256 or 512)
            || iv.Length is not (AesCbcFrameCipher.BlockLength or 256 or 512))
        {
            return Failure<CryptoRequest>(
                $"The placebo key and IV container lengths {key.Length}/{iv.Length} are not supported.",
                "$handshake.crypto");
        }

        return DecodeResult<CryptoRequest>.Success(new CryptoRequest(
            keyVersion!,
            key[..AesCbcFrameCipher.KeyLength],
            iv[..AesCbcFrameCipher.BlockLength]));
    }

    public static DecodeResult<LoginCredentials> DecodeLoginCredentials(PyValue value)
    {
        if (value is not PyTuple { Items.Length: >= 2 } tuple
            || tuple.Items[1] is not PyDictionary login
            || !TryDictionaryValue(login, "user_name", out PyValue? userNameValue)
            || !TryString(userNameValue!, out string? userName)
            || !TryDictionaryValue(login, "user_password_hash", out PyValue? proofValue)
            || !TryBytes(proofValue!, out ImmutableArray<byte> proof))
        {
            return Failure<LoginCredentials>("The encrypted login credential payload is malformed.", "$handshake.login");
        }

        string language = TryDictionaryValue(login, "user_languageid", out PyValue? languageValue)
            && TryString(languageValue!, out string? languageText)
                ? languageText!
                : "EN";
        string country = TryDictionaryValue(login, "user_countrycode", out PyValue? countryValue)
            && TryString(countryValue!, out string? countryText)
                ? countryText!
                : "BG";
        return DecodeResult<LoginCredentials>.Success(new LoginCredentials(userName!, proof, language, country));
    }

    public static PyValue EncodePasswordVersion() => new PyInteger(2);

    public static PyValue EncodeCryptoServerHandshake(ProtocolProfile profile, long proxyNodeId, long clientId)
    {
        PyDictionary serverInfo = Dictionary(
            ("challenge_responsehash", new PyText("55087")),
            ("macho_version", new PyInteger(profile.Handshake.MachoVersion)),
            ("boot_version", new PyFloat(profile.Handshake.ClientVersion)),
            ("boot_build", new PyInteger(profile.Handshake.ClientBuild)),
            ("boot_codename", new PyText("V24.01")),
            ("boot_region", new PyText("ccp")),
            ("cluster_usercount", new PyInteger(0)),
            ("proxy_nodeid", new PyInteger(proxyNodeId)),
            ("user_logonqueueposition", new PyInteger(1)),
            ("config_vals", new PyDictionary()));
        return new PyTuple(
            new PyText(string.Empty),
            new PyTuple(new PyBuffer(CreateNoOpFunctionPayload()), new PyBoolean(false)),
            new PyDictionary(),
            serverInfo);
    }

    public static PyValue EncodeSessionAcknowledgement(SessionAcknowledgement acknowledgement)
        => Dictionary(
            ("live_updates", new PyList()),
            ("session_init", Dictionary(
                ("languageID", new PyText(acknowledgement.LanguageId)),
                ("countryCode", new PyText(acknowledgement.CountryCode)),
                ("userid", new PyInteger(acknowledgement.UserId)),
                ("maxSessionTime", PyNull.Instance),
                ("userType", new PyInteger(30)),
                ("role", new PyInteger(acknowledgement.Role)),
                ("address", new PyText(acknowledgement.Address)),
                ("inDetention", PyNull.Instance))),
            ("sessionID", new PyInteger(acknowledgement.SessionId)),
            ("client_hash", PyNull.Instance),
            ("user_clientid", new PyInteger(acknowledgement.ClientId)));

    private static PyDictionary Dictionary(params (string Key, PyValue Value)[] entries)
        => new(entries.Select(entry => new PyDictionaryEntry(new PyText(entry.Key), entry.Value)).ToArray());

    private static byte[] CreateNoOpFunctionPayload()
    {
        ReadOnlySpan<byte> expression = "None"u8;
        byte[] payload = new byte[1 + sizeof(int) + expression.Length];
        payload[0] = 0x74;
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(1), expression.Length);
        expression.CopyTo(payload.AsSpan(1 + sizeof(int)));
        return payload;
    }

    private static bool TryDictionaryValue(PyDictionary dictionary, string key, out PyValue? value)
    {
        foreach (PyDictionaryEntry entry in dictionary.Entries)
        {
            if (TryString(entry.Key, out string? candidate) && candidate == key)
            {
                value = entry.Value;
                return true;
            }
        }

        value = null;
        return false;
    }

    private static bool TryString(PyValue value, out string? result)
    {
        value = Unwrap(value);
        switch (value)
        {
            case PyText text:
                result = text.Value;
                return true;
            case PyToken token:
                result = token.Value;
                return true;
            case PyStringTableReference reference:
                result = reference.Value;
                return true;
            case PyBuffer buffer:
                try
                {
                    result = StrictUtf8.GetString(buffer.Value.AsSpan());
                    return true;
                }
                catch (DecoderFallbackException)
                {
                    break;
                }
        }

        result = null;
        return false;
    }

    private static bool TryBytes(PyValue value, out ImmutableArray<byte> result)
    {
        value = Unwrap(value);
        if (value is PyBuffer buffer)
        {
            result = buffer.Value;
            return true;
        }

        result = [];
        return false;
    }

    private static bool TryInt32(PyValue value, out int result)
    {
        value = Unwrap(value);
        if (value is PyInteger integer && integer.Value is >= int.MinValue and <= int.MaxValue)
        {
            result = (int)integer.Value;
            return true;
        }

        result = 0;
        return false;
    }

    private static bool TryDouble(PyValue value, out double result)
    {
        value = Unwrap(value);
        if (value is PyFloat floating)
        {
            result = floating.Value;
            return true;
        }

        if (value is PyInteger integer)
        {
            result = integer.Value;
            return true;
        }

        result = 0;
        return false;
    }

    private static PyValue Unwrap(PyValue value)
        => value is PySavedValueReference reference ? reference.Value : value;

    private static DecodeResult<T> Failure<T>(string message, string path)
        where T : class
        => DecodeResult<T>.Failure(new ProtocolError(
            ProtocolErrorCodes.InvalidHandshake,
            0,
            path,
            message));
}
