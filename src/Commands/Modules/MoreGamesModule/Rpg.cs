﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.Net;
using PacManBot.Constants;
using PacManBot.Extensions;
using PacManBot.Games;
using PacManBot.Games.Concrete;
using PacManBot.Games.Concrete.Rpg;

namespace PacManBot.Commands.Modules
{
    [Name("👾More Games"), Remarks("3")]
    public class RpgGameModule : BaseGameModule<RpgGame>
    {
        private static readonly IEnumerable<MethodInfo> RpgMethods = typeof(MoreGamesModule).GetMethods()
            .Where(x => x.Get<RpgCommandAttribute>()?.VerifyMethod(x) != null)
            .ToArray();

        [AttributeUsage(AttributeTargets.Method)]
        private class NotRequiresRpgAttribute : Attribute { }

        [AttributeUsage(AttributeTargets.Method)]
        private class RpgCommandAttribute : Attribute
        {
            public string[] Names { get; }
            public RpgCommandAttribute(params string[] names)
            {
                Names = names;
            }

            // Runtime check that all commands are valid
            public object VerifyMethod(MethodInfo method)
            {
                if (method.ReturnType != typeof(Task<string>) || method.GetParameters().Length != 0)
                {
                    throw new InvalidOperationException($"{method.Name} does not match the expected {GetType().Name} signature.");
                }
                return this;
            }
        }


        public string Args { get; set; }


        [Command("rpg"), Remarks("Play an RPG game"), Parameters("[command]"), Priority(4)]
        [Summary("Play ReactionRPG, a new game where you beat monsters and level up." +
            "\nThe game is yours. You can play in **any channel** anywhere you go, even DMs with the bot." +
            "\n\n**__Commands:__**" +
            "\n**{prefix}rpg manual** - See detailed instructions for the game." +
            "\n\n**{prefix}rpg** - Start a new battle or resend the current battle." +
            "\n**{prefix}rpg pvp <player>** - Challenge a user to a battle, or accept a user's challenge." +
            "\n**{prefix}rpg equip <item>** - Equip an item in your inventory." +
            "\n**{prefix}rpg heal** - Refill your HP, only once per battle." +
            "\n**{prefix}rpg cancel** - Cancel a battle, dying instantly against monsters." +
            "\n\n**{prefix}rpg profile** - Check a summary of your hero (or another person's)." +
            "\n**{prefix}rpg skills** - Check your hero's skills lines and active skills." +
            "\n**{prefix}rpg spend <skill> <amount>** - Spend skill points on a skill line." +
            "\n**{prefix}rpg name <name>** - Change your hero's name." +
            "\n**{prefix}rpg color <color>** - Change the color of your hero's profile." +
            "\n**{prefix}rpg delete** - Delete your hero.")]
        public async Task RpgMaster(string commandName = "", [Remainder]string args = "")
        {
            commandName = commandName.ToLowerInvariant();

            var command = RpgMethods
                .FirstOrDefault(x => x.Get<RpgCommandAttribute>().Names.Contains(commandName));

            if (command == null)
            {
                var skill = Game == null ? null
                    : RpgExtensions.SkillTypes.Values.FirstOrDefault(x => x.Shortcut == commandName);

                if (skill == null)
                {
                    await ReplyAsync($"Unknown RPG command! Do `{Context.Prefix}rpg manual` for game instructions," +
                        $" or `{Context.Prefix}rpg help` for a list of commands.");
                    return;
                }

                string response = await RpgUseActiveSkill(skill);
                if (response != null) await ReplyAsync(response);
            }
            else
            {
                if (Game != null) Game.LastPlayed = DateTime.Now;

                if (Game == null && command.Get<NotRequiresRpgAttribute>() == null)
                {
                    await ReplyAsync($"You can use `{Context.Prefix}rpg start` to start your adventure.");
                    return;
                }

                Args = args.Trim();
                string response = await command.Invoke<Task<string>>(this);
                if (response != null) await ReplyAsync(response);
            }
        }




