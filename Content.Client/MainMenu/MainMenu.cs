// SPDX-FileCopyrightText: 2020 DTanxxx <55208219+DTanxxx@users.noreply.github.com>
// SPDX-FileCopyrightText: 2020 Exp <theexp111@gmail.com>
// SPDX-FileCopyrightText: 2021 Acruid <shatter66@gmail.com>
// SPDX-FileCopyrightText: 2021 Galactic Chimp <63882831+GalacticChimp@users.noreply.github.com>
// SPDX-FileCopyrightText: 2022 DrSmugleaf <DrSmugleaf@users.noreply.github.com>
// SPDX-FileCopyrightText: 2022 Jezithyr <Jezithyr.@gmail.com>
// SPDX-FileCopyrightText: 2022 Jezithyr <Jezithyr@gmail.com>
// SPDX-FileCopyrightText: 2022 Jezithyr <jmaster9999@gmail.com>
// SPDX-FileCopyrightText: 2022 Pieter-Jan Briers <pieterjan.briers+git@gmail.com>
// SPDX-FileCopyrightText: 2022 ShadowCommander <10494922+ShadowCommander@users.noreply.github.com>
// SPDX-FileCopyrightText: 2022 Visne <39844191+Visne@users.noreply.github.com>
// SPDX-FileCopyrightText: 2022 mirrorcult <lunarautomaton6@gmail.com>
// SPDX-FileCopyrightText: 2022 wrexbe <81056464+wrexbe@users.noreply.github.com>
// SPDX-FileCopyrightText: 2022 wrexbe <wrexbe@protonmail.com>
// SPDX-FileCopyrightText: 2023 metalgearsloth <31366439+metalgearsloth@users.noreply.github.com>
// SPDX-FileCopyrightText: 2024 Winkarst <74284083+Winkarst-cpu@users.noreply.github.com>
// SPDX-FileCopyrightText: 2025 Aiden <28298836+Aidenkrz@users.noreply.github.com>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Text.RegularExpressions;
using Content.Client.MainMenu.UI;
using Content.Client.UserInterface.Systems.EscapeMenu;
using Robust.Client;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.Network;
using Robust.Shared.Utility;
using UsernameHelpers = Robust.Shared.AuthLib.UsernameHelpers;
using System.Linq;
using System.Numerics;
using System.Text;
using Content.Client.Resources;
using Content.Shared.Arcade;
using Content.Shared.Input;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Utility;
using static Robust.Client.UserInterface.Controls.BoxContainer;

namespace Content.Client.MainMenu
{
    /// <summary>
    ///     Main menu screen that is the first screen to be displayed when the game starts.
    /// </summary>
    // Instantiated dynamically through the StateManager, Dependencies will be resolved.
    public sealed class MainScreen : Robust.Client.State.State
    {
        [Dependency] private readonly IBaseClient _client = default!;
        [Dependency] private readonly IClientNetManager _netManager = default!;
        [Dependency] private readonly IConfigurationManager _configurationManager = default!;
        [Dependency] private readonly IGameController _controllerProxy = default!;
        [Dependency] private readonly IResourceCache _resourceCache = default!;
        [Dependency] private readonly IUserInterfaceManager _userInterfaceManager = default!;
        [Dependency] private readonly ILogManager _logManager = default!;
        // Paradox-Start: Discord auth deny window dependencies
        [Dependency] private readonly IClipboardManager _clipboard = default!;
        [Dependency] private readonly IUriOpener _uri = default!;
        private DefaultWindow? _discordAuthWindow;
        // Paradox-End

        private ISawmill _sawmill = default!;

        private MainMenuControl _mainMenuControl = default!;
        private bool _isConnecting;

        // ReSharper disable once InconsistentNaming
        private static readonly Regex IPv6Regex = new(@"\[(.*:.*:.*)](?::(\d+))?");

