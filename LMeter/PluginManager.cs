﻿using System;
using System.Net.Http;
using System.Numerics;
using System.Text.RegularExpressions;
using Dalamud.Game.Command;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using ImGuiNET;
using LMeter.Act;
using LMeter.Config;
using LMeter.Helpers;
using LMeter.Meter;
using LMeter.Windows;
using Microsoft.ClearScript.V8;

namespace LMeter
{
    public class PluginManager : IPluginDisposable
    {
        private readonly Vector2 _configSize = new(650, 750);

        private readonly IClientState _clientState;
        private readonly IDalamudPluginInterface _pluginInterface;
        private readonly ICommandManager _commandManager;
        private readonly WindowSystem _windowSystem;
        private readonly ConfigWindow _configRoot;
        private readonly LMeterConfig _config;
        private Vector2 _origin;

        private readonly ImGuiWindowFlags _mainWindowFlags =
            ImGuiWindowFlags.NoTitleBar |
            ImGuiWindowFlags.NoScrollbar |
            ImGuiWindowFlags.AlwaysAutoResize |
            ImGuiWindowFlags.NoBackground |
            ImGuiWindowFlags.NoInputs |
            ImGuiWindowFlags.NoBringToFrontOnFocus |
            ImGuiWindowFlags.NoSavedSettings;

        public PluginManager(
            IClientState clientState,
            ICommandManager commandManager,
            IDalamudPluginInterface pluginInterface,
            LMeterConfig config)
        {
            _clientState = clientState;
            _commandManager = commandManager;
            _pluginInterface = pluginInterface;
            _config = config;

            _origin = ImGui.GetMainViewport().Size / 2f;
            _configRoot = new ConfigWindow("ConfigRoot", _origin, _configSize);
            _windowSystem = new WindowSystem("LMeter");
            _windowSystem.AddWindow(_configRoot);

            _commandManager.AddHandler(
                "/lm",
                new CommandInfo(PluginCommand)
                {
                    HelpMessage = "Opens the LMeter configuration window.\n"
                                + "/lm end → Ends current Act Encounter.\n"
                                + "/lm clear → Clears all Act encounter log data.\n"
                                + "/lm ct <number> → Toggles clickthrough status for the given profile.\n"
                                + "/lm toggle <number> [on|off] → Toggles visibility for the given profile.",
                    ShowInHelp = true
                }
            );

            _clientState.Logout += OnLogout;
            _pluginInterface.UiBuilder.OpenConfigUi += OpenConfigUi;
            _pluginInterface.UiBuilder.Draw += Draw;
        }

        private void Draw()
        {
            if (_clientState.LocalPlayer == null || CharacterState.IsCharacterBusy())
            {
                return;
            }

            _origin = ImGui.GetMainViewport().Size / 2f;
            _windowSystem.Draw();
            _config.ActConfig.TryReconnect();
            _config.ActConfig.TryEndEncounter();

            ImGuiHelpers.ForceNextWindowMainViewport();
            ImGui.SetNextWindowPos(Vector2.Zero);
            ImGui.SetNextWindowSize(ImGui.GetMainViewport().Size);
            if (ImGui.Begin("LMeter_Root", _mainWindowFlags))
            {
                Singletons.Get<ClipRectsHelper>().Update();
                foreach (MeterWindow meter in _config.MeterList.Meters)
                {
                    meter.Draw(_origin);
                }
            }

            ImGui.End();
        }

        public void Clear()
        {
            Singletons.Get<LogClient>().Clear();
            foreach (MeterWindow meter in _config.MeterList.Meters)
            {
                meter.Clear();
            }
        }

        public void ChangeClientType(int clientType)
        {
            if (!Singletons.IsRegistered<LogClient>())
            {
                return;
            }

            LogClient oldClient = Singletons.Get<LogClient>();
            oldClient.Shutdown();

            LogClient newClient = clientType switch
            {
                1 => new IpcClient(_config.ActConfig),
                _ => new WebSocketClient(_config.ActConfig)
            };

            newClient.Start();
            Singletons.Update(newClient);
        }

        public void Edit(IConfigurable configItem)
        {
            _configRoot.PushConfig(configItem);
        }

        public void ConfigureMeter(MeterWindow meter)
        {
            if (!_configRoot.IsOpen)
            {
                this.OpenConfigUi();
                this.Edit(meter);
            }
        }

        private void OpenConfigUi()
        {
            if (!_configRoot.IsOpen)
            {
                _configRoot.PushConfig(_config);
            }
        }

        private void OnLogout()
        {
            ConfigHelpers.SaveConfig();
        }

        private void PluginCommand(string command, string arguments)
        {
            try
            {
                // string regex = @"https://assets.rpglogs.com/js/log-parsers/parser-ff\.[a-f0-9]+\.js";
                // Regex parseRegex = new Regex(regex);
                // HttpClient httpClient = new();
                // httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("LMeter/0.4.0.2 (+https://github.com/lichie567/LMeter)");
                // var response = httpClient.GetAsync("https://www.fflogs.com/desktop-client/parser").GetAwaiter().GetResult();
                // string result = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                // var match = parseRegex.Match(result);
                // string? parser_url = null;

                // if (match.Success)
                // {
                //     parser_url = match.Groups[0].ToString();
                //     response = httpClient.GetAsync(parser_url).GetAwaiter().GetResult();
                //     string script = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                //     Singletons.Get<IPluginLog>().Info($"parser_url: {parser_url}");
                //     Singletons.Get<IPluginLog>().Info($"script: {script}");

                //     if (!string.IsNullOrEmpty(script))
                //     {
                //         var engine = new V8ScriptEngine();
                //         // engine.Compile(script);
                //         var scriptResult = engine.Evaluate($"var window = {{}}; var parserObject = {script}");
                //         Singletons.Get<IPluginLog>().Info($"{result}");
                //     }
                // }

                // FFLogsClient client = new();
                // client.CollectMeters();

            }
            catch (Exception ex)
            {
                Singletons.Get<IPluginLog>().Error($"exception: {ex}");
            }


            string[] argArray = arguments.Split(" ");
            switch (argArray)
            {
                case {} args when args[0].Equals("end"):
                    Singletons.Get<LogClient>().EndEncounter();
                    break;
                case {} args when args[0].Equals("clear"):
                    this.Clear();
                    break;
                case { } args when args[0].Equals("toggle"):
                    _config.MeterList.ToggleMeter(args.Length > 1 ? GetIntArg(args[1]) - 1 : null);
                    break;
                case { } args when args[0].Equals("ct"):
                    _config.MeterList.ToggleClickThrough(args.Length > 1 ? GetIntArg(args[1]) - 1 : null);
                    break;
                default:
                    this.ToggleWindow();
                    break;
            }
        }

        private static int GetIntArg(string argument)
        {
            return !string.IsNullOrEmpty(argument) && int.TryParse(argument, out int num) ? num : 0;
        }

        private void ToggleWindow()
        {
            if (_configRoot.IsOpen)
            {
                _configRoot.IsOpen = false;
            }
            else
            {
                _configRoot.PushConfig(_config);
            }
        }

        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Don't modify order
                _pluginInterface.UiBuilder.Draw -= Draw;
                _pluginInterface.UiBuilder.OpenConfigUi -= OpenConfigUi;
                _clientState.Logout -= OnLogout;
                _commandManager.RemoveHandler("/lm");
                _windowSystem.RemoveAllWindows();
            }
        }
    }
}
