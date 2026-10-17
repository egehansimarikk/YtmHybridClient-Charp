using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace YtmHybridClient
{
    public class LosslessResolver
    {
        private readonly List<ILosslessProvider> _providers = new List<ILosslessProvider>();

        public LosslessResolver()
        {
            // İleride başka servisler eklendiğinde buraya tek satırla eklenebilir (_providers.Add(new TidalProvider()); gibi)
            _providers.Add(new JamendoProvider());
        }

        public async Task<string?> ResolveLosslessStreamAsync(string title, string artist, string album = "")
        {
            try
            {
                Debug.WriteLine($"[LosslessResolver] Aranan Şarkı: '{artist} - {title}'");

                foreach (var provider in _providers)
                {
                    string? streamUrl = await provider.GetFlacStreamUrlAsync(title, artist, album);
                    if (!string.IsNullOrEmpty(streamUrl))
                    {
                        return streamUrl;
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LosslessResolver Hata] {ex.Message}");
                return null;
            }
        }
    }
}