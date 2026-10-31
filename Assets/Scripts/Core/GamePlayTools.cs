using System.Collections.Generic;
using UnityEngine;
using IntersectionMaster.Data;
using IntersectionMaster.Traffic;
using IntersectionMaster.Systems;

namespace IntersectionMaster.Core
{
    /// <summary>Herramientas al jugar, como el original (botones Tráfico y Semáforo de la derecha).</summary>
    public enum PlayTool { None, Info, StopCar, Controller, MultiButton }

    /// <summary>Botón para varios semáforos puesto durante la partida (solo para esa partida).</summary>
    public class RuntimeButton
    {
        public Vector2 pos;
        public readonly List<KeyValuePair<IntersectionController, Approach>> links = new List<KeyValuePair<IntersectionController, Approach>>();
        public GameObject view;
    }

    public partial class GameController
    {
        public PlayTool Tool { get; private set; }
        /// <summary>Vehículo elegido con Información.</summary>
        public VehicleController InfoVehicle { get; private set; }
        public bool FollowInfo;
        /// <summary>Controlador que se edita (Semáforo → Controlador de semáforos) y etapa elegida (0..5).</summary>
        public IntersectionController CtrlEdit { get; private set; }
        public int CtrlStage;
        public readonly List<RuntimeButton> RuntimeButtons = new List<RuntimeButton>();
        public RuntimeButton EditButton { get; private set; }
        public const int MaxStages = 6;

        Transform toolRoot;
        readonly List<SpriteRenderer> toolPool = new List<SpriteRenderer>();
        int toolUsed;

        public void SetPlayTool(PlayTool t)
        {
            Tool = Tool == t ? PlayTool.None : t;
            if (Tool != PlayTool.Info) { InfoVehicle = null; FollowInfo = false; }
            if (Tool != PlayTool.Controller) CtrlEdit = null;
            if (Tool != PlayTool.MultiButton) EditButton = null;
        }

        void ResetPlayTools()
        {
            Tool = PlayTool.None;
            InfoVehicle = null;
            FollowInfo = false;
            CtrlEdit = null;
            EditButton = null;
            foreach (var b in RuntimeButtons) if (b.view != null) Destroy(b.view);
            RuntimeButtons.Clear();
            VehicleController.ShowPatience = false;
        }

        /// <summary>Todos los semáforos del mapa a rojo (Semáforo → Todo rojo).</summary>
        public void AllRed()
        {
            if (sim == null) return;
            foreach (var ic in sim.Intersections) ic.light.AllRed();
            AudioManager.Instance.PlayClick();
        }

        VehicleController VehicleAt(Vector2 world, float tol)
        {
            VehicleController best = null;
            float bd = tol;
            foreach (var v in sim.Vehicles)
            {
                if (v.agent != 0 || v.arrived) continue;
                float d = Vector2.Distance(v.Pos, world) - v.HalfLength * 0.6f;
                if (d < bd) { bd = d; best = v; }
            }
            return best;
        }

        /// <summary>Toque en el mapa con una herramienta activa. Devuelve true si lo atendió.</summary>
        bool HandleToolTap(Vector2 world, float slack)
        {
            switch (Tool)
            {
                case PlayTool.Info:
                    {
                        var v = VehicleAt(world, 0.45f + slack);
                        InfoVehicle = v;
                        if (v == null) FollowInfo = false;
                        return true;
                    }
                case PlayTool.StopCar:
                    {
                        var v = VehicleAt(world, 0.45f + slack);
                        if (v != null) { v.userStopped = !v.userStopped; AudioManager.Instance.PlayClick(); }
                        return true;
                    }
                case PlayTool.Controller:
                    {
                        IntersectionController ic; Approach app;
                        if (!mapRenderer.FindSignalAt(world, slack, out ic, out app)) return true;
                        if (CtrlEdit != ic) { CtrlEdit = ic; CtrlStage = 0; EnsureStages(ic); return true; }
                        // Tocar un semáforo lo pone o lo quita de la etapa elegida.
                        var ph = ic.light.phases[CtrlStage];
                        if (!ph.approaches.Remove(app)) ph.approaches.Add(app);
                        AudioManager.Instance.PlayClick();
                        return true;
                    }
                case PlayTool.MultiButton:
                    {
                        var hit = RuntimeButtonAt(world, 0.4f + slack);
                        if (hit != null) { EditButton = hit; return true; }
                        IntersectionController ic; Approach app;
                        if (EditButton != null && mapRenderer.FindSignalAt(world, slack, out ic, out app))
                        {
                            int idx = EditButton.links.FindIndex(kv => kv.Value == app);
                            if (idx >= 0) EditButton.links.RemoveAt(idx);
                            else EditButton.links.Add(new KeyValuePair<IntersectionController, Approach>(ic, app));
                            AudioManager.Instance.PlayClick();
                            return true;
                        }
                        // Tocar un sitio libre pone un botón nuevo.
                        var b = new RuntimeButton { pos = world };
                        b.view = MakeButtonView(world);
                        RuntimeButtons.Add(b);
                        EditButton = b;
                        AudioManager.Instance.PlayClick();
                        return true;
                    }
            }
            return false;
        }

