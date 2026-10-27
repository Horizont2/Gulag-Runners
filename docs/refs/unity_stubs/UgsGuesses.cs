// GUESSES, and the name says so.
//
// Every other file in unity_stubs/ stands in for an API that is known: uGUI's Button
// has an onClick, the Input System has a ButtonControl, and the stub is a transcription.
// This one is not. The container these were written in cannot reach docs.unity.com or
// packages.unity.com -- the proxy answers 403 to both -- so the Unity Gaming Services
// surface below was written from memory and shaped to fit the calls UgsQueue makes.
//
// What a green compile_check therefore proves about UgsQueue.cs: that it is internally
// consistent, that its syntax is right, and that nothing else in the project broke. What
// it does NOT prove is that any of these names exist. Unity's own compiler is the check
// that matters, and these stubs are here so one unverifiable file does not take the
// whole suite red while that check is pending.
//
// When Unity disagrees, Unity is right. Correct the real file first, then correct these
// to match, so the next person reading them is not misled twice.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Unity.Services.Core
{
    public enum ServicesInitializationState { Uninitialized, Initializing, Initialized }

    public static class UnityServices
    {
        public static ServicesInitializationState State { get; set; }
        public static Task InitializeAsync() => Task.CompletedTask;
    }
}

namespace Unity.Services.Authentication
{
    public class AuthenticationService
    {
        public static AuthenticationService Instance { get; } = new AuthenticationService();
        public bool IsSignedIn { get; set; }
        public string PlayerId { get; set; }
        public Task SignInAnonymouslyAsync() => Task.CompletedTask;
    }
}

namespace Unity.Services.Multiplayer
{
    public enum VisibilityPropertyOptions { Public, Member, Private }

    public class PlayerProperty
    {
        public PlayerProperty(string value, VisibilityPropertyOptions visibility) { Value = value; }
        public string Value { get; set; }
    }

    public interface IReadOnlyPlayer
    {
        string Id { get; }
        IReadOnlyDictionary<string, PlayerProperty> Properties { get; }
    }

    public interface ISession
    {
        string Id { get; }
        IReadOnlyPlayer CurrentPlayer { get; }
        IReadOnlyList<IReadOnlyPlayer> Players { get; }
        Task LeaveAsync();
    }

    public class MatchmakerOptions
    {
        public string QueueName { get; set; }
        public Dictionary<string, PlayerProperty> PlayerProperties { get; set; }
    }

    public class SessionOptions
    {
        public int MaxPlayers { get; set; }
        public SessionOptions WithRelayNetwork() => this;
    }

    public class MultiplayerService
    {
        public static MultiplayerService Instance { get; } = new MultiplayerService();

        public Task<ISession> MatchmakeSessionAsync(MatchmakerOptions ticket,
                                                    SessionOptions session,
                                                    CancellationToken stop = default)
            => Task.FromResult<ISession>(null);
    }
}

// ---------------------------------------------------------------- Unity Transport
//
// Same caveat as above, with one difference worth knowing: com.unity.transport 2.7.4
// IS installed in the project, so Unity's own compiler checks RelayTransport against
// the real thing. These exist only so compile_check here can parse the file at all.

namespace Unity.Networking.Transport
{
    public struct NetworkEndpoint
    {
        public static NetworkEndpoint AnyIpv4 => default;
    }

    public struct NetworkConnection
    {
        public bool IsCreated { get; }
    }

    public struct DataStreamReader
    {
        public int Length { get; }
        public byte ReadByte() => 0;
    }

    public struct DataStreamWriter
    {
        public void WriteByte(byte value) { }
    }

    public struct NetworkSettings
    {
        public NetworkSettings WithRelayParameters(
            ref Unity.Networking.Transport.Relay.RelayServerData data) => this;
    }

    public static class NetworkEvent
    {
        public enum Type { Empty, Data, Connect, Disconnect }
    }

    public struct JobHandleStub { public void Complete() { } }

    public struct NetworkDriver
    {
        public static NetworkDriver Create(NetworkSettings settings) => default;

        public bool IsCreated { get; }
        public int Bind(NetworkEndpoint endpoint) => 0;
        public int Listen() => 0;
        public NetworkConnection Connect() => default;
        public NetworkConnection Accept() => default;
        public JobHandleStub ScheduleUpdate() => default;

        public NetworkEvent.Type PopEvent(out NetworkConnection connection,
                                          out DataStreamReader reader)
        {
            connection = default;
            reader = default;
            return NetworkEvent.Type.Empty;
        }

        public int BeginSend(NetworkConnection connection, out DataStreamWriter writer)
        {
            writer = default;
            return 0;
        }

        public int EndSend(DataStreamWriter writer) => 0;
        public void Dispose() { }
    }
}

namespace Unity.Networking.Transport.Relay
{
    public struct RelayServerData { }
}
