using Dalamud.Game.Chat;
using Dalamud.Game.Command;
using Dalamud.Plugin;
using ImGuiNET;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace XIVComboExpandedPlugin
{
    public class XIVComboExpandedPlugin : IDalamudPlugin
    {
        public string Name => "XIV Combo Expanded Plugin";
        public string Command => "/pcombo";

        internal XIVComboExpandedConfiguration Configuration;
        internal const int CURRENT_CONFIG_VERSION = 4;

        internal DalamudPluginInterface Interface;
        private IconReplacer IconReplacer;

        private Dictionary<string, List<(CustomComboPreset preset, CustomComboInfoAttribute info)>> GroupedPresets;

        public void Initialize(DalamudPluginInterface pluginInterface)
        {
            Interface = pluginInterface;

            Interface.CommandManager.AddHandler(Command, new CommandInfo(OnCommandDebugCombo)
            {
                HelpMessage = "Open a window to edit custom combo settings.",
                ShowInHelp = true
            });

            Configuration = pluginInterface.GetPluginConfig() as XIVComboExpandedConfiguration ?? new XIVComboExpandedConfiguration();
            if (Configuration.Version < CURRENT_CONFIG_VERSION)
            {
                Configuration.Upgrade();
                SaveConfiguration();
            }

            IconReplacer = new IconReplacer(pluginInterface.ClientState, pluginInterface.TargetModuleScanner, Configuration);
            
            Interface.UiBuilder.OnOpenConfigUi += (sender, args) => isImguiComboSetupOpen = true;
            Interface.UiBuilder.OnBuildUi += UiBuilder_OnBuildUi;

            GroupedPresets = Enum
                .GetValues(typeof(CustomComboPreset))
                .Cast<CustomComboPreset>()
                .Select(preset => (preset, info: preset.GetAttribute<CustomComboInfoAttribute>()))
                .Where(presetWithInfo => presetWithInfo.info != null)
                .OrderBy(presetWithInfo => presetWithInfo.info.JobName)
                .GroupBy(presetWithInfo => presetWithInfo.info.JobName)
                .ToDictionary(presetWithInfos => presetWithInfos.Key,
                              presetWithInfos => presetWithInfos.ToList());
        }

        private bool isImguiComboSetupOpen = false;

        private void SaveConfiguration()
        {
            Interface.SavePluginConfig(Configuration);
        }

        private void UiBuilder_OnBuildUi()
        {
            if (!isImguiComboSetupOpen)
                return;

            ImGui.SetNextWindowSize(new Vector2(740, 490));

            ImGui.Begin("Custom Combo Setup", ref isImguiComboSetupOpen, ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar);

            ImGui.Text("This window allows you to enable and disable custom combos to your liking.");
            ImGui.Separator();

            ImGui.BeginChild("scrolling", new Vector2(0, 400), true, ImGuiWindowFlags.HorizontalScrollbar);

            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0, 5));

            int i = 1;
            foreach (var jobName in GroupedPresets.Keys)
            {
                if (ImGui.CollapsingHeader(jobName))
                {
                    foreach (var (preset, info) in GroupedPresets[jobName])
                    {
                        bool enabled = Configuration.IsEnabled(preset);

                        ImGui.PushItemWidth(200);
                        if (ImGui.Checkbox(info.FancyName, ref enabled))
                        {
                            if (enabled)
                                Configuration.EnabledActions.Add(preset);
                            else
                                Configuration.EnabledActions.Remove(preset);
                            IconReplacer.UpdateEnabledActionIDs();
                            SaveConfiguration();
                        }
                        ImGui.PopItemWidth();

                        ImGui.TextColored(new Vector4(0.68f, 0.68f, 0.68f, 1.0f), $"#{i}: {info.Description}");
                        ImGui.Spacing();

                        i++;
                    }
                }
                else
                {
                    i += GroupedPresets[jobName].Count;
                }
            }

            ImGui.PopStyleVar();

            ImGui.EndChild();

            ImGui.Separator();

            if (ImGui.Button("Close"))
                isImguiComboSetupOpen = false;

            ImGui.End();
        }

        public void Dispose()
        {
            IconReplacer.Dispose();

            Interface.CommandManager.RemoveHandler(Command);

            Interface.Dispose();
        }

        private void OnCommandDebugCombo(string command, string arguments)
        {
            var argumentsParts = arguments.Split();

            switch (argumentsParts[0])
            {
                case "setall":
                    {
                        foreach (var preset in Enum.GetValues(typeof(CustomComboPreset)).Cast<CustomComboPreset>())
                            Configuration.EnabledActions.Add(preset);

                        Interface.Framework.Gui.Chat.Print("All SET");
                    }
                    break;
                case "unsetall":
                    {
                        foreach (var preset in Enum.GetValues(typeof(CustomComboPreset)).Cast<CustomComboPreset>())
                            Configuration.EnabledActions.Remove(preset);

                        Interface.Framework.Gui.Chat.Print("All UNSET");
                    }
                    break;
                case "set":
                    {
                        var targetPreset = argumentsParts[1].ToLower();
                        foreach (var preset in Enum.GetValues(typeof(CustomComboPreset)).Cast<CustomComboPreset>())
                        {
                            if (preset.ToString().ToLower() != targetPreset)
                                continue;

                            Configuration.EnabledActions.Add(preset);
                            Interface.Framework.Gui.Chat.Print($"{preset} SET");
                        }
                    }
                    break;
                case "toggle":
                    {
                        var targetPreset = argumentsParts[1].ToLower();
                        foreach (var preset in Enum.GetValues(typeof(CustomComboPreset)).Cast<CustomComboPreset>())
                        {
                            if (preset.ToString().ToLower() != targetPreset)
                                continue;

                            if (Configuration.EnabledActions.Contains(preset))
                            {
                                Configuration.EnabledActions.Remove(preset);
                                Interface.Framework.Gui.Chat.Print($"{preset} UNSET");
                            }
                            else
                            {
                                Configuration.EnabledActions.Add(preset);
                                Interface.Framework.Gui.Chat.Print($"{preset} SET");
                            }
                        }
                    }
                    break;
                case "unset":
                    {
                        var targetPreset = argumentsParts[1].ToLower();
                        foreach (var preset in Enum.GetValues(typeof(CustomComboPreset)).Cast<CustomComboPreset>())
                        {
                            if (preset.ToString().ToLower() != targetPreset)
                                continue;

                            Configuration.EnabledActions.Remove(preset);
                            Interface.Framework.Gui.Chat.Print($"{preset} UNSET");
                        }
                    }
                    break;
                case "list":
                    {
                        string filter;
                        if (argumentsParts.Length == 1)
                            filter = "all";
                        else
                            filter = argumentsParts[1].ToLower();

                        foreach (var preset in Enum.GetValues(typeof(CustomComboPreset)).Cast<CustomComboPreset>())
                        {
                            if (filter == "set")
                            {
                                if (Configuration.EnabledActions.Contains(preset))
                                    Interface.Framework.Gui.Chat.Print(preset.ToString());
                            }
                            else if (filter == "unset")
                            {
                                if (!Configuration.EnabledActions.Contains(preset))
                                    Interface.Framework.Gui.Chat.Print(preset.ToString());
                            }
                            else if (filter == "all")
                            {
                                Interface.Framework.Gui.Chat.Print(preset.ToString());
                            }
                        }
                    }
                    break;
                default:
                    isImguiComboSetupOpen = true;
                    break;
            }

            Interface.SavePluginConfig(Configuration);
        }
    }
}
