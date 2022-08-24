﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using HarmonyLib;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.ViewModelCollection;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.LinQuick;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;
using static BanditMilitias.Helpers.Helper;

namespace BanditMilitias.Patches
{
    public class Hacks
    {
        // rewrite of broken original in 1.8.0
        [HarmonyPatch(typeof(Hideout), "MapFaction", MethodType.Getter)]
        public static class HideoutMapFactionGetter
        {
            public static bool Prefix(Hideout __instance, ref IFaction __result)
            {
                __result = Clan.BanditFactions.First(c => c.Culture == __instance.Settlement.Culture);
                return false;
            }
        }

        // troops with missing data causing lots of NREs elsewhere
        // just a temporary patch
        public static void HackPurgeAllBadTroopsFromAllParties()
        {
            Log("Starting iteration off all troops in all parties... this might take a few minutes...");
            foreach (var mobileParty in MobileParty.All)
            {
                var rosters = new[] { mobileParty.MemberRoster, mobileParty.PrisonRoster };
                foreach (var roster in rosters)
                {
                    while (roster.GetTroopRoster().AnyQ(t => t.Character.Name == null))
                    {
                        foreach (var troop in roster.GetTroopRoster())
                        {
                            if (troop.Character.Name == null)
                            {
                                Log($"removing bad troop {troop.Character.StringId} from {mobileParty.StringId}.  Prison roster? {roster.IsPrisonRoster}");
                                roster.AddToCounts(troop.Character, -1);
                                MBObjectManager.Instance.UnregisterObject(troop.Character);
                            }
                        }
                    }
                }
            }
        }

        // throws during nuke
        // parameters are included for debugging
        [HarmonyPatch(typeof(TroopRoster), "ClampXp")]
        public static class TroopRosterClampXpPatch
        {
            public static Exception Finalizer(Exception __exception, TroopRoster __instance)
            {
                if (__exception is not null)
                    Log(__exception);
                return null;
            }
        }
    }
}
