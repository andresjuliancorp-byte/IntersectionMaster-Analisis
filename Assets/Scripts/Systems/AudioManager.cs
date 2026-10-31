using System.Collections.Generic;
using UnityEngine;

namespace IntersectionMaster.Systems
{
    /// <summary>
    /// Sonidos generados por código (sin archivos de audio): efectos puntuales y un ambiente de ciudad
    /// (rumor de tráfico y pájaros) que se oye durante la partida y sube con la cantidad de vehículos.
    /// Se pueden reemplazar por clips reales sin tocar el resto del juego.
    /// </summary>
    public class AudioManager : MonoBehaviour
    {
        static AudioManager instance;
        public static AudioManager Instance
        {
            get
            {
                if (instance == null)
                {
                    var go = new GameObject("AudioManager");
                    instance = go.AddComponent<AudioManager>();
                    DontDestroyOnLoad(go);
                }
                return instance;
            }
        }

        public bool Muted;
        /// <summary>Si el jugador quiere oír el sonido ambiente (opción).</summary>
        public bool AmbientEnabled = true;

        AudioSource src, amb, mus;
        /// <summary>Música relajante generada por código (Ajustes → Música).</summary>
        public bool MusicEnabled;
        AudioClip ding, crash, click, win, lose, horn, ambience;
        bool ambientActive;
        float ambientTarget = 0.1f;

        void Awake()
        {
            src = gameObject.AddComponent<AudioSource>();
            src.playOnAwake = false;
            amb = gameObject.AddComponent<AudioSource>();
            amb.playOnAwake = false;
            amb.loop = true;
            amb.volume = 0f;

            ding = Tone("ding", 0.12f, 1100f, 1500f, 0.18f, false);
            click = Tone("click", 0.05f, 700f, 700f, 0.2f, false);
            crash = Tone("crash", 0.4f, 0f, 0f, 0.4f, true);
            win = Tone("win", 0.5f, 600f, 1200f, 0.25f, false);
            lose = Tone("lose", 0.5f, 400f, 150f, 0.25f, false);
            horn = Tone("horn", 0.28f, 360f, 340f, 0.2f, false);
            ambience = MakeAmbience();
            amb.clip = ambience;
        }

        public void PlayDelivered() { Play(ding, 0.35f); }
        public void PlayCrash() { PlayCrash(false); }
        /// <summary>Choque: leve (crujido corto) o fuerte (golpe grave + cristales) si hay un vehículo grande.</summary>
        public void PlayCrash(bool heavy)
        {
            // 7 choques distintos (4 leves y 3 fuertes), como el original.
            if (crashLight == null)
            {
                crashLight = new AudioClip[4]; crashHeavy = new AudioClip[3];
                for (int i = 0; i < 4; i++) crashLight[i] = MakeCrash(false, 13 + i * 7, 0.45f + i * 0.06f);
                for (int i = 0; i < 3; i++) crashHeavy[i] = MakeCrash(true, 31 + i * 11, 0.9f + i * 0.15f);
            }
            Play(heavy ? crashHeavy[Random.Range(0, 3)] : crashLight[Random.Range(0, 4)], heavy ? 0.9f : 0.75f);
        }
        AudioClip[] crashLight, crashHeavy;

        /// <summary>Estrellas al terminar un nivel: una, dos o tres campanadas que suben.</summary>
        public void PlayStars(int n)
        {
            if (stars == null) { stars = new AudioClip[3]; for (int i = 0; i < 3; i++) stars[i] = Chime("star" + i, 660f * Mathf.Pow(1.26f, i)); }
            for (int i = 0; i < Mathf.Clamp(n, 0, 3); i++) StartCoroutine(PlayLater(stars[i], 0.45f, 0.35f + i * 0.32f));
        }
        AudioClip[] stars;

        System.Collections.IEnumerator PlayLater(AudioClip c, float vol, float delay)
        {
            yield return new WaitForSecondsRealtime(delay);
            Play(c, vol);
        }

