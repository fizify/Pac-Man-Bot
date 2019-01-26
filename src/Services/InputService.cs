using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using PacManBot.Extensions;
using PacManBot.Games;
using PacManBot.Games.Concrete;
using PacManBot.Services.Database;

namespace PacManBot.Services
{
    /// <summary>
    /// Handles all external input coming from Discord, using it for commands and games.
    /// </summary>
    public class InputService
    {
        private readonly IServiceProvider services;
        private readonly PmDiscordClient client;
        private readonly PmCommandService commands;
        private readonly StorageService storage;
        private readonly LoggingService log;
        private readonly GameService games;

        private readonly ulong[] bannedChannels;

        private static readonly Regex WakaRegex = new Regex(@"^(w+a+k+a+\W*)+$", RegexOptions.IgnoreCase);


        public InputService(IServiceProvider services, PmDiscordClient client, PmCommandService commands,
            StorageService storage, LoggingService log, GameService games, PmConfig config)
        {
            this.services = services;
            this.client = client;
            this.commands = commands;
            this.storage = storage;
            this.log = log;
            this.games = games;

            bannedChannels = config.bannedChannels;
        }


        /// <summary>Start listening to input events from Discord.</summary>
        public void StartListening()
        {
            client.MessageReceived += OnMessageReceived;
            client.ReactionAdded += OnReactionAdded;
            client.ReactionRemoved += OnReactionRemoved;
        }


        /// <summary>Stop listening to input events from Discord.</summary>
        public void StopListening()
        {
            client.MessageReceived -= OnMessageReceived;
            client.ReactionAdded -= OnReactionAdded;
            client.ReactionRemoved -= OnReactionRemoved;
        }



        private Task OnMessageReceived(SocketMessage m)
        {
            OnMessageReceivedAsync(m); // Fire and forget
            return Task.CompletedTask;
        }


        private Task OnReactionAdded(Cacheable<IUserMessage, ulong> m, ISocketMessageChannel c, SocketReaction r)
        {
            OnReactionChangedAsync(m, c, r);
            return Task.CompletedTask;
        }


        private Task OnReactionRemoved(Cacheable<IUserMessage, ulong> m, ISocketMessageChannel c, SocketReaction r)
        {
            OnReactionChangedAsync(m, c, r);
            return Task.CompletedTask;
        }




        private async void OnMessageReceivedAsync(SocketMessage genericMessage)
        {
            try
            {
                if (bannedChannels.Contains(genericMessage.Channel.Id))
                {
                    if (genericMessage.Channel is IGuildChannel guildChannel) await guildChannel.Guild.LeaveAsync();
                    return;
                }

                if (genericMessage is SocketUserMessage message && !message.Author.IsBot
                    && await message.Channel.BotCan(ChannelPermission.SendMessages))
                {
                    // Only runs one
                    if (await MessageGameInputAsync(message) || await CommandAsync(message) || await AutoresponseAsync(message)) { }
                }
            }
            catch (Exception e)
            {
                log.Exception($"In {genericMessage.Channel.FullName()}", e);
            }
        }


        private async void OnReactionChangedAsync(Cacheable<IUserMessage, ulong> messageData, ISocketMessageChannel channel, SocketReaction reaction)
        {
            try
            {
                if (!await channel.BotCan(ChannelPermission.ReadMessageHistory)) return;

                var message = reaction.Message.Value ?? await messageData.GetOrDownloadAsync();

                if (reaction.UserId != client.CurrentUser.Id && message?.Author.Id == client.CurrentUser.Id)
                {
                    await ReactionGameInputAsync(message, channel, reaction);
                }
            }
            catch (Exception e)
            {
                log.Exception($"In {channel.FullName()}", e);
            }
        }




        /// <summary>Tries to find and execute a command. Returns whether it is successful.</summary>
        private async Task<bool> CommandAsync(SocketUserMessage message)
        {
            var result = await commands.TryExecuteAsync(message);
            if (result.IsSuccess) return true;
            else if (result.Error != CommandError.UnknownCommand && result.ErrorReason != null)
            {
                await message.Channel.SendMessageAsync(result.ErrorReason, options: PmBot.DefaultOptions);
            }

            return false;
        }


