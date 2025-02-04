using MCM.Abstractions.Attributes;
using MCM.Abstractions.Attributes.v2;
using MCM.Abstractions.Base.Global;
using MCM.Common;
using Microsoft.Extensions.Logging;
using TaleWorlds.Localization;

// ReSharper disable UnusedAutoPropertyAccessor.Local
// ReSharper disable FieldCanBeMadeReadOnly.Local
// ReSharper disable ConvertToAutoProperty
// ReSharper disable InconsistentNaming    
// ReSharper disable PropertyCanBeMadeInitOnly.Global
// ReSharper disable AutoPropertyCanBeMadeGetOnly.Local
// ReSharper disable PropertyCanBeMadeInitOnly.Local
// ReSharper disable UnusedMember.Global   
// ReSharper disable FieldCanBeMadeReadOnly.Global 
// ReSharper disable ConvertToConstant.Global

namespace BanditMilitias
{
    public class Settings : AttributeGlobalSettings<Settings>
    {
        public delegate void OnSettingsChangedDelegate();
        public static event OnSettingsChangedDelegate OnSettingsChanged;
        public override string FormatType => "json";
        public override string FolderName => "BanditMilitias";

        [SettingPropertyBool("{=BMTrain}Train Militias", HintText = "{=BMTrainDesc}Bandit heroes will train their militias.", Order = 0, RequireRestart = false)]
        [SettingPropertyGroup("{=BMPrimary}Primary Settings", GroupOrder = 0)]
        public bool CanTrain { get; private set; } = true;

        [SettingPropertyInteger("{=BMDailyTrain}Daily Training Chance", 0, 100, HintText = "{=BMDailyTrainDesc}Each day they might train further.", Order = 1, RequireRestart = false)]
        [SettingPropertyGroup("{=BMPrimary}Primary Settings", GroupOrder = 2)]
        public float TrainingChance { get; private set; } = 10;

        [SettingPropertyDropdown("{=BMXpBoost}Militia XP Boost", HintText = "{=BMXpBoostDesc}Hardest grants enough XP to significantly upgrade troops.  Off grants no bonus XP.", Order = 2, RequireRestart = false)]
        [SettingPropertyGroup("{=BMPrimary}Primary Settings")]
        public Dropdown<string> XpGift { get; internal set; } = new(new[] { "{=BMXpOff}Off", "{=BMXpNormal}Normal", "{=BMXpHard}Hard", "{=BMXpHardest}Hardest" }, 1);

        [SettingPropertyInteger("{=BMGrowChance}Growth Chance Percent", 0, 100, HintText = "{=BMGrowChanceDesc}Chance per day that the militia will gain more troops (0 for off).", Order = 3, RequireRestart = false)]
        [SettingPropertyGroup("{=BMPrimary}Primary Settings")]
        public int GrowthChance { get; private set; } = 50;

        [SettingPropertyInteger("{=BMGrowPercent}Growth Percent", 0, 100, HintText = "{=BMGrowPercentDesc}Grow each troop type by this percent.", Order = 4, RequireRestart = false)]
        [SettingPropertyGroup("{=BMPrimary}Primary Settings")]
        public int GrowthPercent { get; private set; } = 1;

        [SettingPropertyBool("{=BMIgnore}Ignore Villagers/Caravans", HintText = "{=BMIgnoreDesc}They won't be attacked by BMs.", Order = 5, RequireRestart = false)]
        [SettingPropertyGroup("{=BMPrimary}Primary Settings")]
        public bool IgnoreVillagersCaravans { get; private set; } = false;

        [SettingPropertyBool("{=BMSpawn}BM Spawn", HintText = "{=BMSpawnDesc}New BM will form spontaneously as well as by merging together normally.", Order = 6, RequireRestart = false)]
        [SettingPropertyGroup("{=BMPrimary}Primary Settings")]
        public bool MilitiaSpawn { get; private set; } = true;

        [SettingPropertyInteger("{=BMSpawnChance}Spawn Chance Percent", 1, 100, HintText = "{=BMSpawnChanceDesc}BM will spawn hourly at this likelihood.", Order = 7, RequireRestart = false)]
        [SettingPropertyGroup("{=BMPrimary}Primary Settings")]
        public int SpawnChance { get; private set; } = 30;

        [SettingPropertyInteger("{=BMCooldown}Change Cooldown", 0, 168, HintText = "{=BMCooldownDesc}BM won't merge or split a second time until this many hours go by.", Order = 8, RequireRestart = false)]
        [SettingPropertyGroup("{=BMPrimary}Primary Settings")]
        public int CooldownHours { get; private set; } = 24;

        // [SettingPropertyInteger("{=BMIdealBoost}Vanilla Bandit Count Boost Percent", 0, 1000, HintText = "{=BMIdealBoostDesc}Increase the vanilla party count by this percentage.", Order = 9, RequireRestart = false)]
        // [SettingPropertyGroup("{=BMPrimary}Primary Settings")]
        // public int IdealCountBoost
        // {
        //     get => idealCountBoost;
        //     set
        //     {
        //         idealCountBoost = value;
        //         // convert once here since it's a hot getter being patched
        //         idealBoostFactor = Convert.ToInt32(value / 100f);
        //     }
        // }

