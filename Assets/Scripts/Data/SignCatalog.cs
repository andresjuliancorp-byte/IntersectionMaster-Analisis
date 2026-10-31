using UnityEngine;

namespace IntersectionMaster.Data
{
    /// <summary>
    /// Catálogo de señales de tráfico decorativas (Decoración → Señal de tráfico). Son pictogramas originales dibujados
    /// por código con formas simples; solo decoran, no cambian el tráfico (para eso están Stop/Ceda en el nodo).
    /// </summary>
    public static class SignCatalog
    {
        public enum Shape { Circle, Triangle, InvTriangle, Square, Octagon, Diamond }

        public struct Def
        {
            public string name;
            public Shape shape;
            public Color32 bg, border, ink;
            public string symbol;   // pictograma de IconKit (sym_*) o null
            public string text;     // texto (números, STOP, P) o null
            public bool slash;      // barra roja de prohibición

            public Def(string name, Shape shape, Color32 bg, Color32 border, Color32 ink, string symbol, string text = null, bool slash = false)
            {
                this.name = name; this.shape = shape; this.bg = bg; this.border = border; this.ink = ink;
                this.symbol = symbol; this.text = text; this.slash = slash;
            }
        }

        static readonly Color32 W = new Color32(250, 250, 250, 255);
        static readonly Color32 R = new Color32(214, 40, 40, 255);
        static readonly Color32 K = new Color32(25, 25, 28, 255);
        static readonly Color32 B = new Color32(32, 98, 196, 255);
        static readonly Color32 Y = new Color32(244, 194, 13, 255);
        static readonly Color32 G = new Color32(30, 130, 70, 255);

