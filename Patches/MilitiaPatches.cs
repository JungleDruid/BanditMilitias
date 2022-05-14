using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using BanditMilitias.Helpers;
using HarmonyLib;
using SandBox.View.Map;
using SandBox.ViewModelCollection;
using SandBox.ViewModelCollection.MobilePartyTracker;
using SandBox.ViewModelCollection.Nameplate;
using StoryMode.GameComponents;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.AgentOrigins;
using TaleWorlds.CampaignSystem.CampaignBehaviors.AiBehaviors;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.LinQuick;
using static BanditMilitias.Helpers.Helper;
using static BanditMilitias.Globals;

// ReSharper disable UnusedMember.Global 
// ReSharper disable UnusedType.Global   
// ReSharper disable UnusedMember.Local   
// ReSharper disable RedundantAssignment  
// ReSharper disable InconsistentNaming

namespace BanditMilitias.Patches
{
    public static class MilitiaPatches
    {
        private static float lastChecked;

        [HarmonyPatch(typeof(Campaign), "Tick")]
        public static class CampaignTickPatch
        {
            // main merge method
            private static void Postfix()
            {
                if (Campaign.Current.TimeControlMode == CampaignTimeControlMode.Stop
                    || Campaign.Current.TimeControlMode == CampaignTimeControlMode.UnstoppableFastForwardForPartyWaitTime
                    || Campaign.Current.TimeControlMode == CampaignTimeControlMode.FastForwardStop
                    || Campaign.Current.TimeControlMode == CampaignTimeControlMode.StoppableFastForward)
                {
                    return;
                }

                if (lastChecked == 0)
                {
                    lastChecked = Campaign.CurrentTime;
                }

                // don't run this if paused and unless 3% off power limit
                if (Campaign.CurrentTime - lastChecked < 1f
                    || MilitiaPowerPercent + MilitiaPowerPercent / 100 * 0.03 > Globals.Settings.GlobalPowerPercent)
                {
                    return;
                }

                lastChecked = Campaign.CurrentTime;
                var parties = MobileParty.All.Where(m =>
                        m.Party.IsMobile
                        && m.CurrentSettlement is null
                        && !m.IsUsedByAQuest()
                        && m.IsBandit
                        && m.MemberRoster.TotalManCount >= Globals.Settings.MergeableSize)
                    .ToListQ();
                for (var index = 0; index < parties.Count; index++)
                {
                    //T.Restart();
                    var mobileParty = parties[index];

                    if (Hideouts.AnyQ(s => s.Position2D.Distance(mobileParty.Position2D) < MinDistanceFromHideout))
                    {
                        continue;
                    }

                    if (mobileParty.IsTooBusyToMerge())
                    {
                        continue;
                    }

                    var nearbyParties = MobileParty.FindPartiesAroundPosition(mobileParty.Position2D, FindRadius)
                        .Intersect(parties)
                        .ToListQ();
                    nearbyParties.Remove(mobileParty);

                    if (!nearbyParties.Any())
                    {
                        continue;
                    }

                    if (mobileParty.StringId.Contains("manhunter")) // Calradia Expanded Kingdoms
                    {
                        continue;
                    }

                    if (mobileParty.IsBM())
                    {
                        if (CampaignTime.Now < mobileParty.BM()?.LastMergedOrSplitDate + CampaignTime.Hours(Globals.Settings.CooldownHours))
                        {
                            continue;
                        }
                    }

                    var targetParties = nearbyParties.Where(m =>
                        m.MemberRoster.TotalManCount + mobileParty.MemberRoster.TotalManCount >= Globals.Settings.MinPartySize
                        && IsAvailableBanditParty(m)).ToListQ();

                    var targetParty = targetParties?.GetRandomElement()?.Party;

                    //SubModule.Log($">T targetParty {T.ElapsedTicks / 10000F:F3}ms.");
                    // "nobody" is a valid answer
                    if (targetParty is null)
                    {
                        continue;
                    }

                    if (targetParty.MobileParty.IsBM())
                    {
                        var component = (ModBanditMilitiaPartyComponent)targetParty.MobileParty.PartyComponent;
                        CampaignTime? targetLastChangeDate = component.LastMergedOrSplitDate;
                        if (CampaignTime.Now < targetLastChangeDate + CampaignTime.Hours(Globals.Settings.CooldownHours))
                        {
                            continue;
                        }
                    }

                    var militiaTotalCount = mobileParty.MemberRoster.TotalManCount + targetParty.MemberRoster.TotalManCount;
                    if (MilitiaPowerPercent > Globals.Settings.GlobalPowerPercent
                        || militiaTotalCount > CalculatedMaxPartySize
                        || militiaTotalCount < Globals.Settings.MinPartySize
                        || NumMountedTroops(mobileParty.MemberRoster) + NumMountedTroops(targetParty.MemberRoster) > militiaTotalCount / 2)
                    {
                        continue;
                    }

                    //SubModule.Log($"==> counted {T.ElapsedTicks / 10000F:F3}ms.");
                    if (mobileParty != targetParty.MobileParty.MoveTargetParty &&
                        Campaign.Current.Models.MapDistanceModel.GetDistance(targetParty.MobileParty, mobileParty) > MergeDistance)
                    {
                        //SubModule.Log($"{mobileParty} seeking > {targetParty.MobileParty}");
                        mobileParty.SetMoveEscortParty(targetParty.MobileParty);
                        //SubModule.Log($"SetNavigationModeParty ==> {T.ElapsedTicks / 10000F:F3}ms");

                        if (targetParty.MobileParty.MoveTargetParty != mobileParty)
                        {
                            //SubModule.Log($"{targetParty.MobileParty} seeking back > {mobileParty}");
                            targetParty.MobileParty.SetMoveEscortParty(mobileParty);
                            //SubModule.Log($"SetNavigationModeTargetParty ==> {T.ElapsedTicks / 10000F:F3}ms");
                        }

                        continue;
                    }

                    //SubModule.Log($"==> found settlement {T.ElapsedTicks / 10000F:F3}ms."); 
                    // create a new party merged from the two           
                    var rosters = MergeRosters(mobileParty, targetParty);
                    var clan = GetMostPrevalent(rosters[0]);
                    if (clan is null) Debugger.Break();
                    var bm = MobileParty.CreateParty("Bandit_Militia", new ModBanditMilitiaPartyComponent(clan), m => m.ActualClan = clan);
                    InitMilitia(bm, rosters, mobileParty.Position2D);
                    // teleport new militias near the player
                    if (Globals.Settings.TestingMode)
                    {
                        // in case a prisoner
                        var party = Hero.MainHero.PartyBelongedTo ?? Hero.MainHero.PartyBelongedToAsPrisoner.MobileParty;
                        bm.Position2D = party.Position2D;
                    }

                    bm.Party.Visuals.SetMapIconAsDirty();
                    try
                    {
                        // can throw if Clan is null
                        Trash(mobileParty);
                        Trash(targetParty.MobileParty);
                    }
                    catch (Exception ex)
                    {
                        Log(ex);
                    }

                    DoPowerCalculations();
                    //SubModule.Log($"==> Finished all work: {T.ElapsedTicks / 10000F:F3}ms.");
                }

                //SubModule.Log($"Looped ==> {T.ElapsedTicks / 10000F:F3}ms");
            }
        }

