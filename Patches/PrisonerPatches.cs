using System.Linq;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.LinQuick;
using TaleWorlds.Localization;
using static BanditMilitias.Helper;
using static BanditMilitias.Globals;

// ReSharper disable UnusedType.Global
// ReSharper disable InconsistentNaming
// ReSharper disable UnusedMember.Local

namespace BanditMilitias.Patches
{
    public static class PrisonerPatches
    {
        // rename leaderless BMs after they've lost the battle
        [HarmonyPatch(typeof(MapEvent), "FinishBattle")]
        public static class MapEventFinishBattlePatch
        {
            public static void Prefix(MapEvent __instance)
            {
                if (!__instance.HasWinner) return;
                var loserBMs = __instance.PartiesOnSide(__instance.DefeatedSide)
                    .WhereQ(p => p.Party?.MobileParty?.PartyComponent is ModBanditMilitiaPartyComponent);
                foreach (var party in loserBMs)
                {
                    MobileParty mobileParty = party.Party.MobileParty;
                    if (mobileParty.LeaderHero.IsDead && mobileParty.MemberRoster.TotalHealthyCount >= Globals.Settings.DisperseSize)
                    {
                        Log.Debug?.Log($"[Info] {mobileParty.Name}({mobileParty.StringId}) has lost a battle and its leader {mobileParty.LeaderHero.Name}, but was not dispersed.");
                        RemoveMilitiaLeader(mobileParty);
                    }

                    RemoveUndersizedTracker(mobileParty);
                }

                DoPowerCalculations();
            }
        }

        // upgrades all troops with any looted equipment in Postfix
        // drops Avoidance scores when BMs win
        [HarmonyPatch(typeof(MapEvent), "LootDefeatedParties")]
        public static class MapEventLootDefeatedPartiesPatch
        {
            //private static IEnumerable<MapEventParty> loserBMs;
            public static void Prefix(MapEvent __instance)
            {
                if (!__instance.HasWinner)
                    return;
                var loserBMs = __instance.PartiesOnSide(__instance.DefeatedSide)
                    .WhereQ(p => p.Party?.MobileParty?.PartyComponent is ModBanditMilitiaPartyComponent);
                foreach (var party in loserBMs)
                {
                    RemoveUndersizedTracker(party.Party.MobileParty);
                }

                DoPowerCalculations();
            }

            public static void Postfix(MapEvent __instance)
            {
                if (!__instance.HasWinner)
                    return;
                var loserBMs = __instance.PartiesOnSide(__instance.DefeatedSide)
                    .WhereQ(p => p.Party?.MobileParty?.PartyComponent is ModBanditMilitiaPartyComponent);
                foreach (var party in loserBMs)
                {
                    Log.Debug?.Log($"[Info] {party.Party.MobileParty.Name}({party.Party.MobileParty.StringId}) is defeated in battle.");
                    if (party.Party.MobileParty.MemberRoster.TotalHealthyCount < Globals.Settings.DisperseSize)
                        Trash(party.Party.MobileParty, RemoveHeroCondition.OnlyDead);
                }

                var winnerBMs = __instance.PartiesOnSide(__instance.WinningSide)
                    .WhereQ(p => p.Party?.MobileParty?.PartyComponent is ModBanditMilitiaPartyComponent).ToListQ();
                if (!winnerBMs.Any())
                    return;
                var loserHeroes = __instance.PartiesOnSide(__instance.DefeatedSide)
                    .SelectQ(mep => mep.Party.Owner).WhereQ(h => h is not null).ToListQ();
                foreach (var bm in winnerBMs)
                {
                    PartyBase party = bm.Party;
                    if (party.LeaderHero?.IsDead == true)
                    {
                        Log.Debug?.Log($"[Info] {party.MobileParty.Name}({party.MobileParty.StringId}) has won a battle but lost its leader {party.LeaderHero.Name}.");
                        if (party.MemberRoster.Contains(party.LeaderHero.CharacterObject))
                            party.MemberRoster.RemoveTroop(party.LeaderHero.CharacterObject);
                        party.LeaderHero.RemoveMilitiaHero();
                        RemoveMilitiaLeader(party.MobileParty);
                    }

                    DecreaseAvoidance(loserHeroes, bm);
                }
            }
        }

        // remove all heroes who are taken as prisoners
        [HarmonyPatch(typeof(TakePrisonerAction), "ApplyInternal")]
        public static class TakePrisonerActionApplyInternalPatch
        {
            public static bool Prefix(PartyBase capturerParty, Hero prisonerCharacter, bool isEventCalled)
            {
                if (prisonerCharacter.IsBM())
                {
                    Log.Debug?.Log($"[Info] {prisonerCharacter.Name} is taken prisoner by {capturerParty.Name}.");
                    if (Heroes.Contains(prisonerCharacter))
                        prisonerCharacter.RemoveMilitiaHero();
                    return false;
                }

                return true;
            }
        }

        // remove all released heroes
        [HarmonyPatch(typeof(EndCaptivityAction), "ApplyInternal")]
        public static class EndCaptivityActionApplyInternalPatch
        {
            public static bool Prefix(Hero prisoner, EndCaptivityDetail detail, Hero facilitatior)
            {
                if (prisoner.IsBM())
                {
                    Log.Debug?.Log($"[Info] {prisoner.Name} is released due to {detail}.");
                    if (Heroes.Contains(prisoner))
                        prisoner.RemoveMilitiaHero();
                    return false;
                }

                return true;
            }
        }

        // convert heroes to prisoners after they surrendered and agreed to join.
        [HarmonyPatch(typeof(BanditsCampaignBehavior), "OpenRosterScreenAfterBanditEncounter")]
        public static class BanditsCampaignBehaviorOpenRosterScreenAfterBanditEncounterPatch
        {
            public static void Prefix(MobileParty conversationParty, bool doBanditsJoinPlayerSide)
            {
                if (!conversationParty.IsBM()) return;

                if (doBanditsJoinPlayerSide)
                {
                    foreach (TroopRosterElement troop in conversationParty.MemberRoster.RemoveIf(t => t.Character.IsHero))
                    {
                        troop.Character.HeroObject.RemoveMilitiaHero();
                        conversationParty.PrisonRoster.Add(troop);
                    }
                    Log.Debug?.Log($"[Info] {conversationParty.Name}({conversationParty.StringId}) has joined to the player.");
                }
                else
                {
                    Log.Debug?.Log($"[Info] {conversationParty.Name}({conversationParty.StringId}) has surrendered to the player.");
                }
            }
        }
        
        // prevent stray BM heroes from entering settlements
        [HarmonyPatch(typeof(TeleportHeroAction), nameof(TeleportHeroAction.ApplyImmediateTeleportToSettlement))]
        public static class TeleportHeroActionApplyImmediateTeleportToSettlementPatch
        {
            public static bool Prefix(Hero heroToBeMoved, Settlement targetSettlement)
            {
                if (heroToBeMoved.IsBM() && targetSettlement is not null)
                {
                    Log.Debug?.Log($"[Info] Removing stray hero {heroToBeMoved.Name} before they enter settlement {targetSettlement.Name}.");
                    heroToBeMoved.RemoveMilitiaHero();
                    return false;
                }

                return true;
            }
        }
    }
}