        [RpgCommand("", "battle", "fight", "b", "rpg")]
        public async Task<string> RpgStartBattle()
        {
            if (Game.State != State.Active)
            {
                var timeLeft = TimeSpan.FromSeconds(30) - (DateTime.Now - Game.lastBattle);
                if (timeLeft > TimeSpan.Zero)
                {
                    return $"{CustomEmoji.Cross} You may battle again in {timeLeft.Humanized(empty: "1 second")}";
                }

                Game.StartFight();
                Game.fightEmbed = Game.Fight();
            }

            Game.CancelRequests();

            var old = await Game.GetMessage();
            if (old != null)
            {
                try { await old.DeleteAsync(); }
                catch (HttpException) { }
            }

            Game.fightEmbed = Game.fightEmbed ?? (Game.IsPvp ? Game.FightPvP() : Game.Fight());
            var message = await ReplyAsync(Game.fightEmbed);

            Game.ChannelId = Context.Channel.Id;
            Game.MessageId = message.Id;
            if (Game.IsPvp && Game.PvpBattleConfirmed)
            {
                Game.PvpGame.ChannelId = Context.Channel.Id;
                Game.PvpGame.MessageId = message.Id;
            }

            Games.Save(Game);

            await RpgAddEmotes(message, Game);
            return null;
        }


        public async Task<string> RpgUseActiveSkill(Skill skill)
        {
            if (Game.State != State.Active)
                return "You can only use an active skill during battle!";
            if (Game.IsPvp && !Game.isPvpTurn)
                return "It's not your turn.";

            var unlocked = Game.player.UnlockedSkills;

            if (!Game.player.UnlockedSkills.Contains(skill))
                return $"You haven't unlocked the `{skill.Shortcut}` active skill.";
            if (Game.player.Mana == 0)
                return $"You don't have any {CustomEmoji.Mana}left! You should heal.";
            if (skill.ManaCost > Game.player.Mana)
                return $"{skill.Name} requires {skill.ManaCost}{CustomEmoji.Mana}" +
                       $"but you only have {Game.player.Mana}{CustomEmoji.Mana}";

            Game.player.UpdateStats();
            foreach (var op in Game.Opponents) op.UpdateStats();

            var gameMsg = await Game.GetMessage();
            Game.player.Mana -= skill.ManaCost;
            if (Game.IsPvp)
            {
                Game.fightEmbed = Game.FightPvP(true, skill);
                if (Game.IsPvp) // Match didn't end
                {
                    Game.isPvpTurn = false;
                    Game.PvpGame.isPvpTurn = true;
                    Game.PvpGame.fightEmbed = Game.fightEmbed;
                }
            }
            else
            {
                Game.fightEmbed = Game.Fight(-1, skill);
            }

            if (Game.State == State.Active && (gameMsg == null || gameMsg.Channel.Id != Context.Channel.Id))
            {
                gameMsg = await ReplyAsync(Game.fightEmbed);
                Game.ChannelId = Context.Channel.Id;
                Game.MessageId = gameMsg.Id;
                Games.Save(Game);

                await RpgAddEmotes(gameMsg, Game);
            }
            else
            {
                Games.Save(Game);
                Game.CancelRequests();
                try { await gameMsg.ModifyAsync(Game.GetMessageUpdate(), Game.GetRequestOptions()); }
                catch (OperationCanceledException) { }
            }

            if (Context.BotCan(ChannelPermission.ManageMessages)) await Context.Message.DeleteAsync(DefaultOptions);

            return null;
        }




        [RpgCommand("profile", "p", "stats", "inventory", "inv"), NotRequiresRpg]
        public async Task<string> RpgProfile()
        {
            var rpg = Game;
            var otherUser = await Context.ParseUserAsync(Args);
            if (otherUser != null) rpg = Games.GetForUser<RpgGame>(otherUser.Id);

            if (rpg == null)
            {
                if (otherUser == null) await ReplyAsync($"You can use `{Context.Prefix}rpg start` to start your adventure."); 
                else await ReplyAsync("This person hasn't started their adventure.");
                return null;
            }

            await ReplyAsync(rpg.player.Profile(Context.Prefix, own: otherUser == null));
            return null;
        }


        [RpgCommand("skills", "skill", "s", "spells")]
        public async Task<string> RpgSkills()
        {
            await ReplyAsync(Game.player.Skills(Context.Prefix));
            return null;
        }



