using ECommons.GameHelpers;
using FFXIVClientStructs.FFXIV.Client.Game;
using RotationSolver.Basic.Actions;

namespace RotationSolver.RebornRotations.Tank;

[Rotation("Reborn", CombatType.PvE, GameVersion = "7.4")]
[SourceCode(Path = "main/RebornRotations/Tank/WAR_Reborn.cs")]

public sealed class WAR_Reborn : WarriorRotation
{
    #region Config Options
    [RotationConfig(CombatType.PvE, Name = "Only use Nascent Flash if Tank Stance is off")]
    public bool NeverscentFlash { get; set; } = false;

    [RotationConfig(CombatType.PvE, Name = "Use Bloodwhetting/Raw intuition on single enemies")]
    public bool SoloIntuition { get; set; } = false;

    [Range(0, 1, ConfigUnitType.Percent)]
    [RotationConfig(CombatType.PvE, Name = "Bloodwhetting/Raw intuition heal threshold")]
    public float HealIntuition { get; set; } = 0.7f;

    [RotationConfig(CombatType.PvE, Name = "Use both stacks of Onslaught during burst while standing still")]
    public bool YEETBurst { get; set; } = true;

    [RotationConfig(CombatType.PvE, Name = "Use a stack of Onslaught when its about to overcap while standing still")]
    public bool YEETCooldown { get; set; } = false;

    [RotationConfig(CombatType.PvE, Name = "Use Primal Rend while moving (Dangerous)")]
    public bool YEET { get; set; } = false;

    [RotationConfig(CombatType.PvE, Name = "Use Primal Rend while standing still outside of configured melee range (Dangerous)")]
    public bool YEETStill { get; set; } = false;

    [Range(1, 20, ConfigUnitType.Yalms)]
    [RotationConfig(CombatType.PvE, Name = "Max distance you can be from the boss for Primal Rend use (Danger, setting too high will get you killed)")]
    public float PrimalRendDistance2 { get; set; } = 3.5f;

    [Range(0, 20, ConfigUnitType.Yalms)]
    [RotationConfig(CombatType.PvE, Name = "Min distance to use Tomahawk")]
    public float TomahawkDistance { get; set; } = 5.5f;

    [Range(1, 50, ConfigUnitType.Seconds)]
    [RotationConfig(CombatType.PvE, Name = "Seconds remaining on Surging Tempest to refresh Storm's Eye")]
    public float StormsEyeRefreshTimer { get; set; } = 15.0f;

    [Range(1, 10, ConfigUnitType.None)]
    [RotationConfig(CombatType.PvE, Name = "Number of enemies to start using AOE (Overrides defaults)")]
    public int AOECount { get; set; } = 3;

    [Range(0, 1, ConfigUnitType.Percent)]
    [RotationConfig(CombatType.PvE, Name = "Nascent Flash Heal Threshold")]
    public float FlashHeal { get; set; } = 0.6f;

    [Range(0, 1, ConfigUnitType.Percent)]
    [RotationConfig(CombatType.PvE, Name = "Thrill Of Battle Heal Threshold")]
    public float ThrillOfBattleHeal { get; set; } = 0.6f;

    [Range(0, 1, ConfigUnitType.Percent)]
    [RotationConfig(CombatType.PvE, Name = "Equilibrium Heal Threshold")]
    public float EquilibriumHeal { get; set; } = 0.6f;

    #endregion

    #region Countdown Logic
    protected override IAction? CountDownAction(float remainTime)
    {
        if (remainTime < 0.54f && TomahawkPvE.CanUse(out IAction? act))
        {
            return act;
        }
        return base.CountDownAction(remainTime);
    }
    #endregion

