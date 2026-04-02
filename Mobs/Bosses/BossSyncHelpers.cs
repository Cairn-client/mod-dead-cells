using dc.en;
using dc.en.mob;
using DeadCellsMultiplayerMod;

namespace DeadCellsMultiplayerMod.Mobs.Bosses;

public static class BossSyncHelpers
{
    public static bool IsBossMob(Mob mob)
    {
        if (mob == null)
            return false;

        try
        {
            var runtimeType = mob.GetType();
            var typeName = runtimeType?.FullName ?? runtimeType?.ToString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(typeName))
                typeName = mob.GetType().ToString();

            return typeName.Contains("dc.en.mob.boss.", StringComparison.OrdinalIgnoreCase) ||
                   typeName.Contains(".mob.boss.", StringComparison.OrdinalIgnoreCase) ||
                   typeName.Contains(".boss.", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public static double GetHpMultiplierForMob(Mob mob, int playerCount)
    {
        if (mob == null || playerCount <= 1)
            return 1;

        var baseMultiplier = IsBossMob(mob)
            ? 1 + (playerCount - 1) * BossSyncConstants.BossHpMultiplierPerPlayer
            : 1 + (playerCount - 1) * BossSyncConstants.RegularMobHpMultiplierPerPlayer;

        var userMultiplier = IsBossMob(mob)
            ? MultiplayerSettingsStorage.BossesHpMultiplier
            : MultiplayerSettingsStorage.MobsHpMultiplier;

        if (double.IsNaN(userMultiplier) || double.IsInfinity(userMultiplier) || userMultiplier <= 0)
            userMultiplier = 1;

        return baseMultiplier * userMultiplier;
    }
}
