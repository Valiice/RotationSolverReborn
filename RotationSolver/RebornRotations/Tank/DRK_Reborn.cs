using ECommons.DalamudServices;

namespace RotationSolver.RebornRotations.Tank;

[Rotation("Reborn", CombatType.PvE, GameVersion = "7.41")]
[SourceCode(Path = "main/RebornRotations/Tank/DRK_Reborn.cs")]

public sealed class DRK_Reborn : DarkKnightRotation
{
    #region Config Options
    [RotationConfig(CombatType.PvE, Name = "Keep at least 3000 MP")]
    public bool TheBlackestNight { get; set; } = true;

    [RotationConfig(CombatType.PvE, Name = "Use The Blackest Night on lowest HP party member during AOE scenarios")]
    public bool BlackLantern { get; set; } = false;

    [Range(0, 1, ConfigUnitType.Percent)]
    [RotationConfig(CombatType.PvE, Name = "Target health threshold needed to use Blackest Night with above option", Parent = nameof(BlackLantern))]
    private float BlackLanternRatio { get; set; } = 0.5f;

	[RotationConfig(CombatType.PvE, Name = "Use Oblation on lowest HP party member during AOE scenarios")]
	public bool OblationLantern { get; set; } = false;

	[RotationConfig(CombatType.PvE, Name = "Use Oblation last stack of Oblation for party members", Parent = nameof(OblationLantern))]
	public bool OblationLanternStack { get; set; } = false;

	[Range(0, 1, ConfigUnitType.Percent)]
	[RotationConfig(CombatType.PvE, Name = "Target health threshold needed to use Oblation with above option", Parent = nameof(OblationLantern))]
	private float OblationLanternRatio { get; set; } = 0.5f;

    [RotationConfig(CombatType.PvE, Name = "Print overcap warnings to chat (debug)")]
    public bool DebugOvercapMessages { get; set; } = false;

    [RotationConfig(CombatType.PvE, Name = "Dump all resources near encounter end (e.g. Stone Sky Sea)")]
    public bool PanicDump { get; set; } = false;

    [Range(5, 60, ConfigUnitType.Seconds)]
    [RotationConfig(CombatType.PvE, Name = "Seconds before encounter end to start panic dump", Parent = nameof(PanicDump))]
    public float PanicDumpWindow { get; set; } = 15f;

    // Edge-detection state for overcap diagnostics (uses CombatTime floats, no DateTime allocs)
    private int _prevBloodGauge = -1;
    private bool _mpWasMaxed;
    private float _mpMaxedSinceCombat = -1f;
    private bool _panicDumpStarted = false;

	#endregion

    [Range(1, 10, ConfigUnitType.None)]
    [RotationConfig(CombatType.PvE, Name = "Number of enemies to start using AOE (Overrides defaults)")]
    public int AOECount { get; set; } = 3;

    [Range(0, 20, ConfigUnitType.Yalms)]
    [RotationConfig(CombatType.PvE, Name = "Min distance to use Unmend")]
    public float UnmendDistance { get; set; } = 5.5f;

	#region Countdown Logic
	// Countdown logic to prepare for combat.
	// Includes logic for using Provoke, tank stances, and burst medicines.
	protected override IAction? CountDownAction(float remainTime)
    {
        //Provoke when has Shield.
        if (remainTime <= CountDownAhead)
        {
            if (HasTankStance)
            {
                if (ProvokePvE.CanUse(out _))
                {
                    return ProvokePvE;
                }
            }
        }
        if (remainTime <= 2 && UseBurstMedicine(out IAction? act))
        {
            return act;
        }

        if (remainTime <= 3 && TheBlackestNightPvE.CanUse(out act))
        {
            return act;
        }

        if (remainTime < 0.54f && UnmendPvE.CanUse(out act))
        {
            return act;
        }

        return base.CountDownAction(remainTime);
    }
    #endregion

    #region oGCD Logic
    [RotationDesc(ActionID.ShadowstridePvE)]
    protected override bool MoveForwardAbility(IAction nextGCD, out IAction? act)
    {
        if (ShadowstridePvE.CanUse(out act))
        {
            return true;
        }
        return base.MoveForwardAbility(nextGCD, out act);
    }

