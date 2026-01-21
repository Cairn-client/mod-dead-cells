
using dc;
using dc.hl.types;
using Hashlink.Virtuals;
using dc.level;
using HaxeProxy.Runtime;
using dc.tool;
using Newtonsoft.Json;
using System.Collections;
using System.Collections.Generic;
using System.Data.SqlTypes;
using System.Globalization;
using System.Reflection;
using Serilog.Core;


namespace DeadCellsMultiplayerMod
{
    internal class GameDataSync
    {
        static Serilog.ILogger _log;


        static public int Seed;

        static public virtual_baseLootLevel_biome_bonusTripleScrollAfterBC_cellBonus_dlc_doubleUps_eliteRoomChance_eliteWanderChance_flagsProps_group_icon_id_index_loreDescriptions_mapDepth_minGold_mobDensity_mobs_name_nextLevels_parallax_props_quarterUpsBC3_quarterUpsBC4_specificLoots_specificSubBiome_transitionTo_tripleUps_worldDepth_ _isTwitch;
        static public bool _isCustom;
        static public bool _mode;

        static public LaunchMode _launch;
        private static readonly object _gameDataLock = new();
        private static GameData? _remoteGameData;
        private static readonly object _hostGameDataLock = new();
        private static string? _hostGameDataJson;
        public GameDataSync(Serilog.ILogger log)
        {
            _log = log;
        }


        

        public static void user_hook_new_game(Hook_User.orig_newGame orig,
        User self,
        int lvl,
        virtual_baseLootLevel_biome_bonusTripleScrollAfterBC_cellBonus_dlc_doubleUps_eliteRoomChance_eliteWanderChance_flagsProps_group_icon_id_index_loreDescriptions_mapDepth_minGold_mobDensity_mobs_name_nextLevels_parallax_props_quarterUpsBC3_quarterUpsBC4_specificLoots_specificSubBiome_transitionTo_tripleUps_worldDepth_ isTwitch,
        bool isCustom,
        bool mode,
        LaunchMode gdata)
        {
            isCustom = false;
            mode = false;

            Seed = lvl;
            ModEntry.me = null;
            ModEntry.ResetClientSlots();
            ModEntry.kingInitialized = false;
            ModEntry._ghost = null;
            var net = GameMenu.NetRef;
            
            if (net != null && net.IsHost)
            {
                Seed = GameMenu.ForceGenerateServerSeed("NewGame_hook");
                SendGameData(self, net);
                net.SendSeed(Seed);
            }
            else if (net != null)
            {
                if (GameMenu.TryGetRemoteSeed(out var remoteSeed))
                {
                    Seed = remoteSeed;
                }
                if (TryGetRemoteGameData(out GameData? data))
                {
                    self.bossRuneActivated = data.bossRune;
                    // self.mainGame.serverStats.forge = data.forge;
                    if (data.hasMods.HasValue)
                        self.mainGame.serverStats.hasMods = data.hasMods.Value;
                    // self.mainGame.serverStats.history = data.history;
                    self.mainGame.serverStats.isCustom = data.isCustom;
                    // self.mainGame.serverStats.meta = data.meta;

                }
                else
                {
                    _log?.Warning("[NetMod] Remote game data not received yet");
                }
            }
            lvl = Seed;
            _isTwitch = isTwitch;
            _isCustom = isCustom;
            _mode = mode;
            _launch = gdata;
            self.pickDeathItem();
            SendHeroSkin(self, net);
            orig(self, lvl, isTwitch, isCustom, mode, gdata);
        }

