// M5 Scope B (concept 3.3.1b): the PASSIVE world-feed adapter. A runtime
// AddComponent through the bridge's session start (TacticalAIBridge
// .EnsureFeed) - NO prefab surgery, NO scene edits, NO new player input.
// It only READS state the App maintains anyway for the game flow itself:
//
//   * missionLogger.logItems      -> Spawn/Death/Removal/ActivationStart
//                                    (SagaDGPrefab/SagaHGPrefab log these
//                                    for the mission log UI anyway)
//   * DeploymentCard.woundTrackerValue -> GroupDamage (the human's damage
//                                    wheels; read-only diff, the wheel UI
//                                    needs nothing from us)
//   * HeroState.isWounded/isDefeated/hasActivated -> HeroWounded/HeroDefeated/
//                                    HeroActivated (squad HUD state)
//   * gameVars.round              -> RoundEnd
//   * gameVars.currentThreat      -> ThreatSpend
//   * gameVars.currentObjective   -> ObjectiveChange
//   * missionProperties           -> MissionStart (posture input, 3.3.1b #4)
//
// Reset at (session-)mission start; a seq anomaly (restored session: the
// mission logger list shrinks below our drain index) triggers RESYNC = a
// re-log of the complete history from current state, never a guess.
//
// Every log line starts with "WorldEvents::" - that is the player.log
// surface future diagnosis reads. All payloads are PUBLIC information; the
// Core feed validates kind+key vocabulary fail-loud (leak invariant 7.1).
using System;
using System.Collections.Generic;
using UnityEngine;
using Tac = TacticalAI.Core;

namespace TacticalAI
{
    [DisallowMultipleComponent]
    public sealed class WorldEventsUnity : MonoBehaviour
    {
        public Tac.WorldEventFeed Feed { get; private set; }

        string _sessionKey;
        int _drainedLog;                 // missionLogger.logItems drain cursor
        int _round;                      // gameVars.round we last saw
        long _threat;                    // currentThreat we last saw
        string _objective;               // currentObjective we last saw
        bool _firstLogged;               // "WorldEvents::first" line once
        float _pollAccum;

        // passive snapshots (diff sources) - keyed by card id / hero name
        readonly Dictionary<string, int[]> _wheels = new Dictionary<string, int[]>();
        readonly HashSet<string> _seenGroups = new HashSet<string>();
        readonly HashSet<string> _heroWounded = new HashSet<string>();
        readonly HashSet<string> _heroDefeated = new HashSet<string>();
        readonly HashSet<string> _heroActivated = new HashSet<string>();

        const float PollInterval = 0.25f;    // seconds; UI-speed state, no game coupling

        void Awake()
        {
            Feed = new Tac.WorldEventFeed("pending");
        }

        void Update()
        {
            try
            {
                if (!TacticalAISettings.Enabled) return;
                _pollAccum += Time.unscaledDeltaTime;
                if (_pollAccum < PollInterval) return;
                _pollAccum = 0f;
                Poll();
            }
            catch (Exception e)
            {
                // the feed must NEVER break the game; loss is visible in the log
                Debug.LogWarning("WorldEvents::poll error (ignored): " + e.Message);
            }
        }

        /// <summary>Synchronous pull used by the bridge before EVERY decision
        /// (and by tests): ingest current state, then project it.</summary>
        public Tac.FeedWorldView ProjectNow()
        {
            try { Poll(); }
            catch (Exception e) { Debug.LogWarning("WorldEvents::pull error (ignored): " + e.Message); }
            if (Feed == null) return null;
            return Tac.WorldEventVector.Project(Feed.Events, Feed.MissionKey, Feed.Hash());
        }

