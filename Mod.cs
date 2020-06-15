﻿using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.SandBox.Issues;
using TaleWorlds.Core;
using TaleWorlds.InputSystem;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using static Bandit_Militias.Patches;
using static Bandit_Militias.Helper.Globals;

// ReSharper disable ClassNeverInstantiated.Global  
// ReSharper disable UnusedMember.Local  
// ReSharper disable InconsistentNaming

namespace Bandit_Militias
{
    public class Mod : MBSubModuleBase
    {
        private static readonly Harmony harmony = new Harmony("ca.gnivler.bannerlord.BanditMilitias");

        protected override void OnSubModuleLoad()
        {
            //Harmony.DEBUG = true;
            Log("Startup " + DateTime.Now.ToShortTimeString());
            RunManualPatches();
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            //Harmony.DEBUG = false; 
        }

        protected override void OnApplicationTick(float dt)
        {
            // safety hotkey in case things go sideways
            if ((Input.IsKeyDown(InputKey.LeftControl) || Input.IsKeyDown(InputKey.RightControl)) &&
                (Input.IsKeyDown(InputKey.LeftAlt) || Input.IsKeyDown(InputKey.RightAlt)) &&
                Input.IsKeyPressed(InputKey.N))
            {
                Trace("Nuke all hotkey pressed");
                tempList.Clear();
                tempList = MobileParty.All.Where(x => x.Name.ToString() == "Bandit Militia").ToList();
                InformationManager.AddQuickInformation(new TextObject($"Nuking all {tempList.Count} Bandit Militia parties"));
                tempList.Do(x =>
                {
                    Trace($"  Nuking {x.Name}");
                    x.RemoveParty();
                });
            }
        }

        internal static void Log(object input)
        {
            //FileLog.Log($"[Bandit Militias] {input ?? "null"}");
        }

        internal static void Trace(object input)
        {
            //FileLog.Log($"[Bandit Militias] {input ?? "null"}");
        }

        private static void RunManualPatches()
        {
            try
            {
                //SkipIntroPatch();
                // this patch tracks "Help with Bandits" quest so those units don't merge
                // the patches maintain Helper.Globals.questParties
                var internalClass = AccessTools.Inner(typeof(MerchantNeedsHelpWithLootersIssueQuestBehavior),
                    "MerchantNeedsHelpWithLootersIssueQuest");
                var hourlyTickParty = AccessTools.Method(internalClass, "HourlyTickParty");
                var mobilePartyDestroyed = AccessTools.Method(internalClass, "MobilePartyDestroyed");
                var hourlyTickPartyPostfix = AccessTools.Method(typeof(Patches),
                    nameof(HoursTickPartyPatch));
                var mobilePartyDestroyedPostfix = AccessTools.Method(typeof(Patches),
                    nameof(MobilePartyDestroyedPostfix));
                Log($"Referencing internal class {internalClass}");

                Log($"Patching {hourlyTickParty.FullDescription()}");
                harmony.Patch(hourlyTickParty, null, new HarmonyMethod(hourlyTickPartyPostfix));
                Log($"Patching {mobilePartyDestroyed.FullDescription()}");
                harmony.Patch(mobilePartyDestroyed, null, new HarmonyMethod(mobilePartyDestroyedPostfix));
            }
            catch (Exception ex)
            {
                Log(ex);
            }
        }
    }
}
