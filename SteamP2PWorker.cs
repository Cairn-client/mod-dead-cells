
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ModCore.Utilities;
using Newtonsoft.Json;
using Steamworks;

namespace DeadCellsMultiplayerMod
{
    internal static class SteamP2PWorkerEnvironment
    {
        public const string EnvWorkerMode = "DCCM_STEAM_WORKER_MODE";
        public const string WorkerModeP2P = "p2p";
        public const string EnvRole = "DCCM_STEAM_P2P_ROLE";
        public const string EnvHostSteamId = "DCCM_STEAM_P2P_HOST_STEAM_ID";
        public const string EnvCommandPipe = "DCCM_STEAM_P2P_COMMAND_PIPE";
        public const string EnvEventPipe = "DCCM_STEAM_P2P_EVENT_PIPE";

        // Reuse SteamConnect bootstrap error path to capture startup failures before pipes are ready.
        public const string EnvBootstrapResponsePath = "DCCM_STEAM_CONNECT_RESPONSE_PATH";
    }

    internal readonly struct SteamP2PWorkerPacket
    {
        public readonly ulong RemoteSteamId;
        public readonly int Channel;
        public readonly string Payload;

        public SteamP2PWorkerPacket(ulong remoteSteamId, int channel, string payload)
        {
            RemoteSteamId = remoteSteamId;
            Channel = channel;
            Payload = payload ?? string.Empty;
        }
    }

    internal sealed class SteamP2PWorkerBridge : IDisposable
    {
        private const int StartupTimeoutMs = 15000;
        private const int PipeConnectPollMs = 25;

        private readonly Process _process;
        private readonly NamedPipeServerStream _commandPipe;
        private readonly NamedPipeServerStream _eventPipe;
        private readonly StreamWriter _commandWriter;
        private readonly StreamReader _eventReader;
        private readonly string _bootstrapResponsePath;
        private readonly object _commandSync = new();
        private readonly ConcurrentQueue<SteamP2PWorkerPacket> _packets = new();
        private readonly ConcurrentQueue<string> _warnings = new();
        private readonly CancellationTokenSource _readerCts = new();
        private readonly Task _readerTask;
        private volatile bool _disposed;

        private SteamP2PWorkerBridge(
            Process process,
            NamedPipeServerStream commandPipe,
            NamedPipeServerStream eventPipe,
            StreamWriter commandWriter,
            StreamReader eventReader,
            string bootstrapResponsePath)
        {
            _process = process;
            _commandPipe = commandPipe;
            _eventPipe = eventPipe;
            _commandWriter = commandWriter;
            _eventReader = eventReader;
            _bootstrapResponsePath = bootstrapResponsePath;
            _readerTask = Task.Run(() => EventReaderLoop(_readerCts.Token));
        }

        public bool IsRunning
        {
            get
            {
                if (_disposed)
                    return false;

                try
                {
                    return !_process.HasExited;
                }
                catch
                {
                    return false;
                }
            }
        }

