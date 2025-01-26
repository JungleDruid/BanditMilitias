using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.SaveSystem;
using static BanditMilitias.Globals;
using static BanditMilitias.Helper;

// ReSharper disable ConvertToAutoProperty  
// ReSharper disable InconsistentNaming

namespace BanditMilitias
{
    public class ModBanditMilitiaPartyComponent : WarPartyComponent
    {
        [SaveableField(1)] public readonly Banner Banner;
        [SaveableField(2)] public readonly string BannerKey;
        [SaveableField(3)] public CampaignTime LastMergedOrSplitDate = CampaignTime.Now;
        [SaveableField(4)] public Dictionary<Hero, float> Avoidance = new();
        [SaveableField(5)] private Hero leader;
        [SaveableField(6)] private Settlement homeSettlement;
        [CachedData] private TextObject cachedName;

        public override Settlement HomeSettlement => homeSettlement;
        public override Hero Leader => leader;
        public override Hero PartyOwner => MobileParty?.ActualClan?.Leader; // clan is null during nuke  
        private static readonly MethodInfo GetLocalizedText = AccessTools.Method(typeof(MBTextManager), "GetLocalizedText");
        private static readonly MethodInfo OnWarPartyRemoved = AccessTools.Method(typeof(Clan), "OnWarPartyRemoved");

        public override TextObject Name
        {
            get
            {
                cachedName ??= CreateCachedName();
                return cachedName;
            }
        }

        public override void ChangePartyLeader(Hero newLeader)
        {
            if (newLeader is null)
            {
                if (!Heroes.Contains(leader))
                {
                    ForceRemoveLeader();
                }
            }
            else
            {
                Log.Debug?.Log($"{newLeader.Name} is taking over {MobileParty.Name}({MobileParty.StringId}) from {leader.Name}[{leader.HeroState}].");
                leader = newLeader;
                ClearCachedName();
            }
        }

        public void ForceRemoveLeader()
        {
            leader = null;
            ClearCachedName();
        }

        protected override void OnInitialize()
        {
            base.OnInitialize();
            if (!IsBandit(MobileParty))
                IsBandit(MobileParty) = true;
            OnWarPartyRemoved.Invoke(Clan, [this]);
        }

        public ModBanditMilitiaPartyComponent(Settlement settlement, Hero hero)
        {
            Banner = Banners.GetRandomElement();
            BannerKey = Banner.Serialize();
            hero ??= CreateHero(settlement);
            if (hero.HomeSettlement is null)
                _bornSettlement(hero) = settlement;
            hero.UpdateHomeSettlement();
            HiddenInEncyclopedia(hero.CharacterObject) = true;
            homeSettlement = hero.HomeSettlement;
            leader = hero;
        }

        internal TextObject CreateCachedName()
        {
            if (leader is null)
            {
                return new TextObject(Globals.Settings.LeaderlessBanditMilitiaString, new Dictionary<string, object>
                {
                    ["IS_BANDIT"] = 1
                });
            }

            return new TextObject("{=BMPartyName}{LEADER_NAME}'s {PARTY_NAME}", new Dictionary<string, object>
            {
                ["IS_BANDIT"] = 1,
                ["LEADER_NAME"] = leader.FirstName,
                ["PARTY_NAME"] = Globals.Settings.BanditMilitiaString
            });
        }

        public override void ClearCachedName()
        {
            cachedName = null;
        }
    }
}