    [RotationDesc(ActionID.DarkMissionaryPvE, ActionID.ReprisalPvE)]
    protected override bool DefenseAreaAbility(IAction nextGCD, out IAction? act)
    {
        if (!InTwoMIsBurst && BlackLantern && TheBlackestNightPvE.CanUse(out act, targetOverride: TargetType.LowHP) && !TheBlackestNightPvE.Target.Target.HasStatus(false, StatusID.Transcendent) && TheBlackestNightPvE.Target.Target.GetHealthRatio() <= BlackLanternRatio)
        {
            return true;
        }

		if (!InTwoMIsBurst && OblationLantern && OblationPvE.CanUse(out act, usedUp: OblationLanternStack, targetOverride: TargetType.LowHP) && !OblationPvE.Target.Target.HasStatus(false, StatusID.Transcendent) && OblationPvE.Target.Target.GetHealthRatio() <= OblationLanternRatio)
		{
			return true;
		}

		if (!InTwoMIsBurst && DarkMissionaryPvE.CanUse(out act))
        {
            return true;
        }

        if (!InTwoMIsBurst && ReprisalPvE.CanUse(out act, skipAoeCheck: true))
        {
            return true;
        }

		if (!InTwoMIsBurst && OblationPvE.CanUse(out act, skipStatusProvideCheck: false, targetOverride: TargetType.Self))
		{
			return true;
		}

		return base.DefenseAreaAbility(nextGCD, out act);
    }

    [RotationDesc(ActionID.OblationPvE, ActionID.TheBlackestNightPvE, ActionID.DarkMindPvE, ActionID.ShadowWallPvE, ActionID.ShadowedVigilPvE, ActionID.RampartPvE, ActionID.ReprisalPvE)]
    protected override bool DefenseSingleAbility(IAction nextGCD, out IAction? act)
    {
        //10
        if (OblationPvE.CanUse(out act, usedUp: true, skipStatusProvideCheck: false, targetOverride: TargetType.Self))
        {
            return true;
        }

        if (TheBlackestNightPvE.CanUse(out act, targetOverride: TargetType.Self))
        {
            return true;
        }
        //20
        if (DarkMindPvE.CanUse(out act))
        {
            return true;
        }

        //30
        if ((!RampartPvE.Cooldown.IsCoolingDown || RampartPvE.Cooldown.ElapsedAfter(60)) && ShadowWallPvE.CanUse(out act))
        {
            return true;
        }

        if ((!RampartPvE.Cooldown.IsCoolingDown || RampartPvE.Cooldown.ElapsedAfter(60)) && ShadowedVigilPvE.CanUse(out act))
        {
            return true;
        }

        //20
        if (ShadowWallPvE.Cooldown.IsCoolingDown && ShadowWallPvE.Cooldown.ElapsedAfter(60) && RampartPvE.CanUse(out act))
        {
            return true;
        }

        if (ShadowedVigilPvE.Cooldown.IsCoolingDown && ShadowedVigilPvE.Cooldown.ElapsedAfter(60) && RampartPvE.CanUse(out act))
        {
            return true;
        }

        if (ReprisalPvE.CanUse(out act))
        {
            return true;
        }

        return base.DefenseSingleAbility(nextGCD, out act);
    }

