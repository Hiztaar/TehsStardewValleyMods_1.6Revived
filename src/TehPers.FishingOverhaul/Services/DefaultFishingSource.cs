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

        public IEnumerable<FishingContent> Reload(IMonitor _)
        {
            var fishContent = this.GetDefaultFishData();
            var trashContent = this.GetDefaultTrashData();

            // Fix: Load treasure data so chests aren't empty
            var treasureContent = this.GetDefaultTreasureData();

            yield return new FishingContent(this.manifest)
            {
                AddFish = fishContent.AddFish,
                SetFishTraits = fishContent.SetFishTraits,
                AddTrash = trashContent.AddTrash,
                AddTreasure = treasureContent.AddTreasure
            };
        }
    }
}