        public static bool TryStart(NetRole role, CSteamID hostSteamId, out SteamP2PWorkerBridge? bridge, out string error)
        {
            bridge = null;
            error = "Steam P2P worker failed to start";

            var commandPipeName = $"dccm_steam_p2p_cmd_{Guid.NewGuid():N}";
            var eventPipeName = $"dccm_steam_p2p_evt_{Guid.NewGuid():N}";
            var responsePath = Path.Combine(Path.GetTempPath(), $"dccm_steam_p2p_resp_{Guid.NewGuid():N}.json");

            NamedPipeServerStream? commandPipe = null;
            NamedPipeServerStream? eventPipe = null;
            Process? process = null;
            StreamWriter? commandWriter = null;
            StreamReader? eventReader = null;

            try
            {
                commandPipe = new NamedPipeServerStream(
                    commandPipeName,
                    PipeDirection.Out,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                eventPipe = new NamedPipeServerStream(
                    eventPipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                var startInfo = SteamConnect.BuildWorkerStartInfoForRuntime();
                startInfo.Environment[SteamP2PWorkerEnvironment.EnvWorkerMode] = SteamP2PWorkerEnvironment.WorkerModeP2P;
                startInfo.Environment[SteamP2PWorkerEnvironment.EnvRole] = role == NetRole.Host ? "host" : "client";
                startInfo.Environment[SteamP2PWorkerEnvironment.EnvHostSteamId] = hostSteamId.m_SteamID.ToString(CultureInfo.InvariantCulture);
                startInfo.Environment[SteamP2PWorkerEnvironment.EnvCommandPipe] = commandPipeName;
                startInfo.Environment[SteamP2PWorkerEnvironment.EnvEventPipe] = eventPipeName;
                startInfo.Environment[SteamP2PWorkerEnvironment.EnvBootstrapResponsePath] = responsePath;

                var assemblyPath = typeof(SteamP2PWorkerBridge).Assembly.Location;
                if (string.IsNullOrWhiteSpace(assemblyPath))
                {
                    error = "Steam P2P worker assembly path is unavailable";
                    return false;
                }

                var loadAssemblies = SteamConnect.BuildWorkerLoadAssembliesForRuntime(assemblyPath);
                process = WorkerProcessUtils.StartWorkerProcess(
                    typeof(SteamWorkerBootstrap).AssemblyQualifiedName!,
                    nameof(SteamWorkerBootstrap.WorkerEntry),
                    startInfo,
                    loadAssemblies);

                if (process == null)
                {
                    error = "Steam P2P worker process was not started";
                    return false;
                }

                var cmdConnectTask = commandPipe.WaitForConnectionAsync();
                var evtConnectTask = eventPipe.WaitForConnectionAsync();
                if (!WaitForPipeConnections(cmdConnectTask, evtConnectTask, process, StartupTimeoutMs))
                {
                    error = BuildStartupError("Steam P2P worker pipe connection timeout", process, responsePath);
                    return false;
                }

                commandWriter = new StreamWriter(commandPipe, new UTF8Encoding(false), 4096, leaveOpen: true)
                {
                    AutoFlush = true
                };
                eventReader = new StreamReader(eventPipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);

                var ready = ReadReadyEvent(eventReader, process, responsePath, StartupTimeoutMs, out var readyError);
                if (ready == null || !ready.Success)
                {
                    error = string.IsNullOrWhiteSpace(readyError)
                        ? "Steam P2P worker startup did not return ready state"
                        : readyError;
                    return false;
                }

                bridge = new SteamP2PWorkerBridge(process, commandPipe, eventPipe, commandWriter, eventReader, responsePath);
                commandPipe = null;
                eventPipe = null;
                commandWriter = null;
                eventReader = null;
                process = null;
                error = string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.ToString();
                return false;
            }
            finally
            {
                if (process != null)
                {
                    try
                    {
                        if (!process.HasExited)
                            process.Kill(true);
                    }
                    catch
                    {
                    }
                    finally
                    {
                        try { process.Dispose(); } catch { }
                    }
                }

                try { commandWriter?.Dispose(); } catch { }
                try { eventReader?.Dispose(); } catch { }
                try { commandPipe?.Dispose(); } catch { }
                try { eventPipe?.Dispose(); } catch { }

                if (bridge == null)
                {
                    try
                    {
                        if (File.Exists(responsePath))
                            File.Delete(responsePath);
                    }
                    catch
                    {
                    }
                }
            }
        }

        public bool TrySend(ulong steamId, EP2PSend sendType, int channel, byte[] payload, out string error)
        {
            error = string.Empty;

            if (_disposed || payload == null || payload.Length == 0)
            {
                error = "Steam P2P worker is not available";
                return false;
            }

            var command = new WorkerCommand
            {
                Type = WorkerCommandTypeSend,
                SteamId = steamId,
                Channel = channel,
                SendType = sendType.ToString(),
                Payload = Convert.ToBase64String(payload)
            };

            return TryWriteCommand(command, out error);
        }

        public bool TryClosePeer(ulong steamId)
        {
            var command = new WorkerCommand
            {
                Type = WorkerCommandTypeClosePeer,
                SteamId = steamId
            };

            return TryWriteCommand(command, out _);
        }

        public bool TryReadPacket(out SteamP2PWorkerPacket packet)
        {
            return _packets.TryDequeue(out packet);
        }

        public bool TryReadWarning(out string warning)
        {
            return _warnings.TryDequeue(out warning!);
        }
        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            try
            {
                TryWriteCommand(new WorkerCommand { Type = WorkerCommandTypeStop }, out _);
            }
            catch
            {
            }

            try { _readerCts.Cancel(); } catch { }
            try { _commandWriter.Dispose(); } catch { }
            try { _eventReader.Dispose(); } catch { }
            try { _commandPipe.Dispose(); } catch { }
            try { _eventPipe.Dispose(); } catch { }
            try { _readerTask.Wait(500); } catch { }

            try
            {
                if (!_process.HasExited && !_process.WaitForExit(1000))
                {
                    try { _process.Kill(true); } catch { }
                    try { _process.WaitForExit(1000); } catch { }
                }
            }
            catch
            {
            }
            finally
            {
                try { _process.Dispose(); } catch { }
            }

            try
            {
                if (File.Exists(_bootstrapResponsePath))
                    File.Delete(_bootstrapResponsePath);
            }
            catch
            {
            }
        }

        private async Task EventReaderLoop(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    string? line;
                    try
                    {
                        line = await _eventReader.ReadLineAsync().ConfigureAwait(false);
                    }
                    catch
                    {
                        break;
                    }

                    if (line == null)
                        break;
                    if (line.Length == 0)
                        continue;

                    WorkerEvent? evt = null;
                    try
                    {
                        evt = JsonConvert.DeserializeObject<WorkerEvent>(line);
                    }
                    catch
                    {
                    }

                    if (evt == null || string.IsNullOrWhiteSpace(evt.Type))
                        continue;

                    if (string.Equals(evt.Type, WorkerEventTypePacket, StringComparison.Ordinal))
                    {
                        if (string.IsNullOrWhiteSpace(evt.Payload))
                            continue;

                        byte[] bytes;
                        try
                        {
                            bytes = Convert.FromBase64String(evt.Payload);
                        }
                        catch
                        {
                            continue;
                        }

                        var payload = Encoding.UTF8.GetString(bytes);
                        _packets.Enqueue(new SteamP2PWorkerPacket(evt.SteamId, evt.Channel, payload));
                        continue;
                    }

                    if (string.Equals(evt.Type, WorkerEventTypeWarning, StringComparison.Ordinal) ||
                        string.Equals(evt.Type, WorkerEventTypeError, StringComparison.Ordinal))
                    {
                        if (!string.IsNullOrWhiteSpace(evt.Error))
                            _warnings.Enqueue(evt.Error);
                    }
                }
            }
            finally
            {
                if (!_disposed)
                {
                    string msg;
                    try
                    {
                        msg = _process.HasExited
                            ? $"Steam P2P worker exited (exit={_process.ExitCode})"
                            : "Steam P2P worker event stream ended";
                    }
                    catch
                    {
                        msg = "Steam P2P worker event stream ended";
                    }

                    _warnings.Enqueue(msg);
                }
            }
        }

