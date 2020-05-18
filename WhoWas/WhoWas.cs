using Dalamud.Game.ClientState.Actors.Types;
using Dalamud.Game.Command;
using Dalamud.Plugin;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game.Chat;
using Dalamud.Game.Chat.SeStringHandling;
using Dalamud.Game.Chat.SeStringHandling.Payloads;

namespace WhoWas
{
    internal static class WhoWasResources
    {
        public static readonly string CrossWorldIcon = Encoding.UTF8.GetString(new byte[] {
            0x02, 0x12, 0x02, 0x59, 0x03
        });
    }

    public class WhoWas : IDalamudPlugin
    {
        private bool isCurrentPlayerChecked;
        private CancellationTokenSource tokenSource;
        private DalamudPluginInterface pluginInterface;
        private ConcurrentQueue<KeyValuePair<string, string>> playerQueue;
        private WhoWasConfiguration config;

        public string Name => "WhoWas";

        public void Initialize(DalamudPluginInterface pluginInterface)
        {
            this.pluginInterface = pluginInterface;

            this.config = (WhoWasConfiguration)this.pluginInterface.GetPluginConfig() ?? new WhoWasConfiguration();
            this.config.Initialize(this.pluginInterface);

            this.playerQueue = new ConcurrentQueue<KeyValuePair<string, string>>();

            this.pluginInterface.Framework.Gui.Chat.OnChatMessage += OnChatMessage;
            this.pluginInterface.UiBuilder.OnBuildUi += OnUpdate;

            AddComandHandlers();

            this.tokenSource = new CancellationTokenSource();

            MakeXIVAPIRequests(this.tokenSource.Token);
        }

        private void OnChatMessage(XivChatType type, uint senderId, ref SeString sender, ref SeString message, ref bool isHandled)
        {
            var payload = (sender ?? message)?.Payloads.FirstOrDefault(p => p is PlayerPayload);
            if (payload == null)
                return;

            var player = payload as PlayerPayload;

            // ReSharper disable PossibleNullReferenceException
            if (this.playerQueue.All(p => p.Key != player.PlayerName && p.Value != player.World.Name))
                this.playerQueue.Enqueue(new KeyValuePair<string, string>(player.PlayerName, player.World.Name));
            // ReSharper restore PossibleNullReferenceException
        }

        private async Task MakeXIVAPIRequests(CancellationToken token)
        {
            while (true)
            {
                if (token.IsCancellationRequested)
                    token.ThrowIfCancellationRequested();
                
                if (this.playerQueue.TryDequeue(out var player))
                {
                    await CachePlayer(player.Key, player.Value, token);
                    this.config.Save();
                }

                await Task.Delay(1000, token);
            }
        }

        private void OnUpdate()
        {
            if (this.pluginInterface.ClientState.LocalPlayer == null)
            {
                this.isCurrentPlayerChecked = false; // Possibly switching characters
                return;
            }

            if (!this.isCurrentPlayerChecked)
            {
                var localPlayer = this.pluginInterface.ClientState.LocalPlayer;
                this.playerQueue.Enqueue(new KeyValuePair<string, string>(localPlayer.Name, localPlayer.HomeWorld.GameData.Name)); // Ensure that the logged-in player is checked-in.
                this.isCurrentPlayerChecked = true;
            }

            foreach (var actor in this.pluginInterface.ClientState.Actors)
            {
                // Is indiscriminately making an HTTP call for the Lodestone IDs of every single player who happens to load in efficient in any way, shape or form?
                // Is it efficient to do this multiple times per player?
                // Probably not, but I can't be bothered to find content IDs instead, assuming they're even in memory somewhere.
                // And you can bet this won't be easy to convert to use content IDs even if they are found in memory.
                if (actor is PlayerCharacter player && this.playerQueue.All(p => p.Key != player.Name && p.Value != player.HomeWorld.GameData.Name))
                {
                    this.playerQueue.Enqueue(new KeyValuePair<string, string>(player.Name, player.HomeWorld.GameData.Name));
                }
            }
        }

        private void WhoWasCommand(string command, string args)
            => WhoWasCommandAsync(args);

        private async Task WhoWasCommandAsync(string args)
        {
            var parsedArgs = args.Split(' ');

            if (parsedArgs.Length != 3)
                return;

            this.pluginInterface.Framework.Gui.Chat.Print("Searching...");

            var firstName = parsedArgs[0];
            var lastName = parsedArgs[1];
            var formattedName = $"{Capitalize(firstName)} {Capitalize(lastName)}";

            var world = parsedArgs[2];

            var lodestoneId = await GetCharacterLodestoneId(formattedName, world);

            var character = this.config.Characters.FirstOrDefault(ch => ch.LodestoneId == lodestoneId);
            if (character == null)
            {
                this.pluginInterface.Framework.Gui.Chat.Print("No character matching that query has been cached.");
                return;
            }

            var output = $"{formattedName}{WhoWasResources.CrossWorldIcon}{Capitalize(world)} used to be:";
            output = character.NameWorlds
                .Aggregate(output, (current, charaInfo) => current + $"\n   {charaInfo.Key}{WhoWasResources.CrossWorldIcon}{charaInfo.Value}");

            this.pluginInterface.Framework.Gui.Chat.Print(output);
        }

