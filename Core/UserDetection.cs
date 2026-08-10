using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ProfileShift.Core
{
    public class UserProfile
    {
        public string Username { get; set; } = string.Empty;
        public string ProfilePath { get; set; } = string.Empty;
        public bool IsSelected { get; set; }
    }

    public static class UserDetection
    {
        private static readonly string[] SystemUsers = new[]
        {
            "Public", "Default", "Default User", "All Users", "desktop.ini"
        };

        public static List<UserProfile> GetLocalUserProfiles()
        {
            var profiles = new List<UserProfile>();
            string usersDir = @"C:\Users";

            if (Directory.Exists(usersDir))
            {
                var dirs = Directory.GetDirectories(usersDir);
                foreach (var dir in dirs)
                {
                    string folderName = Path.GetFileName(dir);
                    if (!SystemUsers.Contains(folderName, StringComparer.OrdinalIgnoreCase))
                    {
                        profiles.Add(new UserProfile
                        {
                            Username = folderName,
                            ProfilePath = dir,
                            IsSelected = string.Equals(folderName, Environment.UserName, StringComparison.OrdinalIgnoreCase)
                        });
                    }
                }
            }

            if (profiles.Count == 0)
            {
                profiles.Add(new UserProfile
                {
                    Username = Environment.UserName,
                    ProfilePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    IsSelected = true
                });
            }

            return profiles;
        }
    }
}
