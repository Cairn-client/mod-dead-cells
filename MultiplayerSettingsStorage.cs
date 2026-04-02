using ModCore.Storage;

namespace DeadCellsMultiplayerMod;

public sealed class MultiplayerSettingsData
{
    public bool EnableMobsSync { get; set; } = true;

    public double MobsInterpolationQuality { get; set; } = 0.62;

    public double MobsHpMultiplier { get; set; } = 1.0;

    public double BossesHpMultiplier { get; set; } = 1.0;

    public bool SyncVerticalPosition { get; set; } = false;
}

public static class MultiplayerSettingsStorage
{
    private const string ConfigName = "DeadCellsMultiplayerMod.MultiplayerSettings";
    private const double InterpolationMin = 0.20;
    private const double InterpolationMax = 1.00;
    private const double HpMultiplierMin = 0.25;
    private const double HpMultiplierMax = 8.00;

    private static readonly object SyncRoot = new();
    private static readonly Config<MultiplayerSettingsData> Config = new(ConfigName);

    public static bool EnableMobsSync
    {
        get
        {
            lock (SyncRoot)
                return EnsureDataNormalizedUnsafe().EnableMobsSync;
        }
        set
        {
            lock (SyncRoot)
            {
                var data = EnsureDataNormalizedUnsafe();
                if (data.EnableMobsSync == value)
                    return;

                data.EnableMobsSync = value;
                SaveUnsafe();
            }
        }
    }

    public static double MobsInterpolationQuality
    {
        get
        {
            lock (SyncRoot)
                return EnsureDataNormalizedUnsafe().MobsInterpolationQuality;
        }
        set
        {
            lock (SyncRoot)
            {
                var clamped = Clamp(value, InterpolationMin, InterpolationMax);
                var data = EnsureDataNormalizedUnsafe();
                if (Approximately(data.MobsInterpolationQuality, clamped))
                    return;

                data.MobsInterpolationQuality = clamped;
                SaveUnsafe();
            }
        }
    }

    public static double MobsHpMultiplier
    {
        get
        {
            lock (SyncRoot)
                return EnsureDataNormalizedUnsafe().MobsHpMultiplier;
        }
        set
        {
            lock (SyncRoot)
            {
                var clamped = Clamp(value, HpMultiplierMin, HpMultiplierMax);
                var data = EnsureDataNormalizedUnsafe();
                if (Approximately(data.MobsHpMultiplier, clamped))
                    return;

                data.MobsHpMultiplier = clamped;
                SaveUnsafe();
            }
        }
    }

    public static double BossesHpMultiplier
    {
        get
        {
            lock (SyncRoot)
                return EnsureDataNormalizedUnsafe().BossesHpMultiplier;
        }
        set
        {
            lock (SyncRoot)
            {
                var clamped = Clamp(value, HpMultiplierMin, HpMultiplierMax);
                var data = EnsureDataNormalizedUnsafe();
                if (Approximately(data.BossesHpMultiplier, clamped))
                    return;

                data.BossesHpMultiplier = clamped;
                SaveUnsafe();
            }
        }
    }

    public static bool SyncVerticalPosition
    {
        get
        {
            lock (SyncRoot)
                return EnsureDataNormalizedUnsafe().SyncVerticalPosition;
        }
        set
        {
            lock (SyncRoot)
            {
                var data = EnsureDataNormalizedUnsafe();
                if (data.SyncVerticalPosition == value)
                    return;

                data.SyncVerticalPosition = value;
                SaveUnsafe();
            }
        }
    }

    public static void Save()
    {
        lock (SyncRoot)
            SaveUnsafe();
    }

    private static MultiplayerSettingsData EnsureDataNormalizedUnsafe()
    {
        var data = Config.Value ?? new MultiplayerSettingsData();
        bool changed = false;

        var interpolation = Clamp(data.MobsInterpolationQuality, InterpolationMin, InterpolationMax);
        if (!Approximately(data.MobsInterpolationQuality, interpolation))
        {
            data.MobsInterpolationQuality = interpolation;
            changed = true;
        }

        var mobsHp = Clamp(data.MobsHpMultiplier, HpMultiplierMin, HpMultiplierMax);
        if (!Approximately(data.MobsHpMultiplier, mobsHp))
        {
            data.MobsHpMultiplier = mobsHp;
            changed = true;
        }

        var bossesHp = Clamp(data.BossesHpMultiplier, HpMultiplierMin, HpMultiplierMax);
        if (!Approximately(data.BossesHpMultiplier, bossesHp))
        {
            data.BossesHpMultiplier = bossesHp;
            changed = true;
        }

        if (!ReferenceEquals(Config.Value, data))
        {
            Config.Value = data;
            changed = true;
        }

        if (changed)
            SaveUnsafe();

        return data;
    }

    private static void SaveUnsafe()
    {
        Config.Save();
    }

    private static bool Approximately(double left, double right)
    {
        return System.Math.Abs(left - right) <= 0.0001;
    }

    private static double Clamp(double value, double min, double max)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            return min;
        if (value < min)
            return min;
        if (value > max)
            return max;
        return value;
    }
}
