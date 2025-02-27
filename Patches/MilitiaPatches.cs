using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Helpers;
using Microsoft.Extensions.Logging;
using SandBox.View.Map;
using SandBox.ViewModelCollection.Map;
using SandBox.ViewModelCollection.Nameplate;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.AgentOrigins;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.CampaignBehaviors.AiBehaviors;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Conversation;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.LinQuick;
using TaleWorlds.Localization;
using static BanditMilitias.Helper;
using static BanditMilitias.Globals;

// ReSharper disable ConvertIfStatementToReturnStatement
// ReSharper disable UnusedMember.Global
// ReSharper disable UnusedType.Global
// ReSharper disable UnusedMember.Local
// ReSharper disable RedundantAssignment
// ReSharper disable InconsistentNaming

namespace BanditMilitias.Patches
{
    internal sealed class MilitiaPatches
    {
        private static ILogger _logger;
        private static ILogger Logger => _logger ??= LogFactory.Get<MilitiaPatches>();
        
        private static readonly AccessTools.FieldRef<MobilePartyAi, int> numberOfRecentFleeingFromAParty =
            AccessTools.FieldRefAccess<MobilePartyAi, int>("_numberOfRecentFleeingFromAParty");

        // Merge bandit parties when they are engaged with each other
        [HarmonyPatch(typeof(EncounterManager), nameof(EncounterManager.StartPartyEncounter))]
        public static class MobilePartyOnPartyInteraction
        {
            public static bool Prefix(PartyBase attackerParty, PartyBase defenderParty)
            {
                if (PartyBase.MainParty == attackerParty || PartyBase.MainParty == defenderParty ||
                    attackerParty?.MobileParty?.IsBandit != true || defenderParty?.MobileParty?.IsBandit != true ||
                    FactionManager.IsAtWarAgainstFaction(attackerParty.MapFaction, defenderParty.MapFaction))
                    return true;
                TryMergeParties(attackerParty.MobileParty, defenderParty.MobileParty);
                return false;
            }
        }

        // changes the flag
        [HarmonyPatch(typeof(PartyVisual), "AddCharacterToPartyIcon")]
        public static class PartyVisualAddCharacterToPartyIconPatch
        {
            private static void Prefix(CharacterObject characterObject, ref string bannerKey)
            {
                if (Globals.Settings.RandomBanners &&
                    characterObject.HeroObject?.PartyBelongedTo?.IsBM() == true)
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
                    __instance.IsMobile &&
                    __instance.MobileParty.IsBM())
                {
                    __result = __instance.MobileParty.GetBM().Banner;
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
                    party.IsMobile &&
                    party.MobileParty.IsBM())
                {
                    __result = party.MobileParty?.GetBM().Banner;
                }
            }
        }

        // prevent bandit militias from entering hideouts
        [HarmonyPatch(typeof(EnterSettlementAction), "ApplyForParty")]
        public static class EnterSettlementActionApplyForPartyPatch
        {
            private static bool Prefix(MobileParty mobileParty, Settlement settlement)
            {
                if (mobileParty.IsBM())
                {
                    Logger.LogTrace($"Preventing {mobileParty} from entering {settlement.Name}");
                    mobileParty.Ai.SetMoveModeHold();
                    return false;
                }

                return true;
            }
        }

        // changes the name on the campaign map (hot path)
        [HarmonyPatch(typeof(PartyNameplateVM), "RefreshDynamicProperties")]
        public static class PartyNameplateVMRefreshDynamicPropertiesPatch
        {
            public static void Prefix(PartyNameplateVM __instance, TextObject ____latestNameTextObject, out bool __state)
            {
                __state = false;
                
                // Leader is null after a battle, crashes after-action
                // this staged approach feels awkward but it's fast
                if (__instance.Party?.LeaderHero is null)
                {
                    return;
                }

                if (!__instance.Party.IsBandit || !__instance.Party.IsBM())
                {
                    return;
                }

                __state = ____latestNameTextObject != __instance.Party.LeaderHero.Name;
            }
            
            public static void Postfix(PartyNameplateVM __instance, ref string ____fullNameBind, bool __state)
            {
                if (!__state) return;
                
                ____fullNameBind = __instance.Party.Name.ToString();
            }
        }

