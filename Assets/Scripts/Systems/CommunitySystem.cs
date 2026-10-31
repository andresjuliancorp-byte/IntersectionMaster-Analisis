using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEngine;
using IntersectionMaster.Data;

namespace IntersectionMaster.Systems
{
    /// <summary>
    /// Compartir mapas sin servidor: un mapa se convierte en un texto («código») que se copia al portapapeles y se
    /// manda por cualquier mensajería. Quien lo recibe lo pega en «Pegar código» y el mapa se valida y se guarda.
    /// Formato: "IM2:" + Base64(GZip(JSON del mapa sin miniatura)).
    /// </summary>
    public static class MapShare
    {
        const string Prefix = "IM2:";
        const int MaxCodeChars = 4000000;

        public static string Encode(MapData map)
        {
            var copy = map.Clone();
            copy.thumbnailBase64 = "";
            string json = MapSerializationSystem.ToJson(copy, false);
            byte[] raw = Encoding.UTF8.GetBytes(json);
            using (var ms = new MemoryStream())
            {
                using (var gz = new GZipStream(ms, CompressionMode.Compress, true))
                {
                    gz.Write(raw, 0, raw.Length);
                }
                return Prefix + Convert.ToBase64String(ms.ToArray());
            }
        }

        public static bool TryDecode(string code, out MapData map, out string error)
        {
            map = null;
            error = "";
            if (string.IsNullOrWhiteSpace(code)) { error = "El portapapeles está vacío."; return false; }
            code = code.Trim();
            if (code.Length > MaxCodeChars) { error = "El código es demasiado largo."; return false; }
            if (!code.StartsWith(Prefix)) { error = "Eso no parece un código de mapa de Intersection Master."; return false; }

            string json;
            try
            {
                byte[] packed = Convert.FromBase64String(code.Substring(Prefix.Length).Replace("\n", "").Replace("\r", "").Replace(" ", ""));
                using (var input = new MemoryStream(packed))
                using (var gz = new GZipStream(input, CompressionMode.Decompress))
                using (var output = new MemoryStream())
                {
                    var buffer = new byte[8192];
                    int read;
                    while ((read = gz.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        output.Write(buffer, 0, read);
                        if (output.Length > MapSerializationSystem.MaxJsonChars) { error = "El mapa descomprimido es demasiado grande."; return false; }
                    }
                    json = Encoding.UTF8.GetString(output.ToArray());
                }
            }
            catch (Exception ex)
            {
                error = "Código dañado: " + ex.Message;
                return false;
            }

            if (!MapSerializationSystem.TryFromJson(json, out map, out error)) return false;
            map.id = MapData.NewId();
            return true;
        }

        /// <summary>Lee el portapapeles, importa el mapa y lo guarda. Devuelve un mensaje para el jugador.</summary>
        public static string ImportFromClipboard()
        {
            MapData map;
            string err;
            if (!TryDecode(GUIUtility.systemCopyBuffer, out map, out err)) return err;
            if (!SaveSystem.Save(map, out err)) return err;
            return "Mapa «" + map.name + "» importado.";
        }
    }

    /// <summary>
    /// Contrato para una comunidad en línea. El juego NO incluye ningún servidor: para activarla hay que
    /// implementar esta interfaz contra un servicio propio (con moderación y política de privacidad) y asignarla a
    /// CommunityHub.Backend. Mientras no haya backend, la comunidad aparece como no disponible.
    /// </summary>
    public interface ICommunityBackend
    {
        string DisplayName { get; }
        /// <summary>Resumen de los mapas publicados (sin descargarlos enteros).</summary>
        List<MapInfo> ListMaps(int page, int pageSize, out string error);
        /// <summary>Descarga un mapa por id. El resultado pasa por MapSerializationSystem (validación y saneado).</summary>
        string DownloadMapJson(string id, out string error);
        /// <summary>Publica un mapa del jugador.</summary>
        bool Upload(MapData map, out string error);
    }

    public static class CommunityHub
    {
        /// <summary>null = sin servidor configurado (estado actual).</summary>
        public static ICommunityBackend Backend;

        public static bool Available { get { return Backend != null; } }

        public static string StatusText
        {
            get
            {
                return Available ? "Comunidad: " + Backend.DisplayName
                    : "Comunidad en línea no disponible: falta configurar un servidor. Mientras tanto, comparte mapas con un código.";
            }
        }

        /// <summary>Descarga un mapa de la comunidad, lo valida y lo guarda como mapa propio.</summary>
        public static string Download(string id)
        {
            if (!Available) return StatusText;
            string err;
            string json = Backend.DownloadMapJson(id, out err);
            if (json == null) return err;
            MapData map;
            if (!MapSerializationSystem.TryFromJson(json, out map, out err)) return err;
            map.id = MapData.NewId();
            if (!SaveSystem.Save(map, out err)) return err;
            return "Mapa «" + map.name + "» descargado.";
        }
    }
}