        // ------------------------------------------------------------------
        void Poll()
        {
            var sd = DataStore.sagaSessionData;
            if (sd == null || sd.gameVars == null) return;
            string key = SessionKey();
            if (key == null) return;
            if (key != _sessionKey) SessionStart(key);

            var gv = sd.gameVars;
            int round = Math.Max(1, gv.round);

            // restore/resync detector: a reloaded session replaces the log;
            // our cursor beyond the list end can only mean a seq discontinuity
            var logger = sd.missionLogger;
            if (logger != null && logger.logItems != null && logger.logItems.Count < _drainedLog)
            {
                _drainedLog = 0;
                Resync();
            }

            // round boundary: the finished round becomes public fact
            if (round != _round)
            {
                if (_round > 0) Append("RoundEnd", _round, null);
                _round = round;
            }

            // threat + objective (public HUD state)
            long threat = gv.currentThreat;
            if (threat != _threat)
            {
                if (_threat != 0 || Feed.Count > 0)
                    Append("ThreatSpend", round, P("balance", threat));
                _threat = threat;
            }
            var obj = gv.currentObjective;
            if (!string.Equals(obj ?? "", _objective ?? ""))
            {
                Append("ObjectiveChange", round, P("objective", obj ?? ""));
                _objective = obj ?? "";
            }

            // mission logger: what the App wrote for the player's own log
            DrainLogger(round);

            // deployed groups + per-figure damage wheels + hero markers
            PollGroups(round);
            PollHeroes(round);
        }

        void SessionStart(string key)
        {
            _sessionKey = key;
            Feed = new Tac.WorldEventFeed(key);
            TacticalAIBridge.Feed = Feed;
            _drainedLog = 0;
            _round = 0;
            _threat = 0;
            _objective = null;
            _wheels.Clear();
            _seenGroups.Clear();
            _heroWounded.Clear();
            _heroDefeated.Clear();
            _heroActivated.Clear();

            var sd = DataStore.sagaSessionData;
            var mp = TryMissionProperties();
            string mission = mp != null ? (mp.missionName ?? mp.missionID ?? "mission") : "mission";
            string objective = (sd.gameVars.currentObjective ?? null)
                               ?? (mp != null ? mp.startingObjective : null) ?? "";
            _objective = objective;
            _round = Math.Max(1, sd.gameVars.round);
            _threat = sd.gameVars.currentThreat;
            Append("MissionStart", _round, new Dictionary<string, object>
            {
                { "objective", objective },
                { "priorityTarget", mp != null ? mp.priorityTargetType.ToString() : "Rebel" },
                // runtime roundLimit wins (-1 = no clock); authored value
                // is the fallback while the session boots.
                { "roundLimit", (long)(sd.gameVars.roundLimit >= 0 ? sd.gameVars.roundLimit
                                          : mp != null ? mp.roundLimit : -1) },
                { "mission", mission },
                { "missionType", mp != null ? mp.missionType.ToString() : "Story" },
            });
            _objective = objective;   // MissionStart is the objective baseline;
                                      // ObjectiveChange only fires on real change.

            // baseline the wheels WITHOUT emitting events (state at feed
            // start is not a delta), and remember what is on the board.
            foreach (var cd in DataStore.deployedEnemies)
            {
                if (cd == null || cd.isDummy) continue;
                _wheels[cd.id] = (int[])cd.woundTrackerValue.Clone();
                _seenGroups.Add(cd.id);
            }
            Debug.Log("WorldEvents::session " + key + " mission=" + mission
                      + " objective=\"" + objective + "\"");
        }

