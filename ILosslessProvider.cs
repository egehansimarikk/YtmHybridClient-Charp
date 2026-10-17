using System.Threading;
using System.Threading.Tasks;

namespace YtmHybridClient.Providers
{
    public interface ILosslessProvider
    {
        string Name { get; }
        Task<string?> GetFlacStreamUrlAsync(string title, string artist, string album, CancellationToken cancellationToken = default);
    }
}