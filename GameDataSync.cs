
using dc;
using dc.hl.types;
using Hashlink.Virtuals;
using dc.level;
using HaxeProxy.Runtime;
using dc.tool;
using dc.libs;
using dc.pr;
using System.Globalization;
using Serilog.Core;
using System.Collections.Generic;
using System.Threading;
// using Newtonsoft.Json;

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
        private static readonly object _bossRuneLock = new();
        private static int? _remoteBossRune;
        private static int? _hostBossRune;

        private static readonly object _roomGenLock = new();
        private static bool? _remoteDisableLoreRooms;
        private static int? _remoteFixedSeed;
        private static readonly Dictionary<string, bool> _remoteLoreByLevel = new(StringComparer.Ordinal);
        private const int RoomGenWaitMs = 250;
        private const int RoomGenWaitStepMs = 25;
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

            lock (_roomGenLock)
            {
                _remoteDisableLoreRooms = null;
                _remoteFixedSeed = null;
                _remoteLoreByLevel.Clear();
            }
            
            if (net != null && net.IsHost)
            {
                Seed = GameMenu.ForceGenerateServerSeed("NewGame_hook");
                SendBossRune(self, net);
                net.SendSeed(Seed);
                SendRoomGenConfig(net);
            }
            else if (net != null)
            {
                if (GameMenu.TryGetRemoteSeed(out var remoteSeed))
                {
                    Seed = remoteSeed;
                }
                if (TryGetRemoteBossRune(out var bossRune))
                {
                    self.bossRuneActivated = bossRune;
                }
                else
                {
                    _log?.Warning("[NetMod] Remote boss rune not received yet");
                }
            }
            lvl = Seed;
            _isTwitch = isTwitch;
            _isCustom = isCustom;
            _mode = mode;
            _launch = gdata;
            self.pickDeathItem();
            SendHeroSkin(self, net);
            SendHeroHeadSkin(self, net);
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
            
            

            // SendHeroSkin(seed, net);
            return orig(self, seed, ldat, resetCount, resetCount2);
        }

        public static RoomNode hook_generateGraph(Hook_LevelGen.orig_generateGraph orig,
        LevelGen self,
        User ldat,
        virtual_baseLootLevel_biome_bonusTripleScrollAfterBC_cellBonus_dlc_doubleUps_eliteRoomChance_eliteWanderChance_flagsProps_group_icon_id_index_loreDescriptions_mapDepth_minGold_mobDensity_mobs_name_nextLevels_parallax_props_quarterUpsBC3_quarterUpsBC4_specificLoots_specificSubBiome_transitionTo_tripleUps_worldDepth_ rng,
        Rand @struct)
        {
            var net = GameMenu.NetRef;
            if (net == null || !net.IsAlive || net.IsHost)
                return orig(self, ldat, rng, @struct);

            bool? oldDisableLore = null;
            int? oldFixedSeed = null;
            if (TryWaitForRoomGenConfig(out var disableLoreRooms, out var fixedSeed))
            {
                _log?.Information("[NetMod] RoomGen sync applied: disableLoreRooms={DisableLore} fixedSeed={FixedSeed}", disableLoreRooms, fixedSeed);
                var main = Main.Class.ME;
                if (main != null && main.options != null)
                {
                    oldDisableLore = main.options.disableLoreRooms;
                    main.options.disableLoreRooms = disableLoreRooms;
                }

                var game = Game.Class.ME;
                if (game != null && game.data != null && game.data.cgData != null)
                {
                    oldFixedSeed = game.data.cgData.fixedSeed;
                    game.data.cgData.fixedSeed = fixedSeed;
                }
            }

            var root = orig(self, ldat, rng, @struct);

            if (oldDisableLore.HasValue && Main.Class.ME?.options != null)
                Main.Class.ME.options.disableLoreRooms = oldDisableLore.Value;
            if (oldFixedSeed.HasValue && Game.Class.ME?.data?.cgData != null)
                Game.Class.ME.data.cgData.fixedSeed = oldFixedSeed.Value;

            return root;
        }

        public static bool hook_levelRequiresLoreRoom(Hook_StoryManager.orig_levelRequiresLoreRoom orig,
            StoryManager self,
            virtual_baseLootLevel_biome_bonusTripleScrollAfterBC_cellBonus_dlc_doubleUps_eliteRoomChance_eliteWanderChance_flagsProps_group_icon_id_index_loreDescriptions_mapDepth_minGold_mobDensity_mobs_name_nextLevels_parallax_props_quarterUpsBC3_quarterUpsBC4_specificLoots_specificSubBiome_transitionTo_tripleUps_worldDepth_ d)
        {
            var net = GameMenu.NetRef;
            var levelId = d != null && d.id != null ? d.id.ToString() : null;
            if (net != null && net.IsHost)
            {
                var result = orig(self, d);
                if (!string.IsNullOrWhiteSpace(levelId))
                    net.SendLoreRequirement(levelId, result);
                return result;
            }

            if (net != null && !net.IsHost && !string.IsNullOrWhiteSpace(levelId))
            {
                if (TryWaitForLoreRequirement(levelId, out var value))
                    return value;
            }

            return orig(self, d);
        }

        public static void ReceiveRoomGenConfig(string payload)
        {
            if (string.IsNullOrWhiteSpace(payload))
                return;

            var parts = payload.Split('|');
            if (parts.Length < 2)
                return;

            var disableLore = parts[0] == "1";
            if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var fixedSeed))
                fixedSeed = -1;

            lock (_roomGenLock)
            {
                _remoteDisableLoreRooms = disableLore;
                _remoteFixedSeed = fixedSeed;
            }

            _log?.Information("[NetMod] RoomGen config received: disableLoreRooms={DisableLore} fixedSeed={FixedSeed}", disableLore, fixedSeed);
        }

        public static void ReceiveLoreRequirement(string payload)
        {
            if (string.IsNullOrWhiteSpace(payload))
                return;

            var parts = payload.Split('|', 2);
            if (parts.Length != 2)
                return;

            var levelId = parts[0];
            var required = parts[1] == "1";
            if (string.IsNullOrWhiteSpace(levelId))
                return;

            lock (_roomGenLock)
            {
                _remoteLoreByLevel[levelId] = required;
            }

            _log?.Information("[NetMod] Lore requirement received: levelId={LevelId} required={Required}", levelId, required);
        }

        private static bool TryWaitForRoomGenConfig(out bool disableLoreRooms, out int fixedSeed)
        {
            disableLoreRooms = false;
            fixedSeed = -1;

            var elapsed = 0;
            while (elapsed <= RoomGenWaitMs)
            {
                lock (_roomGenLock)
                {
                    if (_remoteDisableLoreRooms.HasValue && _remoteFixedSeed.HasValue)
                    {
                        disableLoreRooms = _remoteDisableLoreRooms.Value;
                        fixedSeed = _remoteFixedSeed.Value;
                        return true;
                    }
                }

                Thread.Sleep(RoomGenWaitStepMs);
                elapsed += RoomGenWaitStepMs;
            }

            return false;
        }

        private static bool TryWaitForLoreRequirement(string levelId, out bool value)
        {
            value = false;
            if (string.IsNullOrWhiteSpace(levelId))
                return false;

            var elapsed = 0;
            while (elapsed <= RoomGenWaitMs)
            {
                lock (_roomGenLock)
                {
                    if (_remoteLoreByLevel.TryGetValue(levelId, out value))
                        return true;
                }

                Thread.Sleep(RoomGenWaitStepMs);
                elapsed += RoomGenWaitStepMs;
            }

            return false;
        }

        private static void SendRoomGenConfig(NetNode net)
        {
            var disableLore = false;
            var fixedSeed = -1;
            var main = Main.Class.ME;
            if (main != null && main.options != null)
                disableLore = main.options.disableLoreRooms;
            var game = Game.Class.ME;
            if (game != null && game.data != null && game.data.cgData != null)
                fixedSeed = game.data.cgData.fixedSeed;

            net.SendRoomGenConfig(disableLore, fixedSeed);
        }

        public static void SendBossRune(User self, NetNode? net)
        {
            if (self == null)
                return;

            var bossRune = ToInt(self.bossRuneActivated);
            lock (_bossRuneLock)
            {
                _hostBossRune = bossRune;
            }

            if (net == null || !net.IsAlive)
                return;

            net.SendBossRune(bossRune);
        }

        public static void ReceiveBossRune(string payload)
        {
            if (string.IsNullOrWhiteSpace(payload))
                return;

            if (!int.TryParse(payload, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bossRune))
            {
                _log?.Warning("[NetMod] Failed to parse boss rune payload: {Payload}", payload);
                return;
            }

            lock (_bossRuneLock)
            {
                _remoteBossRune = bossRune;
            }

            _log?.Information("[NetMod] Received boss rune {BossRune}", bossRune);
        }

        public static bool TryGetHostBossRune(out int bossRune)
        {
            lock (_bossRuneLock)
            {
                if (_hostBossRune.HasValue)
                {
                    bossRune = _hostBossRune.Value;
                    return true;
                }
            }

            bossRune = 0;
            return false;
        }

        public static bool TryGetRemoteBossRune(out int bossRune)
        {
            lock (_bossRuneLock)
            {
                if (_remoteBossRune.HasValue)
                {
                    bossRune = _remoteBossRune.Value;
                    return true;
                }
            }

            bossRune = 0;
            return false;
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


        public static void ReceiveHeroHeadSkin(string skin)
        {
            try
            {
                var cleaned = CleanSkin(skin);
                if (string.IsNullOrWhiteSpace(cleaned))
                    cleaned = "BaseFlame";

                ModEntry.SetRemoteHeadSkin(cleaned);
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


        private static void SendHeroHeadSkin(User user, NetNode? net)
        {
            if (net == null || !net.IsAlive)
                return;

            try
            {
                var skin = CleanSkin(user?.heroHeadSkin?.ToString());
                if (string.IsNullOrWhiteSpace(skin))
                    skin = "BaseFlame";

                net.SendHeroHeadSkin(skin);
            }
            catch (Exception ex)
            {
                _log?.Warning("[NetMod] Failed to send hero skin: {Message}", ex.Message);
            }
        }

        private static string CleanSkin(string? skin)
        {
            if (string.IsNullOrEmpty(skin))
                return string.Empty;

            return skin.Replace("|", "/").Replace("\r", string.Empty).Replace("\n", string.Empty);
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

    }
}
