using System;
using System.Collections.Generic;
using System.Diagnostics;
using SandBox.ViewModelCollection.Map;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.ObjectSystem;
// ReSharper disable InconsistentNaming

namespace BanditMilitias
{
    public static class Globals
    {
        internal static void ClearGlobals()
        {
            PartyImageMap = new();
            ItemTypes = new();
            Recruits = new();
            LootRecord = new();
            EquipmentItems = new();
            Arrows = new();
            Bolts = new();
            BanditEquipment = new();
            LastCalculated = 0;
            PartyCacheInterval = 0;
            RaidCap = 0;
            EquipmentMap = new();
            MapMobilePartyTrackerVM.Trackers.Clear();
            BMHeroes = new();
            Mounts = new();
            Saddles = new();
            Hideouts = new();
            AllBMs = new ModBanditMilitiaPartyComponent[] { };
        }

        // merge/split criteria
        internal const float MergeDistance = 2;
        internal const float FindRadius = 20;
        internal const float MinDistanceFromHideout = 8;

        // holders for criteria
        internal static float CalculatedMaxPartySize;
        internal static float CalculatedGlobalPowerLimit;
        internal static float GlobalMilitiaPower;
        internal static float MilitiaPowerPercent;
        internal static float MilitiaPartyAveragePower;

        // dictionary maps
        internal static Dictionary<MobileParty, ImageIdentifierVM> PartyImageMap = new();
        internal static Dictionary<ItemObject.ItemTypeEnum, List<ItemObject>> ItemTypes = new();
        internal static Dictionary<CultureObject, List<CharacterObject>> Recruits = new();
        internal static Dictionary<MapEventSide, List<EquipmentElement>> LootRecord = new();

        // object tracking
        internal static List<Hero> BanditMilitiaHeroes = new();
        internal static List<CharacterObject> BanditMilitiaCharacters = new();
        internal static List<CharacterObject> BanditMilitiaTroops = new();
        internal static Dictionary<string, Equipment> EquipmentMap = new();

        // misc
        internal static readonly Random Rng = new();
        internal static readonly Stopwatch T = new();
        internal static Settings Settings;
        internal static List<EquipmentElement> EquipmentItems = new();
        internal static List<ItemObject> Arrows = new();
        internal static List<ItemObject> Bolts = new();
        internal static List<Equipment> BanditEquipment = new();
        internal static readonly List<Banner> Banners = new();
        internal static double LastCalculated;
        internal static double PartyCacheInterval;
        internal static int RaidCap;
        internal static Clan Looters;
        internal static List<ItemObject> Mounts;
        internal static List<ItemObject> Saddles;
        internal static List<Settlement> Hideouts;
        internal static IEnumerable<ModBanditMilitiaPartyComponent> AllBMs;
        internal static CampaignPeriodicEventManager CampaignPeriodicEventManager;
        internal static object Ticker;

        // ReSharper disable once InconsistentNaming
        internal static MapMobilePartyTrackerVM MapMobilePartyTrackerVM;

        internal static float Variance => MBRandom.RandomFloatRanged(0.925f, 1.075f);
        internal static List<CharacterObject> BMHeroes = new();

        // ArmsDealer compatibility
        internal static CultureObject BlackFlag => MBObjectManager.Instance.GetObject<CultureObject>("ad_bandit_blackflag");

        internal static readonly Dictionary<string, int> DifficultyXpMap = new()
        {
            { "Off", 0 },
            { "Normal", 300 },
            { "Hard", 600 },
            { "Hardest", 900 },
        };

        internal static readonly Dictionary<string, int> GoldMap = new()
        {
            { "Low", 250 },
            { "Normal", 500 },
            { "Rich", 900 },
            { "Richest", 2000 },
        };
    }
}