        RuntimeButton RuntimeButtonAt(Vector2 world, float tol)
        {
            foreach (var b in RuntimeButtons) if (Vector2.Distance(b.pos, world) < tol) return b;
            return null;
        }

        GameObject MakeButtonView(Vector2 p)
        {
            var go = new GameObject("RuntimeButton");
            go.transform.position = new Vector3(p.x, p.y, 0f);
            SpriteFactory.Make("Base", go.transform, SpriteFactory.Circle, new Color(0.2f, 0.2f, 0.24f, 0.95f), Vector2.zero, Vector2.one * 0.5f, 70);
            SpriteFactory.Make("Face", go.transform, SpriteFactory.Circle, new Color(0.91f, 0.47f, 0.13f, 1f), Vector2.zero, Vector2.one * 0.36f, 71);
            SpriteFactory.Make("Icon", go.transform, IntersectionMaster.UI.IconKit.Get("gbutton"), Color.white, Vector2.zero, Vector2.one * 0.3f, 72);
            return go;
        }

        /// <summary>Botón puesto en la partida: cada semáforo vinculado cambia (verde → rojo y rojo → verde).</summary>
        void PressRuntimeButton(RuntimeButton b)
        {
            if (b.links.Count == 0) { ShowMessage("Este botón no tiene semáforos: elige «Botón para múltiples semáforos» y toca los semáforos."); return; }
            foreach (var kv in b.links)
            {
                if (kv.Key.light.IsOn(kv.Value)) kv.Key.light.RequestRed(kv.Value);
                else kv.Key.light.RequestGreen(kv.Value);
            }
            PhaseChanges++;
            AudioManager.Instance.PlayClick();
        }

        /// <summary>Asegura MaxStages etapas en el controlador (las que ya tenía se conservan).</summary>
        void EnsureStages(IntersectionController ic)
        {
            var ph = ic.light.phases;
            while (ph.Count < MaxStages) ph.Add(new SignalPhase { label = "Etapa " + (ph.Count + 1) });
        }

        public int StageCount(IntersectionController ic)
        {
            int n = 0;
            foreach (var p in ic.light.phases) if (p.approaches.Count > 0) n++;
            return n;
        }

        public void SetCtrlAuto(bool on)
        {
            if (CtrlEdit == null) return;
            var l = CtrlEdit.light;
            // Solo se recorren las etapas con semáforos.
            l.phases.RemoveAll(p => p.approaches.Count == 0);
            if (on && l.phases.Count < 2) { ShowMessage("Pon semáforos en al menos dos etapas."); EnsureStages(CtrlEdit); return; }
            l.autoCycle = on;
            if (on) l.RestartCycle();
            if (!on || l.phases.Count < MaxStages) EnsureStages(CtrlEdit);
        }

        public float StageGreen(int stage)
        {
            if (CtrlEdit == null || stage >= CtrlEdit.light.phases.Count) return 8f;
            var p = CtrlEdit.light.phases[stage];
            return p.green > 0.1f ? p.green : CtrlEdit.light.greenSeconds;
        }

        public void AdjustStageGreen(float d)
        {
            if (CtrlEdit == null) return;
            EnsureStages(CtrlEdit);
            var p = CtrlEdit.light.phases[CtrlStage];
            p.green = Mathf.Clamp(StageGreen(CtrlStage) + d, 2f, 60f);
        }

        /// <summary>Tiempo entre etapas: ámbar y todo en rojo antes de la siguiente.</summary>
        public float ChangeDelay { get { return CtrlEdit == null ? 2f : CtrlEdit.light.yellowSeconds + CtrlEdit.light.allRedSeconds; } }

