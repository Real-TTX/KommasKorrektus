using KommaKorrektus.Pages.Time;

namespace KommaKorrektus.Tests.Web;

public class TimeEntryTests
{
    [Theory]
    [InlineData("1:30", 5400)]
    [InlineData("0:45", 2700)]
    [InlineData("90", 5400)]
    [InlineData("90 min", 5400)]
    [InlineData("1,5", 5400)]
    [InlineData("1.5 Stunden", 5400)]
    [InlineData("2 h", 7200)]
    [InlineData("2", 7200)]
    [InlineData("45m", 2700)]
    [InlineData("1:30:30", 5430)]
    public void A_duration_can_be_written_the_way_people_write_it(string text, int seconds) => Assert.Equal(seconds, EditModel.ParseDuration(text));

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("1:75")]
    [InlineData("-5")]
    [InlineData("99999")]
    public void Nonsense_is_not_a_duration(string text) => Assert.Null(EditModel.ParseDuration(text));
}
