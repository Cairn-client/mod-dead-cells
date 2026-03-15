using System;
using System.Diagnostics;
using dc.en;
using dc.tool.mainSkills;
using DeadCellsMultiplayerMod.Ghost;
using DeadCellsMultiplayerMod.Ghost.GhostBase;

namespace DeadCellsMultiplayerMod;

public partial class ModEntry
{
    private const string RemoteDiveAttackKind = "__DIVE_ATTACK__";
    private const int RemoteDiveAttackSlot = -1;
    private const double LocalDiveStartRepeatBlockSeconds = 0.04;
    private const double LocalDiveLandRepeatBlockSeconds = 0.04;

    private long _lastLocalDiveStartSendTicks;
    private long _lastLocalDiveLandSendTicks;

    private void Hook_DiveAttack_onStart(Hook_DiveAttack.orig_onStart orig, DiveAttack self)
    {
        orig(self);
        NotifyLocalDiveAttackStartedFromHooks(self);
    }

    private void Hook_DiveAttack_onOwnerLand(Hook_DiveAttack.orig_onOwnerLand orig, DiveAttack self, double high)
    {
        var wasDiving = IsDiveReallyActive(self);
        orig(self, high);
        NotifyLocalDiveAttackLandedFromHooks(self, high, wasDiving);
    }

    private void NotifyLocalDiveAttackStartedFromHooks(DiveAttack? self)
    {
        if (_netRole == NetRole.None || self == null || me == null)
            return;
        if (KingWeaponSupport.IsInKingContext)
            return;

        var net = _net;
        if (net == null || !net.IsAlive)
            return;

        Hero? owner;
        try { owner = self.hero; } catch { return; }
        if (owner == null || !ReferenceEquals(owner, me))
            return;

        var isDiving = IsDiveReallyActive(self);
        if (!isDiving)
            return;

        if (IsLocalDiveRepeat(ref _lastLocalDiveStartSendTicks, LocalDiveStartRepeatBlockSeconds))
            return;

        net.SendAttack(
            RemoteDiveAttackKind,
            RemoteDiveAttackSlot,
            isDiving ? 1 : 0,
            null,
            RemoteAttackAction.Attack);
    }

    private void NotifyLocalDiveAttackLandedFromHooks(DiveAttack? self, double high, bool wasDiving)
    {
        if (_netRole == NetRole.None || self == null || me == null)
            return;
        if (KingWeaponSupport.IsInKingContext)
            return;

        var net = _net;
        if (net == null || !net.IsAlive)
            return;

        Hero? owner;
        try { owner = self.hero; } catch { return; }
        if (owner == null || !ReferenceEquals(owner, me))
            return;
        if (!wasDiving)
            return;

        if (IsLocalDiveRepeat(ref _lastLocalDiveLandSendTicks, LocalDiveLandRepeatBlockSeconds))
            return;

        net.SendAttack(
            RemoteDiveAttackKind,
            RemoteDiveAttackSlot,
            1,
            EncodeRemoteDiveHeight(high),
            RemoteAttackAction.Interrupt);
    }

    private static bool IsDiveReallyActive(DiveAttack? self)
    {
        if (self == null)
            return false;

        try
        {
            if (self.isDiving())
                return true;
        }
        catch
        {
        }

        try
        {
            return self.isActive();
        }
        catch
        {
            return false;
        }
    }

    private static bool IsLocalDiveRepeat(ref long lastTicks, double minSeconds)
    {
        var now = Stopwatch.GetTimestamp();
        var minTicks = (long)(Stopwatch.Frequency * minSeconds);
        if (lastTicks != 0 && now - lastTicks < minTicks)
            return true;
        lastTicks = now;
        return false;
    }

    private static int EncodeRemoteDiveHeight(double high)
    {
        if (double.IsNaN(high) || double.IsInfinity(high))
            return 1000;

        if (high < -1000.0)
            high = -1000.0;
        else if (high > 1000.0)
            high = 1000.0;

        return (int)Math.Round(high * 1000.0, MidpointRounding.AwayFromZero);
    }

    private static double DecodeRemoteDiveHeight(int? encoded)
    {
        if (!encoded.HasValue)
            return 1.0;

        return encoded.Value / 1000.0;
    }

    private static bool IsRemoteDiveAttackKind(string? kindId)
    {
        if (string.IsNullOrWhiteSpace(kindId))
            return false;

        return string.Equals(kindId, RemoteDiveAttackKind, StringComparison.Ordinal);
    }

    private bool TryHandleRemoteDiveAttack(NetNode.RemoteAttack attack, int localId)
    {
        if (!IsRemoteDiveAttackKind(attack.Kind))
            return false;

        var remoteIsDiving = attack.PermanentId != 0;
        if (!remoteIsDiving)
            return true;

        if (!TryGetClientIndex(localId, attack.Id, out var index))
            return true;

        var client = clients[index];
        if (client == null)
            return true;

        try
        {
            if (attack.Action == RemoteAttackAction.Interrupt)
                client.TriggerRemoteDiveAttackLand(DecodeRemoteDiveHeight(attack.Ammo));
            else
                client.TriggerRemoteDiveAttackStart();
        }
        catch (Exception ex)
        {
            Logger.Warning(
                "[NetMod] Remote dive replay failed remoteId={RemoteId}: {Message}",
                attack.Id,
                ex.Message);
        }

        return true;
    }
}
