using ProtoBuf;

namespace Sparky.VSIntegration.BehaviorSync;

/// <summary>
/// Sent from server to clients when a BEBehaviorCircuit is dynamically added
/// to a BlockEntityGeneric (e.g., when placing voxels in stairs).
/// Clients use this to add the behavior locally before tree attributes sync.
/// </summary>
[ProtoContract]
public class BehaviorAddedPacket {
    [ProtoMember(1)]
    public int X { get; set; }

    [ProtoMember(2)]
    public int Y { get; set; }

    [ProtoMember(3)]
    public int Z { get; set; }

    public BehaviorAddedPacket() { }

    public BehaviorAddedPacket(int x, int y, int z) {
        X = x;
        Y = y;
        Z = z;
    }
}