        // -------------------------------------------------------- logger feed
        void DrainLogger(int round)
        {
            var sd = DataStore.sagaSessionData;
            var logger = sd == null ? null : sd.missionLogger;
            if (logger == null || logger.logItems == null) return;
            var items = logger.logItems;
            while (_drainedLog < items.Count)
            {
                var it = items[_drainedLog++];
                if (it == null || string.IsNullOrEmpty(it.message)) continue;
                int r = Math.Max(1, it.round);
                // M8/F1: LogItem.message is the LOCALIZED rich-text piece
                // (label + colored NAME); the raw card name is the last
                // color span - the raw-text compare of M5 never matched in
                // the shipping game and leaked labels into GroupId/hash.
                var name = Tac.MissionLogText.ExtractRawName(it.message);
                if (string.IsNullOrEmpty(name)) continue;
                switch (it.missionLogType)
                {
                    case Saga.MissionLogType.GroupDefeated:
                        {
                            // heroes and groups share the log kind - resolve
                            // by what is actually deployed (public state)
                            if (_heroDefeated.Contains(name) || IsHeroName(name))
                                Append("HeroDefeated", r, P("hero", name));
                            else
                                Append("Death", r, P("group", IdForName(name)));
                            break;
                        }
                    case Saga.MissionLogType.GroupRemoved:
                        Append("Removal", r, P("group", IdForName(name)));
                        break;
                    case Saga.MissionLogType.GroupActivation:
                        if (!IsHeroName(name)) Append("ActivationStart", r, P("group", IdForName(name)));
                        break;
                }
            }
        }

        // ------------------------------------------------------- group wheels
        void PollGroups(int round)
        {
            foreach (var cd in DataStore.deployedEnemies)
            {
                if (cd == null || cd.isDummy) continue;
                if (!_seenGroups.Contains(cd.id))
                {
                    _seenGroups.Add(cd.id);
                    _wheels[cd.id] = (int[])cd.woundTrackerValue.Clone();
                    Append("Spawn", round, new Dictionary<string, object>
                    {
                        { "group", cd.id },
                        { "size", (long)Math.Max(1, cd.size) },
                    });
                    continue;
                }
                int[] prev;
                if (!_wheels.TryGetValue(cd.id, out prev))
                {
                    // the card list rebuilt under us (save-restore): wheel
                    // history is gone -> full re-log, never a guess
                    prev = (int[])cd.woundTrackerValue.Clone();
                    _wheels[cd.id] = prev;
                    Resync();
                    return;
                }
                int n = Math.Min(Math.Max(cd.size, 1), Math.Min(prev.Length, cd.woundTrackerValue.Length));
                for (int i = 0; i < n; i++)
                {
                    int cur = cd.woundTrackerValue[i];
                    if (cur == prev[i]) continue;
                    prev[i] = cur;
                    Append("GroupDamage", round, new Dictionary<string, object>
                    {
                        { "group", cd.id },
                        { "figure", (long)i },
                        { "wheel", (long)cur },
                        { "health", (long)cd.health },
                    });
                }
            }
        }

        // -------------------------------------------------------- hero markers
        void PollHeroes(int round)
        {
            foreach (var cd in DataStore.deployedHeroes)
            {
                var hs = cd.heroState;
                if (hs == null) continue;
                var name = cd.name;
                if (hs.isDefeated && !_heroDefeated.Contains(name))
                {
                    _heroDefeated.Add(name);
                    Append("HeroDefeated", round, P("hero", name));
                }
                if (hs.isWounded && !_heroWounded.Contains(name))
                {
                    _heroWounded.Add(name);
                    Append("HeroWounded", round, P("hero", name));
                }
                if (hs.hasActivated != null)
                    for (int i = 0; i < hs.hasActivated.Length; i++)
                    {
                        if (!hs.hasActivated[i]) continue;
                        var tag = name + "#" + i;
                        if (_heroActivated.Add(tag))
                            Append("HeroActivated", round, P("hero", name));
                    }
            }
        }