        private void WhoIsCachedCommand(string command, string args)
        {
            this.pluginInterface.Framework.Gui.Chat.Print($"{this.config.Characters.Count} players cached.");
            foreach (var player in this.config.Characters)
            {
                this.pluginInterface.Framework.Gui.Chat.Print($"{player.NameWorlds.First().Key}{WhoWasResources.CrossWorldIcon}{player.NameWorlds.First().Value}");
                foreach (var nameWorld in player.NameWorlds.Skip(1))
                {
                    this.pluginInterface.Framework.Gui.Chat.Print($"   {nameWorld.Key}{WhoWasResources.CrossWorldIcon}{nameWorld.Value}");
                }
            }
        }

        private async Task CachePlayer(string fullName, string world, CancellationToken token)
        {
#if DEBUG
            this.pluginInterface.Log($"Caching {fullName} ({world})");
#endif

            var lodestoneId = await GetCharacterLodestoneId(fullName, world, token);
#if DEBUG
            this.pluginInterface.Log($"Got Lodestone ID {lodestoneId} for {fullName} ({world})");
#endif

            var existing = this.config.Characters.FirstOrDefault(chara => chara.LodestoneId == lodestoneId);
            if (existing != null)
            {
                if (!existing.NameWorlds.Contains(new KeyValuePair<string, string>(fullName, world)))
                    existing.NameWorlds.Add(fullName, world);
            }
            else if (lodestoneId != 0)
            {
                var character = new Character
                {
                    LodestoneId = lodestoneId
                };
                this.config.Characters.Add(character);
                character.NameWorlds.Add(fullName, world);
            }
        }

        private async Task<ulong> GetCharacterLodestoneId(string fullName, string world, CancellationToken token = default)
        {
            using var http = new HttpClient();

            // TODO make this not hammer XIVAPI
            HttpResponseMessage resRaw;
            try
            {
                resRaw = await http.GetAsync($"https://xivapi.com/character/search?name={fullName}&server={world}",
                    token);
            }
            catch (HttpRequestException e)
            {
                this.pluginInterface.LogError(e.Message + $"\nError code: {e.HResult}");
                return 0;
            }

            dynamic res;
            try
            {
                res = JsonConvert.DeserializeObject(await resRaw.Content.ReadAsStringAsync());
            }
            catch (JsonReaderException e)
            {
                this.pluginInterface.LogError(e.Message + $"\nSource text: {resRaw}");
                return 0;
            }

            var lodestoneId = 0UL;
            foreach (var result in res.Results)
            {
                if (result.Name == fullName && ((string)result.Server).StartsWith(world)) // The first result isn't always correct because sasuga SE
                {
                    lodestoneId = result.ID;
                }
            }

            return lodestoneId;
        }

        private static string Capitalize(string input)
            => char.ToUpperInvariant(input[0]) + input.ToLowerInvariant().Substring(1);

        private void AddComandHandlers()
        {
            this.pluginInterface.CommandManager.AddHandler("/whowas", new CommandInfo(WhoWasCommand)
            {
                HelpMessage = "See a player's previous character names.",
                ShowInHelp = true,
            });
            this.pluginInterface.CommandManager.AddHandler("/whois", new CommandInfo(WhoWasCommand)
            {
                ShowInHelp = true,
            });

            this.pluginInterface.CommandManager.AddHandler("/whowascached", new CommandInfo(WhoIsCachedCommand)
            {
                HelpMessage = "See who was cached.",
                ShowInHelp = true,
            });
            this.pluginInterface.CommandManager.AddHandler("/whoiscached", new CommandInfo(WhoIsCachedCommand)
            {
                ShowInHelp = true,
            });
        }

        private void RemoveCommandHandlers()
        {
            this.pluginInterface.CommandManager.RemoveHandler("/whowas");
            this.pluginInterface.CommandManager.RemoveHandler("/whoiscached");
        }

        #region IDisposable Support
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                this.tokenSource.Cancel();
                this.tokenSource.Dispose();

                RemoveCommandHandlers();

                this.pluginInterface.UiBuilder.OnBuildUi -= OnUpdate;
                this.pluginInterface.Framework.Gui.Chat.OnChatMessage -= OnChatMessage;

                this.config.Save();

                this.pluginInterface.Dispose();
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        #endregion
    }
}