        [SettingPropertyDropdown("{=BMGoldReward}Bandit Hero Gold Reward", Order = 9, RequireRestart = false)]
        [SettingPropertyGroup("{=BMPrimary}Primary Settings")]
        public Dropdown<string> GoldReward { get; internal set; } = new(new[] { "{=BMGoldLow}Low", "{=BMGoldNormal}Normal", "{=BMGoldRich}Rich", "{=BMGoldRichest}Richest" }, 1);

        [SettingPropertyInteger("{=BMDisperse}Disperse Militia Size", 10, 100, HintText = "{=BMDisperseDesc}Militias defeated with fewer than this many remaining troops will be dispersed.", Order = 0, RequireRestart = false)]
        [SettingPropertyGroup("{=BMSizeAdjustments}Size Adjustments", GroupOrder = 2)]
        public int DisperseSize { get; private set; } = 20;

        [SettingPropertyInteger("{=BMMinSize}Minimum Size", 1, 100, HintText = "{=BMMinSizeDesc}No BMs smaller than this will form.", Order = 1, RequireRestart = false)]
        [SettingPropertyGroup("{=BMSizeAdjustments}Size Adjustments")]
        public int MinPartySize { get; private set; } = 20;

        [SettingPropertyInteger("{=BMMergeSize}Mergeable party size", 1, 100, HintText = "{=BMMergeSizeDesc}Small looter and bandit parties won't merge.", Order = 1, RequireRestart = false)]
        [SettingPropertyGroup("{=BMSizeAdjustments}Size Adjustments")]
        public int MergeableSize { get; private set; } = 10;

        [SettingPropertyInteger("{=BMSplit}Random Split Chance", 0, 100, HintText = "{=BMSplitDesc}How likely BM is to split when large enough.", Order = 2, RequireRestart = false)]
        [SettingPropertyGroup("{=BMAdjustments}Militia Adjustments", GroupOrder = 1)]
        public int RandomSplitChance { get; private set; } = 10;

        [SettingPropertyInteger("{=BMMaxValue}Max Item Value", 1000, 1000000, HintText = "{=BMMaxValueDesc}Limit the per-piece value of equipment given to the Heroes.  Mostly for when other mods give you Hero loot.", Order = 7, RequireRestart = false)]
        [SettingPropertyGroup("{=BMAdjustments}Militia Adjustments")]
        public int MaxItemValue { get; private set; } = 10_000;

        [SettingPropertyInteger("{=BMLooter}Looter Conversions", 0, 100, HintText = "How many looters get made into better units when training.", Order = 8, RequireRestart = false)]
        [SettingPropertyGroup("{=BMAdjustments}Militia Adjustments")]
        public int LooterUpgradePercent { get; private set; } = 15;

        [SettingPropertyInteger("{=BMUpgrade}Upgrade Units", 0, 100, HintText = "{=BMUpgradeDesc}Upgrade (at most) this percentage of troops when training occurs.", Order = 12, RequireRestart = false)]
        [SettingPropertyGroup("{=BMAdjustments}Militia Adjustments")]
        public int UpgradeUnitsPercent { get; private set; } = 25;

        [SettingPropertyInteger("{=BMPower}Global Power", 0, 1000, HintText = "{=BMPowerDesc}Major setting.  Setting higher means more, bigger BMs.", Order = 13, RequireRestart = false)]
        [SettingPropertyGroup("{=BMAdjustments}Militia Adjustments")]
        public int GlobalPowerPercent { get; private set; } = 15;

        [SettingPropertyInteger("{=BMTier}Max Training Tier", 1, 6, HintText = "{=BMTierDesc}BM won't train any units past this tier.", Order = 14, RequireRestart = false)]
        [SettingPropertyGroup("{=BMAdjustments}Militia Adjustments")]
        public int MaxTrainingTier { get; private set; } = 4;

        [SettingPropertyInteger("{=BMWeaker}Ignore Weaker Parties", 0, 100, HintText = "{=BMWeakerDesc}10 means any party 10% weaker will be ignored.  100 attacks without restriction.", Order = 9, RequireRestart = false)]
        [SettingPropertyGroup("{=BMAdjustments}Militia Adjustments")]
        public int MaxStrengthDeltaPercent { get; private set; } = 10;

        [SettingPropertyBool("{=BMPillage}Allow Pillaging", HintText = "{=BMPillageDesc}Allow PILLAGING!.", Order = 10, RequireRestart = false)]
        [SettingPropertyGroup("{=BMAdjustments}Militia Adjustments")]
        public bool AllowPillaging { get; private set; } = true;
        
        [SettingPropertyFloatingInteger("{=BMPillageChance}Pillaging Chance", 0, 100, HintText = "{=BMPillageChanceDesc}The chance of Bandit Militias AI to consider raiding a village. It triggers once per in-game hour for every bandit militia party, so a smaller value is advised.", Order = 11, RequireRestart = false)]
        [SettingPropertyGroup("{=BMAdjustments}Militia Adjustments")]
        public float PillagingChance { get; private set; } = 1;
        
