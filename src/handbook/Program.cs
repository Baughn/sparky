using System.Text.Json;
using System.Text.Json.Serialization;
using Sparky.Handbook;
using Sparky.Handbook.Client.Standalone;
using Sparky.Handbook.Protocol;
using Sparky.Handbook.Server;

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
long tick = 0;
var jsonOptions = new JsonSerializerOptions {
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    Converters = { new JsonStringEnumConverter() }
};

while (!client.ShouldClose) {
    var now = DateTime.UtcNow;
    var dt = (float)(now - lastTime).TotalSeconds;
    lastTime = now;

    // Poll client input and log to stdout
    foreach (var input in client.PollInput()) {
        Console.WriteLine(JsonSerializer.Serialize(new { tick, @event = input }, jsonOptions));
        server.HandleInput(input);
    }

    // Tick server and send render commands to client
    var commands = server.Tick(dt);
    client.HandleCommands(commands);

    // Render
    client.Render();

    tick++;

    // Cap frame rate (~60 FPS)
    Thread.Sleep(16);
}

Console.WriteLine("Goodbye!");
