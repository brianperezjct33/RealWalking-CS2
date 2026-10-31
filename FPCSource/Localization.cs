// SPDX-License-Identifier: GPL-2.0-only
// Real Walking standalone translation reader, newly implemented for this integration.
// The previous Localization.cs (derived from Plop the Growables, Apache-2.0)
// is deliberately NOT shipped in the combined GPLv2-only source distribution.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Colossal.Localization;
using Colossal.Logging;
using Game.Modding;
using Game.SceneFlow;

namespace FirstPersonCameraContinued
{
    public static class Localization
    {
        public static void LoadTranslations(ModSetting settings, ILog log)
        {
            Assembly assembly = typeof(Localization).Assembly;
            string prefix = assembly.GetName().Name + ".l10n.";
            try
            {
                foreach (string locale in GameManager.instance.localizationManager.GetSupportedLocales())
                {
                    string resource = prefix + locale + ".csv";
                    try
                    {
                        using (Stream input = assembly.GetManifestResourceStream(resource))
                        {
                            if (input == null) continue;
                            using (var reader = new StreamReader(input, Encoding.UTF8, true))
                            {
                                var entries = new Dictionary<string, string>(StringComparer.Ordinal);
                                foreach (var fields in ReadCsvRecords(reader.ReadToEnd()))
                                {
                                    if (fields.Count < 2 || string.IsNullOrWhiteSpace(fields[0])) continue;
                                    string key = ExpandSettingsKey(fields[0].Trim().TrimStart('\uFEFF'), settings);
                                    if (!entries.ContainsKey(key)) entries.Add(key, fields[1].Replace("\\n", "\n"));
                                }
                                GameManager.instance.localizationManager.AddSource(locale, new MemorySource(entries));
                                log.Info("[RW] Registered " + entries.Count + " camera translations for " + locale);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        // A broken optional translation must not interrupt camera startup.
                        log.Error(ex, "[RW] Cannot read bundled translation " + resource);
                    }
                }
            }
            catch (Exception ex)
            {
                log.Error(ex, "[RW] Cannot enumerate supported game locales");
            }
        }

        // Character-state CSV reader: commas, doubled quotes and embedded line breaks
        // are handled without bringing in a new library or another mod dependency.
        private static IEnumerable<List<string>> ReadCsvRecords(string data)
        {
            var record = new List<string>(2);
            var field = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < data.Length; i++)
            {
                char current = data[i];
                if (current == '"')
                {
                    if (quoted && i + 1 < data.Length && data[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else quoted = !quoted;
                }
                else if (current == ',' && !quoted)
                {
                    record.Add(field.ToString());
                    field.Clear();
                }
                else if ((current == '\r' || current == '\n') && !quoted)
                {
                    record.Add(field.ToString());
                    field.Clear();
                    if (record.Count > 1) yield return record;
                    record = new List<string>(2);
                    if (current == '\r' && i + 1 < data.Length && data[i + 1] == '\n') i++;
                }
                else field.Append(current);
            }
            if (record.Count > 0 || field.Length > 0)
            {
                record.Add(field.ToString());
                if (record.Count > 1) yield return record;
            }
        }

        private static string ExpandSettingsKey(string key, ModSetting settings)
        {
            int split = key.IndexOf(':');
            if (split < 0) return key;
            string name = key.Substring(split + 1);
            switch (key.Substring(0, split))
            {
                case "Options.TAB": return settings.GetOptionTabLocaleID(name);
                case "Options.GROUP": return settings.GetOptionGroupLocaleID(name);
                case "Options.OPTION": return settings.GetOptionLabelLocaleID(name);
                case "Options.OPTION_DESCRIPTION": return settings.GetOptionDescLocaleID(name);
                case "Options.BINDING": return settings.GetBindingKeyLocaleID(name);
                case "Options.WARNING": return settings.GetOptionWarningLocaleID(name);
                default: return settings.GetSettingsLocaleID();
            }
        }
    }
}
