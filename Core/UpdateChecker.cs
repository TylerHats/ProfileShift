using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace ProfileShift.Core
{
    public class ReleaseInfo
    {
        public string TagName { get; set; } = string.Empty;
        public string HtmlUrl { get; set; } = string.Empty;
        public bool IsNewer { get; set; }
    }

    public static class UpdateChecker
    {
        private static readonly HttpClient client = new HttpClient();
        private const string CurrentVersion = "v2.5.0";
        private const string GitHubApiUrl = "https://api.github.com/repos/TylerHats/ProfileShift/releases/latest";

        public static async Task<ReleaseInfo?> CheckForUpdatesAsync()
        {
            try
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("ProfileShift-UpdateChecker");
                var response = await client.GetAsync(GitHubApiUrl);

                if (response.IsSuccessStatusCode)
                {
                    string json = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    string tag = root.GetProperty("tag_name").GetString() ?? "";
                    string url = root.GetProperty("html_url").GetString() ?? "";

                    return new ReleaseInfo
                    {
                        TagName = tag,
                        HtmlUrl = url,
                        IsNewer = !string.Equals(tag, CurrentVersion, StringComparison.OrdinalIgnoreCase)
                    };
                }
            }
            catch { }

            return null;
        }
    }
}
