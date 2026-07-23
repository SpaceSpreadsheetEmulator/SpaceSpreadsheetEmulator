using SpaceSpreadsheetEmulator.Protocol.Profiles;

namespace SpaceSpreadsheetEmulator.Protocol.Tests.Profiles;

public class ProtocolProfileCatalogTests
{
    [Fact]
    public void Build3396210LoadsIndexedStringTableFromResource()
    {
        ProtocolProfile profile = ProtocolProfileCatalog.GetRequired(3_396_210);

        Assert.Equal(196, profile.StringTable.Length);
        Assert.Equal("<reserved>", profile.StringTable[0]);
        Assert.Equal("*corpid", profile.StringTable[1]);
        Assert.Equal("macho.CallReq", profile.StringTable[46]);
        Assert.Equal("agent.StorylineMissionDetails", profile.StringTable[195]);
    }

    [Fact]
    public void UnknownBuildIsRejected()
    {
        Assert.Throws<KeyNotFoundException>(() => ProtocolProfileCatalog.GetRequired(1));
    }
}
