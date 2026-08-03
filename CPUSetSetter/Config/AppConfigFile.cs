using CPUSetSetter.Config.Models;
using CPUSetSetter.Platforms;
using CPUSetSetter.Themes;
using CPUSetSetter.UI.Tabs.Processes;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;


namespace CPUSetSetter.Config
{
    public static class AppConfigFile
    {
        private static readonly JsonSerializerOptions jsonOptions = new() { WriteIndented = true };

        private static readonly string configPath;
        private static readonly string saveTempPath;
        private static readonly string backupNameTemplate;
        public const int ConfigVersion = 3;

        static AppConfigFile()
        {
            string portableConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CPUSetSetter_config.json");

            string configDirectory;
            if (File.Exists(portableConfigPath))
            {
                // If the config file is placed in the same directory as CPUSetSetter.exe, use it as the portable config location
                configDirectory = AppDomain.CurrentDomain.BaseDirectory;
            }
            else
            {
                // Otherwise, place it in AppData
                configDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CPU Set Setter");
                Directory.CreateDirectory(configDirectory);
            }

            configPath = Path.Combine(configDirectory, "CPUSetSetter_config.json");
            saveTempPath = Path.Combine(configDirectory, "CPUSetSetter_config_new.json");
            backupNameTemplate = Path.Combine(configDirectory, "CPUSetSetter_config_backup{0}.json");
        }

        public static void Save(AppConfig config)
        {
            ConfigJson configJson = new(config);
            try
            {
                // Save the new config to a temp file before overwriting the config, in case the serialization fails and clears the config
                FileStream fileStream = File.Create(saveTempPath);
                JsonSerializer.Serialize(fileStream, configJson, options: jsonOptions);
                fileStream.Dispose();
                File.Move(saveTempPath, configPath, true);
            }
            catch (Exception ex)
            {
                WindowLogger.Write($"Failed to write config: {ex}");
            }
        }

        public static AppConfig Load()
        {
            if (!File.Exists(configPath))
            {
                // The config file does not exist yet, use the defaults
                return JsonToConfig(ConfigJson.Default, true, true, out bool _);
            }

            try
            {
                using FileStream fileStream = File.OpenRead(configPath);
                ConfigJson configJson = JsonSerializer.Deserialize<ConfigJson>(fileStream, options: jsonOptions) ?? throw new NullReferenceException();
                AppConfig config = JsonToConfig(configJson, false, false, out bool hadSoftError);

                if (hadSoftError)
                {
                    try
                    {
                        string backupName = BackupConfig();
                        WindowLogger.Write($"Your config contained an error. The old config was backed up to '{backupName}'");
                    }
                    catch (Exception backupEx)
                    {
                        WindowLogger.Write($"Unable to make a backup of your old config: {backupEx}\n");
                        WindowLogger.Write("Your config contained an error. What did you do to make even the backup fail??");
                    }
                }

                return config;
            }
            catch (Exception readEx)
            {
                // The config file was likely corrupt
                WindowLogger.Write($"Failed to read config: {readEx}\n");
                try
                {
                    string backupName = BackupConfig();
                    WindowLogger.Write($"Your config has been reset. The old config was backed up to '{backupName}'");
                }
                catch (Exception backupEx)
                {
                    WindowLogger.Write($"Unable to make a backup of your old config: {backupEx}\n");
                    WindowLogger.Write("Your config has been reset. What did you do to make even the backup fail??");
                }
                // Use the defaults
                return JsonToConfig(ConfigJson.Default, true, false, out bool _);
            }
        }

        private static string BackupConfig()
        {
            int i = 0;
            while (true)
            {
                string backupName = string.Format(backupNameTemplate, i++);
                try
                {
                    File.Copy(configPath, backupName, false);
                    return backupName;
                }
                catch (IOException)
                {
                    // Continue when the backup name already exists
                    continue;
                }
            }
        }

