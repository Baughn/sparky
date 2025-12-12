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

        _window.Add(_drawingArea);
        _window.ShowAll();
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

        // Draw toolbar
        DrawToolbar(ctx);
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

        // Color based on voltage
        var voltage = data.State.VoltageNormalized;
        var r = Math.Clamp(voltage, 0, 1);
        var g = Math.Clamp(1 - Math.Abs(voltage - 0.5) * 2, 0, 1);
        var b = Math.Clamp(1 - voltage, 0, 1);

        switch (data.Type)
        {
            case CellType.Wire:
                ctx.SetSourceRGB(r * 0.8, g * 0.8, b * 0.8);
                ctx.Rectangle(x + 2, y + 2, CellSize - 4, CellSize - 4);
                ctx.Fill();
                break;

            case CellType.Ground:
                ctx.SetSourceRGB(0.2, 0.8, 0.2);
                ctx.Rectangle(x + 2, y + 2, CellSize - 4, CellSize - 4);
                ctx.Fill();
                // Draw ground symbol
                ctx.SetSourceRGB(0, 0, 0);
                ctx.LineWidth = 2;
                ctx.MoveTo(x + CellSize / 2, y + 4);
                ctx.LineTo(x + CellSize / 2, y + CellSize - 4);
                ctx.Stroke();
                break;

            case CellType.Battery:
                // Yellow for battery
                ctx.SetSourceRGB(0.9, 0.9, 0.2);
                ctx.Rectangle(x + 2, y + 2, CellSize - 4, CellSize - 4);
                ctx.Fill();
                // + symbol
                ctx.SetSourceRGB(0, 0, 0);
                ctx.LineWidth = 2;
                ctx.MoveTo(x + CellSize / 2, y + 4);
                ctx.LineTo(x + CellSize / 2, y + CellSize - 4);
                ctx.MoveTo(x + 4, y + CellSize / 2);
                ctx.LineTo(x + CellSize - 4, y + CellSize / 2);
                ctx.Stroke();
                break;

            case CellType.Resistor:
                // Color by heat (power dissipation)
                var heat = data.State.PowerNormalized;
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
                break;
        }
    }

    private void DrawToolbar(Context ctx)
    {
        var tools = new[] { CellType.Wire, CellType.Battery, CellType.Resistor, CellType.Ground };
        var toolNames = new[] { "Wire [1]", "Battery [2]", "Resistor [3]", "Ground [4]" };
        var y = Padding + _gridHeight * CellSize + 20;

        for (int i = 0; i < tools.Length; i++)
        {
            var x = Padding + i * 100;

            // Highlight selected tool
            if (tools[i] == _selectedTool)
            {
                ctx.SetSourceRGB(0.4, 0.4, 0.6);
                ctx.Rectangle(x - 2, y - 2, 94, 24);
                ctx.Fill();
            }

            ctx.SetSourceRGB(0.9, 0.9, 0.9);
            ctx.MoveTo(x, y + 16);
            ctx.ShowText(toolNames[i]);
        }

        // Show rotation
        ctx.MoveTo(Padding + 420, y + 16);
        ctx.ShowText($"Rotation: {_rotation * 90}° [R]");
    }

    private void OnKeyPress(object o, KeyPressEventArgs args)
    {
        switch (args.Event.Key)
        {
            case Gdk.Key.Key_1:
                _selectedTool = CellType.Wire;
                break;
            case Gdk.Key.Key_2:
                _selectedTool = CellType.Battery;
                break;
            case Gdk.Key.Key_3:
                _selectedTool = CellType.Resistor;
                break;
            case Gdk.Key.Key_4:
                _selectedTool = CellType.Ground;
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

        if (args.Event.Button == 1) // Left click - place
        {
            _pendingInput.Enqueue(new PlaceComponent(pos, _selectedTool, _rotation));
        }
        else if (args.Event.Button == 3) // Right click - remove
        {
            _pendingInput.Enqueue(new RemoveComponent(pos));
        }
    }

    public void Dispose()
    {
        _window.Dispose();
    }

    private record CellRenderData(CellType Type, int Rotation, CellVisualState State);
}
