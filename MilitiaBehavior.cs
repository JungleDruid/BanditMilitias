using System;
using System.Collections.Generic;
using System.Linq;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.LinQuick;
using TaleWorlds.TwoDimension;
using static BanditMilitias.Helper;
using static BanditMilitias.Globals;

// ReSharper disable InconsistentNaming

namespace BanditMilitias
{
    public class MilitiaBehavior : CampaignBehaviorBase
    {
        private const double SmallChance = 0.0005;
        internal const float Increment = 5;
        private const float EffectRadius = 100;
        private const int AdjustRadius = 50;
        private const int settlementFindRange = 200;

        public override void RegisterEvents()
        {
            CampaignEvents.VillageBeingRaided.AddNonSerializedListener(this, village =>
            {
                if (village.Settlement.Party?.MapEvent is null
                    || !village.Settlement.Party.MapEvent.PartiesOnSide(BattleSideEnum.Attacker)
                        .AnyQ(m => m.Party.IsMobile && m.Party.MobileParty.IsBM())) return;
                PartyBase party = village.Settlement.Party.MapEvent.PartiesOnSide(BattleSideEnum.Attacker).First().Party;
                Log.Debug?.Log($"{party.Name}({party.MobileParty.StringId} is raiding {village.Name}.");
                if (Globals.Settings.ShowRaids && village.Owner?.LeaderHero == Hero.MainHero)
                {
                    InformationManager.DisplayMessage(new InformationMessage($"{village.Name} is being raided by {party.Name}!"));
                }
            });
            CampaignEvents.RaidCompletedEvent.AddNonSerializedListener(this, (_, m) =>
            {
                if (!m.AttackerSide.Parties.AnyQ(mep => mep.Party.IsMobile && mep.Party.MobileParty.IsBM())) return;
                PartyBase party = m.AttackerSide.Parties.First().Party;
                Log.Debug?.Log($"{party.Name}({party.MobileParty.StringId} has done raiding {m.MapEventSettlement?.Name}.");
                party.MobileParty.Ai.SetDoNotMakeNewDecisions(false);
                party.MobileParty.Ai.SetMoveModeHold();
                if (Globals.Settings.ShowRaids)
                {
                    InformationManager.DisplayMessage(
                        new InformationMessage($"{m.MapEventSettlement?.Name} raided!  " +
                                               $"{party.Name} is fat with loot near {SettlementHelper.FindNearestTown().Name}!"));
                }
            });

            CampaignEvents.TickPartialHourlyAiEvent.AddNonSerializedListener(this, TickPartialHourlyAiEvent);
            CampaignEvents.DailyTickPartyEvent.AddNonSerializedListener(this, DailyTickPartyEvent);
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, SpawnBM);
            CampaignEvents.MobilePartyDestroyed.AddNonSerializedListener(this, MobilePartyDestroyed);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, DailyTick);
        }

        private void DailyTick()
        {
            if (Globals.Settings.Debug)
            {
                var bandits = MobileParty.AllBanditParties.WhereQ(p => !p.IsBM());
                Log.Debug?.Log($"[Info] Total regular bandit parties, Day {CampaignTime.Now.GetDayOfYear}, {bandits.Count()}");
                foreach (var hero in Hero.AllAliveHeroes.WhereQ(h => h.PartyBelongedTo.IsBM()))
                {
                    if (hero.BattleEquipment[5].IsEmpty)
                    {
                        Log.Debug?.Log($"[Warning] Naked hero {hero.PartyBelongedTo.Name}");
                    }
                }

                foreach (var hero in Heroes.WhereQ(h => h.IsDead))
                {
                    Log.Debug?.Log($"[Warning] {hero.Name} is dead. [{hero.DeathMark} during Day {hero.DeathDay.GetDayOfYear}]");
                }

                foreach (var hero in Heroes.WhereQ(h => h.PartyBelongedTo is null).ToArrayQ())
                {
                    Log.Debug?.Log($"[Info] Removing stray hero {hero.Name} from daily cleanup. HeroState: {hero.HeroState}");
                    hero.RemoveMilitiaHero();
                }

                foreach (var hero in Heroes.WhereQ(h => h.IsPrisoner))
                {
                    Log.Debug?.Log($"[Warning] {hero.Name} is a prisoner.");
                }

                foreach (var bm in AllBMs.WhereQ(c => c.MobileParty.LeaderHero is null))
                {
                    Log.Debug?.Log($"[Warning] {bm.MobileParty.Name}({bm.MobileParty.StringId}) does not have a leader.");
                }
            }
        }

        private static void MobilePartyDestroyed(MobileParty mobileParty, PartyBase destroyer)
        {
            // Avoidance-bomb all BMs in the area
            int AvoidanceIncrease() => Rng.Next(15, 35);
            if (!mobileParty.IsBM() || destroyer?.LeaderHero is null)
                return;

            destroyer.MobileParty.GetBM()?.Avoidance.Remove(mobileParty.LeaderHero);
            foreach (var BM in GetCachedBMs().WhereQ(bm =>
                         bm.MobileParty.Position2D.Distance(mobileParty.Position2D) < EffectRadius))
            {
                if (BM.Avoidance.TryGetValue(destroyer.LeaderHero, out _))
                    BM.Avoidance[destroyer.LeaderHero] += AvoidanceIncrease();
                else
                    BM.Avoidance.Add(destroyer.LeaderHero, AvoidanceIncrease());
            }
        }

        private static void TickPartialHourlyAiEvent(MobileParty mobileParty)
        {
            if (mobileParty.PartyComponent is not (BanditPartyComponent or ModBanditMilitiaPartyComponent))
                return;

            if (mobileParty.MemberRoster.TotalManCount < Globals.Settings.MergeableSize)
                return;

            if (mobileParty.IsUsedByAQuest())
                return;

            // they will evacuate hideouts and not chase caravans
            if (mobileParty.PartyComponent is BanditPartyComponent)
            {
                if ((mobileParty.CurrentSettlement is not null
                     && mobileParty.Ai.AiBehaviorMapEntity is Settlement { IsHideout: true })
                    || mobileParty.Ai.AiBehaviorMapEntity is MobileParty { IsCaravan: true })
                    return;
            }

            if (mobileParty.MapEvent is not null)
                return;

            if (mobileParty.IsBM())
            {
                // let another hero in the party take over the leaderless militia
                // the game auto-replaces the leader if there's another hero in the party, just putting this here in case of some oversight
                if (mobileParty.LeaderHero is null && mobileParty.MemberRoster.TotalHeroes > 0)
                {
                    var leader = mobileParty.MemberRoster.GetTroopRoster()
                        .WhereQ(t => t.Character.IsHero)
                        .OrderByQ(t => -t.Character.HeroObject.Power)
                        .First().Character.HeroObject;
                    mobileParty.ChangePartyLeader(leader);
                    mobileParty.Ai.SetMoveModeHold();
                    return;
                }
            }
            
            // cancel merge if the target has changed its behavior
            if (mobileParty.DefaultBehavior == AiBehavior.EngageParty 
                && mobileParty.TargetParty is not null 
                && !FactionManager.IsAtWarAgainstFaction(mobileParty.MapFaction, mobileParty.TargetParty.MapFaction))
            {
                if (mobileParty.TargetParty.DefaultBehavior != AiBehavior.EngageParty ||
                    mobileParty.TargetParty.TargetParty != mobileParty)
                {
                    mobileParty.Ai.SetMoveModeHold();
                    BMThink(mobileParty);
                    return;
                }
            }

            // near any Hideouts?
            if (mobileParty.IsBM())
            {
                var locatableSearchData = Settlement.StartFindingLocatablesAroundPosition(mobileParty.Position2D, MinDistanceFromHideout);
                for (Settlement settlement =
                         Settlement.FindNextLocatable(ref locatableSearchData); settlement != null; settlement =
                         Settlement.FindNextLocatable(ref locatableSearchData))
                {
                    if (!settlement.IsHideout) continue;
                    BMThink(mobileParty);
                    return;
                }
            }

            // BM changed too recently?
            if (mobileParty.IsBM()
                && CampaignTime.Now < mobileParty.GetBM().LastMergedOrSplitDate + CampaignTime.Hours(Globals.Settings.CooldownHours))
            {
                BMThink(mobileParty);
                return;
            }

            List<MobileParty> nearbyBandits = [];
            {
                var locatableSearchData = MobileParty.StartFindingLocatablesAroundPosition(mobileParty.Position2D, FindRadius);
                for (MobileParty party =
                         MobileParty.FindNextLocatable(ref locatableSearchData); party != null; party =
                         MobileParty.FindNextLocatable(ref locatableSearchData))
                {
                    if (party == mobileParty) continue;
                    if (party.IsBandit && party.MapEvent is null &&
                        mobileParty.MemberRoster.TotalManCount > Globals.Settings.MergeableSize &&
                        mobileParty.MemberRoster.TotalManCount + mobileParty.MemberRoster.TotalManCount >= Globals.Settings.MinPartySize &&
                        IsAvailableBanditParty(party))
                    {
                        nearbyBandits.Add(party);
                    }
                }
            }
            
            if (nearbyBandits.Count == 0)
            {
                BMThink(mobileParty);
                return;
            }

            MobileParty mergeTarget = null;
            foreach (var target in nearbyBandits.OrderByQ(m => m.Position2D.Distance(mobileParty.Position2D)))
            {
                var militiaTotalCount = mobileParty.MemberRoster.TotalManCount + target.MemberRoster.TotalManCount;
                if (militiaTotalCount < Globals.Settings.MinPartySize || militiaTotalCount > CalculatedMaxPartySize)
                    continue;

                if (target.IsBM())
                {
                    CampaignTime? targetLastChangeDate = target.GetBM().LastMergedOrSplitDate;
                    if (CampaignTime.Now < targetLastChangeDate + CampaignTime.Hours(Globals.Settings.CooldownHours))
                        continue;
                }

                if (NumMountedTroops(mobileParty.MemberRoster) + NumMountedTroops(target.MemberRoster) > militiaTotalCount / 2)
                    continue;

                mergeTarget = target;
                break;
            }

            if (mergeTarget is null)
            {
                BMThink(mobileParty);
                return;
            }

            if (Campaign.Current.Models.MapDistanceModel.GetDistance(mergeTarget, mobileParty) > MergeDistance
                && mobileParty.TargetParty != mergeTarget)
            {
                //Log.Debug?.Log($"{new string('>', 100)} MOVING {mobileParty.StringId,20} {mergeTarget.StringId,20}");
                mobileParty.Ai.SetMoveEngageParty(mergeTarget);
                mergeTarget.Ai.SetMoveEngageParty(mobileParty);
            }
        }

        internal static void BMThink(MobileParty mobileParty)
        {
            Settlement target;
            switch (mobileParty.Ai.DefaultBehavior)
            {
                case AiBehavior.None:
                case AiBehavior.Hold:
                    if (mobileParty.TargetSettlement is null)
                    {
                        target = Settlement.All.WhereQ(s => s.Position2D.Distance(mobileParty.Position2D) < settlementFindRange).GetRandomElementInefficiently();
                        mobileParty.Ai.SetMovePatrolAroundSettlement(target);
                    }

                    break;
                case AiBehavior.GoToSettlement:
                    // Sometimes they might be stuck in a hideout
                    if (mobileParty.TargetSettlement?.IsHideout == true)
                    {
                        if (!mobileParty.IsEngaging && mobileParty.Position2D.Distance(mobileParty.TargetSettlement.Position2D) == 0f)
                        {
                            mobileParty.Ai.SetMoveModeHold();
                        }
                    }
                    break;
                case AiBehavior.PatrolAroundPoint:
                    // PILLAGE!
                    if (Globals.Settings.AllowPillaging
                        && mobileParty.LeaderHero is not null
                        && mobileParty.Party.TotalStrength > MilitiaPartyAveragePower
                        && MBRandom.RandomFloat < Globals.Settings.PillagingChance * 0.01f
                        && GetCachedBMs().CountQ(m => m.MobileParty.ShortTermBehavior is AiBehavior.RaidSettlement) < RaidCap)
                    {
                        target = SettlementHelper.FindNearestVillage(s =>
                        {
                            // JetBrains Rider suggested this insanity
                            if (s.Village is { VillageState: Village.VillageStates.BeingRaided or Village.VillageStates.Looted }
                                || s.Owner is null
                                || s.GetValue() <= 0)
                            {
                                return false;
                            }

                            return true;
                        }, mobileParty);

                        var BM = mobileParty.GetBM();
                        if (BM is null)
                            return;

                        if (BM.Avoidance.ContainsKey(target.Owner)
                            && Rng.NextDouble() * 100 <= BM.Avoidance[target.Owner])
                        {
                            Log.Debug?.Log($"||| {mobileParty.Name} avoided pillaging {target}");
                            break;
                        }

                        if (target.OwnerClan == Hero.MainHero.Clan)
                            InformationManager.DisplayMessage(new InformationMessage($"{mobileParty.Name} is raiding your village {target.Name} near {target.Town?.Name}!"));

                        Log.Debug?.Log($"{mobileParty.Name}({mobileParty.StringId} has decided to raid {target.Name}.");
                        mobileParty.Ai.SetMoveRaidSettlement(target);
                        mobileParty.Ai.SetDoNotMakeNewDecisions(true);
                    }

                    break;
            }
        }

        private static void DailyTickPartyEvent(MobileParty mobileParty)
        {
            if (mobileParty.IsBM())
            {
                if ((int)CampaignTime.Now.ToWeeks % CampaignTime.DaysInWeek == 0
                    && Globals.Settings.AllowPillaging)
                {
                    AdjustAvoidance(mobileParty);
                }

                TryGrowing(mobileParty);
                if (Rng.NextDouble() <= Globals.Settings.TrainingChance)
                {
                    TrainMilitia(mobileParty);
                }

                TrySplitParty(mobileParty);
            }
        }

        private static void AdjustAvoidance(MobileParty mobileParty)
        {
            foreach (var BM in GetCachedBMs(true)
                         .WhereQ(bm => bm.Leader is not null
                                       && bm.MobileParty.Position2D.Distance(mobileParty.Position2D) < AdjustRadius))
            {
                foreach (var kvp in BM.Avoidance)
                {
                    if (BM.Avoidance.ContainsKey(kvp.Key))
                    {
                        if (kvp.Value > BM.Avoidance[kvp.Key])
                        {
                            BM.Avoidance[kvp.Key] -= Increment;
                        }
                        else if (kvp.Value < BM.Avoidance[kvp.Key])
                        {
                            BM.Avoidance[kvp.Key] += Increment;
                        }
                    }
                }
            }
        }

        private static void TryGrowing(MobileParty mobileParty)
        {
            if (Globals.Settings.GrowthPercent > 0
                && MilitiaPowerPercent <= Globals.Settings.GlobalPowerPercent
                && mobileParty.ShortTermBehavior != AiBehavior.FleeToPoint
                && mobileParty.MapEvent is null
                && IsAvailableBanditParty(mobileParty)
                && Rng.NextDouble() <= Globals.Settings.GrowthChance / 100f)
            {
                var eligibleToGrow = mobileParty.MemberRoster.GetTroopRoster().WhereQ(rosterElement =>
                        rosterElement.Character.Tier < Globals.Settings.MaxTrainingTier
                        && !rosterElement.Character.IsHero
                        && mobileParty.ShortTermBehavior != AiBehavior.FleeToPoint
                        && !mobileParty.IsVisible
                        && !rosterElement.Character.Name.ToString().StartsWith("Glorious"))
                    .ToListQ();
                if (eligibleToGrow.Any())
                {
                    var growthAmount = mobileParty.MemberRoster.TotalManCount * Globals.Settings.GrowthPercent / 100f;
                    // bump up growth to reach GlobalPowerPercent (synthetic but it helps warm up militia population)
                    // thanks Erythion!
                    var boost = CalculatedGlobalPowerLimit / GlobalMilitiaPower;
                    growthAmount += Globals.Settings.GlobalPowerPercent / 100f * boost;
                    growthAmount = Mathf.Clamp(growthAmount, 1, 50);
                    //Log.Debug?.Log($"+++ Growing {mobileParty.Name}, total: {mobileParty.MemberRoster.TotalManCount}");
                    for (var i = 0; i < growthAmount && mobileParty.MemberRoster.TotalManCount + 1 < CalculatedMaxPartySize; i++)
                    {
                        var troop = eligibleToGrow.GetRandomElement().Character;
                        if (GlobalMilitiaPower + troop.GetPower() < CalculatedGlobalPowerLimit)
                        {
                            mobileParty.MemberRoster.AddToCounts(troop, 1);
                        }
                    }

                    AdjustCavalryCount(mobileParty.MemberRoster);
                    //var troopString = $"{mobileParty.Party.NumberOfAllMembers} troop" + (mobileParty.Party.NumberOfAllMembers > 1 ? "s" : "");
                    //var strengthString = $"{Math.Round(mobileParty.Party.TotalStrength)} strength";
                    //Log.Debug?.Log($"{$"Grown to",-70} | {troopString,10} | {strengthString,12} |");
                    DoPowerCalculations();
                    // Log.Debug?.Log($"Grown to: {mobileParty.MemberRoster.TotalManCount}");
                }
            }
        }

        private static void SpawnBM()
        {
            if (!Globals.Settings.MilitiaSpawn)
                return;

            try
            {
                var settlement = Settlement.All
                    .WhereQ(s => s.IsHideout && s.GetTrackDistanceToMainAgent() > 100)
                    .GetRandomElementInefficiently();
                if (settlement == null)
                {
                    Log.Debug?.Log($"[Warning] SpawnBM: No hideout available.");
                    return;
                }
                
                for (var i = 0;
                     MilitiaPowerPercent + 1 <= Globals.Settings.GlobalPowerPercent
                     && i < (Globals.Settings.GlobalPowerPercent - MilitiaPowerPercent) / 24f;
                     i++)
                {
                    if (Rng.Next(0, 101) > Globals.Settings.SpawnChance)
                        continue;

                    Clan clan;
                    // ROT
                    if (settlement.OwnerClan == Wights)
                        clan = Clan.BanditFactions.Except(new[] { Wights }).GetRandomElementInefficiently();
                    else
                        clan = settlement.OwnerClan;
                    var min = Convert.ToInt32(Globals.Settings.MinPartySize);
                    var max = Convert.ToInt32(CalculatedMaxPartySize);
                    // if the MinPartySize is cranked it will throw ArgumentOutOfRangeException
                    if (max < min)
                        max = min;
                    var roster = TroopRoster.CreateDummyTroopRoster();
                    var size = Convert.ToInt32(Rng.Next(min, max + 1) / 2f);
                    var foot = Rng.Next(40, 61);
                    var range = Rng.Next(20, Rng.Next(35, 100 - foot) + 1);
                    var horse = 100 - foot - range;
                    // DRM has no cavalry
                    if (Globals.BasicCavalry.Count == 0)
                    {
                        foot += horse % 2 == 0
                            ? horse / 2
                            : horse / 2 + 1;
                        range += horse / 2;
                        horse = 0;
                    }

                    var formation = new List<int>
                    {
                        foot, range, horse
                    };
                    for (var index = 0; index < formation.Count; index++)
                    {
                        for (var c = 0; c < formation[index] * size / 100f; c++)
                            switch (index)
                            {
                                case 0:
                                    roster.AddToCounts(Globals.BasicInfantry.GetRandomElement(), 1);
                                    break;
                                case 1:
                                    roster.AddToCounts(Globals.BasicRanged.GetRandomElement(), 1);
                                    break;
                                case 2:
                                    roster.AddToCounts(Globals.BasicCavalry.GetRandomElement(), 1);
                                    break;
                            }
                    }

                    var bm = MobileParty.CreateParty("Bandit_Militia", new ModBanditMilitiaPartyComponent(settlement, null), m => m.ActualClan = settlement.OwnerClan);
                    InitMilitia(bm, [roster, TroopRoster.CreateDummyTroopRoster()], settlement.GatePosition);
                    DoPowerCalculations();
                    
                    Log.Debug?.Log($"[Info] Spawned {bm.Name}({bm.StringId}).");

                    // teleport new militias near the player
                    if (Globals.Settings.TestingMode)
                    {
                        // in case a prisoner
                        var party = Hero.MainHero.PartyBelongedTo ?? Hero.MainHero.PartyBelongedToAsPrisoner.MobileParty;
                        bm.Position2D = party.Position2D;
                    }
                }
            }
            catch (Exception ex)
            {
                InformationManager.DisplayMessage(new InformationMessage("Problem spawning BM, please open a bug report with the log.txt file (Debug setting must be on)."));
                InformationManager.DisplayMessage(new InformationMessage($"{ex.Message}"));
                Log.Debug?.Log(ex);
            }
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("Heroes", ref Heroes);
            if (dataStore.IsLoading)
            {
                // clean up heroes from an old bug
                Globals.Heroes.RemoveAll(h => !Hero.AllAliveHeroes.Contains(h));
                var deadHeroes = Heroes.WhereQ(h => Hero.DeadOrDisabledHeroes.Contains(h)).ToListQ();
                foreach (Hero hero in deadHeroes)
                {
                    Helper.DeadOrDisabledHeroes(Campaign.Current.CampaignObjectManager).Remove(hero);
                }
                Globals.Heroes.RemoveAll(h => deadHeroes.Contains(h));
            }
        }
    }
}