        [RpgCommand("heal", "h", "potion")]
        public async Task<string> RpgHeal()
        {
            if (Game.lastHeal > Game.lastBattle && Game.State == State.Active)
                return $"{CustomEmoji.Cross} You already healed during this battle.";
            else if (Game.IsPvp && Game.PvpBattleConfirmed)
                return $"{CustomEmoji.Cross} You can't heal in a PVP battle.";

            var timeLeft = TimeSpan.FromMinutes(5) - (DateTime.Now - Game.lastHeal);

            if (timeLeft > TimeSpan.Zero)
                return $"{CustomEmoji.Cross} You may heal again in {timeLeft.Humanized(empty: "1 second")}";

            Game.lastHeal = DateTime.Now;
            Game.player.Life = Game.player.MaxLife;
            Game.player.Mana = Game.player.MaxMana;
            Games.Save(Game);

            await ReplyAsync($"💟 Fully restored!");

            if (Game.State == State.Active)
            {
                var message = await Game.GetMessage();
                if (message != null)
                {
                    Game.lastEmote = "";
                    Game.fightEmbed = Game.IsPvp ? Game.FightPvP() : Game.Fight();
                    Game.CancelRequests();
                    try { await message.ModifyAsync(m => m.Embed = Game.fightEmbed.Build(), Game.GetRequestOptions()); }
                    catch (OperationCanceledException) { }
                }

                if (Context.BotCan(ChannelPermission.ManageMessages))
                {
                    await Context.Message.DeleteAsync(DefaultOptions);
                }
            }

            return null;
        }


        [RpgCommand("equip", "e", "weapon", "armor")]
        public async Task<string> RpgEquip()
        {
            if (Args == "") return "You must specify an item from your inventory.";

            Equipment bestMatch = null;
            double bestPercent = 0;
            foreach (var item in Game.player.inventory.Select(x => x.GetEquip()))
            {
                double sim = Args.Similarity(item.Name, false);
                if (sim > bestPercent)
                {
                    bestMatch = item;
                    bestPercent = sim;
                }
                if (sim == 1) break;
            }

            if (bestPercent < 0.69)
                return $"Can't find a weapon with that name in your inventory." +
                       $" Did you mean `{bestMatch}`?".If(bestPercent > 0.39);
            if (bestMatch is Armor && Game.State == State.Active)
                return "You can't switch armors mid-battle (but you can switch weapons).";

            Game.player.EquipItem(bestMatch.Key);
            Games.Save(Game);
            await ReplyAsync($"⚔ Equipped `{bestMatch}`.");

            if (Game.State == State.Active && !Game.IsPvp)
            {
                var message = await Game.GetMessage();
                if (message != null)
                {
                    Game.lastEmote = RpgGame.ProfileEmote;
                    var embed = Game.player.Profile(Context.Prefix, reaction: true).Build();
                    Game.CancelRequests();
                    try { await message.ModifyAsync(m => m.Embed = embed, Game.GetRequestOptions()); }
                    catch (OperationCanceledException) { }
                }

                if (Context.BotCan(ChannelPermission.ManageMessages))
                {
                    await Context.Message.DeleteAsync(DefaultOptions);
                }
            }

            return null;
        }


        [RpgCommand("spend", "invest")]
        public async Task<string> RpgSpendSkills()
        {
            if (Args == "") return "Please specify a skill and amount to spend.";

            Args = Args.ToLowerInvariant();
            string[] splice = Args.Split(' ', 2);
            string skill = splice[0];
            int amount = 0;
            if (splice.Length == 2)
            {
                if (splice[1] == "all") amount = Game.player.skillPoints;
                else int.TryParse(splice[1], out amount); // Default value 0
            }

            if (amount < 1) return "Please specify a valid amount of skill points to spend.";
            if (amount > Game.player.skillPoints) return "You don't have that many skill points!";

            SkillType type;

            switch (skill)
            {
                case "p": case "power":
                case "pow": case "damage": case "dmg":
                    type = SkillType.Dmg;
                    break;

                case "g": case "grit": case "defense": case "def":
                    type = SkillType.Def;
                    break;

                case "f": case "focus": case "luck": case "critchance": case "crit":
                    type = SkillType.Crit;
                    break;

                default:
                    return "That's not a valid skill name! You can choose power, grit or focus.";
            }

            if (Game.player.spentSkill[type] + amount > RpgPlayer.SkillMax)
                return $"A skill line can only have {RpgPlayer.SkillMax} skill points invested.";

            int oldValue = Game.player.spentSkill[type];
            Game.player.spentSkill[type] += amount;
            Game.player.skillPoints -= amount;
            Games.Save(Game);
            await AutoReactAsync();

            var newSkills = RpgExtensions.SkillTypes.Values
                .Where(x => x.Type == type && x.SkillGet > oldValue && x.SkillGet <= Game.player.spentSkill[x.Type]);

            foreach (var sk in newSkills)
            {
                await ReplyAsync("You unlocked a new skill!\n\n" +
                    $"**[{sk.Name}]**" +
                    $"\n*{sk.Description}*" +
                    $"\nMana cost: {sk.ManaCost}{CustomEmoji.Mana}" +
                    $"\nUse with the command: `{Context.Prefix}rpg {sk.Shortcut}`");
            }

            if (Game.State == State.Active && !Game.IsPvp)
            {
                var message = await Game.GetMessage();
                if (message != null)
                {
                    Game.lastEmote = RpgGame.SkillsEmote;
                    var embed = Game.player.Skills(Context.Prefix, true).Build();
                    Game.CancelRequests();
                    try { await message.ModifyAsync(m => m.Embed = embed, Game.GetRequestOptions()); }
                    catch (OperationCanceledException) { }
                }
            }

            return null;
        }


