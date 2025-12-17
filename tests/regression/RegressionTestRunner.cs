using System.Text.Json;
using System.Text.Json.Serialization;
using Sparky.TwoD.Protocol;
using Sparky.TwoD.Server;

namespace Sparky.Tests.Regression;

/// <summary>
/// Replays JSONL input files and verifies assertions against cell state.
/// </summary>
public static class RegressionTestRunner {
    public record AssertionResult(bool Passed, string Message, int LineNumber);

    private static readonly JsonSerializerOptions JsonOptions = new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// Runs a regression test file and returns assertion results.
    /// </summary>
    public static List<AssertionResult> RunTestFile(string jsonlPath) {
        var server = new GameServer(width: 24, height: 24);
        var results = new List<AssertionResult>();
        var lineNumber = 0;
        long lastTick = -1;

        foreach (var line in File.ReadLines(jsonlPath)) {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
                continue;

            using var json = JsonDocument.Parse(line);
            var root = json.RootElement;

            if (root.TryGetProperty("event", out var eventProp)) {
                // Check if we need to tick before processing this event (new tick number)
                if (root.TryGetProperty("tick", out var tickProp)) {
                    var tick = tickProp.GetInt64();
                    if (tick != lastTick && lastTick >= 0) {
                        // Tick for each frame change, like the real editor
                        server.Tick(0.016f);
                    }
                    lastTick = tick;
                }

                // Replay input event
                var input = DeserializeInputEvent(root);
                if (input != null) {
                    server.HandleInput(input);
                }
            } else if (root.TryGetProperty("assert", out _)) {
                // Run simulation tick before checking assertion
                server.Tick(0.016f);

                // Verify assertion
                var result = CheckAssertion(server, root, lineNumber);
                results.Add(result);
            }
        }

        return results;
    }

    private static InputEvent? DeserializeInputEvent(JsonElement root) {
        if (!root.TryGetProperty("event", out var eventElement))
            return null;

        // Get the $type discriminator
        if (!eventElement.TryGetProperty("$type", out var typeElement))
            return null;

        var typeName = typeElement.GetString();

        return typeName switch {
            "PlaceComponent" => DeserializePlaceComponent(eventElement),
            "RemoveComponent" => DeserializeRemoveComponent(eventElement),
            "ToggleSwitchInput" => DeserializeToggleSwitchInput(eventElement),
            "SetComponentValue" => DeserializeSetComponentValue(eventElement),
            _ => null
        };
    }

    private static PlaceComponent DeserializePlaceComponent(JsonElement element) {
        var pos = DeserializeGridPos(element.GetProperty("pos"));
        var type = DeserializeCellType(element.GetProperty("type"));
        var rotation = element.TryGetProperty("rotation", out var rotProp) ? rotProp.GetInt32() : 0;
        return new PlaceComponent(pos, type, rotation);
    }

    private static RemoveComponent DeserializeRemoveComponent(JsonElement element) {
        var pos = DeserializeGridPos(element.GetProperty("pos"));
        return new RemoveComponent(pos);
    }

    private static ToggleSwitchInput DeserializeToggleSwitchInput(JsonElement element) {
        var pos = DeserializeGridPos(element.GetProperty("pos"));
        return new ToggleSwitchInput(pos);
    }

    private static SetComponentValue DeserializeSetComponentValue(JsonElement element) {
        var pos = DeserializeGridPos(element.GetProperty("pos"));
        var value = element.GetProperty("value").GetDouble();
        return new SetComponentValue(pos, value);
    }

    private static GridPos DeserializeGridPos(JsonElement element) {
        var x = element.GetProperty("x").GetInt32();
        var y = element.GetProperty("y").GetInt32();
        return new GridPos(x, y);
    }

    private static CellType DeserializeCellType(JsonElement element) {
        if (element.ValueKind == JsonValueKind.Number) {
            return (CellType)element.GetInt32();
        } else if (element.ValueKind == JsonValueKind.String) {
            return Enum.Parse<CellType>(element.GetString()!);
        }
        throw new JsonException($"Cannot deserialize CellType from {element.ValueKind}");
    }

