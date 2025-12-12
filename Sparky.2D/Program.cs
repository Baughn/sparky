using Sparky.TwoD;
using Sparky.TwoD.Client;
using Sparky.TwoD.Protocol;
using Sparky.TwoD.Server;

// Create server and client
var server = new GameServer(width: 24, height: 24);
using var client = new StandaloneClient();

// Initialize client with full state
client.HandleCommands(server.GetFullState());

Console.WriteLine("Sparky 2D Circuit Editor");
Console.WriteLine("========================");
Console.WriteLine("Controls:");
Console.WriteLine("  1-4: Select tool (Wire, Battery, Resistor, Ground)");
Console.WriteLine("  R: Rotate component");
Console.WriteLine("  Left click: Place component");
Console.WriteLine("  Right click: Remove component");
Console.WriteLine("  Q/Escape: Quit");
Console.WriteLine();

// Main loop
var lastTime = DateTime.UtcNow;
while (!client.ShouldClose)
{
    var now = DateTime.UtcNow;
    var dt = (float)(now - lastTime).TotalSeconds;
    lastTime = now;

    // Poll client input
    foreach (var input in client.PollInput())
    {
        server.HandleInput(input);
    }

    // Tick server and send render commands to client
    var commands = server.Tick(dt);
    client.HandleCommands(commands);

    // Render
    client.Render();

    // Cap frame rate (~60 FPS)
    Thread.Sleep(16);
}

Console.WriteLine("Goodbye!");