        // blocks conversations with militias
        [HarmonyPatch(typeof(PlayerEncounter), "DoMeetingInternal")]
        public static class PlayerEncounterDoMeetingInternalPatch
        {
            public static bool Prefix(PartyBase ____encounteredParty)
            {
                if (Globals.Settings.SkipConversations && ____encounteredParty.MobileParty.IsBM())
                {
                    GameMenu.SwitchToMenu("encounter");
                    return false;
                }

                return true;
            }
        }

        // prevent bandit militia leaders from being identified as lords
        [HarmonyPatch(typeof(ConversationManager), "GetSentenceMatch")]
        public static class ConversationManagerGetSentenceMatchPatch
        {
            public static bool Prefix(int sentenceIndex, bool onlyPlayer, List<ConversationSentence> ____sentences, ref bool __result)
            {
                if (Hero.OneToOneConversationHero?.Clan?.IsBanditFaction == true)
                {
                    var sentence = ____sentences[sentenceIndex];
                    if (Globals.LordConversationTokens.Contains(sentence.InputToken) ||
                        Globals.LordConversationTokens.Contains(sentence.OutputToken))
                    {
                        __result = false;
                        return false;
                    }
                }

                return true;
            }
        }

        // skip the dialogues with bandit militia heroes after combat
        [HarmonyPatch(typeof(PlayerEncounter), "DoCaptureHeroes")]
        public static class PlayerEncounterDoCaptureHeroesPatch
        {
            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var codeMatcher = new CodeMatcher(instructions);

                // if (this._capturedHeroes.Count > 0)
                CodeMatcher target = codeMatcher
                    .MatchStartForward(
                        new CodeMatch(OpCodes.Ldarg_0),
                        CodeMatch.LoadsField(AccessTools.Field(typeof(PlayerEncounter), "_capturedHeroes")),
                        CodeMatch.Calls(AccessTools.Method(typeof(TroopRosterElement), "get_Count")),
                        CodeMatch.LoadsConstant(),
                        CodeMatch.Branches()
                    ).ThrowIfInvalid("Could not find the target at DoCaptureHeroes");

                // insert UnCaptureBMHeroes
                CodeInstruction[] insertion =
                [
                    CodeInstruction.LoadArgument(0),
                    CodeInstruction.LoadField(typeof(PlayerEncounter), "_capturedHeroes"),
                    CodeInstruction.LoadLocal(0),
                    CodeInstruction.Call(typeof(PlayerEncounterDoCaptureHeroesPatch), nameof(UnCaptureBMHeroes))
                ];
                
                target.Instruction.MoveLabelsTo(insertion[0]);
                target.Insert(insertion);
                
                return codeMatcher.Instructions();
            }

