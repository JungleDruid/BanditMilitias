using TaleWorlds.CampaignSystem;

namespace BanditMilitias;

public static class SaveCleanerAddon
{
    public static void AddConditions()
    {
        SaveCleaner.CleanConditions.AddForceKeep<SubModule>(ForceKeep);
    }

    public static void RemoveConditions()
    {
        SaveCleaner.CleanConditions.RemoveAllForceKeeps<SubModule>();
    }

    private static bool ForceKeep(object obj)
    {
        return obj switch
        {
            Hero hero => Globals.Heroes.Contains(hero),
            CharacterObject { IsHero: true } c => Globals.Heroes.Contains(c.HeroObject),
            _ => false
        };
    }
}