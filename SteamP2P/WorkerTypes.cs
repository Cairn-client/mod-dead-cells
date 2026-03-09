namespace DeadCellsMultiplayerMod
{
    internal sealed class WorkerCommand
    {
        public string Type { get; set; } = string.Empty;
        public ulong SteamId { get; set; }
        public int Channel { get; set; }
        public string SendType { get; set; } = string.Empty;
        public string Payload { get; set; } = string.Empty;
    }

    internal sealed class WorkerEvent
    {
        public string Type { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string Error { get; set; } = string.Empty;
        public ulong SteamId { get; set; }
        public int Channel { get; set; }
        public string Payload { get; set; } = string.Empty;
    }

    internal static class WorkerCommandTypes
    {
        public const string Send = "send";
        public const string ClosePeer = "closePeer";
        public const string Stop = "stop";
    }

    internal static class WorkerEventTypes
    {
        public const string Ready = "ready";
        public const string Packet = "packet";
        public const string Warning = "warn";
        public const string Error = "error";
    }
}