    #region oGCD Logic
    protected override bool AttackAbility(IAction nextGCD, out IAction? act)
    {
        // 1. INFURIATE OPTIMIZATION
        if (InfuriatePvE.CanUse(out act, gcdCountForAbility: 3))
        {
            // SAFETY: Do not double-cast if we just used it or have the buff.
            if (StatusHelper.PlayerHasStatus(true, StatusID.NascentChaos) || IsLastAction(false, InfuriatePvE))
            {
                act = null;
            }
            else
            {
                // Optimization: Dump if in burst, or if Opener, or if about to overcap.
                // increased buffer to < 20s to prevent ever hitting 2 stacks during combat
                bool isBurst = IsBurstStatus || StatusHelper.PlayerHasStatus(true, StatusID.SurgingTempest);

                if (CombatElapsedLessGCD(4) ||
                    isBurst ||
                    (InfuriatePvE.Cooldown.CurrentCharges > 0 && InfuriatePvE.Cooldown.RecastTimeRemainOneCharge < 20))
                {
                    return true;
                }
            }
        }

        // 2. Prevent oGCDs during the very first GCD
        if (CombatElapsedLessGCD(2))
        {
            act = null;
            return false;
        }

        // 3. INNER RELEASE
        if (!StatusHelper.PlayerWillStatusEndGCD(2, 0, true, StatusID.SurgingTempest)
            || !StormsEyePvE.EnoughLevel)
        {
            if (InnerReleasePvE.CanUse(out act))
            {
                return true;
            }
            if (!InnerReleasePvE.Info.EnoughLevelAndQuest() && BerserkPvE.CanUse(out act))
            {
                return true;
            }
        }

        // 4. OROGENY / UPHEAVAL
        if (NumberOfHostilesInRange >= AOECount && OrogenyPvE.CanUse(out act, skipAoeCheck: true))
        {
            return true;
        }

        if (UpheavalPvE.CanUse(out act))
        {
            return true;
        }

        // 5. PRIMAL WRATH
        if (StatusHelper.PlayerHasStatus(false, StatusID.Wrathful) && PrimalWrathPvE.CanUse(out act, skipAoeCheck: true))
        {
            return true;
        }

        // 6. ONSLAUGHT
        bool isBurstStatus = IsBurstStatus;

        if (YEETBurst && OnslaughtPvE.CanUse(out act, usedUp: isBurstStatus) &&
           !IsMoving &&
           !IsLastAction(false, OnslaughtPvE) &&
           !IsLastAction(false, UpheavalPvE) &&
            StatusHelper.PlayerHasStatus(true, StatusID.SurgingTempest))
        {
            return true;
        }

        if (YEETCooldown && OnslaughtPvE.CanUse(out act, usedUp: true) &&
           !IsMoving &&
           !IsLastAction(false, OnslaughtPvE) &&
           OnslaughtPvE.Cooldown.WillHaveXChargesGCD(OnslaughtMax, 1) &&
            StatusHelper.PlayerHasStatus(true, StatusID.SurgingTempest))
        {
            return true;
        }

        if (MergedStatus.HasFlag(AutoStatus.MoveForward) && MoveForwardAbility(nextGCD, out act))
        {
            return true;
        }

        return base.AttackAbility(nextGCD, out act);
    }

    protected override bool GeneralAbility(IAction nextGCD, out IAction? act)
    {
        if ((InCombat && Player?.GetHealthRatio() < HealIntuition && NumberOfHostilesInRange > 0) || (InCombat && PartyMembers.Count() is 1 && NumberOfHostilesInRange > 0))
        {
            if (BloodwhettingPvE.CanUse(out act)) return true;
            if (!BloodwhettingPvE.Info.EnoughLevelAndQuest() && RawIntuitionPvE.CanUse(out act)) return true;
        }

        if (Player?.GetHealthRatio() < ThrillOfBattleHeal)
        {
            if (ThrillOfBattlePvE.CanUse(out act)) return true;
        }

        if (!StatusHelper.PlayerHasStatus(true, StatusID.Holmgang_409))
        {
            if (Player?.GetHealthRatio() < EquilibriumHeal)
            {
                if (EquilibriumPvE.CanUse(out act)) return true;
            }
        }

        if (StatusHelper.PlayerHasStatus(true, StatusID.PrimalRendReady) && InCombat && UseBurstMedicine(out act))
        {
            return true;
        }
        return base.GeneralAbility(nextGCD, out act);
    }