        public static ArrayObj hook_generate(Hook_LevelGen.orig_generate orig,
        LevelGen self,
        User seed,
        int ldat,
        virtual_baseLootLevel_biome_bonusTripleScrollAfterBC_cellBonus_dlc_doubleUps_eliteRoomChance_eliteWanderChance_flagsProps_group_icon_id_index_loreDescriptions_mapDepth_minGold_mobDensity_mobs_name_nextLevels_parallax_props_quarterUpsBC3_quarterUpsBC4_specificLoots_specificSubBiome_transitionTo_tripleUps_worldDepth_ resetCount,
        Ref<bool> resetCount2)
        {
            // ldat = Seed;
            ModEntry.ResetClientSlots();
            // var net = GameMenu.NetRef;

            // SendHeroSkin(seed, net);
            return orig(self, seed, ldat, resetCount, resetCount2);
        }

        public static void SendGameData(User self, NetNode? net)
        {
            try
            {
                var payload = BuildGameDataPayload(self);
                if (payload == null)
                    return;

                var json = JsonConvert.SerializeObject(payload);
                lock (_hostGameDataLock)
                {
                    _hostGameDataJson = json;
                }

                if (net == null || !net.IsAlive)
                    return;

                net.SendGameData(json);
            }
            catch (Exception ex)
            {
                _log?.Warning("[NetMod] Failed to send game data: {Message}", ex.Message);
            }
        }

        public static void ReceiveGameData(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return;

            try
            {
                var parsed = JsonConvert.DeserializeObject<GameData>(json);
                if (parsed == null)
                    return;

                lock (_gameDataLock)
                {
                    _remoteGameData = parsed;
                }
                _log?.Information("[NetMod] Received game data");
            }
            catch (Exception ex)
            {
                _log?.Warning("[NetMod] Failed to receive game data: {Message}", ex.Message);
            }
        }

        public static bool TryGetHostGameData(out string? json)
        {
            lock (_hostGameDataLock)
            {
                json = _hostGameDataJson;
                return !string.IsNullOrWhiteSpace(json);
            }
        }


        public static bool TryGetRemoteGameData(out GameData? data)
        {
            lock (_gameDataLock)
            {
                data = _remoteGameData;
                return data != null;
            }
        }

        public static void ReceiveHeroSkin(string skin)
        {
            try
            {
                var cleaned = CleanSkin(skin);
                if (string.IsNullOrWhiteSpace(cleaned))
                    cleaned = "PrisonerDefault";

                ModEntry.SetRemoteSkin(cleaned);
            }
            catch (Exception ex)
            {
                _log?.Warning("[NetMod] Failed to receive hero skin: {Message}", ex.Message);
            }
        }

        private static void SendHeroSkin(User user, NetNode? net)
        {
            if (net == null || !net.IsAlive)
                return;

            try
            {
                var skin = CleanSkin(user?.heroSkin?.ToString());
                if (string.IsNullOrWhiteSpace(skin))
                    skin = "PrisonerDefault";

                net.SendHeroSkin(skin);
            }
            catch (Exception ex)
            {
                _log?.Warning("[NetMod] Failed to send hero skin: {Message}", ex.Message);
            }
        }

        public sealed class GameData
        {
            public int bossRune;
            public List<double>? forge;
            public List<HistoryEntryPayload>? history;
            public List<string>? meta;
            public bool? hasMods;
            public bool isCustom;
        }

        public sealed class HistoryEntryPayload
        {
            public int brut;
            public int cellsEarned;
            public string? level;
            public int surv;
            public int tact;
            public double time;
        }

        private static GameData? BuildGameDataPayload(User self)
        {
            if (self == null)
                return null;

            var stats = self.mainGame?.serverStats;
            if (stats == null)
                return null;

            return new GameData
            {
                bossRune = ToInt(self.bossRuneActivated),
                forge = ToDoubleList(stats.forge),
                history = ToHistoryList(stats.history),
                meta = ToStringList(stats.meta),
                hasMods = ToNullableBool(stats.hasMods),
                isCustom = ToBool(stats.isCustom, false)
            };
        }

        private static string CleanSkin(string? skin)
        {
            if (string.IsNullOrEmpty(skin))
                return string.Empty;

            return skin.Replace("|", "/").Replace("\r", string.Empty).Replace("\n", string.Empty);
        }