        public static readonly Def[] Defs =
        {
            new Def("Stop", Shape.Octagon, R, W, W, null, "STOP"),
            new Def("Ceda el paso", Shape.InvTriangle, W, R, K, null),
            new Def("Prohibido el paso", Shape.Circle, R, R, W, "sym_bar"),
            new Def("Velocidad 20", Shape.Circle, W, R, K, null, "20"),
            new Def("Velocidad 30", Shape.Circle, W, R, K, null, "30"),
            new Def("Velocidad 40", Shape.Circle, W, R, K, null, "40"),
            new Def("Velocidad 50", Shape.Circle, W, R, K, null, "50"),
            new Def("Velocidad 60", Shape.Circle, W, R, K, null, "60"),
            new Def("Velocidad 80", Shape.Circle, W, R, K, null, "80"),
            new Def("Velocidad 100", Shape.Circle, W, R, K, null, "100"),
            new Def("Velocidad 120", Shape.Circle, W, R, K, null, "120"),
            new Def("Prohibido girar\na la izquierda", Shape.Circle, W, R, K, "sym_left", null, true),
            new Def("Prohibido girar\na la derecha", Shape.Circle, W, R, K, "sym_right", null, true),
            new Def("Prohibido\ncambio de sentido", Shape.Circle, W, R, K, "sym_uturn", null, true),
            new Def("Prohibido\ncamiones", Shape.Circle, W, R, K, "sym_truck"),
            new Def("Prohibido\nbicicletas", Shape.Circle, W, R, K, "sym_bike"),
            new Def("Prohibido\npeatones", Shape.Circle, W, R, K, "sym_walk"),
            new Def("Prohibido\naparcar", Shape.Circle, B, R, R, null, null, true),
            new Def("Sentido\nobligatorio recto", Shape.Circle, B, B, W, "sym_up"),
            new Def("Obligatorio\nizquierda", Shape.Circle, B, B, W, "sym_left"),
            new Def("Obligatorio\nderecha", Shape.Circle, B, B, W, "sym_right"),
            new Def("Rotonda", Shape.Circle, B, B, W, "sym_round"),
            new Def("Carril bici", Shape.Circle, B, B, W, "sym_bike"),
            new Def("Camino\npeatonal", Shape.Circle, B, B, W, "sym_walk"),
            new Def("Peligro", Shape.Triangle, W, R, K, "sym_excl"),
            new Def("Curva a la\nizquierda", Shape.Triangle, W, R, K, "sym_curveL"),
            new Def("Curva a la\nderecha", Shape.Triangle, W, R, K, "sym_curveR"),
            new Def("Semáforo", Shape.Triangle, W, R, K, "sym_signal"),
            new Def("Peatones", Shape.Triangle, W, R, K, "sym_walk"),
            new Def("Obras", Shape.Triangle, W, R, K, "sym_work"),
            new Def("Paso a nivel", Shape.Triangle, W, R, K, "sym_train"),
            new Def("Estrechamiento", Shape.Triangle, W, R, K, "sym_narrow"),
            new Def("Rotonda\npróxima", Shape.Triangle, W, R, K, "sym_round"),
            new Def("Aparcamiento", Shape.Square, B, W, W, null, "P"),
            new Def("Paso de\npeatones", Shape.Square, B, W, W, "sym_walk"),
            new Def("Parada de\nautobús", Shape.Square, B, W, W, "sym_bus"),
            new Def("Sentido único", Shape.Square, B, W, W, "sym_up"),
            new Def("Calle sin\nsalida", Shape.Square, B, W, W, "sym_deadend"),
            new Def("Autopista", Shape.Square, G, W, W, "sym_highway"),
            new Def("Calle con\nprioridad", Shape.Diamond, Y, W, Y, null),
            new Def("Velocidad 70", Shape.Circle, W, R, K, null, "70"),
            new Def("Velocidad 90", Shape.Circle, W, R, K, null, "90"),
            new Def("Velocidad 110", Shape.Circle, W, R, K, null, "110"),
            new Def("Fin de\nlimitación", Shape.Circle, W, K, K, "sym_endslash"),
            new Def("Prohibido\nadelantar", Shape.Circle, W, R, K, "sym_twocars"),
            new Def("Prohibido\nmotos", Shape.Circle, W, R, K, "sym_moto"),
            new Def("Prohibido\nautobuses", Shape.Circle, W, R, K, "sym_bus"),
            new Def("Prohibido\nparar", Shape.Circle, B, R, R, "sym_xbar"),
            new Def("Prohibido\nrecto", Shape.Circle, W, R, K, "sym_up", null, true),
            new Def("Velocidad\nmínima 30", Shape.Circle, B, B, W, null, "30"),
            new Def("Recto o\nizquierda", Shape.Circle, B, B, W, "sym_upleft"),
            new Def("Recto o\nderecha", Shape.Circle, B, B, W, "sym_upright"),
            new Def("Izquierda o\nderecha", Shape.Circle, B, B, W, "sym_leftright"),
            new Def("Paso por la\nderecha", Shape.Circle, B, B, W, "sym_keepright"),
            new Def("Paso por la\nizquierda", Shape.Circle, B, B, W, "sym_keepleft"),
            new Def("Niños", Shape.Triangle, W, R, K, "sym_children"),
            new Def("Animales", Shape.Triangle, W, R, K, "sym_deer"),
            new Def("Pavimento\ndeslizante", Shape.Triangle, W, R, K, "sym_slip"),
            new Def("Resalto", Shape.Triangle, W, R, K, "sym_bump"),
            new Def("Doble\nsentido", Shape.Triangle, W, R, K, "sym_twoway"),
            new Def("Incorporación", Shape.Triangle, W, R, K, "sym_merge"),
            new Def("Ciclistas", Shape.Triangle, W, R, K, "sym_bike"),
            new Def("Cruce", Shape.Triangle, W, R, K, "sym_cross"),
            new Def("Curvas\npeligrosas", Shape.Triangle, W, R, K, "sym_zigzag"),
            new Def("Pendiente", Shape.Triangle, W, R, K, "sym_hill"),
            new Def("Prioridad\nen cruce", Shape.Triangle, W, R, K, "sym_priocross"),
            new Def("Hospital", Shape.Square, B, W, W, null, "H"),
            new Def("Información", Shape.Square, B, W, W, null, "I"),
            new Def("Túnel", Shape.Square, B, W, W, "sym_tunnel"),
            new Def("Calle\npeatonal", Shape.Square, B, W, W, "sym_walk"),
            new Def("Dirección\nizquierda", Shape.Square, B, W, W, "sym_left"),
            new Def("Dirección\nderecha", Shape.Square, B, W, W, "sym_right"),
            new Def("Salida", Shape.Square, G, W, W, "sym_exitramp"),
            new Def("Fin de\nautopista", Shape.Square, G, W, W, "sym_highway", null, true),
            new Def("Curva a la\nizquierda (panel)", Shape.Square, Y, K, K, "sym_chevL"),
            new Def("Curva a la\nderecha (panel)", Shape.Square, Y, K, K, "sym_chevR"),
            new Def("Fin de\nprioridad", Shape.Diamond, Y, W, K, "sym_endslash"),
        };

        public static int Count { get { return Defs.Length; } }

        public static int Clamp(int i) { return Mathf.Clamp(i, 0, Defs.Length - 1); }

        public static string SpriteName(int i) { return "sign_" + Clamp(i); }
    }
}