    [RotationDesc(ActionID.ShakeItOffPvE, ActionID.ReprisalPvE)]
    protected override bool HealSingleAbility(IAction nextGCD, out IAction? act)
    {
        if (ShakeItOffPvE.CanUse(out act, skipAoeCheck: true))
        {
            return true;
        }

        return base.HealSingleAbility(nextGCD, out act);
    }

    [RotationDesc(ActionID.RawIntuitionPvE, ActionID.VengeancePvE, ActionID.RampartPvE, ActionID.RawIntuitionPvE, ActionID.ReprisalPvE)]
    protected override bool DefenseSingleAbility(IAction nextGCD, out IAction? act)
    {
        bool RawSingleTargets = SoloIntuition;
        act = null;

        if (StatusHelper.PlayerHasStatus(true, StatusID.Holmgang_409) && Player?.GetHealthRatio() < 0.3f)
        {
            return false;
        }

        if (RawIntuitionPvE.CanUse(out act) && (RawSingleTargets || NumberOfHostilesInRange > 2))
        {
            return true;
        }

        if (!StatusHelper.PlayerWillStatusEndGCD(0, 0, true, StatusID.Bloodwhetting, StatusID.RawIntuition))
        {
            return false;
        }

        if (ReprisalPvE.CanUse(out act, skipAoeCheck: true))
        {
            return true;
        }

        if ((!RampartPvE.Cooldown.IsCoolingDown || RampartPvE.Cooldown.ElapsedAfter(60)) && VengeancePvE.CanUse(out act))
        {
            return true;
        }

        if (((VengeancePvE.Cooldown.IsCoolingDown && VengeancePvE.Cooldown.ElapsedAfter(60)) || !VengeancePvE.EnoughLevel) && RampartPvE.CanUse(out act))
        {
            return true;
        }

        return base.DefenseSingleAbility(nextGCD, out act);
    }

    [RotationDesc(ActionID.ShakeItOffPvE, ActionID.ReprisalPvE)]
    protected override bool DefenseAreaAbility(IAction nextGCD, out IAction? act)
    {
        if (ShakeItOffPvE.CanUse(out act, skipAoeCheck: true))
        {
            return true;
        }

        return base.DefenseAreaAbility(nextGCD, out act);
    }
    #endregion