        [RpgCommand("name", "setname")]
        public async Task<string> RpgSetName()
        {
            if (Args == "") return "Please specify a new name.";
            if (Args.Length > 32) return "Your name can't be longer than 32 characters.";
            if (Args.Contains("@")) return $"Your name can't contain \"@\"";

            Game.player.SetName(Args);
            Games.Save(Game);
            await AutoReactAsync();
            return null;
        }


        [RpgCommand("color", "setcolor")]
        public async Task<string> RpgSetColor()
        {
            if (Args == "") return "Please specify a color name.";

            var color = Args.ToColor();

            if (color == null) return "That is neither a valid color name or hex code. Example: `red` or `#FF0000`";

            Game.player.Color = color.Value;
            Games.Save(Game);

            await ReplyAsync(new EmbedBuilder
            {
                Title = "Player color set",
                Description = $"#{color.Value.RawValue:X6}",
                Color = color,
            });
            return null;
        }


        [RpgCommand("cancel", "die", "end", "killme")]
        public async Task<string> RpgCancelBattle()
        {
            if (Game.State != State.Active) return "You're not fighting anything.";

            string reply = "";
            var oldMessage = await Game.GetMessage();

            if (Game.IsPvp)
            {
                reply = "PVP match cancelled.";
                if (Game.PvpBattleConfirmed) Game.PvpGame.ResetBattle(State.Completed);
            }
            else
            {
                reply = Game.player.Die();
            }

            Game.ResetBattle(State.Completed);

            if (oldMessage != null)
            {
                Game.CancelRequests();
                try { await oldMessage.DeleteAsync(); }
                catch (HttpException) { }
            }

            return reply;
        }


        [RpgCommand("pvp", "vs", "challenge")]
        public async Task<string> RpgStartPvpBattle()
        {
            if (Game.State == State.Active) return "You're already busy fighting.";
            if (Args == "") return "You must specify a person to challenge in a PVP battle.";

            RpgGame otherGame = null;
            var otherUser = await Context.ParseUserAsync(Args);

            if (otherUser == null) return "Can't find that user to challenge!";
            if (otherUser.Id == Context.User.Id) return "You can't battle yourself, smart guy.";
            if ((otherGame = Games.GetForUser<RpgGame>(otherUser.Id)) == null) return "This person doesn't have a hero.";

            if (otherGame.pvpUserId == Context.User.Id) // Accept fight
            {
                Game.StartFight(otherUser.Id);
                Game.isPvpTurn = true;
                Game.ChannelId = otherGame.ChannelId;
                Game.MessageId = otherGame.MessageId;
                Games.Save(Game);
                await AutoReactAsync();

                var msg = await otherGame.GetMessage();
                Game.fightEmbed = Game.FightPvP();
                Game.CancelRequests();
                Game.PvpGame.CancelRequests();
                try
                {
                    await msg.ModifyAsync(Game.GetMessageUpdate(), Game.GetRequestOptions());
                    await RpgAddEmotes(msg, Game);
                }
                catch (OperationCanceledException) { }
            }
            else if (otherGame.State == State.Active)
            {
                return "This person is already busy fighting.";
            }
            else // Propose fight
            {
                Game.StartFight(otherUser.Id);
                Game.isPvpTurn = false;
                string content = $"{otherUser.Mention} do **{Context.Prefix}rpg pvp {Context.User.Mention}** " +
                                 $"to accept the challenge. You should heal first.";

                var msg = await ReplyAsync(content, Game.FightPvP());
                Game.MessageId = msg.Id;
                Game.ChannelId = Context.Channel.Id;
                Games.Save(Game);
            }

            return null;
        }