        /// <inheritdoc />
        protected override void Startup()
        {
            _sawmill = _logManager.GetSawmill("mainmenu");

            _mainMenuControl = new MainMenuControl(_resourceCache, _configurationManager);
            _userInterfaceManager.StateRoot.AddChild(_mainMenuControl);

            _mainMenuControl.QuitButton.OnPressed += QuitButtonPressed;
            _mainMenuControl.OptionsButton.OnPressed += OptionsButtonPressed;
            _mainMenuControl.DirectConnectButton.OnPressed += DirectConnectButtonPressed;
            _mainMenuControl.AddressBox.OnTextEntered += AddressBoxEntered;
            _mainMenuControl.ChangelogButton.OnPressed += ChangelogButtonPressed;

            _client.RunLevelChanged += RunLevelChanged;
        }

        /// <inheritdoc />
        protected override void Shutdown()
        {
            _client.RunLevelChanged -= RunLevelChanged;
            _netManager.ConnectFailed -= _onConnectFailed;

            _mainMenuControl.Dispose();
        }

        private void ChangelogButtonPressed(BaseButton.ButtonEventArgs args)
        {
            _userInterfaceManager.GetUIController<ChangelogUIController>().ToggleWindow();
        }

        private void OptionsButtonPressed(BaseButton.ButtonEventArgs args)
        {
            _userInterfaceManager.GetUIController<OptionsUIController>().ToggleWindow();
        }

        private void QuitButtonPressed(BaseButton.ButtonEventArgs args)
        {
            _controllerProxy.Shutdown();
        }

        private void DirectConnectButtonPressed(BaseButton.ButtonEventArgs args)
        {
            var input = _mainMenuControl.AddressBox;
            TryConnect(input.Text);
        }

        private void AddressBoxEntered(LineEdit.LineEditEventArgs args)
        {
            if (_isConnecting)
            {
                return;
            }

            TryConnect(args.Text);
        }

        private void TryConnect(string address)
        {
            var inputName = _mainMenuControl.UsernameBox.Text.Trim();
            if (!UsernameHelpers.IsNameValid(inputName, out var reason))
            {
                var invalidReason = Loc.GetString(reason.ToText());
                _userInterfaceManager.Popup(
                    Loc.GetString("main-menu-invalid-username-with-reason", ("invalidReason", invalidReason)),
                    Loc.GetString("main-menu-invalid-username"));
                return;
            }

            var configName = _configurationManager.GetCVar(CVars.PlayerName);
            if (_mainMenuControl.UsernameBox.Text != configName)
            {
                _configurationManager.SetCVar(CVars.PlayerName, inputName);
                _configurationManager.SaveToFile();
            }

            _setConnectingState(true);
            _netManager.ConnectFailed += _onConnectFailed;
            try
            {
                ParseAddress(address, out var ip, out var port);
                _client.ConnectToServer(ip, port);
            }
            catch (ArgumentException e)
            {
                _userInterfaceManager.Popup($"Unable to connect: {e.Message}", "Connection error.");
                _sawmill.Warning(e.ToString());
                _netManager.ConnectFailed -= _onConnectFailed;
                _setConnectingState(false);
            }
        }

        private void RunLevelChanged(object? obj, RunLevelChangedEventArgs args)
        {
            switch (args.NewLevel)
            {
                case ClientRunLevel.Connecting:
                    _setConnectingState(true);
                    break;
                case ClientRunLevel.Initialize:
                    _setConnectingState(false);
                    _netManager.ConnectFailed -= _onConnectFailed;
                    break;
            }
        }

        private void ParseAddress(string address, out string ip, out ushort port)
        {
            var match6 = IPv6Regex.Match(address);
            if (match6 != Match.Empty)
            {
                ip = match6.Groups[1].Value;
                if (!match6.Groups[2].Success)
                {
                    port = _client.DefaultPort;
                }
                else if (!ushort.TryParse(match6.Groups[2].Value, out port))
                {
                    throw new ArgumentException("Not a valid port.");
                }

                return;
            }

            // See if the IP includes a port.
            var split = address.Split(':');
            ip = address;
            port = _client.DefaultPort;
            if (split.Length > 2)
            {
                throw new ArgumentException("Not a valid Address.");
            }

            // IP:port format.
            if (split.Length == 2)
            {
                ip = split[0];
                if (!ushort.TryParse(split[1], out port))
                {
                    throw new ArgumentException("Not a valid port.");
                }
            }
        }