        private bool TryWriteCommand(WorkerCommand command, out string error)
        {
            error = string.Empty;

            if (_disposed)
            {
                error = "Steam P2P worker bridge is disposed";
                return false;
            }

            string json;
            try
            {
                json = JsonConvert.SerializeObject(command);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }

            lock (_commandSync)
            {
                try
                {
                    _commandWriter.WriteLine(json);
                    _commandWriter.Flush();
                    return true;
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    return false;
                }
            }
        }

        private static WorkerEvent? ReadReadyEvent(StreamReader reader, Process process, string responsePath, int timeoutMs, out string error)
        {
            error = string.Empty;
            var readTask = reader.ReadLineAsync();
            var timeoutAt = Environment.TickCount64 + timeoutMs;
            while (!readTask.IsCompleted)
            {
                if (Environment.TickCount64 >= timeoutAt)
                {
                    error = BuildStartupError("Steam P2P worker startup timeout", process, responsePath);
                    return null;
                }

                try
                {
                    if (process.HasExited)
                    {
                        error = BuildStartupError("Steam P2P worker exited before ready", process, responsePath);
                        return null;
                    }
                }
                catch
                {
                }

                Thread.Sleep(20);
            }

            string? line;
            try
            {
                line = readTask.Result;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                error = "Steam P2P worker returned empty startup event";
                return null;
            }

            try
            {
                var evt = JsonConvert.DeserializeObject<WorkerEvent>(line);
                if (evt == null)
                {
                    error = "Steam P2P worker returned invalid startup event";
                    return null;
                }

                if (!string.Equals(evt.Type, WorkerEventTypeReady, StringComparison.Ordinal))
                {
                    error = "Steam P2P worker did not return ready event";
                    return null;
                }

                if (!evt.Success)
                    error = string.IsNullOrWhiteSpace(evt.Error) ? "Steam P2P worker reported startup failure" : evt.Error;
                return evt;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }
        }

