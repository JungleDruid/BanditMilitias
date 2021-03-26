using System;
using System.Collections.Generic;
using System.Linq;
using Bandit_Militias.Helpers;
using HarmonyLib;
using SandBox.View.Map;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.SandBox.CampaignBehaviors.AiBehaviors;
using TaleWorlds.Core;
using TaleWorlds.Library;
using static Bandit_Militias.Helpers.Helper;
using static Bandit_Militias.Globals;

// ReSharper disable UnusedMember.Global
// ReSharper disable UnusedType.Global
// ReSharper disable UnusedMember.Local
// ReSharper disable RedundantAssignment
// ReSharper disable InconsistentNaming

namespace Bandit_Militias.Patches
{
    public class MiscPatches
    {
        [HarmonyPatch(typeof(MapScreen), "OnInitialize")]
        public static class MapScreenOnInitializePatch
        {
            private static void Postfix()
            {
                Mod.Log("MapScreen.OnInitialize");
                HeroCreatorCopy.VeteransRespect = PerkObject.All.First(x => x.StringId == "LeadershipVeteransRespect");
                HeroCreatorCopy.Leadership = SkillObject.All.First(x => x.StringId == "Leadership");
                EquipmentItems.Clear();
                PopulateItems();
                Recruits = CharacterObject.All.Where(x =>
                        x.Level == 11 &&
                        x.Occupation == Occupation.Soldier &&
                        !x.StringId.StartsWith("regular_fighter") &&
                        !x.StringId.StartsWith("veteran_borrowed_troop") &&
                        !x.StringId.EndsWith("_tier_1") &&
                        !x.StringId.Contains("_militia_") &&
                        !x.StringId.Equals("sturgian_warrior_son") &&
                        !x.StringId.Equals("khuzait_noble_son") &&
                        !x.StringId.Equals("imperial_vigla_recruit") &&
                        !x.StringId.Equals("battanian_highborn_youth") &&
                        !x.StringId.Equals("vlandian_squire") &&
                        !x.StringId.Equals("aserai_youth") &&
                        !x.StringId.Equals("poacher"))
                    .ToList();

                // used for armour
                foreach (ItemObject.ItemTypeEnum value in Enum.GetValues(typeof(ItemObject.ItemTypeEnum)))
                {
                    ItemTypes[value] = Items.FindAll(x =>
                        x.Type == value && x.Value >= 1000 && x.Value <= Globals.Settings.MaxItemValue * Variance).ToList();
                }

                // front-load
                BanditEquipment.Clear();
                for (var i = 0; i < 500; i++)
                {
                    BanditEquipment.Add(BuildViableEquipmentSet());
                }

                Militias.Clear();
                Hideouts = Settlement.FindAll(x => x.IsHideout()).ToList();

                var militias = MobileParty.All.Where(x =>
                    x != null && x.StringId.StartsWith("Bandit_Militia")).ToList();
                for (var i = 0; i < militias.Count; i++)
                {
                    var militia = militias[i];
                    if (militia.LeaderHero == null)
                    {
                        Mod.Log("Leaderless militia found and removed.");
                        Trash(militia);
                    }
                    else
                    {
                        Militias.Add(new Militia(militia));
                    }
                }

                Mod.Log($"Militias: {militias.Count} (registered {Militias.Count})");
                Flush();
                // 1.4.3b is dropping the militia settlements at some point, I haven't figured out where
                ReHome();
                DailyCalculations();
            }
        }

        // 0 member parties will form if this is happening
        // was only happening with debugger attached because that makes sense
        [HarmonyPatch(typeof(MobileParty), "FillPartyStacks")]
        public class MobilePartyFillPartyStacksPatch
        {
            private static bool Prefix(PartyTemplateObject pt)
            {
                if (pt == null)
                {
                    Mod.Log("BROKEN");
                    Debug.PrintError("Bandit Militias is broken please notify @gnivler via Nexus");
                    return false;
                }

                return true;
            }
        }

        // just disperse small militias
        // todo prevent this unless the militia has lost or retreated from combat
        [HarmonyPatch(typeof(MapEventSide), "HandleMapEventEndForParty")]
        public class MapEventSideHandleMapEventEndForPartyPatch
        {
            private static void Prefix(PartyBase party, ref Hero __state)
            {
                __state = party.LeaderHero;
            }
            
