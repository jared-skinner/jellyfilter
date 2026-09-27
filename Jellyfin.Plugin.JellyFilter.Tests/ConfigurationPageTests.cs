namespace Jellyfin.Plugin.JellyFilter.Tests;

public class ConfigurationPageTests
{
    /// <summary>
    /// The dashboard page is located by name at runtime, so a rename or a dropped
    /// <c>EmbeddedResource</c> entry would only show up as a blank settings page on a live server.
    /// </summary>
    [Fact]
    public void The_dashboard_page_is_embedded_under_the_name_the_plugin_asks_for()
    {
        var resources = typeof(Plugin).Assembly.GetManifestResourceNames();

        Assert.Contains("Jellyfin.Plugin.JellyFilter.Configuration.configPage.html", resources);
    }
}
