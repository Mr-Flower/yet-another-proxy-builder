using MTGProxyBuilder.Core.Services;

namespace MTGProxyBuilder.Tests.Services;

public class MpcFillXmlImportTests
{
    // ================================================================
    //  CleanCardName tests
    // ================================================================

    [Fact]
    public void CleanCardName_PrefersQuery()
    {
        var card = new MpcFillXmlCard { Query = "lightning bolt", Name = "Lightning Bolt (LEA 161).png" };
        Assert.Equal("lightning bolt", MpcFillXmlImportService.CleanCardName(card));
    }

    [Fact]
    public void CleanCardName_FallsBackToName_WhenQueryEmpty()
    {
        var card = new MpcFillXmlCard { Query = "", Name = "Lightning Bolt.png" };
        Assert.Equal("Lightning Bolt", MpcFillXmlImportService.CleanCardName(card));
    }

    [Fact]
    public void CleanCardName_StripsTokenPrefix()
    {
        var card = new MpcFillXmlCard { Query = "t:goblin" };
        Assert.Equal("goblin", MpcFillXmlImportService.CleanCardName(card));
    }

    [Fact]
    public void CleanCardName_StripsBackPrefix()
    {
        var card = new MpcFillXmlCard { Query = "b:some back" };
        Assert.Equal("some back", MpcFillXmlImportService.CleanCardName(card));
    }

    [Fact]
    public void CleanCardName_StripsFileExtension()
    {
        var card = new MpcFillXmlCard { Query = "", Name = "Sol Ring.jpg" };
        Assert.Equal("Sol Ring", MpcFillXmlImportService.CleanCardName(card));
    }

    [Fact]
    public void CleanCardName_StripsParenthetical()
    {
        var card = new MpcFillXmlCard { Query = "", Name = "Island (Unsanctioned).png" };
        Assert.Equal("Island", MpcFillXmlImportService.CleanCardName(card));
    }

    [Fact]
    public void CleanCardName_TrimsWhitespace()
    {
        var card = new MpcFillXmlCard { Query = "  mountain  " };
        Assert.Equal("mountain", MpcFillXmlImportService.CleanCardName(card));
    }

    [Fact]
    public void CleanCardName_EmptyBoth_ReturnsEmpty()
    {
        var card = new MpcFillXmlCard { Query = "", Name = "" };
        Assert.Equal("", MpcFillXmlImportService.CleanCardName(card));
    }

    [Fact]
    public void CleanCardName_NoExtensionNoParenthetical()
    {
        var card = new MpcFillXmlCard { Query = "black lotus" };
        Assert.Equal("black lotus", MpcFillXmlImportService.CleanCardName(card));
    }

    [Fact]
    public void CleanCardName_QueryWithExtension()
    {
        var card = new MpcFillXmlCard { Query = "forest.png" };
        Assert.Equal("forest", MpcFillXmlImportService.CleanCardName(card));
    }

    // ================================================================
    //  ParseXml tests
    // ================================================================

    private string WriteTempXml(string xml)
    {
        var path = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.xml");
        File.WriteAllText(path, xml);
        return path;
    }

