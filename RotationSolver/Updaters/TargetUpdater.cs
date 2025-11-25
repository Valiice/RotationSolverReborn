using ECommons.DalamudServices;
using ECommons.ExcelServices;
using ECommons.GameFunctions;
using ECommons.GameHelpers;
using ECommons.Logging;

namespace RotationSolver.Updaters;

internal static partial class TargetUpdater
{
    private static readonly ObjectListDelay<IBattleChara>
        _raisePartyTargets = new(() => Service.Config.RaiseDelay2),
        _raiseAllTargets = new(() => Service.Config.RaiseDelay2),
        _dispelPartyTargets = new(() => Service.Config.EsunaDelay);

    private static DateTime _lastUpdateTimeToKill = DateTime.MinValue;
    private static readonly TimeSpan TimeToKillUpdateInterval = TimeSpan.FromSeconds(1);

    // --- OPTIMIZATION BUFFERS ---
    // 1. General Buffers (From previous optimization)
    private static readonly List<IBattleChara> _allTargetsBuffer = [];
    private static readonly List<IBattleChara> _partyMembersBuffer = [];
    private static readonly List<IBattleChara> _allianceMembersBuffer = [];
    private static readonly List<IBattleChara> _hostileTargetsBuffer = [];

    // 2. Death Target Buffers (From your code)
    private static readonly List<IBattleChara> _deathTanks = [];
    private static readonly List<IBattleChara> _deathHealers = [];
    private static readonly List<IBattleChara> _deathOffHealers = [];
    private static readonly List<IBattleChara> _deathOthers = [];
    private static readonly HashSet<IBattleChara> _deathPartySet = [];

    internal static void UpdateTargets()
    {
        //PluginLog.Debug("Updating targets");
        DataCenter.TargetsByRange.Clear();

        // Populate buffers
        UpdateAllTargetsBuffer();
        DataCenter.AllTargets = _allTargetsBuffer;

        if (DataCenter.AllTargets != null)
        {
            DataCenter.PartyMembers = GetPartyMembers();
            DataCenter.AllianceMembers = GetAllianceMembers();
            DataCenter.AllHostileTargets = GetAllHostileTargets();

            DataCenter.DeathTarget = GetDeathTarget();
            DataCenter.DispelTarget = GetDispelTarget();

            DataCenter.ProvokeTarget = (DataCenter.Role == JobRole.Tank || Player.Object.HasStatus(true, StatusID.VariantUltimatumSet))
                ? GetFirstHostileTarget(ObjectHelper.CanProvoke)
                : null;

            DataCenter.InterruptTarget = GetFirstHostileTarget(ObjectHelper.CanInterrupt);
        }
        UpdateTimeToKill();
    }

    private static void UpdateAllTargetsBuffer()
    {
        _allTargetsBuffer.Clear();
        bool skipDummyCheck = !Service.Config.DisableTargetDummys;
        foreach (var obj in Svc.Objects)
        {
            if (obj is IBattleChara battleChara)
            {
                if ((skipDummyCheck || !battleChara.IsDummy()) && battleChara.StatusList != null && battleChara.IsTargetable && !battleChara.IsPet())
                {
                    _allTargetsBuffer.Add(battleChara);
                }
            }
        }
    }

    // Removed the allocating GetAllTargets() in favor of the void UpdateAllTargetsBuffer()

    private static unsafe List<IBattleChara> GetPartyMembers()
    {
        return GetMembers(_allTargetsBuffer, _partyMembersBuffer, isParty: true);
    }

    private static unsafe List<IBattleChara> GetAllianceMembers()
    {
        RaiseType raisetype = Service.Config.RaiseType;

        if (raisetype == RaiseType.PartyOnly)
        {
            _allianceMembersBuffer.Clear();
            return _allianceMembersBuffer;
        }

        if (raisetype == RaiseType.AllOutOfDuty)
        {
            return GetMembers(_allTargetsBuffer, _allianceMembersBuffer, isParty: false, isAlliance: false, IsOutDuty: true);
        }

        return GetMembers(_allTargetsBuffer, _allianceMembersBuffer, isParty: false, isAlliance: true, IsOutDuty: false);
    }

    private static unsafe List<IBattleChara> GetMembers(List<IBattleChara> source, List<IBattleChara> buffer, bool isParty, bool isAlliance = false, bool IsOutDuty = false)
    {
        buffer.Clear();
        if (source == null) return buffer;

        foreach (IBattleChara member in source)
        {
            try
            {
                if (member.IsPet()) continue;
                if (isParty && !member.IsParty()) continue;
                if (isAlliance && (!ObjectHelper.IsAllianceMember(member) || member.IsParty())) continue;
                if (IsOutDuty && (!ObjectHelper.IsOtherPlayerOutOfDuty(member) || member.IsParty())) continue;

                FFXIVClientStructs.FFXIV.Client.Game.Character.Character* character = member.Character();
                if (character == null) continue;

                buffer.Add(member);
            }
            catch (Exception ex)
            {
                PluginLog.Error($"Error in GetMembers: {ex.Message}");
            }
        }
        return buffer;
    }