        // ------------------------------------------------------------- resync
        /// <summary>Re-log the COMPLETE current history (seq renumbered from
        /// 1) so consumers recover a contiguous stream. Built purely from
        /// current public state - nothing estimated.</summary>
        public void Resync()
        {
            var log = new List<Tac.WorldEvent>();
            int round = Math.Max(1, _round);
            var mp = TryMissionProperties();
            var sd = DataStore.sagaSessionData;
            string mission = mp != null ? (mp.missionName ?? mp.missionID ?? "mission") : "mission";
            string objective = _objective
                               ?? (mp != null ? mp.startingObjective : null) ?? "";
            log.Add(Mk(1, "MissionStart", round, new Dictionary<string, object>
            {
                { "objective", objective },
                { "priorityTarget", mp != null ? mp.priorityTargetType.ToString() : "Rebel" },
                { "roundLimit", (long)(sd != null && sd.gameVars != null && sd.gameVars.roundLimit >= 0
                                       ? sd.gameVars.roundLimit
                                       : mp != null ? mp.roundLimit : -1) },
                { "mission", mission },
                { "missionType", mp != null ? mp.missionType.ToString() : "Story" },
                { "resync", true },
            }));
            int seq = 2;
            foreach (var cd in DataStore.deployedEnemies)
            {
                if (cd == null || cd.isDummy) continue;
                log.Add(Mk(seq++, "Spawn", round, new Dictionary<string, object>
                {
                    { "group", cd.id },
                    { "size", (long)Math.Max(1, cd.size) },
                    { "resync", true },
                }));
                _wheels[cd.id] = (int[])cd.woundTrackerValue.Clone();
                _seenGroups.Add(cd.id);
                int n = Math.Min(Math.Max(cd.size, 1), Math.Min(3, cd.woundTrackerValue.Length));
                for (int i = 0; i < n; i++)
                    log.Add(Mk(seq++, "GroupDamage", round, new Dictionary<string, object>
                    {
                        { "group", cd.id },
                        { "figure", (long)i },
                        { "wheel", (long)cd.woundTrackerValue[i] },
                        { "health", (long)cd.health },
                        { "resync", true },
                    }));
            }
            foreach (var h in _heroWounded) log.Add(Mk(seq++, "HeroWounded", round, P("hero", h)));
            foreach (var h in _heroDefeated) log.Add(Mk(seq++, "HeroDefeated", round, P("hero", h)));
            log.Add(Mk(seq++, "ThreatSpend", round, P("balance", (long)(sd?.gameVars?.currentThreat ?? 0))));
            Feed.Resync(log);
            _drainedLog = 0;
            var logger = sd == null ? null : sd.missionLogger;
            if (logger != null && logger.logItems != null) _drainedLog = logger.logItems.Count;
            Debug.Log("WorldEvents::resync " + Feed.MissionKey + " seq=" + Feed.LastSeq
                      + " groups=" + _seenGroups.Count + " heroes=" + _heroWounded.Count + "/"
                      + _heroDefeated.Count + " (re-log from current state, no estimation)");
        }

        // ------------------------------------------------------------ plumbing
        Tac.WorldEvent Append(string kind, int round, Dictionary<string, object> payload)
        {
            var e = Feed.Append(kind, round, payload);
            if (!_firstLogged)
            {
                _firstLogged = true;
                Debug.Log("WorldEvents::first seq=" + e.Seq + " kind=" + kind
                          + " round=" + round + " mission=" + Feed.MissionKey);
            }
            return e;
        }

        static Tac.WorldEvent Mk(int seq, string kind, int round, Dictionary<string, object> p)
            => new Tac.WorldEvent(kind, round, p) { Seq = seq };

        static Dictionary<string, object> P(string k, object v)
            => new Dictionary<string, object> { { k, v } };

        static string SessionKey()
        {
            try
            {
                return Saga.RunningCampaign.sagaCampaignGUID + "|" +
                       (DataStore.sagaSessionData?.setupOptions?.projectItem?.missionID ?? "solo");
            }
            catch { return null; }
        }

        static Saga.MissionProperties TryMissionProperties()
        {
            try { return DataStore.mission?.missionProperties; }
            catch { return null; }
        }

        // name -> card id resolution over deployed public state (never hidden).
        // Unknown names pass through unchanged: they are still public facts.
        static string IdForName(string name)
        {
            try
            {
                foreach (var cd in DataStore.deployedEnemies)
                    if (cd != null && cd.name == name) return cd.id;
            }
            catch { }
            return name;
        }

        bool IsHeroName(string name)
        {
            try
            {
                foreach (var cd in DataStore.deployedHeroes)
                    if (cd != null && cd.name == name) return true;
            }
            catch { }
            return false;
        }
    }
}
