using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using SandBox.View.Map;
using SandBox.ViewModelCollection.Map;
using TaleWorlds.CampaignSystem.Issues;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Engine;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using static BanditMilitias.Helper;

// ReSharper disable UnusedMember.Global
// ReSharper disable UnusedType.Global
// ReSharper disable UnusedMember.Local
// ReSharper disable RedundantAssignment
// ReSharper disable InconsistentNaming

namespace BanditMilitias.Patches
{
    public static class MiscPatches
    {
        [HarmonyPatch(typeof(MapScreen), "OnInitialize")]
        public static class MapScreenOnInitializePatch
        {
            public static void Prefix()
            {
                if (Input.IsKeyDown(InputKey.LeftShift) || Input.IsKeyDown(InputKey.RightShift))
                    Nuke();
            }

            public static void Postfix()
            {
                InitMap();
                RemoveBadItems();
            }
        }

        [HarmonyPatch(typeof(MapMobilePartyTrackerVM), MethodType.Constructor, typeof(Camera), typeof(Action<Vec2>))]
        public static class MapMobilePartyTrackerVMCtorPatch
        {
            public static void Postfix(MapMobilePartyTrackerVM __instance) => Globals.MapMobilePartyTrackerVM = __instance;
        }

        [HarmonyPatch(typeof(MerchantNeedsHelpWithOutlawsIssueQuestBehavior.MerchantNeedsHelpWithOutlawsIssueQuest), "HourlyTickParty")]
        public static class MerchantNeedsHelpWithOutlawsIssueQuestHourlyTickParty
        {
            public static bool Prefix(MobileParty mobileParty) => !mobileParty.IsBM();
        }

        // ServeAsSoldier issue where the MobileParty isn't a quest party
        internal static void PatchSaSDeserters(ref MobileParty __result)
        {
            Traverse.Create(__result).Field<bool>("IsCurrentlyUsedByAQuest").Value = true;
        }

        [HarmonyPatch(typeof(MobileParty), "TaleWorlds.CampaignSystem.Map.IMapEntity.OnPartyInteraction")]
        public static class MobilePartyOnPartyInteractionPatch
        {
            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                CodeMatcher matcher = new(instructions);

                if (matcher.End()
                    .MatchStartBackwards(CodeMatch.Calls(AccessTools.PropertyGetter(typeof(MobileParty), nameof(MobileParty.IsEngaging))))
                    .IsValid)
                {
                    matcher.SetInstructionAndAdvance(CodeInstruction.Call(typeof(MobileParty), "get_ShortTermBehavior"));
                    matcher.InsertAndAdvance(new CodeInstruction(OpCodes.Ldc_I4_6));
                    var targetLabel = (Label)matcher.Operand;
                    matcher.SetInstruction(new CodeInstruction(OpCodes.Bne_Un_S, targetLabel));
                }

                return matcher.InstructionEnumeration();
            }
        }
    }
}