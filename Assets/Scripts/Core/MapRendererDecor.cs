using System.Collections.Generic;
using UnityEngine;
using IntersectionMaster.Data;
using IntersectionMaster.Traffic;

namespace IntersectionMaster.Core
{
    /// <summary>Objetos decorativos especiales, puntos lógicos (parada, peaje, detector) y botones de grupo.</summary>
    public partial class MapRenderer
    {
        MapData builtMap;

        public static readonly string[] ObjColors = { "#FFFFFF", "#F4C20D", "#E04B3C", "#3B82F6", "#2FA39A", "#2B2B2E" };
        public static readonly string[] ObjColorNames = { "Blanco", "Amarillo", "Rojo", "Azul", "Verde", "Negro" };
        public static readonly string[] MaterialNames = { "Hormigón", "Arena", "Agua", "Tierra", "Grava", "Césped oscuro", "Asfalto" };
        static readonly string[] MaterialColors = { "#BDBDB5", "#E3D3A0", "#5DA9E9", "#9C7B54", "#8F8F8F", "#6E9A55", "" };   // asfalto: el de la calzada
        public static readonly string[] StickerNames = { "Flecha recta", "Flecha izquierda", "Flecha derecha", "Bicicleta", "BUS", "LENTO" };
        public static readonly string[] SignFaces = { "Información", "Peligro", "Prohibido", "Dirección", "Cartel de autopista", "Parada de bus" };
        public static readonly string[] MiscNames = { "Cono", "Barril", "Banco", "Cajas", "Mesa de parque", "Boca de incendios", "Contenedor de basura", "Ventilador de tejado", "Cajas con cintas", "Cajas grandes", "Bolardo" };
        public static readonly string[] PowerLineNames = { "Tendido eléctrico", "Solo cables", "Torres de alta tensión" };
        public static readonly string[] LineStyles = { "Continua", "Discontinua", "Doble amarilla", "Amarilla", "Fina", "Punteada", "Rayas cortas", "Rayas largas", "Triángulos", "Gruesa" };

        /// <summary>
        /// Placa de una señal vista desde arriba como en el original: la placa está de pie, así que de frente se ve
        /// aplastada en vertical (≈0,55) y al girarla su eje horizontal se inclina en pantalla (se ve «en diagonal»);
        /// de canto es una línea y por detrás se ve la chapa gris. Se dibuja con una transformación 2×2 (giro·escala·giro).
        /// </summary>
        static void SignPlate(Transform parent, Sprite sprite, Color col, Vector2 center, float sz, float yaw, int order)
        {
            float cw = Mathf.Cos(yaw), sw = Mathf.Sin(yaw);
            if (Mathf.Abs(cw) < 0.06f) cw = cw < 0f ? -0.06f : 0.06f;
            const float lam = 0.55f;
            float m00 = cw, m10 = sw, m01 = 0f, m11 = lam;
            float E = (m00 + m11) * 0.5f, F = (m00 - m11) * 0.5f, G = (m10 + m01) * 0.5f, H = (m10 - m01) * 0.5f;
            float Q = Mathf.Sqrt(E * E + H * H), Rr = Mathf.Sqrt(F * F + G * G);
            float a1 = Mathf.Atan2(G, F), a2 = Mathf.Atan2(H, E);
            var go = new GameObject("SignPlate");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(center.x, center.y, 0f);
            go.transform.localRotation = Quaternion.Euler(0f, 0f, (a2 + a1) * 0.5f * Mathf.Rad2Deg);
            go.transform.localScale = new Vector3(Q + Rr, Q - Rr, 1f);
            SpriteFactory.Make("SignFace", go.transform, sprite, col, Vector2.zero, new Vector2(sz, sz), order, (a2 - a1) * 0.5f * Mathf.Rad2Deg);
        }

        Color ObjCol(int idx) { return SpriteFactory.Hex(ObjColors[Mathf.Abs(idx) % ObjColors.Length]); }

        SpriteRenderer Bar(Transform parent, string name, Vector2 a, Vector2 b, float w, Color c, int order)
        {
            Vector2 dv = b - a;
            float len = Mathf.Max(0.001f, dv.magnitude);
            return SpriteFactory.Make(name, parent, SpriteFactory.Square, c, (a + b) * 0.5f, new Vector2(len, w), order, Mathf.Atan2(dv.y, dv.x) * Mathf.Rad2Deg);
        }

        void Label(Transform parent, string text, Vector2 pos, float size, Color col, float rot, int order, float yScale = 1f)
        {
            var go = new GameObject("Text");
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(pos.x, pos.y, 0f);
            go.transform.rotation = Quaternion.Euler(0f, 0f, rot);
            var tm = go.AddComponent<TextMesh>();
            var font = IntersectionMaster.UI.UIKit.DefaultFont;
            tm.font = font;
            tm.text = text;
            tm.fontSize = 64;
            tm.characterSize = 0.05f * size;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = col;
            if (Mathf.Abs(yScale - 1f) > 0.01f) go.transform.localScale = new Vector3(1f, Mathf.Clamp(yScale, 0.2f, 5f), 1f);
            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                if (font != null) mr.sharedMaterial = font.material;
                mr.sortingOrder = order;
            }
        }

