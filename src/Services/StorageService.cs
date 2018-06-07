using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using Newtonsoft.Json;
using Microsoft.Extensions.Configuration;
using Discord;
using Discord.WebSocket;
using PacManBot.Games;
using PacManBot.Constants;

namespace PacManBot.Services
{
    public class StorageService
    {
        private readonly DiscordShardedClient client;
        private readonly LoggingService logger;
        private readonly Dictionary<ulong, string> prefixes;
        private readonly List<ScoreEntry> scoreEntries;
        private readonly List<IChannelGame> games; // Channel-specific games
        private readonly List<IBaseGame> userGames; // Non-channel specific games

        private readonly JsonSerializerSettings gameJsonSettings = new JsonSerializerSettings {
            ConstructorHandling = ConstructorHandling.AllowNonPublicDefaultConstructor,
        };
        private static readonly Dictionary<string, Type> StoreableGamesTypes = GetStoreableGameTypes();

        public string WakaExclude { get; private set; }
        public IReadOnlyList<IChannelGame> Games { get; private set; }
        public IReadOnlyList<IBaseGame> UserGames { get; private set; }

        public IConfigurationRoot BotContent { get; private set; } = null;
        public string DefaultPrefix { get; private set; }
        public string[] PettingMessages { get; private set; }
        public string[] SuperPettingMessages { get; private set; }
        public ulong[] NoPrefixChannels { get; private set; }



        public StorageService(DiscordShardedClient client, LoggingService logger, BotConfig config)
        {
            this.client = client;
            this.logger = logger;

            DefaultPrefix = config.defaultPrefix;
            prefixes = new Dictionary<ulong, string>();
            scoreEntries = new List<ScoreEntry>();
            games = new List<IChannelGame>();
            userGames = new List<IBaseGame>();

            Games = games.AsReadOnly();
            UserGames = userGames.AsReadOnly();

            LoadBotContent();
            LoadWakaExclude();
            LoadPrefixes();
            LoadScoreboard();
            LoadGames();
        }



        public string GetPrefix(ulong serverId)
        {
            return (prefixes.ContainsKey(serverId)) ? prefixes[serverId] : DefaultPrefix;
        }
        public string GetPrefix(IGuild guild = null)
        {
            return (guild == null) ? DefaultPrefix : GetPrefix(guild.Id);
        }


        public string GetPrefixOrEmpty(IGuild guild)
        {
            return (guild == null) ? "" : GetPrefix(guild.Id);
        }


        public void SetPrefix(ulong guildId, string prefix)
        {
            if (prefixes.ContainsKey(guildId))
            {
                string replace = "";

                if (prefix == DefaultPrefix)
                {
                    prefixes.Remove(guildId);
                }
                else
                {
                    prefixes[guildId] = prefix;
                    replace = $"{guildId} {Regex.Escape(prefix)}\n";
                }

                File.WriteAllText(BotFile.Prefixes, Regex.Replace(File.ReadAllText(BotFile.Prefixes), $@"{guildId}.*\n", replace));
            }
            else if (prefix != DefaultPrefix)
            {
                prefixes.Add(guildId, prefix);
                File.AppendAllText(BotFile.Prefixes, $"{guildId} {prefix}\n");
            }
        }


        public bool ToggleWaka(ulong guildId)
        {
            bool nowaka = WakaExclude.Contains($"{guildId}");
            if (nowaka)
            {
                WakaExclude = WakaExclude.Replace($"{guildId} ", "");
                File.WriteAllText(BotFile.WakaExclude, WakaExclude);
            }
            else
            {
                WakaExclude += $"{guildId} ";
                File.AppendAllText(BotFile.WakaExclude, $"{guildId} ");
            }

            return nowaka;
        }



        public void AddGame(IChannelGame game)
        {
            games.Add(game);
        }


        public void AddUserGame(IBaseGame game)
        {
            userGames.Add(game);
        }


        public void DeleteGame(IChannelGame game)
        {
            try
            {
                game.CancelRequests();
                if (game is IStoreableGame sGame && File.Exists(GameFile(sGame))) File.Delete(GameFile(sGame));
                games.Remove(game);
                logger.Log(LogSeverity.Verbose, LogSource.Storage, $"Removed {game.GetType().Name} at {game.ChannelId}");
            }
            catch (Exception e)
            {
                logger.Log(LogSeverity.Error, LogSource.Storage, $"Trying to remove game at {game.ChannelId}: {e}");
            }
        }


        public void DeleteUserGame(IBaseGame game)
        {
            try
            {
                game.CancelRequests();
                userGames.Remove(game);
                logger.Log(LogSeverity.Verbose, LogSource.Storage, $"Removed {game.GetType().Name} for {game.OwnerId}");
            }
            catch (Exception e)
            {
                logger.Log(LogSeverity.Error, LogSource.Storage, $"Trying to remove user game for {game.OwnerId}: {e}");
            }
        }

        public void StoreGame(IStoreableGame game)
        {
            File.WriteAllText(GameFile(game), JsonConvert.SerializeObject(game), Encoding.UTF8);
        }


        public IChannelGame GetGame(ulong channelId)
            => games.FirstOrDefault(g => g.ChannelId == channelId);

        public TGame GetGame<TGame>(ulong channelId) where TGame : IChannelGame
            => (TGame)games.FirstOrDefault(g => g.ChannelId == channelId && g is TGame);


        public IBaseGame GetUserGame(ulong channelId)
            => games.FirstOrDefault(g => g.ChannelId == channelId);

        public TGame GetUserGame<TGame>(ulong userId) where TGame : IBaseGame
            => (TGame)userGames.FirstOrDefault(g => g.OwnerId == userId && g is TGame);



