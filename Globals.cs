using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using SandBox.ViewModelCollection.Map;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.ObjectSystem;

namespace BanditMilitias
{
    // ReSharper disable once ClassNeverInstantiated.Global
    public class Globals
    {
        // merge/split criteria
        public const float MergeDistance = 2;
        public const float FindRadius = 20;
        public const float MinDistanceFromHideout = 8;

        // holders for criteria
        public static float CalculatedMaxPartySize;
        public static float CalculatedGlobalPowerLimit;
        public static float GlobalMilitiaPower;
        public static float MilitiaPowerPercent;
        public static float MilitiaPartyAveragePower;

        // dictionary maps
        public static readonly Dictionary<MobileParty, ImageIdentifierVM> PartyImageMap = new();
        public static readonly Dictionary<ItemObject.ItemTypeEnum, List<ItemObject>> ItemTypes = new();
        public static readonly Dictionary<CultureObject, List<CharacterObject>> Recruits = new();
        public static readonly Dictionary<MapEventSide, List<EquipmentElement>> LootRecord = new();

        // misc
        public static readonly Random Rng = new();
        public static readonly Stopwatch T = new();
        public static Settings Settings;
        public static readonly List<EquipmentElement> EquipmentItems = new();
        public static List<ItemObject> Arrows = new();
        public static List<ItemObject> Bolts = new();
        public static readonly List<Equipment> BanditEquipment = new();
        public static readonly List<Banner> Banners = new();
        public static double LastCalculated;
        public static double PartyCacheInterval;
        public static int RaidCap;
        public static Dictionary<string, Equipment> EquipmentMap = new();
        private static Clan looters;
        public static Clan Looters => looters ??= Clan.BanditFactions.First(c => c.StringId == "looters");
        private static IEnumerable<Clan> synthClans;
        public static IEnumerable<Clan> SynthClans => synthClans ??= Clan.BanditFactions.Except(new[] { Looters });

        // ReSharper disable once InconsistentNaming
        public static MapMobilePartyTrackerVM MapMobilePartyTrackerVM;

        public static float Variance => MBRandom.RandomFloatRanged(0.925f, 1.075f);
        public static List<CharacterObject> HeroCharacters = new();
        
        // ArmsDealer compatibility
        public static CultureObject BlackFlag => MBObjectManager.Instance.GetObject<CultureObject>("ad_bandit_blackflag");

        public static readonly Dictionary<string, int> DifficultyXpMap = new()
        {
            { "Off", 0 },
            { "Normal", 300 },
            { "Hard", 600 },
            { "Hardest", 900 },
        };

        public static readonly Dictionary<string, int> GoldMap = new()
        {
            { "Low", 250 },
            { "Normal", 500 },
            { "Rich", 900 },
            { "Richest", 2000 },
        };
    }
}