        private static bool WaitForPipeConnections(Task commandTask, Task eventTask, Process process, int timeoutMs)
        {
            var timeoutAt = Environment.TickCount64 + timeoutMs;
            while (Environment.TickCount64 < timeoutAt)
            {
                if (commandTask.IsCompleted && eventTask.IsCompleted)
                    return !(commandTask.IsFaulted || eventTask.IsFaulted || commandTask.IsCanceled || eventTask.IsCanceled);

                try
                {
                    if (process.HasExited)
                        return false;
                }
                catch
                {
                    return false;
                }

                Thread.Sleep(PipeConnectPollMs);
            }

            return false;
        }

        private static string BuildStartupError(string prefix, Process process, string responsePath)
        {
            if (TryReadBootstrapError(responsePath, out var workerError) && !string.IsNullOrWhiteSpace(workerError))
                return $"{prefix}: {workerError}";

            int? exitCode = null;
            try
            {
                if (process.HasExited)
                    exitCode = process.ExitCode;
            }
            catch
            {
            }

            if (exitCode.HasValue)
                return $"{prefix} (exit={exitCode.Value})";

            return prefix;
        }

        private static bool TryReadBootstrapError(string responsePath, out string error)
        {
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(responsePath) || !File.Exists(responsePath))
                return false;

            try
            {
                var json = File.ReadAllText(responsePath);
                if (string.IsNullOrWhiteSpace(json))
                    return false;

                var parsed = JsonConvert.DeserializeObject<BootstrapResponse>(json);
                if (parsed == null)
                    return false;

                error = parsed.Error ?? string.Empty;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private sealed class BootstrapResponse
        {
            public bool Success { get; set; }
            public string Error { get; set; } = string.Empty;
        }

        private const string WorkerCommandTypeSend = "send";
        private const string WorkerCommandTypeClosePeer = "closePeer";
        private const string WorkerCommandTypeStop = "stop";
        private const string WorkerEventTypeReady = "ready";
        private const string WorkerEventTypePacket = "packet";
        private const string WorkerEventTypeWarning = "warn";
        private const string WorkerEventTypeError = "error";

        private sealed class WorkerCommand
        {
            public string Type { get; set; } = string.Empty;
            public ulong SteamId { get; set; }
            public int Channel { get; set; }
            public string SendType { get; set; } = string.Empty;
            public string Payload { get; set; } = string.Empty;
        }

        private sealed class WorkerEvent
        {
            public string Type { get; set; } = string.Empty;
            public bool Success { get; set; }
            public string Error { get; set; } = string.Empty;
            public ulong SteamId { get; set; }
            public int Channel { get; set; }
            public string Payload { get; set; } = string.Empty;
        }
    }

