using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BanditMilitias.Patches;
using Bannerlord.ButterLib.Common.Extensions;
using HarmonyLib;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Serilog.Events;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.LinQuick;
using TaleWorlds.MountAndBlade;
using static BanditMilitias.Globals;
using Module = TaleWorlds.MountAndBlade.Module;

// ReSharper disable ConditionIsAlwaysTrueOrFalse
// ReSharper disable ClassNeverInstantiated.Global
// ReSharper disable UnusedMember.Local
// ReSharper disable InconsistentNaming

namespace BanditMilitias
{
    public class SubModule : MBSubModuleBase
    {
        public static readonly string Name = typeof(SubModule).Namespace!;
        public static readonly PlatformDirectoryPath ConfigDir = EngineFilePaths.ConfigsPath + $"/ModSettings/Global/{Name}";
        public static readonly bool MEOWMEOW = File.Exists(new PlatformFilePath(ConfigDir, "i_am_a_cat").FileFullPath);
        public static readonly Harmony harmony = new("ca.gnivler.bannerlord.BanditMilitias");
        private static ILogger _logger;
        private static ILogger Logger => _logger ??= LogFactory.Get<SubModule>();
        public static SubModule Instance {get; private set;}

        public void OnServiceRegistration()
        {
            string configPath = new PlatformFilePath(ConfigDir, $"{Name}.json").FileFullPath;
            var config = File.Exists(configPath) ? JsonConvert.DeserializeObject<BanditMilitiaConfig>(File.ReadAllText(configPath)) : new BanditMilitiaConfig();
            this.AddSerilogLoggerProvider($"{Name}.log", [$"{Name}.*"], o => o.MinimumLevel.Is((LogEventLevel)config.MinLogLevel));
        }

        protected override void OnSubModuleLoad()
        {
            Instance = this;
            OnServiceRegistration();
            if (MEOWMEOW)
                AccessTools.Field(typeof(Module), "_splashScreenPlayed").SetValue(Module.CurrentModule, true);
            RunManualPatches();
            harmony.PatchAll(Assembly.GetExecutingAssembly());
        }

        // need to cache the banners before CEK adds background colours which
        // causes custom banners to crash for reasons unknown
        private static void CacheBanners()
        {
            for (var i = 0; i < 5000; i++)
            {
                Banners.Add((Banner)AccessTools.Method(typeof(Banner), "CreateRandomBannerInternal")
                    .Invoke(typeof(Banner), new object[] { MBRandom.RandomInt(0, int.MaxValue), -1 }));
            }
        }

        protected override void OnBeforeInitialModuleScreenSetAsRoot()
        {
            Globals.Settings = Settings.Instance;
            Globals.Settings!.XpGift = new(Globals.DifficultyXpMap.Keys.SelectQ(k => k.ToString()), 1);
            Globals.Settings!.GoldReward = new(Globals.GoldMap.Keys.SelectQ(k => k.ToString()), 1);
            Logger.LogInformation($"{Globals.Settings!.DisplayName} starting up...");
        }

        // Calradia Expanded: Kingdoms
        private static void AdjustForLoadOrder()
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            var BM = assemblies.First(a => a.FullName.StartsWith("BanditMilitias"));
            var CEK = assemblies.FirstOrDefaultQ(x => x.FullName.StartsWith("CalradiaExpandedKingdoms"));
            if (CEK is not null)
                if (assemblies.FindIndex(a => a == BM) > assemblies.FindIndex(a => a == CEK))
                    Globals.Settings.RandomBanners = false;
        }

        protected override void OnApplicationTick(float dt)
        {
            Commands.OnTick();
        }

        protected override void OnGameStart(Game game, IGameStarter gameStarterObject)
        {
            if (gameStarterObject is CampaignGameStarter gameStarter)
                gameStarter.AddBehavior(new MilitiaBehavior());
            Settings.OnSettingsChanged += OnSettingsChanged;
        }

        private static void OnSettingsChanged()
        {
            MethodInfo ResetCached = AccessTools.Method(typeof(MobileParty), "ResetCached");
            foreach (ModBanditMilitiaPartyComponent bm in Helper.GetCachedBMs(true).ToArrayQ())
            {
                bm.ClearCachedName();
                if (bm.MobileParty is null) continue;
                bm.MobileParty.SetCustomName(null);
                ResetCached.Invoke(bm.MobileParty, null);
            }

            Helper.RefreshTrackers();
        }

        public override void OnGameEnd(Game game)
        {
            Globals.Heroes.Clear();
            Settings.OnSettingsChanged -= OnSettingsChanged;
        }

        public override void OnGameInitializationFinished(Game game)
        {
            base.OnGameInitializationFinished(game);
            CacheBanners();
            if (MEOWMEOW)
            {
                CampaignCheats.SetMainPartyAttackable(new List<string> { "0" });
                CampaignCheats.SetCampaignSpeed(new List<string> { "100" });
            }
            // if (MEOWMEOW)
            //     Dev.RunDevPatches();
        }

        private static void RunManualPatches()
        {
            try
            {
                // fix issue in ServeAsSoldier where a Deserters Party is created without being a quest party
                var original = AccessTools.Method("ServeAsSoldier.ExtortionByDesertersEvent:CreateDeserterParty");
                if (original is not null)
                    harmony.Patch(original, postfix: new HarmonyMethod(AccessTools.Method(typeof(MiscPatches), nameof(MiscPatches.PatchSaSDeserters))));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error while running manual patches.");
            }
        }
    }
}