        private static List<double>? ToDoubleList(object? value)
        {
            var items = EnumerateUnknown(value);
            if (items == null)
                return null;

            var list = new List<double>();
            foreach (var item in items)
            {
                if (item == null)
                    continue;
                if (item is double d)
                    list.Add(d);
                else if (item is float f)
                    list.Add(f);
                else if (item is IConvertible conv)
                {
                    try
                    {
                        list.Add(conv.ToDouble(CultureInfo.InvariantCulture));
                    }
                    catch { }
                }
            }

            return list.Count > 0 ? list : null;
        }

        private static List<string>? ToStringList(object? value)
        {
            var items = EnumerateUnknown(value);
            if (items == null)
                return null;

            var list = new List<string>();
            foreach (var item in items)
            {
                if (item == null)
                    continue;
                var text = item.ToString();
                if (string.IsNullOrWhiteSpace(text))
                    continue;
                list.Add(text);
            }

            return list.Count > 0 ? list : null;
        }

        private static List<HistoryEntryPayload>? ToHistoryList(object? value)
        {
            var items = EnumerateUnknown(value);
            if (items == null)
                return null;

            var list = new List<HistoryEntryPayload>();
            foreach (var item in items)
            {
                if (item == null)
                    continue;

                var entry = new HistoryEntryPayload
                {
                    brut = ToInt(GetMemberValue(item, "brut")),
                    cellsEarned = ToInt(GetMemberValue(item, "cellsEarned")),
                    level = GetMemberValue(item, "level")?.ToString(),
                    surv = ToInt(GetMemberValue(item, "surv")),
                    tact = ToInt(GetMemberValue(item, "tact")),
                    time = ToDouble(GetMemberValue(item, "time"))
                };
                list.Add(entry);
            }

            return list.Count > 0 ? list : null;
        }

        private static IEnumerable? EnumerateUnknown(object? value)
        {
            if (value == null)
                return null;

            if (value is string)
                return null;

            if (value is IEnumerable enumerable)
                return enumerable;

            var arrayValue = GetMemberValue(value, "array");
            if (arrayValue is IEnumerable arrayEnumerable)
                return arrayEnumerable;

            return null;
        }

        private static object? GetMemberValue(object? obj, string name)
        {
            if (obj == null || string.IsNullOrWhiteSpace(name))
                return null;

            const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase;
            var type = obj.GetType();
            var prop = type.GetProperty(name, Flags);
            if (prop != null)
                return prop.GetValue(obj);
            var field = type.GetField(name, Flags);
            if (field != null)
                return field.GetValue(obj);

            return null;
        }

        private static int ToInt(object? value)
        {
            if (value == null)
                return 0;

            if (value is int i)
                return i;

            if (value is bool b)
                return b ? 1 : 0;

            if (value is IConvertible conv)
            {
                try
                {
                    return conv.ToInt32(CultureInfo.InvariantCulture);
                }
                catch { }
            }

            return 0;
        }

        private static double ToDouble(object? value)
        {
            if (value == null)
                return 0;

            if (value is double d)
                return d;

            if (value is float f)
                return f;

            if (value is IConvertible conv)
            {
                try
                {
                    return conv.ToDouble(CultureInfo.InvariantCulture);
                }
                catch { }
            }

            return 0;
        }

        private static bool ToBool(object? value, bool fallback)
        {
            var parsed = ToNullableBool(value);
            return parsed ?? fallback;
        }

        private static bool? ToNullableBool(object? value)
        {
            if (value == null)
                return null;

            if (value is bool b)
                return b;

            if (value is int i)
                return i != 0;

            if (value is IConvertible conv)
            {
                try
                {
                    return conv.ToInt32(CultureInfo.InvariantCulture) != 0;
                }
                catch { }
            }

            if (bool.TryParse(value.ToString(), out var parsed))
                return parsed;

            return null;
        }
    }
}
