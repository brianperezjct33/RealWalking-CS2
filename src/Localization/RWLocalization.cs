// SPDX-License-Identifier: GPL-2.0-only
// Real Walking UI language resources. This file does not alter simulation or navigation.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using Game.SceneFlow;

namespace CS2RealWalkingG1Bridge
{
    internal static class RWLocalization
    {
        private const string FallbackLocale = "en-US";
        private const string ResourcePrefix = "CS2RealWalkingG1Bridge.rw_l10n.";
        private static readonly Dictionary<string, Dictionary<string, string>> Cache =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        private static readonly object Gate = new object();
        private static string _activeLocale = "";
        private static Dictionary<string, string> _active;
        private static Dictionary<string, string> _english;

        private static string ActiveLocale()
        {
            try
            {
                string locale = GameManager.instance?.localizationManager?.activeLocaleId;
                if (string.IsNullOrWhiteSpace(locale)) return FallbackLocale;
                // Steam and game locale identifiers sometimes differ in casing or script notation.
                switch (locale.Replace('_','-').ToLowerInvariant())
                {
                    case "zh-cn": case "zh-sg": case "zh-hans": return "zh-HANS";
                    case "zh-tw": case "zh-hk": case "zh-hant": return "zh-HANT";
                    case "pt-br": return "pt-BR";
                    case "en-us": case "en-gb": return "en-US";
                    case "es-es": case "es-mx": return "es-ES";
                    case "de-de": return "de-DE";
                    case "fr-fr": return "fr-FR";
                    case "it-it": return "it-IT";
                    case "ja-jp": return "ja-JP";
                    case "ko-kr": return "ko-KR";
                    case "pl-pl": return "pl-PL";
                    case "ru-ru": return "ru-RU";
                    default: return locale;
                }
            }
            catch { return FallbackLocale; } // Safe before game localization initializes.
        }

        private static Dictionary<string, string> Load(string locale)
        {
            if (Cache.TryGetValue(locale, out var cached)) return cached;
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            string resource = ResourcePrefix + locale + ".csv";
            using (Stream stream = typeof(RWLocalization).Assembly.GetManifestResourceStream(resource))
            {
                if (stream != null)
                {
                    using (var reader = new StreamReader(stream, Encoding.UTF8, true))
                    {
                        foreach (var record in ReadCsv(reader.ReadToEnd()))
                        {
                            if (record.Count < 2) continue;
                            string key = record[0].Trim().TrimStart('\uFEFF');
                            if (key.Length == 0 || result.ContainsKey(key)) continue;
                            result.Add(key, record[1].Replace("\\n", "\n"));
                        }
                    }
                }
            }
            Cache[locale] = result;
            return result;
        }

        // Reads standard CSV quoting and embedded commas / line endings.
        private static IEnumerable<List<string>> ReadCsv(string data)
        {
            var row = new List<string>(2);
            var field = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < data.Length; i++)
            {
                char c = data[i];
                if (c == '"')
                {
                    if (quoted && i + 1 < data.Length && data[i+1] == '"')
                    { field.Append('"'); i++; }
                    else quoted = !quoted;
                }
                else if (c == ',' && !quoted)
                { row.Add(field.ToString()); field.Clear(); }
                else if ((c == '\r' || c == '\n') && !quoted)
                {
                    row.Add(field.ToString()); field.Clear();
                    if (row.Count >= 2) yield return row;
                    row = new List<string>(2);
                    if (c == '\r' && i+1 < data.Length && data[i+1] == '\n') i++;
                }
                else field.Append(c);
            }
            if (row.Count > 0 || field.Length > 0)
            { row.Add(field.ToString()); if (row.Count >= 2) yield return row; }
        }

        internal static string T(string key, params object[] args)
        {
            string value;
            lock (Gate)
            {
                string locale = ActiveLocale();
                if (_english == null) _english = Load(FallbackLocale);
                if (_active == null || !string.Equals(_activeLocale, locale, StringComparison.OrdinalIgnoreCase))
                {
                    _activeLocale = locale;
                    _active = Load(locale);
                }
                if (!_active.TryGetValue(key, out value) && !_english.TryGetValue(key, out value))
                    return key;
            }
            if (args == null || args.Length == 0) return value;
            try
            {
                string cultureName = _activeLocale == "zh-HANS" ? "zh-CN" :
                    _activeLocale == "zh-HANT" ? "zh-TW" : _activeLocale;
                return string.Format(CultureInfo.GetCultureInfo(cultureName), value, args);
            }
            catch (FormatException) { return value; }
            catch (CultureNotFoundException) { return string.Format(CultureInfo.InvariantCulture, value, args); }
        }

        internal static string VehicleName(RWVehicleKind kind)
        {
            switch (kind)
            {
                case RWVehicleKind.Bus: return T("rw.vehicle.bus");
                case RWVehicleKind.Tram: return T("rw.vehicle.tram");
                case RWVehicleKind.Train: return T("rw.vehicle.train");
                case RWVehicleKind.Subway: return T("rw.vehicle.subway");
                case RWVehicleKind.Ferry: return T("rw.vehicle.ferry");
                case RWVehicleKind.Ship: return T("rw.vehicle.ship");
                case RWVehicleKind.Airplane: return T("rw.vehicle.airplane");
                default: return T("rw.vehicle.generic");
            }
        }
    }
}
