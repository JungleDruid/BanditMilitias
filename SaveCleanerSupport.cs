using SaveCleaner;
using TaleWorlds.CampaignSystem;

namespace BanditMilitias;

internal static class SaveCleanerSupport
{
    public static void Register()
    {
        var addon = new SaveCleanerAddon(SubModule.harmony.Id, SubModule.Name);
        addon.Essential += IsEssential;
        addon.Removable += IsRemovable;
        addon.OnWipe += OnWipe;
        addon.Register<SubModule>();
    }

    private static bool OnWipe(SaveCleanerAddon addon)
    {
        return Helper.Nuke();
    }

    private static bool IsRemovable(SaveCleanerAddon addon, object obj)
    {
        return obj switch
        {
            Hero hero => hero.IsBM(),
            CharacterObject character => character.IsBM(),
            _ => false
        };
    }

    private static bool IsEssential(SaveCleanerAddon addon, object obj)
    {
        return obj switch
        {
            Hero hero => Globals.Heroes.Contains(hero),
            CharacterObject character => character.IsHero && Globals.Heroes.Contains(character.HeroObject),
            _ => false
        };
    }
}