using ProtoBuf;

namespace Sparky.VSIntegration.PlayerState;

[ProtoContract]
public class PlayerStatePacket
{
    [ProtoMember(1)]
    public PlayerStateKey Key { get; set; }

    [ProtoMember(2)]
    public int Value { get; set; }
}