    private static List<IBattleChara> GetAllHostileTargets()
    {
        _hostileTargetsBuffer.Clear();
        var allTargets = DataCenter.AllTargets;
        if (allTargets == null) return _hostileTargetsBuffer;

        foreach (IBattleChara target in allTargets)
        {
            if (!target.IsEnemy() || !target.IsTargetable || !target.CanSee() || target.DistanceToPlayer() >= 48)
                continue;

            bool hasInvincible = false;
            var statusList = target.StatusList;
            if (statusList != null)
            {
                var statusCount = statusList.Length;
                for (int i = 0; i < statusCount; i++)
                {
                    var status = statusList[i];
                    if (status != null)
                    {
                        if (status.StatusId != 0 && StatusHelper.IsInvincible(status))
                        {
                            hasInvincible = true;
                            break;
                        }
                    }
                }
            }
            if (hasInvincible &&
                ((DataCenter.IsPvP && !Service.Config.IgnorePvPInvincibility) || !DataCenter.IsPvP))
            {
                continue;
            }

            _hostileTargetsBuffer.Add(target);
        }
        return _hostileTargetsBuffer;
    }

    private static IBattleChara? GetFirstHostileTarget(Func<IBattleChara, bool> predicate)
    {
        var hostileTargets = DataCenter.AllHostileTargets;
        if (hostileTargets == null) return null;

        foreach (IBattleChara target in hostileTargets)
        {
            try
            {
                if (predicate(target))
                    return target;
            }
            catch (Exception ex)
            {
                PluginLog.Error($"Error in GetFirstHostileTarget: {ex.Message}");
            }
        }
        return null;
    }

    private static IBattleChara? GetDeathTarget()
    {
        if (DataCenter.CanRaise())
        {
            try
            {
                RaiseType raisetype = Service.Config.RaiseType;

                // Optimization: Reuse cached HashSet
                _deathPartySet.Clear();
                if (DataCenter.PartyMembers != null)
                {
                    foreach (var target in DataCenter.PartyMembers.GetDeath())
                    {
                        _deathPartySet.Add(target);
                    }
                }

                var validRaiseTargets = new List<IBattleChara>(_deathPartySet);

                if (raisetype == RaiseType.PartyAndAllianceSupports || raisetype == RaiseType.PartyAndAllianceHealers)
                {
                    if (DataCenter.AllianceMembers != null)
                    {
                        foreach (var member in DataCenter.AllianceMembers.GetDeath())
                        {
                            if (!_deathPartySet.Contains(member))
                            {
                                if (raisetype == RaiseType.PartyAndAllianceHealers && member.IsJobCategory(JobRole.Healer))
                                    validRaiseTargets.Add(member);
                                else if (raisetype == RaiseType.PartyAndAllianceSupports && (member.IsJobCategory(JobRole.Healer) || member.IsJobCategory(JobRole.Tank)))
                                    validRaiseTargets.Add(member);
                            }
                        }
                    }
                }
                else if (raisetype == RaiseType.All || raisetype == RaiseType.AllOutOfDuty)
                {
                    if (DataCenter.AllianceMembers != null)
                    {
                        foreach (var target in DataCenter.AllianceMembers.GetDeath())
                        {
                            if (!_deathPartySet.Contains(target))
                            {
                                validRaiseTargets.Add(target);
                            }
                        }
                    }
                }

                // Apply raise delay
                if (raisetype == RaiseType.PartyOnly)
                {
                    _raisePartyTargets.Delay(validRaiseTargets);
                    validRaiseTargets = [.. _raisePartyTargets];
                }
                else
                {
                    _raiseAllTargets.Delay(validRaiseTargets);
                    validRaiseTargets = [.. _raiseAllTargets];
                }

                return GetPriorityDeathTarget(validRaiseTargets, raisetype);
            }
            catch (Exception ex)
            {
                PluginLog.Error($"Error in GetDeathTarget: {ex.Message}");
            }
        }
        return null;
    }

    private static IBattleChara? GetPriorityDeathTarget(List<IBattleChara> validRaiseTargets, RaiseType raiseType = RaiseType.PartyOnly)
    {
        if (validRaiseTargets.Count == 0)
        {
            return null;
        }

        // Optimization: Reuse static lists instead of allocating 4 new lists per frame
        _deathTanks.Clear();
        _deathHealers.Clear();
        _deathOffHealers.Clear();
        _deathOthers.Clear();

        foreach (IBattleChara chara in validRaiseTargets)
        {
            if (chara.IsJobCategory(JobRole.Tank))
            {
                _deathTanks.Add(chara);
            }
            else if (chara.IsJobCategory(JobRole.Healer))
            {
                _deathHealers.Add(chara);
            }
            else if (Service.Config.OffRaiserRaise && chara.IsJobs(Job.SMN, Job.RDM))
            {
                _deathOffHealers.Add(chara);
            }
            else
            {
                _deathOthers.Add(chara);
            }
        }

        if (raiseType == RaiseType.PartyAndAllianceHealers && _deathHealers.Count > 0)
        {
            return _deathHealers[0];
        }

        if (Service.Config.H2)
        {
            _deathOffHealers.Reverse();
            _deathOthers.Reverse();
        }

        if (_deathTanks.Count > 1)
        {
            return _deathTanks[0];
        }

        return _deathHealers.Count > 0
            ? _deathHealers[0]
            : _deathTanks.Count > 0
            ? _deathTanks[0]
            : Service.Config.OffRaiserRaise && _deathOffHealers.Count > 0
            ? _deathOffHealers[0]
            : _deathOthers.Count > 0 ? _deathOthers[0] : null;
    }

