/// <summary>
/// extOSC subscriber for the orchestration server's broadcast
/// (CONTROL_INTEGRATION.md §2, PROTOCOL.md §1).
///
/// A passive subscriber in the strictest sense: it holds nothing but the last
/// values received, never re-derives state from temperature, and sends exactly one
/// thing — <c>/client/hello</c>, so the server knows where to unicast.
///
/// THE ONE RULE THAT SILENTLY BREAKS EVERYTHING: hello must leave the *same socket*
/// we listen on. The server replies to the source (ip, port) of the hello packet —
/// not to a fixed port and not to the payload — so a client transmitting from an
/// ephemeral port registers successfully, appears connected in the server log, and
/// never receives a single broadcast. extOSC's default is exactly that failure
/// (<c>OSCLocalPortMode.Random</c>); <c>FromReceiver</c> + <c>SourceReceiver</c>
/// below is the fix.
///
/// Startup-order independence (CLAUDE.md §9 rule 3): boots to state 0 / intensity 0
/// and converges silently on the first broadcast, so it may launch before, during or
/// after the server with no operator action, and recovers from a server restart, a
/// dropped network or a device sleep within one broadcast interval.
///
/// No rendering knowledge lives here — see CoralAppearance / CoralMagnifier.
/// </summary>
using System;
using extOSC;
using UnityEngine;

namespace CoralPolyps
{
    public class CoralOscListener : MonoBehaviour
    {
        [Tooltip("Log every state change. Cheap, and the only record of what the room did " +
                 "if something looks wrong after the fact.")]
        public bool logStateChanges = true;

        // --- What the server told us. Held, never derived. ---
        public int State { get; private set; }
        public float Intensity { get; private set; }
        public float Temp { get; private set; }

        /// <summary>True once any broadcast has arrived. Drives the "snap, don't slew"
        /// exception on first convergence — a rupture that happened before this app
        /// existed must not be replayed as an animation.</summary>
        public bool EverReceived { get; private set; }

        /// <summary>Datagrams received. A number that isn't climbing is the single
        /// clearest signal in the HUD that the fault is the network, not the mapping.</summary>
        public int RxCount { get; private set; }

        /// <summary>Seconds since the last broadcast; the 5 Hz stream is the heartbeat.
        /// Infinity until the first one arrives.</summary>
        public float SecondsSinceMessage => EverReceived ? Time.realtimeSinceStartup - _lastRx : float.PositiveInfinity;

        public float SecondsSinceHello => _lastHello < 0f ? float.PositiveInfinity : Time.realtimeSinceStartup - _lastHello;

        /// <summary>Connected once, but the stream has since gone quiet (≈5 missed
        /// broadcasts). Not an error — hold the last value and wait.</summary>
        public bool Stalled => EverReceived && SecondsSinceMessage > 1f;

        public bool IsListening => _receiver != null && _receiver.IsStarted;
        public string ClientId => _cfg != null ? _cfg.client_id : "";
        public CoralConfig Config => _cfg;

        CoralConfig _cfg;
        OSCReceiver _receiver;
        OSCTransmitter _transmitter;
        float _lastRx;
        float _lastHello = -1f;
        int _lastLoggedState = -1;

        void Awake()
        {
            _cfg = CoralConfig.Shared;
            // Exhibition devices are pinned and tethered; nothing here should ever
            // let the screen go dark mid-visit (CONTROL_INTEGRATION.md §6.3).
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
        }

        void OnEnable() => OpenSockets();

        void OnDisable() => CloseSockets();

        void Update()
        {
            if (SecondsSinceHello >= Mathf.Max(0.5f, _cfg.hello_interval_s)) SendHello();
        }

        /// <summary>
        /// iOS tears the socket down when the app backgrounds. Rebuild it on resume
        /// and re-announce immediately rather than waiting up to a full hello interval
        /// — this is the "a device that sleeps and wakes resyncs itself" acceptance
        /// test (CONTROL_INTEGRATION.md §6.2, §8).
        /// </summary>
        void OnApplicationPause(bool paused)
        {
            if (paused) return;
            Debug.Log("[osc] resumed — rebuilding socket and re-announcing");
            Reconnect();
        }

        /// <summary>Tear down and rebuild both sockets, then announce. Also how a
        /// server address corrected in the HUD takes effect without a relaunch.</summary>
        public void Reconnect()
        {
            CloseSockets();
            OpenSockets();
        }