        public void AddScore(ScoreEntry entry)
        {
            string scoreString = entry.ToString();
            logger.Log(LogSeverity.Info, LogSource.Storage, $"New scoreboard entry: {scoreString}");
            File.AppendAllText(BotFile.Scoreboard, $"\n{scoreString}");

            int index = scoreEntries.BinarySearch(entry);
            if (index < 0) index = ~index;
            scoreEntries.Insert(index, entry); //Adds entry in sorted position
        }


        public IReadOnlyList<ScoreEntry> GetScores(Utils.TimePeriod period = Utils.TimePeriod.all)
        {
            if (period == Utils.TimePeriod.all) return scoreEntries.AsReadOnly();

            var date = DateTime.Now;
            return scoreEntries.Where(s => (date - s.date).TotalHours <= (int)period).ToList().AsReadOnly();
        }




        public void LoadBotContent()
        {
            if (BotContent == null) BotContent = new ConfigurationBuilder().SetBasePath(AppContext.BaseDirectory).AddJsonFile(BotFile.Contents).Build();
            else BotContent.Reload();

            PettingMessages = BotContent["petting"].Split('\n', StringSplitOptions.RemoveEmptyEntries);
            SuperPettingMessages = BotContent["superpetting"].Split('\n', StringSplitOptions.RemoveEmptyEntries);
            NoPrefixChannels = BotContent["noprefix"].Split(',').Select(x => ulong.Parse(x)).ToArray();
            logger.LoadLogExclude(this);
        }


        private void LoadWakaExclude()
        {
            WakaExclude = File.Exists(BotFile.WakaExclude) ? File.ReadAllText(BotFile.WakaExclude) : "";
        }


        private void LoadPrefixes()
        {
            uint fail = 0;

            string[] lines = File.ReadAllLines(BotFile.Prefixes);
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].StartsWith('#') || string.IsNullOrWhiteSpace(lines[i])) continue;
                string[] data = lines[i].Split(' '); // Splits into guild ID and prefix

                if (data.Length == 2 && ulong.TryParse(data[0], out ulong ID))
                {
                    string prefix = data[1];
                    prefixes.Add(ID, prefix);
                }
                else
                {
                    logger.Log(LogSeverity.Error, LogSource.Storage, $"Invalid entry in {BotFile.Prefixes} at line {i}");
                    fail++;
                }
            }

            logger.Log(LogSeverity.Info, LogSource.Storage, $"Loaded {prefixes.Count} custom prefixes from {BotFile.Prefixes}{$" with {fail} errors".If(fail > 0)}.");
        }


        private void LoadScoreboard()
        {
            uint fail = 0;

            string[] lines = File.ReadAllLines(BotFile.Scoreboard);
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].StartsWith('#')) continue;

                if (ScoreEntry.TryParse(lines[i], out ScoreEntry newEntry))
                {
                    scoreEntries.Add(newEntry);
                }
                else
                {
                    logger.Log(LogSeverity.Error, LogSource.Storage, $"Invalid entry in {BotFile.Scoreboard} at line {i}");
                    fail++;
                }
            }

            scoreEntries.Sort(); // The list will stay sorted as new elements will be added in sorted position
            logger.Log(LogSeverity.Info, LogSource.Storage, $"Loaded {scoreEntries.Count} scoreboard entries from {BotFile.Scoreboard}{$" with {fail} errors".If(fail > 0)}.");
        }



        private void LoadGames()
        {
            if (!Directory.Exists(BotFile.GameFolder))
            {
                Directory.CreateDirectory(BotFile.GameFolder);
                logger.Log(LogSeverity.Warning, LogSource.Storage, $"Created missing directory \"{BotFile.GameFolder}\"");
                return;
            }

            uint fail = 0;
            bool firstFail = true;

            foreach (string file in Directory.GetFiles(BotFile.GameFolder))
            {
                if (file.EndsWith(BotFile.GameExtension))
                {
                    try
                    {
                        IStoreableGame game = null;
                        foreach (var type in StoreableGamesTypes)
                        {
                            if (file.Contains(type.Key))
                            {
                                game = (IStoreableGame)JsonConvert.DeserializeObject(File.ReadAllText(file), type.Value, gameJsonSettings);
                                if (game is ChannelGame cGame) games.Add(cGame);
                                else userGames.Add(game);
                                break;
                            }
                        }
                        game?.SetServices(client, logger, this);
                        // StoreGame(game); // Update old files
                    }
                    catch (Exception e)
                    {
                        logger.Log(LogSeverity.Error, LogSource.Storage, $"Couldn't load game at {file}: {(firstFail ? e.ToString() : e.Message)}");
                        Console.ReadLine();
                        fail++;
                        firstFail = false;
                    }
                }
            }

            logger.Log(LogSeverity.Info, LogSource.Storage, $"Loaded {games.Count + userGames.Count} games from previous session{$" with {fail} errors".If(fail > 0)}.");
        }


        public static string GameFile(IStoreableGame game)
        {
            ulong id = game is ChannelGame cGame ? cGame.ChannelId : game.UserId[0];
            return $"{BotFile.GameFolder}{game.FilenameKey}{id}{BotFile.GameExtension}";
        }


        private static Dictionary<string, Type> GetStoreableGameTypes()
        {
            return Assembly.GetAssembly(typeof(IStoreableGame)).GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract && t.GetInterfaces().Contains(typeof(IStoreableGame)))
                .Select(x => KeyValuePair.Create(((IStoreableGame)Activator.CreateInstance(x, true)).FilenameKey, x))
                .OrderByDescending(x => x.Key.Length)
                .ToDictionary(x => x.Key, x => x.Value);
        }
    }
}