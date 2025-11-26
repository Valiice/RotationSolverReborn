using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using ECommons.Logging;
using Lumina.Excel.Sheets;
using RotationSolver.Commands;
using RotationSolver.UI.HighlightTeachingMode;
using static FFXIVClientStructs.FFXIV.Client.UI.Misc.RaptureHotbarModule;

namespace RotationSolver.Updaters;

internal static class MajorUpdater
{
    private static TimeSpan _timeSinceUpdate = TimeSpan.Zero;

    private static bool _shouldRunThisCycle;
    private static bool _isValidThisCycle;
    private static bool _isActivatedThisCycle;
    private static bool _rotationsLoaded;

    public static bool IsValid
    {
        get
        {
            if (!Player.AvailableThreadSafe)
            {
                _rotationsLoaded = false;
                return false;
            }

            if (Svc.Condition[ConditionFlag.BetweenAreas] || Svc.Condition[ConditionFlag.BetweenAreas51] || Svc.Condition[ConditionFlag.LoggingOut])
            {
                _rotationsLoaded = false;
                return false;
            }

            if (Svc.Condition[ConditionFlag.Mounted] || Svc.Condition[ConditionFlag.RidingPillion])
            {
                return false;
            }

            return true;
        }
    }

    private static Exception? _threadException;

    public static void Enable()
    {
        ActionSequencerUpdater.Enable(Svc.PluginInterface.ConfigDirectory.FullName + "\\Conditions");

        Svc.Framework.Update += RSRGateUpdate;
        Svc.Framework.Update += RSRTeachingClearUpdate;
        Svc.Framework.Update += RSRInvalidUpdate;
        Svc.Framework.Update += RSRActivatedCoreUpdate;
        Svc.Framework.Update += RSRActivatedHighlightUpdate;
        Svc.Framework.Update += RSRCommonUpdate;
        Svc.Framework.Update += RSRCleanupUpdate;
        Svc.Framework.Update += RSRRotationAndStateUpdate;
        Svc.Framework.Update += RSRMiscAndTargetFreelyUpdate;
        Svc.Framework.Update += RSRResetUpdate;
    }

    private static void RSRGateUpdate(IFramework framework)
    {
        try
        {
            _timeSinceUpdate += framework.UpdateDelta;
            if (Service.Config.MinUpdatingTime > 0 && _timeSinceUpdate < TimeSpan.FromSeconds(Service.Config.MinUpdatingTime))
            {
                _shouldRunThisCycle = false;
                return;
            }

            _timeSinceUpdate = TimeSpan.Zero;
            _isValidThisCycle = IsValid;
            _isActivatedThisCycle = DataCenter.IsActivated();
            _shouldRunThisCycle = true;

            if (_isValidThisCycle && !_rotationsLoaded)
            {
                RotationUpdater.LoadBuiltInRotations();
                _rotationsLoaded = true;
            }
        }
        catch (Exception ex)
        {
            LogOnce("GateUpdate Exception", ex);
        }
    }

    private static void RSRTeachingClearUpdate(IFramework framework)
    {
        if (!_shouldRunThisCycle) return;

        if (Service.Config.TeachingMode)
        {
            try
            {
                HotbarHighlightManager.HotbarIDs.Clear();
            }
            catch (Exception ex)
            {
                LogOnce("HotbarHighlightManager.HotbarIDs.Clear Exception", ex);
            }
        }
    }

    private static void RSRInvalidUpdate(IFramework framework)
    {
        if (!_shouldRunThisCycle) return;

        if (!_isValidThisCycle)
        {
            try
            {
                RSCommands.UpdateRotationState();
                ActionUpdater.ClearNextAction();
                MiscUpdater.UpdateEntry();
                ActionUpdater.NextAction = ActionUpdater.NextGCDAction = null;
            }
            catch (Exception ex)
            {
                LogOnce("RSRInvalidUpdate Exception", ex);
            }
            _shouldRunThisCycle = false;
        }
    }

    private static void RSRActivatedCoreUpdate(IFramework framework)
    {
        if (!_shouldRunThisCycle) return;

        try
        {
            StateUpdater.UpdateState();

            if (_isActivatedThisCycle)
            {
                TargetUpdater.UpdateTargets();
            }

            if (!_isActivatedThisCycle)
            {
                ActionUpdater.ClearNextAction();
                MovingUpdater.UpdateCanMove(true);
                return;
            }

            bool canDoAction = ActionUpdater.CanDoAction();
            MovingUpdater.UpdateCanMove(canDoAction);

            if (canDoAction)
            {
                RSCommands.DoAction();
            }

            MacroUpdater.UpdateMacro();
            ActionUpdater.UpdateNextAction();

            if (DataCenter.IsTargetOnly)
            {
                RSCommands.UpdateTargetFromNextAction();
            }

            ActionSequencerUpdater.UpdateActionSequencerAction();
        }
        catch (Exception ex)
        {
            LogOnce("RSRUpdate DC Exception", ex);
        }
    }

    private static void RSRActivatedHighlightUpdate(IFramework framework)
    {
        if (!_shouldRunThisCycle) return;

        if (_isActivatedThisCycle && Service.Config.TeachingMode && ActionUpdater.NextAction is not null)
        {
            try
            {
                IAction nextAction = ActionUpdater.NextAction;
                HotbarID? hotbar = null;
                if (nextAction is IBaseItem item)
                {
                    hotbar = new HotbarID(HotbarSlotType.Item, item.ID);
                }
                else if (nextAction is IBaseAction baseAction)
                {
                    hotbar = baseAction.Action.ActionCategory.RowId is 10 or 11
                            ? GetGeneralActionHotbarID(baseAction)
                            : new HotbarID(HotbarSlotType.Action, baseAction.AdjustedID);
                }

                if (hotbar.HasValue)
                {
                    _ = HotbarHighlightManager.HotbarIDs.Add(hotbar.Value);
                }
            }
            catch (Exception ex)
            {
                LogOnce("Hotbar Highlighting Exception", ex);
            }
        }

        try
        {
            HotbarDisabledColor.ApplyFrame();
        }
        catch (Exception ex)
        {
            LogOnce("Hotbar Disabled Redden Exception", ex);
        }
    }