        /// <summary>Dibuja los tipos de decoración nuevos. Devuelve false para los tipos antiguos (los dibuja DrawDecor).</summary>
        bool DrawDecorExt(DecorItem d, MapData map)
        {
            Transform parent = staticRoot;
            Vector2 p = d.Pos;
            float s = d.size;
            float v = Mathf.Abs(d.variant);
            float ang = d.rot != 0f ? d.rot : 0f;
            var shadow = new Color(0, 0, 0, 0.28f);
            var steel = new Color(0.55f, 0.58f, 0.62f);
            var dark = new Color(0.2f, 0.22f, 0.26f);
            switch (d.type)
            {
                case DecorType.Trailer:
                    {
                        float a = d.rot != 0f ? d.rot : map.RoadAngleNear(p);
                        Vector2 f = FromAngle(a);
                        Color body = Color.Lerp(ObjCol(d.color == 0 ? 0 : d.color), SpriteFactory.Hex("#D9D9D4"), d.color == 0 ? 1f : 0f);
                        RegShadow(SpriteFactory.Make("TrailerShadow", parent, SpriteFactory.Square, shadow, p + new Vector2(0.04f, -0.05f), new Vector2(1.0f, 0.34f) * s, 3, a), p);
                        SpriteFactory.Make("Trailer", parent, SpriteFactory.Square, body, p, new Vector2(1.0f, 0.32f) * s, 4, a);
                        SpriteFactory.Make("TrailerRoof", parent, SpriteFactory.Square, Color.Lerp(body, Color.white, 0.2f), p, new Vector2(0.92f, 0.26f) * s, 5, a);
                        SpriteFactory.Make("Hitch", parent, SpriteFactory.Square, dark, p + f * 0.62f * s, new Vector2(0.24f, 0.05f) * s, 4, a);
                        SpriteFactory.Make("Wheels", parent, SpriteFactory.Square, dark, p - f * 0.28f * s, new Vector2(0.2f, 0.36f) * s, 3, a);
                        return true;
                    }
                case DecorType.FloodLight:
                    // Como la «Luz de inundación de autopista» del original: poste alto con un aro de cuatro focos.
                    DrawRoundLamp(parent, d, p);
                    return true;
                case DecorType.GlowLight:
                    {
                        Color c = d.color == 0 ? new Color(1f, 0.9f, 0.55f) : ObjCol(d.color);
                        var g1 = SpriteFactory.Make("Glow", parent, SpriteFactory.Soft, new Color(c.r, c.g, c.b, 0.3f), p, Vector2.one * 2.4f * s, 6);
                        var g2 = SpriteFactory.Make("Glow2", parent, SpriteFactory.Soft, new Color(c.r, c.g, c.b, 0.45f), p, Vector2.one * 1.2f * s, 6);
                        var g3 = SpriteFactory.Make("GlowCore", parent, SpriteFactory.Circle, Color.Lerp(c, Color.white, 0.6f), p, Vector2.one * 0.14f, 9);
                        if (d.flashPeriod > 0.05f)
                        {
                            // «Destello» del original: la luz se enciende y se apaga con el tiempo elegido.
                            AddBlinker(g1, d.flashPeriod); AddBlinker(g2, d.flashPeriod); AddBlinker(g3, d.flashPeriod);
                        }
                        return true;
                    }
                case DecorType.RailSign:
                    {
                        float a = d.rot != 0f ? d.rot : map.RoadAngleNear(p);
                        RegShadow(SpriteFactory.Make("RailShadow", parent, SpriteFactory.Circle, shadow, p + new Vector2(0.05f, -0.05f), Vector2.one * 0.14f, 3), p);
                        SpriteFactory.Make("RailPost", parent, SpriteFactory.Circle, steel, p, Vector2.one * 0.1f * s, 7);
                        SpriteFactory.Make("RailX1", parent, SpriteFactory.Square, Color.white, p, new Vector2(0.62f, 0.1f) * s, 8, a + 35f);
                        SpriteFactory.Make("RailX2", parent, SpriteFactory.Square, Color.white, p, new Vector2(0.62f, 0.1f) * s, 8, a - 35f);
                        SpriteFactory.Make("RailRed1", parent, SpriteFactory.Square, SpriteFactory.Hex("#D62828"), p, new Vector2(0.56f, 0.05f) * s, 9, a + 35f);
                        SpriteFactory.Make("RailRed2", parent, SpriteFactory.Square, SpriteFactory.Hex("#D62828"), p, new Vector2(0.56f, 0.05f) * s, 9, a - 35f);
                        // Dos luces rojas debajo de la cruz: parpadean mientras un tren se acerca (como el original).
                        Vector2 fa = FromAngle(a);
                        var l1 = SpriteFactory.Make("RailLamp1", parent, SpriteFactory.Circle, RailLampOff, p - new Vector2(0f, 0.16f * s) + fa * 0.1f * s, Vector2.one * 0.09f * s, 10);
                        var l2 = SpriteFactory.Make("RailLamp2", parent, SpriteFactory.Circle, RailLampOff, p - new Vector2(0f, 0.16f * s) - fa * 0.1f * s, Vector2.one * 0.09f * s, 10);
                        var g1 = SpriteFactory.Make("RailGlow1", parent, SpriteFactory.Soft, new Color(1f, 0.15f, 0.1f, 0f), l1.transform.position, Vector2.one * 0.35f * s, NightOverlayOrder + 2);
                        var g2 = SpriteFactory.Make("RailGlow2", parent, SpriteFactory.Soft, new Color(1f, 0.15f, 0.1f, 0f), l2.transform.position, Vector2.one * 0.35f * s, NightOverlayOrder + 2);
                        railSignLamps.Add(new RailLamps { pos = p, a = l1, b = l2, ga = g1, gb = g2, reach = 4f + 2f * d.light });
                        return true;
                    }
                case DecorType.PhonePole:
                    RegShadow(SpriteFactory.Make("PoleShadow", parent, SpriteFactory.Circle, shadow, p + new Vector2(0.06f, -0.06f), Vector2.one * 0.14f, 3), p);
                    SpriteFactory.Make("PoleArm", parent, SpriteFactory.Square, new Color(0.38f, 0.28f, 0.19f), p, new Vector2(0.56f, 0.05f) * s, 8, ang);
                    SpriteFactory.Make("PoleArm2", parent, SpriteFactory.Square, new Color(0.38f, 0.28f, 0.19f), p, new Vector2(0.36f, 0.04f) * s, 8, ang);
                    SpriteFactory.Make("Pole", parent, SpriteFactory.Circle, new Color(0.3f, 0.22f, 0.15f), p, Vector2.one * 0.12f, 9);
                    return true;
                case DecorType.RoadSign:
                    {
                        int fk = (int)v % SignFaces.Length;
                        if (fk == 4)
                        {
                            // Cartel de autopista: panel verde ancho sobre dos postes.
                            Vector2 f4 = FromAngle(ang);
                            RegShadow(SpriteFactory.Make("SignShadow", parent, SpriteFactory.Square, shadow, p + new Vector2(0.08f, -0.08f), new Vector2(1.0f, 0.14f) * s, 3, ang), p);
                            for (int k = -1; k <= 1; k += 2) SpriteFactory.Make("SignPost", parent, SpriteFactory.Circle, steel, p + f4 * (k * 0.38f * s), Vector2.one * 0.08f, 7);
                            SpriteFactory.Make("SignBoardBack", parent, SpriteFactory.Square, Color.white, p, new Vector2(0.98f, 0.13f) * s, 8, ang);
                            SpriteFactory.Make("SignBoard", parent, SpriteFactory.Square, SpriteFactory.Hex("#1E7B45"), p, new Vector2(0.94f, 0.1f) * s, 9, ang);
                            return true;
                        }
                        if (fk == 5)
                        {
                            // Señal de parada de bus: placa azul cuadrada con el autobús blanco.
                            RegShadow(SpriteFactory.Make("SignShadow", parent, SpriteFactory.Circle, shadow, p + new Vector2(-0.12f, -0.02f), new Vector2(0.2f, 0.12f) * s, 3), p);
                            Bar(parent, "SignPole", p, p + new Vector2(0f, 0.16f * s), 0.012f, new Color(0.45f, 0.47f, 0.5f), 8);
                            SpriteFactory.Make("BusSign", parent, SpriteFactory.Square, SpriteFactory.Hex("#2B6CC4"), p + new Vector2(0f, 0.2f * s), Vector2.one * 0.14f * s, 9);
                            SpriteFactory.Make("BusSignBus", parent, SpriteFactory.Square, Color.white, p + new Vector2(0f, 0.2f * s), new Vector2(0.09f, 0.05f) * s, 10);
                            return true;
                        }
                        string[] faces = { "#2B6CC4", "#F4C20D", "#D62828", "#2FA39A" };
                        Color face = SpriteFactory.Hex(faces[fk % 4]);
                        RegShadow(SpriteFactory.Make("SignShadow", parent, SpriteFactory.Square, shadow, p + new Vector2(0.05f, -0.06f), new Vector2(0.4f, 0.1f) * s, 3, ang), p);
                        SpriteFactory.Make("SignPost", parent, SpriteFactory.Circle, steel, p, Vector2.one * 0.09f, 7);
                        SpriteFactory.Make("SignBoardBack", parent, SpriteFactory.Square, Color.white, p, new Vector2(0.42f, 0.1f) * s, 8, ang);
                        SpriteFactory.Make("SignBoard", parent, SpriteFactory.Square, face, p, new Vector2(0.38f, 0.07f) * s, 9, ang);
                        return true;
                    }
                case DecorType.TrafficSign:
                    {
                        // Como el original: la señal está de pie sobre un poste recto (hacia abajo en pantalla) y el giro es
                        // en 3D: al girarla se estrecha hasta verse de canto, y por detrás se ve la chapa gris.
                        float yaw = (d.rot - 90f) * Mathf.Deg2Rad;
                        float cw = Mathf.Cos(yaw);
                        float sz = 0.19f * s;   // como el original: el disco mide ~0,37 del ancho del carril
                        float wScale = Mathf.Max(0.07f, Mathf.Abs(cw));
                        var sprite = IntersectionMaster.UI.IconKit.Get(SignCatalog.SpriteName(d.variant));
                        float poleLen = d.color == 0 ? sz * 0.85f : 0f;
                        Vector2 face = p + new Vector2(0f, poleLen);   // el punto colocado es la base del poste
                        var shadowCol = new Color(0f, 0f, 0f, 0.2f);
                        if (d.color == 0)
                        {
                            // Sombra suave del poste y de la placa sobre el suelo, hacia la izquierda.
                            // Como el original: la sombra cae lejos hacia la izquierda, con la placa más ancha que alta.
                            Vector2 sh = p + new Vector2(-poleLen * 3.4f, -poleLen * 0.05f);
                            RegPoleShadow(Bar(parent, "SignPoleShadow", p, sh, 0.012f, shadowCol, 6), p, sh);
                            RegShadow(SpriteFactory.Make("SignFaceShadow", parent, SpriteFactory.Circle, shadowCol, sh + new Vector2(-sz * 0.2f, 0f), new Vector2(sz * 1.5f, sz * 0.95f * wScale + 0.02f), 6), p);
                            Bar(parent, "SignPole", p, face, 0.012f, new Color(0.45f, 0.47f, 0.5f), 8);
                            SpriteFactory.Make("SignPoleBase", parent, SpriteFactory.Square, new Color(0.3f, 0.31f, 0.33f), p, Vector2.one * 0.03f, 8);
                        }
                        else RegShadow(SpriteFactory.Make("SignFaceShadow", parent, SpriteFactory.Circle, shadowCol, p + new Vector2(-0.04f, -0.05f), new Vector2(sz * wScale, sz * 0.9f), 6), p);
                        SignPlate(parent, sprite, cw >= 0f ? Color.white : new Color(0.42f, 0.43f, 0.46f, 1f), face, sz, yaw, 10);
                        return true;
                    }
                case DecorType.RoundStructure:
                    RegShadow(SpriteFactory.Make("TankShadow", parent, SpriteFactory.Circle, shadow, p + new Vector2(0.1f, -0.1f), Vector2.one * 1.1f * s, 3), p);
                    SpriteFactory.Make("Tank", parent, SpriteFactory.Circle, new Color(0.62f, 0.64f, 0.68f), p, Vector2.one * 1.05f * s, 4);
                    SpriteFactory.Make("TankRing", parent, SpriteFactory.Ring, new Color(0.35f, 0.37f, 0.42f), p, Vector2.one * 0.9f * s, 5);
                    SpriteFactory.Make("TankTop", parent, SpriteFactory.Circle, new Color(0.75f, 0.77f, 0.8f), p + new Vector2(-0.05f, 0.05f) * s, Vector2.one * 0.62f * s, 6);
                    return true;
                case DecorType.BusShelter:
                    {
                        float a = d.rot != 0f ? d.rot : map.RoadAngleNear(p);
                        Vector2 f = FromAngle(a), n = new Vector2(-f.y, f.x);
                        RegShadow(SpriteFactory.Make("ShelterShadow", parent, SpriteFactory.Square, shadow, p + new Vector2(0.06f, -0.06f), new Vector2(0.9f, 0.34f) * s, 3, a), p);
                        SpriteFactory.Make("ShelterRoof", parent, SpriteFactory.Square, new Color(0.55f, 0.7f, 0.82f, 0.9f), p, new Vector2(0.86f, 0.32f) * s, 6, a);
                        SpriteFactory.Make("ShelterEdge", parent, SpriteFactory.Square, steel, p + n * 0.15f * s, new Vector2(0.86f, 0.03f) * s, 7, a);
                        SpriteFactory.Make("Bench", parent, SpriteFactory.Square, new Color(0.45f, 0.32f, 0.2f), p - n * 0.06f * s, new Vector2(0.5f, 0.08f) * s, 7, a);
                        return true;
                    }
                case DecorType.Containers:
                    {
                        string[] cols = { "#C0392B", "#2E86C1", "#27AE60", "#E67E22", "#7F8C8D", "#F1C40F" };
                        float a = ang;
                        Vector2 f = FromAngle(a), n = new Vector2(-f.y, f.x);
                        RegShadow(SpriteFactory.Make("ContShadow", parent, SpriteFactory.Square, shadow, p + new Vector2(0.06f, -0.06f), new Vector2(1.05f, 0.56f) * s, 3, a), p);
                        if ((int)v >= 4)
                        {
                            // Pila de contenedores: tres alturas, cada una algo más arriba en pantalla (como un edificio bajo).
                            for (int lvl = 0; lvl < 3; lvl++)
                                for (int i = 0; i < 2; i++)
                                {
                                    Vector2 off = n * ((i - 0.5f) * 0.26f * s) + new Vector2(0f, lvl * 0.07f * s);
                                    Color c = SpriteFactory.Hex(cols[((int)v * 3 + i + lvl * 2) % cols.Length]);
                                    SpriteFactory.Make("ContSide", parent, SpriteFactory.Square, Color.Lerp(c, Color.black, 0.35f), p + off - new Vector2(0f, 0.035f * s), new Vector2(1.0f, 0.24f) * s, 4 + lvl * 2, a);
                                    SpriteFactory.Make("Container", parent, SpriteFactory.Square, c, p + off, new Vector2(1.0f, 0.24f) * s, 5 + lvl * 2, a);
                                }
                            return true;
                        }
                        for (int i = 0; i < 4; i++)
                        {
                            Vector2 off = f * ((i % 2) - 0.5f) * 0.52f * s + n * ((i / 2) - 0.5f) * 0.26f * s;
                            Color c = SpriteFactory.Hex(cols[((int)v + i * 2) % cols.Length]);
                            SpriteFactory.Make("Container", parent, SpriteFactory.Square, c, p + off, new Vector2(0.5f, 0.24f) * s, 4, a);
                            SpriteFactory.Make("ContTop", parent, SpriteFactory.Square, Color.Lerp(c, Color.white, 0.18f), p + off, new Vector2(0.44f, 0.18f) * s, 5, a);
                        }
                        return true;
                    }
                case DecorType.PowerLine:
                    {
                        Vector2 e = d.End;
                        float len = Vector2.Distance(p, e);
                        if (len < 0.2f) return true;
                        Vector2 dir = (e - p) / len, perp = new Vector2(-dir.y, dir.x);
                        int plk = (int)v % PowerLineNames.Length;
                        if (plk == 1)
                        {
                            // Solo cables (entre postes de teléfono): dos hilos finos de extremo a extremo.
                            for (int k = -1; k <= 1; k += 2)
                                Bar(parent, "Wire", p + perp * (k * 0.12f * s), e + perp * (k * 0.12f * s), 0.01f, new Color(0.1f, 0.1f, 0.12f, 0.75f), 11);
                            return true;
                        }
                        if (plk == 2)
                        {
                            // Torres de alta tensión: celosía cuadrada grande y tres cables.
                            int nt = Mathf.Max(1, Mathf.RoundToInt(len / 3.2f));
                            Vector2 pl = Vector2.zero, pr = Vector2.zero, pc = Vector2.zero;
                            for (int i = 0; i <= nt; i++)
                            {
                                Vector2 q = Vector2.Lerp(p, e, i / (float)nt);
                                float ta = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
                                RegShadow(SpriteFactory.Make("TowerShadow", parent, SpriteFactory.Square, new Color(0f, 0f, 0f, 0.18f), q + new Vector2(-0.35f, -0.05f), new Vector2(0.5f, 0.5f) * s, 3, ta + 45f), q);
                                SpriteFactory.Make("TowerBase", parent, SpriteFactory.Ring, dark, q, Vector2.one * 0.42f * s, 9);
                                Bar(parent, "TowerX1", q - (dir + perp) * 0.15f * s, q + (dir + perp) * 0.15f * s, 0.02f, dark, 9);
                                Bar(parent, "TowerX2", q - (dir - perp) * 0.15f * s, q + (dir - perp) * 0.15f * s, 0.02f, dark, 9);
                                SpriteFactory.Make("TowerArm", parent, SpriteFactory.Square, steel, q, new Vector2(0.05f, 0.9f * s), 10, ta);
                                Vector2 l = q + perp * 0.42f * s, r = q - perp * 0.42f * s;
                                if (i > 0)
                                {
                                    Bar(parent, "Wire", pl, l, 0.014f, new Color(0.1f, 0.1f, 0.12f, 0.8f), 11);
                                    Bar(parent, "Wire", pr, r, 0.014f, new Color(0.1f, 0.1f, 0.12f, 0.8f), 11);
                                    Bar(parent, "Wire", pc, q, 0.014f, new Color(0.1f, 0.1f, 0.12f, 0.8f), 11);
                                }
                                pl = l; pr = r; pc = q;
                            }
                            return true;
                        }
                        int n = Mathf.Max(1, Mathf.RoundToInt(len / 1.6f));
                        Vector2 prevL = Vector2.zero, prevR = Vector2.zero, prevC = Vector2.zero;
                        for (int i = 0; i <= n; i++)
                        {
                            Vector2 q = Vector2.Lerp(p, e, i / (float)n);
                            RegShadow(SpriteFactory.Make("PylonShadow", parent, SpriteFactory.Circle, shadow, q + new Vector2(0.06f, -0.06f), Vector2.one * 0.18f * s, 3), q);
                            SpriteFactory.Make("PylonArm", parent, SpriteFactory.Square, steel, q, new Vector2(0.04f, 0.5f * s), 9, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
                            SpriteFactory.Make("Pylon", parent, SpriteFactory.Circle, dark, q, Vector2.one * 0.14f * s, 10);
                            Vector2 l = q + perp * 0.25f * s, r = q - perp * 0.25f * s;
                            if (i > 0)
                            {
                                Bar(parent, "Wire", prevL, l, 0.012f, new Color(0.1f, 0.1f, 0.12f, 0.8f), 11);
                                Bar(parent, "Wire", prevR, r, 0.012f, new Color(0.1f, 0.1f, 0.12f, 0.8f), 11);
                                Bar(parent, "Wire", prevC, q, 0.012f, new Color(0.1f, 0.1f, 0.12f, 0.8f), 11);
                            }
                            prevL = l; prevR = r; prevC = q;
                        }
                        return true;
                    }
                case DecorType.Crane:
                    {
                        float a = ang;
                        Vector2 f = FromAngle(a), n = new Vector2(-f.y, f.x);
                        Color body = d.color == 0 ? SpriteFactory.Hex("#E8761F") : ObjCol(d.color);
                        RegShadow(SpriteFactory.Make("CraneShadow", parent, SpriteFactory.Square, shadow, p + new Vector2(0.1f, -0.1f), new Vector2(1.7f, 0.9f) * s, 3, a), p);
                        SpriteFactory.Make("CraneRailA", parent, SpriteFactory.Square, dark, p + n * 0.4f * s, new Vector2(1.7f, 0.07f) * s, 4, a);
                        SpriteFactory.Make("CraneRailB", parent, SpriteFactory.Square, dark, p - n * 0.4f * s, new Vector2(1.7f, 0.07f) * s, 4, a);
                        SpriteFactory.Make("CraneBeam", parent, SpriteFactory.Square, body, p, new Vector2(0.18f, 1.0f) * s, 9, a);
                        SpriteFactory.Make("CraneBeam2", parent, SpriteFactory.Square, Color.Lerp(body, Color.black, 0.2f), p, new Vector2(1.5f, 0.12f) * s, 8, a);
                        SpriteFactory.Make("CraneCab", parent, SpriteFactory.Square, Color.Lerp(body, Color.white, 0.2f), p + n * 0.1f * s, new Vector2(0.3f, 0.22f) * s, 10, a);
                        return true;
                    }
                case DecorType.Sticker:
                    {
                        Color c = d.color == 0 ? new Color(1f, 1f, 1f, 0.92f) : ObjCol(d.color);
                        int kind = (int)v % 6;
                        float a = ang;
                        Vector2 f = FromAngle(a), n = new Vector2(-f.y, f.x);
                        switch (kind)
                        {
                            case 0:
                                SpriteFactory.Make("ArrowStem", parent, SpriteFactory.Square, c, p - f * 0.1f * s, new Vector2(0.5f, 0.1f) * s, 4, a);
                                SpriteFactory.Make("ArrowHead", parent, SpriteFactory.Triangle, c, p + f * 0.28f * s, new Vector2(0.3f, 0.3f) * s, 4, a - 90f);
                                break;
                            case 1:
                            case 2:
                                {
                                    float sg = kind == 1 ? 1f : -1f;
                                    SpriteFactory.Make("ArrowStem", parent, SpriteFactory.Square, c, p - f * 0.1f * s, new Vector2(0.4f, 0.1f) * s, 4, a);
                                    SpriteFactory.Make("ArrowStem2", parent, SpriteFactory.Square, c, p + f * 0.12f * s + n * sg * 0.12f * s, new Vector2(0.1f, 0.34f) * s, 4, a);
                                    SpriteFactory.Make("ArrowHead", parent, SpriteFactory.Triangle, c, p + f * 0.12f * s + n * sg * 0.34f * s, new Vector2(0.28f, 0.26f) * s, 4, a + (sg > 0 ? 0f : 180f));
                                    break;
                                }
                            case 3:
                                SpriteFactory.Make("BikeW1", parent, SpriteFactory.Ring, c, p - f * 0.2f * s, Vector2.one * 0.26f * s, 4);
                                SpriteFactory.Make("BikeW2", parent, SpriteFactory.Ring, c, p + f * 0.2f * s, Vector2.one * 0.26f * s, 4);
                                SpriteFactory.Make("BikeBar", parent, SpriteFactory.Square, c, p, new Vector2(0.4f, 0.04f) * s, 4, a + 20f);
                                break;
                            case 4: Label(parent, "BUS", p, 1.6f * s, c, a, 5); break;
                            default: Label(parent, "LENTO", p, 1.3f * s, c, a, 5); break;
                        }
                        return true;
                    }
                case DecorType.TextLabel:
                    {
                        // Como el original: color propio (selector) y escala X/Y por separado (y2 = alto respecto al ancho).
                        Color tc = RoadData.ValidHex(d.hexA) ? SpriteFactory.Hex(d.hexA) : (d.color == 0 ? new Color(0.12f, 0.14f, 0.18f) : ObjCol(d.color));
                        Label(parent, string.IsNullOrEmpty(d.text) ? "Texto" : d.text, p, 1.6f * s, tc, d.rot, 12, d.y2 > 0.05f ? d.y2 : 1f);
                    }
                    return true;
                case DecorType.Lines:
                    {
                        Vector2 e = d.End;
                        float len = Vector2.Distance(p, e);
                        if (len < 0.1f) return true;
                        Vector2 dir = (e - p) / len, perp = new Vector2(-dir.y, dir.x);
                        int style = (int)v % LineStyles.Length;
                        Color c = RoadData.ValidHex(d.hexA) ? SpriteFactory.Hex(d.hexA) : (d.color != 0 ? ObjCol(d.color) : (style >= 2 ? SpriteFactory.Hex("#F4C20D") : new Color(1f, 1f, 1f, 0.95f)));
                        float w = 0.06f * s;
                        if (style == 1)
                        {
                            for (float t = 0f; t < len; t += 0.5f)
                                Bar(parent, "Dash", p + dir * t, p + dir * Mathf.Min(len, t + 0.28f), w, c, 4);
                        }
                        else if (style == 2)
                        {
                            Bar(parent, "Line", p + perp * 0.05f, e + perp * 0.05f, w * 0.8f, c, 4);
                            Bar(parent, "Line", p - perp * 0.05f, e - perp * 0.05f, w * 0.8f, c, 4);
                        }
                        else if (style >= 5 && style <= 7)
                        {
                            // Punteada (puntos juntos), rayas cortas separadas y rayas largas separadas, como las líneas 3, 5 y 6 del original.
                            float dash = style == 5 ? 0.06f : (style == 6 ? 0.18f : 0.36f), gap = style == 5 ? 0.08f : (style == 6 ? 0.5f : 0.5f);
                            var mbl = new MeshBatch();
                            for (float t = 0f; t < len; t += dash + gap) mbl.Rect(p + dir * Mathf.Min(len, t + dash * 0.5f), dir, Mathf.Min(dash, len - t), w * 0.85f, c);
                            mbl.Build(parent, "Dashes", 4);
                        }
                        else if (style == 8)
                        {
                            // Triángulos (dientes de tiburón) a lo largo de la línea.
                            var mbt = new MeshBatch();
                            for (float t = 0f; t + 0.12f <= len; t += 0.2f)
                            {
                                Vector2 a0 = p + dir * t, a1 = p + dir * (t + 0.12f), tip = p + dir * (t + 0.06f) + perp * 0.12f;
                                mbt.Tri(a0, a1, tip, c);
                            }
                            mbt.Build(parent, "Teeth", 4);
                        }
                        else Bar(parent, "Line", p, e, style == 4 ? w * 0.55f : (style == 9 ? w * 2.2f : w), c, 4);
                        return true;
                    }
                case DecorType.StopLine:
                    {
                        Vector2 e = d.End;
                        if (Vector2.Distance(p, e) < 0.1f) return true;
                        Color c = d.color != 0 ? ObjCol(d.color) : new Color(1f, 1f, 1f, 0.97f);
                        if ((int)v % 2 == 1)
                        {
                            Vector2 dd = (e - p).normalized, pp = new Vector2(-dd.y, dd.x);
                            Bar(parent, "Stop", p + pp * 0.04f * s, e + pp * 0.04f * s, 0.05f * s, c, 4);
                            Bar(parent, "Stop", p - pp * 0.04f * s, e - pp * 0.04f * s, 0.05f * s, c, 4);
                        }
                        else Bar(parent, "Stop", p, e, 0.11f * s, c, 4);
                        return true;
                    }
                case DecorType.Sidewalk:
                    {
                        Vector2 e = d.End;
                        if (Vector2.Distance(p, e) < 0.1f) return true;
                        Color c = (int)v % 2 == 0 ? SpriteFactory.Hex("#B9B8B0") : SpriteFactory.Hex("#9A958C");
                        Bar(parent, "SidewalkEdge", p, e, 0.34f * s, Color.Lerp(c, Color.black, 0.35f), 3);
                        Bar(parent, "Sidewalk", p, e, 0.28f * s, c, 3);
                        return true;
                    }
                case DecorType.Median:
                    {
                        Vector2 e = d.End;
                        if (Vector2.Distance(p, e) < 0.1f) return true;
                        int kind = (int)v % 3;
                        Color c = kind == 0 ? SpriteFactory.Hex("#5F9B4A") : (kind == 1 ? SpriteFactory.Hex("#B9B8B0") : SpriteFactory.Hex("#F4C20D"));
                        Bar(parent, "MedianEdge", p, e, 0.3f * s, Color.Lerp(c, Color.black, 0.45f), 3);
                        Bar(parent, "Median", p, e, 0.22f * s, c, 3);
                        if (kind == 2)
                        {
                            Vector2 dd = e - p; float len = dd.magnitude; dd /= len;
                            for (float t = 0f; t < len; t += 0.4f)
                                Bar(parent, "Hazard", p + dd * t, p + dd * Mathf.Min(len, t + 0.2f), 0.22f * s, SpriteFactory.Hex("#2B2B2E"), 3);
                        }
                        return true;
                    }
                case DecorType.Misc:
                    {
                        int kind = (int)v % MiscNames.Length;
                        if (kind >= 4) { DrawMiscMore(parent, kind, p, s, ang, shadow, dark); return true; }
                        RegShadow(SpriteFactory.Make("MiscShadow", parent, SpriteFactory.Circle, shadow, p + new Vector2(0.04f, -0.04f), Vector2.one * 0.32f * s, 3), p);
                        if (kind == 0)
                        {
                            SpriteFactory.Make("ConeBase", parent, SpriteFactory.Square, dark, p, Vector2.one * 0.26f * s, 4, ang);
                            SpriteFactory.Make("Cone", parent, SpriteFactory.Circle, SpriteFactory.Hex("#F26B1D"), p, Vector2.one * 0.22f * s, 5);
                            SpriteFactory.Make("ConeRing", parent, SpriteFactory.Ring, Color.white, p, Vector2.one * 0.14f * s, 6);
                        }
                        else if (kind == 1)
                        {
                            SpriteFactory.Make("Barrel", parent, SpriteFactory.Circle, SpriteFactory.Hex("#2E86C1"), p, Vector2.one * 0.34f * s, 5);
                            SpriteFactory.Make("BarrelTop", parent, SpriteFactory.Ring, new Color(1, 1, 1, 0.8f), p, Vector2.one * 0.24f * s, 6);
                        }
                        else if (kind == 2)
                        {
                            SpriteFactory.Make("Bench", parent, SpriteFactory.Square, new Color(0.5f, 0.36f, 0.22f), p, new Vector2(0.5f, 0.15f) * s, 5, ang);
                            SpriteFactory.Make("BenchBack", parent, SpriteFactory.Square, new Color(0.38f, 0.27f, 0.17f), p + new Vector2(-Mathf.Sin(ang * Mathf.Deg2Rad), Mathf.Cos(ang * Mathf.Deg2Rad)) * 0.09f * s, new Vector2(0.5f, 0.04f) * s, 6, ang);
                        }
                        else
                        {
                            SpriteFactory.Make("Crate", parent, SpriteFactory.Square, SpriteFactory.Hex("#B98B55"), p, Vector2.one * 0.34f * s, 5, ang);
                            SpriteFactory.Make("CrateTop", parent, SpriteFactory.Square, SpriteFactory.Hex("#D4A870"), p, Vector2.one * 0.26f * s, 6, ang);
                        }
                        return true;
                    }
                case DecorType.Boat:
                    {
                        float a = ang;
                        Vector2 f = FromAngle(a);
                        Color hull = d.color == 0 ? Color.white : ObjCol(d.color);
                        SpriteFactory.Make("Wake", parent, SpriteFactory.Circle, new Color(1, 1, 1, 0.25f), p, new Vector2(1.5f, 0.7f) * s, 3, a);
                        RegShadow(SpriteFactory.Make("BoatShadow", parent, SpriteFactory.Square, shadow, p + new Vector2(0.05f, -0.06f), new Vector2(0.9f, 0.3f) * s, 3, a), p);
                        SpriteFactory.Make("Hull", parent, SpriteFactory.Square, hull, p, new Vector2(0.8f, 0.3f) * s, 4, a);
                        SpriteFactory.Make("Bow", parent, SpriteFactory.Triangle, hull, p + f * 0.52f * s, new Vector2(0.3f, 0.3f) * s, 4, a - 90f);
                        SpriteFactory.Make("Deck", parent, SpriteFactory.Square, new Color(0.55f, 0.42f, 0.3f), p - f * 0.05f * s, new Vector2(0.55f, 0.2f) * s, 5, a);
                        SpriteFactory.Make("Cabin", parent, SpriteFactory.Square, new Color(0.9f, 0.92f, 0.95f), p - f * 0.12f * s, new Vector2(0.24f, 0.16f) * s, 6, a);
                        return true;
                    }
                case DecorType.Material:
                    {
                        Vector2 half = new Vector2(Mathf.Abs(d.x2), Mathf.Abs(d.y2)) * s;
                        int kind = (int)v % MaterialColors.Length;
                        Color c = MaterialColors[kind] == "" ? asphalt : SpriteFactory.Hex(MaterialColors[kind]);
                        SpriteFactory.Make("Material", parent, SpriteFactory.Square, c, p, half * 2f, -15);
                        if (kind == 2)
                            for (float yy = -half.y + 0.3f; yy < half.y - 0.1f; yy += 0.45f)
                                SpriteFactory.Make("Wave", parent, SpriteFactory.Square, new Color(1, 1, 1, 0.25f), p + new Vector2(0f, yy), new Vector2(Mathf.Max(0.2f, half.x * 1.4f), 0.03f), -14);
                        return true;
                    }
                case DecorType.Parking:
                    {
                        Vector2 half = new Vector2(Mathf.Abs(d.x2), Mathf.Abs(d.y2)) * s;
                        SpriteFactory.Make("Parking", parent, SpriteFactory.Square, new Color(0.54f, 0.55f, 0.57f), p, half * 2f, -15);
                        var line = new Color(1f, 1f, 1f, 0.85f);
                        SpriteFactory.Make("ParkEdgeA", parent, SpriteFactory.Square, line, p + new Vector2(0f, half.y - 0.03f), new Vector2(half.x * 2f, 0.05f), -14);
                        SpriteFactory.Make("ParkEdgeB", parent, SpriteFactory.Square, line, p - new Vector2(0f, half.y - 0.03f), new Vector2(half.x * 2f, 0.05f), -14);
                        for (float xx = -half.x + 0.5f; xx < half.x - 0.1f; xx += 0.5f)
                            SpriteFactory.Make("Stall", parent, SpriteFactory.Square, line, p + new Vector2(xx, 0f), new Vector2(0.04f, half.y * 2f - 0.1f), -14);
                        return true;
                    }
            }
            return false;
        }

        // ---------------- Puntos lógicos y botones de grupo ----------------

        // ---------------- Paso a nivel, advertencias y cables entre postes ----------------

        class RailLamps { public Vector2 pos; public SpriteRenderer a, b, ga, gb; public float reach; }
        readonly List<RailLamps> railSignLamps = new List<RailLamps>();
        readonly Dictionary<int, SpriteRenderer> detectorWarns = new Dictionary<int, SpriteRenderer>();
        static readonly Color RailLampOff = new Color(0.25f, 0.08f, 0.07f, 1f), WarnOff = new Color(0.45f, 0.4f, 0.25f, 1f);

        void UpdateWarnings()
        {
            if (railSignLamps.Count > 0)
            {
                bool blink = Mathf.Repeat(Time.time * 1.5f, 1f) < 0.5f;
                var trains = IntersectionMaster.Traffic.TrafficSimulationManager.TrainPositions;
                foreach (var r in railSignLamps)
                {
                    if (r.a == null) continue;
                    bool near = false;
                    foreach (var t in trains) if ((t - r.pos).sqrMagnitude < r.reach * r.reach) { near = true; break; }
                    Color on = new Color(1f, 0.15f, 0.1f, 1f);
                    r.a.color = near && blink ? on : RailLampOff;
                    r.b.color = near && !blink ? on : RailLampOff;
                    if (r.ga != null) r.ga.color = new Color(1f, 0.15f, 0.1f, near && blink ? 0.7f : 0f);
                    if (r.gb != null) r.gb.color = new Color(1f, 0.15f, 0.1f, near && !blink ? 0.7f : 0f);
                }
            }
            if (detectorWarns.Count > 0)
            {
                bool blink = Mathf.Repeat(Time.time * 2f, 1f) < 0.6f;
                foreach (var kv in detectorWarns)
                    if (kv.Value != null)
                        kv.Value.color = IntersectionMaster.Traffic.TrafficSimulationManager.OccupiedDetectors.Contains(kv.Key) && blink ? SpriteFactory.Hex("#F6C945") : WarnOff;
            }
        }

        /// <summary>Postes de teléfono con «Cables»: cada uno se une con hilos al poste más cercano (hasta 7 unidades).</summary>
        void PoleCables(MapData map)
        {
            var poles = new List<DecorItem>();
            foreach (var d in map.decor) if (d.type == DecorType.PhonePole && (d.carFlags & 1) != 0) poles.Add(d);
            var done = new HashSet<long>();
            for (int i = 0; i < poles.Count; i++)
            {
                // Los dos vecinos más cercanos (una línea de postes).
                int best = -1, second = -1; float bd = 49f, sd = 49f;
                for (int j = 0; j < poles.Count; j++)
                {
                    if (j == i) continue;
                    float dd = (poles[j].Pos - poles[i].Pos).sqrMagnitude;
                    if (dd < bd) { sd = bd; second = best; bd = dd; best = j; }
                    else if (dd < sd) { sd = dd; second = j; }
                }
                foreach (int j in new[] { best, second })
                {
                    if (j < 0) continue;
                    long key = (long)Mathf.Min(i, j) * 100000 + Mathf.Max(i, j);
                    if (!done.Add(key)) continue;
                    Vector2 a = poles[i].Pos, b = poles[j].Pos;
                    Vector2 dir = (b - a).normalized, perp = new Vector2(-dir.y, dir.x);
                    for (int k = -1; k <= 1; k += 2)
                        Bar(staticRoot, "PoleWire", a + perp * (k * 0.12f), b + perp * (k * 0.12f), 0.01f, new Color(0.1f, 0.1f, 0.12f, 0.7f), 11);
                }
            }
        }

        /// <summary>Lazos de detector dibujados (id del punto → dibujo), para encenderlos cuando hay un vehículo encima.</summary>
        readonly Dictionary<int, SpriteRenderer> detectorLoops = new Dictionary<int, SpriteRenderer>();
        static readonly Color DetectorIdle = new Color(0.1f, 0.1f, 0.12f, 0.35f), DetectorOn = new Color(0.3f, 0.95f, 1f, 0.95f);

        void DrawPointsAndButtons(MapData map, RoadNetworkManager net, bool editorMode)
        {
            Transform parent = roadRoot;
            detectorLoops.Clear();
            detectorWarns.Clear();
            foreach (var pt in map.points)
            {
                NetRoad nr;
                var rd = map.Road(pt.road);
                if (rd == null || !net.roadById.TryGetValue(pt.road, out nr)) continue;
                Vector2 pos = map.PointOnRoad(rd, pt.t);
                float dist;
                float sc = nr.center.Project(pos, out dist);
                Vector2 tan = nr.center.TangentAt(sc);
                Vector2 right = Geo.Right(tan);
                float ang = Mathf.Atan2(tan.y, tan.x) * Mathf.Rad2Deg;
                float hw = nr.halfWidth;
                switch (pt.kind)
                {
                    case PointKind.BusStop:
                        for (int dirIdx = 0; dirIdx < 2; dirIdx++)
                        {
                            if (pt.dir != 2 && pt.dir != dirIdx) continue;
                            if ((dirIdx == 0 ? rd.lanesAB : rd.lanesBA) == 0) continue;
                            Vector2 side = dirIdx == 0 ? right : -right;
                            Vector2 c = pos + side * (hw + CurbWidth + 0.22f);
                            RegShadow(SpriteFactory.Make("StopShadow", parent, SpriteFactory.Square, new Color(0, 0, 0, 0.25f), c + new Vector2(0.04f, -0.04f), new Vector2(0.8f, 0.28f), 14, ang), c);
                            SpriteFactory.Make("StopRoof", parent, SpriteFactory.Square, new Color(0.55f, 0.7f, 0.82f, 0.95f), c, new Vector2(0.78f, 0.26f), 15, ang);
                            SpriteFactory.Make("StopSign", parent, SpriteFactory.Square, SpriteFactory.Hex("#2B6CC4"), c + tan * 0.46f, new Vector2(0.1f, 0.1f), 16, ang);
                            SpriteFactory.Make("StopMark", parent, SpriteFactory.Square, new Color(1f, 0.85f, 0.2f, 0.9f), pos + side * (hw - 0.14f), new Vector2(0.9f, 0.05f), 4, ang);
                        }
                        break;
                    case PointKind.Toll:
                        {
                            // Como el original: pluma roja y blanca que cruza el carril desde un poste con luz roja
                            // (la caseta, si se quiere, se pone aparte con edificios).
                            Vector2 post = pos + right * (hw - 0.03f), tip = pos - right * (hw - 0.06f);
                            RegShadow(Bar(parent, "BoomShadow", post + new Vector2(-0.05f, -0.03f), tip + new Vector2(-0.05f, -0.03f), 0.05f, new Color(0f, 0f, 0f, 0.22f), 3), pos);
                            const int segs = 7;
                            for (int i = 0; i < segs; i++)
                                Bar(parent, "Boom", Vector2.Lerp(post, tip, (float)i / segs), Vector2.Lerp(post, tip, (float)(i + 1) / segs), 0.045f, i % 2 == 0 ? SpriteFactory.Hex("#C62828") : new Color(0.95f, 0.95f, 0.95f), 14);
                            SpriteFactory.Make("BoomPost", parent, SpriteFactory.Square, new Color(0.25f, 0.26f, 0.28f), post, new Vector2(0.08f, 0.08f), 15, ang);
                            SpriteFactory.Make("TollLight", parent, SpriteFactory.Circle, SpriteFactory.Hex("#FF3B30"), post, Vector2.one * 0.05f, 16);
                            SpriteFactory.Make("TollGlow", parent, SpriteFactory.Soft, new Color(1f, 0.15f, 0.12f, 0.45f), post, Vector2.one * 0.75f, 16);
                            break;
                        }
                    case PointKind.Detector:
                        {
                            // Al jugar el lazo se ve (oscuro) y se enciende en celeste mientras un vehículo está encima.
                            var c = editorMode ? new Color(0.2f, 0.85f, 1f, 0.75f) : DetectorIdle;
                            var loop = Bar(parent, "Loop", pos + right * (-hw + 0.03f), pos + right * (hw - 0.03f), editorMode ? 0.07f : 0.05f, c, 5);
                            if (!editorMode && loop != null) detectorLoops[pt.id] = loop;
                            // Icono de advertencia junto al carril: se enciende cuando pasa un vehículo (como el original).
                            Vector2 wp = pos + right * (hw + 0.28f);
                            SpriteFactory.Make("WarnBack", parent, SpriteFactory.Triangle, new Color(0.12f, 0.12f, 0.13f, 0.9f), wp, Vector2.one * 0.26f, 15);
                            var warn = SpriteFactory.Make("Warn", parent, SpriteFactory.Triangle, WarnOff, wp, Vector2.one * 0.2f, 16);
                            SpriteFactory.Make("WarnMark", parent, SpriteFactory.Square, new Color(0.1f, 0.1f, 0.1f, 1f), wp + new Vector2(0f, -0.005f), new Vector2(0.025f, 0.07f), 17);
                            detectorWarns[pt.id] = warn;
                            if (editorMode) SpriteFactory.Make("DetectorMark", parent, SpriteFactory.Square, new Color(0.2f, 0.85f, 1f, 1f), pos, Vector2.one * 0.16f, 17, 45f);
                            break;
                        }
                }
            }

            foreach (var b in map.buttons)
            {
                if (editorMode)
                {
                    foreach (var l in b.links)
                    {
                        var r = map.Road(l.road);
                        if (r == null) continue;
                        Vector2 a = map.PosOf(l.node), o = map.PosOf(l.node == r.a ? r.b : r.a);
                        Vector2 target = Vector2.Lerp(a, o, 0.12f);
                        Bar(parent, "Link", b.Pos, target, 0.035f, new Color(1f, 0.6f, 0.15f, 0.7f), 15);
                        SpriteFactory.Make("LinkEnd", parent, SpriteFactory.Circle, new Color(1f, 0.6f, 0.15f, 0.9f), target, Vector2.one * 0.14f, 16);
                    }
                }
                RegShadow(SpriteFactory.Make("ButtonShadow", parent, SpriteFactory.Circle, new Color(0, 0, 0, 0.3f), b.Pos + new Vector2(0.04f, -0.05f), Vector2.one * 0.62f, 16), b.Pos);
                // Como el original: disco gris oscuro con el dibujo de varios semáforos unidos (botón múltiple).
                SpriteFactory.Make("ButtonBase", parent, SpriteFactory.Circle, new Color(0.3f, 0.34f, 0.32f, 0.95f), b.Pos, Vector2.one * 0.56f, 17);
                SpriteFactory.Make("ButtonRim", parent, SpriteFactory.Ring, new Color(0.85f, 0.88f, 0.86f, 0.9f), b.Pos, Vector2.one * 0.56f, 18);
                var iconC = new Color(0.92f, 0.94f, 0.92f, 1f);
                Vector2[] heads = { new Vector2(-0.11f, 0.06f), new Vector2(0f, -0.05f), new Vector2(0.11f, 0.06f) };
                for (int hi = 0; hi < heads.Length; hi++)
                {
                    SpriteFactory.Make("ButtonLight", parent, SpriteFactory.Square, iconC, b.Pos + heads[hi], new Vector2(0.06f, 0.14f), 19);
                    if (hi > 0) Bar(parent, "ButtonWire", b.Pos + heads[hi - 1], b.Pos + heads[hi], 0.018f, iconC, 19);
                }
            }
        }

        /// <summary>Botón de grupo bajo el punto tocado (o null).</summary>
        public GroupButton FindButtonAt(Vector2 world, float tol)
        {
            return builtMap == null ? null : builtMap.ButtonNear(world, tol);
        }
    
        /// <summary>Más objetos sueltos como los del original: mesa de parque, boca de incendios, contenedor de basura,
        /// ventilador de tejado y cajas (con cintas o grandes).</summary>
        void DrawMiscMore(Transform parent, int kind, Vector2 p, float s, float ang, Color shadow, Color dark)
        {
            Vector2 f = FromAngle(ang), n = new Vector2(-f.y, f.x);
            switch (kind)
            {
                case 4:   // mesa de parque con dos bancos
                    RegShadow(SpriteFactory.Make("TableShadow", parent, SpriteFactory.Square, shadow, p + new Vector2(0.05f, -0.05f), new Vector2(0.5f, 0.42f) * s, 3, ang), p);
                    for (int k = -1; k <= 1; k += 2)
                        SpriteFactory.Make("TableBench", parent, SpriteFactory.Square, new Color(0.42f, 0.3f, 0.19f), p + n * (k * 0.16f * s), new Vector2(0.48f, 0.07f) * s, 5, ang);
                    SpriteFactory.Make("Table", parent, SpriteFactory.Square, new Color(0.55f, 0.4f, 0.25f), p, new Vector2(0.48f, 0.2f) * s, 6, ang);
                    break;
                case 5:   // boca de incendios
                    RegShadow(SpriteFactory.Make("HydShadow", parent, SpriteFactory.Circle, shadow, p + new Vector2(-0.05f, -0.03f), Vector2.one * 0.14f * s, 3), p);
                    SpriteFactory.Make("HydNozzles", parent, SpriteFactory.Square, SpriteFactory.Hex("#A61E1E"), p, new Vector2(0.16f, 0.05f) * s, 5, ang);
                    SpriteFactory.Make("Hydrant", parent, SpriteFactory.Circle, SpriteFactory.Hex("#D62828"), p, Vector2.one * 0.11f * s, 6);
                    SpriteFactory.Make("HydTop", parent, SpriteFactory.Circle, SpriteFactory.Hex("#F05454"), p + new Vector2(-0.01f, 0.01f), Vector2.one * 0.05f * s, 7);
                    break;
                case 6:   // contenedor de basura con tapa
                    RegShadow(SpriteFactory.Make("BinShadow", parent, SpriteFactory.Square, shadow, p + new Vector2(0.05f, -0.05f), new Vector2(0.42f, 0.3f) * s, 3, ang), p);
                    SpriteFactory.Make("Bin", parent, SpriteFactory.Square, SpriteFactory.Hex("#2E7D4F"), p, new Vector2(0.4f, 0.28f) * s, 5, ang);
                    SpriteFactory.Make("BinLid", parent, SpriteFactory.Square, SpriteFactory.Hex("#3E9E66"), p + n * 0.02f * s, new Vector2(0.38f, 0.2f) * s, 6, ang);
                    SpriteFactory.Make("BinHinge", parent, SpriteFactory.Square, dark, p - n * 0.12f * s, new Vector2(0.38f, 0.02f) * s, 7, ang);
                    break;
                case 7:   // ventilador de tejado (para poner sobre los edificios)
                    SpriteFactory.Make("FanBox", parent, SpriteFactory.Square, new Color(0.62f, 0.64f, 0.67f), p, Vector2.one * 0.3f * s, 12, ang);
                    SpriteFactory.Make("FanRing", parent, SpriteFactory.Ring, new Color(0.35f, 0.37f, 0.4f), p, Vector2.one * 0.24f * s, 13);
                    Bar(parent, "FanBlade", p - f * 0.1f * s, p + f * 0.1f * s, 0.03f * s, new Color(0.3f, 0.32f, 0.35f), 14);
                    Bar(parent, "FanBlade", p - n * 0.1f * s, p + n * 0.1f * s, 0.03f * s, new Color(0.3f, 0.32f, 0.35f), 14);
                    break;
                case 8:   // cajas con cintas
                    for (int k = 0; k < 2; k++)
                    {
                        Vector2 q = p + f * ((k - 0.5f) * 0.36f * s);
                        RegShadow(SpriteFactory.Make("CrateShadow", parent, SpriteFactory.Square, shadow, q + new Vector2(0.04f, -0.04f), Vector2.one * 0.32f * s, 3, ang), q);
                        SpriteFactory.Make("Crate", parent, SpriteFactory.Square, SpriteFactory.Hex("#B98B55"), q, Vector2.one * 0.32f * s, 5, ang);
                        SpriteFactory.Make("Strap", parent, SpriteFactory.Square, SpriteFactory.Hex("#2B2B2E"), q, new Vector2(0.04f, 0.33f) * s, 6, ang);
                        SpriteFactory.Make("Strap2", parent, SpriteFactory.Square, SpriteFactory.Hex("#2B2B2E"), q, new Vector2(0.33f, 0.04f) * s, 6, ang);
                    }
                    break;
                case 10:  // bolardo: poste bajo con franja reflectante
                    RegShadow(SpriteFactory.Make("BollardShadow", parent, SpriteFactory.Circle, shadow, p + new Vector2(-0.04f, -0.02f), Vector2.one * 0.1f * s, 3), p);
                    SpriteFactory.Make("Bollard", parent, SpriteFactory.Circle, new Color(0.3f, 0.31f, 0.33f), p, Vector2.one * 0.09f * s, 6);
                    SpriteFactory.Make("BollardBand", parent, SpriteFactory.Ring, SpriteFactory.Hex("#F6C945"), p, Vector2.one * 0.07f * s, 7);
                    break;
                default:  // cajas grandes
                    RegShadow(SpriteFactory.Make("CrateShadow", parent, SpriteFactory.Square, shadow, p + new Vector2(0.06f, -0.06f), new Vector2(0.75f, 0.55f) * s, 3, ang), p);
                    SpriteFactory.Make("BigCrate", parent, SpriteFactory.Square, SpriteFactory.Hex("#A9794A"), p, new Vector2(0.72f, 0.52f) * s, 5, ang);
                    SpriteFactory.Make("BigCrateTop", parent, SpriteFactory.Square, SpriteFactory.Hex("#C99A64"), p, new Vector2(0.62f, 0.42f) * s, 6, ang);
                    Bar(parent, "BigCrateX", p - f * 0.3f * s - n * 0.2f * s, p + f * 0.3f * s + n * 0.2f * s, 0.03f * s, SpriteFactory.Hex("#8A6038"), 7);
                    break;
            }
        }
}
}
