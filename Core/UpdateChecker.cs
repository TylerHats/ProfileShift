using System;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace ProfileShift.Core
{
    public class ReleaseInfo
    {
        public string TagName { get; set; } = string.Empty;
        public string HtmlUrl { get; set; } = string.Empty;
        public string DownloadUrl { get; set; } = string.Empty;
        public bool IsNewer { get; set; }
        public string CurrentVersion { get; set; } = string.Empty;
    }

    public static class UpdateChecker
    {
        private static readonly HttpClient client = new HttpClient();
        private const string GitHubApiUrl = "https://api.github.com/repos/TylerHats/ProfileShift/releases/latest";

        public static async Task<ReleaseInfo?> CheckForUpdatesAsync()
        {
            try
            {
                Version currentVer = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 2, 0);
                string currentTag = $"v{currentVer.Major}.{currentVer.Minor}.{currentVer.Build}";

                client.DefaultRequestHeaders.UserAgent.ParseAdd("ProfileShift-UpdateChecker");
                var response = await client.GetAsync(GitHubApiUrl);

                if (response.IsSuccessStatusCode)
                {
                    string json = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    string tag = root.GetProperty("tag_name").GetString() ?? "";
                    string htmlUrl = root.GetProperty("html_url").GetString() ?? "";
                    string downloadUrl = string.Empty;

                    if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var asset in assets.EnumerateArray())
                        {
                            string assetName = asset.GetProperty("name").GetString() ?? "";
                            if (assetName.Equals("ProfileShift.exe", StringComparison.OrdinalIgnoreCase))
                            {
                                downloadUrl = asset.GetProperty("browser_download_url").GetString() ?? "";
                                break;
                            }
                        }
                    }

                    bool isNewer = false;
                    string rawTag = tag.TrimStart('v', 'V');
                    if (Version.TryParse(rawTag, out var latestVer))
                    {
                        isNewer = latestVer > currentVer;
                    }

                    return new ReleaseInfo
                    {
                        TagName = tag,
                        HtmlUrl = htmlUrl,
                        DownloadUrl = downloadUrl,
                        IsNewer = isNewer,
                        CurrentVersion = currentTag
                    };
                }
            }
            catch { }

            return null;
        }
    }
}
