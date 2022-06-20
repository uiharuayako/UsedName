﻿using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using System.IO;
using System.Reflection;
using Dalamud;
using Dalamud.Game;
using Dalamud.Game.ClientState;
using Dalamud.Game.Gui;
using Dalamud.ContextMenu;
using Dalamud.Interface.Internal.Notifications;
using XivCommon;
using System.Collections.Generic;
using System.Linq;

namespace UsedName
{
    public sealed class UsedName : IDalamudPlugin
    {

        private IDictionary<ulong, Configuration.PlayersNames> playersNameList;

        public string Name => "Used Name";

        private const string commandName = "/pname";

        private XivCommonBase Common { get; }
        public ChatGui Chat { get; private set; }

        public DalamudContextMenuBase ContextMenuBase { get; private set; }
        public ContextMenu ContextMenu { get; private set; }

        private DalamudPluginInterface PluginInterface { get; init; }
        private CommandManager CommandManager { get; init; }
        public Configuration Configuration { get; init; }
        private PluginUI PluginUi { get; init; }
        // interrupt between UI and ContextMenu
        internal string tempPlayerName { get; set;  } = "";


        public UsedName(
            [RequiredVersion("1.0")] DalamudPluginInterface pluginInterface,
            [RequiredVersion("1.0")] CommandManager commandManager,
            ChatGui chatGUI)
        {
            this.PluginInterface = pluginInterface;
            this.CommandManager = commandManager;
            this.ContextMenuBase = new DalamudContextMenuBase();

            this.Configuration = this.PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
            this.Configuration.Initialize(this.PluginInterface);

            this.playersNameList = Configuration.playersNameList;
            this.Common = new XivCommonBase();
            this.Chat = chatGUI;
            this.ContextMenu = new ContextMenu(this);


            // you might normally want to embed resources and load them from the manifest stream
            this.PluginUi = new PluginUI(this);

            this.CommandManager.AddHandler(commandName, new CommandInfo(OnCommand)
            {
                HelpMessage = "Use '/pname' or '/pname update' to update data from FriendList\n" +
                "Use '/pname search firstname lastname' to search 'firstname lastname's used name. I **recommend** using the right-click menu to search\n" +
                "Use '/pname nick firstname lastname nickname' set 'firstname lastname's nickname to 'nickname', only support player from FriendList\n" +
                "(Format require:first last nickname; first last nick name)\n" +
                "Use '/pname config' show plugin's setting"
            }) ;

            // first time
            if (Configuration.playersNameList.Count <= 0)
            {
                this.UpdatePlayerNames();
            }

            this.PluginInterface.UiBuilder.Draw += DrawUI;
            this.PluginInterface.UiBuilder.OpenConfigUi += DrawConfigUI;

        }

        public void Dispose()
        {
            this.PluginUi.Dispose();
            this.CommandManager.RemoveHandler(commandName);
            this.Common.Dispose();
            this.ContextMenu.Dispose();
        }

        private void OnCommand(string command, string args)
        {
            if (args == "update"|| args == "")
            {
                this.UpdatePlayerNames();
            }
            else if (args.StartsWith("search"))
            {
                var temp = args.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
                var targetName = "";
                if (temp.Length == 2)
                {
                    targetName = temp[1];
                }
                else if (temp.Length == 3)
                {
                    targetName = temp[1] + " " + temp[2];
                }
                else
                {
                    Chat.PrintError($"Parameter error, length is'{temp.Length}'");
                    return;
                }
                this.SearchPlayerResult(targetName);
            }
            else if (args.StartsWith("nick"))
            {
                //  "PalyerName nickname", "Palyer Name nick name", "Palyer Name nickname"
                string[] parseName = ParseNameText(args.Substring(4));
                string targetName = parseName[0];
                string nickName = parseName[1];
                this.AddNickName(targetName, nickName);
            }
            else if (args.StartsWith("config"))
            {
                this.DrawConfigUI();
            }
            else
            {
                Chat.PrintError($"Invalid parameter: {args}");
            }
        }


        private void DrawUI()
        {
            this.PluginUi.Draw();
        }

