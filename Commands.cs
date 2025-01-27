using System;
using System.Diagnostics;
using System.Linq;
using Microsoft.Extensions.Logging;
using SandBox.View.Map;
using SandBox.ViewModelCollection.Map;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.LinQuick;
using TaleWorlds.ObjectSystem;
using static BanditMilitias.Globals;
using static BanditMilitias.Helper;

// ReSharper disable InconsistentNaming  

namespace BanditMilitias;

internal class Commands
{
    private static bool IsCat => SubModule.MEOWMEOW;
    private static ILogger _logger;
    private static ILogger Logger => _logger ??= LogFactory.Get<Commands>();

    internal static void OnTick()
    {
        bool superKey = Campaign.Current != null
                        && (Input.IsKeyDown(InputKey.LeftControl) || Input.IsKeyDown(InputKey.RightControl))
                        && (Input.IsKeyDown(InputKey.LeftAlt) || Input.IsKeyDown(InputKey.RightAlt))
                        && (Input.IsKeyDown(InputKey.LeftShift) || Input.IsKeyDown(InputKey.RightShift));

        if (IsCat)
        {
            if (superKey && Input.IsKeyPressed(InputKey.F9)) ShowAllPartiesOnMap();

            if (Input.IsKeyPressed(InputKey.F1)) TeleportToRandomArmy();

            if (Input.IsKeyPressed(InputKey.F2)) TeleportAllRegularBanditsToMe();

            if (Input.IsKeyPressed(InputKey.F3)) TeleportToRandomSeasideHideout();

            if (superKey && Input.IsKeyPressed(InputKey.B)) Debugger.Break();

            if (superKey && Input.IsKeyPressed(InputKey.F10))
                MobileParty.MainParty.ItemRoster.AddToCounts(MBObjectManager.Instance.GetObject<ItemObject>("grain"), 10000);
        }

        if (superKey && Input.IsKeyPressed(InputKey.F11)) ToggleTestingMode();

        if (superKey && Input.IsKeyPressed(InputKey.F12)) PrintAllBanditMilitiaStats();

        bool nukeCommand = (Input.IsKeyDown(InputKey.LeftControl) || Input.IsKeyDown(InputKey.RightControl)) &&
                            (Input.IsKeyDown(InputKey.LeftAlt) || Input.IsKeyDown(InputKey.RightAlt)) &&
                            Input.IsKeyPressed(InputKey.N);
        
        if (nukeCommand) TryNuke();
    }

    private static void PrintAllBanditMilitiaStats()
    {
        foreach (MobileParty militia in MobileParty.All.WhereQ(m => m.IsBM()).OrderBy(x => x.MemberRoster.TotalManCount))
        {
            Logger.LogDebug($">> {militia.LeaderHero.Name,-30}: {militia.MemberRoster.TotalManCount:F1}/{militia.Party.TotalStrength:0}");
            Logger.LogDebug($"  Heroes: {militia.MemberRoster.TotalHeroes}");

            for (int tier = 1; tier <= 6; tier++)
            {
                // ReSharper disable once AccessToModifiedClosure
                int count = militia.MemberRoster.GetTroopRoster().WhereQ(x => x.Character.Tier == tier).SumQ(x => x.Number);
                if (count > 0)
                {
                    Logger.LogDebug($"  Tier {tier}: {count}");
                }
            }

            Logger.LogDebug($"Cavalry: {NumMountedTroops(militia.MemberRoster)} ({(float)NumMountedTroops(militia.MemberRoster) / militia.MemberRoster.TotalManCount * 100}%)");
            if (!((float)NumMountedTroops(militia.MemberRoster) / (militia.MemberRoster.TotalManCount * 100) > militia.MemberRoster.TotalManCount / 2f)) continue;
            Logger.LogDebug(new string('*', 80));
            Logger.LogDebug(new string('*', 80));
        }

        Logger.LogDebug($">>> Total {MobileParty.All.CountQ(m => m.IsBM())} = {MobileParty.All.WhereQ(m => m.IsBM()).SelectQ(x => x.MemberRoster.TotalManCount).Sum()} ({MilitiaPowerPercent}%)");
    }

    private static void ToggleTestingMode()
    {
        Globals.Settings.TestingMode = !Globals.Settings.TestingMode;
        InformationManager.DisplayMessage(new InformationMessage("Testing mode: " + Globals.Settings.TestingMode));
    }

    private static void TryNuke()
    {
        try
        {
            Logger.LogInformation("Clearing mod data.");
            // no idea why it takes several iterations to clean up certain situations, but it does
            for (int index = 0; index < 1; index++)
            {
                Nuke();
            }

            DoPowerCalculations(true);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error clearing mod data.");
        }
    }

    private static void TeleportToRandomSeasideHideout()
    {
        Settlement hideout = Hideouts.WhereQ(h => h.StringId.StartsWith("hideout_seaside") && h.Hideout.IsInfested).GetRandomElementInefficiently();
        if (hideout is null) return;
        InformationManager.DisplayMessage(new InformationMessage($"Hideout: {hideout.StringId}"));
        MobileParty.MainParty.Position2D = hideout.GatePosition;
        MapScreen.Instance.TeleportCameraToMainParty();
    }

    private static void TeleportAllRegularBanditsToMe()
    {
        foreach (MobileParty mobileParty in MobileParty.AllBanditParties.Where(mobileParty => !mobileParty.StringId.StartsWith("Bandit_Militia")))
        {
            mobileParty.Position2D = MobileParty.MainParty.Position2D;
        }
    }

    private static void TeleportToRandomArmy()
    {
        MobileParty party = MobileParty.All.WhereQ(m => m.Army != null).GetRandomElementInefficiently();
        if (party is not null)
            MobileParty.MainParty.Position2D = party.Position2D;
        MapScreen.Instance.TeleportCameraToMainParty();
    }

    private static void ShowAllPartiesOnMap()
    {
        foreach (MobileParty m in MobileParty.All)
            Globals.MapMobilePartyTrackerVM.Trackers.Add(new MobilePartyTrackItemVM(m, MapScreen.Instance._mapCameraView.Camera, null));
    }
}