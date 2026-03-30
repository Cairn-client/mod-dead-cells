using System.Globalization;
using dc.en;
using dc.en.mob;
using dc.en.mob.boss;

namespace DeadCellsMultiplayerMod.Mobs.Bosses;

public static class BossStateSync
{
    private const string PhasePrefix = "bp:";
    private const string ActionPrefix = "ba:";

    public static string AppendBossState(string basePayload, Mob mob)
    {
        if (mob == null)
            return basePayload ?? string.Empty;

        if (!BossSyncHelpers.IsBossMob(mob))
            return basePayload ?? string.Empty;

        var parts = new List<string>();
        if (!string.IsNullOrEmpty(basePayload))
            parts.Add(basePayload);

        if (mob is GardenerBoss gardener)
        {
            try
            {
                var phase = gardener.phase;
                parts.Add(PhasePrefix + phase.ToString(CultureInfo.InvariantCulture));

                var action = gardener.action;
                if (action != null)
                {
                    var idx = (int)action.Index;
                    parts.Add(ActionPrefix + idx.ToString(CultureInfo.InvariantCulture));
                }
            }
            catch
            {
                // ignore
            }
        }
        else if (mob is Collector collector)
        {
            try
            {
                var phase = collector.phase;
                parts.Add(PhasePrefix + phase.ToString(CultureInfo.InvariantCulture));
            }
            catch
            {
                // ignore
            }
        }

        return parts.Count == 0 ? (basePayload ?? string.Empty) : string.Join(".", parts);
    }

    public static void ApplyBossStateFromPayload(Mob mob, string? payload)
    {
        if (mob == null || mob.destroyed || string.IsNullOrWhiteSpace(payload))
            return;

        int? phaseVal = null;
        int? actionVal = null;

        var parts = payload.Split('.', StringSplitOptions.RemoveEmptyEntries);
        foreach (var token in parts)
        {
            var t = token?.Trim();
            if (string.IsNullOrEmpty(t))
                continue;

            if (t.StartsWith(PhasePrefix, StringComparison.OrdinalIgnoreCase))
            {
                var s = t[PhasePrefix.Length..].Trim();
                if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var p))
                    phaseVal = p;
            }
            else if (t.StartsWith(ActionPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var s = t[ActionPrefix.Length..].Trim();
                if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var a))
                    actionVal = a;
            }
        }

        if (mob is GardenerBoss gardener)
        {
            try
            {
                if (phaseVal.HasValue)
                {
                    var currentPhase = gardener.phase;
                    if (currentPhase != phaseVal.Value)
                        gardener.phase = phaseVal.Value;
                }

                if (actionVal.HasValue)
                {
                    var currentActionIndex = TryGetBossActionIndex(gardener.action);
                    if (!currentActionIndex.HasValue || currentActionIndex.Value != actionVal.Value)
                    {
                        var action = CreateBossActionByIndex(actionVal.Value);
                        if (action != null)
                            gardener.action = action;
                    }
                }
            }
            catch
            {
                // ignore
            }
        }
        else if (mob is Collector collector && phaseVal.HasValue)
        {
            try
            {
                var currentPhase = collector.phase;
                if (currentPhase != phaseVal.Value)
                    collector.phase = phaseVal.Value;
            }
            catch
            {
                // ignore
            }
        }
    }

    private static int? TryGetBossActionIndex(BossAction? action)
    {
        if (action == null)
            return null;

        try
        {
            return (int)action.Index;
        }
        catch
        {
            return null;
        }
    }

    private static BossAction? CreateBossActionByIndex(int index)
    {
        return index switch
        {
            (int)BossAction.Indexes.Idle => new BossAction.Idle(),
            (int)BossAction.Indexes.Run => new BossAction.Run(),
            (int)BossAction.Indexes.Walk => new BossAction.Walk(),
            (int)BossAction.Indexes.Fall => new BossAction.Fall(),
            (int)BossAction.Indexes.Attack => new BossAction.Attack(),
            (int)BossAction.Indexes.Hoe => new BossAction.Hoe(),
            (int)BossAction.Indexes.PitchFork => new BossAction.PitchFork(),
            (int)BossAction.Indexes.Sickles => new BossAction.Sickles(),
            (int)BossAction.Indexes.SicklesStun => new BossAction.SicklesStun(),
            (int)BossAction.Indexes.Shovel => new BossAction.Shovel(),
            (int)BossAction.Indexes.ShovelAtk => new BossAction.ShovelAtk(),
            (int)BossAction.Indexes.ShovelUp => new BossAction.ShovelUp(),
            (int)BossAction.Indexes.ShovelAppear => new BossAction.ShovelAppear(),
            (int)BossAction.Indexes.ShovelDisappear => new BossAction.ShovelDisappear(),
            (int)BossAction.Indexes.Vine => new BossAction.Vine(),
            (int)BossAction.Indexes.Spore => new BossAction.Spore(),
            (int)BossAction.Indexes.JumpLoad => new BossAction.JumpLoad(),
            (int)BossAction.Indexes.Jump => new BossAction.Jump(),
            (int)BossAction.Indexes.Land => new BossAction.Land(),
            (int)BossAction.Indexes.Dashing => new BossAction.Dashing(),
            (int)BossAction.Indexes.DigUp => new BossAction.DigUp(),
            (int)BossAction.Indexes.DigDown => new BossAction.DigDown(),
            (int)BossAction.Indexes.Stun => new BossAction.Stun(),
            _ => null
        };
    }
}