        [SettingPropertyBool("{=BMIgnoreSizePenalty}Ignore Size Penalty", HintText = "{=BMIgnoreSizePenaltyDesc}Bandit Militias will move at normal speed regardless of its party size.", Order = 15, RequireRestart = false)]
        [SettingPropertyGroup("{=BMAdjustments}Militia Adjustments")]
        public bool IgnoreSizePenalty { get; private set; } = true;

        [SettingPropertyText("{=BMStringSetting}Bandit Militia", Order = 0, HintText = "{=BMStringSettingDesc}What to name a Bandit Militia.", RequireRestart = false)]
        public string BanditMilitiaString { get; set; } = "Bandit Militia";

        [SettingPropertyText("{=BMLeaderlessStringSetting}Leaderless Bandit Militia", Order = 1, HintText = "{=BMLeaderlessStringSettingDesc}What to name a Bandit Militia with no leader.", RequireRestart = false)]
        public string LeaderlessBanditMilitiaString { get; set; } = "Leaderless Bandit Militia";

        [SettingPropertyBool("{=BMMarkers}Militia Map Markers", HintText = "{=BMMarkersDesc}Have omniscient view of BMs.", Order = 2, RequireRestart = false)]
        public bool Trackers { get; private set; } = false;

        [SettingPropertyInteger("{=BMTrackSize}Minimum BM Size To Track", 1, 500, HintText = "{=BMTrackSizeDesc}Any smaller BMs won't be tracked.", Order = 3, RequireRestart = false)]
        public int TrackedSizeMinimum { get; private set; } = 50;

        [SettingPropertyBool("{=BMBanners}Random Banners", HintText = "{=BMBannersDesc}BMs will have unique banners, or basic bandit clan ones.", Order = 4, RequireRestart = false)]
        public bool RandomBanners { get; set; } = true;

        [SettingPropertyBool("{=BMRaidNotices}Village raid notices", HintText = "{=BMRaidNoticesDesc}When your fiefs are raided you'll see a banner message.", Order = 5, RequireRestart = false)]
        public bool ShowRaids { get; set; } = true;
        
        [SettingPropertyBool("{=BMSkipConversations}Skip Conversations", HintText = "{=BMSkipConversationsDesc}Skip conversations with Bandit Militias. You won't be able to bribe them if enabled.", Order = 6, RequireRestart = false)]
        public bool SkipConversations { get; set; } = false;
        
        [SettingPropertyBool("{=BMRemovePrisonerMessages}Remove prisoner messages", HintText = "{=BMRemovePrisonerMessagesDesc}Remove the messages of Bandit Militia Heroes being taken or released as prisoners.", Order = 6, RequireRestart = false)]
        public bool RemovePrisonerMessages { get; set; } = true;
        
        [SettingPropertyBool("{=BMCheckVoiceGender}Check Voice Gender", HintText = "{=BMCheckVoiceGenderDesc}Double-check if the bandit voice lines match the gender. There are some official voice lines with male voices but don't specify gender, so female bandit leaders will speak with male voices if this option is disabled.", Order = 7, RequireRestart = false)]
        public bool CheckVoiceGender { get; set; } = true;

        [SettingPropertyDropdown("{=BMLoggingLevel}Log Level", HintText = "{=BMDebugDesc}Change the log level, requires restart.",
            Order = 98, RequireRestart = true)]
        public Dropdown<LogLevel> MinLogLevel { get; private set; } = new([LogLevel.Trace, LogLevel.Debug, LogLevel.Information, LogLevel.Warning, LogLevel.Error, LogLevel.Critical, LogLevel.None], 2);

        [SettingPropertyBool("{=BMTesting}Testing Mode", HintText = "{=BMTestingDesc}Teleports BMs to you.", Order = 99, RequireRestart = false)]
        public bool TestingMode { get; internal set; }

        private const string id = "BanditMilitias";

        private string displayName = $"BanditMilitias {typeof(Settings).Assembly.GetName().Version.ToString(3)}";
        // private int idealCountBoost = 5;
        // internal int idealBoostFactor;

        public override void OnPropertyChanged(string propertyName = null)
        {
            base.OnPropertyChanged(propertyName);
            VerifyProperties();

            OnSettingsChanged?.Invoke();
        }

        private void VerifyProperties()
        {
            if (string.IsNullOrWhiteSpace(BanditMilitiaString))
            {
                BanditMilitiaString = new TextObject("{=BMStringSettingDefault}Bandit Militia").ToString();
            }

            if (string.IsNullOrWhiteSpace(LeaderlessBanditMilitiaString))
            {
                LeaderlessBanditMilitiaString = new TextObject("{=BMLeaderlessStringSettingDefault}Leaderless Bandit Militia").ToString();
            }
        }

        public override string Id => id;
        public override string DisplayName => displayName;
    }
}