        public static class DefaultPartySpeedCalculatingModelCalculatePureSpeedPatch
        {
            public static void Postfix(MobileParty mobileParty, ref ExplainedNumber __result)
            {
                if (mobileParty.IsBandit
                    || mobileParty.IsBM()
                    && mobileParty.TargetParty is not null
                    && (mobileParty.TargetParty.IsBandit
                        || mobileParty.TargetParty.IsBM()))
                {
                    __result.AddFactor(0.15f);
                }
                else if (mobileParty.IsBM())
                {
                    __result.AddFactor(-0.15f);
                }
            }
        }

        // changes the flag
        [HarmonyPatch(typeof(PartyVisual), "AddCharacterToPartyIcon")]
        public static class PartyVisualAddCharacterToPartyIconPatch
        {
            private static void Prefix(CharacterObject characterObject, ref string bannerKey)
            {
                if (Globals.Settings.RandomBanners &&
                    characterObject.HeroObject?.PartyBelongedTo is not null &&
                    characterObject.HeroObject.PartyBelongedTo.IsBM())
                {
                    var component = (ModBanditMilitiaPartyComponent)characterObject.HeroObject.PartyBelongedTo.PartyComponent;
                    bannerKey = component.BannerKey;
                }
            }
        }

        // changes the little shield icon under the party
        [HarmonyPatch(typeof(PartyBase), "Banner", MethodType.Getter)]
        public static class PartyBaseBannerPatch
        {
            private static void Postfix(PartyBase __instance, ref Banner __result)
            {
                if (Globals.Settings.RandomBanners &&
                    __instance.MobileParty is not null &&
                    __instance.MobileParty.IsBM())
                {
                    __result = __instance.MobileParty.BM().Banner;
                }
            }
        }

