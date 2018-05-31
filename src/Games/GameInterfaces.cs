using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using PacManBot.Services;
using static PacManBot.Games.GameUtils;

namespace PacManBot.Games
{
    public interface IBaseGame
    {
        // Type members
        string Name { get; } // Display name of the game
        TimeSpan Expiry { get; } // How long until the game times out

        // Game members
        State State { get; set; }
        DateTime LastPlayed { get; set; }
        int Time { get; set; } // Current turn 
        ulong[] UserId { get; set; } // Players

        // Discord
        RequestOptions RequestOptions { get; } // Used when modifying the game message
        Action<MessageProperties> UpdateMessage { get; } // Edit a Discord message with the game

        string GetContent(bool showHelp = true);
        EmbedBuilder GetEmbed(bool showHelp = true);
        void CancelRequests(); // Cancels previous game message edits
    }


    public interface IChannelGame : IBaseGame
    {
        ulong MessageId { get; set; }
        ulong ChannelId { get; set; }

        ISocketMessageChannel Channel { get; }
        SocketGuild Guild { get; }
        Task<IUserMessage> GetMessage();
    }


    public interface IMessagesGame : IChannelGame
    {
        bool IsInput(string value);
        void DoTurn(string input);
    }


    public interface IReactionsGame : IChannelGame
    {
        bool IsInput(IEmote value);
        void DoTurn(IEmote input);
    }


    public interface IStoreableGame : IBaseGame
    {
        string GameFile { get; }
        void SetServices(DiscordShardedClient client, LoggingService logger, StorageService storage);
    }
}