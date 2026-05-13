namespace MTGProxyBuilder.Core.Services
{
    public class ImageCacheService
    {
        private readonly string _cacheDirectory;
        // cardId -> full path; avoids Directory.GetFiles per lookup
        private readonly Dictionary<string, string> _fileIndex = new(StringComparer.OrdinalIgnoreCase);

        public ImageCacheService()
        {
            _cacheDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MTGProxyBuilder", "ImageCache");
            Directory.CreateDirectory(_cacheDirectory);
            RebuildIndex();
        }

        public string CacheDirectory => _cacheDirectory;

        private void RebuildIndex()
        {
            _fileIndex.Clear();
            foreach (var file in Directory.GetFiles(_cacheDirectory))
                _fileIndex[Path.GetFileNameWithoutExtension(file)] = file;
        }

        public async Task<string?> CacheImageFromUrlAsync(HttpClient httpClient, string imageUrl, string cardId)
        {
            try
            {
                if (_fileIndex.TryGetValue(cardId, out var existing))
                    return existing;

                string extension = Path.GetExtension(new Uri(imageUrl).AbsolutePath);
                if (string.IsNullOrEmpty(extension)) extension = ".jpg";

                string fileName = $"{cardId}{extension}";
                string filePath = Path.Combine(_cacheDirectory, fileName);

                var imageData = await httpClient.GetByteArrayAsync(imageUrl);
                await File.WriteAllBytesAsync(filePath, imageData);
                _fileIndex[cardId] = filePath;
                return filePath;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Image cache error: {ex.Message}");
                return null;
            }
        }

        public bool IsImageCached(string cardId)
        {
            return _fileIndex.ContainsKey(cardId);
        }

        public string? GetCachedImagePath(string cardId)
        {
            return _fileIndex.TryGetValue(cardId, out var path) ? path : null;
        }

        public void ClearCache()
        {
            if (Directory.Exists(_cacheDirectory))
            {
                foreach (var file in Directory.GetFiles(_cacheDirectory))
                {
                    try { File.Delete(file); }
                    catch { /* skip locked files */ }
                }
            }
            _fileIndex.Clear();
        }
    }
}
