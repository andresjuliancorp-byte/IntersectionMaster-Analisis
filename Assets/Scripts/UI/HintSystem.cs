using System;
using UnityEngine;
using UnityEngine.UI;
using IntersectionMaster.Core;
using IntersectionMaster.Systems;
using IntersectionMaster.Traffic;

namespace IntersectionMaster.UI
{
    /// <summary>
    /// Tutoriales en contexto (como el original): un aviso corto la primera vez que se usa algo. Se pueden desactivar o
    /// volver a ver desde Ajustes. Textos propios.
    /// </summary>
    public partial class MobileUIController
    {
        public static readonly string[][] Hints =
        {
            new[] { "map", "Mueve el mapa con un dedo y acércate o aléjate separando o juntando dos dedos." },
            new[] { "lights", "Al tocar un semáforo cambia entre rojo y verde. También puedes deslizar el dedo sobre varios para cambiarlos a la vez." },
            new[] { "goal", "Objetivo: que pasen suficientes vehículos antes de que acabe el tiempo (arriba ves cuántos faltan y el reloj). Te penalizan las esperas largas y los choques." },
            new[] { "highscore", "Puntuación más alta: consigue todos los puntos que puedas. Con el tiempo llegan más coches y más impacientes; si alguien espera demasiado o hay un choque, se acaba." },
            new[] { "counter", "Contador de puntuación: tienes 6 minutos para sumar todos los puntos que puedas. Cada vehículo que llega suma; los choques y las esperas largas restan." },
            new[] { "unlimited", "Sin límite: no hay tiempo ni la partida se acaba por los choques. Es como la simulación, pero los conductores son más temerarios." },
            new[] { "sim", "Simulación: los semáforos van solos y los conductores cumplen las normas. Puedes jugar con los semáforos o mirar cómo funciona el cruce." },
            new[] { "crash", "¡Ha habido un accidente! Los vehículos siniestrados desaparecen al ratito." },
            new[] { "stop", "Toca un vehículo para pararlo; vuelve a tocarlo para que siga. Muy útil después de un accidente." },
            new[] { "info", "Toca un vehículo para ver adónde va; así sabes hacia qué salida tiende el tráfico." },
            new[] { "time", "Aquí ves la hora del juego. Tócala para cambiarla: ¡viajar en el tiempo existe!" },
            new[] { "ctrl", "Controlador de semáforos: crea un ciclo automático con los tiempos de cada fase." },
            new[] { "button", "Botón para varios semáforos: ponlo en el mapa y vincúlalo a los semáforos que quieras cambiar a la vez." },
            new[] { "editor", "Editor: elige una herramienta abajo. Construir dibuja carriles; mantén pulsado un nodo para moverlo." },
            new[] { "decor", "Decoración: toca para poner el objeto; tócalo otra vez para elegirlo y arrastrarlo." },
            new[] { "rain", "Está lloviendo: los vehículos van más despacio y frenan antes." },
            new[] { "event", "Evento: a veces pasan cosas en la ciudad (averías, obras, emergencias…). Están en Ajustes de mapa → General." },
        };

        RectTransform hintRoot;

        static string HintText(string key)
        {
            foreach (var h in Hints) if (h[0] == key) return h[1];
            return null;
        }

        /// <summary>Muestra el aviso una sola vez (si los tutoriales están activados).</summary>
        public void ShowHint(string key)
        {
            var p = SaveSystem.Progress;
            if (!p.hintsEnabled || p.hintsSeen.Contains(key) || hintRoot != null) return;
            string text = HintText(key);
            if (text == null) return;
            p.hintsSeen.Add(key);
            SaveSystem.SaveProgress();
            var box = UIKit.Panel(safe, "Hint", new Color(0.1f, 0.11f, 0.13f, 0.94f));
            hintRoot = box.rectTransform;
            hintRoot.anchorMin = new Vector2(0.5f, 0f); hintRoot.anchorMax = new Vector2(0.5f, 0f);
            hintRoot.pivot = new Vector2(0.5f, 0f);
            hintRoot.sizeDelta = new Vector2(640f, 190f);
            hintRoot.anchoredPosition = new Vector2(0f, 150f);
            var bar = UIKit.Panel(box.transform, "Bar", UIKit.Orange);
            Place(bar.rectTransform, 0, 0, 0, 1, 0, 0, -8, 0);
            var t = UIKit.Label(box.transform, text, 24, TextAnchor.MiddleLeft, Color.white);
            UIKit.Stretch(t.rectTransform, 22, 70, 16, 12);
            var ok = UIKit.Button(box.transform, "Entendido", UIKit.Orange, () => { AudioManager.Instance.PlayClick(); CloseHint(); }, 22);
            var ort = ok.GetComponent<RectTransform>();
            ort.anchorMin = ort.anchorMax = new Vector2(1f, 0f);
            ort.pivot = new Vector2(1f, 0f);
            ort.sizeDelta = new Vector2(170f, 54f);
            ort.anchoredPosition = new Vector2(-12f, 10f);
            hintTimer = 14f;
        }

        float hintTimer;

        void CloseHint()
        {
            if (hintRoot != null) Destroy(hintRoot.gameObject);
            hintRoot = null;
        }

        /// <summary>Avisos que dependen de lo que pasa en la partida (se mira unas veces por segundo).</summary>
        void UpdateHints()
        {
            if (hintRoot != null)
            {
                hintTimer -= Time.unscaledDeltaTime;
                if (hintTimer <= 0f) CloseHint();
                return;
            }
            if (game.Screen == AppScreen.Playing && game.sim.Running && !game.TutorialActive)
            {
                if (game.sim.Scoring.accidents > 0) ShowHint("crash");
                else if (game.sim.Raining) ShowHint("rain");
                else if (game.sim.EventCount > 0) ShowHint("event");
            }
        }

        /// <summary>Avisos al empezar una partida, según el modo.</summary>
        void HintsOnPlayStart()
        {
            if (game.TutorialActive) return;
            var p = SaveSystem.Progress;
            string first = null;
            foreach (var k in new[] { "map", "lights", ModeHintKey(), "time" })
                if (k != null && !p.hintsSeen.Contains(k)) { first = k; break; }
            if (first != null) ShowHint(first);
        }

        string ModeHintKey()
        {
            switch (game.levels.Mode)
            {
                case GameMode.Infinite: return "highscore";
                case GameMode.ScoreCounter: return "counter";
                case GameMode.Free: return game.PlayMap != null && game.PlayMap.playMode == 1 ? "sim" : "unlimited";
                case GameMode.Campaign: case GameMode.Challenge:
                    return game.PlayMap != null && game.PlayMap.playMode == 1 ? "sim" : "goal";
            }
            return null;
        }
    }
}