    internal static class SteamP2PWorker
    {
        private const int DeadCellsAppId = 588650;
        private const int PipeConnectTimeoutMs = 15000;
        private const int WorkerTickSleepMs = 5;
        private const int SteamP2PChannelClientToHost = 0;
        private const int SteamP2PChannelHostToClient = 1;
        private const uint SteamMaxPacketSizeBytes = 16u * 1024u * 1024u;
        private const int SteamMinReceiveBufferBytes = 64 * 1024;

        private const string WorkerCommandTypeSend = "send";
        private const string WorkerCommandTypeClosePeer = "closePeer";
        private const string WorkerCommandTypeStop = "stop";
        private const string WorkerEventTypeReady = "ready";
        private const string WorkerEventTypePacket = "packet";
        private const string WorkerEventTypeWarning = "warn";
        private const string WorkerEventTypeError = "error";

        public static void WorkerEntry()
        {
            var bootstrapResponsePath = Environment.GetEnvironmentVariable(SteamP2PWorkerEnvironment.EnvBootstrapResponsePath);

            try
            {
                var roleText = Environment.GetEnvironmentVariable(SteamP2PWorkerEnvironment.EnvRole) ?? string.Empty;
                var commandPipeName = Environment.GetEnvironmentVariable(SteamP2PWorkerEnvironment.EnvCommandPipe) ?? string.Empty;
                var eventPipeName = Environment.GetEnvironmentVariable(SteamP2PWorkerEnvironment.EnvEventPipe) ?? string.Empty;
                var hostSteamIdRaw = Environment.GetEnvironmentVariable(SteamP2PWorkerEnvironment.EnvHostSteamId) ?? "0";

                if (string.IsNullOrWhiteSpace(commandPipeName) || string.IsNullOrWhiteSpace(eventPipeName))
                    throw new InvalidOperationException("Steam P2P worker pipe names are missing");

                if (!ulong.TryParse(hostSteamIdRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var hostSteamId))
                    hostSteamId = 0UL;

                SteamConnect.PrepareSteamNativePathForRuntime();
                if (SteamAPI.RestartAppIfNecessary(new AppId_t(DeadCellsAppId)))
                    throw new InvalidOperationException("Steam requested app restart");

                if (!SteamAPI.Init())
                    throw new InvalidOperationException("Steam API init failed in Steam P2P worker");

                try
                {
                    using var commandPipe = new NamedPipeClientStream(".", commandPipeName, PipeDirection.In, PipeOptions.Asynchronous);
                    using var eventPipe = new NamedPipeClientStream(".", eventPipeName, PipeDirection.Out, PipeOptions.Asynchronous);
                    commandPipe.Connect(PipeConnectTimeoutMs);
                    eventPipe.Connect(PipeConnectTimeoutMs);

                    using var commandReader = new StreamReader(commandPipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);
                    using var eventWriter = new StreamWriter(eventPipe, new UTF8Encoding(false), 4096, leaveOpen: true)
                    {
                        AutoFlush = true
                    };

                    var commands = new ConcurrentQueue<WorkerCommand>();
                    var commandDone = 0;
                    var commandThread = new Thread(() => ReadCommands(commandReader, commands, ref commandDone))
                    {
                        IsBackground = true,
                        Name = "DCCM-SteamP2P-CommandReader"
                    };
                    commandThread.Start();

                    var localSteamId = 0UL;
                    try { localSteamId = SteamUser.GetSteamID().m_SteamID; } catch { }

                    WriteEvent(eventWriter, new WorkerEvent
                    {
                        Type = WorkerEventTypeReady,
                        Success = true,
                        Error = string.Empty,
                        SteamId = localSteamId
                    });

                    if (!string.IsNullOrWhiteSpace(bootstrapResponsePath))
                        TryWriteBootstrapResponse(bootstrapResponsePath, true, string.Empty);

                    var receiveBuffer = new byte[SteamMinReceiveBufferBytes];
                    var running = true;
                    while (running)
                    {
                        var hadWork = false;

                        while (commands.TryDequeue(out var command))
                        {
                            hadWork = true;
                            if (!HandleCommand(command, eventWriter, roleText, hostSteamId, ref running))
                                continue;
                        }

                        if (DrainIncomingPackets(eventWriter, ref receiveBuffer))
                            hadWork = true;

                        try
                        {
                            SteamAPI.RunCallbacks();
                        }
                        catch (Exception ex)
                        {
                            WriteEvent(eventWriter, new WorkerEvent
                            {
                                Type = WorkerEventTypeWarning,
                                Error = $"Steam callbacks error: {ex.Message}"
                            });
                        }

                        if (!hadWork)
                            Thread.Sleep(WorkerTickSleepMs);

                        if (Volatile.Read(ref commandDone) == 1 && commands.IsEmpty)
                            running = false;
                    }
                }
                finally
                {
                    try { SteamAPI.Shutdown(); } catch { }
                }
            }
            catch (Exception ex)
            {
                if (!string.IsNullOrWhiteSpace(bootstrapResponsePath))
                    TryWriteBootstrapResponse(bootstrapResponsePath, false, ex.ToString());

                Environment.Exit(1);
            }

            Environment.Exit(0);
        }