            private static void Postfix(MapEventSide __instance, PartyBase party, Hero __state)
            {
                if (party?.MobileParty == null ||
                    !party.MobileParty.StringId.StartsWith("Bandit_Militia") ||
                    party.PrisonRoster != null &&
                    party.PrisonRoster.Contains(Hero.MainHero.CharacterObject))
                {
                    return;
                }
                
                if (party.MemberRoster?.TotalHealthyCount == 0 ||
                    party.MemberRoster?.TotalHealthyCount < Globals.Settings.MinPartySize &&
                    party.PrisonRoster?.Count < Globals.Settings.MinPartySize &&
                    __instance.Casualties > party.MemberRoster.Count / 2)
                {
                    Mod.Log($"Dispersing {party.Name} of {party.MemberRoster.TotalHealthyCount}+{party.MemberRoster.TotalWounded}w+{party.PrisonRoster.Count}p");
                    __state.KillHero();
                    Trash(party.MobileParty);
                }
            }
        }

        // in e1.5.6 this class was rewritten
        // prevents militias from being added to DynamicBodyCampaignBehavior._heroBehaviorsDictionary
        // checked 1.4.3b
        //[HarmonyPatch(typeof(DynamicBodyCampaignBehavior), "OnAfterDailyTick")]
        //public class DynamicBodyCampaignBehaviorOnAfterDailyTickPatch
        //{
        //    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator ilg)
        //    {
        //        var label = ilg.DefineLabel();
        //        var codes = instructions.ToList();
        //        var insertAt = codes.FindIndex(x => x.opcode.Equals(OpCodes.Stloc_2));
        //        insertAt++;
        //        var moveNext = AccessTools.Method(typeof(IEnumerator), nameof(IEnumerator.MoveNext));
        //        var jumpIndex = codes.FindIndex(x =>
        //            x.opcode == OpCodes.Callvirt && (MethodInfo) x.operand == moveNext);
        //        jumpIndex--;
        //        codes[jumpIndex].labels.Add(label);
        //        var helperMi = AccessTools.Method(
        //            typeof(DynamicBodyCampaignBehaviorOnAfterDailyTickPatch), nameof(helper));
        //        var stack = new List<CodeInstruction>
        //        {
        //            // copy the Hero on top of the stack then feed it to the helper for a bool then branch
        //            new CodeInstruction(OpCodes.Ldloc_2),
        //            new CodeInstruction(OpCodes.Call, helperMi),
        //            new CodeInstruction(OpCodes.Brfalse, label)
        //        };
        //        codes.InsertRange(insertAt, stack);
        //        return codes.AsEnumerable();
        //    }
        //
        //    private static int helper(Hero hero)
        //    {
        //        // ReSharper disable once PossibleNullReferenceException
        //        if (hero.PartyBelongedTo != null &&
        //            hero.PartyBelongedTo.StringId.StartsWith("Bandit_Militia"))
        //        {
        //            return 1;
        //        }
        //
        //        return 0;
        //    }
        //}

        // 1.4.3b is throwing when militias are nuked and the game is reloaded with militia MapEvents
        //[HarmonyPatch(typeof(MapEvent), "RemoveInvolvedPartyInternal")]
        //public class MapEventRemoveInvolvedPartyInternalPatch
        //{
        //    private static bool Prefix(PartyBase party) => party.Visuals != null;
        //} 

        // 1.4.3b will crash on load at TaleWorlds.CampaignSystem.PlayerEncounter.DoWait()
        // because MapEvent.PlayerMapEvent is saved as null for whatever reason
        // best solution so far is to avoid the problem with a kludge
        // myriad other corrective attempts left the game unplayable (can't encounter anything)
        [HarmonyPatch(typeof(MBSaveLoad), "SaveGame")]
        public class MDSaveLoadSaveGamePatch
        {
            private static void Prefix()
            {
                var mapEvent = Traverse.Create(PlayerEncounter.Current).Field("_mapEvent").GetValue<MapEvent>();
                if (mapEvent != null &&
                    mapEvent.InvolvedParties.Any(x =>
                        x.MobileParty != null &&
                        x.MobileParty.StringId.StartsWith("Bandit_Militia")))
                {
                    mapEvent = null;
                    PlayerEncounter.Finish();
                    Campaign.Current.TimeControlMode = CampaignTimeControlMode.Stop;
                }
            }
        }