        internal void DrawMainUI()
        {
            this.PluginUi.Visible = true;
        }
        private void DrawConfigUI()
        {
            this.PluginUi.SettingsVisible = true;
        }

        internal void UpdatePlayerNames()
        {
            var friendList = Common.Functions.FriendList.List.GetEnumerator();
            while (friendList.MoveNext())
            {
                var player = friendList.Current;
                var contentId = player.ContentId;
                var name = player.Name.ToString();
                if (this.playersNameList.ContainsKey(contentId))
                {
                    if (!this.playersNameList[contentId].currentName.Equals(name))
                    {
                        this.playersNameList[contentId].usedNames.Add(name);
                        this.playersNameList[contentId].currentName = name;
                        if (Configuration.ShowNameChange)
                        {
                            var temp = string.IsNullOrEmpty(this.playersNameList[contentId].nickName) ? name : $"({this.playersNameList[contentId].nickName})";
                            Chat.Print($"{temp} changed name to {this.playersNameList[contentId].currentName}\n");
                        }
                    }
                }
                else
                {
                    this.playersNameList.Add(contentId, new Configuration.PlayersNames(name, "", new List<string> { }));
                }

            }
            this.Configuration.Save();
            Chat.Print("Update FriendList completed");
        }

        public IDictionary<ulong, Configuration.PlayersNames> SearchPlayer(string targetName, bool useNickName=false)
        {
            var result = new Dictionary<ulong, Configuration.PlayersNames>();
            targetName = targetName.ToLower();
            foreach (var player in this.playersNameList)
            {
                var current = player.Value.currentName.ToLower();
                var nickNmae = player.Value.nickName.ToLower();
                if (current.Equals(targetName) || (useNickName && nickNmae.ToLower().Equals(targetName.ToLower())) || player.Value.usedNames.Any(name => name.Equals(targetName)))
                {
                    result.Add(player.Key, player.Value);
                }
            }
            return result;
        }

        public string SearchPlayerResult(string targetName)
        {
            string result = "";
            foreach (var player in SearchPlayer(targetName, true))
            {
                var temp = string.IsNullOrEmpty(player.Value.nickName) ? "" : "(" + player.Value.nickName + ")";
                result += $"{player.Value.currentName}{temp}: [{string.Join(",", player.Value.usedNames)}]\n";
            }
            Chat.Print($"Search result(s) for target [{targetName}]:\n{result}");
            return result;
        }
        
        public XivCommon.Functions.FriendList.FriendListEntry GetPlayerByNameFromFriendList(string name)
        {
            var friendList = Common.Functions.FriendList.List.GetEnumerator();
            while (friendList.MoveNext())
            {
                var player = friendList.Current;
                if (player.Name.ToString().Equals(name))
                {
                    return player;
                }
            }
            return new XivCommon.Functions.FriendList.FriendListEntry();
        }

        private void AddNickName(string playerName, string nickName)
        {
            var player = SearchPlayer(playerName);
            if (player.Count == 0)
            {
                Chat.PrintError($"Cannot find player '{playerName}', Please try using '/pusedname update' to update FriendList, or check the spelling");
                return;
            }
            if (player.Count > 1)
            {
                Chat.PrintError($"Find multiple '{playerName}', please search for players using the exact name");
                return;
            }
            this.playersNameList[player.First().Key].nickName = nickName;
            this.Configuration.Save();
            Chat.Print($"The nickname of {playerName} has been set to {nickName}");
        }
        // name from command, try to solve "Palyer Name nick name", "Palyer Name nickname", not support "PalyerName nick name"
        public string[] ParseNameText(string text)
        {
            var playerName = "";
            var nickName = "";
            var name = text.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);

            if (name.Length == 4)
            {
                playerName = name[0] + " " + name[1];
                nickName = name[2] + " " + name[3];
            }
            else if (name.Length == 3)
            {
                playerName = name[0] + " " + name[1];
                nickName = name[2];
            }
            else if (name.Length == 2)
            {
                playerName = name[0];
                nickName = name[1];
            }

            return new string[] { playerName, nickName };

        }
    }
}
