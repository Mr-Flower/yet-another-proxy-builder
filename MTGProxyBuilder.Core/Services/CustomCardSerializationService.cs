using MTGProxyBuilder.Core.Models;
using Newtonsoft.Json;

namespace MTGProxyBuilder.Core.Services
{
    public class CustomCardSerializationService
    {
        private static readonly JsonSerializerSettings Settings = new()
        {
            TypeNameHandling = TypeNameHandling.Auto,
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore
        };

        public async Task<bool> SaveProjectAsync(CustomCardProject project, string filePath)
        {
            project.LastModified = DateTime.Now;
            var json = JsonConvert.SerializeObject(project, Settings);
            await File.WriteAllTextAsync(filePath, json);
            return true;
        }

        public async Task<CustomCardProject?> LoadProjectAsync(string filePath)
        {
            if (!File.Exists(filePath))
                return null;

            var json = await File.ReadAllTextAsync(filePath);
            return JsonConvert.DeserializeObject<CustomCardProject>(json, Settings);
        }
    }
}