        private void _onConnectFailed(object? _, NetConnectFailArgs args)
        {
            _sawmill.Warning($"Connect failed reason RAW: {args.Reason}");

            if (TryParseDiscordAuthDeny(args.Reason, out var code, out var message))
            {
                ShowDiscordAuthDenyWindow(code, message);

                _netManager.ConnectFailed -= _onConnectFailed;
                _setConnectingState(false);
                return;
            }

            _userInterfaceManager.Popup(
                Loc.GetString("main-menu-failed-to-connect", ("reason", args.Reason)));

            _netManager.ConnectFailed -= _onConnectFailed;
            _setConnectingState(false);
        }

        private void _setConnectingState(bool state)
        {
            _isConnecting = state;
            _mainMenuControl.DirectConnectButton.Disabled = state;
        }

        private bool TryParseDiscordAuthDeny(string reason, out int code, out string message)
        {
            code = 0;
            message = string.Empty;

            var index = reason.IndexOf("DISCORD_AUTH_DENY|");
            if (index == -1)
                return false;

            var payload = reason.Substring(index);

            var parts = payload.Split('|', 3);
            if (parts.Length != 3)
                return false;

            if (!int.TryParse(parts[1], out code))
                return false;

            message = parts[2];
            return true;
        }

        private void ShowDiscordAuthDenyWindow(int code, string message)
        {
            if (_discordAuthWindow is { Disposed: false })
            {
                _discordAuthWindow.OpenCentered();
                return;
            }

            var vbox = new BoxContainer
            {
                Orientation = LayoutOrientation.Vertical,
                SeparationOverride = 15,
                Margin = new Thickness(15)
            };

            vbox.AddChild(new Label
            {
                Text = "❌ Требуется авторизация через Discord",
                HorizontalAlignment = Control.HAlignment.Center
            });

            vbox.AddChild(new RichTextLabel
            {
                Text = message
            });

            var codeRow = new BoxContainer
            {
                Orientation = LayoutOrientation.Horizontal,
                SeparationOverride = 10,
                HorizontalAlignment = Control.HAlignment.Center
            };

            codeRow.AddChild(new Label { Text = "Код:" });

            var codeLabel = new Label
            {
                Text = code.ToString(),
                StyleClasses = { "LabelBig" }
            };

            codeRow.AddChild(codeLabel);

            vbox.AddChild(codeRow);

            var copyBtn = new Button
            {
                Text = "📋 Скопировать код"
            };

            copyBtn.OnPressed += _ =>
            {
                _clipboard.SetText(code.ToString());
                _userInterfaceManager.Popup("Код скопирован!");
            };

            var channelBtn = new Button
            {
                Text = "➡️ Открыть канал авторизации"
            };

            channelBtn.OnPressed += _ =>
                _uri.OpenUri("https://discord.com/channels/901772674865455115/1351213738774237184");

            var discordBtn = new Button
            {
                Text = "➡️ Открыть Discord сервер"
            };

            discordBtn.OnPressed += _ =>
                _uri.OpenUri("https://discord.com/invite/NY3KDNuH9r");

            var closeBtn = new Button
            {
                Text = "Закрыть"
            };

            closeBtn.OnPressed += _ => _discordAuthWindow?.Close();

            vbox.AddChild(copyBtn);
            vbox.AddChild(channelBtn);
            vbox.AddChild(discordBtn);
            vbox.AddChild(closeBtn);

            _discordAuthWindow = new DefaultWindow
            {
                Title = "Discord авторизация",
                MinSize = new Vector2(420, 320)
            };

            _discordAuthWindow.OnClose += () =>
            {
                _discordAuthWindow = null;
            };

            _discordAuthWindow.Contents.AddChild(vbox);

            _discordAuthWindow.OpenCentered();
        }
    }
}