    private static void RSRCommonUpdate(IFramework framework)
    {
        if (!_shouldRunThisCycle) return;

        try
        {
            if (_isActivatedThisCycle)
            {
                ActionUpdater.UpdateCombatInfo();
                ActionManagerEx.Instance.UpdateTweaks();
            }

            RotationSolverPlugin.UpdateDisplayWindow();
        }
        catch (Exception ex)
        {
            LogOnce("CommonUpdate Exception", ex);
        }
    }

    private static void RSRCleanupUpdate(IFramework framework)
    {
        if (!_shouldRunThisCycle) return;

        try
        {
            if (DataCenter.SystemWarnings.Count > 0)
            {
                DateTime now = DateTime.Now;
                List<string> keysToRemove = [];
                foreach (KeyValuePair<string, DateTime> kvp in DataCenter.SystemWarnings)
                {
                    if (kvp.Value + TimeSpan.FromMinutes(10) < now) keysToRemove.Add(kvp.Key);
                }
                foreach (string key in keysToRemove) _ = DataCenter.SystemWarnings.Remove(key);
            }

            if (!DataCenter.VfxDataQueue.IsEmpty)
            {
                while (DataCenter.VfxDataQueue.TryPeek(out var vfx) && vfx.TimeDuration > TimeSpan.FromSeconds(6))
                {
                    _ = DataCenter.VfxDataQueue.TryDequeue(out _);
                }
            }
        }
        catch (Exception ex)
        {
            LogOnce("CleanupUpdate Exception", ex);
        }
    }

    private static void RSRRotationAndStateUpdate(IFramework framework)
    {
        if (!_shouldRunThisCycle) return;

        try
        {
            RotationUpdater.UpdateRotation();
            RSCommands.UpdateRotationState();

            if (Service.Config.TeachingMode)
            {
                try
                {
                    HotbarHighlightManager.UpdateSettings();
                }
                catch (Exception ex)
                {
                    LogOnce("HotbarHighlightManager.UpdateSettings Exception", ex);
                }
            }
        }
        catch (Exception ex)
        {
            LogOnce("RotationAndStateUpdate Exception", ex);
        }
    }

    private static void RSRMiscAndTargetFreelyUpdate(IFramework framework)
    {
        if (!_shouldRunThisCycle) return;

        try
        {
            // Allow MiscUpdater to run for overlay updates
            MiscUpdater.UpdateMisc();

            // Gate heavy TargetFreely logic
            if (_isActivatedThisCycle && Service.Config.TargetFreely && !DataCenter.IsPvP)
            {
                IAction? nextAction2 = ActionUpdater.NextAction;
                if (nextAction2 == null && Svc.Targets.Target == null)
                {
                    IBattleChara? closestEnemy = null;
                    float minDistance = float.MaxValue;

                    foreach (var enemy in DataCenter.AllHostileTargets)
                    {
                        if (enemy == null || !enemy.IsEnemy() || enemy == Player.Object) continue;

                        float distance = Vector3.Distance(Player.Object.Position, enemy.Position);
                        if (distance < minDistance)
                        {
                            minDistance = distance;
                            closestEnemy = enemy;
                        }
                    }

                    if (closestEnemy != null)
                    {
                        Svc.Targets.Target = closestEnemy;
                        PluginLog.Information($"Targeting {closestEnemy}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LogOnce("Secondary RSRUpdate Exception", ex);
        }
    }

    private static void RSRResetUpdate(IFramework framework)
    {
        if (!_shouldRunThisCycle) return;
        _shouldRunThisCycle = false;
    }

    private static HotbarID? GetGeneralActionHotbarID(IBaseAction baseAction)
    {
        Lumina.Excel.ExcelSheet<GeneralAction> generalActions = Svc.Data.GetExcelSheet<GeneralAction>();
        if (generalActions == null) return null;

        foreach (GeneralAction gAct in generalActions)
        {
            if (gAct.Action.RowId == baseAction.ID)
                return new HotbarID(HotbarSlotType.GeneralAction, gAct.RowId);
        }
        return null;
    }

    private static void LogOnce(string context, Exception ex)
    {
        if (_threadException == ex) return;
        _threadException = ex;
        PluginLog.Error($"{context}: {ex.Message}");
        if (Service.Config.InDebug) _ = BasicWarningHelper.AddSystemWarning(context);
    }

    public static void Dispose()
    {
        Svc.Framework.Update -= RSRGateUpdate;
        Svc.Framework.Update -= RSRTeachingClearUpdate;
        Svc.Framework.Update -= RSRInvalidUpdate;
        Svc.Framework.Update -= RSRActivatedCoreUpdate;
        Svc.Framework.Update -= RSRActivatedHighlightUpdate;
        Svc.Framework.Update -= RSRCommonUpdate;
        Svc.Framework.Update -= RSRCleanupUpdate;
        Svc.Framework.Update -= RSRRotationAndStateUpdate;
        Svc.Framework.Update -= RSRMiscAndTargetFreelyUpdate;
        Svc.Framework.Update -= RSRResetUpdate;

        MiscUpdater.Dispose();
        ActionSequencerUpdater.SaveFiles();
        ActionUpdater.ClearNextAction();
    }
}