        /// <summary>Tries to find special messages to respond to. Returns whether it is successful.</summary>
        private async Task<bool> AutoresponseAsync(SocketUserMessage message)
        {
            if (!(message.Channel is SocketGuildChannel gChannel) || await storage.AllowsAutoresponseAsync(gChannel.Guild))
            {
                if (WakaRegex.IsMatch(message.Content))
                {
                    await message.Channel.SendMessageAsync("waka", options: PmBot.DefaultOptions);
                    log.Verbose($"Waka at {message.Channel.FullName()}");
                    return true;
                }
                else if (message.Content == "sudo neat")
                {
                    await message.Channel.SendMessageAsync("neat", options: PmBot.DefaultOptions);
                    return true;
                }
            }

            return false;
        }


        /// <summary>Tries to find a game and execute message input. Returns whether it is successful.</summary>
        private async Task<bool> MessageGameInputAsync(SocketUserMessage message)
        {
            var game = games.GetForChannel<IMessagesGame>(message.Channel.Id);
            if (game == null || !game.IsInput(message.Content, message.Author.Id)) return false;

            try
            {
                await ExecuteGameInputAsync(game, message);
            }
            catch (Exception e)
            {
                log.Exception($"During input \"{message.Content}\" in {game.Channel.FullName()}", e, game.GameName);
            }

            return true;
        }


        /// <summary>Tries to find a game and execute reaction input. Returns whether it is successful.</summary>
        private async Task<bool> ReactionGameInputAsync(IUserMessage message, ISocketMessageChannel channel, SocketReaction reaction)
        {
            var game = games.AllGames
                .OfType<IReactionsGame>()
                .FirstOrDefault(g => g.MessageId == message.Id && g.IsInput(reaction.Emote, reaction.UserId));

            if (game == null) return false;

            try
            {
                await ExecuteGameInputAsync(game, reaction, message);
            }
            catch (Exception e)
            {
                log.Exception($"During input \"{reaction.Emote.ReadableName()}\" in {channel.FullName()}", e, game.GameName);
            }

            return true;
        }




        private async Task ExecuteGameInputAsync(IMessagesGame game, IUserMessage message)
        {
            var gameMessage = await game.GetMessage();

            log.Verbose(
                $"Input {message.Content} by {message.Author.FullName()} in {message.Channel.FullName()}",
                game.GameName);

            game.Input(message.Content, message.Author.Id);
            if (game is MultiplayerGame mGame)
            {
                while(mGame.BotTurn) mGame.BotInput();
            }
            if (game.State != State.Active) games.Remove(game);

            if (gameMessage != null && await message.Channel.BotCan(ChannelPermission.ManageMessages))
            {
                game.CancelRequests();
                try { await gameMessage.ModifyAsync(game.GetMessageUpdate(), game.GetRequestOptions()); }
                catch (OperationCanceledException) { }

                await message.DeleteAsync(PmBot.DefaultOptions);
            }
            else
            {
                game.CancelRequests();
                try
                {
                    var newMsg = await message.Channel.SendMessageAsync(game.GetContent(), false, game.GetEmbed()?.Build(), game.GetRequestOptions());
                    game.MessageId = newMsg.Id;
                }
                catch (OperationCanceledException) { }

                if (gameMessage != null) await gameMessage.DeleteAsync(PmBot.DefaultOptions);
            }
        }


        private async Task ExecuteGameInputAsync(IReactionsGame game, SocketReaction reaction, IUserMessage gameMessage)
        {
            var user = reaction.User.IsSpecified ? reaction.User.Value : client.GetUser(reaction.UserId);
            var channel = gameMessage.Channel;
            var guild = (channel as IGuildChannel)?.Guild;

            log.Verbose(
                $"Input {reaction.Emote.ReadableName()} by {user.FullName()} in {channel.FullName()}",
                game.GameName);

            game.Input(reaction.Emote, user.Id);

            if (game.State != State.Active)
            {
                if (!(game is IUserGame)) games.Remove(game);

                if (game is PacManGame pmGame && pmGame.State != State.Cancelled && !pmGame.custom)
                {
                    storage.AddScore(new ScoreEntry(pmGame.score, user.Id, pmGame.State, pmGame.Time,
                        user.NameandDisc(), $"{guild?.Name}/{channel.Name}", DateTime.Now));
                }

                if (await channel.BotCan(ChannelPermission.ManageMessages))
                {
                    await gameMessage.RemoveAllReactionsAsync(PmBot.DefaultOptions);
                }
            }

            game.CancelRequests();
            try { await gameMessage.ModifyAsync(game.GetMessageUpdate(), game.GetRequestOptions()); }
            catch (OperationCanceledException) { }
        }
    }
}
