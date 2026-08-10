using System;
using System.IO;
using System.Text.Json;
using ProfileShift.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ProfileShift.Core
{
    public static class ConfigManager
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        public static void SaveConfigJson(MigrationConfig config, string filePath)
        {
            string json = JsonSerializer.Serialize(config, JsonOptions);
            File.WriteAllText(filePath, json);
        }

        public static MigrationConfig? LoadConfigJson(string filePath)
        {
            if (!File.Exists(filePath)) return null;
            string json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<MigrationConfig>(json, JsonOptions);
        }

        public static void SaveConfigYaml(MigrationConfig config, string filePath)
        {
            var serializer = new SerializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();
            string yaml = serializer.Serialize(config);
            File.WriteAllText(filePath, yaml);
        }

        public static MigrationConfig? LoadConfigYaml(string filePath)
        {
            if (!File.Exists(filePath)) return null;
            string yaml = File.ReadAllText(filePath);
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();
            return deserializer.Deserialize<MigrationConfig>(yaml);
        }

        public static MigrationConfig? LoadAutoConfig(string filePath)
        {
            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            if (ext == ".yaml" || ext == ".yml")
            {
                return LoadConfigYaml(filePath);
            }
            return LoadConfigJson(filePath);
        }
    }
}