        private static void ReadCommands(StreamReader reader, ConcurrentQueue<WorkerCommand> commands, ref int doneFlag)
        {
            try
            {
                while (true)
                {
                    string? line;
                    try
                    {
                        line = reader.ReadLine();
                    }
                    catch
                    {
                        break;
                    }

                    if (line == null)
                        break;
                    if (line.Length == 0)
                        continue;

                    WorkerCommand? command = null;
                    try
                    {
                        command = JsonConvert.DeserializeObject<WorkerCommand>(line);
                    }
                    catch
                    {
                    }

                    if (command != null)
                        commands.Enqueue(command);
                }
            }
            finally
            {
                Volatile.Write(ref doneFlag, 1);
            }
        }

        private static bool HandleCommand(
            WorkerCommand command,
            StreamWriter eventWriter,
            string roleText,
            ulong hostSteamId,
            ref bool running)
        {
            if (command == null || string.IsNullOrWhiteSpace(command.Type))
                return false;

            if (string.Equals(command.Type, WorkerCommandTypeStop, StringComparison.Ordinal))
            {
                running = false;
                return true;
            }

            if (string.Equals(command.Type, WorkerCommandTypeClosePeer, StringComparison.Ordinal))
            {
                if (command.SteamId != 0UL)
                {
                    try { SteamNetworking.CloseP2PSessionWithUser(new CSteamID(command.SteamId)); } catch { }
                }
                return true;
            }

            if (!string.Equals(command.Type, WorkerCommandTypeSend, StringComparison.Ordinal))
                return false;

            if (command.SteamId == 0UL || string.IsNullOrWhiteSpace(command.Payload))
                return false;

            byte[] payloadBytes;
            try
            {
                payloadBytes = Convert.FromBase64String(command.Payload);
            }
            catch
            {
                return false;
            }

            if (!Enum.TryParse(command.SendType, out EP2PSend sendType))
                sendType = EP2PSend.k_EP2PSendReliable;

            var targetSteamId = command.SteamId;
            if (string.Equals(roleText, "client", StringComparison.OrdinalIgnoreCase) &&
                hostSteamId != 0UL &&
                targetSteamId != hostSteamId)
            {
                WriteEvent(eventWriter, new WorkerEvent
                {
                    Type = WorkerEventTypeWarning,
                    Error = $"Steam client attempted send to unexpected peer {targetSteamId}"
                });
                return true;
            }

            bool sent;
            try
            {
                sent = SteamNetworking.SendP2PPacket(
                    new CSteamID(targetSteamId),
                    payloadBytes,
                    (uint)payloadBytes.Length,
                    sendType,
                    command.Channel);
            }
            catch (Exception ex)
            {
                WriteEvent(eventWriter, new WorkerEvent
                {
                    Type = WorkerEventTypeWarning,
                    Error = $"Steam send error: {ex.Message}"
                });
                return true;
            }

            if (!sent)
            {
                WriteEvent(eventWriter, new WorkerEvent
                {
                    Type = WorkerEventTypeWarning,
                    Error = $"Steam send failed to {targetSteamId} ({sendType}, ch={command.Channel})"
                });
            }

            return true;
        }