    private static AssertionResult CheckAssertion(GameServer server, JsonElement root, int lineNumber) {
        var assertType = root.GetProperty("assert").GetString();

        if (assertType != "cell") {
            return new AssertionResult(false, $"Unknown assertion type: {assertType}", lineNumber);
        }

        var pos = DeserializeGridPos(root.GetProperty("pos"));

        // Get current cell state from server
        var cellState = GetCellState(server, pos);

        if (cellState == null) {
            if (root.TryGetProperty("type", out var typeEl) && typeEl.GetString() == "Empty") {
                return new AssertionResult(true, $"Cell at {pos} is empty as expected", lineNumber);
            }
            return new AssertionResult(false, $"Cell at {pos} does not exist", lineNumber);
        }

        var errors = new List<string>();

        // Check type if specified
        if (root.TryGetProperty("type", out var expectedType)) {
            var expectedTypeName = expectedType.GetString();
            var actualTypeName = cellState.Type.ToString();
            if (expectedTypeName != actualTypeName) {
                errors.Add($"type: expected {expectedTypeName}, got {actualTypeName}");
            }
        }

        // Check rotation if specified
        if (root.TryGetProperty("rotation", out var expectedRotation)) {
            var expected = expectedRotation.GetInt32();
            var actual = cellState.Rotation;
            if (expected != actual) {
                errors.Add($"rotation: expected {expected}, got {actual}");
            }
        }

        // Check state fields if specified
        if (root.TryGetProperty("state", out var expectedState)) {
            var actualState = cellState.State;

            if (expectedState.TryGetProperty("voltage", out var expectedVoltage)) {
                var expected = (float)expectedVoltage.GetDouble();
                var actual = actualState.VoltageNormalized;
                if (!IsWithinTolerance(actual, expected)) {
                    errors.Add($"voltage: expected {expected}, got {actual}");
                }
            }

            if (expectedState.TryGetProperty("current", out var expectedCurrent)) {
                var expected = (float)expectedCurrent.GetDouble();
                var actual = actualState.CurrentNormalized;
                if (!IsWithinTolerance(actual, expected)) {
                    errors.Add($"current: expected {expected}, got {actual}");
                }
            }

            if (expectedState.TryGetProperty("power", out var expectedPower)) {
                var expected = (float)expectedPower.GetDouble();
                var actual = actualState.PowerNormalized;
                if (!IsWithinTolerance(actual, expected)) {
                    errors.Add($"power: expected {expected}, got {actual}");
                }
            }

            if (expectedState.TryGetProperty("switchClosed", out var expectedSwitch)) {
                var expected = expectedSwitch.GetBoolean();
                var actual = actualState.SwitchClosed;
                if (expected != actual) {
                    errors.Add($"switchClosed: expected {expected}, got {actual}");
                }
            }
        }

        if (errors.Count > 0) {
            return new AssertionResult(false, $"Cell at ({pos.X},{pos.Y}): {string.Join("; ", errors)}", lineNumber);
        }

        return new AssertionResult(true, $"Cell at ({pos.X},{pos.Y}) matches expected state", lineNumber);
    }

    private static bool IsWithinTolerance(float actual, float expected) {
        // 1% relative tolerance, or 0.001 absolute tolerance if expected is near zero
        var tolerance = Math.Max(Math.Abs(expected) * 0.01f, 0.001f);
        return Math.Abs(actual - expected) <= tolerance;
    }

    private record CellInfo(CellType Type, int Rotation, CellVisualState State);

    private static CellInfo? GetCellState(GameServer server, GridPos pos) {
        // Get full state and find the cell at the given position
        foreach (var command in server.GetFullState()) {
            if (command is SetCell setCell && setCell.Pos == pos) {
                return new CellInfo(setCell.Type, setCell.Rotation, setCell.State);
            }
        }
        return null;
    }
}