        [RpgCommand("start"), NotRequiresRpg]
        public async Task<string> RpgStart()
        {
            if (Game != null) return "You already have a hero!";

            CreateGame(new RpgGame(Context.User.Username, Context.User.Id, Services));

            await RpgSendManual();
            return null;
        }


        [RpgCommand("delete")]
        public async Task<string> RpgDelete()
        {
            await ReplyAsync(
                $"❗ You're about to completely delete your progress in ReactionRPG.\n" +
                $"Are you sure you want to delete your level {Game.player.Level} hero? (Yes/No)");

            if (await GetYesResponse())
            {
                Games.Remove(Game);
                return "Hero deleted 💀";
            }
            return "Hero not deleted ⚔";
        }


        [RpgCommand("help", "commands"), NotRequiresRpg]
        public async Task<string> RpgSendHelp()
        {
            await ReplyAsync(Commands.GetCommandHelp("rpg"));
            return null;
        }


        [RpgCommand("manual", "instructions"), NotRequiresRpg]
        public async Task<string> RpgSendManual()
        {
            var embed = new EmbedBuilder
            {
                Title = $"ReactionRPG Game Manual",
                Color = Colors.Black,
                Description =
                $"Welcome to ReactionRPG{$", {Game?.player.Name}".If(Game != null)}!" +
                $"\nThis game consists of battling enemies, levelling up and unlocking skills." +
                $"\nYou can play in *any channel*, even in DMs with the bot." +
                $"\nUse the command **{Context.Prefix}rpg help** for a list of commands." +
                $"\nUse **{Context.Prefix}rpg profile** to see your hero's profile, and **{Context.Prefix}rpg name/color** to personalize it.",
            };

            embed.AddField(new EmbedFieldBuilder
            {
                Name = "⚔ Battles",
                Value =
                $"To start a battle or re-send the current battle, use the command **{Context.Prefix}rpg**" +
                $"\nWhen in a battle, you can use the _message reactions_ to perform an action." +
                $"\nSelect a number {RpgGame.EmoteNumberInputs[0]} of an enemy to attack. " +
                $"You can also select {RpgGame.MenuEmote} to inspect your enemies, " +
                $"and {RpgGame.ProfileEmote} to see your own profile and skills. React again to close these pages.",
            });

            embed.AddField(new EmbedFieldBuilder
            {
                Name = "📁 Utilities",
                Value =
                $"You will get hurt in battle, and if you die you will lose EXP. To recover" +
                $" {CustomEmoji.Life}and {CustomEmoji.Mana}, use **{Context.Prefix}rpg heal**" +
                $" - It can only be used once per battle." +
                $"\nYou will unlock equipment as you progress. When you have an item in your inventory," +
                $" you can equip it using **{Context.Prefix}rpg equip [item]** - You can switch weapons at any time," +
                $" but you can't switch armors mid-battle.",
            });

            embed.AddField(new EmbedFieldBuilder
            {
                Name = "⭐ Skills",
                Value =
                $"When you level up you gain __skill points__, which you can spend." +
                $"\nThere are three skill lines: __Power__ (attack), __Grit__ (defense) and __Focus__ (crit chance). " +
                $"\nYou can view your skills page using **{Context.Prefix}rpg skills** - " +
                $"To spend points in a skill line use **{Context.Prefix}rpg spend [skill] [amount]**\n" +
                $"You can unlock __active skills__, which can be used during battle and cost {CustomEmoji.Mana}. " +
                $"To use an active skill you unlocked, use that skill's command which can be found in the skills page.",
            });

            await ReplyAsync(embed);
            return null;
        }



        private static async Task RpgAddEmotes(IUserMessage message, RpgGame game)
        {
            if (game.IsPvp)
            {
                try { await message.AddReactionAsync(RpgGame.PvpEmote.ToEmoji(), DefaultOptions); }
                catch (HttpException) { }
                return;
            }

            var emotes = game.IsPvp
                ? new[] { RpgGame.PvpEmote}
                : RpgGame.EmoteNumberInputs.Take(game.enemies.Count()).Concat(RpgGame.EmoteOtherInputs);

            try
            {
                foreach (var emote in emotes)
                {
                    await message.AddReactionAsync((IEmote)emote.ToEmote() ?? emote.ToEmoji(), DefaultOptions);
                }
            }
            catch (HttpException) { }
        }
    }
}
