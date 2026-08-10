using System;
using System.Collections.Generic;
using System.IO;
using UserMoveTool.Models;

namespace UserMoveTool.Core
{
    public static class BrowserDetector
    {
        public static List<BrowserOption> GetAvailableBrowsers(string userProfilePath)
        {
            var options = new List<BrowserOption>
            {
                new BrowserOption
                {
                    Name = "Google Chrome",
                    RelativePath = @"AppData\Local\Google\Chrome\User Data",
                    IsInstalled = Directory.Exists(Path.Combine(userProfilePath, @"AppData\Local\Google\Chrome\User Data"))
                },
                new BrowserOption
                {
                    Name = "Microsoft Edge",
                    RelativePath = @"AppData\Local\Microsoft\Edge\User Data",
                    IsInstalled = Directory.Exists(Path.Combine(userProfilePath, @"AppData\Local\Microsoft\Edge\User Data"))
                },
                new BrowserOption
                {
                    Name = "Mozilla Firefox",
                    RelativePath = @"AppData\Roaming\Mozilla\Firefox",
                    IsInstalled = Directory.Exists(Path.Combine(userProfilePath, @"AppData\Roaming\Mozilla\Firefox"))
                },
                new BrowserOption
                {
                    Name = "Opera",
                    RelativePath = @"AppData\Roaming\Opera Software\Opera Stable",
                    IsInstalled = Directory.Exists(Path.Combine(userProfilePath, @"AppData\Roaming\Opera Software\Opera Stable"))
                },
                new BrowserOption
                {
                    Name = "Brave Browser",
                    RelativePath = @"AppData\Local\BraveSoftware\Brave-Browser\User Data",
                    IsInstalled = Directory.Exists(Path.Combine(userProfilePath, @"AppData\Local\BraveSoftware\Brave-Browser\User Data"))
                },
                new BrowserOption
                {
                    Name = "Vivaldi",
                    RelativePath = @"AppData\Local\Vivaldi\User Data",
                    IsInstalled = Directory.Exists(Path.Combine(userProfilePath, @"AppData\Local\Vivaldi\User Data"))
                }
            };

            return options;
        }
    }
}