        [HarmonyPatch(typeof(HeroCreator), "CreateRelativeNotableHero")]
        public class HeroCreatorCreateRelativeNotableHeroPatch
        {
            private static bool Prefix(Hero relative)
            {
                if (Militias.Any(x => x.Hero == relative))
                {
                    Mod.Log("Not creating relative of Bandit Militia hero");
                    return false;
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(AiBanditPatrollingBehavior), "AiHourlyTick")]
        public class AiBanditPatrollingBehaviorAiHourlyTickPatch
        {
            private static Exception Finalizer(MobileParty mobileParty, Exception __exception)
            {
                if (__exception != null)
                {
                    FileLog.Log($"mobileParty: {mobileParty}");
                    if (mobileParty.LeaderHero == null)
                    {
                        FileLog.Log("\tCRITICAL ERROR - this party has no leader for some reason, trashing...");
                        Trash(mobileParty);
                    }
                }

                return null;
            }
        }


        //[HarmonyPatch(typeof(HeroSpawnCampaignBehavior), "OnHeroDailyTick")]
        //public class HeroSpawnCampaignBehaviorOnHeroDailyTickPatch
        //{
        //    private static bool Prefix(Hero hero)
        //    {
        //        // latest 1.4.3 patch is trying to teleport bandit heroes apparently before they have parties
        //        // there's no party here so unable to filter by Bandit_Militia
        //        // for now this probably doesn't matter but vanilla isn't ready for bandit heroes
        //        // it could fuck up other mods relying on this method unfortunately
        //        // but that seems very unlikely to me right now
        //        return !Clan.BanditFactions.Contains(hero.Clan);
        //    }
        //}

        // todo check if needed in later game versions (currently e1.5.1)
        //[HarmonyPatch(typeof(HeroSpawnCampaignBehavior), "GetMoveScoreForCompanion")]
        //public class HeroSpawnCampaignBehaviorGetMoveScoreForCompanionPatch
        //{
        //    private static bool Prefix(Hero companion, Settlement settlement)
        //    {
        //        return !(companion?.LastSeenPlace == null || settlement?.MapFaction == null);
        //    }
        //}
        //

        [HarmonyPatch(typeof(CampaignEventDispatcher), "OnSettlementEntered")]
        public class Sfsdkj
        {
            private static void Postfix(MobileParty party, Settlement settlement, Hero hero)
            {
                if (hero != null &&
                    hero.StringId.StartsWith("Bandit_Militia"))
                {
                    if (settlement != null &&
                        settlement.HeroesWithoutParty.Contains(hero))
                    {
                        Traverse.Create(settlement).Field<List<Hero>>("_heroesWithoutPartyCache").Value.Remove(hero);
                    }
                }
            }

        }

        [HarmonyPatch(typeof(CampaignEvents), "OnSettlementEntered")]
        public class asdfds
        {
            private static void Postfix(MobileParty party, Settlement settlement, Hero hero)
            {
                if (hero != null &&
                    hero.StringId.StartsWith("Bandit_Militia"))
                {
                    if (settlement != null &&
                        settlement.HeroesWithoutParty.Contains(hero))
                    {
                        Traverse.Create(settlement).Field<List<Hero>>("_heroesWithoutPartyCache").Value.Remove(hero);
                    }
                }

            }
        }

        // doesn't work!  runs but player party doesn't appear nor is movable after release as prisoner
        [HarmonyPatch(typeof(PartyBase), "UpdateVisibilityAndInspected")]
        public class PartyBaseUpdateVisibilityAndInspectedPatch
        {
            private static void Prefix(PartyBase __instance, ref bool __state)
            {
                if (__instance.MobileParty?.StringId == "player_party")
                {
                    if (__instance.Visuals == null)
                    {
                        Traverse.Create(__instance).Field<IPartyVisual>("_visual").Value = Campaign.Current.VisualCreator.PartyVisualCreator.CreatePartyVisual();
                        __state = true;
                        ////__instance.Visuals?.OnStartup(__instance);
                        //Traverse.Create(__instance.MobileParty).Method("StartUp").GetValue();
                        //
                        //PartyBase.MainParty.MobileParty.InitializeMobileParty(Campaign.Current.CurrentGame.ObjectManager.GetObject<PartyTemplateObject>("main_hero_party_template"), __instance.MobileParty.Position2D, 0f, 0f, -1);
                        //PartyBase.MainParty.MobileParty.PartyComponent = new LordPartyComponent(Clan.PlayerClan, Hero.MainHero);
                    }
                }
            }

            private static void Postfix(PartyBase __instance, bool __state)
            {
                if (__instance.MobileParty?.StringId == "player_party")
                {
                    if (__state)
                    {
                        Traverse.Create((PartyVisual) __instance.Visuals).Method("RefreshPartyIcon").GetValue();
                    }
                }
            }
        }
    }
}
