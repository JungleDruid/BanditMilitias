using System;
using HarmonyLib;
using Microsoft.Extensions.Logging;
using TaleWorlds.CampaignSystem.Roster;

// ReSharper disable InconsistentNaming

namespace BanditMilitias.Patches
{
    public sealed class Hacks
    {
        private static ILogger _logger;
        private static ILogger Logger => _logger ??= LogFactory.Get<Hacks>();
        
        // throws during nuke (apparently not in 3.9)
        // parameters are included for debugging
        [HarmonyPatch(typeof(TroopRoster), "ClampXp")]
        public static class TroopRosterClampXpPatch
        {
            public static Exception Finalizer(Exception __exception, TroopRoster __instance)
            {
                if (__exception is not null)
                    Logger.LogError(__exception, "Error at TroopRoster.ClampXp");

                return null;
            }
        }
    }
}
