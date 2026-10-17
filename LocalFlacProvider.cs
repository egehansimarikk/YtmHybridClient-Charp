using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace YtmHybridClient.Providers
{
    public class LocalFlacProvider : ILosslessProvider
    {
        private readonly string _folderPath;

        public LocalFlacProvider(string folderPath)
        {
            _folderPath = folderPath;
        }

        public string Name => "Local FLAC";

        public Task<string?> GetFlacStreamUrlAsync(string title, string artist, string album, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(_folderPath))
                return Task.FromResult<string?>(null);

            string normalizedTitle = Normalize(title);
            string normalizedArtist = Normalize(artist);
            if (normalizedTitle.Length == 0)
                return Task.FromResult<string?>(null);

            try
            {
                foreach (string filePath in Directory.EnumerateFiles(_folderPath, "*.flac", SearchOption.AllDirectories))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    string normalizedFileName = Normalize(Path.GetFileNameWithoutExtension(filePath));
                    if (normalizedFileName.Contains(normalizedTitle, StringComparison.Ordinal) &&
                        (normalizedArtist.Length == 0 || normalizedFileName.Contains(normalizedArtist, StringComparison.Ordinal)))
                    {
                        return Task.FromResult<string?>(filePath);
                    }
                }
            }
            catch (IOException)
            {
                return Task.FromResult<string?>(null);
            }
            catch (UnauthorizedAccessException)
            {
                return Task.FromResult<string?>(null);
            }

            return Task.FromResult<string?>(null);
        }

        private static string Normalize(string value)
        {
            var normalized = new StringBuilder(value.Length);
            foreach (char character in value)
            {
                if (char.IsLetterOrDigit(character))
                    normalized.Append(char.ToLowerInvariant(character));
            }

            return normalized.ToString();
        }
    }
}