    private static IBattleChara? GetDispelTarget()
    {
        if (Player.Job is Job.WHM or Job.SCH or Job.AST or Job.SGE or Job.BRD or Job.CNJ)
        {
            List<IBattleChara> weakenPeople = [];
            List<IBattleChara> dyingPeople = [];

            AddDispelTargets(DataCenter.PartyMembers, weakenPeople);

            // Apply dispel delay
            _dispelPartyTargets.Delay(weakenPeople);
            var delayedWeakenPeople = new List<IBattleChara>();
            foreach (var person in _dispelPartyTargets)
            {
                delayedWeakenPeople.Add(person);
            }

            var CanDispelNonDangerous = !DataCenter.MergedStatus.HasFlag(AutoStatus.HealAreaAbility)
                    && !DataCenter.MergedStatus.HasFlag(AutoStatus.HealAreaSpell)
                    && !DataCenter.MergedStatus.HasFlag(AutoStatus.HealSingleAbility)
                    && !DataCenter.MergedStatus.HasFlag(AutoStatus.HealSingleSpell)
                    && !DataCenter.MergedStatus.HasFlag(AutoStatus.DefenseArea)
                    && !DataCenter.MergedStatus.HasFlag(AutoStatus.DefenseSingle);

            foreach (IBattleChara person in delayedWeakenPeople)
            {
                bool hasDangerous = false;
                if (person.StatusList != null)
                {
                    for (int i = 0; i < person.StatusList.Length; i++)
                    {
                        Dalamud.Game.ClientState.Statuses.Status? status = person.StatusList[i];
                        if (status != null && status.IsDangerous())
                        {
                            hasDangerous = true;
                            break;
                        }
                    }
                }
                if (hasDangerous)
                {
                    dyingPeople.Add(person);
                }
            }

            // Allow non-dangerous dispels when either we're in a safe context or explicitly configured to do so.
            bool allowNonDangerous = CanDispelNonDangerous
                                     || !DataCenter.HasHostilesInRange
                                     || Service.Config.DispelAll
                                     || DataCenter.IsPvP;

            IBattleChara? dangerousTarget = GetClosestTarget(dyingPeople);
            if (!allowNonDangerous)
            {
                return dangerousTarget;
            }

            return dangerousTarget ?? GetClosestTarget(delayedWeakenPeople);
        }
        return null;
    }

    private static void AddDispelTargets(List<IBattleChara>? members, List<IBattleChara> targetList)
    {
        if (members == null)
        {
            return;
        }

        foreach (IBattleChara member in members)
        {
            try
            {
                if (member.StatusList != null)
                {
                    for (int i = 0; i < member.StatusList.Length; i++)
                    {
                        Dalamud.Game.ClientState.Statuses.Status? status = member.StatusList[i];
                        if (status != null && status.CanDispel())
                        {
                            targetList.Add(member);
                            break; // Add only once per member if any status can be dispelled
                        }
                    }
                }
            }
            catch (NullReferenceException ex)
            {
                PluginLog.Error($"NullReferenceException in AddDispelTargets for member {member?.ToString()}: {ex.Message}");
            }
        }
    }

    private static IBattleChara? GetClosestTarget(List<IBattleChara> targets)
    {
        IBattleChara? closestTarget = null;
        float closestDistance = float.MaxValue;

        foreach (IBattleChara target in targets)
        {
            float distance = ObjectHelper.DistanceToPlayer(target);
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closestTarget = target;
            }
        }

        return closestTarget;
    }

    // Recording new entries at 1/second and dequeuing old values to keep only the last DataCenter.HP_RECORD_TIME worth of combat time
    // Has performance implications for keeping too much data for too many targets as they're also all evaluated multiple times a frame for expected TTK
    private static void UpdateTimeToKill()
    {
        DateTime now = DateTime.Now;
        if (now - _lastUpdateTimeToKill < TimeToKillUpdateInterval)
        {
            return;
        }

        _lastUpdateTimeToKill = now;

        var hostiles = DataCenter.AllHostileTargets;
        if (hostiles == null || hostiles.Count == 0)
        {
            return;
        }

        if (DataCenter.RecordedHP.Count >= DataCenter.HP_RECORD_TIME)
        {
            _ = DataCenter.RecordedHP.Dequeue();
        }

        Dictionary<ulong, float> currentHPs = new(hostiles.Count);
        for (int i = 0; i < hostiles.Count; i++)
        {
            var target = hostiles[i];
            if (target != null && target.CurrentHp != 0)
            {
                currentHPs[target.GameObjectId] = target.GetHealthRatio();
            }
        }

        DataCenter.RecordedHP.Enqueue((now, currentHPs));
    }
}