        // changes the shields in combat
        [HarmonyPatch(typeof(PartyGroupAgentOrigin), "Banner", MethodType.Getter)]
        public static class PartyGroupAgentOriginBannerGetterPatch
        {
            private static void Postfix(IAgentOriginBase __instance, ref Banner __result)
            {
                var party = (PartyBase)__instance.BattleCombatant;
                if (Globals.Settings.RandomBanners &&
                    party.MobileParty is not null &&
                    party.MobileParty.IsBM())
                {
                    __result = party.MobileParty?.BM().Banner;
                }
            }
        }

        [HarmonyPatch(typeof(EnterSettlementAction), "ApplyForParty")]
        public static class EnterSettlementActionApplypublicPatch
        {
            private static bool Prefix(MobileParty mobileParty, Settlement settlement)
            {
                if (mobileParty.PartyComponent is ModBanditMilitiaPartyComponent)
                {
                    Log($"Preventing {mobileParty} from entering {settlement.Name}");
                    SetMilitiaPatrol(mobileParty);
                    return false;
                }

                return true;
            }
        }

        // changes the name on the campaign map (hot path)
        [HarmonyPatch(typeof(PartyNameplateVM), "RefreshDynamicProperties")]
        public static class PartyNameplateVMRefreshDynamicPropertiesPatch
        {
            private static readonly Dictionary<MobileParty, string> Map = new();

            private static void Postfix(PartyNameplateVM __instance, ref string ____fullNameBind)
            {
                //T.Restart();
                // Leader is null after a battle, crashes after-action
                // this staged approach feels awkward but it's fast
                if (__instance.Party?.LeaderHero is null)
                {
                    return;
                }

                if (Map.ContainsKey(__instance.Party))
                {
                    ____fullNameBind = Map[__instance.Party];
                    //SubModule.Log(T.ElapsedTicks);
                    return;
                }

                if (!__instance.Party.IsBM())
                {
                    return;
                }

                Map.Add(__instance.Party, __instance.Party.BM().Name.ToString());
                ____fullNameBind = Map[__instance.Party];
                //SubModule.Log(T.ElapsedTicks);
            }
        }

        // blocks conversations with militias
        [HarmonyPatch(typeof(PlayerEncounter), "DoMeetingInternal")]
        public static class PlayerEncounterDoMeetingInternalPatch
        {
            private static bool Prefix(PartyBase ____encounteredParty)
            {
                if (____encounteredParty.MobileParty.IsBM())
                {
                    GameMenu.SwitchToMenu("encounter");
                    return false;
                }

                return true;
            }
        }

