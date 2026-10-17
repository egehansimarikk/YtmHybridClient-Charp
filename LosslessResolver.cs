using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using YtmHybridClient.Providers;

namespace YtmHybridClient.Services
{
    public class LosslessResolver
    {
        // Her kaynağa ayrı süre verilir; biri yavaşsa diğerinin süresini yemez.
        private static readonly TimeSpan PerProviderTimeout = TimeSpan.FromSeconds(3);

        private readonly List<ILosslessProvider> _providers;

        public LosslessResolver(List<ILosslessProvider> activeProviders)
        {
            _providers = activeProviders;
        }

        public async Task<(string? StreamUrl, string ProviderName)> ResolveAudioAsync(string title, string artist, string album, CancellationToken cancellationToken = default)
        {
            foreach (var provider in _providers)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                using var providerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                providerCts.CancelAfter(PerProviderTimeout);

                try
                {
                    System.Diagnostics.Debug.WriteLine($"[Resolver] {provider.Name} deneniyor...");
                    var streamUrl = await provider.GetFlacStreamUrlAsync(title, artist, album, providerCts.Token);

                    if (!string.IsNullOrEmpty(streamUrl))
                        return (streamUrl, provider.Name);

                    System.Diagnostics.Debug.WriteLine($"[Resolver] {provider.Name}: sonuç yok.");
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // Dışarıdan iptal (yeni parça / toplam süre doldu)
                    break;
                }
                catch (OperationCanceledException)
                {
                    // Sadece bu kaynağın süresi doldu, sıradakine geç
                    System.Diagnostics.Debug.WriteLine($"[Resolver] {provider.Name}: {PerProviderTimeout.TotalSeconds:0.#} sn zaman aşımı.");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[{provider.Name} Hata]: {ex.Message}");
                }
            }

            return (null, "YouTube Music");
        }
    }
}
