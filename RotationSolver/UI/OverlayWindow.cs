using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using ECommons.DalamudServices;
using ECommons.Logging;
using RotationSolver.UI.HighlightTeachingMode;
using System.Diagnostics;

namespace RotationSolver.UI;

/// <summary>
/// The Overlay Window
/// </summary>
internal class OverlayWindow : Window
{
    private const ImGuiWindowFlags BaseFlags = ImGuiWindowFlags.NoBackground
    | ImGuiWindowFlags.NoBringToFrontOnFocus
    | ImGuiWindowFlags.NoDecoration
    | ImGuiWindowFlags.NoDocking
    | ImGuiWindowFlags.NoFocusOnAppearing
    | ImGuiWindowFlags.NoInputs
    | ImGuiWindowFlags.NoNav;

    // Async update support and throttling for sync path
    private volatile Task<IDrawing2D[]>? _updateTask;

    // OPTIMIZATION 1: Reusable buffer to avoid allocating Lists/Arrays every frame in the draw loop
    private readonly List<IDrawing2D> _renderBuffer = [];

    private readonly Stopwatch _throttle = Stopwatch.StartNew();
    private const int SyncUpdateMs = 33; // ~30 FPS updates in sync mode

    // OPTIMIZATION 2: Cache the comparer to avoid delegate allocation (From your code)
    private static readonly Comparison<IDrawing2D> _drawingComparer = (a, b) => GetDrawingOrder(a).CompareTo(GetDrawingOrder(b));

    public OverlayWindow()
        : base(nameof(OverlayWindow), BaseFlags, true)
    {
        IsOpen = true;
        AllowClickthrough = true;
        RespectCloseHotkey = false;
    }

    public override void PreDraw()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGuiHelpers.SetNextWindowPosRelativeMainViewport(Vector2.Zero);
        ImGui.SetNextWindowSize(ImGuiHelpers.MainViewport.Size);

        base.PreDraw();
    }

    public override unsafe void Draw()
    {
        if (!HotbarHighlightManager.Enable || Svc.ClientState == null || Svc.ClientState.LocalPlayer == null)
        {
            return;
        }

        // Save and disable AA fill for performance of large overlays
        bool prevAAFill = ImGui.GetStyle().AntiAliasedFill;
        ImGui.GetStyle().AntiAliasedFill = false;

        try
        {
            if (HotbarHighlightManager.UseTaskToAccelerate)
            {
                if (_updateTask == null || _updateTask.IsCompleted)
                {
                    _updateTask = Task.Run(HotbarHighlightManager.To2DAsync);
                }
                if (_updateTask.IsCompletedSuccessfully)
                {
                    var result = _updateTask.Result;
                    UpdateRenderBuffer(result);
                }
            }
            else
            {
                if (_throttle.ElapsedMilliseconds >= SyncUpdateMs)
                {
                    var result = HotbarHighlightManager.To2DAsync().GetAwaiter().GetResult();
                    UpdateRenderBuffer(result);
                    _throttle.Restart();
                }
            }

            ImDrawListPtr drawList = ImGui.GetWindowDrawList();
            if (drawList.Handle == null)
            {
                PluginLog.Warning($"{nameof(OverlayWindow)}: Window draw list is null.");
                return;
            }

            // OPTIMIZATION: Iterate the buffer directly
            foreach (IDrawing2D item in _renderBuffer)
            {
                item.Draw();
            }
        }
        catch (Exception ex)
        {
            PluginLog.Warning($"{nameof(OverlayWindow)} failed to draw on Screen. {ex.Message}");
        }
        finally
        {
            ImGui.GetStyle().AntiAliasedFill = prevAAFill;
        }
    }

    private void UpdateRenderBuffer(IDrawing2D[]? newElements)
    {
        _renderBuffer.Clear();
        if (newElements != null)
        {
            _renderBuffer.AddRange(newElements);
        }
        // Use the cached comparer
        _renderBuffer.Sort(_drawingComparer);
    }

    private static int GetDrawingOrder(object drawing)
    {
        return drawing switch
        {
            PolylineDrawing poly => poly._thickness == 0 ? 0 : 1,
            ImageDrawing => 1,
            _ => 2,
        };
    }

    public override void PostDraw()
    {
        ImGui.PopStyleVar();
        base.PostDraw();
    }
}