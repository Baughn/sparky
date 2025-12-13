using System.Runtime.InteropServices;
using System.Text.Json;
using Cairo;
using Gtk;
using Sparky.TwoD.Protocol;

namespace Sparky.TwoD.Client;

/// <summary>
/// Standalone client using GTK + Cairo for rendering.
/// </summary>
/// <remarks>
/// Uses a GTK window with a DrawingArea for Cairo rendering.
/// This matches the Cairo API used by Vintage Story for 2D GUI elements.
/// </remarks>
public class StandaloneClient : IGameClient, IDisposable
{
    // macOS native APIs for window activation (GTK's Present() doesn't work reliably on macOS)
    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_getClass")]
    private static extern IntPtr objc_getClass(string className);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "sel_registerName")]
    private static extern IntPtr sel_registerName(string selector);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_bool(IntPtr receiver, IntPtr selector, bool arg);

    private static void BringAppToFront()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var nsApp = objc_getClass("NSApplication");
            var sharedApp = sel_registerName("sharedApplication");
            var activate = sel_registerName("activateIgnoringOtherApps:");

            var app = objc_msgSend(nsApp, sharedApp);
            objc_msgSend_bool(app, activate, true);
        }
        // On Linux, GTK's Present() works fine - nothing extra needed
    }

    private readonly Window _window;
    private readonly DrawingArea _drawingArea;

    // Grid state
    private int _gridWidth = 32;
    private int _gridHeight = 32;
    private readonly Dictionary<GridPos, CellRenderData> _cells = new();

    // Input state
    private readonly Queue<InputEvent> _pendingInput = new();
    private CellType _selectedTool = CellType.Wire;
    private int _rotation = 0;
    private bool _debugMode = false;

    // Hover state for tooltip
    private GridPos? _hoveredCell;

    // Drag state for wire routing
    private bool _isDragging;
    private GridPos? _dragStart;
    private readonly List<GridPos> _dragPath = new();
    private bool? _dragHorizontalFirst;  // null until direction determined, then locked

    // Rendering
    private const int CellSize = 20;
    private const int Padding = 10;

    public bool ShouldClose { get; private set; }

    public StandaloneClient()
    {
        Application.Init();

        _window = new Window("Sparky 2D - Circuit Editor");
        _window.SetDefaultSize(800, 600);
        _window.DeleteEvent += (_, _) => ShouldClose = true;
        _window.KeyPressEvent += OnKeyPress;

        _drawingArea = new DrawingArea();
        _drawingArea.Drawn += OnDraw;
        _drawingArea.AddEvents((int)(Gdk.EventMask.ButtonPressMask |
                                      Gdk.EventMask.ButtonReleaseMask |
                                      Gdk.EventMask.PointerMotionMask));
        _drawingArea.ButtonPressEvent += OnButtonPress;
        _drawingArea.ButtonReleaseEvent += OnButtonRelease;
        _drawingArea.MotionNotifyEvent += OnMotionNotify;

        _window.Add(_drawingArea);
        _window.ShowAll();

        // Bring window to front
        _window.Present();
        BringAppToFront();
    }

    public void HandleCommand(RenderCommand command)
    {
        switch (command)
        {
            case SetGridSize size:
                _gridWidth = size.Width;
                _gridHeight = size.Height;
                _cells.Clear();
                break;

            case SetCell cell:
                _cells[cell.Pos] = new CellRenderData(cell.Type, cell.Rotation, cell.State);
                break;

            case ClearCell clear:
                _cells.Remove(clear.Pos);
                break;

            case RenderBatch batch:
                foreach (var cmd in batch.Commands)
                    HandleCommand(cmd);
                break;
        }
    }

    public void HandleCommands(IEnumerable<RenderCommand> commands)
    {
        foreach (var cmd in commands)
            HandleCommand(cmd);
    }

    public IEnumerable<InputEvent> PollInput()
    {
        // Process GTK events
        while (Application.EventsPending())
        {
            Application.RunIteration();
        }

        // Return queued input events
        while (_pendingInput.Count > 0)
        {
            yield return _pendingInput.Dequeue();
        }
    }

    public void Render()
    {
        _drawingArea.QueueDraw();
    }

    private void OnDraw(object o, DrawnArgs args)
    {
        var ctx = args.Cr;

        // Clear background
        ctx.SetSourceRGB(0.15, 0.15, 0.15);
        ctx.Paint();

        // Draw grid
        DrawGrid(ctx);

        // Draw cells
        foreach (var (pos, data) in _cells)
        {
            DrawCell(ctx, pos, data);
        }

        // Draw ghost preview
        DrawGhostPreview(ctx);

        // Draw toolbar
        DrawToolbar(ctx);

        // Draw hover tooltip
        DrawHoverTooltip(ctx);
    }

    private void DrawGrid(Context ctx)
    {
        ctx.SetSourceRGB(0.3, 0.3, 0.3);
        ctx.LineWidth = 0.5;

        for (int x = 0; x <= _gridWidth; x++)
        {
            ctx.MoveTo(Padding + x * CellSize, Padding);
            ctx.LineTo(Padding + x * CellSize, Padding + _gridHeight * CellSize);
        }

        for (int y = 0; y <= _gridHeight; y++)
        {
            ctx.MoveTo(Padding, Padding + y * CellSize);
            ctx.LineTo(Padding + _gridWidth * CellSize, Padding + y * CellSize);
        }

        ctx.Stroke();
    }

    private void DrawCell(Context ctx, GridPos pos, CellRenderData data)
    {
        var x = Padding + pos.X * CellSize;
        var y = Padding + pos.Y * CellSize;

        // Color based on voltage: blue (-5V) → green (0V) → red (+5V)
        // voltage is normalized to 10V scale, so ±5V = ±0.5
        var voltage = data.State.VoltageNormalized;
        var r = Math.Clamp(voltage * 2, 0, 1);           // 0 at ≤0V, 1 at +5V
        var g = Math.Clamp(1 - Math.Abs(voltage) * 2, 0, 1); // 1 at 0V, 0 at ±5V
        var b = Math.Clamp(-voltage * 2, 0, 1);          // 1 at -5V, 0 at ≥0V
        var heat = data.State.PowerNormalized;

        // Switch expression ensures compile error if new CellType is added without handling
        System.Action draw = data.Type switch
        {
            CellType.Empty => () => { },

            CellType.Wire => () =>
            {
                ctx.SetSourceRGB(r * 0.8, g * 0.8, b * 0.8);
                ctx.Rectangle(x + 2, y + 2, CellSize - 4, CellSize - 4);
                ctx.Fill();
            },

            CellType.Ground => () =>
            {
                ctx.SetSourceRGB(0.2, 0.8, 0.2);
                ctx.Rectangle(x + 2, y + 2, CellSize - 4, CellSize - 4);
                ctx.Fill();
                // Draw ground symbol
                ctx.SetSourceRGB(0, 0, 0);
                ctx.LineWidth = 2;
                ctx.MoveTo(x + CellSize / 2, y + 4);
                ctx.LineTo(x + CellSize / 2, y + CellSize - 4);
                ctx.Stroke();
            },

            CellType.Battery => () =>
            {
                // Yellow for battery negative terminal (origin)
                ctx.SetSourceRGB(0.9, 0.9, 0.2);
                ctx.Rectangle(x + 2, y + 2, CellSize - 4, CellSize - 4);
                ctx.Fill();
                // - symbol (this is the NEGATIVE terminal)
                ctx.SetSourceRGB(0, 0, 0);
                ctx.LineWidth = 2;
                ctx.MoveTo(x + 4, y + CellSize / 2);
                ctx.LineTo(x + CellSize - 4, y + CellSize / 2);
                ctx.Stroke();
            },

            CellType.BatteryBody => () =>
            {
                // Dark gray for battery body (insulator)
                ctx.SetSourceRGB(0.4, 0.4, 0.3);
                ctx.Rectangle(x + 2, y + 2, CellSize - 4, CellSize - 4);
                ctx.Fill();
            },

            CellType.BatteryPositive => () =>
            {
                // Yellow for battery positive terminal (far end)
                ctx.SetSourceRGB(0.9, 0.9, 0.2);
                ctx.Rectangle(x + 2, y + 2, CellSize - 4, CellSize - 4);
                ctx.Fill();
                // + symbol (this is the POSITIVE terminal)
                ctx.SetSourceRGB(0, 0, 0);
                ctx.LineWidth = 2;
                ctx.MoveTo(x + CellSize / 2, y + 4);
                ctx.LineTo(x + CellSize / 2, y + CellSize - 4);
                ctx.MoveTo(x + 4, y + CellSize / 2);
                ctx.LineTo(x + CellSize - 4, y + CellSize / 2);
                ctx.Stroke();
            },

            CellType.Resistor => () =>
            {
                // Color by heat (power dissipation)
                ctx.SetSourceRGB(0.5 + heat * 0.5, 0.3 * (1 - heat), 0.1);
                ctx.Rectangle(x + 2, y + 2, CellSize - 4, CellSize - 4);
                ctx.Fill();
                // R symbol
                ctx.SetSourceRGB(0, 0, 0);
                ctx.LineWidth = 1;
                ctx.MoveTo(x + 6, y + 6);
                ctx.LineTo(x + CellSize - 6, y + 6);
                ctx.LineTo(x + CellSize - 6, y + CellSize - 6);
                ctx.LineTo(x + 6, y + CellSize - 6);
                ctx.ClosePath();
                ctx.Stroke();
            },

            CellType.ResistorBody => () =>
            {
                // Darker brown for resistor body (insulator)
                ctx.SetSourceRGB(0.4, 0.25, 0.1);
                ctx.Rectangle(x + 2, y + 2, CellSize - 4, CellSize - 4);
                ctx.Fill();
            },

            CellType.ResistorTerminalB => () =>
            {
                // Same heat-based coloring as origin terminal
                ctx.SetSourceRGB(0.5 + heat * 0.5, 0.3 * (1 - heat), 0.1);
                ctx.Rectangle(x + 2, y + 2, CellSize - 4, CellSize - 4);
                ctx.Fill();
            },

            CellType.Switch => () =>
            {
                // Gray background with voltage-based tint
                ctx.SetSourceRGB(0.4 + r * 0.2, 0.4 + g * 0.2, 0.4 + b * 0.2);
                ctx.Rectangle(x + 2, y + 2, CellSize - 4, CellSize - 4);
                ctx.Fill();

                // Draw switch symbol based on state
                ctx.SetSourceRGB(0, 0, 0);
                ctx.LineWidth = 2;

                if (data.State.SwitchClosed)
                {
                    // Closed: horizontal line (conductor)
                    ctx.MoveTo(x + 4, y + CellSize / 2);
                    ctx.LineTo(x + CellSize - 4, y + CellSize / 2);
                    ctx.Stroke();
                }
                else
                {
                    // Open: angled line (broken circuit)
                    ctx.MoveTo(x + 4, y + CellSize / 2);
                    ctx.LineTo(x + CellSize / 2 - 2, y + CellSize / 3);
                    ctx.Stroke();
                    // Small circle at pivot point
                    ctx.Arc(x + CellSize / 2, y + CellSize / 2, 2, 0, 2 * Math.PI);
                    ctx.Stroke();
                    ctx.MoveTo(x + CellSize / 2 + 2, y + CellSize / 2);
                    ctx.LineTo(x + CellSize - 4, y + CellSize / 2);
                    ctx.Stroke();
                }
            },

            CellType.SwitchBody => () =>
            {
                // Gray body (insulator)
                ctx.SetSourceRGB(0.35, 0.35, 0.35);
                ctx.Rectangle(x + 2, y + 2, CellSize - 4, CellSize - 4);
                ctx.Fill();
            },

            CellType.SwitchTerminalB => () =>
            {
                // Same as Switch origin - gray with voltage tint
                ctx.SetSourceRGB(0.4 + r * 0.2, 0.4 + g * 0.2, 0.4 + b * 0.2);
                ctx.Rectangle(x + 2, y + 2, CellSize - 4, CellSize - 4);
                ctx.Fill();
            },
        };

        draw();
    }

    private void DrawToolbar(Context ctx)
    {
        var tools = new[] { CellType.Wire, CellType.Battery, CellType.Resistor, CellType.Switch, CellType.Ground, CellType.Empty };
        var toolNames = new[] { "Wire [1]", "Battery [2]", "Resistor [3]", "Switch [4]", "Ground [5]", "Eraser [6]" };
        var y = Padding + _gridHeight * CellSize + 20;

        for (int i = 0; i < tools.Length; i++)
        {
            var x = Padding + i * 90;

            // Highlight selected tool (only if not in debug mode)
            if (!_debugMode && tools[i] == _selectedTool)
            {
                ctx.SetSourceRGB(0.4, 0.4, 0.6);
                ctx.Rectangle(x - 2, y - 2, 84, 24);
                ctx.Fill();
            }

            ctx.SetSourceRGB(0.9, 0.9, 0.9);
            ctx.MoveTo(x, y + 16);
            ctx.ShowText(toolNames[i]);
        }

        // Debug tool
        var debugX = Padding + 6 * 90;
        if (_debugMode)
        {
            ctx.SetSourceRGB(0.6, 0.4, 0.4);  // Reddish highlight for debug
            ctx.Rectangle(debugX - 2, y - 2, 84, 24);
            ctx.Fill();
        }
        ctx.SetSourceRGB(0.9, 0.9, 0.9);
        ctx.MoveTo(debugX, y + 16);
        ctx.ShowText("Debug [7]");

        // Show rotation
        ctx.MoveTo(Padding + 650, y + 16);
        ctx.ShowText($"Rot: {_rotation * 90}° [R]");
    }

    private void DrawHoverTooltip(Context ctx)
    {
        if (_hoveredCell == null)
            return;

        var pos = _hoveredCell.Value;
        if (!_cells.TryGetValue(pos, out var data))
            return;

        // Calculate actual voltage from normalized (assuming 10V scale)
        var actualVoltage = data.State.VoltageNormalized * 10.0;
        var actualCurrent = data.State.CurrentNormalized * 1.0; // 1A scale
        var actualPower = data.State.PowerNormalized * 10.0; // 10W scale

        // Build tooltip text
        var lines = new[]
        {
            $"Cell: ({pos.X}, {pos.Y})",
            $"Type: {data.Type}",
            $"V: {actualVoltage:F2}V (norm: {data.State.VoltageNormalized:F3})",
            $"I: {actualCurrent:F3}A",
            $"P: {actualPower:F2}W"
        };

        // Position tooltip near the cell but offset so it doesn't cover it
        var cellX = Padding + pos.X * CellSize;
        var cellY = Padding + pos.Y * CellSize;
        var tooltipX = cellX + CellSize + 5;
        var tooltipY = cellY;

        // Keep tooltip on screen
        if (tooltipX + 180 > _drawingArea.AllocatedWidth)
            tooltipX = cellX - 185;
        if (tooltipY + 90 > _drawingArea.AllocatedHeight)
            tooltipY = _drawingArea.AllocatedHeight - 95;

        // Draw tooltip background
        ctx.SetSourceRGBA(0.1, 0.1, 0.1, 0.9);
        ctx.Rectangle(tooltipX, tooltipY, 180, 85);
        ctx.Fill();

        // Draw tooltip border
        ctx.SetSourceRGB(0.5, 0.5, 0.5);
        ctx.LineWidth = 1;
        ctx.Rectangle(tooltipX, tooltipY, 180, 85);
        ctx.Stroke();

        // Draw tooltip text
        ctx.SetSourceRGB(0.9, 0.9, 0.9);
        for (int i = 0; i < lines.Length; i++)
        {
            ctx.MoveTo(tooltipX + 5, tooltipY + 15 + i * 14);
            ctx.ShowText(lines[i]);
        }

        // Highlight the hovered cell
        ctx.SetSourceRGBA(1, 1, 1, 0.3);
        ctx.LineWidth = 2;
        ctx.Rectangle(cellX, cellY, CellSize, CellSize);
        ctx.Stroke();
    }

    private void OnKeyPress(object o, KeyPressEventArgs args)
    {
        switch (args.Event.Key)
        {
            case Gdk.Key.Key_1:
                _selectedTool = CellType.Wire;
                _debugMode = false;
                break;
            case Gdk.Key.Key_2:
                _selectedTool = CellType.Battery;
                _debugMode = false;
                break;
            case Gdk.Key.Key_3:
                _selectedTool = CellType.Resistor;
                _debugMode = false;
                break;
            case Gdk.Key.Key_4:
                _selectedTool = CellType.Switch;
                _debugMode = false;
                break;
            case Gdk.Key.Key_5:
                _selectedTool = CellType.Ground;
                _debugMode = false;
                break;
            case Gdk.Key.Key_6:
                _selectedTool = CellType.Empty;  // Eraser
                _debugMode = false;
                break;
            case Gdk.Key.Key_7:
                _debugMode = true;
                break;
            case Gdk.Key.r:
            case Gdk.Key.R:
                _rotation = (_rotation + 1) % 4;
                break;
            case Gdk.Key.Escape:
            case Gdk.Key.q:
                ShouldClose = true;
                break;
        }
    }

    private void OnButtonPress(object o, ButtonPressEventArgs args)
    {
        var gridX = (int)((args.Event.X - Padding) / CellSize);
        var gridY = (int)((args.Event.Y - Padding) / CellSize);

        if (gridX < 0 || gridX >= _gridWidth || gridY < 0 || gridY >= _gridHeight)
            return;

        var pos = new GridPos(gridX, gridY);

        if (args.Event.Button == 1) // Left click - place, erase, or debug
        {
            if (_debugMode)
            {
                // Debug tool: output cell state to stdout
                OutputCellDebugInfo(pos);
            }
            else if (_selectedTool == CellType.Empty)
            {
                // Eraser tool: remove component
                _pendingInput.Enqueue(new RemoveComponent(pos));
            }
            else if (_selectedTool == CellType.Wire)
            {
                // Start wire drag
                _isDragging = true;
                _dragStart = pos;
                _dragHorizontalFirst = null;  // Will be determined on first movement
                _dragPath.Clear();
                _dragPath.Add(pos);
                _drawingArea.QueueDraw();
            }
            else
            {
                // Non-wire: immediate placement
                _pendingInput.Enqueue(new PlaceComponent(pos, _selectedTool, _rotation));
            }
        }
        else if (args.Event.Button == 3) // Right click - edit/toggle
        {
            if (!_cells.TryGetValue(pos, out var cell))
                return;

            switch (cell.Type)
            {
                case CellType.Switch:
                case CellType.SwitchBody:
                case CellType.SwitchTerminalB:
                    _pendingInput.Enqueue(new ToggleSwitchInput(pos));
                    break;
                case CellType.Battery:
                case CellType.BatteryBody:
                case CellType.BatteryPositive:
                    ShowEditDialog(pos, "Battery", "Voltage (V):", 5.0, 0.1, 100, false);
                    break;
                case CellType.Resistor:
                case CellType.ResistorBody:
                case CellType.ResistorTerminalB:
                    ShowEditDialog(pos, "Resistor", "Resistance (Ω):", 1.0, 0.001, 1e9, true);
                    break;
            }
        }
    }

    private void ShowEditDialog(GridPos pos, string title, string label, double defaultValue, double min, double max, bool logarithmic)
    {
        var dialog = new Dialog($"Edit {title}", _window, DialogFlags.Modal,
            "Cancel", ResponseType.Cancel, "OK", ResponseType.Ok);

        var hbox = new HBox(false, 10);
        hbox.PackStart(new Label(label), false, false, 5);

        Entry entry;
        HScale? scale = null;

        if (logarithmic)
        {
            // Logarithmic scale: slider maps log10(min)..log10(max)
            var adj = new Adjustment(
                Math.Log10(defaultValue),
                Math.Log10(min),
                Math.Log10(max),
                0.1, 1, 0);
            scale = new HScale(adj) { WidthRequest = 200 };
            entry = new Entry(defaultValue.ToString("G3")) { WidthRequest = 80 };

            scale.ValueChanged += (s, e) =>
                entry.Text = Math.Pow(10, scale.Value).ToString("G3");
            entry.Changed += (s, e) =>
            {
                if (double.TryParse(entry.Text, out var v) && v > 0)
                    scale.Value = Math.Log10(Math.Clamp(v, min, max));
            };

            hbox.PackStart(scale, true, true, 5);
            hbox.PackStart(entry, false, false, 5);
        }
        else
        {
            entry = new Entry(defaultValue.ToString("F2")) { WidthRequest = 100 };
            hbox.PackStart(entry, false, false, 5);
        }

        dialog.ContentArea.PackStart(hbox, false, false, 10);
        dialog.ShowAll();

        if (dialog.Run() == (int)ResponseType.Ok)
        {
            if (double.TryParse(entry.Text, out var newValue) && newValue > 0)
            {
                _pendingInput.Enqueue(new SetComponentValue(pos, newValue));
            }
        }
        dialog.Destroy();
    }

    private void OnButtonRelease(object o, ButtonReleaseEventArgs args)
    {
        if (args.Event.Button == 1 && _isDragging)
        {
            // Finalize wire drag - place wires on all valid positions
            foreach (var pos in _dragPath)
            {
                if (IsValidPlacement(pos))
                {
                    _pendingInput.Enqueue(new PlaceComponent(pos, CellType.Wire, 0));
                }
            }

            // Reset drag state
            _isDragging = false;
            _dragStart = null;
            _dragPath.Clear();
            _dragHorizontalFirst = null;
            _drawingArea.QueueDraw();
        }
    }

    private void OnMotionNotify(object o, MotionNotifyEventArgs args)
    {
        var gridX = (int)((args.Event.X - Padding) / CellSize);
        var gridY = (int)((args.Event.Y - Padding) / CellSize);

        // Clamp to grid bounds
        gridX = Math.Clamp(gridX, 0, _gridWidth - 1);
        gridY = Math.Clamp(gridY, 0, _gridHeight - 1);

        var currentPos = new GridPos(gridX, gridY);

        // Update hover state
        if (_hoveredCell != currentPos)
        {
            _hoveredCell = currentPos;
        }

        // Update drag path if dragging wire
        if (_isDragging && _dragStart.HasValue)
        {
            UpdateDragPath(_dragStart.Value, currentPos);
        }

        _drawingArea.QueueDraw();
    }

    private void UpdateDragPath(GridPos start, GridPos current)
    {
        // Determine direction on first significant movement
        if (_dragHorizontalFirst == null)
        {
            int dx = Math.Abs(current.X - start.X);
            int dy = Math.Abs(current.Y - start.Y);
            if (dx > 0 || dy > 0)
                _dragHorizontalFirst = dx >= dy;
        }

        _dragPath.Clear();
        bool horizFirst = _dragHorizontalFirst ?? true;

        if (horizFirst)
        {
            // Horizontal then vertical
            int dx = Math.Sign(current.X - start.X);
            if (dx != 0)
            {
                for (int x = start.X; x != current.X; x += dx)
                    _dragPath.Add(new GridPos(x, start.Y));
            }
            int dy = Math.Sign(current.Y - start.Y);
            if (dy != 0)
            {
                for (int y = start.Y; y != current.Y + dy; y += dy)
                    _dragPath.Add(new GridPos(current.X, y));
            }
            else
            {
                // No vertical movement, just add the endpoint
                _dragPath.Add(new GridPos(current.X, start.Y));
            }
        }
        else
        {
            // Vertical then horizontal
            int dy = Math.Sign(current.Y - start.Y);
            if (dy != 0)
            {
                for (int y = start.Y; y != current.Y; y += dy)
                    _dragPath.Add(new GridPos(start.X, y));
            }
            int dx = Math.Sign(current.X - start.X);
            if (dx != 0)
            {
                for (int x = start.X; x != current.X + dx; x += dx)
                    _dragPath.Add(new GridPos(x, current.Y));
            }
            else
            {
                // No horizontal movement, just add the endpoint
                _dragPath.Add(new GridPos(start.X, current.Y));
            }
        }

        // Ensure at least the start position is in the path
        if (_dragPath.Count == 0)
            _dragPath.Add(start);
    }

    private void DrawGhostPreview(Context ctx)
    {
        if (_isDragging && _selectedTool == CellType.Wire)
        {
            // Draw wire path preview
            foreach (var pos in _dragPath)
            {
                bool valid = IsValidPlacement(pos);
                DrawGhostCell(ctx, pos, CellType.Wire, valid);
            }
        }
        else if (_hoveredCell.HasValue && !_isDragging)
        {
            // Draw component preview at hover position
            var cells = ComponentTemplates.GetCells(_selectedTool, _rotation);
            foreach (var (offset, cellType) in cells)
            {
                var pos = new GridPos(_hoveredCell.Value.X + offset.X,
                                       _hoveredCell.Value.Y + offset.Y);
                bool valid = IsValidPlacement(pos);
                DrawGhostCell(ctx, pos, cellType, valid);
            }
        }
    }

    private bool IsValidPlacement(GridPos pos)
    {
        // Check bounds
        if (pos.X < 0 || pos.X >= _gridWidth || pos.Y < 0 || pos.Y >= _gridHeight)
            return false;
        // Check if cell already occupied
        return !_cells.ContainsKey(pos);
    }

    private void DrawGhostCell(Context ctx, GridPos pos, CellType type, bool valid)
    {
        var x = Padding + pos.X * CellSize;
        var y = Padding + pos.Y * CellSize;

        // Draw background fill
        if (valid)
        {
            // Green-tinted translucent for valid placement
            ctx.SetSourceRGBA(0.2, 0.8, 0.2, 0.4);
        }
        else
        {
            // Red-tinted translucent for invalid placement
            ctx.SetSourceRGBA(0.9, 0.2, 0.2, 0.4);
        }
        ctx.Rectangle(x + 2, y + 2, CellSize - 4, CellSize - 4);
        ctx.Fill();

        // Draw type indicator with reduced opacity
        ctx.SetSourceRGBA(0.9, 0.9, 0.9, 0.5);
        ctx.LineWidth = 1;

        switch (type)
        {
            case CellType.Battery:
                // - symbol
                ctx.MoveTo(x + 4, y + CellSize / 2);
                ctx.LineTo(x + CellSize - 4, y + CellSize / 2);
                ctx.Stroke();
                break;

            case CellType.BatteryPositive:
                // + symbol
                ctx.MoveTo(x + CellSize / 2, y + 4);
                ctx.LineTo(x + CellSize / 2, y + CellSize - 4);
                ctx.MoveTo(x + 4, y + CellSize / 2);
                ctx.LineTo(x + CellSize - 4, y + CellSize / 2);
                ctx.Stroke();
                break;

            case CellType.Resistor:
            case CellType.ResistorTerminalB:
                // R box
                ctx.MoveTo(x + 6, y + 6);
                ctx.LineTo(x + CellSize - 6, y + 6);
                ctx.LineTo(x + CellSize - 6, y + CellSize - 6);
                ctx.LineTo(x + 6, y + CellSize - 6);
                ctx.ClosePath();
                ctx.Stroke();
                break;

            case CellType.Ground:
                // Ground line
                ctx.MoveTo(x + CellSize / 2, y + 4);
                ctx.LineTo(x + CellSize / 2, y + CellSize - 4);
                ctx.Stroke();
                break;

            case CellType.Switch:
                // Open switch symbol
                ctx.MoveTo(x + 4, y + CellSize / 2);
                ctx.LineTo(x + CellSize / 2 - 2, y + CellSize / 3);
                ctx.Stroke();
                ctx.MoveTo(x + CellSize / 2 + 2, y + CellSize / 2);
                ctx.LineTo(x + CellSize - 4, y + CellSize / 2);
                ctx.Stroke();
                break;

            default:
                // Wire and body cells - no extra indicator
                break;
        }
    }

    private void OutputCellDebugInfo(GridPos pos)
    {
        var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        if (_cells.TryGetValue(pos, out var cell))
        {
            var debugInfo = new
            {
                debug = "cell",
                pos = new { x = pos.X, y = pos.Y },
                type = cell.Type.ToString(),
                rotation = cell.Rotation,
                state = new
                {
                    voltage = cell.State.VoltageNormalized,
                    current = cell.State.CurrentNormalized,
                    power = cell.State.PowerNormalized,
                    switchClosed = cell.State.SwitchClosed
                }
            };
            Console.WriteLine(JsonSerializer.Serialize(debugInfo, jsonOptions));
        }
        else
        {
            var debugInfo = new
            {
                debug = "cell",
                pos = new { x = pos.X, y = pos.Y },
                type = "Empty",
                rotation = 0,
                state = (object?)null
            };
            Console.WriteLine(JsonSerializer.Serialize(debugInfo, jsonOptions));
        }
    }

    public void Dispose()
    {
        _window.Dispose();
    }

    private record CellRenderData(CellType Type, int Rotation, CellVisualState State);
}