        static AudioClip Chime(string name, float f)
        {
            float seconds = 0.7f;
            int n = Mathf.CeilToInt(seconds * Rate);
            var d = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / Rate;
                float env = Mathf.Exp(-t * 5f) * Mathf.Clamp01(t / 0.005f);
                d[i] = (Mathf.Sin(2f * Mathf.PI * f * t) + 0.4f * Mathf.Sin(2f * Mathf.PI * f * 2.01f * t) + 0.15f * Mathf.Sin(2f * Mathf.PI * f * 3f * t)) * 0.3f * env;
            }
            return Clip(name, d);
        }

        // ---------------- Tren ----------------

        AudioSource trainSrc, bellSrc;
        AudioClip trainHorn;
        float lastTrainHorn = -99f;

        /// <summary>Bocina del tren (al aparecer o acercarse a un paso a nivel); como mucho una cada 8 s.</summary>
        public void PlayTrainHorn()
        {
            if (Time.unscaledTime - lastTrainHorn < 8f) return;
            lastTrainHorn = Time.unscaledTime;
            if (trainHorn == null) trainHorn = MakeHorn("trainHorn", 1.4f, 233f, 277f);
            Play(trainHorn, 0.35f);
        }

        /// <summary>Rumor de los trenes en marcha (0 = ninguno) y campana del paso a nivel (mientras un tren se acerca).</summary>
        public void SetTrains(int running, bool crossingBell)
        {
            if (running > 0 && trainSrc == null) trainSrc = Loop(MakeTrainRumble());
            if (crossingBell && bellSrc == null) bellSrc = Loop(MakeBell());
            trainTarget = running > 0 ? Mathf.Min(0.3f, 0.14f + running * 0.05f) : 0f;
            bellTarget = crossingBell ? 0.25f : 0f;
        }
        float trainTarget, bellTarget;

        AudioSource Loop(AudioClip c)
        {
            var a = gameObject.AddComponent<AudioSource>();
            a.playOnAwake = false; a.loop = true; a.volume = 0f; a.clip = c;
            return a;
        }

        void UpdateLoops()
        {
            bool audible = ambientActive && !Muted;
            foreach (var kv in new[] { new KeyValuePair<AudioSource, float>(trainSrc, trainTarget), new KeyValuePair<AudioSource, float>(bellSrc, bellTarget) })
            {
                var a = kv.Key;
                if (a == null) continue;
                float goal = audible ? kv.Value : 0f;
                a.volume = Mathf.MoveTowards(a.volume, goal, Time.unscaledDeltaTime * 0.6f);
                if (goal > 0f && !a.isPlaying) a.Play();
                else if (goal <= 0f && a.volume <= 0.001f && a.isPlaying) a.Pause();
            }
        }

