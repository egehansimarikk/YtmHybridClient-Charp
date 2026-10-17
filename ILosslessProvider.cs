using System.Threading.Tasks;

namespace YtmHybridClient
{
    public interface ILosslessProvider
    {
        string Name { get; }
        Task<string?> GetFlacStreamUrlAsync(string title, string artist, string album);
    }
}