    #region GCD Logic
    protected override bool GeneralGCD(out IAction? act)
    {
        bool hasSurgingTempest = !StatusHelper.PlayerWillStatusEndGCD(3, 0, true, StatusID.SurgingTempest);

        // 1. Spend "Free" Resources (Inner Chaos / Primal Rend)
        if (hasSurgingTempest)
        {
            if (ChaoticCyclonePvE.CanUse(out act)) return true;
            if (InnerChaosPvE.CanUse(out act)) return true;
        }

        // 2. Inner Release Window (Consume stacks immediately)
        if (InnerReleaseStacks > 0)
        {
            if (NumberOfHostilesInRange >= AOECount)
            {
                if (DecimatePvE.CanUse(out act, skipStatusProvideCheck: true, skipAoeCheck: true)) return true;
                if (!DecimatePvE.Info.EnoughLevelAndQuest() && InnerBeastPvE.CanUse(out act, skipStatusProvideCheck: true)) return true;
            }
            if (FellCleavePvE.CanUse(out act, skipStatusProvideCheck: true)) return true;
            if (!FellCleavePvE.Info.EnoughLevelAndQuest() && InnerBeastPvE.CanUse(out act, skipStatusProvideCheck: true)) return true;
        }

        // 3. GAUGE DUMP (High Priority)
        // If Infuriate is Urgent (has charges and is about to gain another), we MUST have <= 50 Gauge.
        // If we have > 50 Gauge, we cannot press Infuriate. We must Fell Cleave immediately.
        bool isInfuriateUrgent = InfuriatePvE.Cooldown.CurrentCharges > 0 && InfuriatePvE.Cooldown.RecastTimeRemainOneCharge < 20;

        if (hasSurgingTempest && (BeastGauge >= 90 || (isInfuriateUrgent && BeastGauge > 50)))
        {
            if (FellCleavePvE.CanUse(out act, skipStatusProvideCheck: true)) return true;
            // Low level fallback
            if (!FellCleavePvE.Info.EnoughLevelAndQuest() && InnerBeastPvE.CanUse(out act, skipStatusProvideCheck: true)) return true;
        }

        // 4. Primal Rend 
        if (hasSurgingTempest && InnerReleaseStacks == 0)
        {
            if (PrimalRendPvE.CanUse(out act, skipAoeCheck: true))
            {
                if (PrimalRendPvE.Target.Target != null && PrimalRendPvE.Target.Target.DistanceToPlayer() <= PrimalRendDistance2) return true;
                if (YEET || (YEETStill && !IsMoving)) return true;
            }
            if (PrimalRuinationPvE.CanUse(out act)) return true;
        }

        // 5. AoE Combo
        if (NumberOfHostilesInRange >= AOECount)
        {
            if (!hasSurgingTempest)
            {
                if (MythrilTempestPvE.CanUse(out act, skipAoeCheck: true)) return true;
                if (OverpowerPvE.CanUse(out act, skipAoeCheck: true)) return true;
            }
            if (DecimatePvE.CanUse(out act, skipStatusProvideCheck: true, skipAoeCheck: true)) return true;
            if (MythrilTempestPvE.CanUse(out act, skipAoeCheck: true)) return true;
            if (OverpowerPvE.CanUse(out act, skipAoeCheck: true)) return true;
        }

        // 6. Single Target Combo
        if (StormsEyePvE.CanUse(out act))
        {
            bool irComingSoon = InnerReleasePvE.Cooldown.RecastTime < 10;
            float buffTime = Player.StatusTime(true, StatusID.SurgingTempest);

            if (buffTime > StormsEyeRefreshTimer || (irComingSoon && buffTime > 5))
            {
                if (StormsPathPvE.CanUse(out var actPath))
                {
                    act = actPath;
                    return true;
                }
            }
            return true;
        }

        if (StormsPathPvE.CanUse(out act)) return true;
        if (MaimPvE.CanUse(out act)) return true;
        if (HeavySwingPvE.CanUse(out act)) return true;

        if (TomahawkPvE.CanUse(out act))
        {
            if (TomahawkPvE.Target.Target != null && TomahawkPvE.Target.Target.DistanceToPlayer() >= TomahawkDistance) return true;
            act = null;
            return false;
        }

        return base.GeneralGCD(out act);
    }

    [RotationDesc(ActionID.NascentFlashPvE)]
    protected override bool HealSingleGCD(out IAction? act)
    {
        if (!NeverscentFlash && NascentFlashPvE.CanUse(out act)
            && (InCombat && NascentFlashPvE.Target.Target?.GetHealthRatio() < FlashHeal))
        {
            return true;
        }

        if (NeverscentFlash && NascentFlashPvE.CanUse(out act)
            && (InCombat && !StatusHelper.PlayerHasStatus(true, StatusID.Defiance) && NascentFlashPvE.Target.Target?.GetHealthRatio() < FlashHeal))
        {
            return true;
        }

        return base.HealSingleGCD(out act);
    }
    #endregion

    #region Extra Methods
    private static bool IsBurstStatus => !StatusHelper.PlayerWillStatusEndGCD(0, 0, false, StatusID.InnerStrength);
    #endregion
}