        static AudioClip MakeTrainRumble()
        {
            float seconds = 2f;
            int n = Mathf.CeilToInt(seconds * Rate);
            var d = new float[n];
            var rnd = new System.Random(77);
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / Rate;
                float w = (float)rnd.NextDouble() * 2f - 1f;
                lp += 0.04f * (w - lp);
                // Golpeteo de las ruedas en las juntas: dos golpes cada medio segundo.
                float ph = Mathf.Repeat(t * 2f, 1f);
                float click = Mathf.Exp(-ph * 40f) + Mathf.Exp(-Mathf.Repeat(ph - 0.18f, 1f) * 40f);
                d[i] = lp * 3f + click * 0.25f * (float)(rnd.NextDouble() - 0.5);
            }
            return Clip("trainRun", d);
        }

        static AudioClip MakeBell()
        {
            float seconds = 1f;   // dos campanadas por segundo
            int n = Mathf.CeilToInt(seconds * Rate);
            var d = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / Rate, ph = Mathf.Repeat(t * 2f, 1f) / 2f;
                float env = Mathf.Exp(-ph * 14f);
                d[i] = (Mathf.Sin(2f * Mathf.PI * 1250f * t) + 0.5f * Mathf.Sin(2f * Mathf.PI * 2500f * t)) * 0.22f * env;
            }
            return Clip("bell", d);
        }
        public void PlayClick() { Play(click, 0.5f); }
        public void PlayWin() { Play(win, 0.7f); }
        public void PlayLose() { Play(lose, 0.7f); }
        public void PlayHorn() { PlayHorn(false); }
        /// <summary>Pitido: tres bocinas de coche distintas, o la de camión/autobús (grave y larga).</summary>
        public void PlayHorn(bool heavy)
        {
            if (horns == null)
            {
                // Como el original: 10 bocinas de coche distintas (dos notas, timbre y largo distintos) y 2 de camión.
                float[,] car = { { 415f, 520f, .30f }, { 350f, 440f, .22f }, { 470f, 590f, .36f }, { 392f, 494f, .26f }, { 440f, 554f, .18f },
                                 { 330f, 415f, .34f }, { 500f, 630f, .24f }, { 370f, 466f, .40f }, { 455f, 540f, .20f }, { 310f, 392f, .28f } };
                horns = new AudioClip[12];
                for (int i = 0; i < 10; i++) horns[i] = MakeHorn("horn" + i, car[i, 2], car[i, 0], car[i, 1]);
                horns[10] = MakeHorn("hornT0", 0.65f, 180f, 225f);
                horns[11] = MakeHorn("hornT1", 0.8f, 150f, 200f);
            }
            if (Time.unscaledTime - lastHorn < 0.35f) return;
            lastHorn = Time.unscaledTime;
            Play(heavy ? horns[10 + Random.Range(0, 2)] : horns[Random.Range(0, 10)], heavy ? 0.4f : 0.32f);
        }
        AudioClip[] horns;
        float lastHorn = -9f, lastSiren = -99f;

        /// <summary>Sirena (sube y baja) al entrar una emergencia; como mucho una cada 6 s.</summary>
        public void PlaySiren()
        {
            if (Time.unscaledTime - lastSiren < 6f) return;
            lastSiren = Time.unscaledTime;
            if (siren == null) siren = MakeSiren();
            Play(siren, 0.22f);
        }
        AudioClip siren, penalty;

        /// <summary>Penalización (vehículo atascado demasiado tiempo): dos notas que bajan.</summary>
        public void PlayPenalty()
        {
            if (penalty == null) penalty = Tone("penalty", 0.35f, 520f, 260f, 0.22f, false);
            Play(penalty, 0.5f);
        }
        /// <summary>Editor: «plop» al poner algo y otro más grave al quitarlo (como el original).</summary>
        public void PlayPlop() { if (plop == null) plop = Tone("plop", 0.09f, 520f, 900f, 0.3f, false); Play(plop, 0.45f); }
        public void PlayUnplop() { if (unplop == null) unplop = Tone("unplop", 0.1f, 700f, 300f, 0.3f, false); Play(unplop, 0.45f); }
        AudioClip plop, unplop;

        void Play(AudioClip c, float volume)
        {
            if (Muted || c == null || src == null) return;
            src.PlayOneShot(c, volume);
        }

        // ---------------- Ambiente ----------------

        /// <summary>Activa el ambiente de ciudad (al empezar una partida).</summary>
        public void StartAmbient()
        {
            ambientActive = true;
            if (amb != null && !amb.isPlaying) amb.Play();
        }

        public void StopAmbient()
        {
            ambientActive = false;
        }

        /// <summary>El rumor del tráfico crece con el número de vehículos en pantalla.</summary>
        public void SetTrafficLevel(int vehicles)
        {
            ambientTarget = 0.07f + Mathf.Min(vehicles, 24) * 0.008f;
        }

        // ---------------- Música del jugador ----------------

        /// <summary>Carpeta donde el jugador pone sus canciones (.ogg, .mp3 o .wav). Si hay alguna, suena en lugar de la melodía propia.</summary>
        public static string MusicFolder { get { return System.IO.Path.Combine(Application.persistentDataPath, "music"); } }
        /// <summary>Créditos que se muestran en Ajustes cuando suena música del jugador (p. ej. de Kevin MacLeod).</summary>
        public static string MusicCredits
        {
            get
            {
                try
                {
                    string f = System.IO.Path.Combine(MusicFolder, "creditos.txt");
                    if (System.IO.File.Exists(f)) return System.IO.File.ReadAllText(f).Trim();
                }
                catch { }
                return UserTracks().Length > 0
                    ? "Música: Kevin MacLeod (incompetech.com)\nLicencia Creative Commons: By Attribution 4.0" : "";
            }
        }

        static string[] UserTracks()
        {
            try
            {
                string dir = MusicFolder;
                if (!System.IO.Directory.Exists(dir)) { System.IO.Directory.CreateDirectory(dir); return new string[0]; }
                var list = new System.Collections.Generic.List<string>();
                foreach (var f in System.IO.Directory.GetFiles(dir))
                {
                    string e = System.IO.Path.GetExtension(f).ToLowerInvariant();
                    if (e == ".ogg" || e == ".mp3" || e == ".wav") list.Add(f);
                }
                list.Sort(System.StringComparer.OrdinalIgnoreCase);
                return list.ToArray();
            }
            catch { return new string[0]; }
        }

        string[] tracks;
        int trackIdx = -1;
        bool loadingTrack;

        static AudioType TypeOf(string f)
        {
            string e = System.IO.Path.GetExtension(f).ToLowerInvariant();
            return e == ".mp3" ? AudioType.MPEG : e == ".wav" ? AudioType.WAV : AudioType.OGGVORBIS;
        }

        System.Collections.IEnumerator LoadTrack(string file)
        {
            loadingTrack = true;
            string url = "file://" + file;
            using (var req = UnityEngine.Networking.UnityWebRequestMultimedia.GetAudioClip(url, TypeOf(file)))
            {
                yield return req.SendWebRequest();
                if (req.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
                {
                    var clip = UnityEngine.Networking.DownloadHandlerAudioClip.GetContent(req);
                    if (clip != null && mus != null)
                    {
                        if (mus.clip != null && mus.clip.name != "music") Destroy(mus.clip);
                        mus.clip = clip;
                        mus.loop = tracks.Length == 1;
                        mus.Play();
                    }
                }
                else Debug.LogWarning("No se pudo cargar la canción " + file + ": " + req.error);
            }
            loadingTrack = false;
        }

        void NextTrack()
        {
            if (tracks == null || tracks.Length == 0 || loadingTrack) return;
            trackIdx = (trackIdx + 1) % tracks.Length;
            StartCoroutine(LoadTrack(tracks[trackIdx]));
        }

        void UpdateMusic()
        {
            bool want = MusicEnabled && !Muted;
            if (want && mus == null)
            {
                tracks = UserTracks();
                if (tracks.Length > 0)
                {
                    mus = gameObject.AddComponent<AudioSource>();
                    mus.playOnAwake = false;
                    mus.volume = 0f;
                    trackIdx = Random.Range(0, tracks.Length) - 1;
                    NextTrack();
                    return;
                }
                mus = gameObject.AddComponent<AudioSource>();
                mus.playOnAwake = false;
                mus.loop = true;
                mus.volume = 0f;
                mus.clip = MakeMusic();
                mus.Play();
            }
            if (mus == null) return;
            // Música del jugador: al acabar una canción pasa a la siguiente.
            if (want && tracks != null && tracks.Length > 1 && !loadingTrack && mus.clip != null && !mus.isPlaying && mus.time <= 0f) NextTrack();
            mus.volume = Mathf.MoveTowards(mus.volume, want ? 0.22f : 0f, Time.unscaledDeltaTime * 0.3f);
            if (!want && mus.volume <= 0.001f && mus.isPlaying) mus.Pause();
            else if (want && !mus.isPlaying) mus.UnPause();
        }

        /// <summary>
        /// 32 segundos que se repiten: acordes suaves (Do, La menor, Fa, Sol) con un arpegio lento de campanitas.
        /// Melodía propia, sin derechos de autor.
        /// </summary>
        static AudioClip MakeMusic()
        {
            const int rate = 22050;
            const float bar = 4f;
            int bars = 8;
            int n = Mathf.CeilToInt(bar * bars * rate);
            var data = new float[n];
            // Notas (Hz) de cada acorde: raíz, tercera, quinta.
            float[][] chords =
            {
                new[] { 130.81f, 164.81f, 196.00f }, new[] { 110.00f, 130.81f, 164.81f },
                new[] { 87.31f, 110.00f, 130.81f }, new[] { 98.00f, 123.47f, 146.83f },
                new[] { 130.81f, 164.81f, 196.00f }, new[] { 110.00f, 130.81f, 164.81f },
                new[] { 87.31f, 110.00f, 130.81f }, new[] { 98.00f, 123.47f, 146.83f }
            };
            for (int b = 0; b < bars; b++)
            {
                var ch = chords[b];
                int start = (int)(b * bar * rate), len = (int)(bar * rate);
                // Colchón: senos suaves con ataque y caída lentos.
                for (int i = 0; i < len && start + i < n; i++)
                {
                    float t = (float)i / rate;
                    float env = Mathf.Clamp01(t / 0.8f) * Mathf.Clamp01((bar - t) / 0.8f);
                    float v = 0f;
                    for (int k = 0; k < 3; k++) v += Mathf.Sin(2f * Mathf.PI * ch[k] * 2f * t) * 0.07f;
                    v += Mathf.Sin(2f * Mathf.PI * ch[0] * t) * 0.06f;
                    data[start + i] += v * env;
                }
                // Arpegio: ocho notas por compás, una octava más arriba, con caída rápida (campanita).
                for (int step = 0; step < 8; step++)
                {
                    float f = ch[(step * 2 + b) % 3] * 4f * (step == 7 ? 2f : 1f);
                    int s0 = start + (int)(step * bar / 8f * rate);
                    int l = (int)(0.9f * rate);
                    for (int i = 0; i < l && s0 + i < n; i++)
                    {
                        float t = (float)i / rate;
                        float env = Mathf.Exp(-t * 4.5f) * Mathf.Clamp01(t / 0.01f);
                        data[s0 + i] += (Mathf.Sin(2f * Mathf.PI * f * t) + 0.3f * Mathf.Sin(2f * Mathf.PI * f * 2f * t)) * env * 0.05f;
                    }
                }
            }
            for (int i = 0; i < n; i++) data[i] = Mathf.Clamp(data[i], -0.9f, 0.9f);
            var clip = AudioClip.Create("music", n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        // ---------------- Lluvia y truenos ----------------

        AudioSource rainSrc;
        bool rainOn;
        float nextThunder;
        AudioClip thunder;

        /// <summary>Lluvia: rumor continuo y, de vez en cuando, un trueno lejano.</summary>
        public void SetRain(bool on)
        {
            rainOn = on;
            if (on && rainSrc == null)
            {
                rainSrc = gameObject.AddComponent<AudioSource>();
                rainSrc.playOnAwake = false;
                rainSrc.loop = true;
                rainSrc.volume = 0f;
                rainSrc.clip = MakeRain();
            }
            if (on) nextThunder = Time.unscaledTime + Random.Range(6f, 14f);
        }

        void UpdateRain()
        {
            if (rainSrc == null) return;
            bool audible = rainOn && ambientActive && AmbientEnabled && !Muted;
            rainSrc.volume = Mathf.MoveTowards(rainSrc.volume, audible ? 0.28f : 0f, Time.unscaledDeltaTime * 0.2f);
            if (audible && !rainSrc.isPlaying) rainSrc.Play();
            else if (!audible && rainSrc.volume <= 0.001f && rainSrc.isPlaying) rainSrc.Pause();
            if (audible && Time.unscaledTime > nextThunder)
            {
                nextThunder = Time.unscaledTime + Random.Range(14f, 32f);
                if (thunder == null) thunder = MakeThunder();
                Play(thunder, Random.Range(0.45f, 0.8f));
            }
        }

        void Update()
        {
            UpdateMusic();
            UpdateRain();
            UpdateLoops();
            if (amb == null) return;
            bool audible = ambientActive && AmbientEnabled && !Muted;
            float goal = audible ? ambientTarget : 0f;
            amb.volume = Mathf.MoveTowards(amb.volume, goal, Time.unscaledDeltaTime * 0.25f);
            if (!audible && amb.volume <= 0.001f && amb.isPlaying) amb.Pause();
            else if (audible && !amb.isPlaying) amb.UnPause();
        }

        // 6 segundos que se repiten: ruido grave suave (motores lejanos) con oleaje lento y algún trino de pájaro.
        static AudioClip MakeAmbience()
        {
            const int rate = 22050;
            const float seconds = 6f;
            int n = Mathf.CeilToInt(seconds * rate);
            var data = new float[n];
            var rnd = new System.Random(21);
            float lp = 0f, lp2 = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / rate;
                float white = (float)rnd.NextDouble() * 2f - 1f;
                lp += 0.035f * (white - lp);
                lp2 += 0.2f * (lp - lp2);
                float swell = 0.65f + 0.35f * Mathf.Sin(2f * Mathf.PI * t / seconds * 2f);
                data[i] = lp2 * swell * 3.2f;
            }

            // Trinos: barridos rápidos de frecuencia alta.
            int[] chirpStart = { (int)(0.9f * rate), (int)(1.05f * rate), (int)(3.4f * rate), (int)(3.55f * rate), (int)(3.7f * rate), (int)(5.1f * rate) };
            float[] chirpBase = { 3100f, 3400f, 2700f, 3000f, 3300f, 3600f };
            for (int c = 0; c < chirpStart.Length; c++)
            {
                int len = (int)(0.09f * rate);
                float phase = 0f;
                for (int i = 0; i < len && chirpStart[c] + i < n; i++)
                {
                    float k = (float)i / len;
                    float f = chirpBase[c] + 900f * k;
                    phase += 2f * Mathf.PI * f / rate;
                    float env = Mathf.Sin(Mathf.PI * k);
                    data[chirpStart[c] + i] += Mathf.Sin(phase) * env * 0.12f;
                }
            }

            // Fundido en los extremos para que el bucle no haga "clic".
            int fade = 1500;
            for (int i = 0; i < fade; i++)
            {
                float k = (float)i / fade;
                data[i] *= k;
                data[n - 1 - i] *= k;
            }
            for (int i = 0; i < n; i++) data[i] = Mathf.Clamp(data[i], -0.9f, 0.9f);

            var clip = AudioClip.Create("ambience", n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        const int Rate = 22050;

        static AudioClip Clip(string name, float[] data)
        {
            for (int i = 0; i < data.Length; i++) data[i] = Mathf.Clamp(data[i], -0.95f, 0.95f);
            var clip = AudioClip.Create(name, data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>Bocina: dos notas a la vez (como las de verdad), onda algo cuadrada y ataque/caída cortos.</summary>
        static AudioClip MakeHorn(string name, float seconds, float fa, float fb)
        {
            int n = Mathf.CeilToInt(seconds * Rate);
            var d = new float[n];
            float pa = 0f, pb = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / Rate;
                float env = Mathf.Clamp01(t / 0.015f) * Mathf.Clamp01((seconds - t) / 0.04f);
                pa += 2f * Mathf.PI * fa / Rate; pb += 2f * Mathf.PI * fb / Rate;
                float a = Mathf.Sin(pa) + 0.35f * Mathf.Sin(pa * 3f) + 0.15f * Mathf.Sin(pa * 5f);
                float b = Mathf.Sin(pb) + 0.35f * Mathf.Sin(pb * 3f) + 0.15f * Mathf.Sin(pb * 5f);
                d[i] = (float)System.Math.Tanh((a + b) * 0.9f) * 0.3f * env;
            }
            return Clip(name, d);
        }

        /// <summary>Choque: golpe grave + chapa (ruido filtrado) + cristales (pitidos agudos sueltos) en el fuerte.</summary>
        static AudioClip MakeCrash(bool heavy, int seed, float seconds)
        {
            int n = Mathf.CeilToInt(seconds * Rate);
            var d = new float[n];
            var rnd = new System.Random(seed);
            float lp = 0f, ph = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / Rate;
                float w = (float)rnd.NextDouble() * 2f - 1f;
                lp += (heavy ? 0.25f : 0.4f) * (w - lp);
                float metal = lp * Mathf.Exp(-t * (heavy ? 4f : 7f));
                ph += 2f * Mathf.PI * Mathf.Lerp(heavy ? 70f : 110f, 40f, t / seconds) / Rate;
                float thump = Mathf.Sin(ph) * Mathf.Exp(-t * (heavy ? 7f : 12f));
                d[i] = (metal * 0.8f + thump * (heavy ? 0.7f : 0.45f)) * Mathf.Clamp01(i / 40f);
            }
            if (heavy)
                for (int k = 0; k < 14; k++)
                {
                    int s0 = (int)((0.08f + (float)rnd.NextDouble() * 0.6f) * Rate);
                    float f = 2500f + (float)rnd.NextDouble() * 3500f;
                    for (int i = 0; i < 900 && s0 + i < n; i++)
                        d[s0 + i] += Mathf.Sin(2f * Mathf.PI * f * i / Rate) * Mathf.Exp(-i / 150f) * 0.12f;
                }
            return Clip((heavy ? "crashH" : "crashL") + seed, d);
        }

        /// <summary>Sirena de ambulancia/bomberos: 2,4 s subiendo y bajando entre 650 y 1300 Hz.</summary>
        static AudioClip MakeSiren()
        {
            float seconds = 2.4f;
            int n = Mathf.CeilToInt(seconds * Rate);
            var d = new float[n];
            float ph = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / Rate;
                float f = 975f + 325f * Mathf.Sin(2f * Mathf.PI * t / 1.2f);
                ph += 2f * Mathf.PI * f / Rate;
                float env = Mathf.Clamp01(t / 0.2f) * Mathf.Clamp01((seconds - t) / 0.4f);
                d[i] = (Mathf.Sin(ph) + 0.2f * Mathf.Sin(ph * 2f)) * 0.25f * env;
            }
            return Clip("siren", d);
        }

        /// <summary>Lluvia: 4 s de ruido suavizado con gotitas sueltas; bucle sin clic.</summary>
        static AudioClip MakeRain()
        {
            float seconds = 4f;
            int n = Mathf.CeilToInt(seconds * Rate);
            var d = new float[n];
            var rnd = new System.Random(5);
            float lp = 0f, hp = 0f, prev = 0f;
            for (int i = 0; i < n; i++)
            {
                float w = (float)rnd.NextDouble() * 2f - 1f;
                lp += 0.5f * (w - lp);
                hp = lp - prev; prev = lp;      // quita graves: suena a "shhh"
                d[i] = (lp * 0.35f + hp * 0.25f);
                if (rnd.NextDouble() < 0.0015) for (int k = 0; k < 200 && i + k < n; k++) d[i + k] += Mathf.Sin(k * 0.9f) * Mathf.Exp(-k / 40f) * 0.15f;
            }
            int fade = 2000;
            for (int i = 0; i < fade; i++)
            {
                float k = (float)i / fade;
                float a = d[i], b = d[n - fade + i];
                d[n - fade + i] = b * (1f - k) + a * k;   // fundido cruzado con el principio
            }
            var trimmed = new float[n - fade];
            System.Array.Copy(d, fade, trimmed, 0, n - fade - fade);
            System.Array.Copy(d, n - fade, trimmed, n - fade - fade, fade);
            return Clip("rain", trimmed);
        }

        /// <summary>Trueno lejano: estallido y retumbo grave de 3 s.</summary>
        static AudioClip MakeThunder()
        {
            float seconds = 3.2f;
            int n = Mathf.CeilToInt(seconds * Rate);
            var d = new float[n];
            var rnd = new System.Random(99);
            float lp = 0f, lp2 = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / Rate;
                float w = (float)rnd.NextDouble() * 2f - 1f;
                lp += 0.02f * (w - lp);
                lp2 += 0.05f * (lp - lp2);
                float env = Mathf.Clamp01(t / 0.08f) * Mathf.Exp(-t * 1.1f) * (0.75f + 0.25f * Mathf.Sin(t * 9f));
                d[i] = lp2 * 14f * env;
            }
            return Clip("thunder", d);
        }

        static AudioClip Tone(string name, float seconds, float f0, float f1, float amp, bool noise)
        {
            const int rate = 22050;
            int n = Mathf.CeilToInt(seconds * rate);
            var data = new float[n];
            var rnd = new System.Random(7);
            float phase = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                float env = (1f - t) * Mathf.Clamp01(i / 80f);
                float v;
                if (noise) v = ((float)rnd.NextDouble() * 2f - 1f);
                else
                {
                    float f = Mathf.Lerp(f0, f1, t);
                    phase += 2f * Mathf.PI * f / rate;
                    v = Mathf.Sin(phase);
                }
                data[i] = v * env * amp;
            }
            var clip = AudioClip.Create(name, n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