    [Fact]
    public void ParseXml_ValidComplete()
    {
        var svc = new MpcFillXmlImportService(null!, null!); // services not needed for parsing
        string xml = @"<order>
            <details><quantity>10</quantity><stock>(S30) Standard Smooth</stock><foil>false</foil></details>
            <fronts>
                <card><id>abc123</id><name>Lightning Bolt.png</name><query>lightning bolt</query><slots>0,1,2</slots></card>
                <card><id>def456</id><name>Sol Ring.png</name><query>sol ring</query><slots>3</slots></card>
            </fronts>
            <backs>
                <card><id>back1</id><name>Back.png</name><query>b:back</query><slots>3</slots></card>
            </backs>
            <cardback>common_back_id</cardback>
        </order>";
        var path = WriteTempXml(xml);

        try
        {
            var (project, error) = svc.ParseXml(path);
            Assert.Null(error);
            Assert.NotNull(project);
            Assert.Equal(10, project!.Quantity);
            Assert.Equal("(S30) Standard Smooth", project.Stock);
            Assert.False(project.Foil);
            Assert.Equal(2, project.Fronts.Count);
            Assert.Equal("abc123", project.Fronts[0].Id);
            Assert.Equal(3, project.Fronts[0].Slots.Count); // slots 0,1,2
            Assert.Single(project.Fronts[1].Slots); // slot 3
            Assert.Single(project.Backs);
            Assert.Equal("common_back_id", project.CommonCardbackId);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ParseXml_MissingBacks_Okay()
    {
        var svc = new MpcFillXmlImportService(null!, null!);
        string xml = @"<order>
            <details><quantity>1</quantity><stock>S30</stock><foil>false</foil></details>
            <fronts><card><id>abc</id><slots>0</slots></card></fronts>
            <cardback>cb</cardback>
        </order>";
        var path = WriteTempXml(xml);

        try
        {
            var (project, error) = svc.ParseXml(path);
            Assert.Null(error);
            Assert.NotNull(project);
            Assert.Empty(project!.Backs);
            Assert.Equal("cb", project.CommonCardbackId);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ParseXml_InvalidRoot_ReturnsError()
    {
        var svc = new MpcFillXmlImportService(null!, null!);
        var path = WriteTempXml("<deck><card/></deck>");

        try
        {
            var (project, error) = svc.ParseXml(path);
            Assert.Null(project);
            Assert.NotNull(error);
            Assert.Contains("root element", error!);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ParseXml_MalformedXml_ReturnsError()
    {
        var svc = new MpcFillXmlImportService(null!, null!);
        var path = WriteTempXml("this is not xml <><>");

        try
        {
            var (project, error) = svc.ParseXml(path);
            Assert.Null(project);
            Assert.NotNull(error);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ParseXml_EmptyFronts()
    {
        var svc = new MpcFillXmlImportService(null!, null!);
        var path = WriteTempXml("<order><fronts/></order>");

        try
        {
            var (project, error) = svc.ParseXml(path);
            Assert.Null(error);
            Assert.NotNull(project);
            Assert.Empty(project!.Fronts);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ParseXml_FoilTrue()
    {
        var svc = new MpcFillXmlImportService(null!, null!);
        var path = WriteTempXml("<order><details><quantity>1</quantity><stock>S30</stock><foil>true</foil></details></order>");

        try
        {
            var (project, _) = svc.ParseXml(path);
            Assert.True(project!.Foil);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ParseXml_InvalidQuantity_DefaultsToZero()
    {
        var svc = new MpcFillXmlImportService(null!, null!);
        var path = WriteTempXml("<order><details><quantity>notanumber</quantity></details></order>");

        try
        {
            var (project, _) = svc.ParseXml(path);
            Assert.Equal(0, project!.Quantity);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ParseXml_CardWithMissingFields_Defaults()
    {
        var svc = new MpcFillXmlImportService(null!, null!);
        var path = WriteTempXml("<order><fronts><card><id>x</id></card></fronts></order>");

        try
        {
            var (project, _) = svc.ParseXml(path);
            Assert.Single(project!.Fronts);
            var card = project.Fronts[0];
            Assert.Equal("x", card.Id);
            Assert.Equal(string.Empty, card.Name);
            Assert.Equal(string.Empty, card.Query);
            Assert.Equal("Google Drive", card.SourceType);
            Assert.Empty(card.Slots);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ParseXml_NonexistentFile_ReturnsError()
    {
        var svc = new MpcFillXmlImportService(null!, null!);
        var (project, error) = svc.ParseXml("/nonexistent/file.xml");
        Assert.Null(project);
        Assert.NotNull(error);
    }
}