    protected override bool AttackAbility(IAction nextGCD, out IAction? act)
    {
        // Overcap detection (edge-triggered, only fires once per transition)
        if (DebugOvercapMessages && InCombat)
        {
            CheckOvercapDiagnostics();
        }

        if (CheckDarkSide)
        {
            if (FloodOfDarknessPvE.CanUse(out act))
            {
                return true;
            }

            if (EdgeOfDarknessPvE.CanUse(out act))
            {
                return true;
            }
        }

        if (IsBurst)
        {
            if (InCombat && LivingShadowPvE.CanUse(out act, skipAoeCheck: true))
            {
                return true;
            }
            if (!IsMoving && SaltedEarthPvE.CanUse(out act, skipAoeCheck: true))
            {
                return true;
            }
        }

        if (CombatElapsedLess(3))
        {
            _panicDumpStarted = false;
            act = null;
            return false;
        }

        // Panic dump: force all major cooldowns when encounter is nearly over
        if (IsNearEncounterEnd)
        {
            if (!_panicDumpStarted)
            {
                _panicDumpStarted = true;
                Svc.Chat.Print($"[DRK] Panic dump started — {ContentTimeLeft:F0}s remaining, Blood: {Blood}");
            }

            if (InCombat && DeliriumPvE.CanUse(out act)) return true;
            if (InCombat && LivingShadowPvE.CanUse(out act, skipAoeCheck: true)) return true;
            if (ShadowbringerPvE.CanUse(out act, usedUp: true, skipAoeCheck: true)) return true;
        }

        // Delirium on cooldown (60s) — fires every minute, not just in 2-min burst windows
        if (InCombat && DeliriumPvE.CanUse(out act)) return true;

        // Blood Weapon pairs with Delirium — fire 1–3 GCDs after Delirium
        if (DeliriumPvE.EnoughLevel && DeliriumPvE.Cooldown.ElapsedAfterGCD(1) && !DeliriumPvE.Cooldown.ElapsedAfterGCD(3)
            && BloodWeaponPvE.CanUse(out act)) return true;

        if (!DeliriumPvE.EnoughLevel && BloodWeaponPvE.CanUse(out act)) return true;

        if (!IsMoving && SaltedEarthPvE.CanUse(out act, skipAoeCheck: true))
        {
            return true;
        }

        if (NumberOfHostilesInRange >= AOECount && AbyssalDrainPvE.CanUse(out act))
        {
            return true;
        }

        if (CarveAndSpitPvE.CanUse(out act))
        {
            return true;
        }

        if (SaltAndDarknessPvE.CanUse(out act))
        {
            return true;
        }

        if (InTwoMIsBurst)
        {
            if (ShadowbringerPvE.CanUse(out act, usedUp: true, skipAoeCheck: true))
            {
                return true;
            }
        }

        return base.AttackAbility(nextGCD, out act);
    }
    #endregion

    #region GCD Logic
    protected override bool GeneralGCD(out IAction? act)
    {
        if (DisesteemPvE.CanUse(out act, skipComboCheck: true, skipAoeCheck: true))
        {
            return true;
        }

        //AOE Delirium
        if (NumberOfHostilesInRange >= AOECount)
        {
            if (ImpalementPvE.CanUse(out act))
            {
                return true;
            }

            if (UseBlood && QuietusPvE.CanUse(out act))
            {
                return true;
            }
        }

        // Single Target Delirium
        if (TorcleaverPvE.CanUse(out act, skipComboCheck: true))
        {
            return true;
        }

        if (ComeuppancePvE.CanUse(out act, skipComboCheck: true))
        {
            return true;
        }

        if (ScarletDeliriumPvE.CanUse(out act, skipComboCheck: true))
        {
            return true;
        }

        if (UseBlood && BloodspillerPvE.CanUse(out act, skipComboCheck: true))
        {
            return true;
        }

        //AOE
        if (NumberOfHostilesInRange >= AOECount)
        {
            if (StalwartSoulPvE.CanUse(out act, skipAoeCheck: true))
            {
                return true;
            }

            if (UnleashPvE.CanUse(out act))
            {
                return true;
            }
        }

        //Single Target
        if (!HasDelirium && SouleaterPvE.CanUse(out act))
        {
            return true;
        }

        if (!HasDelirium && SyphonStrikePvE.CanUse(out act))
        {
            return true;
        }

        if (!HasDelirium && HardSlashPvE.CanUse(out act))
        {
            return true;
        }

        if (UnmendPvE.CanUse(out act))
        {
            if (UnmendPvE.Target.Target != null && UnmendPvE.Target.Target.DistanceToPlayer() >= UnmendDistance)
            {
                return true;
            }
            act = null;
            return false;
        }

        return base.GeneralGCD(out act);
    }
    #endregion

    #region Extra Methods
    // Indicates whether the Dark Knight can heal using a single ability.
    public override bool CanHealSingleAbility => false;

    private unsafe float ContentTimeLeft
    {
        get
        {
            var eventFwk = FFXIVClientStructs.FFXIV.Client.Game.Event.EventFramework.Instance();
            var director = eventFwk != null ? eventFwk->GetInstanceContentDirector() : null;
            return director != null ? director->ContentDirector.ContentTimeLeft : 0f;
        }
    }

    // True when the game's content timer enters the panic window.
    // ContentTimeLeft is 0 when not in timed content — guard prevents false triggers.
    private bool IsNearEncounterEnd => PanicDump && ContentTimeLeft > 0 && ContentTimeLeft <= PanicDumpWindow;