            internal static void UnCaptureBMHeroes(List<TroopRosterElement> capturedHeroes, TroopRoster receivingLootShare)
            {
                foreach (TroopRosterElement element in capturedHeroes.WhereQ(t => t.Character.IsBM()).ToArrayQ())
                {
                    receivingLootShare.AddToCounts(element.Character, element.Number, true, element.WoundedNumber, element.Xp);
                    capturedHeroes.Remove(element);
                }
            }
        }
        
        // skip the dialogues with bandit militia heroes when freeing them from enemy parties
        [HarmonyPatch(typeof(PlayerEncounter), "DoFreeHeroes")]
        public static class PlayerEncounterDoFreeHeroesPatch
        {
            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var codeMatcher = new CodeMatcher(instructions);

                // if (this._capturedHeroes.Count > 0)
                CodeMatcher target = codeMatcher
                    .MatchStartForward(
                        new CodeMatch(OpCodes.Ldarg_0),
                        CodeMatch.LoadsField(AccessTools.Field(typeof(PlayerEncounter), "_freedHeroes")),
                        new CodeMatch(OpCodes.Ldsfld),
                        new CodeMatch(OpCodes.Dup),
                        CodeMatch.Branches()
                    ).ThrowIfInvalid("Could not find the target at DoFreeHeroes");

                // insert UnCaptureBMHeroes
                CodeInstruction[] insertion =
                [
                    CodeInstruction.LoadArgument(0),
                    CodeInstruction.LoadField(typeof(PlayerEncounter), "_freedHeroes"),
                    CodeInstruction.LoadArgument(0),
                    CodeInstruction.LoadField(typeof(PlayerEncounter), "_mapEvent"),
                    CodeInstruction.Call(typeof(PlayerEncounterDoFreeHeroesPatch), nameof(UnFreeBMHeroes))
                ];
                
                target.Instruction.MoveLabelsTo(insertion[0]);
                target.Insert(insertion);
                
                return codeMatcher.Instructions();
            }

            private static void UnFreeBMHeroes(List<TroopRosterElement> freedHeroes, MapEvent mapEvent)
            {
                if (!freedHeroes.Any(e => e.Character.IsBM())) return;
                MethodInfo GetPrisonerRosterReceivingLootShare = AccessTools.Method(typeof(MapEvent), "GetPrisonerRosterReceivingLootShare");
                var receivingLootShare = (TroopRoster)GetPrisonerRosterReceivingLootShare.Invoke(mapEvent, [PartyBase.MainParty]);
                foreach (TroopRosterElement element in freedHeroes.WhereQ(t => t.Character.IsBM()).ToArrayQ())
                {
                    if (element.Character.HeroObject.MapFaction?.IsAtWarWith(Hero.MainHero.MapFaction) == true)
                    {
                        element.Character.HeroObject.PartyBelongedToAsPrisoner.PrisonRoster.RemoveTroop(element.Character);
                        receivingLootShare.AddToCounts(element.Character, 1, true, element.WoundedNumber, element.Xp, false);
                    }
                    freedHeroes.Remove(element);
                }
            }
        }

        [HarmonyPatch(typeof(PartyBase), nameof(PartyBase.AddPrisoner))]
        public static class PartyBaseAddPrisonerPatch
        {
            public static bool Prefix(PartyBase __instance, CharacterObject element)
            {
                if (__instance != PartyBase.MainParty || !element.IsBM()) return true;
                return !__instance.PrisonerHeroes.Contains(element);
            }
        }

        // conversation voice filter
        [HarmonyPatch(typeof(DefaultVoiceOverModel), nameof(DefaultVoiceOverModel.GetSoundPathForCharacter))]
        public static class DefaultVoiceOverModelGetSoundPathForCharacterPatch
        {
            public static void Postfix(CharacterObject character, VoiceObject voiceObject, ref string __result)
            {
                if (!Globals.Settings.CheckVoiceGender)
                    return;
                
                if (character.IsBM() && character.IsFemale)
                {
                    if (!__result.Contains("_female"))
                        __result = "";
                }
            }
        }

        // prevent militias from attacking parties they can destroy easily
        [HarmonyPatch(typeof(MobilePartyAi), "CanAttack")]
        public static class MobilePartyCanAttackPatch
        {
            public static void Postfix(MobileParty targetParty, MobileParty ____mobileParty, ref bool __result)
            {
                if (__result && targetParty.Party.IsMobile && ____mobileParty.IsBM())
                {
                    if (Globals.Settings.IgnoreVillagersCaravans
                        && (targetParty.IsCaravan || targetParty.IsVillager))
                    {
                        __result = false;
                        return;
                    }

                    if (targetParty.LeaderHero is not null
                        && ____mobileParty.GetBM().Avoidance.TryGetValue(targetParty.LeaderHero, out var heroAvoidance)
                        && MBRandom.RandomFloat * 100f < heroAvoidance)
                    {
                        __result = false;
                        return;
                    }

                    var party1Strength = ____mobileParty.GetTotalStrengthWithFollowers();
                    var party2Strength = targetParty.GetTotalStrengthWithFollowers();
                    float delta;
                    if (party1Strength > party2Strength)
                        delta = party1Strength - party2Strength;
                    else
                        delta = party2Strength - party1Strength;
                    var deltaPercent = delta / party1Strength * 100;
                    __result = deltaPercent <= Globals.Settings.MaxStrengthDeltaPercent;
                }
            }
        }

        // changes the optional Tracker icons to match banners
        [HarmonyPatch(typeof(MobilePartyTrackItemVM), "UpdateProperties")]
        public static class MobilePartyTrackItemVMUpdatePropertiesPatch
        {
            public static void Postfix(MobilePartyTrackItemVM __instance, ref ImageIdentifierVM ____factionVisualBind)
            {
                if (__instance.TrackedParty is null || !__instance.TrackedParty.IsBM())
                    return;
                if (!PartyImageMap.TryGetValue(__instance.TrackedParty, out var image))
                {
                    image = new ImageIdentifierVM(__instance.TrackedParty.GetBM().Banner);
                    PartyImageMap.Add(__instance.TrackedParty, image);
                }
                
                ____factionVisualBind = image;
            }
        }

        // skip the regular bandit AI stuff, looks at moving into hideouts
        // and other stuff I don't really want happening
        [HarmonyPatch(typeof(AiBanditPatrollingBehavior), "AiHourlyTick")]
        public static class AiBanditPatrollingBehaviorAiHourlyTickPatch
        {
            public static bool Prefix(MobileParty mobileParty) => !mobileParty.IsBM();
        }

        // copied out of assembly and modified to not check against occupation
        [HarmonyPatch(typeof(NameGenerator), "GenerateHeroFullName")]
        public static class NameGeneratorGenerateHeroName
        {
            public static void Postfix(Hero hero, TextObject heroFirstName, ref TextObject __result)
            {
                if (hero.CharacterObject.Occupation is not Occupation.Bandit
                    || (hero.PartyBelongedTo is not null
                        && !hero.PartyBelongedTo.IsBM()))
                    return;

                var textObject = heroFirstName;
                var index = (int)AccessTools.Method(typeof(NameGenerator), "SelectNameIndex")
                    .Invoke(NameGenerator.Current, new object[] { hero, GangLeaderNames(NameGenerator.Current), 0u, false });
                NameGenerator.Current.AddName(GangLeaderNames(NameGenerator.Current)[index]);
                textObject = GangLeaderNames(NameGenerator.Current)[index].CopyTextObject();
                textObject.SetTextVariable("FEMALE", hero.IsFemale ? 1 : 0);
                textObject.SetTextVariable("IMPERIAL", hero.Culture.StringId == "empire" ? 1 : 0);
                textObject.SetTextVariable("COASTAL", hero.Culture.StringId is "empire" or "vlandia" ? 1 : 0);
                textObject.SetTextVariable("NORTHERN", hero.Culture.StringId is "battania" or "sturgia" ? 1 : 0);
                textObject.SetTextVariable("FIRSTNAME", heroFirstName);
                StringHelpers.SetCharacterProperties("HERO", hero.CharacterObject, textObject);
                __result = textObject;
            }
        }

        // the hero died after winning the battle
        [HarmonyPatch(typeof(DefaultSkillLevelingManager), "OnPersonalSkillExercised")]
        public static class DefaultSkillLevelingManagerOnPersonalSkillExercisedPatch
        {
            public static bool Prefix(Hero hero)
            {
                if (hero is null) return true;
                
                if (hero.HeroDeveloper is null)
                {
                    return false;
                }

                return true;
            }
        }

        // the hero died in a simulated battle?
        [HarmonyPatch(typeof(MobilePartyHelper), nameof(MobilePartyHelper.CanTroopGainXp))]
        public static class MobilePartyHelperCanTroopGainXpPatch
        {
            public static bool Prefix(PartyBase owner, CharacterObject character, ref bool __result)
            {
                if (character?.UpgradeTargets is null)
                {
                    if (owner?.MemberRoster?.Contains(character) == true)
                    {
                        owner.MemberRoster.RemoveTroop(character);
                    }
                    
                    __result = false;
                    return false;
                }

                return true;
            }
        }

        // prevent crash - set HomeSettlement as settlement
        [HarmonyPatch(typeof(MobilePartyAi), "CalculateContinueChasingScore")]
        public class MobilePartyCalculateContinueChasingScore
        {
            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var codeMatcher = new CodeMatcher(instructions);

                // settlement = ((hideout != null) ? hideout.Settlement : null);
                var settlement = (LocalBuilder)codeMatcher
                    .MatchEndForward(
                        CodeMatch.Calls(AccessTools.PropertyGetter(typeof(SettlementComponent), "Settlement")),
                        CodeMatch.StoresLocal(),
                        CodeMatch.Branches()
                    )
                    .InstructionAt(-1)
                    .operand;

                // starts after if (this._mobileParty.IsBandit)
                int start = codeMatcher
                    .MatchEndBackwards(
                        CodeMatch.Calls(AccessTools.PropertyGetter(typeof(MobileParty), "IsBandit")),
                        CodeMatch.Branches()
                    )
                    .ThrowIfInvalid("Could not find IsBandit")
                    .Advance(1)
                    .Pos;
                
                // ends at settlement = ((hideout != null) ? hideout.Settlement : null);
                int end = codeMatcher
                    .SearchForward(c => c.opcode == OpCodes.Stloc_S && c.operand == settlement)
                    .Pos;
                
                // replace with settlement = GetBanditSettlement(_mobileParty);
                return codeMatcher
                    .Start()
                    .Advance(start)
                    .RemoveInstructions(end - start)
                    .Insert([
                        CodeInstruction.LoadArgument(0),
                        CodeInstruction.LoadField(typeof(MobilePartyAi), "_mobileParty"),
                        CodeInstruction.Call(typeof(MobilePartyCalculateContinueChasingScore), nameof(GetBanditSettlement)),
                    ])
                    .Instructions();
            }

            private static Settlement GetBanditSettlement(MobileParty mobileParty)
            {
                return mobileParty.GetBM()?.HomeSettlement ?? mobileParty.BanditPartyComponent?.Hideout?.Settlement;
            }
        }

        // avoid stuffing the BM into PartiesWithoutPartyComponent at CampaignObjectManager.InitializeOnLoad
        [HarmonyPatch(typeof(MobileParty), "UpdatePartyComponentFlags")]
        public static class MobilePartyInitializeOnLoad
        {
            public static void Postfix(MobileParty __instance)
            {
                if (!__instance.IsBandit && __instance.IsBM())
                    IsBandit(__instance) = true;
            }
        }

        // final gate rejects bandit troops from being upgraded to non-bandit troops
        // put an if-BM-jump at the start to bypass the vanilla blockage
        [HarmonyPatch(typeof(PartyUpgraderCampaignBehavior), "GetPossibleUpgradeTargets")]
        public static class PartyUpgraderCampaignBehaviorGetPossibleUpgradeTargets
        {
            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var codes = instructions.ToListQ();
                Label jumpLabel = new();
                var method = AccessTools.Method(typeof(PartyUpgraderCampaignBehaviorGetPossibleUpgradeTargets), nameof(IsBM));
                int index;
                for (index = codes.Count - 1; index >= 0; index--)
                {
                    // float upgradeChanceForTroopUpgrade = Campaign.Current.Models.PartyTroopUpgradeModel.GetUpgradeChanceForTroopUpgrade(party, character, i);
                    if (codes[index].opcode == OpCodes.Call
                        && codes[index + 1].opcode == OpCodes.Callvirt
                        && codes[index + 2].opcode == OpCodes.Callvirt
                        && codes[index + 3].opcode == OpCodes.Ldarg_1)
                    {
                        break;
                    }
                }
                
                for (; index >= 0; index--)
                {
                    if (codes[index].labels.Count <= 0) continue;
                    jumpLabel = codes[index].labels[0];
                    break;
                }
                
                if (jumpLabel.GetHashCode() == 0)
                    throw new Exception("Could not find jumpLabel");
            
                var stack = new List<CodeInstruction>
                {
                    new (OpCodes.Ldarg_1),
                    new(OpCodes.Call, method),
                    new(OpCodes.Brtrue_S, jumpLabel)
                };
                
                int insertion = 0;
                for (; index >= 0; index--)
                {
                    // if ((!party.Culture.IsBandit || characterObject.Culture.IsBandit) && (character.Occupation != Occupation.Bandit || partyTroopUpgradeModel.CanPartyUpgradeTroopToTarget(party, character, characterObject)))
                    if (codes[index].opcode == OpCodes.Ldarg_1
                        && codes[index + 1].opcode == OpCodes.Callvirt
                        && codes[index + 2].opcode == OpCodes.Callvirt
                        && codes[index + 3].opcode == OpCodes.Brfalse_S)
                    {
                        insertion = index;
                        codes[index].MoveLabelsTo(stack[0]);
                        break;
                    }
                }
                
                if (insertion == 0)
                    throw new Exception("Could not find insertion point");
            
                codes.InsertRange(insertion, stack);
                return codes.AsEnumerable();
            }

            private static bool IsBM(PartyBase party)
            {
                return party.MobileParty?.IsBM() == true;
            }
        }

        // allow BMs to upgrade to whatever, mostly so more cavalry are created
        [HarmonyPatch(typeof(DefaultPartyTroopUpgradeModel), "CanPartyUpgradeTroopToTarget")]
        public class DefaultPartyTroopUpgradeModelCanPartyUpgradeTroopToTarget
        {
            public static void Postfix(PartyBase upgradingParty, ref bool __result)
            {
                if (upgradingParty.IsMobile && upgradingParty.MobileParty.IsBM())
                    __result = true;
            }
        }

        // 1.9 broke this
        [HarmonyPatch(typeof(MobileParty), "IsBanditBossParty", MethodType.Getter)]
        public class MobilePartyIsBanditBossParty
        {
            public static bool Prefix(MobileParty __instance) => !__instance.IsBM();
        }

        // bandit heroes' home settlements will be their born settlements if there is none
        [HarmonyPatch(typeof(Hero), nameof(Hero.UpdateHomeSettlement))]
        public class HeroUpdateHomeSettlement
        {
            public static void Postfix(Hero __instance, ref Settlement ____homeSettlement, Settlement ____bornSettlement)
            {
                if (____homeSettlement is not null || ____bornSettlement is null || __instance.Clan?.IsBanditFaction != true) return;
                ____homeSettlement = ____bornSettlement;
            }
        }
        
        // remove from Heroes list when killed
        [HarmonyPatch(typeof(KillCharacterAction), "ApplyInternal")]
        public class KillCharacterActionApplyInternalPatch
        {
            public static void Postfix(Hero victim,
                Hero killer,
                KillCharacterAction.KillCharacterActionDetail actionDetail,
                bool showNotification,
                bool isForced = false)
            {
                if (!Heroes.Contains(victim)) return;
                Logger.LogTrace($"{victim} is killed by {killer} due to {actionDetail}");
                Heroes.Remove(victim);
                CharacterRelationManager.Instance.RemoveHero(victim);
            }
        }

        [HarmonyPatch(typeof(PartyBase), "PartySizeLimit", MethodType.Getter)]
        public static class MobilePartyPartySizeLimitGetterPatch
        {
            public static void Postfix(PartyBase __instance, ref int ____cachedPartyMemberSizeLimit, ref int __result)
            {
                if (!Globals.Settings.IgnoreSizePenalty || !__instance.IsMobile || !__instance.MobileParty.IsBM()) return;
                int totalManCount = __instance.MemberRoster.TotalManCount;
                if (__result >= totalManCount) return;
                ____cachedPartyMemberSizeLimit = totalManCount;
                __result = totalManCount;
            }
        }

        // would've been null if there's no clan leader e.g. bandit clans
        [HarmonyPatch(typeof(DefaultDiplomacyModel), nameof(DefaultDiplomacyModel.GetHeroesForEffectiveRelation))]
        public static class DefaultDiplomacyModelGetHeroesForEffectiveRelationPatch
        {
            public static void Postfix(Hero hero1, Hero hero2, ref Hero effectiveHero1, ref Hero effectiveHero2)
            {
                effectiveHero1 ??= hero1;
                effectiveHero2 ??= hero2;
            }
        }

        // ignore the logs of militia leaders being taken as prisoners
        [HarmonyPatch(typeof(DefaultLogsCampaignBehavior), "OnPrisonerTaken")]
        public static class DefaultLogsCampaignBehaviorOnPrisonerTakenPatch
        {
            public static bool Prefix(PartyBase party, Hero hero)
            {
                if (Globals.Settings.RemovePrisonerMessages && party != PartyBase.MainParty && hero.IsBM())
                {
                    return false;
                }

                return true;
            }
        }

        // ignore the logs of militia leaders being released as prisoners
        [HarmonyPatch(typeof(DefaultLogsCampaignBehavior), "OnHeroPrisonerReleased")]
        public static class DefaultLogsCampaignBehaviorOnHeroPrisonerReleasedPatch
        {
            public static bool Prefix(PartyBase party, Hero hero)
            {
                if (Globals.Settings.RemovePrisonerMessages && party != PartyBase.MainParty && hero.IsBM())
                {
                    return false;
                }

                return true;
            }
        }
    }
}