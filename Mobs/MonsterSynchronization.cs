using System;
using System.Collections.Generic;
using System.Diagnostics;
using dc;
using dc.en;
using dc.pr;
using dc.tool._Cooldown;
using dc.tool.atk;
using DeadCellsMultiplayerMod.Interface.ModuleInitializing;
using ModCore.Events;
using ModCore.Modules;

namespace DeadCellsMultiplayerMod.Mobs.MobsSynchronization
{
    public class MobsSynchronization :
        IOnAdvancedModuleInitializing,
        IEventReceiver
    {
        private readonly ModEntry modEntry;

        private readonly struct MobTargetState
        {
            public readonly int SpawnUid;
            public readonly string Type;
            public readonly double X;
            public readonly double Y;
            public readonly int Dir;
            public readonly int Life;
            public readonly int MaxLife;
            public readonly bool Dead;
            public readonly ulong FastCheckMask;

            public MobTargetState(int spawnUid, string type, double x, double y, int dir, int life, int maxLife, bool dead, ulong fastCheckMask)
            {
                SpawnUid = spawnUid;
                Type = type;
                X = x;
                Y = y;
                Dir = dir;
                Life = life;
                MaxLife = maxLife;
                Dead = dead;
                FastCheckMask = fastCheckMask;
            }
        }

        private static readonly Dictionary<int, Mob> MobBySpawnUid = new();
        private static readonly Dictionary<int, int> SpawnUidByRuntimeUid = new();
        private static readonly Dictionary<int, MobTargetState> ClientTargets = new();
        private static readonly object SyncLock = new();

        private static long _nextPumpTicks;
        private static long _nextSnapshotTicks;
        private static int _fallbackSpawnUid = -1;
        private static int _lastLevelUid;
        private static bool _applyingRemoteDamage;

        private static readonly long PumpIntervalTicks = global::System.Math.Max(1, Stopwatch.Frequency / 60);
        private static readonly long SnapshotIntervalTicks = global::System.Math.Max(1, Stopwatch.Frequency / 12);
        private const double SnapshotLerp = 0.35;
        private const double SnapshotSnapDistanceSq = 4.0;
        private static readonly int[] SyncedFastCheckKeys =
        {
            54525952,   // aiLocked
            314572800,  // invalidateMove/canUpdateMove
            316669952,  // behaviour_platformPatrol
            318767104,  // aggressiveTeleportAi/necromancedTeleportAi
            320864256,  // aggressiveTeleportAi/necromancedTeleportAi
            325058560,  // eliteTeleportAi
            111149056,  // eliteTeleportAi/initSkills
            331350016,  // fixedUpdate state
            333447168,  // fixedUpdate state
            335544320,  // fixedUpdate state
            46137344,   // temporary death/unconscious state
            65011712,   // onLand/fall splash timing
            71303168,   // initSkills/tryToPreventDeath timing
            75497472,   // initSkills timing
            77594624,   // teleport initSkills timing
            83886080,   // aggressiveTeleportAi/initSkills timing
            94371840,   // onCooldownEnd/initSkills timing
            96468992,   // onCooldownEnd/initSkills timing
            98566144,   // onCooldownEnd/initSkills timing
            117440512,  // skill execution timing
            119537664,  // skill execution timing
            121634816,  // skill execution timing
            123731968,  // skill execution timing
            1742733312, // preUpdate state
        };

        public MobsSynchronization(ModEntry entry)
        {
            EventSystem.AddReceiver(this);
            modEntry = entry;
        }

        public void OnAdvancedModuleInitializing(ModEntry entry)
        {
            entry.Logger.Information("\x1b[32m[[ModEntry.MobsSynchronization] Initializing MobsSynchronization hooks...]\x1b[0m ");
            Hook_Level.init += Hook_Level_init;
            Hook_Level.attachMob += Hook_Level_attachMob;
            Hook_Mob.dispose += Hook_Mob_dispose;
            Hook_Mob.behaviourAi += Hook_Mob_behaviourAi;
            Hook_Mob.onDamage += Hook_Mob_onDamage;
            Hook_Mob.fixedUpdate += Hook_Mob_fixedUpdate;
            Hook_Mob.setNemesisTarget += Hook_Mob_setNemesisTarget;
        }

        private void Hook_Level_init(Hook_Level.orig_init orig, Level self)
        {
            ResetMobState();
            orig(self);
            SetCurrentLevelUid(self?.__uid ?? 0);
        }

        private Mob Hook_Level_attachMob(Hook_Level.orig_attachMob orig, Level self, dc.level.Mob e)
        {
            var mob = orig(self, e);
            if (mob == null || !IsRealMob(mob))
                return mob;

            var spawnUid = e?.__uid ?? 0;
            var mobType = NormalizeMobType(e?.kind?.ToString() ?? GetMobType(mob));
            var cx = e?.cx ?? mob.cx;
            var cy = e?.cy ?? mob.cy;

            lock (SyncLock)
            {
                if (spawnUid == 0)
                    spawnUid = BuildFallbackSpawnUidLocked(mobType, cx, cy);
                RegisterMobLocked(spawnUid, mob);
            }

            return mob;
        }

        private void Hook_Mob_dispose(Hook_Mob.orig_dispose orig, Mob self)
        {
            if (self != null && IsRealMob(self))
                UnregisterMob(self);
            orig(self);
        }

        private void Hook_Mob_behaviourAi(Hook_Mob.orig_behaviourAi orig, Mob self)
        {
            if (self != null && IsRealMob(self))
            {
                var net = ModEntry._net;
                if (net != null && net.IsAlive && !net.IsHost)
                    return;
            }

            orig(self);
        }

        private void Hook_Mob_onDamage(Hook_Mob.orig_onDamage orig, Mob self, AttackData attack)
        {
            if (self == null || !IsRealMob(self))
            {
                orig(self, attack);
                return;
            }

            var net = ModEntry._net;
            if (net == null || !net.IsAlive)
            {
                orig(self, attack);
                return;
            }

            if (!net.IsHost)
            {
                var before = self.life;
                orig(self, attack);
                var after = self.life;
                var damage = global::System.Math.Max(0, before - after);
                if (damage <= 0)
                    return;

                if (!WasDamageFromLocalHero(attack))
                    return;

                EnsureMobRegistered(self);
                if (!TryGetSpawnUid(self, out var spawnUid))
                    return;

                var mobType = GetMobType(self);
                net.SendMobDamage(spawnUid, mobType, damage);
                return;
            }

            if (_applyingRemoteDamage)
            {
                orig(self, attack);
                return;
            }

            orig(self, attack);
        }

        private void Hook_Mob_fixedUpdate(Hook_Mob.orig_fixedUpdate orig, Mob self)
        {
            orig(self);

            if (self == null || !IsRealMob(self))
                return;

            EnsureLevelContext(self._level);
            EnsureMobRegistered(self);

            var net = ModEntry._net;
            if (net == null || !net.IsAlive)
                return;

            PumpNetwork(self, net);

            if (!net.IsHost)
                ApplyClientSnapshot(self);
        }

        private void Hook_Mob_setNemesisTarget(Hook_Mob.orig_setNemesisTarget orig, Mob self, Entity e)
        {
            if (self == null)
            {
                orig(self, e);
                return;
            }

            if (ModEntry.me != null && ReferenceEquals(e, ModEntry.me))
            {
                var team = self._team;
                var targetHelper = team.get_targetHelper();
                targetHelper.filterUntargetables();
                orig(self, targetHelper.getBest());
                return;
            }

            orig(self, e);
        }

        private static void PumpNetwork(Mob context, NetNode net)
        {
            var now = Stopwatch.GetTimestamp();
            if (now < _nextPumpTicks)
                return;
            _nextPumpTicks = now + PumpIntervalTicks;

            var level = context._level;
            if (level == null)
                return;

            if (net.IsHost)
            {
                ProcessHostDamageQueue(net, level);
                if (now >= _nextSnapshotTicks)
                {
                    _nextSnapshotTicks = now + SnapshotIntervalTicks;
                    BroadcastHostSnapshots(net, level);
                }
            }
            else
            {
                ProcessClientSnapshotQueue(net, level);
            }
        }

        private static void ProcessHostDamageQueue(NetNode net, Level level)
        {
            if (!net.TryConsumeRemoteMobDamages(out var damages))
                return;

            for (int i = 0; i < damages.Count; i++)
            {
                var damage = damages[i];
                if (damage.SpawnUid == 0 || damage.Damage <= 0)
                    continue;

                if (!TryResolveMob(level, damage.SpawnUid, damage.Type, null, null, out var mob))
                    continue;

                ApplyAuthoritativeDamage(mob, damage.Damage);
            }
        }

        private static void ProcessClientSnapshotQueue(NetNode net, Level level)
        {
            if (!net.TryConsumeRemoteMobSnapshots(out var snapshots))
                return;

            for (int i = 0; i < snapshots.Count; i++)
            {
                var snapshot = snapshots[i];
                if (snapshot.SpawnUid == 0)
                    continue;

                var normalizedType = NormalizeMobType(snapshot.Type);
                var normalizedSnapshot = new MobTargetState(
                    snapshot.SpawnUid,
                    normalizedType,
                    snapshot.X,
                    snapshot.Y,
                    snapshot.Dir,
                    snapshot.Life,
                    snapshot.MaxLife,
                    snapshot.Dead,
                    snapshot.FastCheckMask);

                lock (SyncLock)
                {
                    ClientTargets[snapshot.SpawnUid] = normalizedSnapshot;
                }

                TryResolveMob(level, snapshot.SpawnUid, normalizedType, snapshot.X, snapshot.Y, out _);
            }
        }

        private static void BroadcastHostSnapshots(NetNode net, Level level)
        {
            var entities = level.entities;
            if (entities == null)
                return;

            for (int i = 0; i < entities.length; i++)
            {
                var mob = entities.array[i] as Mob;
                if (mob == null || mob.destroyed || !IsRealMob(mob))
                    continue;

                EnsureMobRegistered(mob);
                if (!TryGetSpawnUid(mob, out var spawnUid))
                    continue;

                var mobType = GetMobType(mob);
                var x = mob.spr != null ? mob.spr.x : mob.cx;
                var y = mob.spr != null ? mob.spr.y : mob.cy;
                var fastCheckMask = BuildFastCheckMask(mob);
                net.SendMobState(spawnUid, mobType, x, y, mob.dir, mob.life, mob.maxLife, mob.life <= 0, fastCheckMask);
            }
        }

        private static void ApplyClientSnapshot(Mob mob)
        {
            if (mob == null || mob.destroyed)
                return;

            if (!TryGetSpawnUid(mob, out var spawnUid))
                return;

            MobTargetState target;
            lock (SyncLock)
            {
                if (!ClientTargets.TryGetValue(spawnUid, out target))
                    return;
            }

            var currentX = mob.spr != null ? mob.spr.x : mob.cx;
            var currentY = mob.spr != null ? mob.spr.y : mob.cy;
            var dx = target.X - currentX;
            var dy = target.Y - currentY;
            var distSq = dx * dx + dy * dy;
            double nextX;
            double nextY;

            if (distSq > SnapshotSnapDistanceSq)
            {
                nextX = target.X;
                nextY = target.Y;
            }
            else
            {
                nextX = currentX + dx * SnapshotLerp;
                nextY = currentY + dy * SnapshotLerp;
            }

            mob.setPosPixel(nextX, nextY);
            if (mob.spr != null)
            {
                // Keep visual sprite interpolation authoritative on client.
                mob.spr.x = nextX;
                mob.spr.y = nextY;
            }

            if (target.Dir != 0)
                mob.dir = target.Dir;

            if (target.MaxLife > 0 && mob.maxLife != target.MaxLife)
                mob.maxLife = target.MaxLife;

            if (mob.life != target.Life)
                mob.life = target.Life;

            if (target.Dead && mob.life > 0)
                mob.life = 0;

            ApplyFastCheckMask(mob, target.FastCheckMask);
        }

        private static ulong BuildFastCheckMask(Mob mob)
        {
            var cd = mob?.cd;
            var fastCheck = cd?.fastCheck;
            if (fastCheck == null)
                return 0;

            ulong mask = 0;
            var keyCount = global::System.Math.Min(SyncedFastCheckKeys.Length, 64);

            for (var i = 0; i < keyCount; i++)
            {
                try
                {
                    if (fastCheck.exists(SyncedFastCheckKeys[i]))
                        mask |= 1UL << i;
                }
                catch
                {
                }
            }

            return mask;
        }

        private static void ApplyFastCheckMask(Mob mob, ulong mask)
        {
            var cd = mob?.cd;
            var fastCheck = cd?.fastCheck;
            if (cd == null || fastCheck == null)
                return;

            var refreshFrames = GetFastCheckRefreshFrames(cd);
            var keyCount = global::System.Math.Min(SyncedFastCheckKeys.Length, 64);

            for (var i = 0; i < keyCount; i++)
            {
                var key = SyncedFastCheckKeys[i];
                var shouldExist = (mask & (1UL << i)) != 0;

                bool hasKey;
                try
                {
                    hasKey = fastCheck.exists(key);
                }
                catch
                {
                    continue;
                }

                if (shouldExist)
                {
                    if (hasKey)
                    {
                        try
                        {
                            if (fastCheck.get(key) is CdInst current && current.frames < refreshFrames)
                                current.frames = refreshFrames;
                        }
                        catch
                        {
                        }

                        continue;
                    }

                    try
                    {
                        var inst = new CdInst(key, refreshFrames);
                        fastCheck.set(key, inst);
                        try { cd.cdList?.push(inst); } catch { }
                    }
                    catch
                    {
                    }

                    continue;
                }

                if (!hasKey)
                    continue;

                try
                {
                    if (fastCheck.get(key) is CdInst existing)
                    {
                        try { cd.cdList?.remove(existing); } catch { }
                    }

                    fastCheck.remove(key);
                }
                catch
                {
                }
            }
        }

        private static double GetFastCheckRefreshFrames(dc.tool.Cooldown cd)
        {
            var baseFps = cd.baseFps;
            if (baseFps <= 0)
                baseFps = 60;
            return global::System.Math.Max(6.0, baseFps * 0.25);
        }

        private static void ApplyAuthoritativeDamage(Mob mob, int damage)
        {
            if (mob == null || mob.destroyed || damage <= 0)
                return;
            if (mob.life <= 0)
                return;

            _applyingRemoteDamage = true;
            try
            {
                var lifeBefore = mob.life;
                var source = ModEntry.me != null ? (Entity)ModEntry.me : mob;

                var attack = new AttackData();
                attack.init(source, damage);
                attack.source = source;
                attack.rawFinalDmg = damage;
                attack.finalDmg = damage;

                mob.onDamage(attack);

                if (mob.life >= lifeBefore)
                    mob.life = global::System.Math.Max(0, lifeBefore - damage);

                if (mob.life <= 0 && lifeBefore > 0)
                {
                    try { mob.onDie(); } catch { }
                }
            }
            catch
            {
                try
                {
                    mob.life = global::System.Math.Max(0, mob.life - damage);
                }
                catch
                {
                }
            }
            finally
            {
                _applyingRemoteDamage = false;
            }
        }

        private static bool TryResolveMob(Level level, int spawnUid, string mobType, double? expectedX, double? expectedY, out Mob mob)
        {
            mob = null!;
            if (spawnUid == 0 || level == null)
                return false;

            lock (SyncLock)
            {
                if (MobBySpawnUid.TryGetValue(spawnUid, out var mapped) && mapped != null && !mapped.destroyed)
                {
                    mob = mapped;
                    return true;
                }
            }

            var entities = level.entities;
            if (entities == null)
                return false;

            Mob bestMob = null!;
            var bestScore = double.MaxValue;
            var hasExpectedPosition = expectedX.HasValue && expectedY.HasValue;

            for (int i = 0; i < entities.length; i++)
            {
                var candidate = entities.array[i] as Mob;
                if (candidate == null || candidate.destroyed || !IsRealMob(candidate))
                    continue;

                var candidateType = GetMobType(candidate);
                if (!IsMobTypeCompatible(candidateType, mobType))
                    continue;

                var candidateRuntimeUid = candidate.__uid;
                var mappedToOtherSpawnUid = false;
                lock (SyncLock)
                {
                    if (candidateRuntimeUid != 0 &&
                        SpawnUidByRuntimeUid.TryGetValue(candidateRuntimeUid, out var existingSpawnUid) &&
                        existingSpawnUid != spawnUid)
                    {
                        if (!hasExpectedPosition)
                            continue;
                        mappedToOtherSpawnUid = true;
                    }
                }

                var score = 0.0;
                if (hasExpectedPosition)
                {
                    var x = candidate.spr != null ? candidate.spr.x : candidate.cx;
                    var y = candidate.spr != null ? candidate.spr.y : candidate.cy;
                    var dx = x - expectedX!.Value;
                    var dy = y - expectedY!.Value;
                    score = dx * dx + dy * dy;

                    if (mappedToOtherSpawnUid)
                    {
                        // Allow rebinding from local runtime UIDs to host spawn UIDs,
                        // but still slightly prefer already-unbound candidates.
                        score += 0.25;
                    }
                }

                if (bestMob == null || score < bestScore)
                {
                    bestMob = candidate;
                    bestScore = score;
                    if (bestScore <= 0.0001)
                        break;
                }
            }

            if (bestMob == null)
                return false;

            lock (SyncLock)
            {
                RegisterMobLocked(spawnUid, bestMob);
            }

            mob = bestMob;
            return true;
        }

        private static bool WasDamageFromLocalHero(AttackData attack)
        {
            if (attack == null)
                return false;

            var hero = ModEntry.me;
            if (hero == null)
                return false;

            if (attack.source != null && ReferenceEquals(attack.source, hero))
                return true;

            return attack.sourceWeapon != null &&
                   attack.sourceWeapon.owner != null &&
                   ReferenceEquals(attack.sourceWeapon.owner, hero);
        }

        private static bool IsRealMob(Mob mob)
        {
            if (mob == null)
                return false;
            var typeName = mob.GetType().ToString();
            return typeName.Contains("dc.en.mob.", StringComparison.Ordinal);
        }

        private static string NormalizeMobType(string mobType)
        {
            if (string.IsNullOrWhiteSpace(mobType))
                return string.Empty;
            return mobType.Replace("|", "/").Replace("\r", string.Empty).Replace("\n", string.Empty).Trim();
        }

        private static string GetMobType(Mob mob)
        {
            if (mob == null)
                return string.Empty;

            var type = mob.type?.ToString();
            if (string.IsNullOrWhiteSpace(type))
                type = mob.GetType().ToString();

            return NormalizeMobType(type);
        }

        private static bool IsMobTypeCompatible(string runtimeType, string incomingType)
        {
            if (string.IsNullOrWhiteSpace(incomingType))
                return true;
            if (string.IsNullOrWhiteSpace(runtimeType))
                return false;

            if (string.Equals(runtimeType, incomingType, StringComparison.OrdinalIgnoreCase))
                return true;

            if (runtimeType.EndsWith(incomingType, StringComparison.OrdinalIgnoreCase))
                return true;

            if (incomingType.EndsWith(runtimeType, StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        private static void EnsureMobRegistered(Mob mob)
        {
            if (mob == null)
                return;

            if (TryGetSpawnUid(mob, out _))
                return;

            lock (SyncLock)
            {
                if (TryGetSpawnUidLocked(mob, out _))
                    return;

                var spawnUid = BuildFallbackSpawnUidLocked(GetMobType(mob), mob.cx, mob.cy);
                RegisterMobLocked(spawnUid, mob);
            }
        }

        private static bool TryGetSpawnUid(Mob mob, out int spawnUid)
        {
            lock (SyncLock)
            {
                return TryGetSpawnUidLocked(mob, out spawnUid);
            }
        }

        private static bool TryGetSpawnUidLocked(Mob mob, out int spawnUid)
        {
            spawnUid = 0;
            if (mob == null)
                return false;

            var runtimeUid = mob.__uid;
            if (runtimeUid != 0 && SpawnUidByRuntimeUid.TryGetValue(runtimeUid, out spawnUid))
            {
                if (MobBySpawnUid.TryGetValue(spawnUid, out var mapped) && ReferenceEquals(mapped, mob))
                    return true;
                SpawnUidByRuntimeUid.Remove(runtimeUid);
            }

            foreach (var pair in MobBySpawnUid)
            {
                if (!ReferenceEquals(pair.Value, mob))
                    continue;

                spawnUid = pair.Key;
                if (runtimeUid != 0)
                    SpawnUidByRuntimeUid[runtimeUid] = spawnUid;
                return true;
            }

            return false;
        }

        private static void RegisterMobLocked(int spawnUid, Mob mob)
        {
            if (spawnUid == 0 || mob == null)
                return;

            if (MobBySpawnUid.TryGetValue(spawnUid, out var oldMob) && oldMob != null && !ReferenceEquals(oldMob, mob))
            {
                var oldRuntimeUid = oldMob.__uid;
                if (oldRuntimeUid != 0 &&
                    SpawnUidByRuntimeUid.TryGetValue(oldRuntimeUid, out var oldSpawnUid) &&
                    oldSpawnUid == spawnUid)
                    SpawnUidByRuntimeUid.Remove(oldRuntimeUid);
            }

            MobBySpawnUid[spawnUid] = mob;

            var runtimeUid = mob.__uid;
            if (runtimeUid == 0)
                return;

            if (SpawnUidByRuntimeUid.TryGetValue(runtimeUid, out var previousSpawnUid) && previousSpawnUid != spawnUid)
                MobBySpawnUid.Remove(previousSpawnUid);

            SpawnUidByRuntimeUid[runtimeUid] = spawnUid;
        }

        private static int BuildFallbackSpawnUidLocked(string mobType, int cx, int cy)
        {
            unchecked
            {
                var baseHash = ComputeDeterministicSpawnHash(mobType, cx, cy);
                var candidate = baseHash;
                var salt = 1;

                while (candidate == 0 || MobBySpawnUid.ContainsKey(candidate))
                {
                    candidate = MixDeterministicSpawnHash(baseHash, salt++);
                    if (salt > 64)
                        break;
                }

                if (candidate != 0 && !MobBySpawnUid.ContainsKey(candidate))
                    return candidate;

                while (MobBySpawnUid.ContainsKey(_fallbackSpawnUid))
                    _fallbackSpawnUid--;

                return _fallbackSpawnUid--;
            }
        }

        private static int ComputeDeterministicSpawnHash(string mobType, int cx, int cy)
        {
            unchecked
            {
                uint hash = 2166136261;
                var text = (mobType ?? string.Empty).ToLowerInvariant();
                for (var i = 0; i < text.Length; i++)
                {
                    hash ^= text[i];
                    hash *= 16777619;
                }

                hash ^= (uint)cx;
                hash *= 16777619;
                hash ^= (uint)cy;
                hash *= 16777619;

                var candidate = (int)(hash & 0x7FFFFFFF);
                return candidate == 0 ? 1 : candidate;
            }
        }

        private static int MixDeterministicSpawnHash(int baseHash, int salt)
        {
            unchecked
            {
                uint hash = (uint)baseHash;
                hash ^= (uint)(salt * 374761393);
                hash = (hash << 13) | (hash >> 19);
                hash *= 1274126177;
                var candidate = (int)(hash & 0x7FFFFFFF);
                return candidate == 0 ? 1 : candidate;
            }
        }

        private static void UnregisterMob(Mob mob)
        {
            lock (SyncLock)
            {
                var runtimeUid = mob.__uid;
                if (runtimeUid != 0 && SpawnUidByRuntimeUid.TryGetValue(runtimeUid, out var spawnUid))
                {
                    SpawnUidByRuntimeUid.Remove(runtimeUid);
                    MobBySpawnUid.Remove(spawnUid);
                    ClientTargets.Remove(spawnUid);
                    return;
                }

                var found = 0;
                var hasFound = false;
                foreach (var pair in MobBySpawnUid)
                {
                    if (!ReferenceEquals(pair.Value, mob))
                        continue;
                    found = pair.Key;
                    hasFound = true;
                    break;
                }

                if (hasFound)
                {
                    MobBySpawnUid.Remove(found);
                    ClientTargets.Remove(found);
                }
            }
        }

        private static void ResetMobState()
        {
            lock (SyncLock)
            {
                MobBySpawnUid.Clear();
                SpawnUidByRuntimeUid.Clear();
                ClientTargets.Clear();
                _fallbackSpawnUid = -1;
                _lastLevelUid = 0;
            }

            _nextPumpTicks = 0;
            _nextSnapshotTicks = 0;
        }

        private static void SetCurrentLevelUid(int levelUid)
        {
            lock (SyncLock)
            {
                _lastLevelUid = levelUid;
            }
        }

        private static void EnsureLevelContext(Level level)
        {
            if (level == null)
                return;

            var levelUid = level.__uid;
            var changed = false;

            lock (SyncLock)
            {
                if (_lastLevelUid == 0)
                {
                    _lastLevelUid = levelUid;
                    return;
                }

                if (_lastLevelUid != levelUid)
                {
                    _lastLevelUid = levelUid;
                    MobBySpawnUid.Clear();
                    SpawnUidByRuntimeUid.Clear();
                    ClientTargets.Clear();
                    _fallbackSpawnUid = -1;
                    changed = true;
                }
            }

            if (changed)
            {
                _nextPumpTicks = 0;
                _nextSnapshotTicks = 0;
            }
        }
    }
}