        void OpenSockets()
        {
            try
            {
                _receiver = gameObject.AddComponent<OSCReceiver>();
                _receiver.LocalPort = _cfg.listen_port;
                _receiver.Bind("/coral/state", OnState);
                _receiver.Bind("/coral/intensity", OnIntensity);
                _receiver.Bind("/coral/temp", OnTemp);
                _receiver.Connect();

                _transmitter = gameObject.AddComponent<OSCTransmitter>();
                _transmitter.RemoteHost = _cfg.server_host;
                _transmitter.RemotePort = _cfg.server_port;
                // NOT the default (Random). See the class comment — this single line
                // is the difference between receiving the fan-out and sitting at
                // state 0 forever with no error anywhere.
                _transmitter.LocalPortMode = OSCLocalPortMode.FromReceiver;
                _transmitter.SourceReceiver = _receiver;
                _transmitter.Connect();

                Debug.Log($"[osc] listening on UDP {_cfg.listen_port}, hello -> " +
                          $"{_cfg.server_host}:{_cfg.server_port} as id={_cfg.client_id}");
                SendHello();
            }
            catch (Exception e)
            {
                // A busy port or an unreachable host must not take the app down: it
                // simply holds state 0 (a healthy coral), which is the correct
                // failure mode for a gallery.
                Debug.LogError($"[osc] could not open sockets on UDP {_cfg.listen_port}: {e.Message}");
            }
        }

        void CloseSockets()
        {
            if (_transmitter != null)
            {
                _transmitter.Close();
                Destroy(_transmitter);
                _transmitter = null;
            }
            if (_receiver != null)
            {
                _receiver.Close();
                Destroy(_receiver);
                _receiver = null;
            }
            _lastHello = -1f;
        }

        void SendHello()
        {
            _lastHello = Time.realtimeSinceStartup;
            if (_transmitter == null) return;
            try
            {
                _transmitter.Send(OSCMessage.Create("/client/hello", OSCValue.String(_cfg.client_id)));
            }
            catch (Exception e)
            {
                // The server may not be up yet, or the Mac may have moved. Keep
                // trying on the heartbeat — it will answer once it is there.
                Debug.LogWarning($"[osc] hello failed: {e.Message}");
            }
        }

        void OnState(OSCMessage m)
        {
            if (!TryReadFloat(m, out float v)) return;
            State = Mathf.Clamp(Mathf.RoundToInt(v), 0, 3);
            if (logStateChanges && State != _lastLoggedState)
            {
                Debug.Log($"[osc] state {_lastLoggedState} -> {State} ({StateName(State)}) intensity={Intensity:F2}");
                _lastLoggedState = State;
            }
            Mark();
        }

        void OnIntensity(OSCMessage m)
        {
            if (!TryReadFloat(m, out float v)) return;
            Intensity = Mathf.Clamp01(v);
            Mark();
        }

        void OnTemp(OSCMessage m)
        {
            // Diagnostics only. Deriving state from this would make the app an
            // independent actor and break the single-source-of-truth rule.
            if (TryReadFloat(m, out float v)) { Temp = v; Mark(); }
        }

        void Mark()
        {
            _lastRx = Time.realtimeSinceStartup;
            if (!EverReceived)
            {
                EverReceived = true;
                Debug.Log("[osc] connected — first broadcast received, converged to server state");
            }
            RxCount++;
        }

        public static string StateName(int state) => state switch
        {
            0 => "Natural",
            1 => "Fluorescent",
            2 => "Bleached",
            3 => "Recovery",
            _ => "?",
        };

        /// <summary>
        /// Read the first argument as a float, accepting int/float/double. The server
        /// sends int32 for state and float32 for the rest, but being permissive means
        /// a type change upstream degrades to a no-op rather than a silent freeze.
        /// </summary>
        static bool TryReadFloat(OSCMessage m, out float value)
        {
            value = 0f;
            if (m?.Values == null || m.Values.Count == 0) return false;
            var v = m.Values[0];
            switch (v.Type)
            {
                case OSCValueType.Float: value = v.FloatValue; return true;
                case OSCValueType.Int: value = v.IntValue; return true;
                case OSCValueType.Double: value = (float)v.DoubleValue; return true;
                default: return false;
            }
        }
    }
}