        public void AdjustChangeDelay(float d)
        {
            if (CtrlEdit == null) return;
            float v = Mathf.Clamp(ChangeDelay + d, 1f, 10f);
            CtrlEdit.light.yellowSeconds = v * 0.6f;
            CtrlEdit.light.allRedSeconds = v * 0.4f;
        }

        // ---------------- Dibujo de las herramientas (cada fotograma) ----------------

        SpriteRenderer ToolSpr(Sprite sp, Color c, Vector2 p, Vector2 size, int order, float rot = 0f)
        {
            if (toolRoot == null) toolRoot = new GameObject("PlayTools").transform;
            SpriteRenderer sr;
            if (toolUsed < toolPool.Count) sr = toolPool[toolUsed];
            else
            {
                var go = new GameObject("T");
                go.transform.SetParent(toolRoot, false);
                sr = go.AddComponent<SpriteRenderer>();
                toolPool.Add(sr);
            }
            toolUsed++;
            sr.enabled = true;
            sr.sprite = sp; sr.color = c; sr.sortingOrder = order;
            sr.transform.position = new Vector3(p.x, p.y, 0f);
            sr.transform.rotation = Quaternion.Euler(0f, 0f, rot);
            sr.transform.localScale = new Vector3(size.x, size.y, 1f);
            return sr;
        }

        void ToolLine(Vector2 a, Vector2 b, float w, Color c, int order)
        {
            Vector2 d = b - a;
            if (d.sqrMagnitude < 1e-6f) return;
            ToolSpr(SpriteFactory.Square, c, (a + b) * 0.5f, new Vector2(d.magnitude, w), order, Geo.Angle(d));
        }

        void LateUpdate()
        {
            toolUsed = 0;
            if (Screen == AppScreen.Playing && sim != null && sim.Running)
            {
                if (InfoVehicle != null && (InfoVehicle.arrived || !sim.Vehicles.Contains(InfoVehicle))) { InfoVehicle = null; FollowInfo = false; }
                if (InfoVehicle != null)
                {
                    // Ruta que le queda, en azul.
                    var v = InfoVehicle;
                    var blue = new Color(0.2f, 0.6f, 1f, 0.85f);
                    Vector2 prev = v.Pos;
                    float s0 = v.s;
                    for (int k = v.routeIndex; k < v.route.Count; k++)
                    {
                        var sg = v.route[k];
                        for (float ss = (k == v.routeIndex ? s0 : 0f); ss <= sg.Length; ss += 0.25f)
                        {
                            Vector2 q = sg.path.PointAt(ss);
                            ToolLine(prev, q, 0.06f, blue, 90);
                            prev = q;
                        }
                    }
                    ToolSpr(SpriteFactory.Ring, Color.white, v.Pos, Vector2.one * (v.Length + 0.2f), 91);
                    if (FollowInfo) cam.CenterOn(v.Pos);
                }
                if (Tool == PlayTool.Controller && CtrlEdit != null)
                {
                    // Los semáforos de la etapa elegida, marcados.
                    var ph = CtrlStage < CtrlEdit.light.phases.Count ? CtrlEdit.light.phases[CtrlStage] : null;
                    foreach (var a in CtrlEdit.light.Approaches)
                    {
                        Vector2 m = mapRenderer.SignalMid(a);
                        bool inStage = ph != null && ph.approaches.Contains(a);
                        ToolSpr(SpriteFactory.Ring, inStage ? new Color(0.25f, 0.9f, 0.4f, 0.95f) : new Color(1f, 1f, 1f, 0.5f), m, Vector2.one * 0.7f, 92);
                    }
                }
                foreach (var b in RuntimeButtons)
                {
                    bool sel = b == EditButton && Tool == PlayTool.MultiButton;
                    if (sel) ToolSpr(SpriteFactory.Ring, Color.white, b.pos, Vector2.one * 0.65f, 69);
                    if (Tool != PlayTool.MultiButton) continue;
                    foreach (var kv in b.links) ToolLine(b.pos, mapRenderer.SignalMid(kv.Value), 0.03f, new Color(0.4f, 0.75f, 1f, sel ? 0.9f : 0.45f), 68);
                }
            }
            for (int i = toolUsed; i < toolPool.Count; i++) toolPool[i].enabled = false;
        }
    }
}