        /// <summary>
        /// Export the current config to a user-chosen file. Returns an error message, or null on success
        /// </summary>
        public static string? ExportToFile(string path)
        {
            try
            {
                ConfigJson configJson = new(AppConfig.Instance);
                using FileStream fileStream = File.Create(path);
                JsonSerializer.Serialize(fileStream, configJson, options: jsonOptions);
                return null;
            }
            catch (Exception ex)
            {
                return $"Failed to export config: {ex.Message}";
            }
        }

        /// <summary>
        /// Import a config from a user-chosen file, replacing the current masks, rules, templates and settings.
        /// Returns an error message, or null on success
        /// </summary>
        public static string? ImportFromFile(string path)
        {
            ConfigJson configJson;
            try
            {
                using FileStream fileStream = File.OpenRead(path);
                configJson = JsonSerializer.Deserialize<ConfigJson>(fileStream, options: jsonOptions) ?? throw new NullReferenceException();
            }
            catch (Exception ex)
            {
                return $"The selected file is not a valid config: {ex.Message}";
            }

            string? validationError = ValidateConfigJson(configJson);
            if (validationError is not null)
                return validationError;

            try
            {
                ApplyImport(configJson);
                return null;
            }
            catch (Exception ex)
            {
                return $"Failed to apply the imported config: {ex.Message}";
            }
        }

        private static string? ValidateConfigJson(ConfigJson configJson)
        {
            if (!TryParseHotkeys(configJson.NoMaskHotkeys))
                return "The config contains an invalid hotkey for the 'No mask' mask.";

            var maskNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (LogicalProcessorMaskJson jsonMask in configJson.Masks)
            {
                if (!maskNames.Add(jsonMask.Name))
                    return $"The config contains multiple masks named '{jsonMask.Name}'.";
                if (jsonMask.BoolMask.Count != CpuInfo.LogicalProcessorCount)
                    return $"The mask '{jsonMask.Name}' does not match this system ({jsonMask.BoolMask.Count} logical processors in the config vs {CpuInfo.LogicalProcessorCount} here).";
                if (!Enum.TryParse<MaskApplyType>(jsonMask.MaskType, out _))
                    return $"The mask '{jsonMask.Name}' has an invalid type '{jsonMask.MaskType}'.";
                if (!TryParseHotkeys(jsonMask.Hotkeys))
                    return $"The mask '{jsonMask.Name}' contains an invalid hotkey.";
            }

            foreach (ProgramRuleJson jsonRule in configJson.ProgramRules)
            {
                if (!maskNames.Contains(jsonRule.LogicalProcessorMaskName))
                    return $"The rule for '{jsonRule.ProgramPath}' references a mask that does not exist ('{jsonRule.LogicalProcessorMaskName}').";
                if (jsonRule.PriorityClass is not null && !Enum.TryParse<ProcessPriorityClass>(jsonRule.PriorityClass, out _))
                    return $"The rule for '{jsonRule.ProgramPath}' has an invalid priority '{jsonRule.PriorityClass}'.";
            }

            foreach (RuleTemplateJson jsonTemplate in configJson.RuleTemplates)
            {
                if (!maskNames.Contains(jsonTemplate.LogicalProcessorMaskName))
                    return $"The template for '{jsonTemplate.RuleGlob}' references a mask that does not exist ('{jsonTemplate.LogicalProcessorMaskName}').";
            }

            if (!Enum.TryParse<Theme>(configJson.UiTheme, out _))
                return $"The config has an invalid theme '{configJson.UiTheme}'.";

            return null;
        }