    // Logic to determine when to use blood-based abilities.
    private bool UseBlood
    {
        get
        {
            if (IsNearEncounterEnd) return true;

            // Conditions based on player statuses and ability cooldowns.
            if (!DeliriumPvE.EnoughLevel || !LivingShadowPvE.EnoughLevel)
            {
                return true;
            }

            if (StatusHelper.PlayerHasStatus(true, StatusID.Delirium_3836))
            {
                return true;
            }

            if (StatusHelper.PlayerHasStatus(true, StatusID.Delirium_1972) && LivingShadowPvE.Cooldown.IsCoolingDown)
            {
                return true;
            }

            return (DeliriumPvE.Cooldown.WillHaveOneChargeGCD(1) && !LivingShadowPvE.Cooldown.WillHaveOneChargeGCD(3))
                || (DeliriumPvE.Cooldown.WillHaveOneChargeGCD(3) && Blood >= 80 && !LivingShadowPvE.Cooldown.WillHaveOneChargeGCD(3))
                || (Blood >= 90 && !LivingShadowPvE.Cooldown.WillHaveOneChargeGCD(1));
        }
    }
    // Determines if currently in a burst phase based on cooldowns of key abilities.
    private bool InTwoMIsBurst => DeliriumPvE.Cooldown.IsCoolingDown && ((LivingShadowPvE.Cooldown.IsCoolingDown && !LivingShadowPvE.Cooldown.ElapsedAfter(20)) || !LivingShadowPvE.EnoughLevel);

    // Manages DarkSide ability based on several conditions.
    private bool CheckDarkSide
    {
        get
        {
            if (DarkSideEndAfterGCD(3))
            {
                return true;
            }

            if (CombatElapsedLess(3))
            {
                return false;
            }

            if (IsNearEncounterEnd) return true;

            if (HasDarkArts && (InTwoMIsBurst || StatusHelper.PlayerHasStatus(true, StatusID.BlackestNight)))
            {
                return true;
            }

            if (InTwoMIsBurst && BloodWeaponPvE.Cooldown.IsCoolingDown && LivingShadowPvE.Cooldown.IsCoolingDown && SaltedEarthPvE.Cooldown.IsCoolingDown && ShadowbringerPvE.Cooldown.CurrentCharges == 0 && CarveAndSpitPvE.Cooldown.IsCoolingDown)
            {
                return true;
            }

            return (!TheBlackestNight || CurrentMp >= 6000) && CurrentMp >= 8500;
        }
    }

    private void CheckOvercapDiagnostics()
    {
        // Skip early combat — sync tracking to current state so we don't
        // false-positive on natural full gauge/MP at pull
        if (CombatElapsedLess(3))
        {
            _prevBloodGauge = Blood;
            _mpWasMaxed = CurrentMp >= 10000;
            _mpMaxedSinceCombat = -1f;
            return;
        }

        // Blood gauge overcap: detect when gauge transitions to 100 from below
        int gauge = Blood;
        if (gauge >= 100 && _prevBloodGauge >= 0 && _prevBloodGauge < 100)
        {
            int gaugeGain = GetLastActionBloodGain();
            int overcap = _prevBloodGauge + gaugeGain - 100;
            if (overcap > 0)
            {
                Svc.Chat.Print($"[DRK] BLOOD GAUGE OVERCAPPED by {overcap} (was {_prevBloodGauge}, +{gaugeGain} = {_prevBloodGauge + gaugeGain})");
            }
        }
        _prevBloodGauge = gauge;

        // MP overcap: track time spent at maximum MP mid-fight
        bool isMpMaxed = CurrentMp >= 10000;
        if (isMpMaxed != _mpWasMaxed)
        {
            if (isMpMaxed)
            {
                // Just hit max MP — start timing
                _mpMaxedSinceCombat = CombatTime;
            }
            else if (_mpMaxedSinceCombat >= 0f)
            {
                // Just left max — report how long we sat there
                Svc.Chat.Print($"[DRK] MP OVERCAPPED for {CombatTime - _mpMaxedSinceCombat:F1}s at 10000/10000 MP");
                _mpMaxedSinceCombat = -1f;
            }
            _mpWasMaxed = isMpMaxed;
        }
    }

    private int GetLastActionBloodGain()
    {
        // DRK blood-generating weaponskills and their amounts
        if (IsLastGCD(false, SouleaterPvE)) return 20;
        if (IsLastGCD(false, StalwartSoulPvE)) return 20;
        if (IsLastGCD(false, SyphonStrikePvE)) return 10;
        return 0;
    }
    #endregion
}