        // prevent militias from attacking parties they can destroy easily
        [HarmonyPatch(typeof(MobileParty), "CanAttack")]
        public static class MobilePartyCanAttackPatch
        {
            private static void Postfix(MobileParty __instance, MobileParty targetParty, ref bool __result)
            {
                if (__result
                    && !targetParty.IsGarrison
                    && __instance.IsBM())
                {
                    if (Globals.Settings.IgnoreVillagersCaravans
                        && targetParty.IsCaravan || targetParty.IsVillager)
                    {
                        __result = false;
                        return;
                    }

                    var party1Strength = __instance.GetTotalStrengthWithFollowers();
                    var party2Strength = targetParty.GetTotalStrengthWithFollowers();
                    float delta;
                    if (party1Strength > party2Strength)
                    {
                        delta = party1Strength - party2Strength;
                    }
                    else
                    {
                        delta = party2Strength - party1Strength;
                    }

                    var deltaPercent = delta / party1Strength * 100;
                    __result = deltaPercent <= Globals.Settings.MaxStrengthDeltaPercent;
                }
            }
        }

        // force Heroes to die in simulated combat
        [HarmonyPriority(Priority.High)]
        [HarmonyPatch(typeof(SPScoreboardVM), "TroopNumberChanged")]
        public static class SPScoreboardVMTroopNumberChangedPatch
        {
            private static void Prefix(BasicCharacterObject character, ref int numberDead, ref int numberWounded)
            {
                var c = (CharacterObject)character;
                if (numberWounded > 0
                    && c.HeroObject?.PartyBelongedTo is not null
                    && c.HeroObject.PartyBelongedTo.IsBM())
                {
                    numberDead = 1;
                    numberWounded = 0;
                }
            }
        }

        [HarmonyPatch(typeof(TroopRoster), "AddToCountsAtIndex")]
        public static class TroopRosterAddToCountsAtIndexPatch
        {
            private static Exception Finalizer(TroopRoster __instance, Exception __exception)
            {
                // throws with Heroes Must Die
                if (__exception is IndexOutOfRangeException)
                {
                    Log("HACK Squelching IndexOutOfRangeException at TroopRoster.AddToCountsAtIndex");
                    return null;
                }

                // throws during nuke of poor state
                if (__exception is NullReferenceException)
                {
                    Log("HACK Squelching NullReferenceException at TroopRoster.AddToCountsAtIndex");
                    return null;
                }

                return __exception;
            }
        }

        // changes the optional Tracker icons to match banners
        [HarmonyPatch(typeof(MobilePartyTrackItemVM), "UpdateProperties")]
        public static class MobilePartyTrackItemVMUpdatePropertiesPatch
        {
            public static void Postfix(MobilePartyTrackItemVM __instance, ref ImageIdentifierVM ____factionVisualBind)
            {
                if (__instance.TrackedParty is not null
                    && PartyImageMap.ContainsKey(__instance.TrackedParty))
                {
                    ____factionVisualBind = PartyImageMap[__instance.TrackedParty];
                }
            }
        }

        // skip the regular bandit AI stuff, looks at moving into hideouts
        // and other stuff I don't really want happening
        [HarmonyPatch(typeof(AiBanditPatrollingBehavior), "AiHourlyTick")]
        public static class AiBanditPatrollingBehaviorAiHourlyTickPatch
        {
            public static bool Prefix(MobileParty mobileParty)
            {
                return !mobileParty.IsBM();
            }
        }

        [HarmonyPatch(typeof(DefaultMobilePartyFoodConsumptionModel), "DoesPartyConsumeFood")]
        public static class DefaultMobilePartyFoodConsumptionModelDoesPartyConsumeFoodPatch
        {
            public static void Postfix(MobileParty mobileParty, ref bool __result)
            {
                if (mobileParty.IsBM())
                {
                    __result = false;
                }
            }
        }
    }
}