        private static bool TryParseHotkeys(List<string> hotkeys)
        {
            foreach (string hotkey in hotkeys)
            {
                if (!Enum.TryParse<VKey>(hotkey, out _))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Replace the live config with the imported one, in-place on the singleton. Removal happens in a safe order:
        /// templates, then rules (via TryRemove, which clears their processes), then masks (via Remove). The NoMask mask
        /// is kept at the front and only its hotkeys are replaced
        /// </summary>
        private static void ApplyImport(ConfigJson configJson)
        {
            AppConfig config = AppConfig.Instance;

            // 1. Remove all rule templates (their Dispose is safe to call directly)
            while (config.RuleTemplates.Count > 0)
                config.RuleTemplates.RemoveAt(config.RuleTemplates.Count - 1);

            // 2. Remove all program rules. TryRemove sets their running processes to NoMask and disposes them safely
            while (config.ProgramRules.Count > 0)
                config.ProgramRules[0].TryRemove();

            // 3. Remove all masks except the NoMask (index 0). Remove() disposes them and unregisters their hotkeys
            for (int i = config.LogicalProcessorMasks.Count - 1; i >= 1; --i)
                config.LogicalProcessorMasks[i].Remove();

            // 4. Update the NoMask's hotkeys in place
            List<VKey> noMaskHotkeys = configJson.NoMaskHotkeys.Select(hotkey => Enum.Parse<VKey>(hotkey)).ToList();
            LogicalProcessorMask noMask = config.LogicalProcessorMasks.Single(mask => mask.MaskType == MaskApplyType.NoMask);
            noMask.Hotkeys.Clear();
            foreach (VKey hotkey in noMaskHotkeys)
                noMask.Hotkeys.Add(hotkey);

            // 5. Add the imported masks
            foreach (LogicalProcessorMaskJson jsonMask in configJson.Masks)
            {
                List<VKey> hotkeys = jsonMask.Hotkeys.Select(hotkey => Enum.Parse<VKey>(hotkey)).ToList();
                MaskApplyType maskType = Enum.Parse<MaskApplyType>(jsonMask.MaskType);
                config.LogicalProcessorMasks.Add(new(jsonMask.Name, maskType, new(jsonMask.BoolMask), hotkeys));
            }

            // 6. Add the imported program rules (skipSetup = true; templates are matched afterwards)
            foreach (ProgramRuleJson jsonRule in configJson.ProgramRules)
            {
                LogicalProcessorMask mask = config.LogicalProcessorMasks.Single(mask => string.Equals(mask.Name, jsonRule.LogicalProcessorMaskName, StringComparison.OrdinalIgnoreCase));
                ProcessPriorityClass? priorityClass = jsonRule.PriorityClass is null
                    ? null
                    : Enum.Parse<ProcessPriorityClass>(jsonRule.PriorityClass);
                config.ProgramRules.Add(new(jsonRule.ProgramPath, mask, jsonRule.AutoReapply, true, priorityClass));
            }

            // 7. Add the imported rule templates
            foreach (RuleTemplateJson jsonTemplate in configJson.RuleTemplates)
            {
                LogicalProcessorMask mask = config.LogicalProcessorMasks.Single(mask => string.Equals(mask.Name, jsonTemplate.LogicalProcessorMaskName, StringComparison.OrdinalIgnoreCase));
                config.RuleTemplates.Add(new(jsonTemplate.RuleGlob, mask));
            }

            // 8. Apply the imported settings
            config.MuteHotkeySound = configJson.MuteHotKeySound;
            config.StartMinimized = configJson.StartMinimized;
            config.DisableWelcomeMessage = configJson.DisableWelcomeMessage;
            config.ShowGameModePopup = configJson.ShowGameModePopup;
            config.ShowUpdatePopup = configJson.ShowUpdatePopup;
            config.ClearMasksOnClose = configJson.ClearMasksOnClose;
            config.UiTheme = Enum.Parse<Theme>(configJson.UiTheme);

            // 9. Match the new templates to the new rules and apply masks to any running processes
            RuleTemplate.OnConfigLoaded();
            config.Save();
        }

        private static AppConfig JsonToConfig(ConfigJson configJson, bool generateDefaultMasks, bool isFirstRun, out bool hadSoftError)
        {
            hadSoftError = false;

            List<VKey> noMaskHotkeys = configJson.NoMaskHotkeys.Select(hotkey => Enum.Parse<VKey>(hotkey)).ToList();
            // Put the NoMask Mask at the front of the logicalProcessorMasks
            List<LogicalProcessorMask> logicalProcessorMasks = [LogicalProcessorMask.InitNoMask(noMaskHotkeys)];

            // Construct the LogicalProcessorMask models from the config
            foreach (LogicalProcessorMaskJson jsonMask in configJson.Masks)
            {
                if (logicalProcessorMasks.Any(existingMask => existingMask.Name == jsonMask.Name))
                {
                    WindowLogger.Write($"Config file contained multiple masks with the same name '{jsonMask.Name}'. The duplicate was removed.");
                    hadSoftError = true;
                    continue;
                }
                if (jsonMask.BoolMask.Count != CpuInfo.LogicalProcessorCount)
                {
                    WindowLogger.Write($"Config file contained incorrect mask length in mask '{jsonMask.Name}'. The invalid mask was removed.");
                    hadSoftError = true;
                    continue;
                }

                List<VKey> hotkeys = jsonMask.Hotkeys.Select(hotkey => Enum.Parse<VKey>(hotkey)).ToList();
                MaskApplyType maskType = Enum.Parse<MaskApplyType>(jsonMask.MaskType);
                logicalProcessorMasks.Add(new(jsonMask.Name, maskType, jsonMask.BoolMask, hotkeys));
            }

            // Construct the ProgramRule models from the config
            List<ProgramRule> programRules = configJson.ProgramRules.Select(jsonProgramRule =>
            {
                LogicalProcessorMask mask = logicalProcessorMasks.Single(mask => mask.Name == jsonProgramRule.LogicalProcessorMaskName);
                ProcessPriorityClass? priorityClass = jsonProgramRule.PriorityClass is null
                    ? null
                    : Enum.Parse<ProcessPriorityClass>(jsonProgramRule.PriorityClass);
                return new ProgramRule(jsonProgramRule.ProgramPath, mask, jsonProgramRule.AutoReapply, true, priorityClass);
            }).ToList();

            // Construct the RuleTemplate models from the config
            List<RuleTemplate> ruleTemplates = configJson.RuleTemplates.Select(jsonRuleTemplate =>
            {
                LogicalProcessorMask mask = logicalProcessorMasks.Single(mask => mask.Name == jsonRuleTemplate.LogicalProcessorMaskName);
                return new RuleTemplate(jsonRuleTemplate.RuleGlob, mask);
            }).ToList();

            // Construct the AppConfig
            return new(logicalProcessorMasks,
                programRules,
                ruleTemplates,
                configJson.MuteHotKeySound,
                configJson.StartMinimized,
                configJson.DisableWelcomeMessage,
                configJson.ShowGameModePopup,
                configJson.ShowUpdatePopup,
                configJson.ClearMasksOnClose,
                Enum.Parse<Theme>(configJson.UiTheme),
                generateDefaultMasks,
                isFirstRun,
                configJson.ConfigVersion);
        }

        private class ConfigJson
        {
            public List<string> NoMaskHotkeys { get; init; }
            public List<LogicalProcessorMaskJson> Masks { get; init; }
            public List<ProgramRuleJson> ProgramRules { get; init; }
            public List<RuleTemplateJson> RuleTemplates { get; init; }
            public bool MuteHotKeySound { get; init; }
            public bool StartMinimized { get; init; }
            public bool DisableWelcomeMessage { get; init; }
            public bool ShowGameModePopup { get; init; }
            public bool ShowUpdatePopup { get; init; }
            public bool ClearMasksOnClose { get; init; }
            public string UiTheme { get; init; }
            public int ConfigVersion { get; init; } // Can be used in the future to migrate config files

            public static ConfigJson Default => new();

            // Default constructor for JSON Deserialization
            [JsonConstructor]
            private ConfigJson()
            {
                NoMaskHotkeys = [];
                Masks = [];
                ProgramRules = [];
                RuleTemplates = [];
                MuteHotKeySound = false;
                StartMinimized = false;
                DisableWelcomeMessage = false;
                ShowGameModePopup = true;
                ShowUpdatePopup = true;
                ClearMasksOnClose = false;
                UiTheme = Theme.System.ToString();
                ConfigVersion = 0;
            }

            /// <summary>
            /// Constructor used for saving the config
            /// </summary>
            public ConfigJson(AppConfig config)
            {
                // Get the Hotkeys for the NoMask
                var noMaskHotkeysVKeys = config.LogicalProcessorMasks.Single(mask => mask.MaskType == MaskApplyType.NoMask).Hotkeys;
                NoMaskHotkeys = noMaskHotkeysVKeys.Select(hotkey => hotkey.ToString()).ToList();

                // Filter out the NoMask from the list of logicalProcessorMasks
                var userDefinedMasks = config.LogicalProcessorMasks.Where(mask => mask.MaskType != MaskApplyType.NoMask);

                // Convert the LogicalProcessorMask models to JSON objects
                Masks = userDefinedMasks.Select(mask =>
                {
                    List<string> hotkeys = mask.Hotkeys.Select(hotkey => hotkey.ToString()).ToList();
                    return new LogicalProcessorMaskJson(mask.Name, mask.MaskType.ToString(), new(mask.BoolMask), hotkeys);
                }).ToList();

                // Convert the ProgramRules models to JSON objects
                ProgramRules = config.ProgramRules.Select(programRule =>
                    new ProgramRuleJson(
                        programRule.ProgramPath,
                        programRule.Mask.Name,
                        programRule.AutoReapply,
                        programRule.PriorityClass?.ToString())
                ).ToList();

                // Convert the RuleTemplates models to JSON objects
                RuleTemplates = config.RuleTemplates.Select(ruleTemplate =>
                    new RuleTemplateJson(ruleTemplate.RuleGlob, ruleTemplate.Mask.Name)
                ).ToList();

                // Set the remainder of the settings to the JSON object
                MuteHotKeySound = config.MuteHotkeySound;
                StartMinimized = config.StartMinimized;
                DisableWelcomeMessage = config.DisableWelcomeMessage;
                ShowGameModePopup = config.ShowGameModePopup;
                ShowUpdatePopup = config.ShowUpdatePopup;
                ClearMasksOnClose = config.ClearMasksOnClose;
                UiTheme = config.UiTheme.ToString();
                ConfigVersion = AppConfigFile.ConfigVersion;
            }
        }

        private class LogicalProcessorMaskJson
        {
            public string Name { get; init; }
            public List<bool> BoolMask { get; init; }
            public string MaskType { get; init; }
            public List<string> Hotkeys { get; init; }

            [JsonConstructor]
            private LogicalProcessorMaskJson()
            {
                Name = string.Empty;
                BoolMask = [];
                MaskType = MaskApplyType.CPUSet.ToString();
                Hotkeys = [];
            }

            public LogicalProcessorMaskJson(string name, string maskType, List<bool> boolMask, List<string> hotkeys)
            {
                Name = name;
                BoolMask = boolMask;
                MaskType = maskType;
                Hotkeys = hotkeys;
            }
        }

        private class ProgramRuleJson
        {
            public string ProgramPath { get; init; }
            public string LogicalProcessorMaskName { get; init; }
            public bool AutoReapply { get; init; }
            public string? PriorityClass { get; init; } // null = default (don't change priority)

            [JsonConstructor]
            private ProgramRuleJson()
            {
                ProgramPath = string.Empty;
                LogicalProcessorMaskName = string.Empty;
                AutoReapply = false;
            }

            public ProgramRuleJson(string programPath, string logicalProcessorMaskName, bool autoReapply,
                string? priorityClass = null)
            {
                ProgramPath = programPath;
                LogicalProcessorMaskName = logicalProcessorMaskName;
                AutoReapply = autoReapply;
                PriorityClass = priorityClass;
            }
        }

        private class RuleTemplateJson
        {
            public string RuleGlob { get; init; }
            public string LogicalProcessorMaskName { get; init; }

            [JsonConstructor]
            private RuleTemplateJson()
            {
                RuleGlob = string.Empty;
                LogicalProcessorMaskName = string.Empty;
            }

            public RuleTemplateJson(string ruleGlob, string logicalProcessorMaskName)
            {
                RuleGlob = ruleGlob;
                LogicalProcessorMaskName = logicalProcessorMaskName;
            }
        }
    }
}