        private static bool DrainIncomingPackets(StreamWriter eventWriter, ref byte[] receiveBuffer)
        {
            var hadPackets = false;

            for (var channel = SteamP2PChannelClientToHost; channel <= SteamP2PChannelHostToClient; channel++)
            {
                try
                {
                    while (SteamNetworking.IsP2PPacketAvailable(out var packetSize, channel))
                    {
                        hadPackets = true;
                        if (packetSize == 0 || packetSize > SteamMaxPacketSizeBytes || packetSize > int.MaxValue)
                            continue;

                        var required = (int)Math.Max(packetSize, SteamMinReceiveBufferBytes);
                        if (receiveBuffer.Length < required)
                            Array.Resize(ref receiveBuffer, required);

                        if (!SteamNetworking.ReadP2PPacket(
                                receiveBuffer,
                                (uint)receiveBuffer.Length,
                                out var bytesRead,
                                out var remoteSteamId,
                                channel))
                        {
                            break;
                        }

                        if (bytesRead == 0 || bytesRead > receiveBuffer.Length)
                            continue;

                        try { SteamNetworking.AcceptP2PSessionWithUser(remoteSteamId); } catch { }

                        var payloadBase64 = Convert.ToBase64String(receiveBuffer, 0, (int)bytesRead);
                        WriteEvent(eventWriter, new WorkerEvent
                        {
                            Type = WorkerEventTypePacket,
                            SteamId = remoteSteamId.m_SteamID,
                            Channel = channel,
                            Payload = payloadBase64
                        });
                    }
                }
                catch (Exception ex)
                {
                    WriteEvent(eventWriter, new WorkerEvent
                    {
                        Type = WorkerEventTypeWarning,
                        Error = $"Steam packet read error: {ex.Message}"
                    });
                }
            }

            return hadPackets;
        }

        private static void WriteEvent(StreamWriter writer, WorkerEvent evt)
        {
            try
            {
                var json = JsonConvert.SerializeObject(evt);
                writer.WriteLine(json);
                writer.Flush();
            }
            catch
            {
            }
        }

        private static void TryWriteBootstrapResponse(string responsePath, bool success, string error)
        {
            try
            {
                var payload = new
                {
                    Success = success,
                    Error = error ?? string.Empty,
                    LobbyId = 0UL,
                    HostIp = string.Empty,
                    HostPort = 0,
                    PersonaName = string.Empty
                };
                File.WriteAllText(responsePath, JsonConvert.SerializeObject(payload));
            }
            catch
            {
            }
        }

        private sealed class WorkerCommand
        {
            public string Type { get; set; } = string.Empty;
            public ulong SteamId { get; set; }
            public int Channel { get; set; }
            public string SendType { get; set; } = string.Empty;
            public string Payload { get; set; } = string.Empty;
        }

        private sealed class WorkerEvent
        {
            public string Type { get; set; } = string.Empty;
            public bool Success { get; set; }
            public string Error { get; set; } = string.Empty;
            public ulong SteamId { get; set; }
            public int Channel { get; set; }
            public string Payload { get; set; } = string.Empty;
        }
    }
}
