using StardewModdingAPI;
using System;
using System.Collections.Generic;
using TehPers.FishingOverhaul.Api;
using TehPers.FishingOverhaul.Api.Content;

namespace TehPers.FishingOverhaul.Services
{
    internal sealed partial class DefaultFishingSource : IFishingContentSource
    {
        private readonly IMonitor monitor;
        private readonly IManifest manifest;

        public DefaultFishingSource(IMonitor monitor, IManifest manifest)
        {
            this.monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
            this.manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
        }

        // CORRECTION 1 : Le type de retour est IEnumerable<FishingContent>
        // CORRECTION 2 : Le paramètre 'monitor' est renommé en '_' pour éviter l'avertissement paramètre inutilisé
        public IEnumerable<FishingContent> Reload(IMonitor _)
        {
            var fishContent = this.GetDefaultFishData();
            var trashContent = this.GetDefaultTrashData();

            // On retourne l'objet dans une collection via 'yield return'
            yield return new FishingContent(this.manifest)
            {
                AddFish = fishContent.AddFish,
                SetFishTraits = fishContent.SetFishTraits,
                AddTrash = trashContent.AddTrash
            };
        }
    }
}
