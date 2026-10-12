using KommasKorrektus.Data.Entities;
using KommasKorrektus.Services;

namespace KommasKorrektus.Tests.Web;

public class VersionNamesTests
{
    [Fact]
    public void The_passes_of_a_round_are_numbered_and_a_file_of_the_customer_begins_the_next_round()
    {
        var names = VersionNames.Of(
        [
            (1, VersionOrigin.Import, "Original"),
            (2, VersionOrigin.Manual, null),
            (3, VersionOrigin.Manual, null),
            (4, VersionOrigin.Working, null),
            (5, VersionOrigin.Upload, "Datei vom Kunden v2"),
            (6, VersionOrigin.Manual, null),
            (7, VersionOrigin.Working, null),
        ]);

        Assert.Equal(new VersionName("Original", 1, 0), names[1]);
        Assert.Equal(new VersionName("Änderung 1", 1, 1), names[2]);
        Assert.Equal(new VersionName("Änderung 2", 1, 2), names[3]);
        Assert.Equal(new VersionName("Änderung 3", 1, 3), names[4]);
        Assert.Equal(new VersionName("Datei vom Kunden v2", 2, 0), names[5]);
        // the count of the passes starts again in the new round
        Assert.Equal(new VersionName("Änderung 1", 2, 1), names[6]);
        Assert.Equal(new VersionName("Änderung 2", 2, 2), names[7]);
        Assert.Equal("Fassung 6 – Änderung 1", names[6].Option(6));
    }

    [Fact]
    public void A_name_somebody_gave_is_kept_and_does_not_count_as_a_number()
    {
        var names = VersionNames.Of(
        [
            (1, VersionOrigin.Import, "Original"),
            (2, VersionOrigin.Manual, "Erster Durchgang"),
            (3, VersionOrigin.Manual, null),
            (4, VersionOrigin.Restore, "Zurück auf Fassung 2"),
            (5, VersionOrigin.Upload, null),
        ]);

        Assert.Equal("Erster Durchgang", names[2].Title);
        // a named pass still is the first of the round: the next one is the second
        Assert.Equal("Änderung 2", names[3].Title);
        Assert.Equal("Zurück auf Fassung 2", names[4].Title);
        Assert.Equal("Datei vom Kunden", names[5].Title);
        Assert.Equal(2, names[5].Round);
    }

    [Fact]
    public void A_document_without_a_file_of_the_customer_has_one_round()
    {
        var names = VersionNames.Of([(1, VersionOrigin.Import, null), (2, VersionOrigin.Working, null)]);

        Assert.Equal("Original", names[1].Title);
        Assert.All(names.Values, n => Assert.Equal(1, n.Round));
    }
}
