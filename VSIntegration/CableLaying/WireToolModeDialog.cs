using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace Sparky.VSIntegration.CableLaying;

/// <summary>
/// Dialog for selecting wire tool mode via F key.
/// Shows a 2x3 grid of mode buttons.
/// </summary>
public class WireToolModeDialog : GuiDialog {
    public override string ToggleKeyCombinationCode => "wiretoolmode";

    private readonly Action<WireToolMode> _onModeSelected;

    public WireToolModeDialog(ICoreClientAPI capi, Action<WireToolMode> onModeSelected) : base(capi) {
        _onModeSelected = onModeSelected;
        Compose();
    }

    private void Compose() {
        const double buttonWidth = 100;
        const double buttonHeight = 30;
        const double padding = 5;
        const int cols = 2;

        ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(padding);
        bgBounds.BothSizing = ElementSizing.FitToChildren;

        ElementBounds dialogBounds = ElementStdBounds
            .AutosizedMainDialog
            .WithAlignment(EnumDialogArea.CenterMiddle);

        var composer = capi.Gui
            .CreateCompo("wiretoolmode", dialogBounds)
            .AddShadedDialogBG(bgBounds, true)
            .AddDialogTitleBar("Wire Tool Mode", OnTitleBarClose)
            .BeginChildElements(bgBounds);

        // Create 2x3 grid of buttons
        var modes = new[]
        {
            (WireToolMode.SingleVoxel, "Single Voxel"),
            (WireToolMode.Cable1x1, "Cable 1x1"),
            (WireToolMode.Cable1x2, "Cable 1x2"),
            (WireToolMode.Cable2x2, "Cable 2x2"),
            (WireToolMode.Cable2x3, "Cable 2x3"),
            (WireToolMode.Cable3x5, "Cable 3x5")
        };

        for (int i = 0; i < modes.Length; i++) {
            int col = i % cols;
            int row = i / cols;
            var mode = modes[i];

            double x = padding + col * (buttonWidth + padding);
            double y = 30 + padding + row * (buttonHeight + padding);

            var buttonBounds = ElementBounds.Fixed(x, y, buttonWidth, buttonHeight);
            string buttonKey = $"mode_{mode.Item1}";

            // Capture mode in closure
            var capturedMode = mode.Item1;
            composer.AddButton(mode.Item2, () => OnModeClicked(capturedMode), buttonBounds, CairoFont.ButtonText(), EnumButtonStyle.Normal, buttonKey);
        }

        SingleComposer = composer.EndChildElements().Compose();
    }

    private bool OnModeClicked(WireToolMode mode) {
        _onModeSelected?.Invoke(mode);
        TryClose();
        return true;
    }

    private void OnTitleBarClose() {
        TryClose();
    }
}
